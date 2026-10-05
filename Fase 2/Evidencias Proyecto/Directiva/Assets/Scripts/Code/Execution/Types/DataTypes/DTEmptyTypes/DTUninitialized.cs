using DSExecution.Errors;
using DSExecution.Operations;

namespace DSExecution.DataTypes
{
    public sealed class DTUninitialized : DataType
    {
        public static DTUninitialized Instance { get; } = new DTUninitialized();

        private DTUninitialized()
        {
            // Prevent instantiation from outside
        }

        public override OperationResult Operate(OperationId operationId, OperationCall opCall)
        {
            return OperationResult.OpError(
                RuntimeError.VariableUsedBeforeInitialization()
            );
        }
    }
}
