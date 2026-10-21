using DSExecution.Errors;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// A cursor returns true when an item is available, even if that item is None.
    /// Returning false with a null error means normal end of iteration; a non-null
    /// error means iteration failed and the VM must fault.
    /// </summary>
    public interface IDirectivaIterator
    {
        bool TryGetNext(IMemContext memory, out DataValue value, out RuntimeError error);
    }
}
