using System;
using System.Collections.Generic;
using System.Linq;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Mutable heap charges, lazy GC, auxiliary roots and failure atomicity.</summary>
    internal static class DILListMemoryTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string why, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "ListMemory", id, why, action);

            Add("TryResizeAuthorizeAndFree", "Heap charge tracks growth and shrink independently of list Capacity", t =>
            {
                var heap = new HeapMemory(8);
                var list = new ListHeapObject(Array.Empty<DataValue>());
                uint reference = heap.Allocate(list);
                t.Equal(1UL, heap.AllocatedSize, "empty allocation");
                t.Check(heap.TryResize(reference, 2), "growth reservation");
                list.AppendAuthorized(DataValue.FromInt(5));
                t.Equal(2UL, heap.AllocatedSize, "growth charge");
                t.Equal(2u, list.Size, "size committed");
                t.Check(heap.TryResize(reference, 1), "shrink reservation");
                list.RemoveLastAuthorized();
                t.Equal(1UL, heap.AllocatedSize, "shrink charge");
                t.Check(heap.Free(reference), "free list");
                t.Equal(0UL, heap.AllocatedSize, "free subtracts reserved charge");
            });
            Add("ResizeRejection", "Failed resize leaves both charge and object unchanged", t =>
            {
                var heap = new HeapMemory(3);
                var list = new ListHeapObject(new[] { DataValue.FromInt(1) });
                uint reference = heap.Allocate(list);
                t.Check(!heap.TryResize(reference, 4), "oversized resize must reject");
                t.Equal(2UL, heap.AllocatedSize, "failed resize charge");
                t.Equal(1, list.Count, "failed resize contents");
                t.Equal(1L, unchecked((long)list.GetElement(0).value), "preserved value");
            });
            Add("ResizePressureCollectsGarbage", "TryResizeHeap triggers lazy mark-and-sweep and retries", t =>
            {
                var memory = new VMMemoryContext(1, 7);
                var list = new ListHeapObject(new[] { DataValue.FromInt(1) });
                uint id = memory.AllocHeap(list);
                memory.SetGlobal(0, DataValue.FromListReference(id));
                uint dead = memory.AllocHeap(new StringHeapObject("abcdefgh")); // 3 units
                t.Equal(5UL, memory.Heap.AllocatedSize, "initial heap charge");
                t.Check(memory.TryResizeHeap(id, 6), "GC must allow growth to six units");
                t.Check(!memory.TryGetHeap(dead, out _), "unreachable object must be collected");
                // Commit the authorized delta before any later heap operation.
                for (int i = 0; i < 4; i++) list.AppendAuthorized(DataValue.FromInt(i));
                t.Equal(6u, list.Size, "logical list size after commit");
                t.Equal(6UL, memory.Heap.AllocatedSize, "heap after GC and growth");
                t.Check(memory.GarbageCollectionCount > 0, "resize should have collected");
            });
            Add("SwapChargesAtomic", "Swapping two prepared lists exchanges charges without extra memory", t =>
            {
                var memory = new VMMemoryContext(0, 8);
                var a = new ListHeapObject(new[] { DataValue.FromInt(1) });
                var b = new ListHeapObject(new[] {
                    DataValue.FromInt(2), DataValue.FromInt(3), DataValue.FromInt(4) });
                uint aRef = memory.AllocHeap(a);
                uint bRef = memory.AllocHeap(b);
                t.Equal(6UL, memory.Heap.AllocatedSize, "both lists charged");
                t.Check(memory.TrySwapHeapContents(aRef, bRef), "swap authorization");
                t.Equal(3, a.Count, "target received prepared contents");
                t.Equal(1, b.Count, "aux received old contents");
                t.Equal(6UL, memory.Heap.AllocatedSize, "swap has zero net delta");
                memory.FreeHeap(bRef);
                t.Equal(4UL, memory.Heap.AllocatedSize, "post-swap aux cleanup charge");
                t.Equal(4u, ((ListHeapObject)memory.GetHeap(aRef)).Size, "target accounting");
            });
            Add("ListAppendOOMIsAtomic", "OOM on APPEND does not mutate destination", t =>
            {
                var code = DILTestHarness.Parse(t, "PUSH INT 1\nPUSH INT 2\nLIST 2\n" +
                    "STORE_LOCAL 0\nLOAD_LOCAL 0\nPUSH INT 3\nAPPEND\nRETURN", locals: 1);
                var vm = DILTestHarness.MakeVM(code, heap: 3);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                var list = (ListHeapObject)vm.Memory.GetHeap(1);
                t.Equal(2, list.Count, "list unchanged on OOM");
                t.Equal(3UL, vm.Memory.Heap.AllocatedSize, "charge unchanged");
                t.Check(vm.Memory.GarbageCollectionCount > 0, "GC attempted on resize pressure");
            });
            Add("InsertOOMIsAtomic", "OOM during prepared INSERT never partially mutates the destination", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nPUSH INT 3\nLIST 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 0\nPUSH INT 2\nINSERT\nRETURN", locals: 1);
                var vm = DILTestHarness.MakeVM(code, heap: 4);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                var list = (ListHeapObject)vm.Memory.GetHeap(1);
                t.Equal(2, list.Count, "destination count unchanged");
                t.Equal(3L, unchecked((long)list.GetElement(1).value), "old tail unchanged");
            });
            Add("ExtendOOMIsAtomic", "OOM during staged EXTEND leaves both input lists intact", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nPUSH INT 2\nLIST 2\nSTORE_LOCAL 0\n" +
                    "PUSH INT 3\nPUSH INT 4\nLIST 2\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nEXTEND\nRETURN", locals: 2);
                var vm = DILTestHarness.MakeVM(code, heap: 7);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Equal(2, ((ListHeapObject)vm.Memory.GetHeap(1)).Count, "target intact");
                t.Equal(2, ((ListHeapObject)vm.Memory.GetHeap(2)).Count, "source intact");
            });
            Add("ListConstructionStackAtomic", "LIST x failure preserves stack and does not allocate", t =>
            {
                var code = DILTestHarness.Direct(t, 0,
                    Instruction.Push(DataValue.FromInt(10)), Instruction.Push(DataValue.FromInt(20)),
                    Instruction.List(2), Instruction.Return());
                var vm = DILTestHarness.MakeVM(code, heap: 2);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Equal(2, vm.EvaluationStackCount, "stack preserved");
                t.Equal(0UL, vm.Memory.Heap.AllocatedSize, "no rejected allocation charged");
            });
            Add("ListSelfCycleGC", "Mark-and-sweep retains rooted self-cycle and reclaims it unrooted", t =>
            {
                var memory = new VMMemoryContext(1, 6);
                var list = new ListHeapObject(Array.Empty<DataValue>());
                uint reference = memory.AllocHeap(list);
                memory.SetGlobal(0, DataValue.FromListReference(reference));
                t.Check(memory.TryResizeHeap(reference, 2), "reserve for cycle");
                list.AppendAuthorized(DataValue.FromListReference(reference));
                t.Equal(0, memory.CollectGarbage(), "rooted cyclic list retained");
                memory.SetGlobal(0, DataValue.None());
                t.Equal(1, memory.CollectGarbage(), "unrooted cycle reclaimed");
                t.Equal(0UL, memory.Heap.AllocatedSize, "no leak after cycle sweep");
            });
            Add("NestedListRefsGC", "GC discovers nested String/Tuple/List references", t =>
            {
                var memory = new VMMemoryContext(1, 30);
                uint text = memory.AllocHeap(new StringHeapObject("keep"));
                uint tuple = memory.AllocHeap(new TupleHeapObject(new[] { DataValue.FromStringReference(text) }));
                uint list = memory.AllocHeap(new ListHeapObject(new[] {
                    DataValue.FromTupleReference(tuple), DataValue.FromStringReference(text) }));
                memory.SetGlobal(0, DataValue.FromListReference(list));
                t.Equal(0, memory.CollectGarbage(), "all objects reachable");
                memory.SetGlobal(0, DataValue.None());
                t.Equal(3, memory.CollectGarbage(), "unrooted graph swept");
            });
            Add("AuxiliaryRootSurvivesExplicitGC", "GC sees a paused continued COPY private auxiliary", t =>
            {
                string pushes = string.Join("\n", Enumerable.Range(0, 40).Select(i => "PUSH INT " + i));
                var code = DILTestHarness.Parse(t,
                    pushes + "\nLIST 40\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCOPY\nRETURN", locals: 1);
                var mem = new VMMemoryContext(0, 90);
                var vm = DILTestHarness.MakeVM(code, memory: mem);
                for (int i = 0; i < 44; i++) vm.Advance(); // COPY just started and allocated aux
                t.Equal(VMExecutionStatus.Running, vm.Status, "paused state");
                t.Equal(2, mem.Heap.ObjectCount, "original and auxiliary both exist");
                t.Equal(0, mem.CollectGarbage(), "auxiliary rooted during continuation");
                var result = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, result.Status, "COPY after GC should complete");
                t.Equal(40, ((ListHeapObject)mem.GetHeap((uint)result.Value.value)).Count,
                    "copy kept all 40 entries");
            });
            Add("AuxiliaryGCUnderPressure", "COPY allocates under memory pressure while VM roots aux", t =>
            {
                string pushes = string.Join("\n", Enumerable.Range(0, 40).Select(i => "PUSH INT " + i));
                var code = DILTestHarness.Parse(t,
                    pushes + "\nLIST 40\nSTORE_LOCAL 0\nLOAD_LOCAL 0\nCOPY\nRETURN", locals: 1);
                var memory = new VMMemoryContext(0, 82);
                uint dead = memory.AllocHeap(new StringHeapObject("deadABCDE")); // 4 units
                var r = DILTestHarness.Run(t, DILTestHarness.MakeVM(code, memory: memory), 200);
                t.Equal(VMExecutionStatus.Completed, r.Status, "copy under heap pressure");
                t.Equal(40, ((ListHeapObject)memory.GetHeap((uint)r.Value.value)).Count,
                    "copy intact despite collection");
                t.Check(memory.GarbageCollectionCount > 0, "expected lazy GC");
                t.Check(!memory.TryGetHeap(dead, out _), "dead temporary reclaimed");
            });
            Add("DeepListConstruction", "256-item list supports indexing and correct logical memory charge", t =>
            {
                string pushes = string.Join("\n", Enumerable.Range(0, 256).Select(i => "PUSH INT " + i));
                var r = DILTestHarness.ExpectCompleted(t,
                    pushes + "\nLIST 256\nRETURN", budget: 300);
                var list = (ListHeapObject)r.VM.Memory.GetHeap((uint)r.Value.value);
                t.Equal(256, list.Count, "deep count");
                t.Equal(257u, list.Size, "size charged");
                t.Equal(255L, unchecked((long)list.GetElement(255).value), "last item");
            });
            Add("AuxiliaryStopsRootingOnFault", "An abandoned continuation buffer is eventually swept", t =>
            {
                string text = "# GLOBAL : target\n" + DILTestHarness.Function(
                    "LOAD_GLOBAL $target\nPUSH INT 0\nPUSH INT 99\nINSERT\nRETURN");
                var code = DILTestHarness.ParseModules(t, "test", new Dictionary<string, string> {
                    ["test"] = text
                });
                var memory = new VMMemoryContext(1, 4);
                var initial = new ListHeapObject(new[] { DataValue.FromInt(1), DataValue.FromInt(2) });
                uint saved = memory.AllocHeap(initial);
                memory.SetGlobal(0, DataValue.FromListReference(saved));
                var vm = DILTestHarness.MakeVM(code, memory: memory);
                var r = DILTestHarness.Run(t, vm);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Equal(2, memory.Heap.ObjectCount, "aux buffer allocated and orphaned");
                memory.CollectGarbage();
                t.Check(memory.TryGetHeap(saved, out _), "saved global must survive fault collection");
                t.Equal(1, memory.Heap.ObjectCount, "abandoned auxiliary reclaimed");
            });
        }
    }
}
