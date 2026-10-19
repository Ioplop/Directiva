namespace DSExecution.Operations
{
    /// <summary>Value-based linear searches on ordered collections.</summary>
    public interface IOpSearch
    {
        [OperationBinding(OperationId.Contains)]
        OperationResult Contains(OperationCall opCall);

        [OperationBinding(OperationId.IndexOf)]
        OperationResult IndexOf(OperationCall opCall);
    }
}
