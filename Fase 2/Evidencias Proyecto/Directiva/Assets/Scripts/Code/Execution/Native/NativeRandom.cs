using System;
using System.Numerics;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.Native
{
    /// <summary>
    /// Seeded deterministic random builtins. The shared VMMemoryContext owns PRNG state:
    /// calls by multiple VMs are ordered by the host scheduler, not UnityEngine.Random.
    /// </summary>
    public static class NativeRandom
    {
        // Maps a full UInt64 to [0,bound) with a fixed amount of work and less than
        // 1 / 2^64 quantization bias. No rejection loop can escape the step budget.
        private static ulong Sample(VMMemoryContext memory, ulong bound)
            => (ulong)(((BigInteger)memory.NextRandomUInt64() * bound) >> 64);

        [DScriptNativeFunction("random", "random", 0, 0)]
        public static NativeResult Random(NativeCallContext c)
            => NativeResult.Success(DataValue.FromDecimalRaw((long)Sample(c.Memory, (ulong)DTDecimal.Scale)));

        [DScriptNativeFunction("random", "randint", 2, 2)]
        public static NativeResult RandInt(NativeCallContext c)
        {
            if (!NativeBuiltins.TryInteger(c[0], out long low) ||
                !NativeBuiltins.TryInteger(c[1], out long high))
                return NativeResult.Failure(RuntimeError.InvalidOperation("randint() requires Int/Bool bounds."));
            if (low > high)
                return NativeResult.Failure(RuntimeError.InvalidOperation("randint() lower bound exceeds upper bound."));
            // width=0 denotes the complete 2^64 interval.
            ulong width = unchecked((ulong)high - (ulong)low + 1UL);
            ulong offset = width == 0 ? c.Memory.NextRandomUInt64() : Sample(c.Memory, width);
            return NativeResult.Success(DataValue.FromInt(unchecked((long)((ulong)low + offset))));
        }

        [DScriptNativeFunction("random", "uniform", 2, 2)]
        public static NativeResult Uniform(NativeCallContext c)
        {
            if (!NativeBuiltins.TryNumericRaw(c[0], out BigInteger low) ||
                !NativeBuiltins.TryNumericRaw(c[1], out BigInteger high))
                return NativeResult.Failure(RuntimeError.InvalidOperation("uniform() requires numeric bounds."));
            if (low > high)
                return NativeResult.Failure(RuntimeError.InvalidOperation("uniform() lower bound exceeds upper bound."));
            // The result is fixed-point in [a,b); one deterministic UInt64 draw.
            ulong sample = c.Memory.NextRandomUInt64();
            BigInteger raw = low + (((high - low) * sample) >> 64);
            if (raw < long.MinValue || raw > long.MaxValue)
                return NativeResult.Failure(RuntimeError.Overflow());
            return NativeResult.Success(DataValue.FromDecimalRaw((long)raw));
        }
    }
}
