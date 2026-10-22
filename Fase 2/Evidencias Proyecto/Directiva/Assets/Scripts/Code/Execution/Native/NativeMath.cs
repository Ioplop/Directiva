using System;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;

namespace DSExecution.Native
{
    /// <summary>
    /// Fixed-point public math API. System.Math is quantized to four fractional digits;
    /// strict bit-identical transcendental results on every host are NOT guaranteed yet.
    /// No unbounded work or Unity frame state is accessed.
    /// </summary>
    public static class NativeMath
    {
        private static bool Numeric(DataValue value, out double number)
        {
            switch (value.dataType)
            {
                case DataTypeId.Int:
                case DataTypeId.Bool:
                    number = unchecked((long)value.value);
                    return true;
                case DataTypeId.Decimal:
                    number = (double)unchecked((long)value.value) / DTDecimal.Scale;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        private static NativeResult DecimalResult(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
                return NativeResult.Failure(RuntimeError.InvalidOperation("Math result is not finite."));
            double scaled = Math.Round(number * DTDecimal.Scale, 0, MidpointRounding.AwayFromZero);
            if (scaled < long.MinValue || scaled >= 9223372036854775808d)
                return NativeResult.Failure(RuntimeError.Overflow());
            return NativeResult.Success(DataValue.FromDecimalRaw((long)scaled));
        }

        private static NativeResult Unary(NativeCallContext c, Func<double, double> operation)
        {
            if (!Numeric(c[0], out double x))
                return NativeResult.Failure(RuntimeError.InvalidOperation("Math function expects a numeric value."));
            return DecimalResult(operation(x));
        }

        [DScriptNativeFunction("math", "sin", 1, 1)]
        public static NativeResult Sin(NativeCallContext c) => Unary(c, Math.Sin);
        [DScriptNativeFunction("math", "cos", 1, 1)]
        public static NativeResult Cos(NativeCallContext c) => Unary(c, Math.Cos);
        [DScriptNativeFunction("math", "tan", 1, 1)]
        public static NativeResult Tan(NativeCallContext c) => Unary(c, Math.Tan);
        [DScriptNativeFunction("math", "asin", 1, 1)]
        public static NativeResult Asin(NativeCallContext c) => Unary(c, Math.Asin);
        [DScriptNativeFunction("math", "acos", 1, 1)]
        public static NativeResult Acos(NativeCallContext c) => Unary(c, Math.Acos);
        [DScriptNativeFunction("math", "atan", 1, 1)]
        public static NativeResult Atan(NativeCallContext c) => Unary(c, Math.Atan);
        [DScriptNativeFunction("math", "sqrt", 1, 1)]
        public static NativeResult Sqrt(NativeCallContext c) => Unary(c, Math.Sqrt);
        [DScriptNativeFunction("math", "degrees", 1, 1)]
        public static NativeResult Degrees(NativeCallContext c) => Unary(c, x => x * (180.0 / Math.PI));
        [DScriptNativeFunction("math", "radians", 1, 1)]
        public static NativeResult Radians(NativeCallContext c) => Unary(c, x => x * (Math.PI / 180.0));

        [DScriptNativeFunction("math", "atan2", 2, 2)]
        public static NativeResult Atan2(NativeCallContext c)
        {
            if (!Numeric(c[0], out double y) || !Numeric(c[1], out double x))
                return NativeResult.Failure(RuntimeError.InvalidOperation("atan2() needs two numeric arguments."));
            return DecimalResult(Math.Atan2(y, x));
        }

        [DScriptNativeFunction("math", "hypot", 2, 2)]
        public static NativeResult Hypot(NativeCallContext c)
        {
            if (!Numeric(c[0], out double x) || !Numeric(c[1], out double y))
                return NativeResult.Failure(RuntimeError.InvalidOperation("hypot() needs two numeric arguments."));
            // Avoid unnecessary intermediate overflow.
            double magnitude = Math.Max(Math.Abs(x), Math.Abs(y));
            return magnitude == 0 ? DecimalResult(0) :
                DecimalResult(magnitude * Math.Sqrt((x / magnitude) * (x / magnitude) + (y / magnitude) * (y / magnitude)));
        }

        [DScriptNativeFunction("math", "floor", 1, 1)]
        public static NativeResult Floor(NativeCallContext c) => IntegerRound(c, false);
        [DScriptNativeFunction("math", "ceil", 1, 1)]
        public static NativeResult Ceil(NativeCallContext c) => IntegerRound(c, true);

        private static NativeResult IntegerRound(NativeCallContext c, bool ceiling)
        {
            DataValue value = c[0];
            if (value.dataType == DataTypeId.Int || value.dataType == DataTypeId.Bool)
                return NativeResult.Success(DataValue.FromInt(unchecked((long)value.value)));
            if (value.dataType != DataTypeId.Decimal)
                return NativeResult.Failure(RuntimeError.InvalidOperation("floor()/ceil() need numeric input."));
            long raw = unchecked((long)value.value);
            long quotient = raw / DTDecimal.Scale;
            long remainder = raw % DTDecimal.Scale;
            if (ceiling && raw > 0 && remainder != 0) quotient++;
            if (!ceiling && raw < 0 && remainder != 0) quotient--;
            return NativeResult.Success(DataValue.FromInt(quotient));
        }
    }
}
