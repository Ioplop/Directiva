# nullable enable
using DSExecution.Values;
using DSExecution.Errors;

namespace DSExecution.Operations
{
    /// <summary>
    /// Contiene el resultado o retorno de aplicar una operación.
    /// </summary>
    public readonly struct OperationResult
    {
        public OperationStatus Status { get; }
        public DataValue Value { get; }
        public OperationState? State { get; }
        public RuntimeError? Error { get; }

        private OperationResult(OperationStatus status, DataValue value, OperationState? state, RuntimeError? error)
        {
            Status = status;
            Value = value;
            State = state;
            Error = error;
        }

        public static OperationResult NotImplemented()
        {
            return new OperationResult(
                OperationStatus.NotImplemented,
                DataValue.None(),
                null,
                null
            );
        }

        public static OperationResult Success(DataValue value)
        {
            return new OperationResult(
                OperationStatus.Success,
                value,
                null,
                null
            );
        }

        public static OperationResult Continued(OperationState state)
        {
            return new OperationResult(
                OperationStatus.Continued,
                DataValue.None(),
                state,
                null
            );
        }

        public static OperationResult OpError(RuntimeError error)
        {
            return new OperationResult(
                OperationStatus.Error,
                DataValue.None(),
                null,
                error
            );
        }
    }
}