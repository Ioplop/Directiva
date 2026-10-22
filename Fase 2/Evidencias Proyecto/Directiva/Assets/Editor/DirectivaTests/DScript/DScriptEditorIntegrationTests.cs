using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Directiva.CodeUI;
using DSExecution.DataTypes;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DScript
{
    /// <summary>Editor pipeline regressions using isolated temp roots, not player files.</summary>
    internal static class DScriptEditorIntegrationTests
    {
        private static void WithWorkspace(Action<string, FileSystemScriptStorage, ScriptWorkspace> action)
        {
            string root = Path.Combine(Path.GetTempPath(), "DirectivaDScriptEditorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var storage = new FileSystemScriptStorage(root);
                var workspace = new ScriptWorkspace(storage);
                action(root, storage, workspace);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static WorkspaceDScriptSourceProvider Provider(string root,
            FileSystemScriptStorage storage, ScriptWorkspace workspace)
            => new WorkspaceDScriptSourceProvider(new DirectivaCode(storage), workspace, root);

        private static long RunInt(DirectivaTestContext t, DScriptEditorPreparedRun result)
        {
            t.Check(result.Success, "Editor preparation: " + result.Error + " / " +
                string.Join(" | ", result.Compilation.Diagnostics));
            var controller = new VMExecutionController();
            controller.Start(result.VM);
            int maxFrames = 50;
            while (controller.State == ScriptExecutionState.Running && --maxFrames > 0)
                controller.Tick(5000);
            t.Equal(ScriptExecutionState.Stopped, controller.State, "Controller should stop");
            t.Equal(VMExecutionStatus.Completed, result.VM.Status, "VM result");
            t.Equal(DataTypeId.Int, result.VM.ReturnValue.dataType, "return type");
            return unchecked((long)result.VM.ReturnValue.value);
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "EditorIntegration", name, description, action);

            Add("OnlyDScriptInTree", "Old DIL remains untouched and is hidden; backup is hidden", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main():\n    return 1\n");
                    File.WriteAllText(Path.Combine(root, "legacy.dil"), "PUSH INT 4\n");
                    storage.WriteBackup("main.dscript", "def main():\n    return 9\n");
                    var files = storage.EnumerateScripts().ToList();
                    t.Equal(1, files.Count, "visible scripts");
                    t.Equal("main.dscript", files[0], "only editable script");
                    t.Equal("PUSH INT 4\n", File.ReadAllText(Path.Combine(root, "legacy.dil")), "old DIL preserved");
                }));

            Add("ActiveWorkingBuffer", "Run from unsaved active buffer without touching saved DScript", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main():\n    return 1\n");
                    workspace.Open("main.dscript");
                    workspace.UpdateActiveWorkingContent("def main():\n    return 42\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Equal(42L, RunInt(t, result), "active buffer returned");
                    t.Contains("return 1", storage.ReadSaved("main.dscript"), "saved source unmodified");
                    t.Check(workspace.Active.IsDirty, "run does not silently save");
                }));

            Add("ImportUsesBackFile", "Imported modules read .dscript.back before saved .dscript", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "import helpers as h\ndef main():\n    return h.answer()\n");
                    storage.CreateScript("", "helpers", "def answer():\n    return 1\n");
                    storage.WriteBackup("helpers.dscript", "def answer():\n    return 88\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Equal(88L, RunInt(t, result), "imported backup content");
                }));

            Add("RuntimeSharedStartup", "Initialize shared slots before editor function and share memory", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "shared value = 17\ndef main():\n    return value + 3\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(result.Initialization != null && !result.Initialization.HasErrors, "successful initialization");
                    t.Equal(20L, RunInt(t, result), "shared initialized before function");
                }));

            Add("SeparateExecutionMemories", "Two executions never share mutable globals or heap", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "shared count = 0\ndef main():\n    count += 1\n    return count\n");
                    var provider = Provider(root, storage, workspace);
                    var first = DScriptEditorRunPipeline.Prepare("main", "main", provider);
                    var second = DScriptEditorRunPipeline.Prepare("main", "main", provider);
                    t.Check(!ReferenceEquals(first.Memory, second.Memory), "memory objects differ");
                    t.Equal(1L, RunInt(t, first), "first context");
                    t.Equal(1L, RunInt(t, second), "second context");
                }));

            Add("FaultyInitializerContinues", "Initializer fault is reported and later shared initializes", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "shared bad = 1 / 0\nshared good = 7\ndef main():\n    return good\n");
                    int errorCount = 0;
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace),
                        onInitializerError: (step, err) => errorCount++);
                    t.Check(result.Success, "fault does not abort editor session");
                    t.Check(result.Initialization.HasErrors, "fault recorded");
                    t.Equal(1, errorCount, "one failure callback");
                    t.Equal(7L, RunInt(t, result), "later initializer remains effective");
                }));

            Add("BlankEntryUsesFirstDeclaredFunction", "An empty fn: executes first def in selected module, not main or alphabetical order", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "entry",
                        "shared n = 10\ndef zebra():\n    return n + 7\ndef main():\n    return 99\ndef alpha():\n    return 1\n");
                    var result = DScriptEditorRunPipeline.Prepare("entry", "", Provider(root, storage, workspace));
                    t.Equal("entry.zebra", result.FunctionName, "first declaration chosen");
                    t.Equal(17L, RunInt(t, result), "first declaration executed");
                }));

            Add("BlankEntryPrintOnlyFunction", "Reproduce editor example with hola_mundo() and implicit None return", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "firstdscript", "def hola_mundo():\n    print(\"Esto debería funcionar!\")\n");
                    var prepared = DScriptEditorRunPipeline.Prepare("firstdscript", "",
                        Provider(root, storage, workspace));
                    t.Check(prepared.Success, "first function prepared: " + prepared.Error);
                    t.Equal("firstdscript.hola_mundo", prepared.FunctionName, "entry is the first function");
                    var output = new List<string>();
                    var controller = new VMExecutionController();
                    controller.Printed += text => output.Add(text);
                    controller.Start(prepared.VM);
                    for (int i = 0; i < 50 && controller.State == ScriptExecutionState.Running; i++)
                        controller.Tick(5000);
                    t.Equal(ScriptExecutionState.Stopped, controller.State, "controller stopped");
                    t.Equal(VMExecutionStatus.Completed, prepared.VM.Status, "execution completed");
                    t.Equal(DataTypeId.None, prepared.VM.ReturnValue.dataType, "implicit return None");
                    t.Equal(1, output.Count, "one printed line");
                    t.Contains("Esto debería funcionar!", output[0], "expected console text");
                }));

            Add("BlankEntryAllowsWhitespaceOrNull", "Null and whitespace fn: also select first function", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "demo", "def hola_mundo():\n    return 42\n");
                    var provider = Provider(root, storage, workspace);
                    var fromNull = DScriptEditorRunPipeline.Prepare("demo", null, provider);
                    var fromSpaces = DScriptEditorRunPipeline.Prepare("demo", "  \t  ", provider);
                    t.Equal("demo.hola_mundo", fromNull.FunctionName, "null selects first def");
                    t.Equal("demo.hola_mundo", fromSpaces.FunctionName, "blank selects first def");
                    t.Equal(42L, RunInt(t, fromNull), "null entry VM");
                    t.Equal(42L, RunInt(t, fromSpaces), "whitespace entry VM");
                }));

            Add("ExplicitEntryOverridesFirst", "An explicit fn: name overrides declaration order", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "entry", "def primero():\n    return 1\ndef segundo():\n    return 2\n");
                    var result = DScriptEditorRunPipeline.Prepare("entry", " segundo ", Provider(root, storage, workspace));
                    t.Equal("entry.segundo", result.FunctionName, "explicit function name");
                    t.Equal(2L, RunInt(t, result), "explicit function executed");
                }));

            Add("DefaultEntryUsesOnlySelectedModule", "Imports and shared do not change which local def is first", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "helper", "def helper_start():\n    return 99\n");
                    storage.CreateScript("", "entry",
                        "import helper as h\nshared n = 4\ndef local_first():\n    return n + 2\n");
                    var result = DScriptEditorRunPipeline.Prepare("entry", "", Provider(root, storage, workspace));
                    t.Equal("entry.local_first", result.FunctionName, "selected module only");
                    t.Equal(6L, RunInt(t, result), "selected module execution");
                }));

            Add("NoLocalFunctionReportsError", "An entry module without defs does not fall back to an imported def or bootstrap", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "helper", "def helper_start():\n    return 99\n");
                    storage.CreateScript("", "entry", "import helper as h\nshared n = 2\n");
                    var result = DScriptEditorRunPipeline.Prepare("entry", "", Provider(root, storage, workspace));
                    t.Check(result.Compilation.Success, "source compiled normally");
                    t.Check(!result.Success, "no user function to execute");
                    t.Contains("no declara ninguna función", result.Error, "specific error");
                    t.Check(result.VM == null && result.Memory == null && result.Initialization == null,
                        "bootstrap was not invoked");
                }));

            Add("FirstDeclaredFunctionStillRequiresNoParameters", "Do not silently skip a first function that needs parameters", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "entry",
                        "def first(x):\n    return x\ndef second():\n    return 2\n");
                    var result = DScriptEditorRunPipeline.Prepare("entry", "", Provider(root, storage, workspace));
                    t.Check(result.Compilation.Success, "compiled despite entry arity");
                    t.Check(!result.Success, "first function cannot start without arguments");
                    t.Contains("'first'", result.Error, "the actual first function rejected");
                    t.Contains("parámetro", result.Error, "descriptive arity error");
                }));

            Add("MetadataDefaultsToAutomaticEntry", "New metadata retains an empty fn: and explicit names remain persisted", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "entry", "def hola():\n    return 5\n");
                    var store = new EditorDocumentMetadataStore(root);
                    t.Equal(string.Empty, new EditorDocumentMetadata().entryFunction, "fresh metadata default");
                    t.Equal(string.Empty, store.Load("entry.dscript").entryFunction, "missing sidecar default");
                    store.Save("entry.dscript", new EditorDocumentMetadata { entryFunction = null });
                    t.Equal(string.Empty, store.Load("entry.dscript").entryFunction, "null metadata normalized");
                    store.Save("entry.dscript", new EditorDocumentMetadata { entryFunction = "hola" });
                    t.Equal("hola", store.Load("entry.dscript").entryFunction, "explicit choice retained");
                }));

            Add("BadFunctionDoesNotBootstrap", "Entry-point validation prevents shared side effects", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "shared n = 7\ndef main():\n    return n\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "unknown", Provider(root, storage, workspace));
                    t.Check(result.Compilation.Success, "DScript valid");
                    t.Check(!result.Success, "unknown function cannot start");
                    t.Check(result.Initialization == null && result.Memory == null, "bootstrap did not run");
                }));

            Add("ParametersRejected", "Editor requires a zero-parameter entry", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main(x):\n    return x\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(result.Compilation.Success, "source compiles");
                    t.Check(!result.Success && result.Error.Contains("parámetro"), "arity error");
                }));

            Add("CompilationErrorSafe", "Bad DScript produces diagnostics and no VM", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main(:\n    return 1\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(!result.Success, "no executable program");
                    t.Check(!result.Compilation.Success && result.Compilation.Diagnostics.Count > 0,
                        "frontend syntax diagnostic");
                }));

            Add("GeneratedSibling", "Generated DIL is saved next to original and never used as input", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main():\n    return 91\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(result.Success, "compiled");
                    t.Equal(0, GeneratedDILCache.Write(result.Program, root).Count, "cache warnings");
                    string path = Path.Combine(root, "main.dil");
                    t.Check(File.Exists(path), "generated sibling exists");
                    string dil = File.ReadAllText(path);
                    t.Check(dil.StartsWith(GeneratedDILCache.Marker, StringComparison.Ordinal), "own cache marker");
                    t.Contains("DEBUG_FILE \"main.dscript\"", dil, "source debug metadata");
                    t.Contains("# FUNCTION : main", dil, "function present");
                    t.Equal(91L, RunInt(t, result), "VM used in-memory CodeContext");
                    t.Equal(1, storage.EnumerateScripts().Count(), "generated .dil stays hidden");
                }));

            Add("NeverOverwriteHandwrittenDIL", "Old manually written DIL is preserved on collision", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main():\n    return 5\n");
                    string path = Path.Combine(root, "main.dil");
                    File.WriteAllText(path, "// I wrote this by hand\nPUSH INT 5\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(result.Success, "compiled program available");
                    t.Equal(1, GeneratedDILCache.Write(result.Program, root).Count, "one collision warning");
                    t.Contains("I wrote this by hand", File.ReadAllText(path), "handwritten source remains");
                    t.Equal(5L, RunInt(t, result), "run independent of cached file");
                }));

            Add("GeneratedDILRefresh", "Recompiling replaces owned cache after source changes", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main():\n    return 2\n");
                    var provider = Provider(root, storage, workspace);
                    var first = DScriptEditorRunPipeline.Prepare("main", "main", provider);
                    GeneratedDILCache.Write(first.Program, root);
                    string path = Path.Combine(root, "main.dil");
                    string before = File.ReadAllText(path);
                    workspace.Open("main.dscript");
                    workspace.UpdateActiveWorkingContent("def main():\n    return 23\n");
                    var second = DScriptEditorRunPipeline.Prepare("main", "main", provider);
                    GeneratedDILCache.Write(second.Program, root);
                    string after = File.ReadAllText(path);
                    t.Check(!string.Equals(before, after, StringComparison.Ordinal), "DIL cache refreshed");
                    t.Equal(23L, RunInt(t, second), "new program executed");
                    t.Contains("return 2", storage.ReadSaved("main.dscript"), "working copy not saved");
                }));

            Add("CacheRemovedOnRename", "Renaming a DScript file removes only its owned old DIL", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "old", "def main():\n    return 1\n");
                    var prepared = DScriptEditorRunPipeline.Prepare("old", "main", Provider(root, storage, workspace));
                    GeneratedDILCache.Write(prepared.Program, root);
                    t.Check(File.Exists(Path.Combine(root, "old.dil")), "old cache present");
                    t.Equal("new_name.dscript", storage.RenameScript("old.dscript", "new_name"), "renamed source");
                    t.Check(!File.Exists(Path.Combine(root, "old.dil")), "old owned cache removed");
                    t.Check(File.Exists(Path.Combine(root, "new_name.dscript")), "renamed source still present");
                }));

            Add("ManualDILSurvivesRename", "Removing a source never removes a manual .dil sibling", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main", "def main():\n    return 1\n");
                    string oldDIL = Path.Combine(root, "main.dil");
                    File.WriteAllText(oldDIL, "// user file\n");
                    storage.RenameScript("main.dscript", "renamed");
                    t.Equal("// user file\n", File.ReadAllText(oldDIL), "manual cache never deleted");
                }));

            Add("SourceDebugPaths", "VM debug points to original .dscript, not generated DIL", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateFolder("", "utils");
                    storage.CreateScript("utils", "unit", "def main():\n    return 21\n");
                    var result = DScriptEditorRunPipeline.Prepare("utils.unit", "main", Provider(root, storage, workspace));
                    t.Check(result.Success, "valid nested module");
                    for (int i = 0; i < 10 && result.VM.Debug.Line < 0 &&
                        result.VM.Status == VMExecutionStatus.Running; i++)
                        result.VM.Advance();
                    t.Equal("utils/unit.dscript", result.VM.Debug.File, "original file path");
                    t.Equal(1, result.VM.Debug.Line, "source line is zero based");
                }));

            Add("ValidModuleNames", "Editor rejects names that cannot map to DScript modules", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    bool badFile = false, badFolder = false;
                    try { storage.CreateScript("", "bad name", ""); }
                    catch (DirectivaLocalizedException) { badFile = true; }
                    try { storage.CreateFolder("", "bad.folder"); }
                    catch (DirectivaLocalizedException) { badFolder = true; }
                    t.Check(badFile && badFolder, "invalid module segments are refused");
                    t.Check(!storage.EnumerateScripts().Any(), "no invalid files created");
                }));

            Add("CanonicalExtension", "New scripts use canonical lowercase .dscript extension", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    string path = storage.CreateScript("", "Demo.DSCRIPT", "def main():\n    return 3\n");
                    t.Equal("Demo.dscript", path, "normalized extension");
                    var run = DScriptEditorRunPipeline.Prepare("Demo", "main", Provider(root, storage, workspace));
                    t.Equal(3L, RunInt(t, run), "script executable through canonical path");
                }));

            Add("BreakpointOneHitPerSourceLine", "One DScript line is not hit for every DIL instruction", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main",
                        "def main():\n    a = 1 + 2 + 3\n    b = a * 4\n    return b\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(result.Success, "compiled");
                    var gate = new DScriptLineBreakpointGate();
                    var points = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
                    { ["main.dscript"] = new HashSet<int> { 2 } };
                    int hits = 0;
                    for (int i = 0; i < 5000 && result.VM.Status == VMExecutionStatus.Running; i++)
                    {
                        if (gate.ShouldPause(result.VM, points)) hits++;
                        result.VM.Advance();
                    }
                    t.Equal(VMExecutionStatus.Completed, result.VM.Status, "program completed");
                    t.Equal(1, hits, "single hit for the source line");
                }));

            Add("BreakpointRearmsForLoop", "A source breakpoint can hit again on a later loop iteration", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "main",
                        "def main():\n    i = 0\n    while i < 3:\n        i += 1\n    return i\n");
                    var result = DScriptEditorRunPipeline.Prepare("main", "main", Provider(root, storage, workspace));
                    t.Check(result.Success, "compiled");
                    var gate = new DScriptLineBreakpointGate();
                    var points = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
                    { ["main.dscript"] = new HashSet<int> { 4 } };
                    int hits = 0;
                    for (int i = 0; i < 5000 && result.VM.Status == VMExecutionStatus.Running; i++)
                    {
                        if (gate.ShouldPause(result.VM, points)) hits++;
                        result.VM.Advance();
                    }
                    t.Equal(VMExecutionStatus.Completed, result.VM.Status, "loop finished");
                    t.Equal(3, hits, "breakpoint reached on each iteration");
                }));

            Add("ExactCaseModules", "Workspace provider preserves case-sensitive module loading", t =>
                WithWorkspace((root, storage, workspace) =>
                {
                    storage.CreateScript("", "TestCase", "def main():\n    return 1\n");
                    var provider = Provider(root, storage, workspace);
                    t.Check(provider.TryReadModule("TestCase", out _), "exact case found");
                    t.Check(!provider.TryReadModule("testcase", out _), "incorrect case rejected");
                }));
        }
    }
}
