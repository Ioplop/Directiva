using System;
using System.Collections.Generic;
using System.Text;
using DSCompilation.DScript.Lexing;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Editor-only syntax categories. They do not imply type checking or valid DScript.
    /// The compiler lexer remains the authority for token boundaries.
    /// </summary>
    public enum DScriptSyntaxKind
    {
        Plain, Keyword, Literal, Number, String, Comment, Operator,
        Variable, Shared, Function, Module, Member
    }

    public readonly struct DScriptSyntaxSpan
    {
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        public DScriptSyntaxKind Kind { get; }

        public DScriptSyntaxSpan(int start, int length, DScriptSyntaxKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }
    }

    /// <summary>
    /// An immutable view of one editor buffer, including partially written code.
    /// The line renderer only formats requested visible columns, even for very large files.
    /// No Unity UI objects, file I/O, or compiler side effects occur here.
    /// </summary>
    public sealed class DScriptSyntaxDocument
    {
        private readonly string _source;
        private readonly int[] _lineStarts;
        private readonly DScriptSyntaxSpan[] _spans;

        internal DScriptSyntaxDocument(string source, int[] lineStarts, DScriptSyntaxSpan[] spans)
        {
            _source = source;
            _lineStarts = lineStarts;
            _spans = spans;
        }

        public string Source => _source;
        public int LineCount => _lineStarts.Length;
        public IReadOnlyList<DScriptSyntaxSpan> Spans => _spans;

        public int LineLength(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= LineCount)
                return 0;
            int end = lineIndex + 1 < LineCount ? _lineStarts[lineIndex + 1] - 1 : _source.Length;
            return Math.Max(0, end - _lineStarts[lineIndex]);
        }

        public DScriptSyntaxKind KindAt(int offset)
        {
            if (offset < 0 || offset >= _source.Length)
                return DScriptSyntaxKind.Plain;
            int index = SpanBeforeOrAt(offset);
            return index >= 0 && offset < _spans[index].End
                ? _spans[index].Kind : DScriptSyntaxKind.Plain;
        }

        /// <summary>
        /// Protects literal source text when adding UI Toolkit rich-text colors.
        /// TextElement.text is not HTML: HTML entities would render visibly.
        /// Color values must be #RRGGBBAA.
        /// </summary>
        public string FormatLine(int lineIndex, int startColumn, int columnCount,
            Func<DScriptSyntaxKind, string> colorHex)
        {
            if (lineIndex < 0 || lineIndex >= LineCount || columnCount <= 0)
                return string.Empty;
            if (colorHex == null) throw new ArgumentNullException(nameof(colorHex));
            int lineStart = _lineStarts[lineIndex];
            int lineLength = LineLength(lineIndex);
            int firstColumn = Math.Min(lineLength, Math.Max(0, startColumn));
            int lastColumn = (int)Math.Min(lineLength, (long)firstColumn + columnCount);
            int from = lineStart + firstColumn;
            int until = lineStart + lastColumn;
            var result = new StringBuilder(Math.Max(16, (until - from) * 2));
            int cursor = from;
            int spanIndex = SpanBeforeOrAt(cursor);
            if (spanIndex < 0 || _spans[spanIndex].End <= cursor)
                spanIndex++;

            while (cursor < until)
            {
                while (spanIndex < _spans.Length && _spans[spanIndex].End <= cursor)
                    spanIndex++;

                if (spanIndex >= _spans.Length || _spans[spanIndex].Start > cursor)
                {
                    int plainEnd = spanIndex >= _spans.Length ? until : Math.Min(until, _spans[spanIndex].Start);
                    AppendLiteralSource(result, _source, cursor, plainEnd);
                    cursor = plainEnd;
                    continue;
                }

                var span = _spans[spanIndex];
                int coloredEnd = Math.Min(until, span.End);
                string color = colorHex(span.Kind);
                if (!string.IsNullOrEmpty(color))
                    result.Append("<color=").Append(color).Append('>');
                AppendLiteralSource(result, _source, cursor, coloredEnd);
                if (!string.IsNullOrEmpty(color))
                    result.Append("</color>");
                cursor = coloredEnd;
            }
            return result.ToString();
        }

        private int SpanBeforeOrAt(int offset)
        {
            int low = 0, high = _spans.Length;
            while (low < high)
            {
                int mid = low + ((high - low) / 2);
                if (_spans[mid].Start <= offset) low = mid + 1;
                else high = mid;
            }
            return low - 1;
        }

        private static void AppendLiteralSource(StringBuilder result, string source, int start, int end)
        {
            // Unity TextCore rich text is not HTML: &lt;, &gt; and &amp; are
            // displayed literally when assigned to a Label from C#.
            // Only '<' can open a tag. Protect each occurrence *individually*
            // so even a source string containing "</noparse>" cannot close the
            // protection scope and inject markup. One source '<' stays one glyph.
            for (int i = start; i < end; i++)
            {
                if (source[i] == '<')
                    result.Append("<noparse><</noparse>");
                else
                    result.Append(source[i]);
            }
        }
    }

    public static class DScriptSyntaxHighlighter
    {
        /// <summary>
        /// Tolerant highlighting on every draft, including syntax errors. The existing
        /// DScript lexer determines tokens; comments are scanned separately because it
        /// intentionally discards them. Contextual names are best-effort visual hints.
        /// </summary>
        public static DScriptSyntaxDocument Analyze(string text)
        {
            string source = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            var starts = new List<int> { 0 };
            for (int i = 0; i < source.Length; i++)
                if (source[i] == '\n') starts.Add(i + 1);

            var lexer = new DScriptLexer("<editor>", source);
            var tokens = lexer.Tokenize();
            var nameOverrides = new Dictionary<int, DScriptSyntaxKind>();
            var sharedNames = new HashSet<string>(StringComparer.Ordinal);
            var moduleNames = new HashSet<string>(StringComparer.Ordinal);

            // First pass: declarations may occur after references in an incomplete file.
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i + 1].Kind != TokenKind.Identifier) continue;
                if (tokens[i].Kind == TokenKind.Def)
                    nameOverrides[i + 1] = DScriptSyntaxKind.Function;
                else if (tokens[i].Kind == TokenKind.Shared)
                {
                    sharedNames.Add(tokens[i + 1].Text);
                    nameOverrides[i + 1] = DScriptSyntaxKind.Shared;
                }
            }

            // Import paths and aliases are modules; from-import names themselves
            // may be functions or variables, so are left to regular classification.
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.Import && tokens[i].Kind != TokenKind.From)
                    continue;
                // The import keyword in `from x import name` is not a module import.
                if (tokens[i].Kind == TokenKind.Import)
                {
                    bool partOfFrom = false;
                    for (int previous = i - 1; previous >= 0; previous--)
                    {
                        if (tokens[previous].Kind == TokenKind.Newline) break;
                        if (tokens[previous].Kind == TokenKind.From)
                        {
                            partOfFrom = true;
                            break;
                        }
                    }
                    if (partOfFrom) continue;
                }
                bool fromImport = tokens[i].Kind == TokenKind.From;
                bool inModulePath = true;
                bool aliasNext = false;
                bool firstComponent = true;
                for (int j = i + 1; j < tokens.Count; j++)
                {
                    var kind = tokens[j].Kind;
                    if (kind == TokenKind.Newline || kind == TokenKind.End) break;
                    if (fromImport && kind == TokenKind.Import)
                    {
                        inModulePath = false;
                        aliasNext = false;
                        continue;
                    }
                    if (kind == TokenKind.As)
                    {
                        aliasNext = true;
                        continue;
                    }
                    if (kind == TokenKind.Comma)
                    {
                        firstComponent = true;
                        continue;
                    }
                    if (kind != TokenKind.Identifier) continue;
                    if (inModulePath)
                    {
                        nameOverrides[j] = DScriptSyntaxKind.Module;
                        if (aliasNext || firstComponent)
                            moduleNames.Add(tokens[j].Text);
                    }
                    if (aliasNext) aliasNext = false;
                    firstComponent = false;
                }
            }

            var spans = new List<DScriptSyntaxSpan>();
            var stringSpans = new List<DScriptSyntaxSpan>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Span.Length <= 0) continue;
                DScriptSyntaxKind kind;
                if (nameOverrides.TryGetValue(i, out var overrideKind))
                    kind = overrideKind;
                else if (token.Kind == TokenKind.Identifier)
                {
                    bool member = i > 0 && tokens[i - 1].Kind == TokenKind.Dot;
                    bool call = i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.LeftParen;
                    if (member) kind = call ? DScriptSyntaxKind.Function : DScriptSyntaxKind.Member;
                    else if (moduleNames.Contains(token.Text)) kind = DScriptSyntaxKind.Module;
                    else if (sharedNames.Contains(token.Text)) kind = DScriptSyntaxKind.Shared;
                    else kind = call ? DScriptSyntaxKind.Function : DScriptSyntaxKind.Variable;
                }
                else kind = Classify(token.Kind);
                if (kind == DScriptSyntaxKind.Plain) continue;
                var span = new DScriptSyntaxSpan(token.Span.Start, token.Span.Length, kind);
                spans.Add(span);
                if (kind == DScriptSyntaxKind.String) stringSpans.Add(span);
            }

            // An unquoted '#' begins a comment, including inside unfinished lines.
            int stringIndex = 0;
            for (int line = 0; line < starts.Count; line++)
            {
                int begin = starts[line];
                int end = line + 1 < starts.Count ? starts[line + 1] - 1 : source.Length;
                for (int pos = begin; pos < end; pos++)
                {
                    if (source[pos] != '#') continue;
                    while (stringIndex < stringSpans.Count && stringSpans[stringIndex].End <= pos)
                        stringIndex++;
                    if (stringIndex < stringSpans.Count &&
                        stringSpans[stringIndex].Start <= pos && pos < stringSpans[stringIndex].End)
                        continue;
                    spans.Add(new DScriptSyntaxSpan(pos, end - pos, DScriptSyntaxKind.Comment));
                    break;
                }
            }

            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            return new DScriptSyntaxDocument(source, starts.ToArray(), spans.ToArray());
        }

        private static DScriptSyntaxKind Classify(TokenKind kind)
        {
            switch (kind)
            {
                case TokenKind.Integer:
                case TokenKind.Decimal:
                    return DScriptSyntaxKind.Number;
                case TokenKind.String:
                    return DScriptSyntaxKind.String;
                case TokenKind.True:
                case TokenKind.False:
                case TokenKind.None:
                    return DScriptSyntaxKind.Literal;
                case TokenKind.Def:
                case TokenKind.Return:
                case TokenKind.If:
                case TokenKind.Elif:
                case TokenKind.Else:
                case TokenKind.While:
                case TokenKind.For:
                case TokenKind.In:
                case TokenKind.Break:
                case TokenKind.Continue:
                case TokenKind.Pass:
                case TokenKind.Global:
                case TokenKind.Shared:
                case TokenKind.Import:
                case TokenKind.From:
                case TokenKind.As:
                case TokenKind.And:
                case TokenKind.Or:
                case TokenKind.Not:
                    return DScriptSyntaxKind.Keyword;
                case TokenKind.Plus:
                case TokenKind.Minus:
                case TokenKind.Star:
                case TokenKind.Slash:
                case TokenKind.DoubleSlash:
                case TokenKind.Percent:
                case TokenKind.DoubleStar:
                case TokenKind.Equals:
                case TokenKind.EqualEqual:
                case TokenKind.NotEqual:
                case TokenKind.Less:
                case TokenKind.LessEqual:
                case TokenKind.Greater:
                case TokenKind.GreaterEqual:
                case TokenKind.PlusEqual:
                case TokenKind.MinusEqual:
                case TokenKind.StarEqual:
                case TokenKind.SlashEqual:
                case TokenKind.DoubleSlashEqual:
                case TokenKind.PercentEqual:
                case TokenKind.DoubleStarEqual:
                    return DScriptSyntaxKind.Operator;
                default:
                    return DScriptSyntaxKind.Plain;
            }
        }
    }
}
