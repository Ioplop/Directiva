namespace DSExecution.Operations
{
    public interface IOpBoolean
    {
        [OperationBinding(OperationId.Not)]
        public OperationResult Not(OperationCall opCall);
    }
}
