using System;
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

        public int GlobalCount => globals.Length;
        public HeapMemory Heap => heap;

        public VMMemoryContext(int globalCount)
            : this(globalCount, new HeapMemory())
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

        public HeapObject GetHeap(uint memRef)
            => heap.Get(memRef);

        public bool TryGetHeap(uint memRef, out HeapObject heapObject)
            => heap.TryGet(memRef, out heapObject);

        public uint AllocHeap(HeapObject obj)
            => heap.Allocate(obj);

        public bool FreeHeap(uint memRef)
            => heap.Free(memRef);

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

        public void ResetGlobals()
        {
            for (int i = 0; i < globals.Length; i++)
                globals[i] = DataValue.Uninitialized();
        }
    }
}
