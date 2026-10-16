using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    internal static class DILMemoryTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string purpose, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "Memory", id, purpose, action);

            Add("LogicalSizes", "String and tuple sizes match deterministic logical accounting", t =>
            {
                t.Equal(1u, new StringHeapObject("").Size, "empty string size");
                t.Equal(3u, new StringHeapObject("abcde").Size, "5 UTF16 code units");
                t.Equal(2u, new StringHeapObject("🐌").Size, "surrogate pair size");
                t.Equal(4u, new TupleHeapObject(new[] {
                    DataValue.FromInt(1), DataValue.FromInt(2), DataValue.FromInt(3)
                }).Size, "three element tuple size");
            });
            Add("HeapAllocationAndFree", "Heap IDs are stable, freeing removes objects and subtracts size", t =>
            {
                var heap = new HeapMemory(10);
                uint a = heap.Allocate(new StringHeapObject("abc"));
                uint b = heap.Allocate(new StringHeapObject("de"));
                t.Check(a != b && a > 0 && b > 0, "unique positive heap ids");
                t.Equal(4UL, heap.AllocatedSize, "total heap units");
                t.Check(heap.Free(a), "free first object");
                t.Check(!heap.TryGet(a, out _), "freed reference must not resolve");
                t.Equal(2UL, heap.AllocatedSize, "size after free");
                t.Check(!heap.Free(a), "double free returns false");
            });
            Add("HeapNoGCBeforePressure", "Mark-sweep is lazy; no GC occurs while capacity suffices", t =>
            {
                var memory = new VMMemoryContext(0, 10);
                memory.AllocHeap(new StringHeapObject("a"));
                memory.AllocHeap(new StringHeapObject("b"));
                t.Equal(0UL, memory.GarbageCollectionCount, "unexpected eager collection");
                t.Equal(4UL, memory.Heap.AllocatedSize, "size before pressure");
            });
            Add("GCOnAllocationPressure", "Failed initial allocation invokes mark-and-sweep then retries", t =>
            {
                var memory = new VMMemoryContext(0, 4);
                uint dead = memory.AllocHeap(new StringHeapObject("a"));
                uint live = memory.AllocHeap(new StringHeapObject("b"));
                t.Check(memory.TryAllocHeap(new StringHeapObject("c"), out uint replacement), "reallocation after GC");
                t.Check(!memory.TryGetHeap(dead, out _), "dead object should be swept");
                t.Check(!memory.TryGetHeap(live, out _), "second dead object should be swept");
                t.Check(memory.TryGetHeap(replacement, out _), "replacement missing");
                t.Check(memory.GarbageCollectionCount > 0, "GC never triggered");
            });
            Add("RootedGlobal", "Shared global is a GC root even under pressure", t =>
            {
                var memory = new VMMemoryContext(1, 6);
                uint rooted = memory.AllocHeap(new StringHeapObject("hi"));
                memory.SetGlobal(0, DataValue.FromStringReference(rooted));
                memory.AllocHeap(new StringHeapObject("garbage")); // Uses 3 units, total 5.
                memory.TryAllocHeap(new StringHeapObject("zzzz"), out _); // Force pressure/GC.
                t.Check(memory.TryGetHeap(rooted, out _), "global root was collected");
                t.Check(memory.GarbageCollectionCount > 0, "no GC occurred");
            });
            Add("RootedLocal", "Local variable keeps a heap object alive across GC", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH STRING \"saved\"\nSTORE_LOCAL 0\n" +
                    "PUSH STRING \"temporary\"\nPOP\nPUSH STRING \"more\"\nPOP\n" +
                    "LOAD_LOCAL 0\nPRINT\nPUSH NONE\nRETURN", locals: 1);
                var vm = DILTestHarness.MakeVM(code, heap: 8);
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "GC with local root");
                t.Equal("saved", r.Output[0], "local value survived");
                t.Check(vm.Memory.GarbageCollectionCount > 0, "expected lazy GC");
            });
            Add("RootedEvalStack", "Stack references remain live during tuple allocation", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"unused\"\nPOP\nPUSH STRING \"A\"\nPUSH STRING \"B\"\nTUPLE 2\n" +
                    "CAST STRING\nPRINT\nPUSH NONE\nRETURN", heap: 12);
                t.Equal("(\"A\", \"B\")", r.Output[0], "stack values survived tuple allocation");
                t.Check(r.VM.Memory.GarbageCollectionCount > 0, "expected GC under tuple/cast pressure");
            });
            Add("NestedReachability", "GC traverses nested tuple references and shared children", t =>
            {
                var memory = new VMMemoryContext(1, 20);
                uint shared = memory.AllocHeap(new StringHeapObject("KEEP"));
                uint inner = memory.AllocHeap(new TupleHeapObject(new[] { DataValue.FromStringReference(shared) }));
                uint outer = memory.AllocHeap(new TupleHeapObject(new[] {
                    DataValue.FromTupleReference(inner), DataValue.FromStringReference(shared) }));
                memory.SetGlobal(0, DataValue.FromTupleReference(outer));
                int removed = memory.CollectGarbage();
                t.Equal(0, removed, "all nested objects are reachable");
                t.Check(memory.TryGetHeap(shared, out _), "shared child missing");
                memory.SetGlobal(0, DataValue.None());
                t.Equal(3, memory.CollectGarbage(), "unrooted nested graph must be swept");
                t.Equal(0, memory.Heap.ObjectCount, "sweep count");
            });
            Add("CycleSafeMarkSweep", "A deliberately forged reference cycle never loops forever", t =>
            {
                var memory = new VMMemoryContext(1, 12);
                var cycle = new TupleHeapObject(new[] { DataValue.FromTupleReference(1) });
                uint cycleId = memory.AllocHeap(cycle);
                t.Equal(1u, cycleId, "fixture reference id");
                memory.SetGlobal(0, DataValue.FromTupleReference(cycleId));
                t.Equal(0, memory.CollectGarbage(), "rooted cycle retained");
                memory.SetGlobal(0, DataValue.None());
                t.Equal(1, memory.CollectGarbage(), "unrooted cycle collected");
            });
            Add("OutOfMemoryLargeString", "An oversized string errors after lazy collection retry", t =>
            {
                var r = DILTestHarness.Evaluate(t, "PUSH STRING \"abcdefghijk\"\nRETURN", heap: 2);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Check(r.VM.Memory.GarbageCollectionCount > 0, "OOM must attempt GC once");
            });
            Add("OutOfMemoryTuple", "TUPLE rejects an allocation that exceeds heap capacity", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nTUPLE 3\nRETURN", heap: 3);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
            });
            Add("TupleFailurePreservesStack", "Failed tuple construction must not pop its input operands", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromInt(11)), Instruction.Push(DataValue.FromInt(22)),
                    Instruction.Tuple(2), Instruction.Return());
                var vm = DILTestHarness.MakeVM(code, heap: 2);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Equal(2, vm.EvaluationStackCount, "tuple construction must be atomic wrt stack");
            });
            Add("TupleConcatOutOfMemory", "Tuple concatenation does not corrupt original immutable tuples", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 2\nTUPLE 1\nSTORE_LOCAL 0\nPUSH INT 3\nTUPLE 1\n" +
                    "LOAD_LOCAL 0\nADD\nRETURN", locals: 1);
                var vm = DILTestHarness.MakeVM(code, heap: 4);
                var r = DILTestHarness.Run(t, vm);
                // Both live tuples use 2 units each; concat requires 3 more.
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Equal(2, vm.Memory.Heap.ObjectCount, "original tuples remain alive");
            });
            Add("TupleCastGCPressure", "Tuple -> String roots input across GC (prior regression)", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"oldoldold\"\nPOP\n" +
                    "PUSH STRING \"A\"\nPUSH INT 12\nTUPLE 2\nCAST STRING\nPRINT\n" +
                    "PUSH NONE\nRETURN", heap: 10);
                t.Equal("(\"A\", 12)", r.Output[0], "tuple format after collection");
                t.Check(r.VM.Memory.GarbageCollectionCount > 0, "expected GC on tuple cast");
            });
            Add("TupleCastOOM", "Tuple -> String reports heap exhaustion", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 10\nPUSH INT 20\nTUPLE 2\nCAST STRING\nRETURN", heap: 5);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
            });
            Add("CompletedVmReturnRoot", "Completed VM return values remain GC roots while VM is alive", t =>
            {
                var code = DILTestHarness.Parse(t, "PUSH STRING \"lives\"\nRETURN");
                var memory = new VMMemoryContext(0, 5);
                var vm = DILTestHarness.MakeVM(code, memory: memory);
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "return status");
                uint id = (uint)r.Value.value;
                t.Equal(0, memory.CollectGarbage(), "completed VM return remains reachable");
                t.Check(memory.TryGetHeap(id, out _), "completed VM lost returned object");
            });
            Add("HeapClear", "Clearing heap resets object count, size and reference numbering", t =>
            {
                var heap = new HeapMemory(9);
                heap.Allocate(new StringHeapObject("a"));
                heap.Clear();
                t.Equal(0, heap.ObjectCount, "clear object count");
                t.Equal(0UL, heap.AllocatedSize, "clear bytes/units");
                t.Equal(1u, heap.Allocate(new StringHeapObject("b")), "clear reference counter");
            });
        }
    }
}
