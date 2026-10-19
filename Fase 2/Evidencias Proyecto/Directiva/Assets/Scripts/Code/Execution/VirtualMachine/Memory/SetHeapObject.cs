using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Mutable deterministically stored set of hashable DataValues. Hash buckets are keyed by
    /// deterministic Directiva hashes; collisions are resolved by ValueEquals.
    /// Logical heap charge is 1 + number of unique members (bucket metadata is host bookkeeping).
    /// </summary>
    public sealed class SetHeapObject : HeapObject
    {
        private List<DataValue> elements = new();
        private List<long> hashes = new();
        private Dictionary<long, List<int>> buckets = new();

        public int Count => elements.Count;
        public ulong Version { get; private set; }
        public override uint Size => 1u + (uint)elements.Count;

        public DataValue GetElement(int index) => elements[index];
        internal long GetElementHash(int index) => hashes[index];

        /// <summary>
        /// Find one matching value inside a hash bucket; equal numeric types share hashes.
        /// Hash collisions are never treated as equality without calling ValueEquals.
        /// </summary>
        internal bool TryFind(DataValue value, long hash, IMemContext memory,
            out int index, out RuntimeError error)
        {
            index = -1;
            error = null;
            if (!buckets.TryGetValue(hash, out List<int> indices))
                return true;
            foreach (int candidate in indices)
            {
                DataValue existing = elements[candidate];
                OperationResult result = OperationDispatcher.Start(OperationId.ValueEquals,
                    new[] { existing, value }, memory).Result;
                if (result.Status == OperationStatus.Error)
                {
                    error = result.Error ?? RuntimeError.InternalVmError("Set member equality failed without error.");
                    return false;
                }
                if (result.Status != OperationStatus.Success || result.Value.dataType != DataTypeId.Bool)
                {
                    error = RuntimeError.InternalVmError(
                        "Set member equality must finish synchronously and return Bool.");
                    return false;
                }
                if (result.Value.value != 0)
                {
                    index = candidate;
                    return true;
                }
            }
            return true;
        }

        /// <summary>
        /// Only call after authorizing +1 logical heap unit, or while constructing an
        /// unallocated SetHeapObject. No user callbacks or GC are performed here.
        /// </summary>
        internal void AddAuthorized(DataValue value, long hash)
        {
            int index = elements.Count;
            elements.Add(value);
            hashes.Add(hash);
            if (!buckets.TryGetValue(hash, out List<int> indices))
            {
                indices = new List<int>();
                buckets.Add(hash, indices);
            }
            indices.Add(index);
            Version = unchecked(Version + 1UL);
        }

        /// <summary>
        /// Only call after authorizing -1 logical heap unit. Swap-delete avoids a linear
        /// shift; set iteration order is deterministic but deliberately not guaranteed.
        /// </summary>
        internal DataValue RemoveAuthorized(int index)
        {
            int last = elements.Count - 1;
            DataValue removed = elements[index];
            long removedHash = hashes[index];
            RemoveBucketIndex(removedHash, index);

            if (index != last)
            {
                DataValue moved = elements[last];
                long movedHash = hashes[last];
                elements[index] = moved;
                hashes[index] = movedHash;
                List<int> movedPositions = buckets[movedHash];
                for (int i = 0; i < movedPositions.Count; i++)
                {
                    if (movedPositions[i] == last)
                    {
                        movedPositions[i] = index;
                        break;
                    }
                }
            }
            elements.RemoveAt(last);
            hashes.RemoveAt(last);
            Version = unchecked(Version + 1UL);
            return removed;
        }

        private void RemoveBucketIndex(long hash, int index)
        {
            List<int> indices = buckets[hash];
            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] != index) continue;
                indices.RemoveAt(i);
                break;
            }
            if (indices.Count == 0)
                buckets.Remove(hash);
        }

        internal override bool TrySwapContents(HeapObject other)
        {
            if (!(other is SetHeapObject set) || ReferenceEquals(this, set))
                return false;
            (elements, set.elements) = (set.elements, elements);
            (hashes, set.hashes) = (set.hashes, hashes);
            (buckets, set.buckets) = (set.buckets, buckets);
            Version = unchecked(Version + 1UL);
            set.Version = unchecked(set.Version + 1UL);
            return true;
        }

        public override IEnumerable<uint> GetReferences()
        {
            foreach (DataValue value in elements)
                if (value.TryGetHeapReference(out uint reference))
                    yield return reference;
        }
    }
}
