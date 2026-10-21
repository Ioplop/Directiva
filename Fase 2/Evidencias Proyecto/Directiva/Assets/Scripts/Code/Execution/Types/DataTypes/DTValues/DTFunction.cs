using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>
    /// Immutable non-heap reference to a function in the current compiled CodeContext.
    /// Functions are compiled once; only their ids travel through DScript values.
    /// </summary>
    public sealed class DTFunction : DTValue, IOpReferenceEquality
    {
        public static DTFunction Instance { get; } = new DTFunction();

        private DTFunction() { }

        public override OperationResult Hash(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                !opCall.Arguments[0].TryGetFunctionId(out int functionId))
                return OperationResult.NotImplemented();

            // Deterministic, consistent with function identity (no .NET hash randomization).
            return OperationResult.Success(DataValue.FromInt(functionId));
        }

        /// <summary>Function identity is its compiled function id within one CodeContext.</summary>
        public OperationResult ReferenceEquals(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 2 ||
                !opCall.Arguments[0].TryGetFunctionId(out int left) ||
                !opCall.Arguments[1].TryGetFunctionId(out int right))
                return OperationResult.NotImplemented();

            return OperationResult.Success(DataValue.FromBool(left == right));
        }

        public override OperationResult IsTruthy(OperationCall opCall)
        {
            // Function id 0 is valid and must not be falsy.
            return OperationResult.Success(DataValue.FromBool(true));
        }

        public override CastResult CastTo(
            DataTypeId targetType, DataValue value, VMMemoryContext memory)
        {
            if (targetType == DataTypeId.String)
            {
                if (!value.TryGetFunctionId(out int functionId))
                    return CastResult.Failure(
                        DSExecution.Errors.RuntimeError.InvalidOperation(
                            "Function contains an invalid compiled function id."));

                return DTString.CreateValue($"<function #{functionId}>", memory);
            }

            return base.CastTo(targetType, value, memory);
        }
    }
}
