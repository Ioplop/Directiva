namespace DSExecution.Operations
{
    /// <summary>List search cursor without heap references (all operands stay VM-rooted).</summary>
    internal sealed class ListSearchState : OperationState
    {
        internal readonly OperationId Kind;
        internal readonly ulong Version;
        internal int NextIndex;

        internal ListSearchState(OperationId kind, ulong version)
        {
            Kind = kind;
            Version = version;
        }
    }
}
