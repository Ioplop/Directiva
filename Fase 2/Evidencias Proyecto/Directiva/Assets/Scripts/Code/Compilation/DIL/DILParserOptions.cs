using System;

namespace DSCompilation.DIL
{
    /// <summary>
    /// Limits and optional transformations applied while translating a DIL module graph into executable VM code.
    /// </summary>
    public sealed class DILParserOptions
    {
        public int MaxGlobalSlots { get; }
        public bool GenerateDebugMetadata { get; }

        public DILParserOptions(
            int maxGlobalSlots = int.MaxValue,
            bool generateDebugMetadata = false)
        {
            if (maxGlobalSlots < 0)
                throw new ArgumentOutOfRangeException(nameof(maxGlobalSlots));

            MaxGlobalSlots = maxGlobalSlots;
            GenerateDebugMetadata = generateDebugMetadata;
        }
    }
}
