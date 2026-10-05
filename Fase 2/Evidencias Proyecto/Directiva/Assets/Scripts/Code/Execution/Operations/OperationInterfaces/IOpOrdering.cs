namespace DSExecution.Operations
{
    public interface IOpOrdering
    {
        [OperationBinding(OperationId.LessThan)]
        public OperationResult Less(OperationCall opCall);

        [OperationBinding(OperationId.LessOrEqualThan)]
        public OperationResult LessOrEqual(OperationCall opCall);

        [OperationBinding(OperationId.GreaterThan)]
        public OperationResult Greater(OperationCall opCall);

        [OperationBinding(OperationId.GreaterOrEqualThan)]
        public OperationResult GreaterOrEqual(OperationCall opCall);
    }
}
