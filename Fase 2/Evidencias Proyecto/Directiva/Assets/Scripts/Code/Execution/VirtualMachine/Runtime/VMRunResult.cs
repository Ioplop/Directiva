#nullable enable
using DSExecution.Errors;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Result of running up to a fixed deterministic instruction budget.
    /// Reaching the budget is not an error; the VM simply remains Running.
    /// </summary>
    public readonly struct VMRunResult
    {
        public VMExecutionStatus Status { get; }
        public int StepsExecuted { get; }
        public bool BudgetExhausted { get; }
        public RuntimeError? Error { get; }
        public DataValue ReturnValue { get; }
        public bool HasReturnValue => Status == VMExecutionStatus.Completed;

        internal VMRunResult(
            VMExecutionStatus status,
            int stepsExecuted,
            bool budgetExhausted,
            RuntimeError? error,
            DataValue returnValue)
        {
            Status = status;
            StepsExecuted = stepsExecuted;
            BudgetExhausted = budgetExhausted;
            Error = error;
            ReturnValue = returnValue;
        }
    }
}
