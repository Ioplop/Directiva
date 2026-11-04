using System;
using System.Collections.Generic;
using DSCompilation.DScript;
using DSCompilation.DScript.Semantics;

namespace Directiva.CodeUI
{
    /// <summary>
    /// A refactoring is allowed to retain errors that already existed in the source.
    /// Renaming identifiers shifts offsets, so compare diagnostic codes and adjusted
    /// source positions instead of full messages or raw character offsets.
    /// </summary>
    internal static class DScriptRenameDiagnostics
    {
        public static bool IntroducesErrors(DScriptAnalysis before, DScriptAnalysis after,
            Func<string, int, int> mapPosition)
        {
            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var diagnostic in before.Diagnostics)
            {
                string key = Key(diagnostic.Module, diagnostic.Code,
                    mapPosition(diagnostic.Module, diagnostic.Span.Start));
                expected.TryGetValue(key, out int count);
                expected[key] = count + 1;
            }
            foreach (var diagnostic in after.Diagnostics)
            {
                string key = Key(diagnostic.Module, diagnostic.Code, diagnostic.Span.Start);
                if (!expected.TryGetValue(key, out int count) || count == 0)
                    return true;
                expected[key] = count - 1;
            }
            return false;
        }

        private static string Key(string module, string code, int position) =>
            module + "\n" + code + "\n" + position;

        /// <summary>Translate an original position after non-overlapping identifier edits.</summary>
        public static int MapPosition(int position, IEnumerable<SourceSpan> edits, int replacementLength)
        {
            int shift = 0;
            foreach (var edit in edits)
            {
                if (position < edit.Start) break;
                if (position < edit.End)
                    return edit.Start + shift + Math.Min(position - edit.Start, replacementLength);
                shift += replacementLength - edit.Length;
            }
            return position + shift;
        }
    }
}
