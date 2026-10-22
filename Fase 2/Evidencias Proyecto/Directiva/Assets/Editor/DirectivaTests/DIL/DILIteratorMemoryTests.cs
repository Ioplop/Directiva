using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Iterator heap roots, GC, mutation detection and multiple VMs.</summary>
    internal static class DILIteratorMemoryTests
    {
        private static void Add(List<DirectivaTestCase> tests, string name,
            string description, Action<DirectivaTestContext> action)
            => DirectivaTestCatalog.Add(tests, "IteratorMemory", name, description, action);

        private static void Fault(DirectivaTestContext t, string body, RuntimeErrorId expected)
            => DILTestHarness.ExpectFault(t, DILTestHarness.Evaluate(t, body, locals: 1), expected);

        internal static void Register(List<DirectivaTestCase> tests)
        {
            Add(tests, "HeapCursorOwnsSource", "A cursor keeps its source alive under explicit GC", t =>
            {
                var memory = new VMMemoryContext(1, 32);
                uint text = memory.AllocHeap(new StringHeapObject("keep"));
                uint source = memory.AllocHeap(new ListHeapObject(new[] { DataValue.FromStringReference(text) }));
                uint iterator = memory.AllocHeap(new IteratorHeapObject(DataTypeId.List, source,
                    memory.GetHeap(source)));
                memory.SetGlobal(0, DataValue.FromIteratorReference(iterator));
                t.Equal(0, memory.CollectGarbage(), "all objects reachable through cursor");
                t.Equal(3, memory.Heap.ObjectCount, "source, child, cursor retained");
                memory.SetGlobal(0, DataValue.None());
                t.Equal(3, memory.CollectGarbage(), "entire iterator graph freed");
                t.Equal(0UL, memory.Heap.AllocatedSize, "no logical leak");
            });
            Add(tests, "MultipleRoots", "Two iterators may protect the same source", t =>
            {
                var mem = new VMMemoryContext(2, 40);
                uint list = mem.AllocHeap(new ListHeapObject(new[] { DataValue.FromInt(4) }));
                uint a = mem.AllocHeap(new IteratorHeapObject(DataTypeId.List, list, mem.GetHeap(list)));
                uint b = mem.AllocHeap(new IteratorHeapObject(DataTypeId.List, list, mem.GetHeap(list)));
                mem.SetGlobal(0, DataValue.FromIteratorReference(a));
                mem.SetGlobal(1, DataValue.FromIteratorReference(b));
                t.Equal(0, mem.CollectGarbage(), "all objects alive");
                mem.SetGlobal(0, DataValue.None());
                t.Equal(1, mem.CollectGarbage(), "first iterator swept");
                mem.SetGlobal(1, DataValue.None());
                t.Equal(2, mem.CollectGarbage(), "source and second cursor swept");
            });
            Add(tests, "LazyGCAtInit", "ITER_INIT triggers lazy GC and keeps the source rooted", t =>
            {
                var vm = DILTestHarness.MakeVM(t,
                    "PUSH STRING \"ab\"\nPOP\nPUSH INT 13\nLIST 1\nITER_INIT\n" +
                    "ITER_NEXT end\nRETURN\nend:\nPUSH NONE\nRETURN", heap: 7);
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "lazy collection execution");
                t.Equal(13L, unchecked((long)r.Value.value), "root survived GC");
                t.Check(vm.Memory.GarbageCollectionCount > 0, "GC happened");
            });
            Add(tests, "CursorAllocationFailure", "OOM on iterator creation leaves a script fault, not an internal exception", t =>
                DILTestHarness.ExpectFault(t,
                    DILTestHarness.Evaluate(t,
                        "PUSH INT 5\nLIST 1\nITER_INIT\nRETURN", heap: 5),
                    RuntimeErrorId.OutOfMemory));
            Add(tests, "MutationByAppend", "Appending during an iteration invalidates its cursor", t =>
                Fault(t, "PUSH INT 1\nLIST 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_INIT\n" +
                    "LOAD_LOCAL 0\nPUSH INT 2\nAPPEND\nPOP\n" +
                    "ITER_NEXT done\ndone:\nPUSH NONE\nRETURN", RuntimeErrorId.IteratorModified));
            Add(tests, "MutationBySet", "Changing a list element invalidates its cursor", t =>
                Fault(t, "PUSH INT 1\nLIST 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_INIT\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nPUSH INT 3\nSET\nPOP\n" +
                    "ITER_NEXT done\ndone:\nPUSH NONE\nRETURN", RuntimeErrorId.IteratorModified));
            Add(tests, "MutationBySetAdd", "Adding a Set member invalidates its cursor", t =>
                Fault(t, "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_INIT\n" +
                    "LOAD_LOCAL 0\nPUSH INT 2\nSET_ADD\nPOP\n" +
                    "ITER_NEXT done\ndone:\nPUSH NONE\nRETURN", RuntimeErrorId.IteratorModified));
            Add(tests, "MutationByDictSet", "Replacing a dictionary value invalidates key cursor conservatively", t =>
                Fault(t, "PUSH INT 1\nPUSH INT 7\nDICT 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_INIT\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nPUSH INT 99\nSET\nPOP\n" +
                    "ITER_NEXT done\ndone:\nPUSH NONE\nRETURN", RuntimeErrorId.IteratorModified));
            Add(tests, "NoMutationOnDuplicateSetAdd", "Adding a duplicate set member does not invalidate cursor", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nITER_INIT\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nSET_ADD\nPOP\n" +
                    "ITER_NEXT done\nPRINT\nITER_NEXT done\n" +
                    "done:\nPUSH NONE\nRETURN", locals: 1);
                t.Equal("1", string.Join(",", r.Output), "duplicate add unchanged");
            });
            Add(tests, "SourceRetainedAcrossFrames", "Cursor returned from one function keeps source alive across calls", t =>
            {
                string src = DILTestHarness.Function(
                    "CALL $test.make\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nITER_NEXT done\nRETURN\n" +
                    "done:\nPUSH NONE\nRETURN", 1) +
                    DILTestHarness.Function(
                        "PUSH INT 23\nLIST 1\nITER_INIT\nRETURN", 0, 0, "make");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string,string> { ["test"] = src });
                var vm = DILTestHarness.MakeVM(code, heap: 24);
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "returning cursor executed");
                t.Equal(23L, unchecked((long)r.Value.value), "value survived frame destruction");
            });
            Add(tests, "DeterministicDualVMProgress", "Shared iterator advances exactly once per deterministic VM step", t =>
            {
                var code = DILTestHarness.Direct(t, 1,
                    Instruction.LoadGlobal(0), Instruction.IterNext(3),
                    Instruction.Return(), Instruction.Push(DataValue.FromInt(-1)), Instruction.Return());
                var mem = new VMMemoryContext(1, 20);
                uint list = mem.AllocHeap(new ListHeapObject(new[] {
                    DataValue.FromInt(1), DataValue.FromInt(2) }));
                uint cursor = mem.AllocHeap(new IteratorHeapObject(DataTypeId.List, list, mem.GetHeap(list)));
                mem.SetGlobal(0, DataValue.FromIteratorReference(cursor));
                var a = DILTestHarness.MakeVM(code, memory: mem);
                var b = DILTestHarness.MakeVM(code, memory: mem);
                var ar = DILTestHarness.Run(t, a);
                var br = DILTestHarness.Run(t, b);
                t.Equal(VMExecutionStatus.Completed, ar.Status, "A completed");
                t.Equal(VMExecutionStatus.Completed, br.Status, "B completed");
                t.Equal(1L, unchecked((long)ar.Value.value), "first VM got first item");
                t.Equal(2L, unchecked((long)br.Value.value), "second VM got next item");
            });
            Add(tests, "VMFreeRootsAfterCompleted", "Completed VM return cursor keeps source alive", t =>
            {
                var code = DILTestHarness.Parse(t, "PUSH INT 3\nLIST 1\nITER_INIT\nRETURN");
                var mem = new VMMemoryContext(0, 16);
                var vm = DILTestHarness.MakeVM(code, memory: mem);
                t.Equal(VMExecutionStatus.Completed, DILTestHarness.Run(t, vm).Status, "complete");
                t.Equal(0, mem.CollectGarbage(), "return cursor root remains");
            });
            Add(tests, "LoneRangeReclaimed", "Range and iterator swept when no longer reachable", t =>
            {
                var mem = new VMMemoryContext(1, 12);
                uint range = mem.AllocHeap(new RangeHeapObject(1, 99999999, 1));
                uint iter = mem.AllocHeap(new IteratorHeapObject(DataTypeId.Range, range, mem.GetHeap(range)));
                mem.SetGlobal(0, DataValue.FromIteratorReference(iter));
                t.Equal(0, mem.CollectGarbage(), "range rooted through iterator");
                mem.SetGlobal(0, DataValue.None());
                t.Equal(2, mem.CollectGarbage(), "both freed");
            });
            Add(tests, "IteratorHeapAccounting", "Iterator and range each use constant-size accounting", t =>
            {
                var mem = new VMMemoryContext(1, 8);
                uint range = mem.AllocHeap(new RangeHeapObject(0, long.MaxValue, 1));
                uint iter = mem.AllocHeap(new IteratorHeapObject(DataTypeId.Range, range, mem.GetHeap(range)));
                mem.SetGlobal(0, DataValue.FromIteratorReference(iter));
                t.Equal(8UL, mem.Heap.AllocatedSize, "two compact objects");
                t.Equal(0, mem.CollectGarbage(), "both alive under root");
            });
            Add(tests, "IteratorSourceWrongHeapType", "Forged iterator points to invalid source object kind", t =>
            {
                // Range constructor validates source kind eagerly.
                var mem = new VMMemoryContext(0, 24);
                uint text = mem.AllocHeap(new StringHeapObject("not a list"));
                bool caught = false;
                try { new IteratorHeapObject(DataTypeId.List, text, mem.GetHeap(text)); }
                catch (ArgumentException) { caught = true; }
                t.Check(caught, "cursor refuses unrelated heap object");
            });
            Add(tests, "IteratorRangeStepNoAllocation", "Iteration advances without allocating a new heap object per value", t =>
            {
                var mem = new VMMemoryContext(0, 8);
                uint range = mem.AllocHeap(new RangeHeapObject(0, long.MaxValue, 1));
                var cursor = new IteratorHeapObject(DataTypeId.Range, range, mem.GetHeap(range));
                uint iter = mem.AllocHeap(cursor);
                int count = mem.Heap.ObjectCount;
                for (int i = 0; i < 1000; i++)
                {
                    t.Check(cursor.TryGetNext(mem, out var item, out RuntimeError error),
                        "range step " + i + ": " + error?.Message);
                    t.Equal((long)i, unchecked((long)item.value), "range value " + i);
                }
                t.Equal(count, mem.Heap.ObjectCount, "constant object count");
                t.Equal(8UL, mem.Heap.AllocatedSize, "constant heap charge");
            });
        }
    }
}
