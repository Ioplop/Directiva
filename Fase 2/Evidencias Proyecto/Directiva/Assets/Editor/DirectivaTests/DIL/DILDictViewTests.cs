using System;
using System.Collections.Generic;
using System.Text;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Deterministic text formatting and continued key/value/item snapshots.</summary>
    internal static class DILDictViewTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "DictViews", id, description, action);

            Add("PrintEmpty", "Empty dictionary formats as {}", t =>
                ExpectPrinted(t, "DICT 0", "{}"));
            Add("PrintOne", "Dictionary prints one key/value entry", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 5\nDICT 1", "{1: 5}"));
            Add("PrintMultiple", "Dictionary formatting preserves ordered entries", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 5\nPUSH INT 2\nPUSH INT 6\nDICT 2", "{1: 5, 2: 6}"));
            Add("PrintStringKey", "String keys and values are quoted", t =>
                ExpectPrinted(t, "PUSH STRING \"a\"\nPUSH STRING \"b\"\nDICT 1", "{\"a\": \"b\"}"));
            Add("PrintDuplicate", "Original duplicate key retains first position", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nPUSH INT 4\n" +
                    "PUSH BOOL TRUE\nPUSH INT 9\nDICT 3", "{1: 9, 3: 4}"));
            Add("PrintDictInList", "List can contain a Dict", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 2\nDICT 1\nLIST 1", "[{1: 2}]"));
            Add("PrintDictInTuple", "Tuple can contain a Dict", t =>
                ExpectPrinted(t, "DICT 0\nTUPLE 1", "({},)"));
            Add("PrintDictInSetImpossible", "A Tuple containing Dict cannot be hashed as a Set member", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "DICT 0\nTUPLE 1\nSET 1\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("PrintDictValueList", "Formatting nested mutable values", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nLIST 2\nDICT 1", "{1: [2, 3]}"));
            Add("PrintNestedDict", "Nested dictionary shows braces and colons", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 2\nDICT 1\n" +
                    "PUSH INT 3\nPUSH INT 4\nDICT 1\nDICT 1", "{{1: 2}: {3: 4}}", expectFault: true));
            Add("PrintNestedDictAsValue", "Nested dictionary is legal as a value", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nDICT 1\nDICT 1", "{1: {2: 3}}"));
            Add("PrintSetValue", "Set can appear as a dictionary value", t =>
                ExpectPrinted(t, "PUSH INT 1\nPUSH INT 2\nSET 1\nDICT 1", "{1: {2}}"));
            Add("PrintStringsEscape", "Escapes in String keys survive formatting", t =>
                ExpectPrinted(t, "PUSH STRING \"a\\\"b\"\nPUSH INT 7\nDICT 1", "{\"a\\\"b\": 7}"));
            Add("CastString", "CAST STRING produces a heap string", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "DICT 0\nCAST STRING\nRETURN");
                t.Equal(DataTypeId.String, r.Value.dataType, "type");
                var o = (StringHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal("{}", o.Value, "value");
            });
            Add("KeysTypeAndCount", "DICT_KEYS produces an ordered List", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 3\nPUSH INT 2\nPUSH INT 4\nDICT 2\nDICT_KEYS\nRETURN");
                t.Equal(DataTypeId.List, r.Value.dataType, "keys type");
                var list = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(2, list.Count, "key count");
                t.Equal(1L, unchecked((long)list.GetElement(0).value), "first key");
                t.Equal(2L, unchecked((long)list.GetElement(1).value), "second key");
            });
            Add("ValuesOrder", "DICT_VALUES preserves insertion order", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 60\nPUSH INT 2\nPUSH INT 70\nDICT 2\nDICT_VALUES\nRETURN");
                var list = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(60L, unchecked((long)list.GetElement(0).value), "first value");
                t.Equal(70L, unchecked((long)list.GetElement(1).value), "second value");
            });
            Add("ItemsPairs", "DICT_ITEMS returns a List of Tuple(key, value)", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 11\nPUSH INT 2\nPUSH INT 22\nDICT 2\nDICT_ITEMS\nRETURN");
                var list = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(2, list.Count, "items length");
                for (int i = 0; i < 2; i++)
                {
                    t.Equal(DataTypeId.Tuple, list.GetElement(i).dataType, "item type");
                    var tuple = (TupleHeapObject)r.VM.Memory.GetHeap((uint)list.GetElement(i).value);
                    t.Equal(2, tuple.Count, "pair arity");
                    t.Equal((long)i + 1, unchecked((long)tuple.GetElement(0).value), "pair key");
                    t.Equal(((long)i + 1) * 11, unchecked((long)tuple.GetElement(1).value), "pair value");
                }
            });
            Add("EmptyKeys", "Empty map has an empty keys list", t =>
                DILTestHarness.ExpectInt(t, "DICT 0\nDICT_KEYS\nLENGTH\nRETURN", 0));
            Add("EmptyValues", "Empty map has an empty values list", t =>
                DILTestHarness.ExpectInt(t, "DICT 0\nDICT_VALUES\nLENGTH\nRETURN", 0));
            Add("EmptyItems", "Empty map has an empty items list", t =>
                DILTestHarness.ExpectInt(t, "DICT 0\nDICT_ITEMS\nLENGTH\nRETURN", 0));
            Add("KeysAreSnapshot", "Changing map after DICT_KEYS does not retroactively change snapshot", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nDICT_KEYS\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nPUSH INT 2\nPUSH INT 20\nSET\nPOP\nLOAD_LOCAL 1\nLENGTH\nRETURN", locals: 2);
                t.Equal(DataTypeId.Int, r.Value.dataType, "result type");
                t.Equal(1L, unchecked((long)r.Value.value), "snapshot length");
            });
            Add("ViewsContinue", "Large DICT_KEYS continues over multiple budgeted VM steps", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, Integers(75) + "DICT_KEYS\nLENGTH\nRETURN", budget: 300);
                t.Equal(75L, unchecked((long)r.Value.value), "key count");
                t.Check(r.Steps > 150, "construction plus continued view steps");
            });
            Add("ValuesContinue", "Large DICT_VALUES completes through continuation", t =>
                DILTestHarness.ExpectInt(t, Integers(40) + "DICT_VALUES\nLENGTH\nRETURN", 40));
            Add("ItemsContinue", "Large DICT_ITEMS creates all pairs", t =>
                DILTestHarness.ExpectInt(t, Integers(22) + "DICT_ITEMS\nLENGTH\nRETURN", 22));
            Add("FormattedCycles", "Self-referential dictionary formats with cycle placeholder", t =>
            {
                var memory = new VMMemoryContext(1);
                var dict = new DictHeapObject();
                uint id = memory.AllocHeap(dict);
                t.Check(memory.TryResizeHeap(id, 3), "reserve key/value");
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromDictReference(id), 1);
                memory.SetGlobal(0, DataValue.FromDictReference(id));
                var program = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# GLOBAL : dict\n" + DILTestHarness.Function(
                        "LOAD_GLOBAL $dict\nCAST STRING\nPRINT\nPUSH NONE\nRETURN")
                });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(program, memory: memory));
                t.Equal(VMExecutionStatus.Completed, r.Status, "cycle format completes");
                t.Equal("{1: {...}}", r.Output[0], "cycle placeholder");
            });
        }

        private static void ExpectPrinted(DirectivaTestContext t, string expression,
            string expected, bool expectFault = false)
        {
            var r = DILTestHarness.Evaluate(t, expression + "\nCAST STRING\nPRINT\nPUSH NONE\nRETURN");
            if (expectFault)
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.UnhashableType);
            else
            {
                t.Equal(VMExecutionStatus.Completed, r.Status, "formatting completed");
                t.Equal(1, r.Output.Count, "PRINT events");
                t.Equal(expected, r.Output[0], "formatted dictionary");
            }
        }

        internal static string Integers(int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
                sb.Append("PUSH INT ").Append(i).Append("\nPUSH INT ").Append(i * 11).Append('\n');
            sb.Append("DICT ").Append(count).Append('\n');
            return sb.ToString();
        }
    }
}
