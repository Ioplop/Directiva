using System;
using System.Collections.Generic;
using Directiva.SimulationFrontend;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests.SimulationFrontendTests
{
    /// <summary>Importer/compilation smoke tests for the Unity presentation layer.</summary>
    internal static class SimulationFrontendTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "Frontend", name, description, action));

            Add("DemoProgramsCompile", "Both Inspector demo programs compile and declare valid nexus/worker entries", t =>
            {
                var player = SimulationSceneSynchronizer.CompileSide("player",
                    SimulationSceneSynchronizer.DefaultPlayerScript);
                var enemy = SimulationSceneSynchronizer.CompileSide("enemy",
                    SimulationSceneSynchronizer.DefaultEnemyScript);
                t.Check(player.TryGetFunctionId("player.nexus_main", out _), "player nexus entry exists");
                t.Check(player.TryGetFunctionId("player.worker", out _), "player worker exists");
                t.Check(enemy.TryGetFunctionId("enemy.nexus_main", out _), "enemy nexus entry exists");
                t.Check(enemy.TryGetFunctionId("enemy.worker", out _), "enemy worker exists");
            });

            Add("EntityPrefabContracts", "All three world prefabs expose VisualEntity and interpolation", t =>
            {
                foreach (string kind in new[] { "Unit", "Nexus", "Resource" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Simulation/" + kind + ".prefab");
                    t.Check(prefab != null, kind + " prefab imported");
                    t.Check(prefab.GetComponent<VisualEntity>() != null,
                        kind + " has VisualEntity");
                    var interpolator = prefab.GetComponent<SimulationTransformInterpolator>();
                    t.Check(interpolator != null, kind + " has interpolator");
                    if (kind == "Resource")
                    {
                        t.Check(!interpolator.SynchronizeRotation,
                            "resource rotation is left to Rotator");
                        t.Check(prefab.GetComponent<VisualEntity>().Side == null,
                            "neutral resource does not require side color");
                    }
                }
            });

            Add("EffectPrefabContracts", "Attack is transient; gather survives until its controller calls End", t =>
            {
                var attack = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Simulation/Attack.prefab");
                var gather = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Simulation/Gather.prefab");
                t.Check(attack != null && gather != null, "both effect prefabs imported");
                t.Check(attack.GetComponent<AttackAligner>() != null, "attack uses AttackAligner");
                t.Check(attack.GetComponent<LifeTimeLimiter>() != null,
                    "attack has its own lifespan");
                t.Check(gather.GetComponent<GatherAligner>() != null, "gather uses GatherAligner");
                t.Check(gather.GetComponent<AttackAligner>() == null,
                    "gather no longer carries an AttackAligner");
                t.Check(gather.GetComponent<LifeTimeLimiter>() == null,
                    "gather has no autonomous lifespan limiter");
            });
        }
    }
}
