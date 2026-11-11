using System;
using System.Collections.Generic;
using System.IO;
using Directiva.SimulationFrontend;
using UnityEditor;
using UnityEngine;

namespace Directiva.EditorTests.SimulationFrontendTests
{
    /// <summary>Filesystem, parser and isolated-compilation tests without entering Play Mode.</summary>
    internal static class PlayerNexusSelectionTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("Simulation", "PlayerScripts", name, description, action));

            Add("DiscoversNestedScripts", "Selection lists files in subfolders, not backups", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript", "def start():\n    pass\n");
                    Write(roots[0], "tools/extra.dscript", "def helper():\n    pass\n");
                    Write(roots[0], "main.dscript.back", "old backup");
                    var scripts = PlayerNexusScriptCatalog.ListScripts(roots[0]);
                    t.Equal(2, scripts.Count, "only real scripts");
                    t.Check(scripts.Contains("main.dscript"), "main listed");
                    t.Check(scripts.Contains("tools/extra.dscript"), "nested module listed");
                    t.Equal("tools.extra", PlayerNexusScriptCatalog.ModuleFromPath("tools/extra.dscript"),
                        "module naming agrees with imports");
                });
            });

            Add("ListsOnlyValidEntryFunctions", "Only parameterless top-level defs appear", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript",
                        "shared count = 0\n\ndef nexus_main():\n    pass\n\ndef worker(x):\n    pass\n\ndef alternative():\n    pass\n");
                    var names = PlayerNexusScriptCatalog.ListEntryFunctions(roots[0], "main.dscript");
                    t.Equal(2, names.Count, "zero-argument entries only");
                    t.Equal("nexus_main", names[0], "source order first");
                    t.Equal("alternative", names[1], "source order second");
                });
            });

            Add("CompilesUserProgramWithImportsAndSelf", "Player entry compiles from disk using the simulation profile", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript",
                        "import utils\ndef nexus_main():\n    move(10, 20)\n    utils.ping()\n");
                    Write(roots[0], "utils.dscript", "def ping():\n    return 1\n");
                    var compiled = PlayerNexusScriptCatalog.Compile(roots[0],
                        "main.dscript", "nexus_main");
                    t.Check(compiled.TryGetFunctionId("main.nexus_main", out _), "entry is linked");
                    t.Check(compiled.TryGetFunctionId("utils.ping", out _), "import is linked");
                });
            });

            Add("SeparateRootsAllowSameNames", "Player and enemy module names never collide", t =>
            {
                InRoots(2, roots =>
                {
                    Write(roots[0], "main.dscript", "def main():\n    return 10\n");
                    Write(roots[1], "main.dscript", "def main():\n    return 20\n");
                    var first = PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");
                    var second = PlayerNexusScriptCatalog.Compile(roots[1], "main.dscript", "main");
                    t.Check(first.TryGetFunctionId("main.main", out _), "first program entry exists");
                    t.Check(second.TryGetFunctionId("main.main", out _), "second program entry exists");
                    t.Check(!ReferenceEquals(first, second), "independent compilation outputs");
                    t.Check(first.DILModules["main"] != second.DILModules["main"],
                        "each root retains its own function body");
                });
            });

            Add("MissingOrParameterizedEntryRejected", "Never bind a missing or parameterized Nexus function", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript", "def worker(x):\n    return x\n");
                    bool rejected = false;
                    try { PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "worker"); }
                    catch (InvalidOperationException) { rejected = true; }
                    t.Check(rejected, "parameterized entry is rejected");
                    rejected = false;
                    try { PlayerNexusScriptCatalog.ModuleFromPath("../other.dscript"); }
                    catch (ArgumentException) { rejected = true; }
                    t.Check(rejected, "parent traversal is rejected");
                });
            });

            Add("PrefabCollidersCanBeSelected", "The three world prefabs have colliders on their meshes", t =>
            {
                foreach (string kind in new[] { "Nexus", "Unit", "Resource" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Simulation/" + kind + ".prefab");
                    t.Check(prefab != null, kind + " prefab exists");
                    t.Check(prefab.GetComponentInChildren<Collider>(true) != null,
                        kind + " can receive world raycasts");
                }
            });
        }

        private static void Write(string root, string path, string source)
        {
            string absolute = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, source);
        }

        private static void InRoots(int count, Action<string[]> run)
        {
            string parent = Path.Combine(Path.GetTempPath(), "DirectivaNexusSelection_" + Guid.NewGuid().ToString("N"));
            var roots = new string[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    roots[i] = Path.Combine(parent, "root_" + i);
                    Directory.CreateDirectory(roots[i]);
                }
                run(roots);
            }
            finally
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, true);
            }
        }
    }
}
