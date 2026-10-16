using System;
using System.Collections.Generic;
using DSCompilation.DIL;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILVMTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "VM", id, purpose, action);

            Add("PushPopReturn", "Stack push/pop order and explicit RETURN", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 8\nPUSH INT 9\nPOP\nRETURN", 8));
            Add("Locals", "Initialize, write, read and overwrite local slots", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 5\nSTORE_LOCAL 0\nPUSH INT 3\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nADD\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nRETURN", locals: 2);
                t.Equal(8L, unchecked((long)r.Value.value), "local slots");
            });
            Add("Globals", "Shared globals: store/reload and persistence through CALL", t =>
            {
                var src = "# GLOBAL : counter\n" +
                    DILTestHarness.Function("PUSH INT 18\nSTORE_GLOBAL $counter\nCALL $test.read\nRETURN") +
                    DILTestHarness.Function("LOAD_GLOBAL $counter\nRETURN", functionName: "read");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "global status");
                t.Equal(18L, unchecked((long)r.Value.value), "global return");
            });
            Add("CallParametersAndNestedCalls", "Correct parameter order, frames and nested RETURN", t =>
            {
                string src = DILTestHarness.Function(
                    "PUSH INT 7\nPUSH INT 12\nCALL $test.subtract\nRETURN") +
                    DILTestHarness.Function(
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nSUBTRACT\nRETURN", 2, 2, "subtract");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "nested frames status");
                t.Equal(-5L, unchecked((long)r.Value.value), "ordered arguments");
            });
            Add("EntryFunctionOverride", "VM starts at selected function, using explicit entry arguments", t =>
            {
                string src = DILTestHarness.Function("PUSH INT 1\nRETURN") +
                    DILTestHarness.Function("LOAD_LOCAL 0\nRETURN", 1, 1, "identity");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> { ["test"] = src });
                int fnId = -1;
                foreach (var fn in code.Functions)
                    if (fn.Name.EndsWith(".identity", StringComparison.Ordinal)) fnId = fn.Id;
                t.Check(fnId >= 0, "Entry override function not found");
                var vm = DILTestHarness.MakeVM(code, entryFunctionId: fnId, args: new[] { DataValue.FromInt(41) });
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "entry override completion");
                t.Equal(41L, unchecked((long)r.Value.value), "entry arg passed through");
            });
            Add("ContextScope", "CONTEXT_START/END resets slots and preserves surrounding locals", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 77\nSTORE_LOCAL 0\nCONTEXT_START 1 1\n" +
                    "PUSH INT 999\nSTORE_LOCAL 1\nCONTEXT_END\nLOAD_LOCAL 0\nRETURN", locals: 2);
                t.Equal(77L, unchecked((long)r.Value.value), "outer local survives scope exit");
            });
            Add("ScopeReinitializes", "Exited context slots cannot be accessed as initialized", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "CONTEXT_START 0 1\nPUSH INT 5\nSTORE_LOCAL 0\nCONTEXT_END\nLOAD_LOCAL 0\nRETURN", 1);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.VariableUsedBeforeInitialization);
            });
            Add("JumpBothDirections", "While-loop implemented with labels and JUMP_IF_TRUE", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 0\nSTORE_LOCAL 0\nagain:\nLOAD_LOCAL 0\nPUSH INT 1\nADD\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 5\nLESS_THAN\nJUMP_IF_TRUE again\nLOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(5L, unchecked((long)r.Value.value), "final loop count");
            });
            Add("ConditionalTrueFalse", "Both conditional branches and value consumption", t =>
            {
                DILTestHarness.ExpectInt(t,
                    "PUSH BOOL TRUE\nJUMP_IF_FALSE no\nPUSH BOOL FALSE\nJUMP_IF_TRUE no\n" +
                    "PUSH INT 10\nJUMP done\nno:\nPUSH INT 20\ndone:\nRETURN", 10);
            });
            Add("IsTruthyBranch", "Explicit truthiness of non-Boolean operands before branching", t =>
            {
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 0\nIS_TRUTHY\nJUMP_IF_FALSE empty\nPUSH INT 1\nRETURN\n" +
                    "empty:\nPUSH INT 2\nRETURN", 2);
            });
            Add("PrintEventOrder", "PRINT emits events in order and consumes its stack argument", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"Alpha\"\nPRINT\nPUSH INT 15\nPRINT\nPUSH BOOL FALSE\nPRINT\n" +
                    "PUSH NONE\nRETURN");
                t.Equal(3, r.Output.Count, "PRINT count");
                t.Equal("Alpha", r.Output[0], "PRINT 0");
                t.Equal("15", r.Output[1], "PRINT 1");
                t.Equal("False", r.Output[2], "PRINT 2");
            });
            Add("RunBudgetAndResume", "Bounded Run leaves VM running and can resume deterministically", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "PUSH INT 1\nPUSH INT 2\nADD\nRETURN");
                var a = vm.Run(2);
                t.Equal(VMExecutionStatus.Running, a.Status, "first budget status");
                t.Check(a.BudgetExhausted, "budget exhaustion flag missing");
                t.Equal(2, a.StepsExecuted, "first budget steps");
                var b = vm.Run(2);
                t.Equal(VMExecutionStatus.Completed, b.Status, "resume status");
                t.Equal(3L, unchecked((long)b.ReturnValue.value), "resume result");
                t.Note("Run(2): " + a.StepsExecuted + " steps; Run(2): " + b.StepsExecuted + " steps");
            });
            Add("InfiniteLoopIsBudgeted", "Infinite loops do not hang the test runner", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "loop:\nPASS\nJUMP loop\nPUSH NONE\nRETURN");
                var r = DILTestHarness.Run(t, vm, 256);
                t.Equal(VMExecutionStatus.Running, r.Status, "loop should keep running");
                t.Check(r.BudgetExhausted, "loop should exhaust budget rather than fault");
                t.Equal(256, r.Steps, "deterministic loop step count");
            });
            Add("CompletedIsIdempotent", "Advancing a completed VM must not execute extra instructions", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 9\nRETURN");
                DILTestHarness.Run(t, vm);
                var again = vm.Advance();
                t.Equal(VMExecutionStatus.Completed, again.Status, "post-completion status");
                t.Equal(9L, unchecked((long)again.ReturnValue.value), "post-completion value");
                t.Equal(0, vm.CallDepth, "call stack empty on completion");
            });
            Add("FaultedIsIdempotent", "Advancing a faulted VM preserves original error", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "POP\nRETURN");
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.StackUnderflow);
                var again = vm.Advance();
                t.Equal(VMExecutionStatus.Faulted, again.Status, "post-fault status");
                t.Equal(RuntimeErrorId.StackUnderflow, again.Error.Id, "preserved error");
            });
            Add("ManualDebugInstructions", "DEBUG_FILE/LINE/SPAN expose 0-based debug info", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.DebugFile("example.dscript"), Instruction.DebugLine(7),
                    Instruction.DebugSpan(18, 27), Instruction.Push(DataValue.None()), Instruction.Return());
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                t.Equal(VMExecutionStatus.Completed, r.Status, "debug instruction run");
                t.Equal("example.dscript", r.VM.Debug.File, "debug file");
                t.Equal(7, r.VM.Debug.Line, "debug line");
                t.Equal(18, r.VM.Debug.SpanStart, "debug span start");
                t.Equal(27, r.VM.Debug.SpanEnd, "debug span end");
            });
            Add("UninitializedStartup", "All new local and global slots start uninitialized", t =>
            {
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# GLOBAL : x\n" + DILTestHarness.Function("LOAD_LOCAL 0\nRETURN", 1)
                });
                var vm = DILTestHarness.MakeVM(code);
                t.Equal(DataTypeId.Uninitialized, vm.CurrentLocals.Get(0).dataType, "local initial value");
                t.Equal(DataTypeId.Uninitialized, vm.Memory.GetGlobal(0).dataType, "global initial value");
            });
            Add("ConstructorParameterValidation", "VM refuses incorrect entry argument count", t =>
            {
                var code = DILTestHarness.Parse(t, "LOAD_LOCAL 0\nRETURN", locals: 1, parameters: 1);
                try { DILTestHarness.MakeVM(code); }
                catch (ArgumentException ex) { t.Note("Expected constructor rejection: " + ex.Message); return; }
                throw new DirectivaTestAssertionException("VM accepted missing entry parameter");
            });
        }
    }
}
