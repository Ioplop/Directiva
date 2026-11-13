using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Modules;
using DOrchestration;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Transient editor diagnostics for the unsaved buffer. Reuses the compiler's
    /// recovering frontend, without publishing to the console or executing DIL.
    /// </summary>
    public static class DScriptLiveDiagnostics
    {
        private const int MaximumVisibleIssues = 40;

        public static IReadOnlyList<DScriptDiagnostic> Analyze(string module, string source,
            IDScriptSourceProvider provider) => Analyze(module, source, provider, -1);

        /// <summary>
        /// Caret-aware diagnostics. -1 retains the original compiler-only behavior for
        /// callers that do not have an editor selection (e.g. existing tests).
        /// </summary>
        public static IReadOnlyList<DScriptDiagnostic> Analyze(string module, string source,
            IDScriptSourceProvider provider, int caret)
        {
            source ??= string.Empty;
            if (string.IsNullOrEmpty(module) || provider == null)
                return Array.Empty<DScriptDiagnostic>();

            try
            {
                var analysis = DScriptFrontend.AnalyzeForRefactoring(module,
                    new CurrentBufferProvider(module, source, provider), SimulationDScriptProfile.Options);
                // Diagnostics from imported files belong to their own editor buffers.
                // Zero-length errors have no glyph to underline (usually unfinished tokens).
                var visible = analysis.Diagnostics
                    .Where(d => d.Module == module && d.Span.Length > 0 &&
                        d.Span.Start >= 0 && d.Span.End <= source.Length).ToList();

                // The lexer reports DS1006 at EOF, without a highlightable span.
                // Find its actual opener only after the player has moved on to a
                // separate statement; otherwise leave an active multiline call alone.
                if (caret >= 0)
                {
                    var openings = DScriptEditorSyntaxContext.OpeningsAt(source, source.Length, out _);
                    foreach (var open in openings)
                    {
                        if (!DScriptEditorSyntaxContext.IsAbandoned(source, open, caret)) continue;
                        int lineStart = source.LastIndexOf('\n', Math.Max(0, open.Offset - 1)) + 1;
                        visible.Insert(0, new DScriptDiagnostic(module, "DS1006",
                            "Unclosed bracket or parenthesis.",
                            new SourceSpan(open.Offset, 1, open.Line + 1, open.Offset - lineStart + 1),
                            "bracket_unclosed"));
                    }
                }
                return visible.GroupBy(d => (d.Span.Start, d.Span.Length, d.Code))
                    .Select(g => g.First()).Take(MaximumVisibleIssues).ToArray();
            }
            catch (Exception)
            {
                // The editor must remain editable if an imported file is damaged.
                return Array.Empty<DScriptDiagnostic>();
            }
        }

        /// <summary>
        /// Optional, hover-only typo hint. Only suggest names the existing completion
        /// engine resolves at the same source location; never silently replace text.
        /// </summary>
        public static string Suggest(string module, string source, DScriptDiagnostic issue,
            IDScriptSourceProvider provider)
        {
            if (issue == null || issue.Code != "DS4016" || provider == null ||
                string.IsNullOrEmpty(module) || source == null)
                return null;

            int start = issue.Span.Start;
            int length = issue.Span.Length;
            if (length < 2 || start < 0 || start + length > source.Length)
                return null;
            string name = source.Substring(start, length);
            if (name.Any(c => !char.IsLetterOrDigit(c) && c != '_')) return null;

            // Remove the misspelled token before completing a single-letter prefix.
            // This avoids the completion engine's intentional mid-token suppression.
            string temporary = source.Substring(0, start) + name.Substring(0, 1) +
                source.Substring(start + length);
            var available = DScriptCompletionEngine.Complete(module, temporary, start + 1,
                new CurrentBufferProvider(module, temporary, provider));
            int limit = Math.Max(2, name.Length / 3);
            return available.Items.Select(item => new { item.Name, Distance = Distance(name, item.Name) })
                .Where(item => item.Distance > 0 && item.Distance <= limit)
                .OrderBy(item => item.Distance).ThenBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => item.Name).FirstOrDefault();
        }

        private static int Distance(string left, string right)
        {
            // Names have bounded lengths here; avoid large allocations if source is malicious.
            if (left.Length > 64 || right.Length > 64) return int.MaxValue;
            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                        previous[j - 1] + (char.ToLowerInvariant(left[i - 1]) ==
                            char.ToLowerInvariant(right[j - 1]) ? 0 : 1));
                var temp = previous; previous = current; current = temp;
            }
            return previous[right.Length];
        }

        private sealed class CurrentBufferProvider : IDScriptSourceProvider
        {
            private readonly string module;
            private readonly string source;
            private readonly IDScriptSourceProvider original;
            public CurrentBufferProvider(string module, string source, IDScriptSourceProvider original)
            { this.module = module; this.source = source; this.original = original; }
            public bool TryReadModule(string name, out string text)
            {
                if (name == module) { text = source; return true; }
                return original.TryReadModule(name, out text);
            }
        }
    }
}
