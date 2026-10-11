namespace DSExecution.Operations
{
    /// <summary>
    /// Computational continuation state for a multi-step operation.
    /// IMPORTANT: OperationState implementations must never own or retain Directiva heap references,
    /// directly or indirectly. Heap values needed across continuation steps must remain reachable
    /// through the pending operation arguments so the garbage collector can see them.
    /// </summary>
    public abstract class OperationState { }
}
