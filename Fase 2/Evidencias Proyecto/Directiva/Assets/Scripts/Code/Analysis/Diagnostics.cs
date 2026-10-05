using System;
using System.Collections.Generic;

namespace Directiva.CodeUI
{
    public enum DiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class CodeDiagnostic
    {
        public string Key { get; }
        public DiagnosticSeverity Severity { get; }
        public IReadOnlyList<object> Arguments { get; }
        public int Start { get; }
        public int Length { get; }

        public CodeDiagnostic(
            string key,
            DiagnosticSeverity severity,
            int start = 0,
            int length = 0,
            params object[] arguments)
        {
            Key = key ?? string.Empty;
            Severity = severity;
            Start = Math.Max(0, start);
            Length = Math.Max(0, length);
            Arguments = arguments ?? Array.Empty<object>();
        }
    }

    public sealed class CodeAnalysisResult
    {
        public static readonly CodeAnalysisResult Empty =
            new CodeAnalysisResult(Array.Empty<CodeDiagnostic>());

        public IReadOnlyList<CodeDiagnostic> Diagnostics { get; }

        public CodeAnalysisResult(IReadOnlyList<CodeDiagnostic> diagnostics)
        {
            Diagnostics = diagnostics ?? Array.Empty<CodeDiagnostic>();
        }
    }
}
