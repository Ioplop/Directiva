using System;
using System.Collections.Generic;
using System.Linq;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>List construction, operations, formatting and regressions through the DIL parser.</summary>
    internal static class DILListTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Lists", id, why, action);

            Add("Empty", "LIST 0 has one logical unit and zero elements", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "LIST 0\nRETURN");
                t.Equal(DataTypeId.List, r.Value.dataType, "reference type");
                var obj = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(0, obj.Count, "empty count");
                t.Equal(1u, obj.Size, "logical size");
            });
            Add("Ordered", "LIST x consumes stack values in insertion order", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 11\nPUSH INT 22\nPUSH INT 33\nLIST 3\nRETURN");
                var obj = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(4u, obj.Size, "3 values plus header");
                for (int i = 0; i < 3; i++)
                    t.Equal((long)(11 * (i + 1)), unchecked((long)obj.GetElement(i).value), "index " + i);
            });
            Add("GetAndNegativeIndex", "Index 0 and -1 select list elements", t =>
            {
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 11\nPUSH INT 22\nPUSH INT 33\nLIST 3\nPUSH INT -1\nGET\nRETURN", 33);
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 11\nPUSH INT 22\nLIST 2\nPUSH BOOL TRUE\nGET\nRETURN", 22);
            });
            Add("LengthAndTruth", "Logical length and truthiness are independent of heap capacity", t =>
            {
                DILTestHarness.ExpectInt(t, "LIST 0\nLENGTH\nRETURN", 0);
                DILTestHarness.ExpectInt(t, "PUSH NONE\nLIST 1\nLENGTH\nRETURN", 1);
                DILTestHarness.ExpectBool(t, "LIST 0\nIS_TRUTHY\nRETURN", false);
                DILTestHarness.ExpectBool(t, "PUSH INT 0\nLIST 1\nIS_TRUTHY\nRETURN", true);
            });
            Add("Append", "APPEND grows a list while preserving its reference", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 9\nAPPEND\n" +
                    "POP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                var obj = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(1, obj.Count, "length after append");
                t.Equal(9L, unchecked((long)obj.GetElement(0).value), "appended item");
                t.Equal(2UL, r.VM.Memory.Heap.AllocatedSize, "heap charged size");
            });
            Add("SetAndAlias", "SET changes a shared list in place", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nLIST 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nSTORE_LOCAL 1\nLOAD_LOCAL 0\nPUSH INT 0\nPUSH INT 77\nSET\n" +
                    "POP\nLOAD_LOCAL 1\nPUSH INT 0\nGET\nRETURN", locals: 2);
                t.Equal(77L, unchecked((long)r.Value.value), "aliased list mutation");
            });
            Add("AppendReturn", "Mutating APPEND returns None, not the list", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "LIST 0\nPUSH INT 7\nAPPEND\nRETURN");
                t.Equal(DataTypeId.None, r.Value.dataType, "append return type");
            });
            Add("InsertMiddle", "INSERT shifts elements without observable half-built lists", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 3\nLIST 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nPUSH INT 2\nINSERT\nPOP\n" +
                    "LOAD_LOCAL 0\nCAST STRING\nPRINT\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("[1, 2, 3]", r.Output[0], "insert result");
                t.Check(r.Steps >= 12, "insert should consume continuation steps");
            });
            Add("InsertClampNegative", "INSERT clamps out-of-range Python-style indexes", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 2\nLIST 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT -999\nPUSH INT 1\nINSERT\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 99\nPUSH INT 3\nINSERT\nPOP\n" +
                    "LOAD_LOCAL 0\nPRINT\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("[1, 2, 3]", r.Output[0], "clamped insertion");
            });
            Add("DeleteMiddle", "DELETE returns None and drops only the indexed element", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 4\nPUSH INT 5\nPUSH INT 6\nLIST 3\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nDELETE\nPOP\nLOAD_LOCAL 0\nPRINT\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("[4, 6]", r.Output[0], "middle delete");
            });
            Add("DeleteEnd", "Removing the last element does not require a buffer", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 5\nPUSH INT 6\nLIST 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT -1\nDELETE\nPOP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                var obj = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(1, obj.Count, "length after tail delete");
                t.Equal(5L, unchecked((long)obj.GetElement(0).value), "remaining value");
            });
            Add("PopMiddle", "POP_ITEM returns removed element and shifts the rest", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 3\nPUSH INT 4\nPUSH INT 5\nLIST 3\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nPOP_ITEM\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nPRINT\nLOAD_LOCAL 1\nRETURN", locals: 2);
                t.Equal(4L, unchecked((long)r.Value.value), "removed value");
                t.Equal("[3, 5]", r.Output[0], "pop left original list correct");
            });
            Add("PopTail", "Tail POP_ITEM is immediate and supports -1", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 3\nPUSH INT 4\nLIST 2\nPUSH INT -1\nPOP_ITEM\nRETURN", 4));
            Add("Extend", "EXTEND mutates destination and leaves source unchanged", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nLIST 2\nSTORE_LOCAL 0\n" +
                    "PUSH INT 3\nPUSH INT 4\nLIST 2\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nEXTEND\nPOP\n" +
                    "LOAD_LOCAL 0\nPRINT\nLOAD_LOCAL 1\nPRINT\nPUSH NONE\nRETURN", locals: 2);
                t.Equal("[1, 2, 3, 4]", r.Output[0], "extended result");
                t.Equal("[3, 4]", r.Output[1], "source unchanged");
            });
            Add("ExtendSelf", "Self-extension duplicates the original elements once", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nLIST 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 0\nEXTEND\nPOP\nLOAD_LOCAL 0\nPRINT\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("[1, 2, 1, 2]", r.Output[0], "self-extend");
            });
            Add("ExtendEmpty", "Extending with [] is a no-op", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nLIST 1\nSTORE_LOCAL 0\nLIST 0\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nEXTEND\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN", locals: 2);
                t.Equal(1L, unchecked((long)r.Value.value), "unchanged length");
            });
            Add("CopyNewIdentity", "Mutable list COPY is shallow but returns a distinct reference", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nLIST 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCOPY\n" +
                    "LOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", false, locals: 1));
            Add("CopyIsShallow", "COPY shares referenced children rather than duplicating strings", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"x\"\nLIST 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCOPY\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nGET\n" +
                    "LOAD_LOCAL 1\nPUSH INT 0\nGET\nREFERENCE_EQUALS\nRETURN", locals: 2);
                t.Equal(1UL, r.Value.value, "shallow element identity");
            });
            Add("AddCreatesNew", "ADD returns concatenated list without mutating operands", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nLIST 1\nSTORE_LOCAL 0\nPUSH INT 2\nLIST 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nADD\nPRINT\n" +
                    "LOAD_LOCAL 0\nPRINT\nLOAD_LOCAL 1\nPRINT\nPUSH NONE\nRETURN", locals: 2);
                t.Equal("[1, 2]", r.Output[0], "concat");
                t.Equal("[1]", r.Output[1], "left intact");
                t.Equal("[2]", r.Output[2], "right intact");
            });
            Add("SearchContains", "CONTAINS finds compatible Bool/Int values", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 0\nPUSH INT 1\nLIST 2\nPUSH BOOL TRUE\nCONTAINS\nRETURN", true));
            Add("SearchAbsent", "CONTAINS absent produces False", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 0\nPUSH INT 1\nLIST 2\nPUSH INT 3\nCONTAINS\nRETURN", false));
            Add("IndexOfFirst", "INDEX_OF returns the first matching index", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 8\nPUSH INT 9\nPUSH INT 8\nLIST 3\nPUSH INT 8\nINDEX_OF\nRETURN", 0));
            Add("IndexOfMissing", "INDEX_OF returns -1 for absent value", t =>
                DILTestHarness.ExpectInt(t,
                    "PUSH INT 8\nLIST 1\nPUSH INT 10\nINDEX_OF\nRETURN", -1));
            Add("StructuralEquality", "Independent lists compare by contents, respecting order", t =>
            {
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH STRING \"x\"\nLIST 2\n" +
                    "PUSH INT 1\nPUSH STRING \"x\"\nLIST 2\nVALUE_EQUALS\nRETURN", true);
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 2\nLIST 2\n" +
                    "PUSH INT 2\nPUSH INT 1\nLIST 2\nVALUE_EQUALS\nRETURN", false);
            });
            Add("ListVersusTuple", "List and Tuple of the same elements are not equal", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nLIST 1\nPUSH INT 1\nTUPLE 1\nVALUE_EQUALS\nRETURN", false));
            Add("StringFormatting", "Mixed nested list/tuple values are formatted readably", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH STRING \"x\"\nPUSH BOOL TRUE\nTUPLE 1\nLIST 3\n" +
                    "CAST STRING\nPRINT\nPUSH NONE\nRETURN");
                t.Equal("[1, \"x\", (True,)]", r.Output[0], "list representation");
            });
            Add("CircularFormatting", "A list containing itself prints without recursion overflow", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nLOAD_LOCAL 0\nAPPEND\nPOP\n" +
                    "LOAD_LOCAL 0\nPRINT\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("[[...]]", r.Output[0], "circular list display");
            });
            Add("TupleListCycleFormatting", "Tuple/List reference cycle is safe during casting", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nTUPLE 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nAPPEND\nPOP\nLOAD_LOCAL 1\nPRINT\nPUSH NONE\nRETURN", locals: 2);
                t.Equal("([(...)],)", r.Output[0], "tuple/list cycle representation");
            });
            Add("ListHashRejected", "Lists cannot be hashed, even if their elements are hashable", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 2\nLIST 1\nHASH\nRETURN"),
                    RuntimeErrorId.UnhashableType));
            Add("TupleWithListUnhashable", "Tuples containing lists remain unhashable with useful paths", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 2\nLIST 2\nTUPLE 1\nHASH\nRETURN");
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.UnhashableType);
                t.Contains("[0]", r.Error.Message, "tuple child path");
            });
            Add("CastIdentity", "CAST LIST succeeds as the same heap reference", t =>
                DILTestHarness.ExpectBool(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCAST LIST\n" +
                    "LOAD_LOCAL 0\nREFERENCE_EQUALS\nRETURN", true, locals: 1));
            Add("ParserRejectsNegativeCount", "LIST negative counts are rejected by DIL", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("LIST -1\nPUSH NONE\nRETURN"), "non-negative"));
            Add("ParserRejectsExtraOperands", "LIST accepts exactly one numeric count", t =>
                DILTestHarness.ExpectParseFailure(t,
                    DILTestHarness.Function("LIST 0 1\nPUSH NONE\nRETURN")));
            Add("ListStackUnderflow", "LIST requires count stack values", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 1\nLIST 2\nRETURN"),
                    RuntimeErrorId.StackUnderflow));
            Add("InvalidGet", "Negative index smaller than -Count is an IndexOutOfRange", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "PUSH INT 2\nLIST 1\nPUSH INT -2\nGET\nRETURN"),
                    RuntimeErrorId.IndexOutOfRange));
            Add("InvalidSet", "SET cannot grow a list by assigning past the end", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "LIST 0\nPUSH INT 0\nPUSH INT 9\nSET\nRETURN"),
                    RuntimeErrorId.IndexOutOfRange));
            Add("InvalidPop", "POP_ITEM from an empty list gives IndexOutOfRange", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "LIST 0\nPUSH INT -1\nPOP_ITEM\nRETURN"),
                    RuntimeErrorId.IndexOutOfRange));
            Add("WrongIndexType", "GET with a String index produces a normal invalid-operation fault", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t, "LIST 0\nPUSH STRING \"x\"\nGET\nRETURN"),
                    RuntimeErrorId.InvalidOperation));
            Add("SearchManyElements", "Searching 80 list elements uses continuation budget", t =>
            {
                string elements = string.Join("\n", Enumerable.Range(0, 80).Select(i => "PUSH INT " + i));
                var r = DILTestHarness.ExpectCompleted(t,
                    elements + "\nLIST 80\nPUSH INT 79\nINDEX_OF\nRETURN", budget: 300);
                t.Equal(79L, unchecked((long)r.Value.value), "late match");
                t.Check(r.Steps > 85, "long search should consume multiple operation steps");
            });
        }
    }
}
