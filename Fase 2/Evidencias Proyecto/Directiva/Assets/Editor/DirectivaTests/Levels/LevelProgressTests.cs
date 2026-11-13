using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMath;
using DOrchestration;
using Directiva.Levels;
using Directiva.Progress;
using Directiva.EditorTests.SimulationTests;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests.LevelTests
{
    /// <summary>Regression tests for time conditions, shield damage, and persistent stars.</summary>
    internal static class LevelProgressTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "Levels", name, description, action));

            Add("ThreeIndependentObjectives", "Only victories award stars; fast and no-health-loss objectives are independent", t =>
            {
                t.Equal(LevelStarFlags.None,
                    LevelStarRules.Evaluate(MatchOutcome.Draw, false, (Fixed)10, 600), "draw grants nothing");
                t.Equal(LevelStarFlags.None,
                    LevelStarRules.Evaluate(MatchOutcome.Side2Won, false, (Fixed)10, 600), "defeat grants nothing");
                t.Equal(LevelStarFlags.All,
                    LevelStarRules.Evaluate(MatchOutcome.Side1Won, false, (Fixed)599, 600),
                    "flawless fast victory gets all stars");
                t.Equal(LevelStarFlags.Victory | LevelStarFlags.FastVictory,
                    LevelStarRules.Evaluate(MatchOutcome.Side1Won, true, (Fixed)599, 600),
                    "health-damaged nexus loses only the no-health-loss star");
                t.Equal(LevelStarFlags.Victory | LevelStarFlags.UntouchedNexus,
                    LevelStarRules.Evaluate(MatchOutcome.Side1Won, false, (Fixed)600, 600),
                    "exactly at speed threshold does not earn fast star");
                t.Equal(LevelStarFlags.Victory,
                    LevelStarRules.Evaluate(MatchOutcome.Side1Won, true, (Fixed)601, 600),
                    "slow victory with health damage grants only victory");
                t.Equal(2, LevelStarRules.Count(LevelStarFlags.Victory | LevelStarFlags.FastVictory),
                    "independent flags count correctly");
            });

            Add("ShieldDamagePreservesSecondStar", "Shield-only hits do not disqualify a win, even after regeneration", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1,
                    100, 10, 10);
                world.Start();
                t.Check(!entity.HasTakenDamage, "new entity has never been damaged");
                t.Check(!entity.HasTakenHealthDamage, "new entity has never lost health");
                entity.ApplyDamage((Fixed)(-2));
                t.Check(!entity.HasTakenDamage, "healing is not damage");
                t.Check(!entity.HasTakenHealthDamage, "healing does not count as health loss");
                entity.ApplyDamage(8);
                entity.ApplyDamage(2); // Exactly consumes the remaining shields.
                t.Check(entity.HasTakenDamage, "shield hits still count as generic damage");
                t.Check(!entity.HasTakenHealthDamage, "shield-only hits do not count as health damage");
                t.Equal((Fixed)100, entity.Health, "health remains full after shields reach zero");
                t.Equal((Fixed)0, entity.Shields, "shields were consumed completely");
                t.Equal(LevelStarFlags.All,
                    LevelStarRules.Evaluate(MatchOutcome.Side1Won, entity.HasTakenHealthDamage, (Fixed)10, 600),
                    "a fast shield-only victory awards the second star");
                for (int i = 0; i < 300; i++) world.Update();
                t.Equal(entity.MaxShields, entity.Shields, "shields regenerated");
                t.Check(!entity.HasTakenHealthDamage, "shield regeneration does not alter health damage history");
            });

            Add("HealthDamageForfeitsSecondStar", "Damage passing through shields permanently prevents the no-health-loss star", t =>
            {
                var world = SimulationTestHarness.NewWorld();
                var entity = SimulationTestHarness.AddWithStats(world, 5, 5, 1,
                    100, 10, 10);
                world.Start();
                entity.ApplyDamage(12); // 10 to shields, 2 to health.
                t.Equal((Fixed)0, entity.Shields, "shields absorbed the first 10 damage");
                t.Equal((Fixed)98, entity.Health, "only the overflow reached health");
                t.Check(entity.HasTakenHealthDamage, "health damage is latched");
                t.Equal(LevelStarFlags.Victory | LevelStarFlags.FastVictory,
                    LevelStarRules.Evaluate(MatchOutcome.Side1Won, entity.HasTakenHealthDamage, (Fixed)10, 600),
                    "a victory with health damage cannot earn the second star");
                entity.ApplyDamage((Fixed)(-2)); // Heal health back to full.
                t.Equal(entity.MaxHealth, entity.Health, "health restored to maximum");
                t.Check(entity.HasTakenHealthDamage, "healing does not erase the lost-health history");
            });

            Add("BinarySaveAccumulatesStars", "Disk round-trip preserves each objective, deduplicates wins and stores multiple levels", t =>
            {
                WithTemporarySave(path =>
                {
                    var store = new LevelProgressStore(path);
                    t.Check(!File.Exists(path), "a fresh game does not create a save until progress is earned");
                    t.Equal(LevelStarFlags.None, store.GetStars(1), "first level starts at zero");
                    t.Check(store.TryAwardStars(1,
                        LevelStarFlags.Victory | LevelStarFlags.FastVictory, out LevelStarFlags added),
                        "first award saved");
                    t.Equal(LevelStarFlags.Victory | LevelStarFlags.FastVictory, added,
                        "awarded two distinct objectives");
                    t.Check(store.TryAwardStars(1,
                        LevelStarFlags.Victory | LevelStarFlags.UntouchedNexus, out added),
                        "second match saved");
                    t.Equal(LevelStarFlags.UntouchedNexus, added,
                        "only previously missing star added");
                    t.Equal(LevelStarFlags.All, store.GetStars(1), "first level has 3 stars");
                    t.Check(store.TryAwardStars(1, LevelStarFlags.All, out added),
                        "replaying a perfect win is harmless");
                    t.Equal(LevelStarFlags.None, added, "repeated objective not awarded again");
                    t.Check(store.TryAwardStars(2, LevelStarFlags.Victory, out added),
                        "another level award saved");

                    var reopened = new LevelProgressStore(path);
                    t.Equal(LevelStarFlags.All, reopened.GetStars(1), "first level survived restart");
                    t.Equal(LevelStarFlags.Victory, reopened.GetStars(2), "second level survived restart");
                    t.Equal(LevelStarFlags.None, reopened.GetStars(3), "unplayed level remains empty");
                    t.Equal(19L, new FileInfo(path).Length, "binary format has two compact entries");
                });
            });

            Add("BackupRecoversProgress", "A corrupt primary is recovered from the last valid backup", t =>
            {
                WithTemporarySave(path =>
                {
                    var store = new LevelProgressStore(path);
                    t.Check(store.TryAwardStars(1, LevelStarFlags.Victory, out _), "first saved win");
                    t.Check(store.TryAwardStars(1, LevelStarFlags.All, out _), "second saved win");
                    t.Check(File.Exists(path + ".bak"), "previous save backed up");
                    File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                    var recovered = new LevelProgressStore(path);
                    t.Equal(LevelStarFlags.Victory, recovered.GetStars(1),
                        "valid previous save recovered rather than empty score");
                    t.Check(recovered.TryAwardStars(1, LevelStarFlags.FastVictory, out _),
                        "recovering and earning a new objective writes again");
                    t.Equal(LevelStarFlags.Victory | LevelStarFlags.FastVictory,
                        new LevelProgressStore(path).GetStars(1), "recovery persisted");
                    t.Check(Directory.GetFiles(Path.GetDirectoryName(path), "Save.bin.corrupt-*").Length == 1,
                        "damaged primary preserved for inspection");
                });
            });

            Add("FutureSaveIsProtected", "Unknown future save versions are read-only, never overwritten", t =>
            {
                WithTemporarySave(path =>
                {
                    byte[] future = { (byte)'D', (byte)'S', (byte)'V', (byte)'1', 2, 0, 0, 0, 0 };
                    File.WriteAllBytes(path, future);
                    var store = new LevelProgressStore(path);
                    t.Check(store.IsReadOnly, "newer format cannot be overwritten");
                    t.Check(!store.TryAwardStars(1, LevelStarFlags.All, out _),
                        "award refused for newer version");
                    t.Check(File.ReadAllBytes(path).SequenceEqual(future),
                        "original future-version data intact");
                });
            });

            Add("TotalCountsOnlyConfiguredLevels", "A duplicate level asset never double-counts the same saved stars", t =>
            {
                // This is a rules test, not a test of Level01/Level02's current values.
                // Use temporary levels so changing or renumbering real assets never breaks it.
                DirectivaLevel one = ScriptableObject.CreateInstance<DirectivaLevel>();
                DirectivaLevel two = ScriptableObject.CreateInstance<DirectivaLevel>();
                try
                {
                    var oneProperties = new SerializedObject(one);
                    oneProperties.FindProperty("levelNumber").intValue = 101;
                    oneProperties.ApplyModifiedPropertiesWithoutUndo();
                    var twoProperties = new SerializedObject(two);
                    twoProperties.FindProperty("levelNumber").intValue = 102;
                    twoProperties.ApplyModifiedPropertiesWithoutUndo();

                    WithTemporarySave(path =>
                    {
                        var store = new LevelProgressStore(path);
                        t.Check(store.TryAwardStars(101, LevelStarFlags.All, out _),
                            "first synthetic level persisted");
                        t.Check(store.TryAwardStars(102, LevelStarFlags.Victory, out _),
                            "second synthetic level persisted");
                        t.Equal(4, LevelProgress.CountStarsInLevels(new[] { one, one, two }, store),
                            "first level counted once, plus the victory star of the second");
                        t.Equal(3, LevelProgress.CountStarsInLevels(new[] { one }, store),
                            "referencing only one level counts only its stars");
                    });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(one);
                    UnityEngine.Object.DestroyImmediate(two);
                }
            });
        }

        private static void WithTemporarySave(Action<string> test)
        {
            string folder = Path.Combine(Path.GetTempPath(), "DirectivaSaveTests_" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try { test(Path.Combine(folder, "Save.bin")); }
            finally { Directory.Delete(folder, true); }
        }
    }
}
