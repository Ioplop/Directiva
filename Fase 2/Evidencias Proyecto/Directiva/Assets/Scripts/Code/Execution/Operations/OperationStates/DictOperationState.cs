namespace DSExecution.Operations
{
    /// <summary>
    /// Scalar continuation state; operand roots remain in the pending VM call and the
    /// partial Dict/List lives exclusively under the VM-managed auxiliary heap pointer.
    /// </summary>
    internal sealed class DictOperationState : OperationState
    {
        internal readonly OperationId Kind;
        internal readonly ulong LeftVersion;
        internal readonly ulong RightVersion;
        internal readonly int LeftCount;
        internal readonly int RightCount;
        internal int NextIndex;

        internal DictOperationState(OperationId kind, ulong leftVersion, ulong rightVersion,
            int leftCount, int rightCount)
        {
            Kind = kind;
            LeftVersion = leftVersion;
            RightVersion = rightVersion;
            LeftCount = leftCount;
            RightCount = rightCount;
        }
    }
}
