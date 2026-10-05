using System.Text;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    internal readonly struct OperationDispatchResult
    {
        public OperationResult Result { get; }
        public int ReceiverIndex { get; }

        public OperationDispatchResult(OperationResult result, int receiverIndex)
        {
            Result = result;
            ReceiverIndex = receiverIndex;
        }
    }

    internal static class OperationDispatcher
    {
        public static OperationDispatchResult Start(
            OperationId operationId,
            DataValue[] arguments,
            IMemContext memory)
        {
            var info = OperationCatalog.GetInfo(operationId);

            foreach (int receiverIndex in info.DispatchIndices)
            {
                var receiver = arguments[receiverIndex];
                var dataType = DataType.GetDataType(receiver.dataType);

                if (dataType == null)
                    continue;

                var call = new OperationCall(
                    arguments,
                    receiverIndex,
                    memory,
                    null
                );

                var result = dataType.Operate(operationId, call);

                if (result.Status != OperationStatus.NotImplemented)
                    return new OperationDispatchResult(result, receiverIndex);
            }

            if (info.FallbackResult.HasValue)
                return new OperationDispatchResult(info.FallbackResult.Value, -1);

            return new OperationDispatchResult(
                OperationResult.OpError(
                    RuntimeError.InvalidOperation(
                        BuildInvalidOperationDetails(operationId, arguments)
                    )
                ),
                -1
            );
        }

        public static OperationResult Continue(
            OperationId operationId,
            DataValue[] arguments,
            int receiverIndex,
            OperationState state,
            IMemContext memory)
        {
            if ((uint)receiverIndex >= (uint)arguments.Length)
            {
                return OperationResult.OpError(
                    RuntimeError.InternalVmError(
                        $"Pending operation {operationId} has invalid receiver index {receiverIndex}."
                    )
                );
            }

            var receiver = arguments[receiverIndex];
            var dataType = DataType.GetDataType(receiver.dataType);

            if (dataType == null)
            {
                return OperationResult.OpError(
                    RuntimeError.InvalidOperation(
                        BuildInvalidOperationDetails(operationId, arguments)
                    )
                );
            }

            var call = new OperationCall(
                arguments,
                receiverIndex,
                memory,
                state
            );

            var result = dataType.Operate(operationId, call);

            if (result.Status == OperationStatus.NotImplemented)
            {
                return OperationResult.OpError(
                    RuntimeError.InternalVmError(
                        $"Continued operation {operationId} became NotImplemented."
                    )
                );
            }

            return result;
        }

        private static string BuildInvalidOperationDetails(
            OperationId operationId,
            DataValue[] arguments)
        {
            var builder = new StringBuilder();
            builder.Append(operationId);
            builder.Append(" does not support operand types (");

            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(arguments[i].dataType);
            }

            builder.Append(").");
            return builder.ToString();
        }
    }
}
