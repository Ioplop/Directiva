using System;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Immutable, lazy signed Int64 arithmetic progression. No individual range values
    /// are stored: a range always uses four abstract heap slots (header and parameters).
    /// </summary>
    public sealed class RangeHeapObject : HeapObject
    {
        public long Start { get; }
        public long Stop { get; }
        public long Step { get; }
        public override uint Size => 4;

        public RangeHeapObject(long start, long stop, long step)
        {
            if (step == 0)
                throw new ArgumentOutOfRangeException(nameof(step), "Range step cannot be zero.");
            Start = start;
            Stop = stop;
            Step = step;
        }

        /// <summary>Exact count without signed subtraction overflow (may exceed Int64.MaxValue).</summary>
        public ulong ElementCount
        {
            get
            {
                if (Step > 0)
                {
                    if (Start >= Stop) return 0;
                    ulong distance = unchecked((ulong)Stop - (ulong)Start);
                    return 1UL + ((distance - 1UL) / (ulong)Step);
                }
                if (Start <= Stop) return 0;
                ulong reverseDistance = unchecked((ulong)Start - (ulong)Stop);
                ulong magnitude = unchecked(0UL - (ulong)Step);
                return 1UL + ((reverseDistance - 1UL) / magnitude);
            }
        }
    }
}
