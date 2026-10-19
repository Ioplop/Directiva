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
        /// <summary>Private heap root held by the VM while an operation is continued.</summary>
        public uint AuxiliaryReference { get; }

        private OperationResult(OperationStatus status, DataValue value, OperationState? state, RuntimeError? error, uint auxiliaryReference = 0)
        {
            Status = status;
            Value = value;
            State = state;
            Error = error;
            AuxiliaryReference = auxiliaryReference;
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

        public static OperationResult Continued(OperationState state, uint auxiliaryReference = 0)
        {
            return new OperationResult(
                OperationStatus.Continued,
                DataValue.None(),
                state,
                null,
                auxiliaryReference
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