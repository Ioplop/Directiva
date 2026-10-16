using System;
using System.Collections.Generic;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Memory shared by VMs that execute the same code context: heap plus global slots.
    /// Locals and evaluation stacks remain private to each VM.
    /// </summary>
    public sealed class VMMemoryContext : IMemContext
    {
        private readonly DataValue[] globals;
        private readonly HeapMemory heap;
        private readonly List<WeakReference<DirectivaVM>> vms = new();

        public int GlobalCount => globals.Length;
        public HeapMemory Heap => heap;
        public ulong GarbageCollectionCount { get; private set; }

        public VMMemoryContext(int globalCount)
            : this(globalCount, new HeapMemory())
        {
        }

        public VMMemoryContext(int globalCount, ulong maxHeapSize)
            : this(globalCount, new HeapMemory(maxHeapSize))
        {
        }

        public VMMemoryContext(int globalCount, HeapMemory heap)
        {
            if (globalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(globalCount));

            this.heap = heap ?? throw new ArgumentNullException(nameof(heap));
            globals = new DataValue[globalCount];
            ResetGlobals();
        }

        internal void RegisterVM(DirectivaVM vm)
        {
            if (vm == null)
                throw new ArgumentNullException(nameof(vm));

            vms.Add(new WeakReference<DirectivaVM>(vm));
        }

        public HeapObject GetHeap(uint memRef)
            => heap.Get(memRef);

        public bool TryGetHeap(uint memRef, out HeapObject heapObject)
            => heap.TryGet(memRef, out heapObject);

        public uint AllocHeap(HeapObject obj)
        {
            if (TryAllocHeap(obj, out uint memRef))
                return memRef;

            throw new InvalidOperationException(
                $"Directiva heap is out of memory ({heap.AllocatedSize}/{heap.MaxSize} units)."
            );
        }

        /// <summary>
        /// Attempts allocation, runs a full mark-and-sweep pass only when necessary, then retries once.
        /// </summary>
        public bool TryAllocHeap(HeapObject obj, out uint memRef)
        {
            if (heap.TryAllocate(obj, out memRef))
                return true;

            CollectGarbage();
            return heap.TryAllocate(obj, out memRef);
        }

        public bool FreeHeap(uint memRef)
            => heap.Free(memRef);

        /// <summary>
        /// Runs a full mark-and-sweep collection over globals, all live VMs sharing this context,
        /// and recursively reachable heap objects. Cycles are naturally handled by the visited set.
        /// </summary>
        public int CollectGarbage()
        {
            GarbageCollectionCount++;

            var marked = new HashSet<uint>();
            var pending = new Stack<uint>();

            void MarkValue(DataValue value)
            {
                if (value.TryGetHeapReference(out uint memoryReference))
                    pending.Push(memoryReference);
            }

            for (int i = 0; i < globals.Length; i++)
                MarkValue(globals[i]);

            for (int i = vms.Count - 1; i >= 0; i--)
            {
                if (!vms[i].TryGetTarget(out DirectivaVM vm))
                {
                    vms.RemoveAt(i);
                    continue;
                }

                foreach (uint memoryReference in vm.EnumerateHeapReferences())
                    pending.Push(memoryReference);
            }

            while (pending.Count > 0)
            {
                uint memoryReference = pending.Pop();
                if (!marked.Add(memoryReference))
                    continue;

                if (!heap.TryGet(memoryReference, out HeapObject heapObject))
                    continue;

                foreach (uint childReference in heapObject.GetReferences())
                {
                    if (childReference != 0 && !marked.Contains(childReference))
                        pending.Push(childReference);
                }
            }

            return heap.Sweep(marked);
        }

        public DataValue GetGlobal(int globalIndex)
        {
            if ((uint)globalIndex >= (uint)globals.Length)
                throw new ArgumentOutOfRangeException(nameof(globalIndex));

            return globals[globalIndex];
        }

        public void SetGlobal(int globalIndex, DataValue value)
        {
            if ((uint)globalIndex >= (uint)globals.Length)
                throw new ArgumentOutOfRangeException(nameof(globalIndex));

            globals[globalIndex] = value;
        }

        internal bool TryGetGlobal(int globalIndex, out DataValue value)
        {
            if ((uint)globalIndex >= (uint)globals.Length)
            {
                value = default;
                return false;
            }

            value = globals[globalIndex];
            return true;
        }

        internal bool TrySetGlobal(int globalIndex, DataValue value)
        {
            if ((uint)globalIndex >= (uint)globals.Length)
                return false;

            globals[globalIndex] = value;
            return true;
        }

        /// <summary>
        /// Resets all shared global slots to Uninitialized without altering heap contents.
        /// </summary>
        public void ResetGlobals()
        {
            for (int i = 0; i < globals.Length; i++)
                globals[i] = DataValue.Uninitialized();
        }
    }
}
