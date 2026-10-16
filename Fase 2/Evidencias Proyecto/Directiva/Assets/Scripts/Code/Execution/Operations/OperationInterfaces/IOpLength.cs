namespace DSExecution.Operations
{
    /// <summary>
    /// Returns the logical length of a sequence or collection as an Int.
    /// </summary>
    public interface IOpLength
    {
        [OperationBinding(OperationId.Length)]
        OperationResult Length(OperationCall opCall);
    }
}
