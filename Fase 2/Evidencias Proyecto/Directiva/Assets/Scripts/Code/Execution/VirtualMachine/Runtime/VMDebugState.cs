#nullable enable

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Current source position reported by DEBUG_* instructions.
    /// Lines are zero-based and spans use [start, end) offsets.
    /// </summary>
    public sealed class VMDebugState
    {
        public string? File { get; internal set; }
        public int Line { get; internal set; } = -1;
        public int SpanStart { get; internal set; } = -1;
        public int SpanEnd { get; internal set; } = -1;

        internal void Reset()
        {
            File = null;
            Line = -1;
            SpanStart = -1;
            SpanEnd = -1;
        }
    }
}
