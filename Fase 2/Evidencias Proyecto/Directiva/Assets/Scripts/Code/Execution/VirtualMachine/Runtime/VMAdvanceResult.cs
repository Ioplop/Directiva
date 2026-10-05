#nullable enable
using DSExecution.Errors;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Result of advancing the VM by exactly one deterministic execution step.
    /// Script/runtime failures are returned here instead of being thrown to the caller.
    /// </summary>
    public readonly struct VMAdvanceResult
    {
        public VMExecutionStatus Status { get; }
        public RuntimeError? Error { get; }
        public DataValue ReturnValue { get; }
        public bool HasReturnValue => Status == VMExecutionStatus.Completed;

        private VMAdvanceResult(
            VMExecutionStatus status,
            RuntimeError? error,
            DataValue returnValue)
        {
            Status = status;
            Error = error;
            ReturnValue = returnValue;
        }

        internal static VMAdvanceResult Running()
            => new(VMExecutionStatus.Running, null, DataValue.None());

        internal static VMAdvanceResult Completed(DataValue returnValue)
            => new(VMExecutionStatus.Completed, null, returnValue);

        internal static VMAdvanceResult Faulted(RuntimeError error)
            => new(VMExecutionStatus.Faulted, error, DataValue.None());
    }
}
