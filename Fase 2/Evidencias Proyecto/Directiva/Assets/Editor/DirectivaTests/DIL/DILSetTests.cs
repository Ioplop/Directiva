using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>End-to-end DIL set construction, uniqueness, mutation, formatting and errors.</summary>
    internal static class DILSetTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Sets", id, why, action);

            Add("Empty", "SET 0 creates one empty heap object", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "SET 0\nRETURN");
                t.Equal(DataTypeId.Set, r.Value.dataType, "returned type");
                var set = (SetHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(0, set.Count, "member count");
                t.Equal(1u, set.Size, "logical size");
            });
            Add("UniqueValues", "SET x discards duplicate members and preserves distinct values", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 1\nPUSH INT 3\nSET 4\nRETURN");
                var set = (SetHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(3, set.Count, "unique members");
                t.Equal(4UL, r.VM.Memory.Heap.AllocatedSize, "unique charge only");
            });
            Add("NumBoolDuplicate", "True and integer one are one unique set member", t =>
                DILTestHarness.ExpectInt(t, "PUSH INT 1\nPUSH BOOL TRUE\nSET 2\nLENGTH\nRETURN", 1));
            Add("NumDecimalDuplicate", "Numerically equal Decimal and Int share a set entry", t =>
                DILTestHarness.ExpectInt(t, "PUSH DECIMAL 2.0\nPUSH INT 2\nSET 2\nLENGTH\nRETURN", 1));
            Add("ZeroNumerics", "Zero, False and zero Decimal collapse", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 0\nPUSH BOOL FALSE\nPUSH DECIMAL 0.0\nSET 3\nLENGTH\nRETURN", 1));
            Add("StringValueDeduplication", "Different heap strings with equal content collapse", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH STRING \"hello\"\nPUSH STRING \"hello\"\nSET 2\nLENGTH\nRETURN", 1));
            Add("TupleValueDeduplication", "Distinct but equal tuples share a set entry", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH STRING \"a\"\nTUPLE 2\n" +
                    "PUSH BOOL TRUE\nPUSH STRING \"a\"\nTUPLE 2\nSET 2\nLENGTH\nRETURN", 1));
            Add("NestedTupleDeduplication", "Nested immutable tuples can be set members", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nTUPLE 1\nTUPLE 1\nPUSH BOOL TRUE\nTUPLE 1\nTUPLE 1\nSET 2\nLENGTH\nRETURN", 1));
            Add("DifferentTypesDistinct", "Numeric and text values are distinct", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 1\nPUSH STRING \"1\"\nSET 2\nLENGTH\nRETURN", 2));
            Add("NoneAsMember", "None has a stable hash and can be a member", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH NONE\nSET 1\nPUSH NONE\nCONTAINS\nRETURN", true));
            Add("NegativeInt", "Negative integer keys work", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT -2147\nSET 1\nPUSH INT -2147\nCONTAINS\nRETURN", true));
            Add("EmptyTruthiness", "Empty set is falsy", t =>
                DILTestHarness.ExpectBool(t, "SET 0\nIS_TRUTHY\nRETURN", false));
            Add("NonEmptyTruthiness", "Nonempty set is truthy even for false members", t =>
                DILTestHarness.ExpectBool(t, "PUSH BOOL FALSE\nSET 1\nIS_TRUTHY\nRETURN", true));
            Add("Length", "LENGTH counts unique members", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 3\nPUSH INT 3\nPUSH INT 4\nSET 3\nLENGTH\nRETURN", 2));
            Add("ContainsFound", "CONTAINS uses hashed value equality", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 3\nPUSH INT 8\nSET 2\nPUSH INT 8\nCONTAINS\nRETURN", true));
            Add("ContainsNotFound", "Missing member returns false", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 3\nPUSH INT 8\nSET 2\nPUSH INT 9\nCONTAINS\nRETURN", false));
            Add("ContainsCrossNumeric", "CONTAINS respects Bool/Int/Decimal equality", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH BOOL TRUE\nSET 1\nPUSH DECIMAL 1.0\nCONTAINS\nRETURN", true));
            Add("SetAddNew", "SET_ADD changes set in place", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "SET 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 42\nSET_ADD\nPOP\n" +
                    "LOAD_LOCAL 0\nRETURN", locals: 1);
                var set = (SetHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(1, set.Count, "added member");
                t.Equal(42L, unchecked((long)set.GetElement(0).value), "added value");
                t.Equal(2UL, r.VM.Memory.Heap.AllocatedSize, "resize charged");
            });
            Add("SetAddDuplicate", "Adding an equal existing member changes neither size nor version", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH BOOL TRUE\nSET_ADD\n" +
                    "POP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                var set = (SetHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(1, set.Count, "unchanged unique count");
                t.Equal(2UL, r.VM.Memory.Heap.AllocatedSize, "unchanged heap charge");
            });
            Add("SetAddAliasing", "Mutation visible through a second reference to same set", t =>
                DILTestHarness.ExpectBool(t,
                    "SET 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nPUSH INT 7\nSET_ADD\nPOP\n" +
                    "LOAD_LOCAL 1\nPUSH INT 7\nCONTAINS\nRETURN", true, locals: 2));
            Add("SetAddReturnsNone", "SET_ADD return is None", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "SET 0\nPUSH INT 7\nSET_ADD\nRETURN");
                t.Equal(DataTypeId.None, r.Value.dataType, "mutation result");
            });
            Add("SetDiscardExisting", "Discard deletes a member", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nPUSH INT 2\nSET 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nSET_DISCARD\nPOP\n" +
                    "LOAD_LOCAL 0\nLENGTH\nRETURN", 1));
            Add("SetDiscardMissing", "Discard missing member is a no-op", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 4\nSET 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nSET_DISCARD\nPOP\n" +
                    "LOAD_LOCAL 0\nLENGTH\nRETURN", 1));
            Add("SetRemoveExisting", "SET_REMOVE deletes a present member", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 4\nSET 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 4\nSET_REMOVE\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 4\nCONTAINS\nRETURN", false, locals: 1));
            Add("SetRemoveMissing", "SET_REMOVE missing member reports a script error", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nPUSH INT 3\nSET_REMOVE\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("SetPopLast", "SET_POP deterministically returns the last stored value", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 5\nPUSH INT 7\nSET 2\nSET_POP\nRETURN", 7));
            Add("SetPopEmpty", "Popping from empty set is an error", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nSET_POP\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("CopyDifferentIdentity", "COPY is shallow but creates a distinct Set", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nCOPY\nLOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", false, locals: 1));
            Add("CopyContent", "COPY preserves set members", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 2\nSET 2\nCOPY\nPUSH INT 2\nCONTAINS\nRETURN", true));
            Add("IdentitySame", "REFERENCE_EQUALS detects identical set references", t =>
                DILTestHarness.ExpectBool(t,
                    "SET 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nLOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN",
                    true, locals: 1));
            Add("EqualityNoOrder", "VALUE_EQUALS compares members regardless of insertion order", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 2\nSET 2\nPUSH INT 2\nPUSH INT 1\nSET 2\n" +
                    "VALUE_EQUALS\nRETURN", true));
            Add("EqualityNoIdentity", "Equal sets need not be same heap object", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nSET 1\nPUSH BOOL TRUE\nSET 1\nVALUE_EQUALS\nRETURN", true));
            Add("InequalityDifferentMember", "Different members make sets unequal", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nSET 1\nPUSH INT 2\nSET 1\nVALUE_EQUALS\nRETURN", false));
            Add("InequalityDifferentCount", "Different member counts make sets unequal", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nSET 1\nPUSH INT 1\nPUSH INT 2\nSET 2\nVALUE_EQUALS\nRETURN", false));
            Add("StringEmpty", "Empty set prints set() rather than ambiguous {}", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "SET 0\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("set()", r.Output[0], "empty set print");
            });
            Add("StringMembers", "Sets format strings with quotes and numbers plainly", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"a\"\nPUSH INT 2\nSET 2\nCAST STRING\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("{\"a\", 2}", r.Output[0], "set string format");
            });
            Add("StringNestedInList", "LIST -> String supports nested sets", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nSET 1\nLIST 1\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("[{1}]", r.Output[0], "nested set format");
            });
            Add("StringNestedInTuple", "TUPLE -> String supports nested empty sets", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "SET 0\nTUPLE 1\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("(set(),)", r.Output[0], "nested empty set format");
            });
            Add("IdentityCast", "CAST SET retains original heap identity", t =>
                DILTestHarness.ExpectBool(t,
                    "SET 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCAST SET\n" +
                    "LOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", true, locals: 1));
            Add("SetUnhashable", "Set itself is not hashable", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nHASH\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("ListCannotBeMember", "Mutable List cannot be a set member", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "LIST 0\nSET 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("SetCannotBeMember", "Mutable Set cannot be a set member", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nSET 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("NestedUnhashableTuple", "Tuple containing List remains unhashable as member", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nLIST 1\nTUPLE 1\nSET 1\nRETURN");
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.UnhashableType);
                t.Contains("[0]", r.Error.Message, "nested tuple path");
            });
            Add("ContainsUnhashable", "Even an empty set rejects unhashable lookup keys", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nLIST 0\nCONTAINS\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("AddUnhashablePreservesSet", "Unhashable SET_ADD fails before mutation", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nLIST 0\nSET_ADD\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("ConstructorUnderflow", "SET count greater than stack contents faults", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nSET 2\nRETURN"), RuntimeErrorId.StackUnderflow));
            Add("NegativeConstructorRejected", "Parser rejects negative SET element count", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("SET -1\nRETURN"), "non-negative"));
            Add("SetZeroStackOverflow", "SET 0 respects evaluation stack capacity", t =>
            {
                var options = new VMOptions(maxEvaluationStackSize: 1);
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nSET 0\nRETURN", vmOptions: options);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.StackOverflow);
            });
            Add("LegacySetInstruction", "Bare SET still assigns by list index", t =>
                ExpectIntWithLocals(t,
                    "PUSH INT 1\nLIST 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 0\n" +
                    "PUSH INT 17\nSET\nPOP\nLOAD_LOCAL 0\nPUSH INT 0\nGET\nRETURN", 17));
            Add("UnsupportedGet", "Sets reject positional GET, unlike Lists", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nPUSH INT 0\nGET\nRETURN"), RuntimeErrorId.InvalidOperation));
        }
        private static void ExpectIntWithLocals(DirectivaTestContext t, string body, long expected)
        {
            var run = DILTestHarness.ExpectCompleted(t, body, locals: 1);
            t.Equal(DataTypeId.Int, run.Value.dataType, "integer result");
            t.Equal(expected, unchecked((long)run.Value.value), "integer value");
        }
    }
}
