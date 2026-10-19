using System;
using System.Collections.Generic;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Directiva object heap with stable uint references. Reference zero is reserved.
    /// ChargedSize records authorized logical units independently of mutable object state.
    /// </summary>
    public sealed class HeapMemory
    {
        public const ulong DefaultMaxSize = 65_536;

        private sealed class Entry
        {
            internal readonly HeapObject Object;
            internal uint ChargedSize;
            internal Entry(HeapObject obj)
            {
                Object = obj;
                ChargedSize = obj.Size;
            }
        }

        private readonly Dictionary<uint, Entry> objects = new();
        private uint nextReference = 1;

        public int ObjectCount => objects.Count;
        public ulong AllocatedSize { get; private set; }
        public ulong MaxSize { get; }

        public HeapMemory(ulong maxSize = DefaultMaxSize)
        {
            MaxSize = maxSize;
        }

        public bool TryAllocate(HeapObject obj, out uint memoryReference)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            ulong size = obj.Size;
            if (nextReference == 0 || size > MaxSize || AllocatedSize > MaxSize - size)
            {
                memoryReference = 0;
                return false;
            }

            memoryReference = nextReference++;
            objects.Add(memoryReference, new Entry(obj));
            AllocatedSize += size;
            return true;
        }

        public uint Allocate(HeapObject obj)
        {
            if (TryAllocate(obj, out uint memoryReference))
                return memoryReference;
            throw new InvalidOperationException(
                $"Heap allocation of {obj?.Size ?? 0} unit(s) exceeds the {MaxSize}-unit heap limit.");
        }

        public HeapObject Get(uint memoryReference)
        {
            if (!objects.TryGetValue(memoryReference, out Entry entry))
                throw new KeyNotFoundException($"Heap reference {memoryReference} does not exist.");
            return entry.Object;
        }

        public bool TryGet(uint memoryReference, out HeapObject heapObject)
        {
            if (objects.TryGetValue(memoryReference, out Entry entry))
            {
                heapObject = entry.Object;
                return true;
            }
            heapObject = null;
            return false;
        }

        /// <summary>
        /// Updates charged size before the caller commits its mutation. A rejected change
        /// does not alter accounting. The caller must not yield between authorization and
        /// committing the new HeapObject.Size; a mismatch is an implementation bug.
        /// </summary>
        public bool TryResize(uint memoryReference, uint newSize)
        {
            if (!objects.TryGetValue(memoryReference, out Entry entry))
                return false;

            if (newSize == 0)
                throw new ArgumentOutOfRangeException(nameof(newSize));

            if (entry.Object.Size != entry.ChargedSize)
                throw new InvalidOperationException(
                    $"Heap object {memoryReference} was modified without a committed resize.");

            ulong oldSize = entry.ChargedSize;
            ulong requested = newSize;
            if (requested > oldSize)
            {
                ulong growth = requested - oldSize;
                if (growth > MaxSize || AllocatedSize > MaxSize - growth)
                    return false;
                AllocatedSize += growth;
            }
            else
            {
                AllocatedSize -= oldSize - requested;
            }

            entry.ChargedSize = newSize;
            return true;
        }

        /// <summary>
        /// Swaps preallocated contents, not heap identities. Object types guarantee that
        /// this is a nonthrowing O(1) swap. Charges are exchanged with the payloads, so
        /// total heap usage is unchanged; no second large allocation is required.
        /// </summary>
        public bool TrySwapContents(uint firstRef, uint secondRef)
        {
            if (firstRef == secondRef || !objects.TryGetValue(firstRef, out Entry first) ||
                !objects.TryGetValue(secondRef, out Entry second))
                return false;

            if (first.Object.Size != first.ChargedSize || second.Object.Size != second.ChargedSize)
                throw new InvalidOperationException("Cannot swap heap objects with uncommitted resizes.");

            if (!first.Object.TrySwapContents(second.Object))
                return false;

            uint temp = first.ChargedSize;
            first.ChargedSize = second.ChargedSize;
            second.ChargedSize = temp;
            return true;
        }

        public bool Free(uint memoryReference)
        {
            if (!objects.TryGetValue(memoryReference, out Entry entry))
                return false;
            objects.Remove(memoryReference);
            AllocatedSize -= entry.ChargedSize;
            return true;
        }

        internal int Sweep(ISet<uint> markedReferences)
        {
            if (markedReferences == null)
                throw new ArgumentNullException(nameof(markedReferences));
            var dead = new List<uint>();
            foreach (uint reference in objects.Keys)
                if (!markedReferences.Contains(reference)) dead.Add(reference);
            foreach (uint reference in dead) Free(reference);
            return dead.Count;
        }

        public void Clear()
        {
            objects.Clear();
            AllocatedSize = 0;
            nextReference = 1;
        }
    }
}
