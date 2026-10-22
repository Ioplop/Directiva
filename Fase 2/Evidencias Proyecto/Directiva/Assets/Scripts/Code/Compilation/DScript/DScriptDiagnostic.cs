using System;

namespace DSCompilation.DScript
{
    /// <summary>Character range in original DScript source; columns and lines are 1-based.</summary>
    public readonly struct SourceSpan
    {
        public int Start { get; }
        public int Length { get; }
        public int Line { get; }
        public int Column { get; }
        public int End => Start + Length;

        public SourceSpan(int start, int length, int line, int column)
        {
            Start = Math.Max(0, start);
            Length = Math.Max(0, length);
            Line = Math.Max(1, line);
            Column = Math.Max(1, column);
        }

        public static SourceSpan Through(SourceSpan first, SourceSpan last) =>
            new SourceSpan(first.Start, Math.Max(0, last.End - first.Start), first.Line, first.Column);

        public override string ToString() => Line + ":" + Column;
    }

    /// <summary>Frontend-only diagnostic, distinct from DIL parsing and runtime errors.</summary>
    public sealed class DScriptDiagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public string Module { get; }
        public SourceSpan Span { get; }

        public DScriptDiagnostic(string module, string code, string message, SourceSpan span)
        {
            Module = module ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Span = span;
        }

        public override string ToString() => Module + ":" + Span + " " + Code + ": " + Message;
    }

    /// <summary>Caps for untrusted player source; no filesystem or Unity dependencies.</summary>
    public sealed class DScriptFrontendOptions
    {
        public int MaxModules { get; }
        public int MaxSourceLength { get; }
        public int MaxDiagnostics { get; }
        public int MaxNestingDepth { get; }

        public DScriptFrontendOptions(int maxModules = 128, int maxSourceLength = 500000,
            int maxDiagnostics = 100, int maxNestingDepth = 64)
        {
            if (maxModules < 1 || maxSourceLength < 1 || maxDiagnostics < 1 || maxNestingDepth < 1)
                throw new ArgumentOutOfRangeException(nameof(maxModules), "Frontend limits must be positive.");
            MaxModules = maxModules;
            MaxSourceLength = maxSourceLength;
            MaxDiagnostics = maxDiagnostics;
            MaxNestingDepth = maxNestingDepth;
        }
    }
}
