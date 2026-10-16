using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILReferenceTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "References", id, purpose, action);

            Add("StringLengthUnicode", "Length counts Unicode code points; heap size counts UTF-16 units", t =>
            {
                DILTestHarness.ExpectInt(t, "PUSH STRING \"A\\uD83D\\uDC0C\"\nLENGTH\nRETURN", 2);
                t.Equal(2u, new StringHeapObject("🐌").Size, "UTF-16 accounting");
            });
            Add("StringConcat", "String ADD allocates a separate immutable string", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"Ab\"\nPUSH STRING \"Cd\"\nADD\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("AbCd", r.Output[0], "string concat output");
            });
            Add("StringHashStable", "String hash is deterministic across isolated heaps", t =>
            {
                var a = DILTestHarness.ExpectCompleted(t, "PUSH STRING \"same🐌\"\nHASH\nRETURN");
                var b = DILTestHarness.ExpectCompleted(t, "PUSH STRING \"same🐌\"\nHASH\nRETURN");
                t.Equal(DataTypeId.Int, a.Value.dataType, "hash type");
                t.Equal(a.Value.value, b.Value.value, "stable string hash");
            });
            Add("StringOrdinalEquality", "String equality compares text, not allocation identity", t =>
            {
                DILTestHarness.ExpectBool(t,
                    "PUSH STRING \"Abc\"\nPUSH STRING \"Abc\"\nVALUE_EQUALS\nRETURN", true);
                DILTestHarness.ExpectBool(t,
                    "PUSH STRING \"Abc\"\nPUSH STRING \"abc\"\nVALUE_EQUALS\nRETURN", false);
            });
            Add("EmptyTuple", "TUPLE 0 consumes no input and creates empty tuple", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "TUPLE 0\nRETURN");
                t.Equal(DataTypeId.Tuple, r.Value.dataType, "tuple type");
                t.Equal(1UL, r.VM.Memory.Heap.AllocatedSize, "empty tuple logical heap units");
            });
            Add("TupleStackOrdering", "TUPLE 3 consumes only top three of five values", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nPUSH INT 4\nPUSH INT 5\n" +
                    "TUPLE 3\nSTORE_LOCAL 0\nPOP\nPOP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(DataTypeId.Tuple, r.Value.dataType, "tuple result");
                uint reference = (uint)r.Value.value;
                var obj = (TupleHeapObject)r.VM.Memory.GetHeap(reference);
                t.Equal(3, obj.Count, "element count");
                t.Equal(3L, unchecked((long)obj.GetElement(0).value), "first element");
                t.Equal(5L, unchecked((long)obj.GetElement(2).value), "third element");
                t.Equal(4u, obj.Size, "logical tuple size");
            });
            Add("NestedIndexAndNegative", "Indexing nested tuples and negative indices", t =>
            {
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 15\nPUSH INT 28\nTUPLE 2\nPUSH BOOL TRUE\nTUPLE 2\n" +
                    "PUSH INT 0\nGET\nPUSH INT -1\nGET\nRETURN", 28);
            });
            Add("IndexBoolAsInt", "Boolean index uses integer compatibility", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 11\nPUSH INT 22\nTUPLE 2\nPUSH BOOL TRUE\nGET\nRETURN", 22));
            Add("TupleValueEquality", "Two different objects may be equal by content", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH STRING \"x\"\nTUPLE 2\n" +
                    "PUSH INT 1\nPUSH STRING \"x\"\nTUPLE 2\nVALUE_EQUALS\nRETURN", true));
            Add("TupleIdentityAndCopy", "COPY preserves immutable heap reference identity", t =>
            {
                var result = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 4\nTUPLE 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCOPY\n" +
                    "LOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", locals: 1);
                t.Equal(DataTypeId.Bool, result.Value.dataType, "COPY identity result type");
                t.Equal(1UL, result.Value.value, "COPY preserves tuple reference identity");
            });
            Add("TupleIdentityDistinct", "Two separately allocated tuples differ in identity", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 4\nTUPLE 1\nPUSH INT 4\nTUPLE 1\nREFERENCE_EQUALS\nRETURN", false));
            Add("TupleHashByValue", "Equal tuples with Int/Bool/Decimal have equal hashes", t =>
            {
                var a = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH BOOL TRUE\nTUPLE 2\nHASH\nRETURN");
                var b = DILTestHarness.ExpectCompleted(t,
                    "PUSH DECIMAL 1.0\nPUSH INT 1\nTUPLE 2\nHASH\nRETURN");
                t.Equal(a.Value.value, b.Value.value, "equivalent tuple hashes");
            });
            Add("TupleConcat", "Tuple ADD returns a new object with ordered elements", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nTUPLE 1\nPUSH INT 2\nPUSH INT 3\nTUPLE 2\nADD\nCAST STRING\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("(1, 2, 3)", r.Output[0], "tuple concatenation");
            });
            Add("TupleStringFormatting", "Quote strings, nested tuples, escapes, None and singleton", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"A\\\"B\"\nPUSH INT 3\nTUPLE 2\n" +
                    "PUSH NONE\nTUPLE 2\nCAST STRING\nPRINT\n" +
                    "TUPLE 0\nPRINT\nPUSH INT 9\nTUPLE 1\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("((\"A\\\"B\", 3), None)", r.Output[0], "nested representation");
                t.Equal("()", r.Output[1], "empty tuple repr");
                t.Equal("(9,)", r.Output[2], "singleton repr");
            });
            Add("TupleLengthAndTruth", "Length and truthiness of empty/nonempty tuples", t =>
            {
                DILTestHarness.ExpectInt(t, "PUSH INT 3\nPUSH NONE\nTUPLE 2\nLENGTH\nRETURN", 2);
                DILTestHarness.ExpectBool(t, "TUPLE 0\nIS_TRUTHY\nRETURN", false);
                DILTestHarness.ExpectBool(t, "PUSH NONE\nTUPLE 1\nIS_TRUTHY\nRETURN", true);
            });
            Add("TupleDoesNotMutateCallerArray", "TupleHeapObject clones its input to preserve immutability", t =>
            {
                var values = new[] { DataValue.FromInt(2), DataValue.FromInt(3) };
                var tuple = new TupleHeapObject(values);
                values[0] = DataValue.FromInt(777);
                t.Equal(2L, unchecked((long)tuple.GetElement(0).value), "immutable defensive copy");
            });
            Add("TupleUnhashablePath", "Unhashable nested element includes zero-based location", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromInt(1)),
                    Instruction.Push(new DataValue(DataTypeId.List, 1)),
                    Instruction.Tuple(2), Instruction.Tuple(1),
                    Instruction.Operation(DSExecution.Operations.OperationId.Hash), Instruction.Return());
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code));
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.UnhashableType);
                t.Contains("[0][1]", r.Error.Message, "unhashable path");
            });
        }
    }
}
