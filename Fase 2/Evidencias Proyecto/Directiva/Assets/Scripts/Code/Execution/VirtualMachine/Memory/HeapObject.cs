namespace DSExecution.VirtualMachine
{
    public abstract class HeapObject
    {
        private uint size;
        public abstract uint Size { get ; }
    }
}