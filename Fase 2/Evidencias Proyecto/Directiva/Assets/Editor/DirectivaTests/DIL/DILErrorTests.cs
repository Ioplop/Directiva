using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>
    /// Error pathways are tested with exact RuntimeErrorId. The DIL parser rejects some
    /// malformed instructions; those VM faults are tested with direct CodeContext fixtures.
    /// InvalidCastValue is currently a reserved factory, not a reachable user-code branch.
    /// </summary>
    internal static class DILErrorTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Errors", id, purpose, action);

            var simple = new[]
            {
                new[] { "DivisionByZero", "PUSH INT 12\nPUSH INT 0\nDIVIDE\nRETURN" },
                new[] { "Overflow", "PUSH INT 9223372036854775807\nPUSH INT 1\nADD\nRETURN" },
                new[] { "InvalidOperation", "PUSH INT 1\nPUSH STRING \"x\"\nADD\nRETURN" },
                new[] { "StackUnderflow", "POP\nPUSH NONE\nRETURN" },
                new[] { "Uninitialized", "LOAD_LOCAL 0\nIS_TRUTHY\nRETURN" },
                new[] { "InvalidInstruction", "PASS" },
                new[] { "InvalidCast", "PUSH STRING \"abc\"\nCAST TUPLE\nRETURN" },
                new[] { "IndexOutOfRange", "PUSH INT 2\nTUPLE 1\nPUSH INT 8\nGET\nRETURN" }
            };
            var ids = new[] {
                RuntimeErrorId.DivisionByZero, RuntimeErrorId.Overflow,
                RuntimeErrorId.InvalidOperation, RuntimeErrorId.StackUnderflow,
                RuntimeErrorId.VariableUsedBeforeInitialization, RuntimeErrorId.InvalidInstruction,
                RuntimeErrorId.InvalidCast, RuntimeErrorId.IndexOutOfRange
            };
            for (int i = 0; i < simple.Length; i++)
            {
                string name = simple[i][0], source = simple[i][1];
                RuntimeErrorId expected = ids[i];
                Add(name, "Runtime must return " + expected + " rather than throw", t =>
                {
                    int locals = expected == RuntimeErrorId.VariableUsedBeforeInitialization ? 1 : 0;
                    var r = DILTestHarness.Evaluate(t, source, locals);
                    DILTestHarness.ExpectFault(t, r, expected);
                });
            }
            Add("StackOverflow", "Evaluation-stack capacity limit produces exact error", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 1\nPUSH INT 2\nRETURN",
                    options: new VMOptions(maxEvaluationStackSize: 1));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.StackOverflow);
            });
            Add("TupleZeroStackCapacity", "TUPLE 0 fails before heap reservation on a full stack", t =>
            {
                var vm = DILTestHarness.MakeVM(t, "PUSH INT 4\nTUPLE 0\nRETURN",
                    options: new VMOptions(maxEvaluationStackSize: 1));
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.StackOverflow);
                t.Equal(0, vm.Memory.Heap.ObjectCount, "failure must not leak tuple allocation");
            });
            Add("TupleUnderflow", "TUPLE count larger than available stack errors", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 1\nTUPLE 2\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add("NonBoolConditional", "JUMP_IF_TRUE refuses Int without IS_TRUTHY", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nJUMP_IF_TRUE end\nend:\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.InvalidOperation));
            Add("OutOfMemory", "Heap exhaustion triggers OutOfMemory rather than host exception", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH STRING \"large payload\"\nRETURN", heap: 1),
                    RuntimeErrorId.OutOfMemory));
            Add("UnhashableType", "Hash unsupported type fails with UnhashableType", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(new DataValue(DataTypeId.List, 1)),
                    Instruction.Operation(OperationId.Hash), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.UnhashableType);
            });
            Add("InvalidMemoryAccess", "Dereferencing a dangling tuple pointer is rejected", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromTupleReference(999)),
                    Instruction.Push(DataValue.FromInt(0)),
                    Instruction.Operation(OperationId.Get), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidMemoryAccess);
            });
            Add("InvalidFunction", "VM traps direct CALL to nonexistent function id", t =>
            {
                var code = DILTestHarness.Direct(t, 0, Instruction.Call(9000), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidFunction);
            });
            Add("InvalidLocalAccess", "VM traps out-of-range local slot (parser would reject)", t =>
            {
                var code = DILTestHarness.Direct(t, 0, Instruction.LoadLocal(-1), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidLocalAccess);
            });
            Add("InvalidGlobalAccess", "VM traps out-of-range global slot (parser would reject)", t =>
            {
                var code = DILTestHarness.Direct(t, 0, Instruction.LoadGlobal(1), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidGlobalAccess);
            });
            Add("InvalidJumpTarget", "VM traps out-of-range control flow jump (parser would reject)", t =>
            {
                var code = DILTestHarness.Direct(t, 0, Instruction.Jump(55));
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidJumpTarget);
            });
            Add("CallStackOverflow", "Recursion beyond configured call depth faults deterministically", t =>
            {
                var code = DILTestHarness.Parse(t, "CALL $test.main\nPUSH NONE\nRETURN");
                var vm = DILTestHarness.MakeVM(code, options: new VMOptions(maxCallDepth: 5));
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.CallStackOverflow);
            });
            Add("InternalVmError", "Malformed operation ID is safely converted into InternalVmError", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromInt(1)),
                    Instruction.Operation((OperationId)32767), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InternalVmError);
            });
            Add("BadContextEnd", "Unmatched CONTEXT_END is rejected at runtime", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "CONTEXT_END\nPUSH NONE\nRETURN"),
                    RuntimeErrorId.InvalidInstruction));
            Add("InvalidDebugSpan", "Negative DEBUG_SPAN operand is rejected", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.DebugSpan(-1, 5), Instruction.Push(DataValue.None()), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidInstruction);
            });
            Add("InvalidCastValueFactory", "Reserved InvalidCastValue error has stable ID and context (factory only)", t =>
            {
                t.Note("This RuntimeErrorId has no reachable DIL path in the current code. Factory contract only.");
                var error = RuntimeError.InvalidCastValue(DataTypeId.String, DataTypeId.Int, "bad text");
                t.Equal(RuntimeErrorId.InvalidCastValue, error.Id, "reserved factory id");
                t.Contains("bad text", error.Message, "diagnostic detail");
                t.Contains("String", error.Message, "source type");
                t.Contains("Int", error.Message, "target type");
            });
            Add("ErrorIdCoverage", "Every declared RuntimeErrorId has an executable test or noted factory-only test", t =>
            {
                var defined = (RuntimeErrorId[])Enum.GetValues(typeof(RuntimeErrorId));
                var covered = new HashSet<RuntimeErrorId> {
                    RuntimeErrorId.DivisionByZero, RuntimeErrorId.Overflow, RuntimeErrorId.InvalidOperation,
                    RuntimeErrorId.InvalidMemoryAccess, RuntimeErrorId.StackOverflow, RuntimeErrorId.StackUnderflow,
                    RuntimeErrorId.VariableUsedBeforeInitialization, RuntimeErrorId.InvalidInstruction,
                    RuntimeErrorId.InvalidFunction, RuntimeErrorId.InvalidLocalAccess, RuntimeErrorId.InvalidGlobalAccess,
                    RuntimeErrorId.InvalidJumpTarget, RuntimeErrorId.CallStackOverflow,
                    RuntimeErrorId.InternalVmError, RuntimeErrorId.OutOfMemory, RuntimeErrorId.InvalidCast,
                    RuntimeErrorId.InvalidCastValue, RuntimeErrorId.IndexOutOfRange, RuntimeErrorId.UnhashableType,
                    RuntimeErrorId.MemberNotFound, RuntimeErrorId.ReadOnlyProperty,
                    RuntimeErrorId.WriteOnlyProperty, RuntimeErrorId.InvalidMemberArgumentCount,
                    RuntimeErrorId.InvalidCallTarget, RuntimeErrorId.InvalidFunctionArgumentCount,
                    RuntimeErrorId.ForeignFunctionProgram,
                    RuntimeErrorId.NotIterable, RuntimeErrorId.InvalidIterator,
                    RuntimeErrorId.IteratorModified
                };
                foreach (var id in defined) t.Check(covered.Contains(id), "Missing test for RuntimeErrorId." + id);
                t.Note("Reserved/factory-only: InvalidCastValue. WriteOnlyProperty is covered by an annotated DataType fixture in the Members group; function-call faults are covered in Functions and FunctionConcurrency; the remaining member faults have DIL fixtures.");
            });
        }
    }
}
