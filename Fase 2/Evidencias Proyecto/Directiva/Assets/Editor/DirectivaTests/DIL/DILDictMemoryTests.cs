using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.EditorTests.DIL
{
    /// <summary>Two-slot charging, GC, resize pressure and continued-buffer safety.</summary>
    internal static class DILDictMemoryTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DirectivaTestCatalog.Add(tests, "DictMemory", id, description, action);

            Add("EmptyCharge", "Empty DICT charges one logical heap unit", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t, "DICT 0\nRETURN", heap: 1);
                t.Equal(1UL, r.VM.Memory.Heap.AllocatedSize, "empty charge");
            });
            Add("OnePairCharge", "One key/value entry costs three logical units", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nRETURN", heap: 3);
                t.Equal(3UL, r.VM.Memory.Heap.AllocatedSize, "one pair charge");
            });
            Add("TwoPairsCharge", "Each additional unique entry charges exactly two units", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nPUSH INT 4\nDICT 2\nRETURN", heap: 5);
                t.Equal(5UL, r.VM.Memory.Heap.AllocatedSize, "two pair charge");
            });
            Add("DuplicateCharge", "Duplicate key is charged once even across numeric types", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH BOOL TRUE\nPUSH INT 3\nDICT 2\nRETURN", heap: 3);
                t.Equal(3UL, r.VM.Memory.Heap.AllocatedSize, "duplicate charge");
            });
            Add("ConstructorOOM", "Constructor refuses more pairs than available heap", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nPUSH INT 4\nDICT 2\nRETURN", heap: 4);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Check(r.VM.Memory.Heap.AllocatedSize <= 4UL, "limit respected");
            });
            Add("SetOOMAtomic", "Insertion at heap limit fails without changing map", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 3\nPUSH INT 4\nSET\nRETURN", locals: 1);
                var vm = DILTestHarness.MakeVM(code, heap: 3);
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.OutOfMemory);
                var dict = (DictHeapObject)vm.Memory.GetHeap(1);
                t.Equal(1, dict.Count, "entry count unchanged");
                t.Equal(2L, unchecked((long)dict.GetValue(0).value), "original value intact");
                t.Equal(3UL, vm.Memory.Heap.AllocatedSize, "charged size intact");
                t.Check(vm.Memory.GarbageCollectionCount > 0, "pressure collection attempted");
            });
            Add("OverwriteFullHeap", "Overwriting an existing key does not allocate slots", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nSTORE_LOCAL 0\nLOAD_LOCAL 0\n" +
                    "PUSH BOOL TRUE\nPUSH INT 99\nSET\nPOP\nLOAD_LOCAL 0\nPUSH INT 1\nGET\nRETURN",
                    locals: 1, heap: 3);
                t.Equal(99L, unchecked((long)r.Value.value), "updated value");
                t.Equal(3UL, r.VM.Memory.Heap.AllocatedSize, "no new charge");
            });
            Add("RemoveReleasesTwo", "DELETE releases two slots", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 2\nPUSH INT 3\nPUSH INT 4\nDICT 2\nSTORE_LOCAL 0\n" +
                    "LOAD_LOCAL 0\nPUSH INT 1\nDELETE\nPOP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(3UL, r.VM.Memory.Heap.AllocatedSize, "two units released");
            });
            Add("PopReleasesTwo", "POP_ITEM shrinks by two slots", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 20\nDICT 1\nPUSH INT 1\nPOP_ITEM\nRETURN");
                t.Equal(1UL, r.VM.Memory.Heap.AllocatedSize, "empty map remains");
            });
            Add("ClearReleasesAll", "DICT_CLEAR returns to one logical unit", t =>
            {
                var r = DILTestHarness.ExpectCompleted(t,
                    "PUSH INT 1\nPUSH INT 11\nPUSH INT 2\nPUSH INT 22\nDICT 2\n" +
                    "STORE_LOCAL 0\nLOAD_LOCAL 0\nDICT_CLEAR\nPOP\nLOAD_LOCAL 0\nRETURN", locals: 1);
                t.Equal(1UL, r.VM.Memory.Heap.AllocatedSize, "empty dictionary");
            });
            Add("ResizePressureGC", "TryResizeHeap retries after collecting unrelated garbage", t =>
            {
                var mem = new VMMemoryContext(1, 7);
                uint dictId = mem.AllocHeap(new DictHeapObject());
                mem.SetGlobal(0, DataValue.FromDictReference(dictId));
                uint garbage = mem.AllocHeap(new StringHeapObject("abcdefgh")); // three units
                t.Check(mem.TryResizeHeap(dictId, 5), "GC frees enough space");
                var dict = (DictHeapObject)mem.GetHeap(dictId);
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromInt(2), 1);
                dict.AddAuthorized(DataValue.FromInt(3), DataValue.FromInt(4), 3);
                t.Equal(5UL, mem.Heap.AllocatedSize, "accurate charge");
                t.Check(!mem.TryGetHeap(garbage, out _), "garbage was collected");
            });
            Add("KeyAndValueReferences", "GC traverses both keys and values", t =>
            {
                var mem = new VMMemoryContext(1);
                uint key = mem.AllocHeap(new StringHeapObject("key"));
                uint value = mem.AllocHeap(new StringHeapObject("value"));
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromStringReference(key), DataValue.FromStringReference(value), 123);
                uint id = mem.AllocHeap(dict);
                mem.SetGlobal(0, DataValue.FromDictReference(id));
                mem.CollectGarbage();
                t.Check(mem.TryGetHeap(key, out _), "key retained");
                t.Check(mem.TryGetHeap(value, out _), "value retained");
                t.Check(mem.TryGetHeap(id, out _), "dictionary retained");
            });
            Add("NestedValuesGC", "Dict -> List -> Tuple -> String survives GC", t =>
            {
                var mem = new VMMemoryContext(1);
                uint str = mem.AllocHeap(new StringHeapObject("nested"));
                uint tuple = mem.AllocHeap(new TupleHeapObject(new[] { DataValue.FromStringReference(str) }));
                uint list = mem.AllocHeap(new ListHeapObject(new[] { DataValue.FromTupleReference(tuple) }));
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(7), DataValue.FromListReference(list), 7);
                uint id = mem.AllocHeap(dict);
                mem.SetGlobal(0, DataValue.FromDictReference(id));
                mem.CollectGarbage();
                foreach (uint reference in new[] { id, list, tuple, str })
                    t.Check(mem.TryGetHeap(reference, out _), "reachable " + reference);
            });
            Add("SelfCycleCollected", "A cycle is collected after the final external root disappears", t =>
            {
                var mem = new VMMemoryContext(1);
                var dict = new DictHeapObject();
                uint id = mem.AllocHeap(dict);
                t.Check(mem.TryResizeHeap(id, 3), "reserve self entry");
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromDictReference(id), 1);
                mem.SetGlobal(0, DataValue.FromDictReference(id));
                mem.CollectGarbage();
                t.Check(mem.TryGetHeap(id, out _), "live self-cycle");
                mem.SetGlobal(0, DataValue.None());
                mem.CollectGarbage();
                t.Check(!mem.TryGetHeap(id, out _), "orphan self-cycle swept");
                t.Equal(0UL, mem.Heap.AllocatedSize, "all memory reclaimed");
            });
            Add("TwoDictCycle", "Two mutually-referencing dictionaries are reclaimed as a cycle", t =>
            {
                var mem = new VMMemoryContext(1);
                var a = new DictHeapObject(); var b = new DictHeapObject();
                uint aid = mem.AllocHeap(a); uint bid = mem.AllocHeap(b);
                t.Check(mem.TryResizeHeap(aid, 3), "reserve A");
                a.AddAuthorized(DataValue.FromInt(1), DataValue.FromDictReference(bid), 1);
                t.Check(mem.TryResizeHeap(bid, 3), "reserve B");
                b.AddAuthorized(DataValue.FromInt(2), DataValue.FromDictReference(aid), 2);
                mem.SetGlobal(0, DataValue.FromDictReference(aid));
                mem.CollectGarbage();
                t.Check(mem.TryGetHeap(bid, out _), "B reachable through A");
                mem.SetGlobal(0, DataValue.None());
                mem.CollectGarbage();
                t.Equal(0, mem.Heap.ObjectCount, "cycle reclaimed");
            });
            Add("UnreachableChildren", "Unreachable dictionary and its unique child are collected", t =>
            {
                var mem = new VMMemoryContext(0);
                uint value = mem.AllocHeap(new StringHeapObject("unreachable"));
                var dict = new DictHeapObject();
                dict.AddAuthorized(DataValue.FromInt(1), DataValue.FromStringReference(value), 1);
                uint id = mem.AllocHeap(dict);
                t.Equal(2, mem.CollectGarbage(), "dead objects swept");
                t.Check(!mem.TryGetHeap(id, out _), "dict collected");
                t.Check(!mem.TryGetHeap(value, out _), "value collected");
            });
            Add("CopyOOM", "COPY buffers are charged and may fail under full heap", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 2\nDICT 1\nCOPY\nRETURN", heap: 3);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
            });
            Add("UpdateOOMAtomic", "Failed DICT_UPDATE cannot publish partially copied entries", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nSTORE_LOCAL 0\n" +
                    "PUSH INT 2\nPUSH INT 20\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nRETURN", locals: 2);
                var vm = DILTestHarness.MakeVM(code, heap: 7);
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.OutOfMemory);
                var dst = (DictHeapObject)vm.Memory.GetHeap(1);
                t.Equal(1, dst.Count, "original unchanged");
                t.Equal(1L, unchecked((long)dst.GetKey(0).value), "original key");
                t.Equal(10L, unchecked((long)dst.GetValue(0).value), "original value");
            });
            Add("ViewsOOM", "DICT_ITEMS may fail on extra item tuple allocations", t =>
            {
                var r = DILTestHarness.Evaluate(t,
                    "PUSH INT 1\nPUSH INT 10\nDICT 1\nDICT_ITEMS\nRETURN", heap: 5);
                DILTestHarness.ExpectFault(t, r, RuntimeErrorId.OutOfMemory);
                t.Check(r.VM.Memory.Heap.AllocatedSize <= 5, "logical heap cap respected");
            });
            Add("PendingViewGC", "Aux buffer and source are rooted while view is continued", t =>
            {
                var code = DILTestHarness.Parse(t, DILDictViewTests.Integers(25) + "DICT_ITEMS\nRETURN");
                var vm = DILTestHarness.MakeVM(code);
                for (int i = 0; i < 51; i++) vm.Advance(); // 50 pushes + DICT
                vm.Advance(); // first DICT_ITEMS step stages the auxiliary List
                t.Equal(VMExecutionStatus.Running, vm.Status, "operation staged");
                vm.Memory.CollectGarbage();
                var r = DILTestHarness.Run(t, vm);
                t.Equal(VMExecutionStatus.Completed, r.Status, "operation continues after GC");
                var list = (ListHeapObject)vm.Memory.GetHeap((uint)r.Value.value);
                t.Equal(25, list.Count, "all tuples survived GC");
            });
            Add("NoLeakedBufferAfterFailure", "Failed buffered update no longer roots its auxiliary object", t =>
            {
                var code = DILTestHarness.Parse(t,
                    "PUSH INT 1\nPUSH INT 1\nDICT 1\nSTORE_LOCAL 0\n" +
                    "PUSH INT 2\nPUSH INT 2\nDICT 1\nSTORE_LOCAL 1\n" +
                    "LOAD_LOCAL 0\nLOAD_LOCAL 1\nDICT_UPDATE\nRETURN", locals: 2);
                var vm = DILTestHarness.MakeVM(code, heap: 7);
                DILTestHarness.ExpectFault(t, DILTestHarness.Run(t, vm), RuntimeErrorId.OutOfMemory);
                int before = vm.Memory.Heap.ObjectCount;
                vm.Memory.CollectGarbage();
                t.Check(vm.Memory.Heap.ObjectCount < before, "abandoned aux reclaimed");
            });
        }
    }
}
