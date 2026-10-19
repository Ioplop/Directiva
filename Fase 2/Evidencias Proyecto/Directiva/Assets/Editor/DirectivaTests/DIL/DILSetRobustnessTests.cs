using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Hash collisions, invariants, malformed input, and deterministic property tests.</summary>
    internal static class DILSetRobustnessTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "SetRobustness", id, why, action);

            Add("HashBucketCollision", "Different values with an artificial same hash stay separate", t =>
            {
                var mem = new VMMemoryContext(0);
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromInt(12), 99);
                set.AddAuthorized(DataValue.FromInt(34), 99);
                t.Check(set.TryFind(DataValue.FromInt(12), 99, mem, out int a, out _), "lookup 12");
                t.Check(set.TryFind(DataValue.FromInt(34), 99, mem, out int b, out _), "lookup 34");
                t.Check(a >= 0 && b >= 0 && a != b, "collision must not merge values");
                t.Check(set.TryFind(DataValue.FromInt(56), 99, mem, out int missing, out _), "lookup 56");
                t.Equal(-1, missing, "missing colliding key");
            });
            Add("CollisionSwapDelete", "Removing one colliding member doesn't corrupt other bucket indices", t =>
            {
                var mem = new VMMemoryContext(0);
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromInt(1), 9);
                set.AddAuthorized(DataValue.FromInt(2), 9);
                set.AddAuthorized(DataValue.FromInt(3), 9);
                set.RemoveAuthorized(0);
                t.Equal(2, set.Count, "remaining members");
                t.Check(set.TryFind(DataValue.FromInt(3), 9, mem, out int pos3, out _), "find moved value");
                t.Check(pos3 >= 0, "third item survived relocation");
                t.Check(set.TryFind(DataValue.FromInt(2), 9, mem, out int pos2, out _), "find second value");
                t.Check(pos2 >= 0, "second item survived relocation");
                t.Check(set.TryFind(DataValue.FromInt(1), 9, mem, out int missing, out _), "find removed value");
                t.Equal(-1, missing, "removed item not present");
            });
            Add("CollisionMixedBuckets", "Moving last member rewrites index in a different hash bucket", t =>
            {
                var mem = new VMMemoryContext(0);
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromInt(1), 7);
                set.AddAuthorized(DataValue.FromInt(2), 7);
                set.AddAuthorized(DataValue.FromInt(3), 8);
                set.RemoveAuthorized(0);
                t.Check(set.TryFind(DataValue.FromInt(3), 8, mem, out int third, out _), "lookup moved third");
                t.Equal(0, third, "moved index is zero");
                t.Check(set.TryFind(DataValue.FromInt(2), 7, mem, out int second, out _), "lookup retained second");
                t.Equal(1, second, "retained index is one");
            });
            Add("CollisionEqualValues", "Two equal values in a colliding bucket compare equal", t =>
            {
                var mem = new VMMemoryContext(0);
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromInt(1), 77);
                t.Check(set.TryFind(DataValue.FromBool(true), 77, mem,
                    out int position, out _), "Bool/Int match");
                t.Equal(0, position, "numeric equality inside collision bucket");
            });
            Add("SetReferenceNotHashable", "HASH on mutable Set returns structured UnhashableType", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nHASH\nRETURN"), RuntimeErrorId.UnhashableType));
            Add("TupleContainingSetUnhashable", "HASH error path identifies nested mutable Set", t =>
            {
                var run = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nSET 1\nTUPLE 1\nHASH\nRETURN");
                DILTestHarness.ExpectFault(t, run, RuntimeErrorId.UnhashableType);
                t.Contains("[0]", run.Error.Message, "tuple path");
            });
            Add("InvalidHeapReference", "Dangling Set reference reports InvalidMemoryAccess", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(new DataValue(DataTypeId.Set, 999)),
                    Instruction.Operation(OperationId.Length), Instruction.Return());
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t,
                    DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidMemoryAccess);
            });
            Add("NegativeDirectCount", "VM rejects a negative constructor count even without parser", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Set(-1), Instruction.Return());
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t,
                    DILTestHarness.MakeVM(code)), RuntimeErrorId.InvalidInstruction);
            });
            Add("BareLegacySetUnderflow", "SET without operands remains a 3-argument operation", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET\nRETURN"), RuntimeErrorId.StackUnderflow));
            Add("SetContainsWrongOperand", "CONTAINS rejects an unsupported query signature", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 1\nCONTAINS\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("UnsupportedSetOrdering", "Sets do not silently accept ordering operators", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nSET 0\nLESS_THAN\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("SetNoIndexOf", "IndexOf is deliberately undefined for an unordered set", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nPUSH INT 1\nINDEX_OF\nRETURN"), RuntimeErrorId.InvalidOperation));
            Add("DeterministicPropertyChurn", "Many mutations agree with a host reference set and logical heap charges", t =>
            {
                var random = new Random(91453);
                var reference = new HashSet<long>();
                var mem = new VMMemoryContext(1, 250);
                var set = new SetHeapObject();
                uint id = mem.AllocHeap(set);
                mem.SetGlobal(0, DataValue.FromSetReference(id));
                for (int i = 0; i < 320; i++)
                {
                    long key = random.Next(0, 48);
                    OperationId operation = i % 3 == 0 ? OperationId.SetDiscard : OperationId.SetAdd;
                    var value = DataValue.FromInt(key);
                    var result = OperationDispatcher.Start(operation,
                        new[] { DataValue.FromSetReference(id), value }, mem).Result;
                    t.Equal(OperationStatus.Success, result.Status, "churn operation " + i);
                    if (operation == OperationId.SetDiscard) reference.Remove(key);
                    else reference.Add(key);
                    if (i % 40 == 39)
                    {
                        t.Equal(reference.Count, set.Count, "count at iteration " + i);
                        t.Equal((ulong)(1 + reference.Count), mem.Heap.AllocatedSize,
                            "charged at iteration " + i);
                        foreach (long candidate in reference)
                        {
                            t.Check(set.TryFind(DataValue.FromInt(candidate), candidate, mem,
                                out int found, out _), "lookup successful");
                            t.Check(found >= 0, "reference member reachable");
                        }
                    }
                }
            });
            Add("LargeSetConstruction", "SET 200 is deterministic and not truncated", t =>
            {
                var values = new long[200];
                for (int i = 0; i < values.Length; i++) values[i] = i % 120;
                var r = DILTestHarness.ExpectCompleted(t,
                    DILSetAlgebraTests.Integers(values) + "LENGTH\nRETURN", budget: 1200);
                t.Equal(120L, unchecked((long)r.Value.value), "unique count");
            });
            Add("RemovalsPreserveHashLookups", "Repeated swap deletion never loses surviving elements", t =>
            {
                var values = new long[45];
                for (int i = 0; i < values.Length; i++) values[i] = i;
                var run = DILTestHarness.ExpectCompleted(t,
                    DILSetAlgebraTests.Integers(values) + "STORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nSET_REMOVE\nPOP\n" +
                    "LOAD_LOCAL 0\nPUSH INT 44\nCONTAINS\nRETURN", locals: 1);
                t.Equal(1UL, run.Value.value, "last element still found after swap-delete");
            });
            Add("MutableSetNotHashableInTuple", "Mutable Set nested in Tuple cannot be added to another Set", t =>
                DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t,
                    "SET 0\nTUPLE 1\nSET 1\nRETURN"), RuntimeErrorId.UnhashableType));
        }
    }
}
