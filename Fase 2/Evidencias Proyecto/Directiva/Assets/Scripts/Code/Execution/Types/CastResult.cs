#nullable enable
using DSExecution.Errors;
using DSExecution.Values;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Result of converting one Directiva DataValue to another DataType.
    /// Cast failures are runtime errors rather than missing operation implementations.
    /// </summary>
    public readonly struct CastResult
    {
        public bool IsSuccess { get; }
        public DataValue Value { get; }
        public RuntimeError? Error { get; }

        private CastResult(bool isSuccess, DataValue value, RuntimeError? error)
        {
            IsSuccess = isSuccess;
            Value = value;
            Error = error;
        }

        public static CastResult Success(DataValue value)
            => new(true, value, null);

        public static CastResult Failure(RuntimeError error)
            => new(false, DataValue.None(), error);
    }
}
