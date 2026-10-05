namespace DSExecution.Operations
{
    public interface IOpContainer
    {
        [OperationBinding(OperationId.Copy)]
        public OperationResult Copy(OperationCall opCall);

        [OperationBinding(OperationId.Append)]
        public OperationResult Append(OperationCall opCall);

        [OperationBinding(OperationId.Insert)]
        public OperationResult Insert(OperationCall opCall);

        [OperationBinding(OperationId.Delete)]
        public OperationResult Delete(OperationCall opCall);

        [OperationBinding(OperationId.Pop)]
        public OperationResult Pop(OperationCall opCall);

        [OperationBinding(OperationId.Get)]
        public OperationResult Get(OperationCall opCall);

        [OperationBinding(OperationId.Set)]
        public OperationResult Set(OperationCall opCall);
    }
}
