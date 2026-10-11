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

        public override uint Size
            => 1u + (uint)(((long)Value.Length + 3L) / 4L);

        public StringHeapObject(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
