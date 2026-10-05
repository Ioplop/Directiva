namespace DSExecution.VirtualMachine
{
    public interface IMemContext
    {
        object GetHeap(HeapObject memRef);

        uint AllocHeap(HeapObject obj);
    }
}