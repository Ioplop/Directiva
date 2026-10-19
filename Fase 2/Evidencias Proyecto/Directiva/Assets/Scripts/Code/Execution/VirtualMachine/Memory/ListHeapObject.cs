using System;
using System.Collections.Generic;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Mutable ordered sequence of DataValues. The logical charge is 1 + Count;
    /// the heap owns resize authorization and accounts referenced children separately.
    /// </summary>
    public sealed class ListHeapObject : HeapObject
    {
        private List<DataValue> items;

        public int Count => items.Count;
        public ulong Version { get; private set; }
        public override uint Size => 1u + (uint)items.Count;

        public ListHeapObject(ReadOnlySpan<DataValue> values)
        {
            items = new List<DataValue>(values.Length);
            for (int i = 0; i < values.Length; i++)
                items.Add(values[i]);
        }

        public DataValue GetElement(int index) => items[index];

        /// <summary>
        /// These mutation methods are internal because callers MUST reserve or release
        /// logical units through IMemContext.TryResizeHeap before changing Count.
        /// </summary>
        internal void AppendAuthorized(DataValue value)
        {
            items.Add(value);
            Version = unchecked(Version + 1UL);
        }

        internal DataValue RemoveLastAuthorized()
        {
            int index = items.Count - 1;
            DataValue removed = items[index];
            items.RemoveAt(index);
            Version = unchecked(Version + 1UL);
            return removed;
        }

        internal void SetElement(int index, DataValue value)
        {
            items[index] = value;
            Version = unchecked(Version + 1UL);
        }

        internal override bool TrySwapContents(HeapObject other)
        {
            if (!(other is ListHeapObject list) || ReferenceEquals(this, list))
                return false;
            // No allocation, GC, or user-code dispatch between exchange and charge swap.
            List<DataValue> oldItems = items;
            items = list.items;
            list.items = oldItems;
            Version = unchecked(Version + 1UL);
            list.Version = unchecked(list.Version + 1UL);
            return true;
        }

        public override IEnumerable<uint> GetReferences()
        {
            foreach (DataValue item in items)
                if (item.TryGetHeapReference(out uint reference))
                    yield return reference;
        }
    }
}
