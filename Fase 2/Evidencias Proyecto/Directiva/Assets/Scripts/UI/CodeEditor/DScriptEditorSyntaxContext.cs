using System;
using System.Collections.Generic;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Lightweight lexical state for IDE assistance. Tracks delimiters without requiring a
    /// complete AST, and deliberately ignores brackets in strings and comments.
    /// This is not a replacement for the DScript lexer or compiler.
    /// </summary>
    internal static class DScriptEditorSyntaxContext
    {
        internal sealed class Opening
        {
            internal char Kind;
            internal int Offset;
            internal int Line;
            internal int Indent;
            internal int Commas;
        }

        internal static List<Opening> OpeningsAt(string source, int end, out bool insideLiteral)
        {
            source ??= string.Empty;
            end = Math.Max(0, Math.Min(end, source.Length));
            var stack = new List<Opening>();
            bool comment = false, escaped = false;
            char quote = '\0';
            int line = 0, lineStart = 0;
            for (int i = 0; i < end; i++)
            {
                char c = source[i];
                if (c == '\n')
                {
                    comment = false;
                    // DScript literals cannot continue onto subsequent source lines.
                    quote = '\0';
                    escaped = false;
                    line++;
                    lineStart = i + 1;
                    continue;
                }
                if (comment) continue;
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '#') { comment = true; continue; }
                if (c == '\'' || c == '"') { quote = c; escaped = false; continue; }
                if (c == '(' || c == '[' || c == '{')
                {
                    int indent = 0;
                    while (lineStart + indent < source.Length &&
                        (source[lineStart + indent] == ' ' || source[lineStart + indent] == '\t'))
                        indent++;
                    stack.Add(new Opening { Kind = c, Offset = i, Line = line, Indent = indent });
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    char expected = c == ')' ? '(' : c == ']' ? '[' : '{';
                    if (stack.Count > 0 && stack[stack.Count - 1].Kind == expected)
                        stack.RemoveAt(stack.Count - 1);
                    else
                    {
                        // A mismatched closer is an independent compiler error. Do not
                        // synthesize diagnostics from an unreliable nesting state.
                        stack.Clear();
                    }
                }
                else if (c == ',' && stack.Count > 0 && stack[stack.Count - 1].Kind == '(')
                    stack[stack.Count - 1].Commas++;
            }
            insideLiteral = comment || quote != '\0';
            return stack;
        }

        internal static bool TryCallName(string source, int openOffset, out string name)
        {
            name = null;
            int end = openOffset;
            while (end > 0 && char.IsWhiteSpace(source[end - 1])) end--;
            int start = end;
            while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_'))
                start--;
            if (start == end || !(char.IsLetter(source[start]) || source[start] == '_')) return false;
            name = source.Substring(start, end - start);
            int lineStart = source.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            string before = source.Substring(lineStart, start - lineStart).TrimStart();
            // A `def name(` header declares parameters; it is not a call.
            if (before == "def") return false;
            return true;
        }

        /// <summary>
        /// Marks an unfinished opening only after the caret has reached a separate,
        /// peer-level statement. A call being continued on following lines is NOT an error.
        /// False negatives are preferable to guessing that an active multiline call is bad.
        /// </summary>
        internal static bool IsAbandoned(string source, Opening opening, int caret)
        {
            caret = Math.Max(0, Math.Min(caret, source.Length));
            int caretLine = CountLines(source, caret);
            if (caretLine <= opening.Line + 1) return false;
            int start = source.IndexOf('\n', opening.Offset);
            if (start < 0) return false;
            start++;
            bool separatedByBlankLine = false;
            int line = opening.Line + 1;
            while (start < caret && line <= caretLine)
            {
                int end = source.IndexOf('\n', start);
                if (end < 0 || end > caret) end = caret;
                string full = source.Substring(start, end - start);
                string trimmed = full.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    if (trimmed.Length == 0) separatedByBlankLine = true;
                }
                else
                {
                    int indent = full.Length - full.TrimStart(' ', '\t').Length;
                    if (indent <= opening.Indent && line >= opening.Line + 2 &&
                        (separatedByBlankLine || BeginsIndependentStatement(trimmed)))
                        return true;
                }
                if (end >= caret) break;
                start = end + 1;
                line++;
            }
            return false;
        }

        private static bool BeginsIndependentStatement(string s)
        {
            string[] keywords = { "def ", "shared ", "global ", "import ", "from ", "return ",
                "return", "pass", "break", "continue", "if ", "elif ", "else:",
                "while ", "for " };
            foreach (string keyword in keywords)
                if (s == keyword || s.StartsWith(keyword, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static int CountLines(string text, int end)
        {
            int count = 0;
            for (int i = 0; i < end; i++) if (text[i] == '\n') count++;
            return count;
        }
    }
}
