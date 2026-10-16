using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    public sealed class DTNone : DataType, IOpHash
    {
        public static DTNone Instance { get; } = new DTNone();

        private DTNone()
        {
            // Prevent instantiation from outside
        }

        public override CastResult CastTo(
            DataTypeId targetType,
            DataValue value,
            VMMemoryContext memory)
        {
            if (targetType == DataTypeId.String)
                return DTString.CreateValue("None", memory);

            return base.CastTo(targetType, value, memory);
        }

        /// <summary>
        /// None is hashable, with a stable constant independent of process identity.
        /// </summary>
        public OperationResult Hash(OperationCall opCall)
        {
            if (opCall.Arguments.Length != 1 ||
                opCall.Arguments[0].dataType != DataTypeId.None)
                return OperationResult.NotImplemented();

            return OperationResult.Success(
                DataValue.FromInt(unchecked((long)0x9E3779B97F4A7C15UL)));
        }

        public override OperationResult IsTruthy(OperationCall opCall)
        {
            return OperationResult.Success(DataValue.FromBool(false));
        }
    }
}
