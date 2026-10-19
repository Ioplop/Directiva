using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Set heap charging, mark/sweep child roots and failure atomicity.</summary>
    internal static class DILSetMemoryTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "SetMemory", id, why, action);

            Add("ConstructorCharge", "SET x is charged by unique members not input count", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 1\nPUSH INT 2\nSET 3\nRETURN", heap: 3);
                t.Equal(3UL, r.VM.Memory.Heap.AllocatedSize, "1 + 2 unique members");
            });
            Add("ConstructorOOM", "Oversized set allocation fails without exceeding heap limit", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nSET 3\nRETURN", heap: 3);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Check(r.VM.Memory.Heap.AllocatedSize <= 3UL, "heap limit never exceeded");
            });
            Add("AddOOM", "Add fails atomically when heap is full", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nPUSH INT 2\nSET 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 3\nSET_ADD\nRETURN", locals: 1);
                var vm = DILTestHarness.MakeVM(code, heap: 3);
                var run = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, run, RuntimeErrorId.OutOfMemory);
                var set = (SetHeapObject)vm.Memory.GetHeap(1);
                t.Equal(2, set.Count, "no partial add");
                t.Equal(3UL, vm.Memory.Heap.AllocatedSize, "heap charge unchanged");
                t.Check(vm.Memory.GarbageCollectionCount > 0, "GC attempted");
            });
            Add("DuplicateAtLimit", "Duplicate add succeeds at exact heap capacity", t =>
            {
                var run = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nSET 1\nPUSH BOOL TRUE\nSET_ADD\nRETURN", heap: 2);
                t.Equal(DataTypeId.None, run.Value.dataType, "duplicate add result");
                t.Equal(2UL, run.VM.Memory.Heap.AllocatedSize, "no extra charge");
            });
            Add("RemoveReleasesCharge", "Deletion releases a logical slot", t =>
            {
                var run = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nSET 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 2\nSET_REMOVE\nPOP\n" +
                    "LOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(2UL, run.VM.Memory.Heap.AllocatedSize, "shrunk set charged two units");
            });
            Add("PopReleasesCharge", "Pop returns element and releases one slot", t =>
            {
                var run = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 5\nPUSH INT 6\nSET 2\nSET_POP\nRETURN");
                t.Equal(2UL, run.VM.Memory.Heap.AllocatedSize, "set retains one member");
                t.Equal(6L, unchecked((long)run.Value.value), "popped tail");
            });
            Add("ResizeFailureLeavesCharge", "HeapMemory rejects invalid expansion without changing contents", t =>
            {
                var memory = new HeapMemory(2);
                var set = new SetHeapObject();
                uint id = memory.Allocate(set);
                t.Check(!memory.TryResize(id, 3), "resize must reject");
                t.Equal(1UL, memory.AllocatedSize, "unchanged charge");
                t.Equal(0, set.Count, "unchanged elements");
            });
            Add("ResizePressureCollectsGarbage", "TryResizeHeap can collect dead objects to authorize growth", t =>
            {
                var memory = new VMMemoryContext(1, 7);
                var set = new SetHeapObject();
                uint id = memory.AllocHeap(set);
                memory.SetGlobal(0, DataValue.FromSetReference(id));
                uint dead = memory.AllocHeap(new StringHeapObject("abcdefgh")); // 3 units
                t.Equal(4UL, memory.Heap.AllocatedSize, "before pressure");
                t.Check(memory.TryResizeHeap(id, 6), "lazy GC should authorize six units");
                for (int i = 0; i < 5; i++) set.AddAuthorized(DataValue.FromInt(i), i);
                t.Equal(6u, set.Size, "committed resize");
                t.Equal(6UL, memory.Heap.AllocatedSize, "charged size");
                t.Check(!memory.TryGetHeap(dead, out _), "garbage collected");
                t.Check(memory.GarbageCollectionCount > 0, "GC was triggered");
            });
            Add("ReachableStringsRetained", "Set GetReferences keeps member strings alive", t =>
            {
                var memory = new VMMemoryContext(1, 100);
                uint stringId = memory.AllocHeap(new StringHeapObject("root"));
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromStringReference(stringId), 17);
                uint setRef = memory.AllocHeap(set);
                memory.SetGlobal(0, DataValue.FromSetReference(setRef));
                uint dead = memory.AllocHeap(new StringHeapObject("unreachable"));
                memory.CollectGarbage();
                t.Check(memory.TryGetHeap(stringId, out _), "referenced string retained");
                t.Check(memory.TryGetHeap(setRef, out _), "rooted set retained");
                t.Check(!memory.TryGetHeap(dead, out _), "unrooted sibling collected");
            });
            Add("NestedRoots", "List -> Set -> Tuple -> String references survive mark/sweep", t =>
            {
                var mem = new VMMemoryContext(1);
                uint str = mem.AllocHeap(new StringHeapObject("deep"));
                uint tuple = mem.AllocHeap(new TupleHeapObject(new[] { DataValue.FromStringReference(str) }));
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromTupleReference(tuple), 42);
                uint setRef = mem.AllocHeap(set);
                uint list = mem.AllocHeap(new ListHeapObject(new[] { DataValue.FromSetReference(setRef) }));
                mem.SetGlobal(0, DataValue.FromListReference(list));
                mem.CollectGarbage();
                foreach (uint reference in new[] { str, tuple, setRef, list })
                    t.Check(mem.TryGetHeap(reference, out _), "nested ref " + reference + " retained");
            });
            Add("UnreachableSetSwept", "Unreachable set and its unrooted children are both swept", t =>
            {
                var mem = new VMMemoryContext(0);
                uint str = mem.AllocHeap(new StringHeapObject("orphan"));
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromStringReference(str), 3);
                uint id = mem.AllocHeap(set);
                int collected = mem.CollectGarbage();
                t.Equal(2, collected, "both heap objects collected");
                t.Check(!mem.TryGetHeap(str, out _), "string collected");
                t.Check(!mem.TryGetHeap(id, out _), "set collected");
                t.Equal(0UL, mem.Heap.AllocatedSize, "empty heap charge");
            });
            Add("SetInsideCyclicList", "Cycle containing a set survives while rooted, dies when root cleared", t =>
            {
                var memory = new VMMemoryContext(1);
                var list = new ListHeapObject(Array.Empty<DataValue>());
                uint listId = memory.AllocHeap(list);
                var set = new SetHeapObject();
                set.AddAuthorized(DataValue.FromInt(5), 5);
                uint setId = memory.AllocHeap(set);
                // List can contain a set and itself; Set cannot contain mutable objects.
                t.Check(memory.TryResizeHeap(listId, 3), "reserve two children");
                list.AppendAuthorized(DataValue.FromSetReference(setId));
                list.AppendAuthorized(DataValue.FromListReference(listId));
                memory.SetGlobal(0, DataValue.FromListReference(listId));
                memory.CollectGarbage();
                t.Check(memory.TryGetHeap(setId, out _), "child reachable in cycle");
                memory.SetGlobal(0, DataValue.None());
                memory.CollectGarbage();
                t.Check(!memory.TryGetHeap(setId, out _), "set cycle now garbage");
                t.Check(!memory.TryGetHeap(listId, out _), "list cycle now garbage");
            });
            Add("UpdateOOMIsAtomic", "Failed buffered SetUpdate never publishes half-built state", t =>
            {
                string body = "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\n" +
                    "PUSH INT 2\nSET 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nSET_UPDATE\nRETURN";
                var code = DILTestHarness.Parse(t, body, locals: 2);
                var vm = DILTestHarness.MakeVM(code, heap: 5);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                var a = (SetHeapObject)vm.Memory.GetHeap(1);
                t.Equal(1, a.Count, "original destination unchanged");
                t.Equal(1L, unchecked((long)a.GetElement(0).value), "old member retained");
                vm.Memory.CollectGarbage();
                t.Check(vm.Memory.Heap.AllocatedSize <= 5UL, "no heap overflow");
            });
            Add("AuxiliarySweptAfterFailedUpdate", "Failed continued operation releases its private GC root", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\nPUSH INT 2\nSET 1\n" +
                    "STORE_LOCAL 1\nLOAD_LOCAL 0\nLOAD_LOCAL 1\nSET_UPDATE\nRETURN",
                    locals: 2);
                var vm = DILTestHarness.MakeVM(code, heap: 5);
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.OutOfMemory);
                // A faulted VM has no roots, and its private aux is no longer pinned.
                vm.Memory.CollectGarbage();
                t.Equal(0, vm.Memory.Heap.ObjectCount, "all abandoned objects reclaimed");
                t.Equal(0UL, vm.Memory.Heap.AllocatedSize, "all logical charges released");
            });
            Add("SetCopyGC", "Copied set can outlive source references with shared String child", t =>
            {
                var run = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"child\"\nSET 1\nCOPY\nRETURN", heap: 30);
                uint id = (uint)run.Value.value;
                run.VM.Memory.CollectGarbage();
                var set = (SetHeapObject)run.VM.Memory.GetHeap(id);
                t.Equal(1, set.Count, "copied set exists");
                uint child = (uint)set.GetElement(0).value;
                t.Check(run.VM.Memory.TryGetHeap(child, out _), "string preserved through copy");
            });
            Add("UpdateChargeSwapped", "Successful update swaps charged contents without leaking aux", t =>
            {
                var run = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nSET 1\nSTORE_LOCAL 0\n" +
                    "PUSH INT 2\nSET 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nSET_UPDATE\nPOP\n" +
                    "LOAD_LOCAL 0\nRETURN", locals: 2, heap: 10);
                t.Equal(5UL, run.VM.Memory.Heap.AllocatedSize, "3 plus 2 logical units");
                t.Equal(2, run.VM.Memory.Heap.ObjectCount, "aux freed after commit");
            });
            Add("LargeSetRootFromReturn", "Returned set is rooted by completed VM during GC", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH STRING \"alive\"\nSET 1\nRETURN");
                uint refId = (uint)r.Value.value;
                r.VM.Memory.CollectGarbage();
                t.Check(r.VM.Memory.TryGetHeap(refId, out HeapObject set), "completed return root alive");
                uint stringRef = (uint)((SetHeapObject)set).GetElement(0).value;
                t.Check(r.VM.Memory.TryGetHeap(stringRef, out _), "member string alive");
            });
            Add("StressAddRemove", "Repeated changes preserve counts and charges without stale buckets", t =>
            {
                var memory = new VMMemoryContext(1, 400);
                var set = new SetHeapObject();
                uint id = memory.AllocHeap(set);
                memory.SetGlobal(0, DataValue.FromSetReference(id));
                for (int i = 0; i < 150; i++)
                {
                    t.Check(memory.TryResizeHeap(id, set.Size + 1u), "add authorization " + i);
                    set.AddAuthorized(DataValue.FromInt(i), i);
                }
                t.Equal(151UL, memory.Heap.AllocatedSize, "after grow");
                for (int i = 0; i < 150; i += 2)
                {
                    t.Check(set.TryFind(DataValue.FromInt(i), i, memory, out int pos,
                        out RuntimeError error), "lookup after swaps " + (error?.Message ?? ""));
                    t.Check(pos >= 0, "must find even value " + i);
                    t.Check(memory.TryResizeHeap(id, set.Size - 1u), "remove authorization " + i);
                    set.RemoveAuthorized(pos);
                }
                t.Equal(75, set.Count, "75 surviving odds");
                t.Equal(76UL, memory.Heap.AllocatedSize, "logical charge after removals");
                for (int i = 1; i < 150; i += 2)
                {
                    t.Check(set.TryFind(DataValue.FromInt(i), i, memory, out int pos, out _), "lookup valid");
                    t.Check(pos >= 0, "remaining odd value " + i);
                }
            });
        }
    }
}
