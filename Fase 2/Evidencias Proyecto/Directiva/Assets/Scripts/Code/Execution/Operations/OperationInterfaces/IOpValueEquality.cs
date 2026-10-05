namespace DSExecution.Operations
{
    public interface IOpValueEquality
    {
        [OperationBinding(OperationId.ValueEquals)]
        public OperationResult ValueEquals(OperationCall opCall);
    }
}
