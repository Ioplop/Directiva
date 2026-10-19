namespace DSExecution.Operations
{
    /// <summary>
    /// Continuation progress for list-copying operations. Only scalars are stored here:
    /// heap roots reside in pending VM operands and OperationCall.AuxiliaryReference.
    /// </summary>
    internal sealed class ListBuildState : OperationState
    {
        internal readonly OperationId Kind;
        internal readonly ulong LeftVersion;
        internal readonly ulong RightVersion;
        internal readonly int LeftCount;
        internal readonly int RightCount;
        internal readonly int FinalCount;
        internal readonly int AtIndex;
        internal int NextIndex;

        internal ListBuildState(OperationId kind, ulong leftVersion, ulong rightVersion,
            int leftCount, int rightCount, int finalCount, int atIndex)
        {
            Kind = kind;
            LeftVersion = leftVersion;
            RightVersion = rightVersion;
            LeftCount = leftCount;
            RightCount = rightCount;
            FinalCount = finalCount;
            AtIndex = atIndex;
        }
    }
}
