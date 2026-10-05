using System.Numerics;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.DataTypes
{
    public sealed class DTDecimal : DTValue, IOpArithmetic, IOpOrdering, IOpBoolean
    {
        public const long Scale = 10000;

        public static DTDecimal Instance { get; } = new DTDecimal();

        private sealed class DecimalPowerState : OperationState
        {
            public BigInteger Result { get; set; }
            public BigInteger Factor { get; set; }
            public ulong RemainingExponent { get; set; }

            public DecimalPowerState(BigInteger factor, ulong exponent)
            {
                Result = Scale;
                Factor = factor;
                RemainingExponent = exponent;
            }
        }

        private DTDecimal()
        {
            // Prevent instantiation from outside
        }

        private static bool TryResolveScaled(DataValue value, out BigInteger result)
        {
            switch (value.dataType)
            {
                case DataTypeId.Decimal:
                    result = unchecked((long)value.value);
                    return true;

                case DataTypeId.Int:
                case DataTypeId.Bool:
                    result = (BigInteger)unchecked((long)value.value) * Scale;
                    return true;

                default:
                    result = BigInteger.Zero;
                    return false;
            }
        }

        private static bool TryGetTwoNumericValues(
            OperationCall opCall,
            out BigInteger left,
            out BigInteger right)
        {
            return TryResolveScaled(opCall.Arguments[0], out left) &&
                   TryResolveScaled(opCall.Arguments[1], out right);
        }

        private static OperationResult CreateDecimalResult(BigInteger rawValue)
        {
            if (rawValue < long.MinValue || rawValue > long.MaxValue)
                return OperationResult.OpError(RuntimeError.Overflow());

            return OperationResult.Success(
                DataValue.FromDecimalRaw((long)rawValue)
            );
        }

        private static OperationResult CreateBoolResult(bool value)
        {
            return OperationResult.Success(DataValue.FromBool(value));
        }

        private static ulong GetExponentMagnitude(long exponent)
        {
            if (exponent >= 0)
                return (ulong)exponent;

            return unchecked((ulong)(-(exponent + 1))) + 1UL;
        }

        private static bool TryResolveIntegerExponent(
            DataValue value,
            out long exponent)
        {
            switch (value.dataType)
            {
                case DataTypeId.Bool:
                case DataTypeId.Int:
                    exponent = unchecked((long)value.value);
                    return true;

                case DataTypeId.Decimal:
                    var rawValue = unchecked((long)value.value);

                    if (rawValue % Scale != 0)
                    {
                        exponent = 0;
                        return false;
                    }

                    exponent = rawValue / Scale;
                    return true;

                default:
                    exponent = 0;
                    return false;
            }
        }

        public OperationResult Add(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateDecimalResult(left + right);
        }

        public OperationResult Subtract(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateDecimalResult(left - right);
        }

        public OperationResult Multiply(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateDecimalResult((left * right) / Scale);
        }

        public OperationResult Divide(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            if (right.IsZero)
                return OperationResult.OpError(RuntimeError.DivisionByZero());

            return CreateDecimalResult((left * Scale) / right);
        }

        public OperationResult FloorDivide(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            if (right.IsZero)
                return OperationResult.OpError(RuntimeError.DivisionByZero());

            var quotient = BigInteger.DivRem(left, right, out var remainder);

            if (!remainder.IsZero && (remainder.Sign < 0) != (right.Sign < 0))
                quotient--;

            return CreateDecimalResult(quotient * Scale);
        }

        public OperationResult Modulo(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            if (right.IsZero)
                return OperationResult.OpError(RuntimeError.DivisionByZero());

            BigInteger.DivRem(left, right, out var remainder);

            if (!remainder.IsZero && (remainder.Sign < 0) != (right.Sign < 0))
                remainder += right;

            return CreateDecimalResult(remainder);
        }

        private static OperationResult AdvancePower(DecimalPowerState state)
        {
            if (state.RemainingExponent == 0)
                return CreateDecimalResult(state.Result);

            if ((state.RemainingExponent & 1UL) != 0)
            {
                state.Result = (state.Result * state.Factor) / Scale;

                if (state.Result < long.MinValue || state.Result > long.MaxValue)
                    return OperationResult.OpError(RuntimeError.Overflow());
            }

            state.RemainingExponent >>= 1;

            if (state.RemainingExponent == 0)
                return CreateDecimalResult(state.Result);

            state.Factor = (state.Factor * state.Factor) / Scale;

            if (state.Factor < long.MinValue || state.Factor > long.MaxValue)
                return OperationResult.OpError(RuntimeError.Overflow());

            return OperationResult.Continued(state);
        }

        public OperationResult Power(OperationCall opCall)
        {
            DecimalPowerState state;

            if (opCall.State == null)
            {
                if (!TryResolveScaled(opCall.Arguments[0], out var baseValue) ||
                    !TryResolveIntegerExponent(opCall.Arguments[1], out var exponent))
                {
                    return OperationResult.NotImplemented();
                }

                if (exponent < 0)
                {
                    if (baseValue.IsZero)
                        return OperationResult.OpError(RuntimeError.DivisionByZero());

                    baseValue = ((BigInteger)Scale * Scale) / baseValue;
                }

                state = new DecimalPowerState(
                    baseValue,
                    GetExponentMagnitude(exponent)
                );
            }
            else
            {
                state = opCall.State as DecimalPowerState
                    ?? throw new System.InvalidOperationException(
                        "Power received an invalid OperationState."
                    );
            }

            return AdvancePower(state);
        }

        public override OperationResult ValueEquals(OperationCall opCall)
        {
            var baseResult = base.ValueEquals(opCall);

            if (baseResult.Status != OperationStatus.NotImplemented)
                return baseResult;

            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return baseResult;

            return CreateBoolResult(left == right);
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

        public OperationResult Less(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateBoolResult(left < right);
        }

        public OperationResult LessOrEqual(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateBoolResult(left <= right);
        }

        public OperationResult Greater(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateBoolResult(left > right);
        }

        public OperationResult GreaterOrEqual(OperationCall opCall)
        {
            if (!TryGetTwoNumericValues(opCall, out var left, out var right))
                return OperationResult.NotImplemented();

            return CreateBoolResult(left >= right);
        }

        public OperationResult Not(OperationCall opCall)
        {
            return CreateBoolResult(opCall.Arguments[0].value == 0);
        }
    }
}
