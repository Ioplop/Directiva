namespace DSExecution.VirtualMachine
{
    public interface IMemContext
    {
        HeapObject GetHeap(uint memRef);

        uint AllocHeap(HeapObject obj);
    }
}