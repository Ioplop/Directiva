using System;

namespace DSCompilation.DIL
{
    /// <summary>
    /// Limits applied while translating a DIL module graph into executable VM code.
    /// </summary>
    public sealed class DILParserOptions
    {
        public int MaxGlobalSlots { get; }

        public DILParserOptions(int maxGlobalSlots = int.MaxValue)
        {
            if (maxGlobalSlots < 0)
                throw new ArgumentOutOfRangeException(nameof(maxGlobalSlots));

            MaxGlobalSlots = maxGlobalSlots;
        }
    }
}
