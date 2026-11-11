using System;
using System.Collections.Generic;
using Directiva.Levels;
using Directiva.MainMenu;
using Directiva.SimulationFrontend;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests.LevelTests
{
    /// <summary>Checks level assets, sorting, launch state, and isolated enemy compilation.</summary>
    internal static class DirectivaLevelTests
    {
        private const string SamplePath = "Assets/Levels/Level01.asset";

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "Levels", name, description, action));

            Add("SampleLevelAsset", "Level 1 and the scene are configured as reusable assets", t =>
            {
                DirectivaLevel sample = AssetDatabase.LoadAssetAtPath<DirectivaLevel>(SamplePath);
                t.Check(sample != null, "sample DirectivaLevel asset exists");
                t.Equal(1, sample.LevelNumber, "sample level order");
                t.Equal(0, sample.RequiredStars, "sample level is initially unlocked");
                t.Equal("Level01", sample.CodeFolder, "enemy folder");
                t.Equal("enemy", sample.EntryModule, "enemy entry module");
                t.Equal("nexus_main", sample.EntryFunction, "enemy entry function");
                t.Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(sample.ScenePath) != null,
                    "selected scene asset exists");
                t.Equal("Assets/Scenes/BaseSimulation", sample.SceneLoadPath,
                    "Unity receives a scene path without the .unity extension");
            });

            Add("ImportedDscriptIsTextAsset", "A .dscript under Resources is importable as a TextAsset", t =>
            {
                TextAsset text = Resources.Load<TextAsset>("Levels/Level01/enemy");
                t.Check(text != null, "custom .dscript importer produced a TextAsset");
                t.Contains("def nexus_main():", text.text, "source text retained");
            });

            Add("LevelProgramCompiles", "Selected enemy script compiles with its own module provider", t =>
            {
                DirectivaLevel sample = AssetDatabase.LoadAssetAtPath<DirectivaLevel>(SamplePath);
                var compiled = SimulationSceneSynchronizer.CompileLevelEnemy(sample);
                t.Check(compiled.TryGetFunctionId("enemy.nexus_main", out _),
                    "selected nexus function linked");
                t.Check(compiled.TryGetFunctionId("enemy.worker", out _),
                    "the worker referenced by build is linked");
            });

            Add("RootedModuleProvider", "Imports cannot escape the selected level or reach player code", t =>
            {
                var provider = new ResourcesLevelDScriptSourceProvider("Level01");
                t.Check(provider.TryReadModule("enemy", out string source),
                    "enemy module is available from its own folder");
                t.Contains("shared charge", source, "expected enemy script read");
                t.Check(!provider.TryReadModule("player", out _),
                    "player workspace is never searched");
                t.Check(!provider.TryReadModule("MissingOtherLevel.enemy", out _),
                    "imports cannot load another level by prefix");
                bool rejected = false;
                try { new ResourcesLevelDScriptSourceProvider("../Level01"); }
                catch (ArgumentException) { rejected = true; }
                t.Check(rejected, "directory traversal is rejected");
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

            Add("SelectedLevelSurvivesLaunch", "Launch context retains asset for restart and clears cleanly", t =>
            {
                DirectivaLevel level = AssetDatabase.LoadAssetAtPath<DirectivaLevel>(SamplePath);
                try
                {
                    LevelLaunchContext.SetPendingLevel(level);
                    t.Check(LevelLaunchContext.CurrentLevel == level,
                        "selected asset remains available across scene load");
                    t.Equal(level.LevelName, LevelLaunchContext.CurrentLevelName,
                        "menu display name preserved");
                    t.Check(!LevelLaunchContext.TryConsume(out _),
                        "legacy bootstrapper does not consume a ScriptableObject launch");
                    t.Check(LevelLaunchContext.CurrentLevel == level,
                        "selected asset persists after legacy consume attempt");
                }
                finally { LevelLaunchContext.Clear(); }
                t.Check(LevelLaunchContext.CurrentLevel == null, "clear resets selection");
            });
        }
    }
}
