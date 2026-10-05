namespace DSExecution.Operations
{
    public interface IOpOrdering
    {
        [OperationBinding(OperationId.LessThan)]
        public OperationResult Less(OperationCall opCall);

        [OperationBinding(OperationId.LessOrEqual)]
        public OperationResult LessOrEqual(OperationCall opCall);

        [OperationBinding(OperationId.GreaterThan)]
        public OperationResult Greater(OperationCall opCall);

        [OperationBinding(OperationId.GreaterOrEqual)]
        public OperationResult GreaterOrEqual(OperationCall opCall);
    }
}
