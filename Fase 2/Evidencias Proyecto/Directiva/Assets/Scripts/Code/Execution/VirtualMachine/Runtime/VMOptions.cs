using System;

namespace DSExecution.VirtualMachine
{
    public sealed class VMOptions
    {
        public int MaxEvaluationStackSize { get; }
        public int MaxCallDepth { get; }

        public VMOptions(
            int maxEvaluationStackSize = 4096,
            int maxCallDepth = 256)
        {
            if (maxEvaluationStackSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEvaluationStackSize));

            if (maxCallDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCallDepth));

            MaxEvaluationStackSize = maxEvaluationStackSize;
            MaxCallDepth = maxCallDepth;
        }
    }
}
