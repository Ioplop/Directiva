using System;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.DataTypes
{
    public sealed class DTBool : DTValue, IOpArithmetic, IOpOrdering, IOpBoolean
    {
        public static DTBool Instance { get; } = new DTBool();

        private DTBool()
        {
            // Prevent instantiation from outside
        }

        private static bool IsIntCompatible(DataValue value)
        {
            return value.dataType == DataTypeId.Bool ||
                   value.dataType == DataTypeId.Int;
        }

        private static DataValue AsInt(DataValue value)
        {
            if (value.dataType == DataTypeId.Bool)
                return DataValue.FromInt(unchecked((long)value.value));

            return value;
        }

        private static OperationResult OperateAsInt(
            OperationId operationId,
            OperationCall opCall)
        {
            var left = opCall.Arguments[0];
            var right = opCall.Arguments[1];

            if (!IsIntCompatible(left) || !IsIntCompatible(right))
                return OperationResult.NotImplemented();

            Span<DataValue> arguments = stackalloc DataValue[2];
            arguments[0] = AsInt(left);
            arguments[1] = AsInt(right);

            var intCall = new OperationCall(
                arguments,
                opCall.ReceiverIndex,
                opCall.Memory,
                opCall.State
            );

            return DTInt.Instance.Operate(operationId, intCall);
        }

        public OperationResult Add(OperationCall opCall)
            => OperateAsInt(OperationId.Add, opCall);

        public OperationResult Subtract(OperationCall opCall)
            => OperateAsInt(OperationId.Subtract, opCall);

        public OperationResult Multiply(OperationCall opCall)
            => OperateAsInt(OperationId.Multiply, opCall);

        public OperationResult Divide(OperationCall opCall)
            => OperateAsInt(OperationId.Divide, opCall);

        public OperationResult FloorDivide(OperationCall opCall)
            => OperateAsInt(OperationId.FloorDivide, opCall);

        public OperationResult Modulo(OperationCall opCall)
            => OperateAsInt(OperationId.Modulo, opCall);

        public OperationResult Power(OperationCall opCall)
            => OperateAsInt(OperationId.Power, opCall);

        public OperationResult Less(OperationCall opCall)
            => OperateAsInt(OperationId.LessThan, opCall);

        public OperationResult LessOrEqual(OperationCall opCall)
            => OperateAsInt(OperationId.LessOrEqual, opCall);

        public OperationResult Greater(OperationCall opCall)
            => OperateAsInt(OperationId.GreaterThan, opCall);

        public OperationResult GreaterOrEqual(OperationCall opCall)
            => OperateAsInt(OperationId.GreaterOrEqual, opCall);

        public override OperationResult ValueEquals(OperationCall opCall)
        {
            var baseResult = base.ValueEquals(opCall);

            if (baseResult.Status != OperationStatus.NotImplemented)
                return baseResult;

            var left = opCall.Arguments[0];
            var right = opCall.Arguments[1];

            bool boolAndInt =
                (left.dataType == DataTypeId.Bool && right.dataType == DataTypeId.Int) ||
                (left.dataType == DataTypeId.Int && right.dataType == DataTypeId.Bool);

            if (!boolAndInt)
                return baseResult;

            return OperationResult.Success(
                DataValue.FromBool(left.value == right.value)
            );
        }

        public override OperationResult Hash(OperationCall opCall)
        {
            return OperationResult.Success(
                DataValue.FromInt(unchecked((long)opCall.Arguments[0].value))
            );
        }

        public override OperationResult IsTruthy(OperationCall opCall)
        {
            return OperationResult.Success(opCall.Arguments[0]);
        }

        public OperationResult Not(OperationCall opCall)
        {
            return OperationResult.Success(
                DataValue.FromBool(opCall.Arguments[0].value == 0)
            );
        }
    }
}
