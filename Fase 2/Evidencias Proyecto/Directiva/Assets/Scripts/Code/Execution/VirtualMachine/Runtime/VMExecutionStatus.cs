namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// High-level lifecycle state of a Directiva VM instance.
    /// </summary>
    public enum VMExecutionStatus
    {
        Running = 0,
        Completed = 1,
        Faulted = 2
    }
}
