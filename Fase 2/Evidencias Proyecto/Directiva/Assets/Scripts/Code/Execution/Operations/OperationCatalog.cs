using System;
using System.Linq;
using DSExecution.Values;

namespace DSExecution.Operations
{
    public static class OperationCatalog
    {
        /// <summary>
        /// Indicates the number of parameters needed to execute these operations, and which parameter indexes are allowed to dispatch the operation.
        /// </summary>
        private static readonly OperationInfo[] operations = CreateOperationCatalog();

        public static OperationInfo GetInfo(OperationId opId)
        {
            // Assumes opId is a valid declared OperationId.
            // CreateOperationCatalog guarantees that every declared operation
            // has a corresponding OperationInfo.
            return operations[(int)opId];
        }

        private static void SetOperation(
            OperationInfo?[] operations,
            OperationId operationId,
            OperationInfo operationInfo
            )
        {
            int index = (int)operationId;

            if (operations[index] != null)
                throw new InvalidOperationException(
                    $"Operation {operationId} was registered twice.");

            operations[index] = operationInfo;
        }

        private static OperationInfo[] CreateOperationCatalog()
        {
            int maxOperationId = Enum
                .GetValues(typeof(OperationId))
                .Cast<OperationId>()
                .Max(id => (int)id);

            OperationInfo?[] temp = new OperationInfo?[maxOperationId + 1];
            SetOperation(temp, OperationId.Add, new(2, null, 0, 1));
            SetOperation(temp, OperationId.Subtract, new(2, null, 0, 1));
            SetOperation(temp, OperationId.Multiply, new(2, null, 0, 1));
            SetOperation(temp, OperationId.Divide, new(2, null, 0, 1));
            SetOperation(temp, OperationId.FloorDivide, new(2, null, 0, 1));
            SetOperation(temp, OperationId.Modulo, new(2, null, 0, 1));
            SetOperation(temp, OperationId.Power, new(2, null, 0, 1));
            SetOperation(temp, OperationId.ValueEquals, new(2, OperationResult.Success(DataValue.FromBool(false)), 0, 1));
            SetOperation(temp, OperationId.ReferenceEquals, new(2, OperationResult.Success(DataValue.FromBool(false)), 0, 1));
            SetOperation(temp, OperationId.GreaterThan, new(2, null, 0, 1));
            SetOperation(temp, OperationId.GreaterOrEqual, new(2, null, 0, 1));
            SetOperation(temp, OperationId.LessThan, new(2, null, 0, 1));
            SetOperation(temp, OperationId.LessOrEqual, new(2, null, 0, 1));
            SetOperation(temp, OperationId.Not, new(1, null, 0));
            SetOperation(temp, OperationId.Copy, new(1, null, 0));
            SetOperation(temp, OperationId.Append, new(2, null, 0));
            SetOperation(temp, OperationId.Insert, new(3, null, 0));
            SetOperation(temp, OperationId.Delete, new(2, null, 0));
            SetOperation(temp, OperationId.Pop, new(2, null, 0));
            SetOperation(temp, OperationId.Get, new(2, null, 0));
            SetOperation(temp, OperationId.Set, new(3, null, 0));
            SetOperation(temp, OperationId.Hash, new(1, null, 0));
            SetOperation(temp, OperationId.IsTruthy, new(1, null, 0));

            OperationInfo[] result = new OperationInfo[temp.Length];

            foreach (OperationId id in Enum.GetValues(typeof(OperationId)))
            {
                OperationInfo? info = temp[(int)id];

                if (info == null)
                    throw new InvalidOperationException(
                        $"Operation {id} has no OperationInfo registered.");

                result[(int)id] = info.Value;
            }

            return result;
        }
    }
}