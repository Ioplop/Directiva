using System;
using System.Numerics;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.Native
{
    /// <summary>Basic language builtins, available without an import statement.</summary>
    public static class NativeBuiltins
    {
        [DScriptNativeFunction("", "print", 1, 1)]
        public static NativeResult Print(NativeCallContext c) => c.Print(c[0]);

        [DScriptNativeFunction("", "range", 1, 3)]
        public static NativeResult Range(NativeCallContext c)
        {
            long start = 0, stop, step = 1;
            for (int i = 0; i < c.Count; i++)
                if (!TryInteger(c[i], out _))
                    return NativeResult.Failure(RuntimeError.InvalidOperation("range() needs Int/Bool arguments."));
            stop = unchecked((long)c[c.Count - 1].value);
            if (c.Count >= 2)
            {
                start = unchecked((long)c[0].value);
                stop = unchecked((long)c[1].value);
            }
            if (c.Count == 3) step = unchecked((long)c[2].value);
            if (step == 0)
                return NativeResult.Failure(RuntimeError.InvalidOperation("range() step cannot be zero."));
            if (!c.Memory.TryAllocHeap(new RangeHeapObject(start, stop, step), out uint id))
                return NativeResult.Failure(RuntimeError.OutOfMemory());
            return NativeResult.Success(DataValue.FromRangeReference(id));
        }

        [DScriptNativeFunction("", "len", 1, 1)]
        public static NativeResult Length(NativeCallContext c) => c.Operate(OperationId.Length, c[0]);

        [DScriptNativeFunction("", "abs", 1, 1)]
        public static NativeResult Abs(NativeCallContext c)
        {
            DataValue x = c[0];
            if (x.dataType != DataTypeId.Int && x.dataType != DataTypeId.Decimal && x.dataType != DataTypeId.Bool)
                return NativeResult.Failure(RuntimeError.InvalidOperation("abs() needs a numeric argument."));
            long raw = unchecked((long)x.value);
            if (raw == long.MinValue) return NativeResult.Failure(RuntimeError.Overflow());
            long magnitude = raw < 0 ? -raw : raw;
            return NativeResult.Success(x.dataType == DataTypeId.Decimal
                ? DataValue.FromDecimalRaw(magnitude) : DataValue.FromInt(magnitude));
        }

        [DScriptNativeFunction("", "min", 2, 2)]
        public static NativeResult Min(NativeCallContext c) => Extremum(c, true);
        [DScriptNativeFunction("", "max", 2, 2)]
        public static NativeResult Max(NativeCallContext c) => Extremum(c, false);

        private static NativeResult Extremum(NativeCallContext c, bool minimum)
        {
            if (!TryNumericRaw(c[0], out BigInteger a) || !TryNumericRaw(c[1], out BigInteger b))
                return NativeResult.Failure(RuntimeError.InvalidOperation("min()/max() require numeric arguments."));
            int compare = a.CompareTo(b);
            // Consistent with Python, equal values preserve the first argument's identity/type.
            return NativeResult.Success((minimum ? compare <= 0 : compare >= 0) ? c[0] : c[1]);
        }

        [DScriptNativeFunction("", "int", 1, 1)]
        public static NativeResult Int(NativeCallContext c) => c[0].dataType == DataTypeId.Bool
            ? NativeResult.Success(DataValue.FromInt(unchecked((long)c[0].value)))
            : c.Cast(c[0], DataTypeId.Int);
        [DScriptNativeFunction("", "decimal", 1, 1)]
        public static NativeResult Decimal(NativeCallContext c) => c[0].dataType == DataTypeId.Bool
            ? NativeResult.Success(DataValue.FromDecimalRaw(unchecked((long)c[0].value) * DTDecimal.Scale))
            : c.Cast(c[0], DataTypeId.Decimal);
        [DScriptNativeFunction("", "str", 1, 1)]
        public static NativeResult Str(NativeCallContext c) => c.Cast(c[0], DataTypeId.String);
        [DScriptNativeFunction("", "bool", 1, 1)]
        public static NativeResult Bool(NativeCallContext c) => c.Operate(OperationId.IsTruthy, c[0]);

        [DScriptNativeFunction("", "clamp", 3, 3)]
        public static NativeResult Clamp(NativeCallContext c)
        {
            if (!TryNumericRaw(c[0], out BigInteger x) ||
                !TryNumericRaw(c[1], out BigInteger low) ||
                !TryNumericRaw(c[2], out BigInteger high))
                return NativeResult.Failure(RuntimeError.InvalidOperation("clamp() requires numeric arguments."));
            if (low > high)
                return NativeResult.Failure(RuntimeError.InvalidOperation("clamp() minimum exceeds maximum."));
            return NativeResult.Success(x < low ? c[1] : x > high ? c[2] : c[0]);
        }

        internal static bool TryInteger(DataValue value, out long number)
        {
            if (value.dataType == DataTypeId.Int || value.dataType == DataTypeId.Bool)
            {
                number = unchecked((long)value.value);
                return true;
            }
            number = 0;
            return false;
        }

        /// <summary>Comparable numeric values in DTDecimal.Scale fixed-point units.</summary>
        internal static bool TryNumericRaw(DataValue value, out BigInteger raw)
        {
            if (value.dataType == DataTypeId.Decimal)
            {
                raw = unchecked((long)value.value);
                return true;
            }
            if (TryInteger(value, out long integer))
            {
                raw = (BigInteger)integer * DTDecimal.Scale;
                return true;
            }
            raw = BigInteger.Zero;
            return false;
        }
    }
}
