namespace DSExecution.Operations
{
    public interface IOpTruthiness
    {
        [OperationBinding(OperationId.IsTruthy)]
        public OperationResult IsTruthy(OperationCall opCall);
    }
}
