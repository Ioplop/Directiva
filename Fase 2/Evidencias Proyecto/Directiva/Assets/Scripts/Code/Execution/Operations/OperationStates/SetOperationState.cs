namespace DSExecution.Operations
{
    /// <summary>
    /// Scalar-only set continuation state. Heap references stay in VM operand roots
    /// and the VM-managed auxiliary root, never in an OperationState.
    /// </summary>
    internal sealed class SetOperationState : OperationState
    {
        internal readonly OperationId Kind;
        internal readonly ulong LeftVersion;
        internal readonly ulong RightVersion;
        internal readonly int LeftCount;
        internal readonly int RightCount;
        internal int NextIndex;

        internal SetOperationState(OperationId kind, ulong leftVersion, ulong rightVersion,
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
