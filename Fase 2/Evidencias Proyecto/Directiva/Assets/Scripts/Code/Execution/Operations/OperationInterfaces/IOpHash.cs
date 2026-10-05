namespace DSExecution.Operations
{
    public interface IOpHash
    {
        [OperationBinding(OperationId.Hash)]
        public OperationResult Hash(OperationCall opCall);
    }
}
