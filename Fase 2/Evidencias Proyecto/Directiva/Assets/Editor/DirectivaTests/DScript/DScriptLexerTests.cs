using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Lexing;

namespace Directiva.EditorTests.DScript
{
    internal static class DScriptLexerTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> body)
                => DScriptTestHarness.Add(tests, "Lexer", id, description, body);

            var valid = new (string Name, string Code, TokenKind[] Kinds)[]
            {
                ("Empty", "", new[]{ TokenKind.End }),
                ("CommentOnly", "# hola\n\n", new[]{ TokenKind.End }),
                ("NamesAndKeywordCase", "if If true True False None:\n    pass\n",
                    new[]{ TokenKind.If, TokenKind.Identifier, TokenKind.Identifier, TokenKind.True, TokenKind.False,
                        TokenKind.None, TokenKind.Colon, TokenKind.Newline, TokenKind.Indent, TokenKind.Pass,
                        TokenKind.Newline, TokenKind.Dedent, TokenKind.End }),
                ("Operators", "a += b ** -2 // 3 != c <= d >= e == f\n",
                    new[]{ TokenKind.Identifier, TokenKind.PlusEqual, TokenKind.Identifier, TokenKind.DoubleStar,
                        TokenKind.Minus, TokenKind.Integer, TokenKind.DoubleSlash, TokenKind.Integer,
                        TokenKind.NotEqual, TokenKind.Identifier, TokenKind.LessEqual, TokenKind.Identifier,
                        TokenKind.GreaterEqual, TokenKind.Identifier, TokenKind.EqualEqual, TokenKind.Identifier,
                        TokenKind.Newline, TokenKind.End }),
                ("BracketContinuation", "x = [1,\n    2,\n    3]\n",
                    new[]{ TokenKind.Identifier, TokenKind.Equals, TokenKind.LeftBracket, TokenKind.Integer,
                        TokenKind.Comma, TokenKind.Integer, TokenKind.Comma, TokenKind.Integer,
                        TokenKind.RightBracket, TokenKind.Newline, TokenKind.End }),
                ("SharedTokens", "shared action = miner\n",
                    new[]{ TokenKind.Shared, TokenKind.Identifier, TokenKind.Equals, TokenKind.Identifier,
                        TokenKind.Newline, TokenKind.End }),
                ("LegacyGlobalKeyword", "global x\n",
                    new[]{ TokenKind.Global, TokenKind.Identifier, TokenKind.Newline, TokenKind.End }),
                ("CRLF", "def f():\r\n    pass\r\n",
                    new[]{ TokenKind.Def, TokenKind.Identifier, TokenKind.LeftParen, TokenKind.RightParen,
                        TokenKind.Colon, TokenKind.Newline, TokenKind.Indent, TokenKind.Pass, TokenKind.Newline,
                        TokenKind.Dedent, TokenKind.End }),
                ("NestedIndent", "if True:\n    if False:\n        pass\n    pass\npass\n",
                    new[]{ TokenKind.If, TokenKind.True, TokenKind.Colon, TokenKind.Newline,
                        TokenKind.Indent, TokenKind.If, TokenKind.False, TokenKind.Colon, TokenKind.Newline,
                        TokenKind.Indent, TokenKind.Pass, TokenKind.Newline, TokenKind.Dedent, TokenKind.Pass,
                        TokenKind.Newline, TokenKind.Dedent, TokenKind.Pass, TokenKind.Newline, TokenKind.End }),
                ("LiteralBrackets", "x = {1: [True, None], 2: (4,)}\n",
                    new[]{ TokenKind.Identifier, TokenKind.Equals, TokenKind.LeftBrace, TokenKind.Integer,
                        TokenKind.Colon, TokenKind.LeftBracket, TokenKind.True, TokenKind.Comma, TokenKind.None,
                        TokenKind.RightBracket, TokenKind.Comma, TokenKind.Integer, TokenKind.Colon,
                        TokenKind.LeftParen, TokenKind.Integer, TokenKind.Comma, TokenKind.RightParen,
                        TokenKind.RightBrace, TokenKind.Newline, TokenKind.End })
            };
            foreach (var fixture in valid)
                Add(fixture.Name, "Lexer produces stable tokens for " + fixture.Name, t =>
                {
                    var actual = DScriptTestHarness.Lex(t, fixture.Code, out var diagnostics);
                    t.Equal(0, diagnostics.Count, "lexical errors");
                    t.Equal(string.Join(",", fixture.Kinds), string.Join(",", actual.Select(x => x.Kind)), "tokens");
                });

            var invalid = new (string Name, string Code, string Diagnostic)[]
            {
                ("Tabs", "def f():\n\tpass\n", "DS1001"),
                ("InconsistentDedent", "if True:\n    pass\n  pass\n", "DS1003"),
                ("UnexpectedCharacter", "x = @\n", "DS1009"),
                ("BadEscape", "x = '\\q'\n", "DS1007"),
                ("UnterminatedString", "x = 'oops\n", "DS1008"),
                ("UnclosedParen", "foo(1\n", "DS1006"),
                ("MismatchedParen", "foo([1)\n", "DS1006"),
                ("DecimalPrecision", "x = 1.23456\n", "DS1004"),
                ("HugeInteger", "x = 9999999999999999999999\n", "DS1005")
            };
            foreach (var fixture in invalid)
                Add(fixture.Name, "Lexer rejects " + fixture.Name, t =>
                {
                    DScriptTestHarness.Lex(t, fixture.Code, out var errors);
                    t.Check(errors.Any(e => e.Code == fixture.Diagnostic), "Expected " + fixture.Diagnostic);
                });

            Add("StringEscapes", "String literals decode supported escapes and Unicode", t =>
            {
                var tokens = DScriptTestHarness.Lex(t, "x = 'A\\nB\\u263A\\\\'\n", out var errors);
                t.Equal(0, errors.Count, "errors");
                t.Equal("A\nB\u263A\\", tokens.Single(x => x.Kind == TokenKind.String).Text, "decoded string");
            });
            Add("SourcePositions", "Token positions preserve source offsets and lines", t =>
            {
                var tokens = DScriptTestHarness.Lex(t, "x = 1\ny = 20\n", out var errors);
                t.Equal(0, errors.Count, "errors");
                var twenty = tokens.Single(x => x.Text == "20");
                t.Equal(10, twenty.Span.Start, "start offset");
                t.Equal(2, twenty.Span.Line, "line");
                t.Equal(5, twenty.Span.Column, "column");
            });
            Add("SourceLimit", "Reject giant untrusted source before allocating tokens", t =>
            {
                DScriptTestHarness.Lex(t, new string('x', 40), out var errors,
                    new DScriptFrontendOptions(maxSourceLength: 20));
                t.Check(errors.Any(e => e.Code == "DS1000"), "source length guard");
            });
            Add("IndentLimit", "Guard indentation nesting", t =>
            {
                DScriptTestHarness.Lex(t, "if True:\n    if True:\n        pass\n", out var errors,
                    new DScriptFrontendOptions(maxNestingDepth: 2));
                t.Check(errors.Any(e => e.Code == "DS1002"), "indentation depth guard");
            });
        }
    }
}
