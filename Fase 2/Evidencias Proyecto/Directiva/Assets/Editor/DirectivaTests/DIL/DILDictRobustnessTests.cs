using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Hash collisions, deterministic churn, type/stack limits and deep cyclic equality.</summary>
    internal static class DILDictRobustnessTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "DictRobustness", id, description, action);

            Add("HashCollisionDistinct", "Two unequal keys with same forced hash stay separate", t =>
            {
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(11), 123);
                dict.AddAuthorized(DataValue.FromInt(2), DataValue.FromInt(22), 123);
                var mem = new VMMemoryContext(0);
                t.Check(dict.TryFind(DataValue.FromInt(1), 123, mem, out int i, out _), "first lookup");
                t.Equal(0, i, "first index");
                t.Check(dict.TryFind(DataValue.FromInt(2), 123, mem, out i, out _), "second lookup");
                t.Equal(1, i, "second index");
                t.Equal(22L, unchecked((long)dict.GetValue(i).value), "second value");
            });
            Add("HashCollisionMissing", "Equal hashes do not imply equal keys", t =>
            {
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(10), 4);
                t.Check(dict.TryFind(DataValue.FromInt(7), 4, new VMMemoryContext(0),
                    out int found, out _), "lookup");
                t.Equal(-1, found, "colliding key not present");
            });
            Add("HashCollisionRemove", "Deleting first colliding key preserves second bucket", t =>
            {
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(10), 4);
                dict.AddAuthorized(DataValue.FromInt(2), DataValue.FromInt(20), 4);
                dict.RemoveAuthorized(0);
                t.Check(dict.TryFind(DataValue.FromInt(2), 4, new VMMemoryContext(0),
                    out int found, out _), "lookup");
                t.Equal(0, found, "second bucket moved to index zero");
            });
            Add("HashCollisionReplace", "Colliding key replacement does not change other entry", t =>
            {
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(10), 4);
                dict.AddAuthorized(DataValue.FromInt(2), DataValue.FromInt(20), 4);
                dict.SetValueAuthorized(0, DataValue.FromInt(33));
                t.Equal(33L, unchecked((long)dict.GetValue(0).value), "first value");
                t.Equal(20L, unchecked((long)dict.GetValue(1).value), "second value");
            });
            Add("BucketShiftMiddle", "Deleting a middle entry updates multiple hash bucket indices", t =>
            {
                var dict = new DictHeapObject();
                for (int i = 0; i < 7; i++)
                    dict.AddAuthorized(DataValue.FromInt(i), DataValue.FromInt(i + 100), i % 3);
                dict.RemoveAuthorized(3);
                t.Equal(6, dict.Count, "one removed");
                var mem = new VMMemoryContext(0);
                for (int i = 0; i < 7; i++)
                {
                    t.Check(dict.TryFind(DataValue.FromInt(i), i % 3, mem,
                        out int found, out _), "lookup " + i);
                    t.Equal(i == 3 ? -1 : i < 3 ? i : i - 1, found, "position for " + i);
                }
            });
            Add("InsertionOrderAfterRemoval", "Deleting and reinserting a key moves it to the end", t =>
            {
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(10), 1);
                dict.AddAuthorized(DataValue.FromInt(2), DataValue.FromInt(20), 2);
                dict.RemoveAuthorized(0);
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(11), 1);
                t.Equal(2L, unchecked((long)dict.GetKey(0).value), "first remaining key");
                t.Equal(1L, unchecked((long)dict.GetKey(1).value), "reinserted key");
            });
            Add("OriginalKeyOnOverwrite", "Bool-equivalent key updates value but preserves original Int key", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH BOOL TRUE\nPUSH INT 4\nDICT 2\nRETURN");
                var dict = (DictHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(DataTypeId.Int, dict.GetKey(0).dataType, "original key type");
                t.Equal(4L, unchecked((long)dict.GetValue(0).value), "last value");
            });
            Add("DeterministicPropertyChurn", "500 deterministic host model mutations preserve content", t =>
            {
                var random = new Random(112358);
                var expected = new Dictionary<long, long>();
                var map = new DictHeapObject();
                var mem = new VMMemoryContext(1, 1000);
                uint id = mem.AllocHeap(map);
                mem.SetGlobal(0, DataValue.FromDictReference(id));
                for (int i = 0; i < 500; i++)
                {
                    long key = random.Next(0, 31);
                    bool remove = random.Next(0, 4) == 0;
                    t.Check(map.TryFind(DataValue.FromInt(key), key, mem, out int index,
                        out RuntimeError error), "lookup in iteration " + i + ": " + error?.Message);
                    if (remove)
                    {
                        if (index < 0) continue;
                        t.Check(mem.TryResizeHeap(id, map.Size - 2u), "authorize remove");
                        map.RemoveAuthorized(index);
                        expected.Remove(key);
                    }
                    else
                    {
                        long value = random.Next(0, 10000);
                        if (index >= 0) map.SetValueAuthorized(index, DataValue.FromInt(value));
                        else
                        {
                            t.Check(mem.TryResizeHeap(id, map.Size + 2u), "authorize add");
                            map.AddAuthorized(DataValue.FromInt(key), DataValue.FromInt(value), key);
                        }
                        expected[key] = value;
                    }
                    t.Equal(expected.Count, map.Count, "count at iteration " + i);
                    t.Equal(1UL + (ulong)expected.Count * 2UL,
                        mem.Heap.AllocatedSize, "charged heap at iteration " + i);
                }
                foreach (var item in expected)
                {
                    t.Check(map.TryFind(DataValue.FromInt(item.Key), item.Key, mem,
                        out int found, out _), "final lookup");
                    t.Check(found >= 0, "final key present");
                    t.Equal(item.Value, unchecked((long)map.GetValue(found).value), "final value");
                }
            });
            Add("LargeConstructor", "Constructing 220 distinct entries is deterministic", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    DILDictViewTests.Integers(220) + "LENGTH\nRETURN", budget: 600);
                t.Equal(220L, unchecked((long)r.Value.value), "all entries");
            });
            Add("LargeUpdate", "Updating large maps goes through continued copy and union", t =>
            {
                string source = DILDictViewTests.Integers(35) + "STORE_LOCAL 0\n" +
                    "PUSH INT 100\nPUSH INT 200\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nPOP\nLOAD_LOCAL 0\nLENGTH\nRETURN";
                var r = DILTestHarness.ExpectCompleted(t, source, locals: 2, budget: 250);
                t.Equal(36L, unchecked((long)r.Value.value), "merged entries");
            });
            Add("LargeCopy", "COPY of >16 entries returns all entries", t =>
                DILTestHarness.ExpectInt(t, DILDictViewTests.Integers(37) + "COPY\nLENGTH\nRETURN", 37));
            Add("LargeValues", "Long values snapshots use more than one continuation", t =>
                DILTestHarness.ExpectInt(t,
                    DILDictViewTests.Integers(41) + "DICT_VALUES\nLENGTH\nRETURN", 41));
            Add("DanglingReference", "Invalid dictionary heap ID reports InvalidMemoryAccess", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromDictReference(100)),
                    Instruction.Operation(OperationId.Length), Instruction.Return());
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, DILTestHarness.MakeVM(code)),
                    RuntimeErrorId.InvalidMemoryAccess);
            });
            Add("WrongHeapType", "Reference tagged as Dict but pointing at String is rejected", t =>
            {
                var mem = new VMMemoryContext(0);
                uint id = mem.AllocHeap(new StringHeapObject("not a dictionary"));
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromDictReference(id)),
                    Instruction.Operation(OperationId.Length), Instruction.Return());
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: mem)),
                    RuntimeErrorId.InvalidMemoryAccess);
            });
            Add("NegativeDirectConstructor", "VM rejects negative count even without DIL parser", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Dict(-1), Instruction.Return());
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, DILTestHarness.MakeVM(code)),
                    RuntimeErrorId.InvalidInstruction);
            });
            Add("HugeDirectConstructor", "VM rejects count whose doubled size overflows int", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Dict(int.MaxValue), Instruction.Return());
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, DILTestHarness.MakeVM(code)),
                    RuntimeErrorId.InvalidInstruction);
            });
            Add("BareSetStillWorks", "Legacy SET (without x) remains the three-operand mutator", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "LIST 0\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 2\nAPPEND\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nPUSH INT 3\nSET\nPOP\nLOAD_LOCAL 0\nPUSH INT 0\nGET\nRETURN", locals: 1);
                t.Equal(3L, unchecked((long)r.Value.value), "legacy SET result");
            });
            Add("CyclicEquality", "Two independent cyclic maps with equal structures compare equal", t =>
            {
                var mem = new VMMemoryContext(2);
                uint left = CreateCycle(mem, 1);
                uint right = CreateCycle(mem, 1);
                mem.SetGlobal(0, DataValue.FromDictReference(left));
                mem.SetGlobal(1, DataValue.FromDictReference(right));
                var op = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# GLOBAL : a\n# GLOBAL : b\n" + DILTestHarness.Function(
                        "LOAD_GLOBAL $a\nLOAD_GLOBAL $b\nVALUE_EQUALS\nRETURN")
                });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(op, memory: mem));
                t.Equal(VMExecutionStatus.Completed, r.Status, "cyclic equality finishes");
                t.Equal(1UL, r.Value.value, "equal cycles");
            });
            Add("CyclicInequality", "Cyclic maps with different keys compare unequal", t =>
            {
                var mem = new VMMemoryContext(2);
                uint left = CreateCycle(mem, 1);
                uint right = CreateCycle(mem, 2);
                mem.SetGlobal(0, DataValue.FromDictReference(left));
                mem.SetGlobal(1, DataValue.FromDictReference(right));
                var op = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = "# GLOBAL : a\n# GLOBAL : b\n" + DILTestHarness.Function(
                        "LOAD_GLOBAL $a\nLOAD_GLOBAL $b\nVALUE_EQUALS\nRETURN")
                });
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(op, memory: mem));
                t.Equal(VMExecutionStatus.Completed, r.Status, "cyclic inequality finishes");
                t.Equal(0UL, r.Value.value, "different keys");
            });
            Add("TupleWithDictEquality", "SequenceEquality supports tuples containing equal maps", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nTUPLE 1\n" +
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nTUPLE 1\nVALUE_EQUALS\nRETURN", true));
            Add("ListWithDictEquality", "SequenceEquality supports lists containing equal maps", t =>
                DILTestHarness.ExpectBool(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nLIST 1\n" +
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nLIST 1\nVALUE_EQUALS\nRETURN", true));
        }

        private static uint CreateCycle(VMMemoryContext mem, int key)
        {
            var dict = new DictHeapObject();
            uint id = mem.AllocHeap(dict);
            if (!mem.TryResizeHeap(id, 3)) throw new InvalidOperationException("Could not resize test dict");
            dict.AddAuthorized(DataValue.FromInt(key), DataValue.FromDictReference(id), key);
            return id;
        }
    }
}
