namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Memory services exposed to data type operations.
    /// Globals and locals are managed by the VM itself; operations only need heap access.
    /// </summary>
    public interface IMemContext
    {
        HeapObject GetHeap(uint memRef);

        bool TryGetHeap(uint memRef, out HeapObject heapObject);

        uint AllocHeap(HeapObject obj);

        /// <summary>
        /// Attempts an allocation, triggering the memory context's lazy GC before reporting failure.
        /// </summary>
        bool TryAllocHeap(HeapObject obj, out uint memRef);

        /// <summary>
        /// Authorizes a new logical size before the owning object changes its contents.
        /// Failure leaves both the object and heap accounting unchanged. The caller must
        /// immediately commit a successful resize without another allocation/GC in between.
        /// </summary>
        bool TryResizeHeap(uint memRef, uint newSize);

        /// <summary>
        /// Atomically exchanges already-reserved contents between two heap objects.
        /// Used to publish prepared mutable results without allocating again.
        /// </summary>
        bool TrySwapHeapContents(uint firstRef, uint secondRef);

        bool FreeHeap(uint memRef);
    }
}
