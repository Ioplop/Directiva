using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DSCompilation.DScript.Lexing
{
    /// <summary>Indentation-sensitive, invariant DScript tokenizer. No Unity APIs or I/O.</summary>
    public sealed class DScriptLexer
    {
        private static readonly Dictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind>(StringComparer.Ordinal)
        {
            ["def"] = TokenKind.Def, ["return"] = TokenKind.Return, ["if"] = TokenKind.If,
            ["elif"] = TokenKind.Elif, ["else"] = TokenKind.Else, ["while"] = TokenKind.While,
            ["for"] = TokenKind.For, ["in"] = TokenKind.In, ["break"] = TokenKind.Break,
            ["continue"] = TokenKind.Continue, ["pass"] = TokenKind.Pass, ["global"] = TokenKind.Global,
            ["shared"] = TokenKind.Shared,
            ["import"] = TokenKind.Import, ["from"] = TokenKind.From, ["as"] = TokenKind.As,
            ["and"] = TokenKind.And, ["or"] = TokenKind.Or, ["not"] = TokenKind.Not,
            ["True"] = TokenKind.True, ["False"] = TokenKind.False, ["None"] = TokenKind.None
        };

        private readonly string module;
        private readonly string source;
        private readonly DScriptFrontendOptions options;
        private readonly List<DScriptToken> tokens = new List<DScriptToken>();
        private readonly List<DScriptDiagnostic> diagnostics = new List<DScriptDiagnostic>();
        private readonly Stack<int> indents = new Stack<int>();
        private readonly Stack<char> brackets = new Stack<char>();

        public IReadOnlyList<DScriptDiagnostic> Diagnostics => diagnostics;
        public DScriptLexer(string module, string source, DScriptFrontendOptions options = null)
        {
            this.module = module ?? string.Empty;
            this.source = source ?? string.Empty;
            this.options = options ?? new DScriptFrontendOptions();
        }

        public IReadOnlyList<DScriptToken> Tokenize()
        {
            tokens.Clear(); diagnostics.Clear(); indents.Clear(); brackets.Clear();
            indents.Push(0);
            if (source.Length > options.MaxSourceLength)
            {
                Report("DS1000", "Source exceeds the maximum length.", new SourceSpan(0, 0, 1, 1));
                tokens.Add(new DScriptToken(TokenKind.End, "", new SourceSpan(0, 0, 1, 1)));
                return tokens;
            }
            int offset = 0, lineNumber = 1;
            while (offset < source.Length && diagnostics.Count < options.MaxDiagnostics)
            {
                int end = source.IndexOf('\n', offset);
                if (end < 0) end = source.Length;
                int lineEnd = end > offset && source[end - 1] == '\r' ? end - 1 : end;
                LexLine(offset, lineEnd, lineNumber);
                offset = end < source.Length ? end + 1 : source.Length;
                lineNumber++;
            }
            if (brackets.Count != 0)
                Report("DS1006", "Unclosed bracket or parenthesis.", new SourceSpan(source.Length, 0, lineNumber, 1));
            if (tokens.Count > 0 && tokens[tokens.Count - 1].Kind != TokenKind.Newline)
                Add(TokenKind.Newline, "", source.Length, 0, lineNumber, 1);
            while (indents.Count > 1)
            {
                indents.Pop();
                Add(TokenKind.Dedent, "", source.Length, 0, lineNumber, 1);
            }
            Add(TokenKind.End, "", source.Length, 0, lineNumber, 1);
            return tokens;
        }

        private void LexLine(int start, int end, int line)
        {
            int i = start;
            if (brackets.Count == 0)
            {
                while (i < end && (source[i] == ' ' || source[i] == '\t'))
                {
                    if (source[i] == '\t') Report("DS1001", "Tabs are not allowed for indentation; use spaces.", Span(i, 1, start, line));
                    i++;
                }
                if (i == end || source[i] == '#') return;
                int indentation = i - start;
                if (indentation > indents.Peek())
                {
                    if (indents.Count >= options.MaxNestingDepth)
                        Report("DS1002", "Maximum indentation depth exceeded.", Span(start, indentation, start, line));
                    else
                    {
                        indents.Push(indentation);
                        Add(TokenKind.Indent, "", start, indentation, line, 1);
                    }
                }
                else if (indentation < indents.Peek())
                {
                    while (indents.Count > 1 && indentation < indents.Peek())
                    {
                        indents.Pop();
                        Add(TokenKind.Dedent, "", start, 0, line, 1);
                    }
                    if (indentation != indents.Peek())
                        Report("DS1003", "Inconsistent dedent: no matching indentation level.", Span(start, indentation, start, line));
                }
            }
            bool emitted = false;
            while (i < end)
            {
                char c = source[i];
                if (c == ' ' || c == '\t') { i++; continue; }
                if (c == '#') break;
                int at = i;
                if (IsIdentifierStart(c))
                {
                    i++;
                    while (i < end && IsIdentifierPart(source[i])) i++;
                    string identifier = source.Substring(at, i - at);
                    if (!Keywords.TryGetValue(identifier, out TokenKind kind)) kind = TokenKind.Identifier;
                    Add(kind, identifier, at, i - at, line, at - start + 1);
                    emitted = true;
                    continue;
                }
                if (IsDigit(c))
                {
                    i++;
                    while (i < end && IsDigit(source[i])) i++;
                    TokenKind kind = TokenKind.Integer;
                    if (i + 1 < end && source[i] == '.' && IsDigit(source[i + 1]))
                    {
                        i += 2;
                        while (i < end && IsDigit(source[i])) i++;
                        kind = TokenKind.Decimal;
                    }
                    string literal = source.Substring(at, i - at);
                    // Validation belongs to the lexer, not to the runtime parser.
                    if (kind == TokenKind.Decimal)
                    {
                        string[] pieces = literal.Split('.');
                        if (pieces[1].Length > 4 || !decimal.TryParse(literal, NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out decimal parsed) || parsed > (decimal)long.MaxValue / 10000m)
                            Report("DS1004", "Decimal literal exceeds fixed-point precision or range (four fractional digits).", Span(at, i - at, start, line));
                    }
                    else if (!ulong.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        Report("DS1005", "Integer literal exceeds the supported Int64 magnitude.", Span(at, i - at, start, line));
                    Add(kind, literal, at, i - at, line, at - start + 1);
                    emitted = true;
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    i++;
                    var text = new StringBuilder();
                    bool closed = false;
                    while (i < end)
                    {
                        char next = source[i++];
                        if (next == c) { closed = true; break; }
                        if (next != '\\') { text.Append(next); continue; }
                        if (i >= end) break;
                        char escaped = source[i++];
                        switch (escaped)
                        {
                            case 'n': text.Append('\n'); break;
                            case 'r': text.Append('\r'); break;
                            case 't': text.Append('\t'); break;
                            case '\\': text.Append('\\'); break;
                            case '\'': text.Append('\''); break;
                            case '"': text.Append('"'); break;
                            case '0': text.Append('\0'); break;
                            case 'u':
                                if (i + 4 <= end && ushort.TryParse(source.Substring(i, 4),
                                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                                { text.Append((char)code); i += 4; }
                                else Report("DS1007", "Invalid Unicode escape; expected four hex digits.", Span(at, i - at, start, line));
                                break;
                            default: Report("DS1007", "Unsupported string escape: \\" + escaped, Span(i - 2, 2, start, line)); break;
                        }
                    }
                    if (!closed) Report("DS1008", "Unterminated string literal.", Span(at, i - at, start, line));
                    Add(TokenKind.String, text.ToString(), at, i - at, line, at - start + 1);
                    emitted = true;
                    continue;
                }
                TokenKind tokenKind;
                int width;
                if (Match(at, end, "**=", out width)) tokenKind = TokenKind.DoubleStarEqual;
                else if (Match(at, end, "//=", out width)) tokenKind = TokenKind.DoubleSlashEqual;
                else if (Match(at, end, "**", out width)) tokenKind = TokenKind.DoubleStar;
                else if (Match(at, end, "//", out width)) tokenKind = TokenKind.DoubleSlash;
                else if (Match(at, end, "==", out width)) tokenKind = TokenKind.EqualEqual;
                else if (Match(at, end, "!=", out width)) tokenKind = TokenKind.NotEqual;
                else if (Match(at, end, "<=", out width)) tokenKind = TokenKind.LessEqual;
                else if (Match(at, end, ">=", out width)) tokenKind = TokenKind.GreaterEqual;
                else if (Match(at, end, "+=", out width)) tokenKind = TokenKind.PlusEqual;
                else if (Match(at, end, "-=", out width)) tokenKind = TokenKind.MinusEqual;
                else if (Match(at, end, "*=", out width)) tokenKind = TokenKind.StarEqual;
                else if (Match(at, end, "/=", out width)) tokenKind = TokenKind.SlashEqual;
                else if (Match(at, end, "%=", out width)) tokenKind = TokenKind.PercentEqual;
                else
                {
                    width = 1;
                    switch (c)
                    {
                        case '(': tokenKind = TokenKind.LeftParen; Open('(', at, start, line); break;
                        case '[': tokenKind = TokenKind.LeftBracket; Open('[', at, start, line); break;
                        case '{': tokenKind = TokenKind.LeftBrace; Open('{', at, start, line); break;
                        case ')': tokenKind = TokenKind.RightParen; Close('(', at, start, line); break;
                        case ']': tokenKind = TokenKind.RightBracket; Close('[', at, start, line); break;
                        case '}': tokenKind = TokenKind.RightBrace; Close('{', at, start, line); break;
                        case '.': tokenKind = TokenKind.Dot; break;
                        case ',': tokenKind = TokenKind.Comma; break;
                        case ':': tokenKind = TokenKind.Colon; break;
                        case '+': tokenKind = TokenKind.Plus; break;
                        case '-': tokenKind = TokenKind.Minus; break;
                        case '*': tokenKind = TokenKind.Star; break;
                        case '/': tokenKind = TokenKind.Slash; break;
                        case '%': tokenKind = TokenKind.Percent; break;
                        case '=': tokenKind = TokenKind.Equals; break;
                        case '<': tokenKind = TokenKind.Less; break;
                        case '>': tokenKind = TokenKind.Greater; break;
                        default:
                            Report("DS1009", "Unexpected character '" + c + "'.", Span(at, 1, start, line));
                            i++;
                            continue;
                    }
                }
                Add(tokenKind, source.Substring(at, width), at, width, line, at - start + 1);
                i += width;
                emitted = true;
            }
            if (brackets.Count == 0 && emitted) Add(TokenKind.Newline, "", end, 0, line, end - start + 1);
        }

        private bool Match(int position, int end, string text, out int length)
        {
            length = text.Length;
            return position + length <= end && string.CompareOrdinal(source, position, text, 0, length) == 0;
        }
        private void Open(char kind, int at, int start, int line)
        {
            if (brackets.Count >= options.MaxNestingDepth)
                Report("DS1006", "Maximum bracket nesting depth exceeded.", Span(at, 1, start, line));
            else brackets.Push(kind);
        }
        private void Close(char expected, int at, int start, int line)
        {
            if (brackets.Count == 0 || brackets.Peek() != expected)
                Report("DS1006", "Unmatched or mismatched closing bracket.", Span(at, 1, start, line));
            else brackets.Pop();
        }
        private void Add(TokenKind kind, string text, int start, int length, int line, int column)
            => tokens.Add(new DScriptToken(kind, text, new SourceSpan(start, length, line, column)));
        private void Report(string code, string message, SourceSpan span)
        {
            if (diagnostics.Count < options.MaxDiagnostics)
                diagnostics.Add(new DScriptDiagnostic(module, code, message, span));
        }
        private static bool IsIdentifierStart(char c) => c == '_' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
        private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);
        private static bool IsDigit(char c) => c >= '0' && c <= '9';
        private static SourceSpan Span(int at, int length, int lineStart, int line)
            => new SourceSpan(at, length, line, at - lineStart + 1);
    }
}
