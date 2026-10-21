using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Function identity, global action dispatch, determinism and shared VM memory.</summary>
    internal static class DILFunctionConcurrencyTests
    {
        private static CodeContext SharedProgram(DirectivaTestContext t)
        {
            string source = "# GLOBAL : action\n" +
                DILFunctionTests.Fn("initialize", "PUSH_FUNCTION miner\nSTORE_GLOBAL $action\nPUSH NONE\nRETURN") +
                DILFunctionTests.Fn("miner", "PUSH INT 10\nRETURN") +
                DILFunctionTests.Fn("attacker", "PUSH INT 90\nRETURN") +
                DILFunctionTests.Fn("switch", "PUSH_FUNCTION attacker\nSTORE_GLOBAL $action\nPUSH NONE\nRETURN") +
                DILFunctionTests.Fn("bot", "LOAD_GLOBAL $action\nCALL_INDIRECT 0\nRETURN") +
                DILFunctionTests.Fn("read", "LOAD_GLOBAL $action\nRETURN");
            return DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                ["test"] = source
            });
        }

        private static DirectivaVM VM(CodeContext code, VMMemoryContext memory, string name)
            => DILTestHarness.MakeVM(code, memory: memory,
                entryFunctionId: DILFunctionTests.Id(code, name));

        private static void CheckInt(DirectivaTestContext t, DirectivaVM vm, long value)
        {
            var r = DILTestHarness.Run(t, vm);
            t.Equal(VMExecutionStatus.Completed, r.Status, "completed: " + r.Error?.Message);
            t.Equal(DataTypeId.Int, r.Value.dataType, "result type");
            t.Equal(value, unchecked((long)r.Value.value), "result value");
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> test)
                => DirectivaTestCatalog.Add(tests, "FunctionConcurrency", id, description, test);

            Add("GlobalBehaviorSwitch", "One VM changes a global Function and bots see it on their next invocation", t =>
            {
                var code = SharedProgram(t);
                var memory = new VMMemoryContext(code.GlobalCount);
                var setup = VM(code, memory, "initialize");
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, setup).Status, "setup");
                CheckInt(t, VM(code, memory, "bot"), 10);
                CheckInt(t, VM(code, memory, "bot"), 10);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, VM(code, memory, "switch")).Status, "switch");
                CheckInt(t, VM(code, memory, "bot"), 90);
                CheckInt(t, VM(code, memory, "bot"), 90);
            });
            Add("ReadSnapshot", "A bot already holding a function value keeps it after global reassignment", t =>
            {
                var code = SharedProgram(t);
                var memory = new VMMemoryContext(code.GlobalCount);
                DILTestHarness.Run(t, VM(code, memory, "initialize"));
                var bot = VM(code, memory, "bot");
                t.Equal(VMExecutionStatus.Running, bot.Run(1).Status, "bot loaded old function");
                DILTestHarness.Run(t, VM(code, memory, "switch"));
                CheckInt(t, bot, 10);
                CheckInt(t, VM(code, memory, "bot"), 90);
            });
            Add("SimultaneousBots", "Several VMs with the same program execute the same Function ID independently", t =>
            {
                var code = SharedProgram(t);
                var memory = new VMMemoryContext(code.GlobalCount);
                DILTestHarness.Run(t, VM(code, memory, "initialize"));
                var a = VM(code, memory, "bot");
                var b = VM(code, memory, "bot");
                for (int step = 0; step < 64 &&
                    (a.Status == VMExecutionStatus.Running || b.Status == VMExecutionStatus.Running); step++)
                {
                    if (a.Status == VMExecutionStatus.Running) a.Advance();
                    if (b.Status == VMExecutionStatus.Running) b.Advance();
                }
                t.Equal(VMExecutionStatus.Completed, a.Status, "A");
                t.Equal(VMExecutionStatus.Completed, b.Status, "B");
                t.Equal(10L, unchecked((long)a.ReturnValue.value), "A result");
                t.Equal(10L, unchecked((long)b.ReturnValue.value), "B result");
            });
            Add("FunctionValueStoredInGlobal", "Function refs are copied without allocation through shared global storage", t =>
            {
                var code = SharedProgram(t);
                var memory = new VMMemoryContext(code.GlobalCount);
                DILTestHarness.Run(t, VM(code, memory, "initialize"));
                var r = DILTestHarness.Run(t, VM(code, memory, "read"));
                t.Equal(VMExecutionStatus.Completed, r.Status, "reader");
                t.Equal(DataTypeId.Function, r.Value.dataType, "global type");
                t.Equal(DILFunctionTests.Id(code, "miner"), (int)r.Value.value, "global function id");
                t.Equal((ulong)0, memory.Heap.AllocatedSize, "no heap allocation");
            });
            Add("DifferentProgramRejected", "Function ids cannot silently alias across distinct programs sharing globals", t =>
            {
                var codeA = SharedProgram(t);
                var codeB = SharedProgram(t); // Same contents, different CodeContext instances.
                var memory = new VMMemoryContext(codeA.GlobalCount);
                DILTestHarness.Run(t, VM(codeA, memory, "initialize"));
                var r = DILTestHarness.Run(t, VM(codeB, memory, "bot"));
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.ForeignFunctionProgram);
            });
            Add("DifferentProgramPushRejected", "Creating a Function ref in a conflicting program is rejected", t =>
            {
                var codeA = SharedProgram(t);
                var codeB = SharedProgram(t);
                var memory = new VMMemoryContext(codeA.GlobalCount);
                DILTestHarness.Run(t, VM(codeA, memory, "initialize"));
                var r = DILTestHarness.Run(t, VM(codeB, memory, "switch"));
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.ForeignFunctionProgram);
            });
            Add("IndependentMemoryContexts", "Same compiled code can execute across isolated global memories", t =>
            {
                var code = SharedProgram(t);
                var memA = new VMMemoryContext(code.GlobalCount);
                var memB = new VMMemoryContext(code.GlobalCount);
                DILTestHarness.Run(t, VM(code, memA, "initialize"));
                DILTestHarness.Run(t, VM(code, memB, "initialize"));
                DILTestHarness.Run(t, VM(code, memA, "switch"));
                CheckInt(t, VM(code, memA, "bot"), 90);
                CheckInt(t, VM(code, memB, "bot"), 10);
            });
            Add("FunctionAsEntryArgument", "Function values may be passed as entry parameters by the host", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string,string> {
                    ["test"] = DILFunctionTests.Fn("default", "PUSH NONE\nRETURN") +
                               DILFunctionTests.Fn("worker", "PUSH INT 55\nRETURN") +
                               DILFunctionTests.Fn("invoke", "LOAD_LOCAL 0\nCALL_INDIRECT 0\nRETURN", 1, 1)
                });
                var mem = new VMMemoryContext(code.GlobalCount);
                var vm = DILTestHarness.MakeVM(code, memory: mem,
                    entryFunctionId: DILFunctionTests.Id(code, "invoke"),
                    args: new[] { DataValue.FromFunctionId(DILFunctionTests.Id(code, "worker")) });
                CheckInt(t, vm, 55);
            });
            Add("GCWithFunctionRefs", "Function refs stored in lists and globals survive GC without heap traversal", t =>
            {
                string src = "# GLOBAL : slots\n" +
                    DILFunctionTests.Fn("setup", "PUSH_FUNCTION worker\nLIST 1\nSTORE_GLOBAL $slots\nPUSH NONE\nRETURN") +
                    DILFunctionTests.Fn("reader", "LOAD_GLOBAL $slots\nPUSH INT 0\nGET\nCALL_INDIRECT 0\nRETURN") +
                    DILFunctionTests.Fn("worker", "PUSH INT 64\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test",
                    new Dictionary<string, string> { ["test"] = src });
                var memory = new VMMemoryContext(code.GlobalCount);
                DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory,
                    entryFunctionId: DILFunctionTests.Id(code, "setup")));
                memory.CollectGarbage();
                CheckInt(t, DILTestHarness.MakeVM(code, memory: memory,
                    entryFunctionId: DILFunctionTests.Id(code, "reader")), 64);
            });
            Add("DeterministicInterleaving", "Identical schedules produce the same shared function-selection result", t =>
            {
                long[] outcomes = new long[3];
                for (int run = 0; run < outcomes.Length; run++)
                {
                    var code = SharedProgram(t);
                    var memory = new VMMemoryContext(code.GlobalCount);
                    DILTestHarness.Run(t, VM(code, memory, "initialize"));
                    var botA = VM(code, memory, "bot");
                    var botB = VM(code, memory, "bot");
                    botA.Advance(); // LOAD_GLOBAL old function.
                    DILTestHarness.Run(t, VM(code, memory, "switch"));
                    var a = DILTestHarness.Run(t, botA);
                    var b = DILTestHarness.Run(t, botB);
                    t.Equal(VMExecutionStatus.Completed, a.Status, "A");
                    t.Equal(VMExecutionStatus.Completed, b.Status, "B");
                    outcomes[run] = unchecked((long)a.Value.value) * 100 + unchecked((long)b.Value.value);
                }
                t.Equal(1090L, outcomes[0], "expected schedule");
                t.Equal(outcomes[0], outcomes[1], "repeat 1");
                t.Equal(outcomes[0], outcomes[2], "repeat 2");
            });
        }
    }
}
