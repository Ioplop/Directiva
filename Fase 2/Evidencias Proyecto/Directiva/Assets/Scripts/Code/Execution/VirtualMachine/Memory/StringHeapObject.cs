using System;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Immutable UTF-16 string stored in the Directiva heap.
    /// One memory unit represents four UTF-16 code units (approximately 64 bits), plus one
    /// base unit for the heap object itself.
    /// </summary>
    public sealed class StringHeapObject : HeapObject
    {
        public string Value { get; }

        // A nullable value distinguishes an uncached hash from the legitimate hash 0.
        // This host-side optimization does not change Directiva's logical heap charge.
        private long? cachedHash;

        internal bool TryGetCachedHash(out long hash)
        {
            hash = cachedHash.GetValueOrDefault();
            return cachedHash.HasValue;
        }

        internal void CacheHash(long hash) => cachedHash = hash;

        public override uint Size
            => 1u + (uint)(((long)Value.Length + 3L) / 4L);

        public StringHeapObject(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
