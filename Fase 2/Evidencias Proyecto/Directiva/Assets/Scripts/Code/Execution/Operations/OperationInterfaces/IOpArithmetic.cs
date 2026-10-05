namespace DSExecution.Operations
{
    public interface IOpArithmetic
    {
        [OperationBinding(OperationId.Add)]
        public OperationResult Add(OperationCall opCall);

        [OperationBinding(OperationId.Subtract)]
        public OperationResult Subtract(OperationCall opCall);

        [OperationBinding(OperationId.Multiply)]
        public OperationResult Multiply(OperationCall opCall);

        [OperationBinding(OperationId.Divide)]
        public OperationResult Divide(OperationCall opCall);

        [OperationBinding(OperationId.FloorDivide)]
        public OperationResult FloorDivide(OperationCall opCall);

        [OperationBinding(OperationId.Modulo)]
        public OperationResult Modulo(OperationCall opCall);

        [OperationBinding(OperationId.Power)]
        public OperationResult Power(OperationCall opCall);
    }
}
