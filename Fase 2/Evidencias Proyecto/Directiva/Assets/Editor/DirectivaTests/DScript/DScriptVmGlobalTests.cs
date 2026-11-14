using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;
using DSCompilation.DScript.Ast;
using Directiva.CodeUI;
using DSExecution.DataTypes;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DScript
{
    /// <summary>Regressions for persistent, VM-private DScript globals.</summary>
    internal static class DScriptVmGlobalTests
    {
        private static DScriptCompiledProgram Compile(DirectivaTestContext t, string main,
            Dictionary<string, string> more = null)
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["test"] = main };
            if (more != null)
                foreach (var extra in more) sources.Add(extra.Key, extra.Value);
            foreach (var source in sources) t.Attach(source.Key + ".dscript", source.Value);
            var result = DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(sources));
            t.Check(result.Success, "global compile: " + string.Join(" | ", result.Diagnostics));
            return result.Program;
        }

        private static DirectivaVM Create(DirectivaTestContext t, DScriptCompiledProgram code,
            VMMemoryContext memory, string function = "test.main")
        {
            t.Check(code.TryGetFunctionId(function, out int id), "entry " + function);
            return new DirectivaVM(code.Code, memory, null, id);
        }

        private static long RunInt(DirectivaTestContext t, DirectivaVM vm)
        {
            var result = vm.Run(100000);
            t.Equal(VMExecutionStatus.Completed, result.Status, "execution: " + result.Error?.Message);
            t.Equal(DataTypeId.Int, vm.ReturnValue.dataType, "integer return");
            return unchecked((long)vm.ReturnValue.value);
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "VmGlobals", id, description, action);

            Add("DistinctSlots", "Shared and private globals have different identities and slot spaces", t =>
            {
                var result = DScriptTestHarness.Ok(t,
                    "shared total = 1\nglobal target: Int = 2\ndef main():\n    return total + target\n");
                t.Equal(1, result.GlobalSlots.Count, "one team slot");
                t.Equal(1, result.VmGlobalSlots.Count, "one VM slot");
                t.Equal(SymbolKind.Global, result.ModuleSymbols["test"].Globals["total"].Kind, "shared kind");
                t.Equal(SymbolKind.VmGlobal, result.ModuleSymbols["test"].Globals["target"].Kind, "private kind");
                t.Equal(0, result.VmGlobalSlots[0].Slot, "private index");
            });
            Add("VmInitializerTrampoline", "Generated VM-global initializers use valid DIL function references", t =>
            {
                const string source =
                    "import lib\nshared offset = 2\nglobal own: Int = lib.base + offset\n" +
                    "def main():\n    return own + lib.base\n";
                var program = Compile(t, source, new Dictionary<string, string>
                    { ["lib"] = "global base: Int = 7\n" });
                string bootstrap = program.DILModules[program.BootstrapModule];
                t.Check(bootstrap.Contains("PUSH_FUNCTION " + program.BootstrapModule + ".__vinit_00000"),
                    "initializer name resolves to a function reference");
                t.Check(bootstrap.Contains("CALL_INDIRECT 0\nPOP"),
                    "trampoline invokes each initializer without arguments and discards its result");
                t.Check(!bootstrap.Contains("CALL " + program.BootstrapModule + ".__vinit_"),
                    "the CALL opcode must never receive a nonnumeric function name");

                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var report = DScriptBootstrapRunner.Initialize(program, memory);
                t.Check(!report.HasErrors, "existing shared bootstrap remains valid");
                t.Equal(16L, RunInt(t, Create(t, program, memory)),
                    "imported private initializer runs before dependent private initializer");
                t.Equal(16L, RunInt(t, Create(t, program, memory)),
                    "each new VM executes its own private initializers");
            });
            Add("IndependentAcrossVMs", "Two VMs executing the same script never share global assignments", t =>
            {
                var program = Compile(t, "global counter = 0\ndef main():\n    counter += 1\n    return counter\n");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var first = Create(t, program, memory);
                var second = Create(t, program, memory);
                t.Equal(1L, RunInt(t, first), "first VM");
                t.Equal(1L, RunInt(t, second), "second VM");
                first.RestartCompleted();
                t.Equal(2L, RunInt(t, first), "first VM persists across calls");
                second.RestartCompleted();
                t.Equal(2L, RunInt(t, second), "second VM persists independently");
                t.Equal(1L, RunInt(t, Create(t, program, memory)), "new VM starts fresh");
            });
            Add("CrossFunctionPersistence", "A helper function updates a private global read by its entry function", t =>
            {
                var program = Compile(t,
                    "global target_resource = None\ndef main():\n    select_resource()\n    return target_resource\ndef select_resource():\n    if not target_resource:\n        target_resource = 42\n");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var vm = Create(t, program, memory);
                t.Equal(42L, RunInt(t, vm), "helper assignment updates the VM global");
                vm.RestartCompleted();
                t.Equal(42L, RunInt(t, vm), "helper sees the persisted value on the next invocation");
                t.Equal(42L, RunInt(t, Create(t, program, memory)), "a fresh VM initializes separately");
            });
            Add("SharedUnchanged", "Shared data remains team-wide while global data is VM-specific", t =>
            {
                var program = Compile(t, "shared team = 0\nglobal local = 0\ndef main():\n    team += 1\n    local += 1\n    return team * 100 + local\n");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var report = DScriptBootstrapRunner.Initialize(program, memory);
                t.Check(!report.HasErrors, "bootstrap");
                var one = Create(t, program, memory);
                var two = Create(t, program, memory);
                t.Equal(101L, RunInt(t, one), "first VM");
                t.Equal(201L, RunInt(t, two), "second VM sees team state");
                one.RestartCompleted();
                t.Equal(302L, RunInt(t, one), "shared + individual private update");
            });
            Add("SurvivesFaultRecovery", "Recovering a faulted VM does not reset its persistent globals", t =>
            {
                var program = Compile(t,
                    "global attempts = 0\ndef main():\n    attempts += 1\n    return 1 / 0\n");
                var vm = Create(t, program, new VMMemoryContext(program.Code.GlobalCount));
                t.Equal(VMExecutionStatus.Faulted, vm.Run(100000).Status, "first run fails");
                t.Equal(1L, unchecked((long)vm.GetVmGlobal(0).value), "first attempt remains stored");
                vm.RestartFaulted();
                t.Equal(VMExecutionStatus.Faulted, vm.Run(100000).Status, "second run fails");
                t.Equal(2L, unchecked((long)vm.GetVmGlobal(0).value), "private state survived recovery");
            });
            Add("PrivateMutableCollection", "A private list persists and is not aliased between VMs", t =>
            {
                var program = Compile(t, "global items = []\ndef main():\n    items.append(8)\n    return items.length\n");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var one = Create(t, program, memory);
                var two = Create(t, program, memory);
                t.Equal(1L, RunInt(t, one), "first list");
                t.Equal(1L, RunInt(t, two), "independent second list");
                one.RestartCompleted();
                t.Equal(2L, RunInt(t, one), "list survives function calls");
            });
            Add("GarbageCollectionRoots", "Private values remain GC roots between VM invocations", t =>
            {
                var program = Compile(t, "global items = [1, 2]\ndef main():\n    return 7\n");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var vm = Create(t, program, memory);
                t.Equal(7L, RunInt(t, vm), "main return");
                t.Check(vm.GetVmGlobal(0).TryGetHeapReference(out uint reference), "list is a heap value");
                memory.CollectGarbage();
                t.Check(memory.TryGetHeap(reference, out _), "list preserved after completed VM GC");
            });
            Add("ImportedPrivateValue", "Imported module globals remain private to each VM", t =>
            {
                var program = Compile(t, "import lib\nglobal add = lib.start + 3\ndef main():\n    return add + lib.start\n",
                    new Dictionary<string, string> { ["lib"] = "global start = 9\n" });
                t.Equal(2, program.VmGlobalSlots.Count, "private slots from both modules");
                t.Equal(21L, RunInt(t, Create(t, program,
                    new VMMemoryContext(program.Code.GlobalCount))), "import initialization order");
            });
            Add("SameNameDifferentModules", "Private globals from distinct modules never merge by name", t =>
            {
                var program = Compile(t, "import left\nimport right\ndef main():\n    return left.x * 10 + right.x\n",
                    new Dictionary<string, string> { ["left"] = "global x = 1\n", ["right"] = "global x = 2\n" });
                t.Equal(2, program.VmGlobalSlots.Count, "independent module-private slots");
                t.Equal(12L, RunInt(t, Create(t, program,
                    new VMMemoryContext(program.Code.GlobalCount))), "both independent names");
            });
            Add("NoPrivateBootstrap", "Shared bootstrap does not execute private initializers", t =>
            {
                var program = Compile(t,
                    "shared counter = 0\nglobal mine = touch()\ndef touch():\n    counter += 1\n    return 5\ndef main():\n    return counter\n");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                var report = DScriptBootstrapRunner.Initialize(program, memory);
                t.Check(!report.HasErrors, "shared initialized");
                t.Equal(0L, unchecked((long)memory.GetGlobal(0).value), "private init not run in bootstrap");
                t.Equal(1L, RunInt(t, Create(t, program, memory)), "private init runs once on VM start");
                t.Equal(2L, RunInt(t, Create(t, program, memory)), "new VM initializes again");
            });
            Add("NoInitializer", "Uninitialized private slot accepts assignment inside a function", t =>
            {
                var program = Compile(t, "global target\ndef main():\n    target = 17\n    return target\n");
                t.Equal(0, program.Code.GlobalCount, "no shared slots");
                t.Equal(1, program.Code.VmGlobalCount, "private reserved slot");
                t.Equal(-1, program.Code.VmInitializerFunctionId, "nothing to execute at startup");
                t.Equal(17L, RunInt(t, Create(t, program,
                    new VMMemoryContext(program.Code.GlobalCount))), "assigned local private slot");
            });
            Add("GlobalSyntaxScope", "Global cannot be declared inside function suites", t =>
                DScriptTestHarness.Error(t, "def main():\n    global x = 1\n", "DS2023"));
            Add("GlobalSharedNameCollision", "One module cannot give private and shared variables one name", t =>
                DScriptTestHarness.Error(t, "global x = 1\nshared x = 2\n", "DS4002"));
            Add("TypedGlobalCallback", "VM-global Function annotations guide completion without calls", t =>
            {
                const string source = "global callback: Function = uni\ndef unit():\n    pass\n";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = source });
                int caret = source.IndexOf("uni\n", StringComparison.Ordinal) + 3;
                var suggestion = DScriptCompletionEngine.Complete("test", source, caret, provider)
                    .Items.First(item => item.Name == "unit");
                t.Check(!suggestion.InsertParentheses, "typed private Function accepts a reference");
                const string completed = "global callback: Function = unit\ndef unit():\n    pass\n";
                t.Check(DScriptCompiler.Compile("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] = completed })).Success,
                    "private Function annotation has no runtime type enforcement");
            });
            Add("ImportedAliasTypeHint", "Private globals imported with an alias retain the declared type", t =>
            {
                const string source = "from lib import flag as alias\ndef main():\n    return alias\n";
                var sources = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["test"] = source, ["lib"] = "global flag: Bool = True\n" };
                var analysis = DScriptFrontend.Analyze("test",
                    new InMemoryDScriptSourceProvider(sources));
                t.Check(analysis.Success, "typed import resolves");
                var entry = analysis.Modules["test"].Statements.OfType<FunctionStatement>().Single();
                var expression = entry.Body.OfType<ReturnStatement>().Single().Value;
                t.Equal("Bool", DScriptCompletionEngine.InferKnownType(analysis, "test", entry,
                    expression, expression.Span.Start), "import alias retains canonical type");
            });
            Add("TypedPrivateNonCallable", "Typed Bool private globals warn on calls but dynamic ones do not", t =>
            {
                const string typed = "global active: Bool = False\ndef main():\n    active()\n";
                const string dynamic = "global active = False\ndef main():\n    active()\n";
                t.Check(DScriptLiveDiagnostics.Analyze("test", typed,
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string> { ["test"] = typed }))
                    .Any(issue => issue.Code == "DS6203"), "typed private global is not callable");
                t.Check(!DScriptLiveDiagnostics.Analyze("test", dynamic,
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string> { ["test"] = dynamic }))
                    .Any(issue => issue.Code == "DS6203"), "untyped private global remains dynamic");
            });
            Add("GlobalAndSharedReverseCollision", "The shared/global name conflict is diagnosed in either order", t =>
                DScriptTestHarness.Error(t, "shared x = 1\nglobal x = 2\n", "DS4002"));
            Add("RootCompletion", "Global appears among the valid top-level declarations", t =>
            {
                const string source = "glo";
                var result = DScriptCompletionEngine.Complete("test", source, source.Length,
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string> { ["test"] = source }));
                t.Check(result.Items.Any(item => item.Name == "global"), "global top-level keyword");
            });
            Add("UnchangedSharedOnly", "Old shared-only programs allocate no VM globals", t =>
            {
                var program = Compile(t, "shared count = 7\ndef main():\n    return count\n");
                t.Equal(0, program.Code.VmGlobalCount, "no new VM slots");
                t.Equal(-1, program.Code.VmInitializerFunctionId, "no startup wrapper");
                var memory = new VMMemoryContext(program.Code.GlobalCount);
                DScriptBootstrapRunner.Initialize(program, memory);
                t.Equal(7L, RunInt(t, Create(t, program, memory)), "original runtime behavior");
            });
        }
    }
}
