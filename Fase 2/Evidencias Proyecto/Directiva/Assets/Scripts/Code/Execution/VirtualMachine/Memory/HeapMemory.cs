using System;
using System.Collections.Generic;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Object heap addressed by stable uint references. Reference 0 is intentionally never allocated.
    /// Size is measured in deterministic Directiva memory units rather than host-runtime bytes.
    /// </summary>
    public sealed class HeapMemory
    {
        public const ulong DefaultMaxSize = 65_536;

        private readonly Dictionary<uint, HeapObject> objects = new();
        private uint nextReference = 1;

        public int ObjectCount => objects.Count;
        public ulong AllocatedSize { get; private set; }
        public ulong MaxSize { get; }

        public HeapMemory(ulong maxSize = DefaultMaxSize)
        {
            MaxSize = maxSize;
        }

        /// <summary>
        /// Attempts to store an object without performing garbage collection.
        /// </summary>
        public bool TryAllocate(HeapObject obj, out uint memoryReference)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            if (nextReference == 0)
            {
                memoryReference = 0;
                return false;
            }

            ulong objectSize = obj.Size;
            if (objectSize > MaxSize || AllocatedSize > MaxSize - objectSize)
            {
                memoryReference = 0;
                return false;
            }

            memoryReference = nextReference++;
            objects.Add(memoryReference, obj);
            AllocatedSize += objectSize;
            return true;
        }

        /// <summary>
        /// Stores an object or throws when the configured logical heap limit is exhausted.
        /// Prefer VMMemoryContext.TryAllocHeap so lazy GC can run before failure.
        /// </summary>
        public uint Allocate(HeapObject obj)
        {
            if (TryAllocate(obj, out uint memoryReference))
                return memoryReference;

            throw new InvalidOperationException(
                $"Heap allocation of {obj?.Size ?? 0} unit(s) exceeds the {MaxSize}-unit heap limit."
            );
        }

        public HeapObject Get(uint memoryReference)
        {
            if (!objects.TryGetValue(memoryReference, out var obj))
                throw new KeyNotFoundException($"Heap reference {memoryReference} does not exist.");

            return obj;
        }

        public bool TryGet(uint memoryReference, out HeapObject heapObject)
            => objects.TryGetValue(memoryReference, out heapObject);

        /// <summary>
        /// Removes an allocated object. Existing references to it become invalid.
        /// </summary>
        public bool Free(uint memoryReference)
        {
            if (!objects.TryGetValue(memoryReference, out var obj))
                return false;

            objects.Remove(memoryReference);
            AllocatedSize -= obj.Size;
            return true;
        }

        /// <summary>
        /// Removes every object whose reference was not marked as reachable.
        /// </summary>
        internal int Sweep(ISet<uint> markedReferences)
        {
            if (markedReferences == null)
                throw new ArgumentNullException(nameof(markedReferences));

            if (objects.Count == 0)
                return 0;

            var deadReferences = new List<uint>();
            foreach (uint memoryReference in objects.Keys)
            {
                if (!markedReferences.Contains(memoryReference))
                    deadReferences.Add(memoryReference);
            }

            foreach (uint memoryReference in deadReferences)
                Free(memoryReference);

            return deadReferences.Count;
        }

        /// <summary>
        /// Clears the entire heap and restarts reference allocation from 1.
        /// </summary>
        public void Clear()
        {
            objects.Clear();
            AllocatedSize = 0;
            nextReference = 1;
        }
    }
}
