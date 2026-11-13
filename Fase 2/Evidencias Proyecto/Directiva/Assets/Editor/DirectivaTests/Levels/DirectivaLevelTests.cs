using System;
using System.Collections.Generic;
using Directiva.Levels;
using DMath;
using DSimulation;
using Directiva.MainMenu;
using Directiva.Progress;
using Directiva.SimulationFrontend;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Directiva.EditorTests.LevelTests
{
    /// <summary>
    /// Verifies every DirectivaLevel asset's configuration and enemy program without
    /// requiring specific level numbers, star thresholds, scripts, or durations.
    /// </summary>
    internal static class DirectivaLevelTests
    {
        private sealed class LevelAsset
        {
            internal readonly string Path;
            internal readonly DirectivaLevel Level;

            internal LevelAsset(string path, DirectivaLevel level)
            {
                Path = path;
                Level = level;
            }
        }

        // Discover assets on test execution, not registration: adding a level does
        // not require the editor's test catalog to be rebuilt to include it.
        private static List<LevelAsset> FindLevels(DirectivaTestContext t)
        {
            string[] guids = AssetDatabase.FindAssets("t:DirectivaLevel", new[] { "Assets" });
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);

            t.Check(paths.Count > 0, "at least one DirectivaLevel asset exists");
            var levels = new List<LevelAsset>(paths.Count);
            foreach (string path in paths)
            {
                DirectivaLevel level = AssetDatabase.LoadAssetAtPath<DirectivaLevel>(path);
                t.Check(level != null, path + ": DirectivaLevel asset can be loaded");
                levels.Add(new LevelAsset(path, level));
            }
            t.Note("Found " + levels.Count + " DirectivaLevel assets.");
            return levels;
        }

        // A locked level can only rely on stars earned in earlier levels.
        // Count each level number once, matching how saved progress is identified.
        private static int MaximumStarsFromEarlierLevels(int targetNumber,
            IEnumerable<int> configuredLevelNumbers)
        {
            var earlierNumbers = new HashSet<int>();
            foreach (int number in configuredLevelNumbers)
            {
                if (number > 0 && number < targetNumber)
                    earlierNumbers.Add(number);
            }
            return earlierNumbers.Count * LevelStarRules.Count(LevelStarFlags.All);
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "Levels", name, description, action));

            Add("AllLevelAssetsConfigured", "Every level has valid progress IDs, objectives and enemy references", t =>
            {
                var usedNumbers = new Dictionary<int, string>();
                foreach (LevelAsset asset in FindLevels(t))
                {
                    DirectivaLevel level = asset.Level;
                    string path = asset.Path;
                    var serialized = new SerializedObject(level);
                    int number = serialized.FindProperty("levelNumber").intValue;
                    int requiredStars = serialized.FindProperty("requiredStars").intValue;
                    int maxDuration = serialized.FindProperty("maxMatchDurationSeconds").intValue;
                    int fastVictory = serialized.FindProperty("fastVictorySeconds").intValue;

                    t.Check(number > 0, path + ": level number must be positive for saving stars");
                    t.Check(!usedNumbers.TryGetValue(number, out string previous),
                        path + ": level number " + number + " is also used by " + previous +
                        " (save progress is identified by level number)");
                    usedNumbers.Add(number, path);
                    t.Check(requiredStars >= 0, path + ": required stars cannot be negative");
                    t.Check(maxDuration > 0, path + ": match duration must be positive");
                    t.Check(fastVictory > 0,
                        path + ": fast-victory target must be positive");
                    // The Inspector warns about this too. It is a design choice,
                    // not a broken asset: all victories would earn the speed star.
                    if (fastVictory >= maxDuration)
                        t.Note(path + ": fast-victory threshold is not below the match limit " +
                            "(speed star may be automatic).");
                    t.Check(!string.IsNullOrWhiteSpace(level.CodeFolder),
                        path + ": enemy code folder is assigned");
                    t.Check(!string.IsNullOrWhiteSpace(level.EntryModule),
                        path + ": enemy entry module is assigned");
                    t.Check(!string.IsNullOrWhiteSpace(level.EntryFunction),
                        path + ": enemy entry function is assigned");
                    t.Note(path + ": basic configuration OK");
                }
            });

            Add("AllLevelWorldLayouts", "Every level builds a valid world from its own stored layout", t =>
            {
                foreach (LevelAsset asset in FindLevels(t))
                {
                    DirectivaLevel level = asset.Level;
                    LevelWorldLayout layout = level.World;
                    t.Check(layout != null, asset.Path + ": layout exists");
                    t.Check(level.Settings != null, asset.Path + ": simulation parameters exist");
                    try
                    {
                        DSimulation.Simulation sim = LevelWorldBuilder.Build(level);
                        sim.Start();
                        t.Equal(layout.InitialResourcesSide1, sim.GetSide(1).Resources,
                            asset.Path + ": side 1 resources loaded from asset");
                        t.Equal(layout.InitialResourcesSide2, sim.GetSide(2).Resources,
                            asset.Path + ": side 2 resources loaded from asset");
                        t.Equal(layout.Deposits.Count, sim.GetSide(0).ResourceEntities.Count,
                            asset.Path + ": no implicit mirrored deposits added");
                        t.Check(sim.GetSide(1).Nexus != null && sim.GetSide(2).Nexus != null,
                            asset.Path + ": both nexus positions loaded");
                        t.Note(asset.Path + ": world constructed from its layout");
                    }
                    catch (Exception ex)
                    {
                        t.Check(false, asset.Path + ": invalid layout/parameters: " + ex.Message);
                    }
                }
            });

            Add("IndependentLevelSimulationParameters",
                "Level parameters change a new world's entities without changing unrelated worlds", t =>
            {
                var level = ScriptableObject.CreateInstance<DirectivaLevel>();
                try
                {
                    var original = LevelWorldBuilder.Build(level);
                    original.Start();
                    level.Settings.nexusHealth = 220;
                    level.Settings.unitHealth = 27;
                    level.Settings.unitCost = 17;
                    level.Settings.resourceHealth = 135;
                    level.Settings.deltaTime = 0.04f;
                    level.World.Deposits[0].Amount = 218;
                    level.World.Deposits[0].Richness = 3;
                    var modified = LevelWorldBuilder.Build(level);
                    modified.Start();
                    t.Equal((Fixed)100, original.GetSide(1).Nexus.MaxHealth,
                        "first simulation uses original nexus health");
                    t.Equal((Fixed)220, modified.GetSide(1).Nexus.MaxHealth,
                        "new simulation uses configured nexus health");
                    t.Equal((Fixed)10, original.GetSide(1).Nexus.simulation.Parameters.UnitHealth,
                        "old simulation unit parameters remain independent");
                    t.Equal((Fixed)27, modified.Parameters.UnitHealth,
                        "new simulation unit parameters changed");
                    t.Equal(10, original.Parameters.UnitCost, "old world keeps building cost");
                    t.Equal(17, modified.Parameters.UnitCost, "new world uses changed building cost");
                    t.Equal((Fixed)100, original.GetSide(0).ResourceEntities[0].MaxHealth,
                        "default deposit health");
                    t.Equal((Fixed)135, modified.GetSide(0).ResourceEntities[0].MaxHealth,
                        "custom deposit health");
                    t.Equal(218, modified.GetSide(0).ResourceEntities[0].Amount,
                        "custom deposit amount");
                    t.Equal(3, modified.GetSide(0).ResourceEntities[0].GatherAmount,
                        "custom deposit richness");
                    modified.Update();
                    t.Equal(Fixed.Decimal(4, 2), modified.Time,
                        "simulation time uses configured tick duration");
                }
                finally { UnityEngine.Object.DestroyImmediate(level); }
            });

            Add("LevelMirrorGeometry", "Axis and point reflections yield expected coordinates", t =>
            {
                var center = new Vector2(25f, 25f);
                var source = new Vector2(9f, 20f);
                Vector2 point = LevelMirrorMath.Reflect(source, LevelMirrorMode.Point, center, 90f);
                Vector2 vertical = LevelMirrorMath.Reflect(source, LevelMirrorMode.Axis, center, 90f);
                t.Check(Vector2.Distance(point, new Vector2(41f, 30f)) < 0.0001f,
                    "point reflection mirrors both axes");
                t.Check(Vector2.Distance(vertical, new Vector2(41f, 20f)) < 0.0001f,
                    "vertical-axis reflection preserves Y");
                t.Check(Vector2.Distance(LevelMirrorMath.Reflect(point, LevelMirrorMode.Point,
                    center, 90f), source) < 0.0001f,
                    "reflection is its own inverse");
            });

            Add("AllLevelsUnlockable", "Every level can be unlocked using stars from lower-numbered levels", t =>
            {
                List<LevelAsset> assets = FindLevels(t);
                var configuredNumbers = new List<int>(assets.Count);
                foreach (LevelAsset asset in assets)
                    configuredNumbers.Add(asset.Level.LevelNumber);

                foreach (LevelAsset asset in assets)
                {
                    DirectivaLevel level = asset.Level;
                    int maximum = MaximumStarsFromEarlierLevels(level.LevelNumber, configuredNumbers);
                    t.Check(level.RequiredStars <= maximum,
                        asset.Path + ": level " + level.LevelNumber + " requires " +
                        level.RequiredStars + " stars, but lower-numbered levels can provide " +
                        "at most " + maximum + " stars. This level cannot be unlocked.");
                    t.Note(asset.Path + ": unlock requirement " + level.RequiredStars +
                        " is reachable with up to " + maximum + " earlier stars.");
                }
            });

            Add("LevelUnlockBudget", "Unlock math only counts distinct, lower-numbered levels", t =>
            {
                int[] levelNumbers = { 4, 1, 2, 1, 9, 3 };
                t.Equal(0, MaximumStarsFromEarlierLevels(1, levelNumbers),
                    "the first level cannot depend on other levels' stars");
                t.Equal(3, MaximumStarsFromEarlierLevels(2, levelNumbers),
                    "level 2 has at most three stars from level 1 (no duplicate counting)");
                t.Equal(6, MaximumStarsFromEarlierLevels(3, levelNumbers),
                    "level 3 can earn up to six stars from levels 1 and 2");
                t.Equal(9, MaximumStarsFromEarlierLevels(4, levelNumbers),
                    "later and equal-numbered levels do not count toward unlocking");
                t.Check(9 > MaximumStarsFromEarlierLevels(2, levelNumbers),
                    "a level 2 requiring nine stars would be impossible to unlock");
                t.Check(3 <= MaximumStarsFromEarlierLevels(2, levelNumbers),
                    "requiring all three stars from the preceding level is possible");
            });

            Add("AllLevelScenesInBuild", "Every level references an existing, enabled Unity scene", t =>
            {
                foreach (LevelAsset asset in FindLevels(t))
                {
                    string scenePath = asset.Level.ScenePath;
                    string prefix = asset.Path + ": ";
                    t.Check(!string.IsNullOrWhiteSpace(scenePath) &&
                        scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase),
                        prefix + "scene path must point to a .unity asset");
                    t.Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null,
                        prefix + "selected scene asset exists: " + scenePath);
                    int sceneIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
                    t.Check(sceneIndex >= 0,
                        prefix + "scene is enabled in the active Build Profile: " + scenePath);
                    t.Equal(scenePath, SceneUtility.GetScenePathByBuildIndex(sceneIndex),
                        prefix + "scene build index resolves to the selected scene");
                    t.Equal(scenePath.Substring(0, scenePath.Length - ".unity".Length),
                        asset.Level.SceneLoadPath,
                        prefix + "Unity scene load path omits the .unity extension");
                    t.Note(prefix + "scene available");
                }
            });

            Add("AllLevelEnemyScriptsImported", "Every level's selected .dscript is available as a TextAsset", t =>
            {
                foreach (LevelAsset asset in FindLevels(t))
                {
                    DirectivaLevel level = asset.Level;
                    string prefix = asset.Path + ": ";
                    t.Check(!string.IsNullOrWhiteSpace(level.CodeFolder) &&
                        !string.IsNullOrWhiteSpace(level.EntryModule),
                        prefix + "enemy folder and entry module are configured");

                    ResourcesLevelDScriptSourceProvider provider = null;
                    try { provider = new ResourcesLevelDScriptSourceProvider(level.CodeFolder); }
                    catch (ArgumentException) { /* Report bad asset configuration as a failed assertion. */ }
                    t.Check(provider != null, prefix + "enemy folder must be a safe relative path");
                    string rootPath = "Assets/Resources/Levels/" + level.CodeFolder;
                    t.Check(AssetDatabase.IsValidFolder(rootPath),
                        prefix + "enemy folder exists: " + rootPath);

                    string resourcePath = "Levels/" + level.CodeFolder + "/" +
                        level.EntryModule.Replace('.', '/');
                    TextAsset imported = Resources.Load<TextAsset>(resourcePath);
                    t.Check(imported != null,
                        prefix + "selected .dscript is imported as a TextAsset: " + resourcePath);
                    t.Check(!string.IsNullOrWhiteSpace(imported.text),
                        prefix + "selected enemy script is not empty");
                    t.Check(provider.TryReadModule(level.EntryModule, out string source),
                        prefix + "enemy entry module can be read through its level-isolated provider");
                    t.Equal(imported.text, source, prefix + "provider reads the selected script");
                    t.Note(prefix + "enemy script imported");
                }
            });

            Add("AllLevelEnemiesCompile", "Every configured enemy program compiles and links its entry function", t =>
            {
                foreach (LevelAsset asset in FindLevels(t))
                {
                    try
                    {
                        var compiled = SimulationSceneSynchronizer.CompileLevelEnemy(asset.Level);
                        t.Check(compiled.TryGetFunctionId(asset.Level.QualifiedEnemyEntry, out _),
                            asset.Path + ": linked enemy entry " + asset.Level.QualifiedEnemyEntry);
                    }
                    catch (DirectivaTestAssertionException) { throw; }
                    catch (Exception error)
                    {
                        t.Check(false, asset.Path + ": enemy program failed to compile: " + error.Message);
                    }
                    t.Note(asset.Path + ": enemy program compiles");
                }
            });

            Add("RootedModuleProvider", "Every level's module provider rejects access outside its enemy directory", t =>
            {
                foreach (LevelAsset asset in FindLevels(t))
                {
                    DirectivaLevel level = asset.Level;
                    string prefix = asset.Path + ": ";
                    var provider = new ResourcesLevelDScriptSourceProvider(level.CodeFolder);
                    t.Check(provider.TryReadModule(level.EntryModule, out _),
                        prefix + "configured enemy module is available within its own folder");
                    t.Check(!provider.TryReadModule("../player", out _),
                        prefix + "imports cannot traverse into player code");
                    t.Check(!provider.TryReadModule("../another-level.enemy", out _),
                        prefix + "imports cannot traverse to another level");
                    bool rejected = false;
                    try { new ResourcesLevelDScriptSourceProvider("../" + level.CodeFolder); }
                    catch (ArgumentException) { rejected = true; }
                    t.Check(rejected, prefix + "enemy folder traversal is rejected");
                    rejected = false;
                    try { new ResourcesLevelDScriptSourceProvider("/" + level.CodeFolder); }
                    catch (ArgumentException) { rejected = true; }
                    t.Check(rejected, prefix + "absolute enemy folder paths are rejected");
                }
            });

            Add("LevelSortPriority", "Number, required stars, name, then first list occurrence", t =>
            {
                var owned = new List<DirectivaLevel>();
                DirectivaLevel Make(int number, int stars, string title)
                {
                    var level = ScriptableObject.CreateInstance<DirectivaLevel>();
                    owned.Add(level);
                    var so = new SerializedObject(level);
                    so.FindProperty("levelNumber").intValue = number;
                    so.FindProperty("requiredStars").intValue = stars;
                    so.FindProperty("levelName").stringValue = title;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return level;
                }
                try
                {
                    DirectivaLevel betaTwo = Make(2, 5, "Beta");
                    DirectivaLevel zetaOne = Make(1, 3, "Zeta");
                    DirectivaLevel alphaOne = Make(1, 3, "Alpha");
                    DirectivaLevel alphaTwo = Make(1, 3, "Alpha");
                    DirectivaLevel betaOne = Make(1, 5, "Beta");
                    DirectivaLevel first = Make(0, 20, "Ultimo");
                    List<DirectivaLevel> ordered = DirectivaLevelOrder.Sort(
                        new[] { betaTwo, zetaOne, alphaOne, alphaTwo, betaOne, first });
                    t.Equal(6, ordered.Count, "no levels lost");
                    t.Check(ordered[0] == first, "first key is level number");
                    t.Check(ordered[1] == alphaOne && ordered[2] == alphaTwo,
                        "alphabetical ties preserve original order");
                    t.Check(ordered[3] == zetaOne && ordered[4] == betaOne,
                        "name comes after stars");
                    t.Check(ordered[5] == betaTwo, "number outranks every other field");
                }
                finally
                {
                    foreach (DirectivaLevel level in owned) UnityEngine.Object.DestroyImmediate(level);
                }
            });

            Add("SelectedLevelSurvivesLaunch", "Every level can be selected, retained and cleared across a launch", t =>
            {
                foreach (LevelAsset asset in FindLevels(t))
                {
                    DirectivaLevel level = asset.Level;
                    try
                    {
                        LevelLaunchContext.SetPendingLevel(level);
                        t.Check(LevelLaunchContext.CurrentLevel == level,
                            asset.Path + ": selected asset remains available across scene load");
                        t.Equal(level.LevelName, LevelLaunchContext.CurrentLevelName,
                            asset.Path + ": menu display name preserved");
                        t.Check(!LevelLaunchContext.TryConsume(out _),
                            asset.Path + ": legacy bootstrapper does not consume an asset launch");
                        t.Check(LevelLaunchContext.CurrentLevel == level,
                            asset.Path + ": selected asset persists after legacy consume attempt");
                    }
                    finally { LevelLaunchContext.Clear(); }
                    t.Check(LevelLaunchContext.CurrentLevel == null,
                        asset.Path + ": clearing launch resets selection");
                }
            });
        }
    }
}
