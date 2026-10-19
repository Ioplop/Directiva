using System;
using System.Collections.Generic;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Insertion-ordered mutable map. Hash buckets select candidate keys; equality
    /// resolves collisions. Each entry charges two logical heap slots (key + value).
    /// All mutators require prior heap resize authorization if the entry count changes.
    /// </summary>
    public sealed class DictHeapObject : HeapObject
    {
        private List<DataValue> keys = new();
        private List<DataValue> values = new();
        private List<long> hashes = new();
        private Dictionary<long, List<int>> buckets = new();

        public int Count => keys.Count;
        public ulong Version { get; private set; }
        public override uint Size => 1u + 2u * (uint)keys.Count;

        public DataValue GetKey(int index) => keys[index];
        public DataValue GetValue(int index) => values[index];
        internal long GetKeyHash(int index) => hashes[index];

        internal bool TryFind(DataValue key, long hash, IMemContext memory,
            out int index, out RuntimeError error)
        {
            index = -1;
            error = null;
            if (!buckets.TryGetValue(hash, out List<int> indices))
                return true;
            foreach (int candidate in indices)
            {
                OperationResult comparison = OperationDispatcher.Start(OperationId.ValueEquals,
                    new[] { keys[candidate], key }, memory).Result;
                if (comparison.Status == OperationStatus.Error)
                {
                    error = comparison.Error ?? RuntimeError.InternalVmError("Dictionary key comparison failed.");
                    return false;
                }
                if (comparison.Status != OperationStatus.Success || comparison.Value.dataType != DataTypeId.Bool)
                {
                    error = RuntimeError.InternalVmError(
                        "Dictionary key comparison must finish synchronously with Bool.");
                    return false;
                }
                if (comparison.Value.value != 0)
                {
                    index = candidate;
                    return true;
                }
            }
            return true;
        }

        /// <summary>Add a previously validated unique key after reserving +2 slots.</summary>
        internal void AddAuthorized(DataValue key, DataValue value, long hash)
        {
            int index = keys.Count;
            keys.Add(key);
            values.Add(value);
            hashes.Add(hash);
            if (!buckets.TryGetValue(hash, out List<int> indices))
            {
                indices = new List<int>();
                buckets.Add(hash, indices);
            }
            indices.Add(index);
            Version = unchecked(Version + 1UL);
        }

        /// <summary>Replacement retains the original key and its insertion position.</summary>
        internal void SetValueAuthorized(int index, DataValue value)
        {
            values[index] = value;
            Version = unchecked(Version + 1UL);
        }

        /// <summary>Remove while preserving the deterministic insertion order.</summary>
        internal DataValue RemoveAuthorized(int index)
        {
            DataValue removed = values[index];
            keys.RemoveAt(index);
            values.RemoveAt(index);
            hashes.RemoveAt(index);
            // Keep bucket indices aligned with the compact ordered arrays.
            var emptyBuckets = new List<long>();
            foreach (var bucket in buckets)
            {
                List<int> positions = bucket.Value;
                for (int i = positions.Count - 1; i >= 0; i--)
                {
                    if (positions[i] == index) positions.RemoveAt(i);
                    else if (positions[i] > index) positions[i]--;
                }
                if (positions.Count == 0) emptyBuckets.Add(bucket.Key);
            }
            foreach (long hash in emptyBuckets) buckets.Remove(hash);
            Version = unchecked(Version + 1UL);
            return removed;
        }

        internal void ClearAuthorized()
        {
            if (Count == 0) return;
            keys.Clear();
            values.Clear();
            hashes.Clear();
            buckets.Clear();
            Version = unchecked(Version + 1UL);
        }

        internal override bool TrySwapContents(HeapObject other)
        {
            if (!(other is DictHeapObject dictionary) || ReferenceEquals(this, dictionary))
                return false;
            (keys, dictionary.keys) = (dictionary.keys, keys);
            (values, dictionary.values) = (dictionary.values, values);
            (hashes, dictionary.hashes) = (dictionary.hashes, hashes);
            (buckets, dictionary.buckets) = (dictionary.buckets, buckets);
            Version = unchecked(Version + 1UL);
            dictionary.Version = unchecked(dictionary.Version + 1UL);
            return true;
        }

        public override IEnumerable<uint> GetReferences()
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].TryGetHeapReference(out uint keyRef)) yield return keyRef;
                if (values[i].TryGetHeapReference(out uint valueRef)) yield return valueRef;
            }
        }
    }
}
