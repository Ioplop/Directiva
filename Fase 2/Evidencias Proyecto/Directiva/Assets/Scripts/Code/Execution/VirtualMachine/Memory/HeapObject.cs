using System.Collections.Generic;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Base object stored in the Directiva heap. Size is expressed in logical memory units,
    /// where one unit is approximately one 64-bit value slot rather than host-runtime bytes.
    /// </summary>
    public abstract class HeapObject
    {
        public abstract uint Size { get; }

        /// <summary>
        /// Enumerates heap references owned by this object for mark-and-sweep traversal.
        /// Leaf objects such as String expose none.
        /// </summary>
        public virtual IEnumerable<uint> GetReferences()
        {
            yield break;
        }

        /// <summary>
        /// Compatibility alias for older heap object callers. New GC code uses GetReferences().
        /// </summary>
        public IEnumerable<uint> EnumerateHeapReferences() => GetReferences();

        /// <summary>
        /// Only mutable, compatible heap objects may swap their contents. Called from
        /// HeapMemory with accounting updated in the same uninterrupted VM step.
        /// Implementations must not allocate or fail after modifying either object.
        /// </summary>
        internal virtual bool TrySwapContents(HeapObject other) => false;
    }
}
