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
        /// <summary>Logical budget units consumed; DEBUG_* instructions count as zero.</summary>
        public int StepsExecuted { get; }
        /// <summary>Actual calls to Advance, including DEBUG_* instructions.</summary>
        public int AdvancesExecuted { get; }
        public bool BudgetExhausted { get; }
        public RuntimeError? Error { get; }
        public DataValue ReturnValue { get; }
        public bool HasReturnValue => Status == VMExecutionStatus.Completed;

        internal VMRunResult(
            VMExecutionStatus status,
            int stepsExecuted,
            int advancesExecuted,
            bool budgetExhausted,
            RuntimeError? error,
            DataValue returnValue)
        {
            Status = status;
            StepsExecuted = stepsExecuted;
            AdvancesExecuted = advancesExecuted;
            BudgetExhausted = budgetExhausted;
            Error = error;
            ReturnValue = returnValue;
        }
    }
}
