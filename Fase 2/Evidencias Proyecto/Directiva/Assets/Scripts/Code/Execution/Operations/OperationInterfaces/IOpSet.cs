namespace DSExecution.Operations
{
    /// <summary>Set-specific commands and algebra; legacy operation ids remain untouched.</summary>
    public interface IOpSet
    {
        [OperationBinding(OperationId.SetAdd)] OperationResult SetAdd(OperationCall call);
        [OperationBinding(OperationId.SetDiscard)] OperationResult SetDiscard(OperationCall call);
        [OperationBinding(OperationId.SetRemove)] OperationResult SetRemove(OperationCall call);
        [OperationBinding(OperationId.SetPop)] OperationResult SetPop(OperationCall call);
        [OperationBinding(OperationId.SetUnion)] OperationResult SetUnion(OperationCall call);
        [OperationBinding(OperationId.SetIntersection)] OperationResult SetIntersection(OperationCall call);
        [OperationBinding(OperationId.SetDifference)] OperationResult SetDifference(OperationCall call);
        [OperationBinding(OperationId.SetSymmetricDifference)] OperationResult SetSymmetricDifference(OperationCall call);
        [OperationBinding(OperationId.SetUpdate)] OperationResult SetUpdate(OperationCall call);
        [OperationBinding(OperationId.SetIsSubset)] OperationResult SetIsSubset(OperationCall call);
        [OperationBinding(OperationId.SetIsSuperset)] OperationResult SetIsSuperset(OperationCall call);
        [OperationBinding(OperationId.SetIsDisjoint)] OperationResult SetIsDisjoint(OperationCall call);
    }
}
