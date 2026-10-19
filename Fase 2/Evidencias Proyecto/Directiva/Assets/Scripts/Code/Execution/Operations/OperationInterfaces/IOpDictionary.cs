namespace DSExecution.Operations
{
    /// <summary>Dictionary-specific operations; generic GET/SET/DELETE/POP operate on keys.</summary>
    public interface IOpDictionary
    {
        [OperationBinding(OperationId.DictUpdate)] OperationResult DictUpdate(OperationCall call);
        [OperationBinding(OperationId.DictKeys)] OperationResult DictKeys(OperationCall call);
        [OperationBinding(OperationId.DictValues)] OperationResult DictValues(OperationCall call);
        [OperationBinding(OperationId.DictItems)] OperationResult DictItems(OperationCall call);
        [OperationBinding(OperationId.DictClear)] OperationResult DictClear(OperationCall call);
        [OperationBinding(OperationId.DictGetDefault)] OperationResult DictGetDefault(OperationCall call);
    }
}
