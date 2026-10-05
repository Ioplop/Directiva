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

        bool FreeHeap(uint memRef);
    }
}
