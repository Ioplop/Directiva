using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.DataTypes
{
    // TODO: Implement interfaces. Commented for now so we can finish Int first.
    public sealed class DTDecimal : DTValue //, IOpArithmetic, IOpOrdering, IOpBoolean
    {
        public const long Scale = 10000;

        public static DTDecimal Instance { get; } = new DTDecimal();

        private DTDecimal()
        {
            // Prevent instantiation from outside
        }

        public long BoolToDecimalValue(bool value) { 
            return (value ? 1*Scale : 0);
        }

        public (DataValue decimalSide, DataValue otherSide) GetTwoOpSidesByType(OperationCall opCall)
        {
            // Podemos asumir que uno de los dos valores es decimal porque si no la operación no sería despachada esta clase.
            var left = opCall.Arguments[0];
            var right = opCall.Arguments[1];
            return left.dataType == DataTypeId.Decimal ? (left, right) : (right, left);
        }

        public override OperationResult ValueEquals(OperationCall opCall)
        {
            var baseResult = base.ValueEquals(opCall);

            if (baseResult.Status != OperationStatus.NotImplemented)
                return baseResult;

            var (decimalSide, otherSide) = GetTwoOpSidesByType(opCall);
            long decimalValue = unchecked((long)decimalSide.value);

            // Early out if we have decimal part which is indicative of difference when comparing to bool or int.
            if (decimalValue % Scale != 0)
                return OperationResult.Success(DataValue.FromBool(false));

            long otherValue;
            if (otherSide.dataType == DataTypeId.Bool)
            {
                otherValue = otherSide.value == 1 ? Scale : 0;
            }
            else if (otherSide.dataType == DataTypeId.Int)
            {
                // To prevent overflowing, we convert decimal to int, and not the other way around!
                decimalValue /= Scale;
                otherValue = unchecked((long)otherSide.value);
            }
            else
            {
                // Not implemented!
                return baseResult;
            }
            return OperationResult.Success(DataValue.FromBool(decimalValue == otherValue));
        }

        public override OperationResult Hash(OperationCall opCall)
        {
            var value = unchecked((long)opCall.Arguments[0].value);

            var integerPart = value / Scale;
            var decimalPart = value % Scale;

            ulong bits = unchecked((ulong)decimalPart);
            ulong rotated = (bits << 32) | (bits >> 32);

            long result = unchecked(
                integerPart + (long)rotated
            );

            return OperationResult.Success(
                DataValue.FromInt(result)
            );
        }
    }
}