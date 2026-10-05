namespace DSExecution.Operations
{
    public interface IOpReferenceEquality
    {
        [OperationBinding(OperationId.ReferenceEquals)]
        public OperationResult ReferenceEquals(OperationCall opCall);
    }
}
