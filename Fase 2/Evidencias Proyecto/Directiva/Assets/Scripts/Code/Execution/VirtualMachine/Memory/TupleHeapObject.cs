using System;
using System.Collections.Generic;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Immutable ordered collection of DataValues stored in the Directiva heap.
    /// Its logical size is one object unit plus one unit per element; referenced
    /// heap objects are accounted for separately.
    /// </summary>
    public sealed class TupleHeapObject : HeapObject
    {
        private readonly DataValue[] items;

        // Tuples cannot change their elements. A successful structural hash stays valid;
        // tuples containing unhashable mutable objects never receive a cached value.
        private long? cachedHash;

        internal bool TryGetCachedHash(out long hash)
        {
            hash = cachedHash.GetValueOrDefault();
            return cachedHash.HasValue;
        }

        internal void CacheHash(long hash) => cachedHash = hash;

        public int Count => items.Length;
        public ReadOnlySpan<DataValue> Items => items;
        public override uint Size => 1u + (uint)items.Length;

        public TupleHeapObject(ReadOnlySpan<DataValue> values)
        {
            // Keep a private copy so callers cannot mutate a tuple after construction.
            items = values.ToArray();
        }

        public DataValue GetElement(int index) => items[index];

        /// <summary>
        /// Returns direct child heap references only. The GC handles recursion and cycles.
        /// </summary>
        public override IEnumerable<uint> GetReferences()
        {
            foreach (DataValue item in items)
            {
                if (item.TryGetHeapReference(out uint reference))
                    yield return reference;
            }
        }
    }
}
