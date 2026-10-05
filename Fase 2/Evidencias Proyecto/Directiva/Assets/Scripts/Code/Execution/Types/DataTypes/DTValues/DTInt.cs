using System;
using System.Numerics;
using DSExecution.Values;
using DSExecution.Operations;
using DSExecution.Errors;

namespace DSExecution.DataTypes
{
    public sealed class DTInt : DTValue, IOpArithmetic, IOpOrdering, IOpBoolean
    {
        public static DTInt Instance { get; } = new DTInt();

        private DTInt()
        {
            // Prevent instantiation from outside
        }

        private static long ResolveInt(DataValue value)
        {
            return unchecked((long)Resolve(value));
        }

        private static OperationResult CreateSuccessIntResult(long value)
        {
            return OperationResult.Success(DataValue.FromInt(value));
        }

        private static OperationResult CreateSuccessBoolResult(bool value)
        {
            return OperationResult.Success(DataValue.FromBool(value));
        }

        private static ulong GetExponentMagnitude(long exponent)
        {
            if (exponent >= 0)
                return (ulong)exponent;

            return unchecked((ulong)(-(exponent + 1))) + 1UL;
        }

        private static (long leftValue, long rightValue)? GetTwoOperationValues(OperationCall opCall)
        {
            var leftData = opCall.Arguments[0];
            var rightData = opCall.Arguments[1];

            if (leftData.dataType != DataTypeId.Int || rightData.dataType != DataTypeId.Int)
                return null;
            
            var leftValue = ResolveInt(leftData);
            var rightValue = ResolveInt(rightData);

            return (leftValue, rightValue);
        }

        public OperationResult Add(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (!values.HasValue)
                return OperationResult.NotImplemented();

            try
            {
                var result = checked(values.Value.leftValue + values.Value.rightValue);
                return CreateSuccessIntResult(result);
            }
            catch (OverflowException)
            {
                return OperationResult.OpError(RuntimeError.Overflow());
            }
        }

        public OperationResult Subtract(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);
            if (!values.HasValue)
                return OperationResult.NotImplemented();

            try
            {
                var result = checked(values.Value.leftValue - values.Value.rightValue);
                return CreateSuccessIntResult(result);
            }
            catch (OverflowException)
            {
                return OperationResult.OpError(RuntimeError.Overflow());
            }
        }

        public OperationResult Multiply(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);
            if (!values.HasValue)
                return OperationResult.NotImplemented();

