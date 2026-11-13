using System;
using System.Collections.Generic;

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

    public enum DScriptDiagnosticSeverity { Error, Warning }

    /// <summary>Frontend-only diagnostic, distinct from DIL parsing and runtime errors.</summary>
    public sealed class DScriptDiagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public string Module { get; }
        public SourceSpan Span { get; }
        public string LocalizationKey { get; }
        public object[] LocalizationArguments { get; }
        public DScriptDiagnosticSeverity Severity { get; }

        public DScriptDiagnostic(string module, string code, string message, SourceSpan span,
            string localizationKey = null, params object[] localizationArguments)
            : this(module, code, message, span, DScriptDiagnosticSeverity.Error,
                localizationKey, localizationArguments) { }

        public DScriptDiagnostic(string module, string code, string message, SourceSpan span,
            DScriptDiagnosticSeverity severity, string localizationKey = null,
            params object[] localizationArguments)
        {
            Module = module ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Span = span;
            Severity = severity;
            LocalizationKey = localizationKey;
            LocalizationArguments = localizationArguments ?? Array.Empty<object>();
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
        private readonly HashSet<string> implicitSelfMethods;

        // The host supplies the method names; the generic compiler does not depend on
        // DSimulation or DOrchestration. Properties are deliberately not callable names.
        public DScriptFrontendOptions(int maxModules = 128, int maxSourceLength = 500000,
            int maxDiagnostics = 100, int maxNestingDepth = 64,
            IEnumerable<string> implicitSelfMethods = null)
        {
            if (maxModules < 1 || maxSourceLength < 1 || maxDiagnostics < 1 || maxNestingDepth < 1)
                throw new ArgumentOutOfRangeException(nameof(maxModules), "Frontend limits must be positive.");
            MaxModules = maxModules;
            MaxSourceLength = maxSourceLength;
            MaxDiagnostics = maxDiagnostics;
            MaxNestingDepth = maxNestingDepth;
            this.implicitSelfMethods = implicitSelfMethods == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(implicitSelfMethods, StringComparer.Ordinal);
        }

        public bool IsImplicitSelfMethod(string name) => implicitSelfMethods.Contains(name);
    }
}
