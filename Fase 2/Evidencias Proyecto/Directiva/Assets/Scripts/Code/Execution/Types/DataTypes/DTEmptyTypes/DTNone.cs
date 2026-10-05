using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.DataTypes
{
    public sealed class DTNone : DataType
    {
        public static DTNone Instance { get; } = new DTNone();

        private DTNone()
        {
            // Prevent instantiation from outside
        }

        public override OperationResult IsTruthy(OperationCall opCall)
        {
            return OperationResult.Success(DataValue.FromBool(false));
        }
    }
}
