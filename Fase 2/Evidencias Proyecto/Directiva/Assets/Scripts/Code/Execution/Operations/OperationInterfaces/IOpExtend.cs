namespace DSExecution.Operations
{
    /// <summary>Extends a mutable collection with another collection.</summary>
    public interface IOpExtend
    {
        [OperationBinding(OperationId.Extend)]
        OperationResult Extend(OperationCall opCall);
    }
}
