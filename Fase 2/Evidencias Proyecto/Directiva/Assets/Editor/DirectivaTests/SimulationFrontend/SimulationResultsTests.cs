using System;
using System.Collections.Generic;
using DMath;
using DSEvents;
using DOrchestration;
using DSimulation;
using Directiva.CodeUI;
using Directiva.EditorTests.SimulationTests;
using Directiva.MainMenu;
using Directiva.Progress;
using Directiva.SimulationFrontend;
using Directiva.SimulationUI;
using Directiva.Settings;
using UnityEditor;
using UnityEngine.Audio;
using Directiva.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.EditorTests.SimulationFrontendTests
{
    internal static class SimulationResultsTests
    {
        private sealed class MemorySettingsStorage : SettingsStorage
        {
            public string StoredJson;

            protected override bool TryReadPrimary(out string json)
            {
                json = StoredJson;
                return json != null;
            }

            protected override bool TryReadBackup(out string json)
            {
                json = null;
                return false;
            }

            protected override bool TryWrite(string json)
            {
                StoredJson = json;
                return true;
            }
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("Simulation", "Results", name, description, run));

            Add("ResourcesCountExtractionNotUnload",
                "Positive player cargo deltas count gathering once; unload/drop and opponent cargo do not", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var player = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 15, 15, 0);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                t.Equal(0L, stats.ResourcesGathered, "no initial resources are counted");
                stats.Record(new EventCargo(player.id, 5));
                stats.Record(new EventCargo(enemy.id, 9));
                stats.Record(new EventCargo(player.id, 0));
                stats.Record(new EventUnloadCargo(player.id, 99));
                stats.Record(new EventCargo(player.id, 4));
                t.Equal(9L, stats.ResourcesGathered, "only positive player extraction deltas");
            });

            Add("EffectiveDamageAndOverkill",
                "Damage across shields and health includes only actual loss, no overkill or regeneration", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var player = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var enemy = SimulationTestHarness.AddUnit(world, 2, 8, 8, 0);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                Fixed originalShields = enemy.Shields;
                Fixed originalHealth = enemy.Health;
                stats.Record(new EventAttack(player.id, enemy.id));
                stats.Record(new EventShields(enemy.id, originalShields - 4));
                stats.Record(new EventHealth(enemy.id, originalHealth - 3));
                stats.Record(new EventAdvance(0, 1));
                stats.Record(new EventShields(enemy.id, originalShields - 2)); // regeneration
                t.Equal(7L * Constants.FixedScale, stats.EffectiveDamageRaw,
                    "only actual 4 shield + 3 health damage");
                stats.Record(new EventAttack(player.id, enemy.id));
                stats.Record(new EventShields(enemy.id, 0));
                stats.Record(new EventHealth(enemy.id, 0));
                stats.Record(new EventDeath(enemy.id));
                t.Equal(1, stats.EntitiesDestroyed, "lethal attack credited once");
                t.Equal((long)(originalShields.RawValue + originalHealth.RawValue +
                    2 * Constants.FixedScale), stats.EffectiveDamageRaw,
                    "regenerated shields may be damaged again but overkill is not counted");
            });

            Add("ResourceAndNexusKills",
                "Combat destruction includes neutral resources and the enemy Nexus", t =>
            {
                var world = SimulationTestHarness.NewWorld(width: 40, height: 40);
                var player = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var enemyNexus = new SBNexus(world.GetSide(2), new FixedVector2(25, 25));
                var deposit = new SBResource(world.GetSide(0), new FixedVector2(12, 12), 0, 5);
                world.EnqueueCreation(enemyNexus);
                world.EnqueueCreation(deposit);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                stats.Record(new EventAttack(player.id, deposit.id));
                stats.Record(new EventHealth(deposit.id, 0));
                // SBEntity.Die() can publish its last position BEFORE EventDeath.
                stats.Record(new EventPosition(deposit.id, new FixedVector2(13, 12)));
                stats.Record(new EventDeath(deposit.id));
                stats.Record(new EventAdvance(0, 1));
                stats.Record(new EventAttack(player.id, enemyNexus.id));
                stats.Record(new EventShields(enemyNexus.id, 0));
                stats.Record(new EventHealth(enemyNexus.id, 0));
                stats.Record(new EventDeath(enemyNexus.id));
                t.Equal(2, stats.EntitiesDestroyed, "resource and nexus both count");
                t.Equal((long)(deposit.MaxHealth.RawValue + enemyNexus.Shields.RawValue +
                    enemyNexus.Health.RawValue), stats.EffectiveDamageRaw,
                    "shield and health damage of both targets counted");
            });

            Add("ResourceGatheringCreditsDepletion",
                "A deposit emptied by the player's gatherer counts as a destroyed entity", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var player = SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                var resource = new SBResource(world.GetSide(0), new FixedVector2(8, 8), 0, 1);
                world.EnqueueCreation(resource);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                stats.Record(new EventGather(player.id, resource.id));
                stats.Record(new EventResources(resource.id, 0));
                stats.Record(new EventDeath(resource.id));
                stats.Record(new EventCargo(player.id, 1));
                t.Equal(1, stats.EntitiesDestroyed, "empty player-harvested deposit counts");
                t.Equal(1L, stats.ResourcesGathered, "harvest still counts");
            });

            Add("BuiltUnitsExcludeInitialEntities",
                "Only player units created after match start count as built", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                SimulationTestHarness.AddUnit(world, 1, 5, 5, 0);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                t.Equal(0, stats.UnitsBuilt, "starting units are not built this match");
                stats.Record(new EventCreateUnit(100, 1, 10, 10, new FixedVector2(5, 6), 0, 15));
                stats.Record(new EventCreateUnit(101, 2, 10, 10, new FixedVector2(6, 7), 0, 15));
                t.Equal(1, stats.UnitsBuilt, "only new player unit counted");
            });

            Add("StarsAndRates",
                "Result goals are per-match and unit orders divide by simulated seconds", t =>
            {
                var result = new SimulationMatchResult(MatchOutcome.Side1Won,
                    LevelStarFlags.Victory | LevelStarFlags.FastVictory, 90,
                    12, 2, 4, 3, 150000, 30d, 75);
                t.Equal(2.5d, result.UnitOrdersPerSecond, "orders per simulated second");
                t.Equal(15d, result.EffectiveDamage, "Fixed damage to human-readable points");
                t.Equal("1:30", SimulationResultsPanel.FormatTime(90), "star deadline in minutes");
                t.Equal("1:01:01", SimulationResultsPanel.FormatTime(3661), "long matches in hours");
                t.Check((result.EarnedStars & LevelStarFlags.UntouchedNexus) == 0,
                    "previously earned objectives are not injected into this match");
            });

            Add("ResultsPanelAndNavigation",
                "Result screen opens and closes, and return-to-levels request is consumed exactly once", t =>
            {
                MainMenuNavigationRequest.Clear();
                var root = new VisualElement();
                var panel = new SimulationResultsPanel(root, null, null, null);
                var resultsScroll = root.Q<ScrollView>(className: "sim-results-scroll");
                t.Check(resultsScroll != null, "results use a scroll view for short screens");
                t.Equal(ScrollerVisibility.Auto, resultsScroll.verticalScrollerVisibility,
                    "scrollbar is visible only when results overflow");
                t.Equal(ScrollerVisibility.Hidden, resultsScroll.horizontalScrollerVisibility,
                    "results do not acquire horizontal scrolling");
                t.Check(!panel.IsVisible, "initially hidden");
                panel.Show(new SimulationMatchResult(MatchOutcome.Draw,
                    LevelStarFlags.None, 120, 0, 0, 0, 0, 0, 60, 0));
                t.Check(panel.IsVisible, "visible when finished");
                panel.Hide();
                t.Check(!panel.IsVisible, "return closes without destroying scene");
                MainMenuNavigationRequest.RequestLevels();
                t.Check(MainMenuNavigationRequest.ConsumeLevels(), "first main menu loads levels");
                t.Check(!MainMenuNavigationRequest.ConsumeLevels(), "ordinary menu entry opens home");
            });

            Add("ReturnToLevelsPreferencePersists",
                "Confirmation is on by default; explicit opt-out survives JSON save/load", t =>
            {
                GameSettings defaults = GameSettings.CreateDefaults(1280, 720);
                t.Check(!defaults.general.skipReturnToLevelsConfirmation,
                    "new installs ask before returning to levels");
                t.Check(defaults.general.simulateWhenInactive,
                    "new installs keep simulating when the window loses focus");
                t.Check(defaults.graphics.fullscreen,
                    "new installs start in fullscreen");

                var storage = new MemorySettingsStorage
                {
                    // Old JSON without the new preference must inherit the new default.
                    StoredJson = "{\"version\":1,\"general\":{\"language\":\"en\"}}"
                };
                t.Equal(SettingsLoadResult.Loaded,
                    storage.Load(defaults, out GameSettings previous),
                    "legacy settings file is accepted");
                t.Check(!previous.general.skipReturnToLevelsConfirmation,
                    "legacy settings without a preference inherit confirmation enabled");
                t.Check(previous.general.simulateWhenInactive,
                    "missing simulate preference inherits true");
                t.Check(previous.graphics.fullscreen,
                    "missing fullscreen preference inherits true");
                t.Equal("en", previous.general.language,
                    "existing language preference is preserved");

                previous.general.skipReturnToLevelsConfirmation = true;
                previous.general.simulateWhenInactive = false;
                previous.graphics.fullscreen = false;
                t.Check(storage.Save(previous), "explicit preferences are saved");
                t.Equal(SettingsLoadResult.Loaded,
                    storage.Load(defaults, out GameSettings reloaded),
                    "saved preferences reload");
                t.Check(reloaded.general.skipReturnToLevelsConfirmation,
                    "explicit opt-out survives restart");
                t.Check(!reloaded.general.simulateWhenInactive,
                    "explicit inactive simulation preference survives restart");
                t.Check(!reloaded.graphics.fullscreen,
                    "explicit fullscreen preference survives restart");
                t.Check(!defaults.general.skipReturnToLevelsConfirmation,
                    "loading/editing cannot mutate default settings");
                t.Check(reloaded.Copy().general.skipReturnToLevelsConfirmation,
                    "defensive copies include the preference");
            });

            Add("ReturnToLevelsConfirmation",
                "Leaving the match is cancellable, localized and requests the levels screen only after confirmation", t =>
            {
                MainMenuNavigationRequest.Clear();
                var root = new VisualElement();
                int confirmed = 0;
                var prompt = new SimulationLeaveConfirmation(root, () =>
                {
                    confirmed++;
                    MainMenuNavigationRequest.RequestLevels();
                });

                t.Check(!prompt.IsVisible, "confirmation starts hidden");
                prompt.Show();
                t.Check(prompt.IsVisible, "confirmation is displayed when requested");
                var title = root.Q<Label>(className: "sim-leave-title");
                var message = root.Q<Label>(className: "sim-leave-message");
                var confirm = root.Q<Button>(className: "sim-leave-button-primary");
                t.Check(title != null && !string.IsNullOrWhiteSpace(title.text),
                    "localized confirmation title is visible");
                t.Check(message != null && !string.IsNullOrWhiteSpace(message.text),
                    "localized confirmation message is visible");
                t.Check(confirm != null && !string.IsNullOrWhiteSpace(confirm.text),
                    "localized confirmation action is visible");

                prompt.Hide(); // Cancel must not change the scene or arm navigation.
                t.Check(!prompt.IsVisible, "cancelling dismisses confirmation");
                t.Equal(0, confirmed, "cancel does not request navigation");
                t.Check(!MainMenuNavigationRequest.ConsumeLevels(),
                    "cancel leaves main-menu navigation state untouched");

                prompt.Show();
                prompt.Confirm();
                t.Check(!prompt.IsVisible, "confirmation closes before navigation");
                t.Equal(1, confirmed, "confirmation invokes the callback once");
                t.Check(MainMenuNavigationRequest.ConsumeLevels(),
                    "confirmed navigation opens level selection");
                t.Check(!MainMenuNavigationRequest.ConsumeLevels(),
                    "level selection request is consumed once");
                prompt.Confirm();
                t.Equal(1, confirmed, "hidden confirmation cannot trigger again");
            });

            Add("ResultsObjectiveIconsUseIndependentThemeSprites",
                "Results use their own earned/unearned icons, each falling back to level-selection art", t =>
            {
                var texture = new Texture2D(4, 4);
                var sprites = new Sprite[4];
                var theme = ScriptableObject.CreateInstance<DirectivaUITheme>();
                try
                {
                    for (int i = 0; i < sprites.Length; i++)
                        sprites[i] = Sprite.Create(texture, new Rect(0, 0, 4, 4),
                            new Vector2(0.5f, 0.5f));
                    theme.icons.earnedStar = sprites[0];
                    theme.icons.unearnedStar = sprites[1];

                    var root = new VisualElement();
                    var panel = new SimulationResultsPanel(root, theme, null, null);
                    panel.Show(new SimulationMatchResult(MatchOutcome.Side1Won,
                        LevelStarFlags.Victory, 90, 0, 0, 0, 0, 0, 60, 0));
                    var images = root.Query<Image>(className: "sim-results-star-icon").ToList();
                    t.Equal(3, images.Count, "three result objective icons exist");
                    t.Check(images[0].sprite == sprites[0],
                        "earned result objective initially uses earned level icon");
                    t.Check(images[1].sprite == sprites[1],
                        "unearned result objective initially uses unearned level icon");

                    theme.icons.resultObjectiveEarned = sprites[2];
                    panel.Relocalize();
                    t.Check(images[0].sprite == sprites[2],
                        "earned result objective uses its dedicated sprite");
                    t.Check(images[1].sprite == sprites[1],
                        "unearned result objective independently retains its fallback");

                    theme.icons.resultObjectiveUnearned = sprites[3];
                    panel.Relocalize();
                    t.Check(images[1].sprite == sprites[3] && images[2].sprite == sprites[3],
                        "both unearned objectives use the dedicated results sprite");
                    t.Check(theme.icons.earnedStar == sprites[0] &&
                        theme.icons.unearnedStar == sprites[1],
                        "level-selection icons remain untouched");
                }
                finally
                {
                    foreach (Sprite sprite in sprites)
                        if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
                    UnityEngine.Object.DestroyImmediate(theme);
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            });

            Add("BothSidesGatherBuildAndDepleteResources",
                "Resources, new units and depleted deposits are attributed to the correct side", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var blue = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var red = SimulationTestHarness.AddUnit(world, 2, 16, 16, 0);
                var resource = new SBResource(world.GetSide(0), new FixedVector2(9, 9), 0, 1);
                world.EnqueueCreation(resource);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                stats.Record(new EventCargo(blue.id, 3));
                stats.Record(new EventCargo(red.id, 8));
                stats.Record(new EventCargo(red.id, 0));
                stats.Record(new EventCargo(red.id, 2));
                t.Equal(3L, stats.GetResourcesGathered(1), "blue gathered 3");
                t.Equal(10L, stats.GetResourcesGathered(2), "red gathered 10");

                stats.Record(new EventCreateUnit(100, 1, 10, 10,
                    new FixedVector2(5, 6), 0, 15));
                stats.Record(new EventCreateUnit(101, 2, 10, 10,
                    new FixedVector2(6, 7), 0, 15));
                stats.Record(new EventCreateUnit(102, 2, 10, 10,
                    new FixedVector2(7, 8), 0, 15));
                t.Equal(1, stats.GetUnitsBuilt(1), "only one blue unit constructed");
                t.Equal(2, stats.GetUnitsBuilt(2), "two red units constructed");

                stats.Record(new EventGather(red.id, resource.id));
                stats.Record(new EventResources(resource.id, 0));
                stats.Record(new EventDeath(resource.id));
                t.Equal(0, stats.GetEntitiesDestroyed(1), "blue not credited for red depletion");
                t.Equal(1, stats.GetEntitiesDestroyed(2), "red credited for emptied resource");
            });

            Add("BothSidesDamageAndKills",
                "Both sides count actual damage and kills, but not enemy damage or overkill", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var blue = SimulationTestHarness.AddUnit(world, 1, 4, 4, 0);
                var red = SimulationTestHarness.AddUnit(world, 2, 16, 16, 0);
                world.Start();
                var stats = new SimulationMatchStatistics(world);
                stats.Record(new EventAttack(blue.id, red.id));
                stats.Record(new EventShields(red.id, red.Shields - 2));
                stats.Record(new EventHealth(red.id, red.Health - 3));
                stats.Record(new EventAdvance(0, 1));
                t.Equal(5L * Constants.FixedScale, stats.GetEffectiveDamageRaw(1),
                    "blue shield + health damage");
                t.Equal(0L, stats.GetEffectiveDamageRaw(2), "red has dealt no damage yet");

                stats.Record(new EventAttack(red.id, blue.id));
                stats.Record(new EventShields(blue.id, 0));
                stats.Record(new EventHealth(blue.id, 0));
                stats.Record(new EventDeath(blue.id));
                t.Equal(1, stats.GetEntitiesDestroyed(2), "red gets blue lethal kill");
                t.Equal(0, stats.GetEntitiesDestroyed(1), "blue gets no credit for own death");
                t.Equal(blue.Shields.RawValue + blue.Health.RawValue,
                    stats.GetEffectiveDamageRaw(2), "red gets actual damage, not overkill");
                t.Equal(5L * Constants.FixedScale, stats.GetEffectiveDamageRaw(1),
                    "blue's previous damage stays independent");
            });

            Add("BothSidesResultsColumns",
                "The results panel draws two labeled and colored numeric columns", t =>
            {
                var blue = new SimulationSideMatchResult(12, 1, 3, 4,
                    25 * Constants.FixedScale, 45);
                var red = new SimulationSideMatchResult(28, 6, 7, 8,
                    60 * Constants.FixedScale, 15);
                var result = new SimulationMatchResult(MatchOutcome.Side1Won,
                    LevelStarFlags.Victory, 90, blue, red, 30);
                t.Equal(1.5d, result.Blue.OrdersPerSecond(result.ElapsedSeconds),
                    "blue's accepted unit orders / elapsed time");
                t.Equal(0.5d, result.Red.OrdersPerSecond(result.ElapsedSeconds),
                    "red's accepted unit orders / elapsed time");
                t.Equal(60d, result.Red.EffectiveDamage, "red damage is independent");

                var root = new VisualElement();
                var panel = new SimulationResultsPanel(root, null, null, null);
                panel.Show(result);
                var blues = root.Query<Label>(className: "sim-results-value-blue").ToList();
                var reds = root.Query<Label>(className: "sim-results-value-red").ToList();
                t.Equal(7, blues.Count, "blue header + six side values");
                t.Equal(7, reds.Count, "red header + six side values");
                t.Equal("12", blues[1].text, "blue resources in blue column");
                t.Equal("28", reds[1].text, "red resources in red column");
                t.Equal("1", blues[2].text, "blue errors");
                t.Equal("6", reds[2].text, "red errors");
                t.Equal("3", blues[3].text, "blue unit builds");
                t.Equal("7", reds[3].text, "red unit builds");
                t.Equal("4", blues[4].text, "blue entity kills");
                t.Equal("8", reds[4].text, "red entity kills");
                t.Equal("1.50", blues[6].text.Replace(',', '.'), "blue unit orders / second");
                t.Equal("0.50", reds[6].text.Replace(',', '.'), "red unit orders / second");
                var elapsed = root.Query<Label>(className: "sim-results-time-value").ToList();
                t.Equal(1, elapsed.Count, "exactly one elapsed time value");
                t.Equal("0:30", elapsed[0].text, "elapsed time shown once, not per side");
            });

            Add("ResultLevelSubtitleAndTime",
                "Results show the level name and one neutral elapsed-time metric", t =>
            {
                var root = new VisualElement();
                var panel = new SimulationResultsPanel(root, null, null, null);
                var sample = new SimulationMatchResult(MatchOutcome.Draw,
                    LevelStarFlags.None, 100, 0, 0, 0, 0, 0, 125, 0);
                panel.Show(sample, "Zona Central");
                var levels = root.Query<Label>(className: "sim-results-level").ToList();
                t.Equal(1, levels.Count, "one level subtitle");
                t.Equal("Zona Central", levels[0].text, "subtitle uses current level display name");
                var times = root.Query<Label>(className: "sim-results-time-value").ToList();
                t.Equal(1, times.Count, "one elapsed time label");
                t.Equal("2:05", times[0].text, "common elapsed time under comparison");
                t.Equal(7, root.Query<Label>(className: "sim-results-value-blue").ToList().Count,
                    "no blue time column");
                t.Equal(7, root.Query<Label>(className: "sim-results-value-red").ToList().Count,
                    "no red time column");
                panel.Show(sample, "Otra partida");
                t.Equal("Otra partida", levels[0].text, "next match updates level name");
            });

            Add("ResultSoundClipsMatchOutcome",
                "The theme's victory, defeat and draw clips are chosen independently", t =>
            {
                var theme = ScriptableObject.CreateInstance<DirectivaUITheme>();
                var victory = AudioClip.Create("VictoryTest", 128, 1, 22050, false);
                var defeat = AudioClip.Create("DefeatTest", 128, 1, 22050, false);
                var draw = AudioClip.Create("DrawTest", 128, 1, 22050, false);
                try
                {
                    theme.resultVictoryClip = victory;
                    theme.resultDefeatClip = defeat;
                    theme.resultDrawClip = draw;
                    t.Check(SimulationUIController.SelectResultClip(theme, MatchOutcome.Side1Won)
                        == victory, "winning result selects victory clip");
                    t.Check(SimulationUIController.SelectResultClip(theme, MatchOutcome.Side2Won)
                        == defeat, "losing result selects defeat clip");
                    t.Check(SimulationUIController.SelectResultClip(theme, MatchOutcome.Draw)
                        == draw, "draw result selects draw clip");
                    theme.resultDefeatClip = null;
                    t.Check(SimulationUIController.SelectResultClip(theme, MatchOutcome.Side2Won)
                        == null, "unassigned result clip is silent");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(victory);
                    UnityEngine.Object.DestroyImmediate(defeat);
                    UnityEngine.Object.DestroyImmediate(draw);
                    UnityEngine.Object.DestroyImmediate(theme);
                }
            });

            Add("MixerVolumeAndGroups",
                "The mixer contains exposed Master/Simulation/Music/UI controls and valid sound buses", t =>
            {
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(
                    "Assets/Resources/Audio/DirectivaMixer.mixer");
                t.Check(mixer != null, "DirectivaMixer asset imported successfully");
                if (mixer == null) return;
                foreach (string group in new[] { "Master", "Simulation", "Music", "UI" })
                {
                    var candidates = mixer.FindMatchingGroups(group);
                    t.Check(Array.Exists(candidates, candidate => candidate.name == group),
                        group + " mixer group exists");
                    t.Check(mixer.GetFloat(group + "Volume", out float volumeDb),
                        group + " volume is exposed to script");
                }
                t.Equal(-80f, DirectivaAudio.ToDecibels(0), "0% is silent");
                t.Equal(0f, DirectivaAudio.ToDecibels(100), "100% is unity gain");
                t.Check(Math.Abs(DirectivaAudio.ToDecibels(50) + 6.0206f) < 0.01f,
                    "50% maps to approximately -6 dB");
            });

            Add("SimulationPrefabsHaveAudioRouters",
                "All five existing 3D simulation SFX prefabs use SoundPitchRandomizer", t =>
            {
                foreach (string prefab in new[] { "Attack", "Gather", "UnitDeath",
                    "NexusDeath", "ResourceDeath" })
                {
                    string assetPath = "Assets/Prefabs/Simulation/" + prefab + ".prefab";
                    var template = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    t.Check(template != null, prefab + " prefab loads");
                    if (template == null) continue;
                    t.Check(template.GetComponentInChildren<SoundPitchRandomizer>(true) != null,
                        prefab + " includes runtime mixer-routing component");
                    t.Check(template.GetComponentInChildren<AudioSource>(true) != null,
                        prefab + " includes an AudioSource");
                }
            });

            Add("LocalizedResultText",
                "Spanish and English result messages include all metrics and star conditions", t =>
            {
                var es = new LocalizationService("es");
                var en = new LocalizationService("en");
                foreach (var key in new[] { "results_victory", "results_defeat", "results_draw",
                    "results_resources", "results_errors", "results_built", "results_destroyed",
                    "results_damage", "results_time", "results_orders_per_second",
                    "results_star_victory", "results_star_nexus", "results_star_fast",
                    "results_return", "results_levels", "results_side_blue", "results_side_red" })
                {
                    t.Check(!es.Get("Simulation", key).Contains("TODO"), "Spanish: " + key);
                    t.Check(!en.Get("Simulation", key).Contains("TODO"), "English: " + key);
                }
                t.Check(es.Get("Simulation", "results_star_fast", "2:30").Contains("2:30"),
                    "Spanish fast objective uses specific level duration");
                t.Check(en.Get("Simulation", "results_star_fast", "2:30").Contains("2:30"),
                    "English fast objective uses specific level duration");
            });
        }
    }
}
