using System;
using System.Collections.Generic;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Object heap addressed by stable uint references. Reference 0 is intentionally never allocated.
    /// Reclamation policy is deliberately kept separate from allocation so reference types/GC can be
    /// added without changing DataValue or VM instructions.
    /// </summary>
    public sealed class HeapMemory
    {
        private readonly Dictionary<uint, HeapObject> objects = new();
        private uint nextReference = 1;

        public int ObjectCount => objects.Count;
        public ulong AllocatedSize { get; private set; }

        public uint Allocate(HeapObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            if (nextReference == 0)
                throw new InvalidOperationException("Heap reference space is exhausted.");

            uint memoryReference = nextReference++;
            objects.Add(memoryReference, obj);
            AllocatedSize += obj.Size;
            return memoryReference;
        }

        public HeapObject Get(uint memoryReference)
        {
            if (!objects.TryGetValue(memoryReference, out var obj))
                throw new KeyNotFoundException($"Heap reference {memoryReference} does not exist.");

            return obj;
        }

        public bool TryGet(uint memoryReference, out HeapObject heapObject)
            => objects.TryGetValue(memoryReference, out heapObject);

        public bool Free(uint memoryReference)
        {
            if (!objects.TryGetValue(memoryReference, out var obj))
                return false;

            objects.Remove(memoryReference);
            AllocatedSize -= obj.Size;
            return true;
        }

        public void Clear()
        {
            objects.Clear();
            AllocatedSize = 0;
            nextReference = 1;
        }
    }
}
