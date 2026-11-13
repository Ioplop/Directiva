using System;
using System.Collections.Generic;
using System.IO;
using Directiva.SimulationFrontend;
using Directiva.SimulationUI;
using Directiva.CodeUI;
using UnityEngine.UIElements;
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

            Add("SaveReplacesSelectedProgramSources",
                "Saving an edited entry file notifies the match and makes its new code available on disk", t =>
            {
                InRoots(1, roots =>
                {
                    const string original = "def main():\n    return 1\n";
                    const string modified = "def main():\n    return 2\n";
                    Write(roots[0], "main.dscript", original);
                    var storage = new FileSystemScriptStorage(roots[0]);
                    var workspace = new ScriptWorkspace(storage);
                    int saves = 0;
                    workspace.SavedScriptsChanged += () => saves++;
                    var first = PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");

                    workspace.Open("main.dscript");
                    workspace.UpdateActiveWorkingContent(modified);
                    workspace.FlushActiveBackup();
                    t.Equal(0, saves, "editing/auto-backup never triggers a program reload");
                    t.Equal(original, storage.ReadSaved("main.dscript"), "only the backup changed");

                    workspace.Save("main.dscript");
                    t.Equal(1, saves, "explicit save emits a single persisted-source notification");
                    t.Equal(modified, storage.ReadSaved("main.dscript"), "new code is saved");
                    t.Check(!workspace.Active.IsDirty, "document is clean after save");
                    var second = PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");
                    t.Check(first.DILModules["main"] != second.DILModules["main"],
                        "next compilation uses the edited program instead of the old snapshot");
                });
            });

            Add("SaveImportedScriptInvalidatesProgram",
                "Saving a dependency must notify the match even when the Nexus entry file is untouched", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript",
                        "import utils\ndef main():\n    return utils.answer()\n");
                    Write(roots[0], "utils.dscript", "def answer():\n    return 1\n");
                    var first = PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");

                    var workspace = new ScriptWorkspace(new FileSystemScriptStorage(roots[0]));
                    int saves = 0;
                    workspace.SavedScriptsChanged += () => saves++;
                    workspace.Open("utils.dscript");
                    workspace.UpdateActiveWorkingContent("def answer():\n    return 42\n");
                    workspace.Save("utils.dscript");

                    t.Equal(1, saves, "saving the dependency emits a source-change notification");
                    var second = PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");
                    t.Check(first.DILModules["utils"] != second.DILModules["utils"],
                        "new compilation reads the updated imported module");
                    t.Equal(first.DILModules["main"], second.DILModules["main"],
                        "the unchanged entry file need not be saved again");
                });
            });

            Add("ParserFailureKeepsClickableLocation",
                "A script rejected by the Nexus selector keeps the exact editable file and line", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "Test/test.dscript", "move()\n");
                    PlayerNexusCompilationException failure = null;
                    try
                    {
                        PlayerNexusScriptCatalog.ListEntryFunctions(roots[0], "Test/test.dscript");
                    }
                    catch (PlayerNexusCompilationException exception) { failure = exception; }

                    t.Check(failure != null, "invalid module-level statement is rejected");
                    t.Check(failure.Diagnostics.Count > 0, "the parser diagnostic is preserved");
                    var diagnostic = failure.Diagnostics[0];
                    t.Equal("Test.test", diagnostic.Module, "nested file retains module name");
                    t.Equal(1, diagnostic.Span.Line, "diagnostic points to first source line");
                    var location = OutputSourceLocation.FromDiagnostic(diagnostic);
                    t.Check(location.HasValue, "diagnostic can be used as a console link");
                    t.Equal("Test/test.dscript", location.Value.FilePath,
                        "console link opens the player's editable file");
                    t.Equal(1, location.Value.Line, "console link preserves line");
                    t.Equal(1, location.Value.Column, "console link preserves column");
                });
            });

            Add("CompilationFailureKeepsClickableLocation",
                "A compilation error after parsing retains the file/line of the failed source", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript", "def main():\n    unknown_name(123)\n");
                    PlayerNexusCompilationException failure = null;
                    try
                    {
                        PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");
                    }
                    catch (PlayerNexusCompilationException exception) { failure = exception; }

                    t.Check(failure != null, "invalid program is rejected");
                    t.Check(failure.Diagnostics.Count > 0, "all compiler diagnostics are retained");
                    bool foundSource = false;
                    foreach (var diagnostic in failure.Diagnostics)
                    {
                        var location = OutputSourceLocation.FromDiagnostic(diagnostic);
                        if (location.HasValue && location.Value.FilePath == "main.dscript" &&
                            location.Value.Line == 2)
                            foundSource = true;
                    }
                    t.Check(foundSource, "console can navigate to the failing expression");
                });
            });

            Add("ImportedModuleErrorKeepsClickableLocation",
                "Diagnostics in imported player modules navigate to the imported source", t =>
            {
                InRoots(1, roots =>
                {
                    Write(roots[0], "main.dscript", "import utils\ndef main():\n    pass\n");
                    Write(roots[0], "utils.dscript", "def broken():\n    undefined_variable\n");
                    PlayerNexusCompilationException failure = null;
                    try
                    {
                        PlayerNexusScriptCatalog.Compile(roots[0], "main.dscript", "main");
                    }
                    catch (PlayerNexusCompilationException exception) { failure = exception; }
                    t.Check(failure != null, "imported source error prevents compilation");
                    bool importLocation = false;
                    foreach (var diagnostic in failure.Diagnostics)
                    {
                        var location = OutputSourceLocation.FromDiagnostic(diagnostic);
                        if (location.HasValue && location.Value.FilePath == "utils.dscript" &&
                            location.Value.Line == 2)
                            importLocation = true;
                    }
                    t.Check(importLocation, "imported source preserves its own location");
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

            Add("PickerDoesNotInventAnInitialSelection",
                "A populated script list still has blank script and function fields until chosen", t =>
            {
                var root = new VisualElement();
                var picker = new SimulationNexusProgramPicker(root);
                int assignments = 0;
                picker.ScriptChanged += _ => assignments++;
                picker.FunctionChanged += (_, __) => assignments++;
                var scripts = new[] { "Level01/lvl01.dscript", "second.dscript" };
                picker.SetScripts(scripts, null);
                picker.SetFunctions(Array.Empty<string>(), null);
                t.Equal(string.Empty, picker.SelectedScript,
                    "first file must not appear assigned by default");
                t.Equal(string.Empty, picker.SelectedFunction,
                    "no script means no entry function");
                t.Check(root.Q<DropdownField>("nexus-script-dropdown").enabledSelf,
                    "user may choose a script while stopped");
                t.Check(!root.Q<DropdownField>("nexus-function-dropdown").enabledSelf,
                    "no entry function may be chosen without a script");
                t.Equal(0, assignments, "listing scripts must not silently start a program");

                picker.SetScripts(scripts, "Level01/lvl01.dscript");
                picker.SetFunctions(new[] { "nexus_main", "worker" }, "nexus_main");
                t.Equal("Level01/lvl01.dscript", picker.SelectedScript,
                    "explicitly chosen script is visible");
                t.Equal("nexus_main", picker.SelectedFunction,
                    "explicitly chosen entry is visible");
                picker.SetScripts(scripts, "removed.dscript");
                picker.SetFunctions(Array.Empty<string>(), null);
                t.Equal(string.Empty, picker.SelectedScript,
                    "a missing saved selection must not silently select another script");
                t.Equal(string.Empty, picker.SelectedFunction,
                    "entry clears with the missing script");
                t.Equal(0, assignments, "silent refreshes never execute a program");
            });

            Add("PickerChangesDoNotApplyDuringRefresh",
                "Refreshing saved script/function choices does not emit selection events", t =>
            {
                var root = new VisualElement();
                var picker = new SimulationNexusProgramPicker(root);
                int scriptChanged = 0;
                int functionChanged = 0;
                picker.ScriptChanged += _ => scriptChanged++;
                picker.FunctionChanged += (_, __) => functionChanged++;

                picker.SetScripts(new[] { "one.dscript", "two.dscript" }, "one.dscript");
                picker.SetFunctions(new[] { "main", "other" }, "main");
                t.Equal("one.dscript", picker.SelectedScript, "initial saved script");
                t.Equal("main", picker.SelectedFunction, "initial entry");
                t.Equal(0, scriptChanged, "script refresh never assigns");
                t.Equal(0, functionChanged, "function refresh never assigns");

                picker.SetScripts(new[] { "one.dscript", "two.dscript" }, "two.dscript");
                picker.SetFunctions(new[] { "main", "other" }, "other");
                t.Equal("two.dscript", picker.SelectedScript, "refreshed selected script");
                t.Equal("other", picker.SelectedFunction, "refreshed selected function");
                t.Equal(0, scriptChanged, "programmatic script selection is silent");
                t.Equal(0, functionChanged, "programmatic entry selection is silent");

                var scriptField = root.Q<DropdownField>("nexus-script-dropdown");
                var functionField = root.Q<DropdownField>("nexus-function-dropdown");
                t.Check(scriptField != null && functionField != null, "both dropdowns exist");
                // Detached UI Toolkit fields do not dispatch ChangeEvent in all
                // Unity versions. Selection callback delivery is checked in
                // Play Mode; here we validate silent programmatic refreshes.
            });

            Add("PickerUnsavedWarningIsConditional",
                "An unsaved warning is visible only while working code differs from saved code", t =>
            {
                var root = new VisualElement();
                var picker = new SimulationNexusProgramPicker(root);
                var warning = root.Q<Label>(className: "sim-nexus-panel-warning");
                t.Check(warning != null, "warning element exists");
                t.Equal(DisplayStyle.None, warning.style.display.value, "hidden initially");
                picker.SetUnsavedChanges(true);
                t.Equal(DisplayStyle.Flex, warning.style.display.value, "shown for dirty workspace");
                picker.SetUnsavedChanges(false);
                t.Equal(DisplayStyle.None, warning.style.display.value, "hidden after save");
            });

            Add("PickerLocksUntilStop", "Only Stop restores Nexus script and function editing", t =>
            {
                var root = new VisualElement();
                var picker = new SimulationNexusProgramPicker(root);
                var scriptField = root.Q<DropdownField>("nexus-script-dropdown");
                var functionField = root.Q<DropdownField>("nexus-function-dropdown");
                var lockWarning = root.Q<Label>("nexus-locked-warning");
                var unsavedWarning = root.Q<Label>(className: "sim-nexus-panel-warning");
                t.Check(scriptField != null && functionField != null, "dropdowns exist");
                t.Check(lockWarning != null, "localized locked warning exists");
                picker.SetScripts(new[] { "one.dscript", "two.dscript" }, "one.dscript");
                picker.SetFunctions(new[] { "main", "other" }, "main");
                t.Check(scriptField.enabledSelf && functionField.enabledSelf,
                    "both choices enabled while stopped");
                picker.SetUnsavedChanges(true);
                picker.SetSelectionLocked(true);
                t.Check(picker.SelectionLocked, "match has locked the picker");
                t.Check(!scriptField.enabledSelf && !functionField.enabledSelf,
                    "both controls are disabled while a match is active");
                t.Equal(DisplayStyle.Flex, lockWarning.style.display.value,
                    "match warning is shown");
                t.Equal(DisplayStyle.None, unsavedWarning.style.display.value,
                    "match warning takes precedence over the unsaved notice");
                picker.SetScripts(new[] { "three.dscript" }, "three.dscript");
                picker.SetFunctions(new[] { "worker" }, "worker");
                t.Check(!scriptField.enabledSelf && !functionField.enabledSelf,
                    "refresh cannot reenable a locked selector");
                picker.SetSelectionLocked(false);
                t.Check(!picker.SelectionLocked, "Stop unlocks the picker");
                t.Check(scriptField.enabledSelf && functionField.enabledSelf,
                    "dropdowns become available after Stop");
                t.Equal(DisplayStyle.None, lockWarning.style.display.value,
                    "match warning disappears after Stop");
                t.Equal(DisplayStyle.Flex, unsavedWarning.style.display.value,
                    "pending save warning returns after Stop");
            });

            Add("PickerEmptyChoicesRemainDisabled", "Stop does not enable a selector with no options", t =>
            {
                var root = new VisualElement();
                var picker = new SimulationNexusProgramPicker(root);
                var scriptField = root.Q<DropdownField>("nexus-script-dropdown");
                var functionField = root.Q<DropdownField>("nexus-function-dropdown");
                picker.SetScripts(Array.Empty<string>(), null);
                picker.SetFunctions(Array.Empty<string>(), null);
                picker.SetSelectionLocked(true);
                picker.SetSelectionLocked(false);
                t.Check(!picker.SelectionLocked, "picker returns to stopped state");
                // No choices must remain inaccessible even after unlocking.
                t.Check(!scriptField.enabledSelf && !functionField.enabledSelf,
                    "empty fields stay disabled after Stop");
                t.Equal(string.Empty, picker.SelectedScript, "empty scripts remain empty");
                t.Equal(string.Empty, picker.SelectedFunction, "empty entries remain empty");
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
