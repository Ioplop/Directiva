using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    public sealed class DTNone : DataType
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

        public override OperationResult IsTruthy(OperationCall opCall)
        {
            return OperationResult.Success(DataValue.FromBool(false));
        }
    }
}