            try
            {
                var result = checked(values.Value.leftValue * values.Value.rightValue);
                return CreateSuccessIntResult(result);
            }
            catch (OverflowException)
            {
                return OperationResult.OpError(RuntimeError.Overflow());
            }
        }

        public OperationResult Divide(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (!values.HasValue)
                return OperationResult.NotImplemented();

            var a = values.Value.leftValue;
            var b = values.Value.rightValue;

            if (b == 0)
                return OperationResult.OpError(
                    RuntimeError.DivisionByZero()
                );

            BigInteger result =
                ((BigInteger)a * DTDecimal.Scale) / b;

            if (result < long.MinValue ||
                result > long.MaxValue)
            {
                return OperationResult.OpError(
                    RuntimeError.Overflow()
                );
            }

            return OperationResult.Success(
                DataValue.FromDecimalRaw((long)result)
            );
        }

        public OperationResult FloorDivide(OperationCall opCall)
        {
            // This feels overcomplicated but it's necessary to emulate python integer division behaviour.
            var values = GetTwoOperationValues(opCall);

            if (!values.HasValue)
                return OperationResult.NotImplemented();

            var a = values.Value.leftValue;
            var b = values.Value.rightValue;

            if (b == 0)
                return OperationResult.OpError(
                    RuntimeError.DivisionByZero()
                );

            if (a == long.MinValue && b == -1)
                return OperationResult.OpError(
                    RuntimeError.Overflow()
                );

            var quotient = a / b;
            var remainder = a % b;

            if (remainder != 0 && (remainder < 0) != (b < 0))
                quotient--;

            return CreateSuccessIntResult(quotient);
        }

        public OperationResult Modulo(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (!values.HasValue)
                return OperationResult.NotImplemented();

            var a = values.Value.leftValue;
            var b = values.Value.rightValue;

            if (b == 0)
                return OperationResult.OpError(
                    RuntimeError.DivisionByZero()
                );

            if (a == long.MinValue && b == -1)
                return CreateSuccessIntResult(0);

            var result = a % b;

            if (result != 0 && (result < 0) != (b < 0))
                result += b;

            return CreateSuccessIntResult(result);
        }

        private static OperationResult FinishPower(IntPowerState state)
        {
            if (!state.NegativeExponent)
            {
                if (state.Result < long.MinValue ||
                    state.Result > long.MaxValue)
                {
                    return OperationResult.OpError(
                        RuntimeError.Overflow()
                    );
                }

                return OperationResult.Success(
                    DataValue.FromInt((long)state.Result)
                );
            }

            BigInteger raw =
                DTDecimal.Scale / state.Result;

            return OperationResult.Success(
                DataValue.FromDecimalRaw((long)raw)
            );
        }

        private static OperationResult AdvancePower(IntPowerState state)
        {
            if (state.RemainingExponent == 0)
                return FinishPower(state);

            if ((state.RemainingExponent & 1UL) != 0)
                state.Result *= state.Factor;

            state.RemainingExponent >>= 1;

            if (state.RemainingExponent == 0)
                return FinishPower(state);

            state.Factor *= state.Factor;

            if (!state.NegativeExponent)
            {
                if (state.Result < long.MinValue ||
                    state.Result > long.MaxValue ||
                    state.Factor < long.MinValue ||
                    state.Factor > long.MaxValue)
                {
                    return OperationResult.OpError(
                        RuntimeError.Overflow()
                    );
                }
            }
            else
            {
                if (BigInteger.Abs(state.Result) > DTDecimal.Scale ||
                    BigInteger.Abs(state.Factor) > DTDecimal.Scale)
                {
                    return OperationResult.Success(
                        DataValue.FromDecimalRaw(0)
                    );
                }
            }

            return OperationResult.Continued(state);
        }

        public OperationResult Power(OperationCall opCall)
        {
            IntPowerState state;

            if (opCall.State == null)
            {
                var values = GetTwoOperationValues(opCall);

                if (!values.HasValue)
                    return OperationResult.NotImplemented();

                var baseValue = values.Value.leftValue;
                var exponent = values.Value.rightValue;

                bool negativeExponent = exponent < 0;

                if (negativeExponent && baseValue == 0)
                    return OperationResult.OpError(
                        RuntimeError.DivisionByZero()
                    );

                state = new IntPowerState(
                    baseValue,
                    GetExponentMagnitude(exponent),
                    negativeExponent
                );
            }
            else
            {
                state = opCall.State as IntPowerState
                    ?? throw new InvalidOperationException(
                        "Power received an invalid OperationState."
                    );
            }

            return AdvancePower(state);
        }

        public override OperationResult Hash(OperationCall opCall)
        {
            return OperationResult.Success(opCall.Arguments[0]);
        }

        public OperationResult Less(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (values.HasValue)
            {
                var a = values.Value.leftValue;
                var b = values.Value.rightValue;
                return CreateSuccessBoolResult(a < b);
            }
            return OperationResult.NotImplemented();
        }

        public OperationResult LessOrEqual(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (values.HasValue)
            {
                var a = values.Value.leftValue;
                var b = values.Value.rightValue;
                return CreateSuccessBoolResult(a <= b);
            }
            return OperationResult.NotImplemented();
        }

        public OperationResult Greater(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (values.HasValue)
            {
                var a = values.Value.leftValue;
                var b = values.Value.rightValue;
                return CreateSuccessBoolResult(a > b);
            }
            return OperationResult.NotImplemented();
        }

        public OperationResult GreaterOrEqual(OperationCall opCall)
        {
            var values = GetTwoOperationValues(opCall);

            if (values.HasValue)
            {
                var a = values.Value.leftValue;
                var b = values.Value.rightValue;
                return CreateSuccessBoolResult(a >= b);
            }
            return OperationResult.NotImplemented();
        }

        public OperationResult Not(OperationCall opCall)
        {
            return OperationResult.Success(DataValue.FromBool(opCall.Arguments[0].value == 0));
        }
    }
}