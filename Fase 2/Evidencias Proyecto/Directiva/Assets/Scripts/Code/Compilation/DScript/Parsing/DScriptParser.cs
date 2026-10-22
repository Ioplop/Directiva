using System;
using System.Collections.Generic;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;

namespace DSCompilation.DScript.Parsing
{
    /// <summary>Recursive-descent frontend parser. Produces syntax trees, never DIL or VM values.</summary>
    public sealed class DScriptParser
    {
        private sealed class BadSyntax : Exception { }
        private readonly string module;
        private readonly IReadOnlyList<DScriptToken> tokens;
        private readonly DScriptFrontendOptions options;
        private readonly List<DScriptDiagnostic> diagnostics = new List<DScriptDiagnostic>();
        private int position;
        private int depth;
        private int expressionDepth;

        public IReadOnlyList<DScriptDiagnostic> Diagnostics => diagnostics;
        public DScriptParser(string module, IReadOnlyList<DScriptToken> tokens,
            DScriptFrontendOptions options = null)
        {
            this.module = module ?? string.Empty;
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            this.options = options ?? new DScriptFrontendOptions();
        }

        public ModuleNode Parse()
        {
            position = 0; depth = 0; expressionDepth = 0; diagnostics.Clear();
            var statements = ReadStatements(false);
            SourceSpan span = tokens.Count != 0 ? tokens[0].Span : new SourceSpan(0, 0, 1, 1);
            return new ModuleNode(module, statements, span);
        }

        private IReadOnlyList<Statement> ReadStatements(bool block)
        {
            var result = new List<Statement>();
            while (!Is(TokenKind.End) && !(block && Is(TokenKind.Dedent)) && diagnostics.Count < options.MaxDiagnostics)
            {
                if (Match(TokenKind.Newline)) continue;
                if (Is(TokenKind.Indent) || Is(TokenKind.Dedent))
                {
                    Error("DS2001", "Unexpected indentation level.", Current.Span);
                    Advance();
                    continue;
                }
                int before = position;
                try
                {
                    Statement item = ReadStatement();
                    if (item != null) result.Add(item);
                }
                catch (BadSyntax)
                {
                    // Skip this logical line, but preserve the containing suite for further diagnostics.
                    while (!Is(TokenKind.End) && !Is(TokenKind.Newline) && !Is(TokenKind.Dedent)) Advance();
                    Match(TokenKind.Newline);
                }
                if (position == before) Advance();
            }
            if (block) Expect(TokenKind.Dedent, "Expected the end of an indented block.");
            return result;
        }

        private Statement ReadStatement()
        {
            // Only declarations are admitted outside functions. Shared declarations are
            // intentionally forbidden inside any suite (not just function bodies).
            if (depth == 0 && !(Is(TokenKind.Def) || Is(TokenKind.Import) ||
                Is(TokenKind.From) || Is(TokenKind.Shared) || Is(TokenKind.Global)))
            {
                Error("DS2013", "Module scope allows only imports, shared declarations and functions.", Current.Span);
                throw new BadSyntax();
            }
            switch (Current.Kind)
            {
                case TokenKind.Def: return ReadFunction();
                case TokenKind.Import: return ReadImport();
                case TokenKind.From: return ReadFromImport();
                case TokenKind.Shared: return ReadShared();
                case TokenKind.Global:
                    Error("DS2015", "'global' is not a DScript declaration; use module-level 'shared'.", Current.Span);
                    throw new BadSyntax();
                case TokenKind.If: return ReadIf();
                case TokenKind.While: return ReadWhile();
                case TokenKind.For: return ReadFor();
                case TokenKind.Return:
                {
                    var beginning = Advance();
                    Expression value = Is(TokenKind.Newline) ? null : ReadExpression();
                    var end = Expect(TokenKind.Newline, "Expected end of return statement.");
                    return new ReturnStatement(value, SourceSpan.Through(beginning.Span, end.Span));
                }
                case TokenKind.Pass: case TokenKind.Break: case TokenKind.Continue:
                {
                    var beginning = Advance();
                    var end = Expect(TokenKind.Newline, "Expected end of statement.");
                    return new FlowStatement(beginning.Text, SourceSpan.Through(beginning.Span, end.Span));
                }
                default: return ReadSimple();
            }
        }

        private Statement ReadFunction()
        {
            var begin = Expect(TokenKind.Def, "Expected def.");
            var name = Expect(TokenKind.Identifier, "Expected function name.");
            Expect(TokenKind.LeftParen, "Expected '(' after function name.");
            var parameters = new List<string>();
            if (!Is(TokenKind.RightParen))
            {
                do
                {
                    parameters.Add(Expect(TokenKind.Identifier, "Expected parameter name.").Text);
                } while (Match(TokenKind.Comma) && !Is(TokenKind.RightParen));
            }
            Expect(TokenKind.RightParen, "Expected ')' after parameters.");
            var body = ReadSuite();
            return new FunctionStatement(name.Text, parameters, body, SourceSpan.Through(begin.Span, Previous.Span));
        }

        private Statement ReadImport()
        {
            var begin = Advance();
            string name = ReadQualifiedName();
            string alias = Match(TokenKind.As) ? Expect(TokenKind.Identifier, "Expected import alias.").Text : null;
            var end = Expect(TokenKind.Newline, "Expected end of import statement (one module per import).");
            return new ImportStatement(name, alias, SourceSpan.Through(begin.Span, end.Span));
        }

        private Statement ReadFromImport()
        {
            var begin = Advance();
            string name = ReadQualifiedName();
            Expect(TokenKind.Import, "Expected 'import' in from-import statement.");
            var imports = new List<ImportedName>();
            bool wildcard = Match(TokenKind.Star);
            if (!wildcard)
            {
                do
                {
                    var identifier = Expect(TokenKind.Identifier, "Expected imported symbol name.");
                    string alias = Match(TokenKind.As) ? Expect(TokenKind.Identifier, "Expected symbol alias.").Text : null;
                    imports.Add(new ImportedName(identifier.Text, alias, identifier.Span));
                    if (Is(TokenKind.Newline)) break;
                } while (Match(TokenKind.Comma));
            }
            var end = Expect(TokenKind.Newline, "Expected end of from-import statement.");
            return new FromImportStatement(name, imports, wildcard, SourceSpan.Through(begin.Span, end.Span));
        }

        private string ReadQualifiedName()
        {
            string name = Expect(TokenKind.Identifier, "Expected module name.").Text;
            while (Match(TokenKind.Dot)) name += "." + Expect(TokenKind.Identifier, "Expected module path segment.").Text;
            return name;
        }

        private Statement ReadShared()
        {
            if (depth != 0)
            {
                Error("DS2014", "'shared' declarations are only permitted at module scope.", Current.Span);
                throw new BadSyntax();
            }
            var begin = Advance();
            var name = Expect(TokenKind.Identifier, "Expected shared variable name.");
            Expression initializer = Match(TokenKind.Equals) ? ReadExpression() : null;
            var end = Expect(TokenKind.Newline, "Expected end of shared declaration.");
            return new SharedStatement(name.Text, name.Span, initializer,
                SourceSpan.Through(begin.Span, end.Span));
        }

        private Statement ReadIf()
        {
            var start = Expect(TokenKind.If, "Expected if.");
            var branches = new List<ConditionalBranch>();
            branches.Add(new ConditionalBranch(ReadExpression(), ReadSuite()));
            while (Match(TokenKind.Elif)) branches.Add(new ConditionalBranch(ReadExpression(), ReadSuite()));
            IReadOnlyList<Statement> alternative = Array.Empty<Statement>();
            if (Match(TokenKind.Else)) alternative = ReadSuite();
            return new IfStatement(branches, alternative, SourceSpan.Through(start.Span, Previous.Span));
        }

        private Statement ReadWhile()
        {
            var start = Advance();
            var condition = ReadExpression();
            var body = ReadSuite();
            return new WhileStatement(condition, body, SourceSpan.Through(start.Span, Previous.Span));
        }

        private Statement ReadFor()
        {
            var start = Advance();
            var variable = Expect(TokenKind.Identifier, "For loop target must be a single variable.");
            Expect(TokenKind.In, "Expected 'in' in for loop.");
            var iterable = ReadExpression();
            var body = ReadSuite();
            return new ForStatement(variable.Text, variable.Span, iterable, body,
                SourceSpan.Through(start.Span, Previous.Span));
        }

        private IReadOnlyList<Statement> ReadSuite()
        {
            Expect(TokenKind.Colon, "Expected ':' before indented block.");
            Expect(TokenKind.Newline, "Expected newline after ':' (inline suites are not supported).");
            Expect(TokenKind.Indent, "Expected an indented block.");
            if (++depth > options.MaxNestingDepth)
            {
                Error("DS2002", "Maximum syntax nesting depth exceeded.", Current.Span);
                throw new BadSyntax();
            }
            var body = ReadStatements(true);
            depth--;
            if (body.Count == 0)
                Error("DS2003", "An indented suite must contain at least one statement.", Previous.Span);
            return body;
        }

        private Statement ReadSimple()
        {
            Expression lhs = ReadExpression();
            TokenKind kind = Current.Kind;
            if (kind == TokenKind.Equals || kind == TokenKind.PlusEqual || kind == TokenKind.MinusEqual ||
                kind == TokenKind.StarEqual || kind == TokenKind.SlashEqual || kind == TokenKind.DoubleSlashEqual ||
                kind == TokenKind.PercentEqual || kind == TokenKind.DoubleStarEqual)
            {
                var op = Advance();
                if (!(lhs is NameExpression || lhs is IndexExpression || lhs is AttributeExpression))
                    Error("DS2004", "Assignment target must be a variable, index or attribute.", lhs.Span);
                Expression rhs = ReadExpression();
                var end = Expect(TokenKind.Newline, "Expected end of assignment.");
                return new AssignmentStatement(lhs, op.Text, rhs, SourceSpan.Through(lhs.Span, end.Span));
            }
            Expect(TokenKind.Newline, "Expected end of expression statement.");
            return new ExpressionStatement(lhs, lhs.Span);
        }

        private Expression ReadExpression()
        {
            if (++expressionDepth > options.MaxNestingDepth)
            {
                expressionDepth--;
                Error("DS2002", "Maximum expression nesting depth exceeded.", Current.Span);
                throw new BadSyntax();
            }
            try { return ReadOr(); }
            finally { expressionDepth--; }
        }
        private Expression ReadOr()
        {
            Expression left = ReadAnd();
            while (Is(TokenKind.Or)) { var op = Advance(); var right = ReadAnd(); left = Join(left, op, right); }
            return left;
        }
        private Expression ReadAnd()
        {
            Expression left = ReadNot();
            while (Is(TokenKind.And)) { var op = Advance(); var right = ReadNot(); left = Join(left, op, right); }
            return left;
        }
        private Expression ReadNot()
        {
            var operators = new List<DScriptToken>();
            while (Is(TokenKind.Not))
            {
                if (operators.Count >= options.MaxNestingDepth)
                {
                    Error("DS2002", "Maximum unary nesting depth exceeded.", Current.Span);
                    throw new BadSyntax();
                }
                operators.Add(Advance());
            }
            Expression value = ReadComparison();
            for (int i = operators.Count - 1; i >= 0; i--)
                value = new UnaryExpression(operators[i].Text, value,
                    SourceSpan.Through(operators[i].Span, value.Span));
            return value;
        }
        private Expression ReadComparison()
        {
            Expression left = ReadAdditive();
            if (IsComparison(Current.Kind) || Is(TokenKind.Not) && Peek(1).Kind == TokenKind.In)
            {
                var op = Advance();
                string symbol = op.Text;
                if (op.Kind == TokenKind.Not) { Advance(); symbol = "not in"; }
                Expression right = ReadAdditive();
                left = new BinaryExpression(left, symbol, right, SourceSpan.Through(left.Span, right.Span));
                if (IsComparison(Current.Kind) || Is(TokenKind.Not) && Peek(1).Kind == TokenKind.In)
                {
                    Error("DS2012", "Chained comparisons are not supported yet; combine with 'and'.", Current.Span);
                    throw new BadSyntax();
                }
            }
            return left;
        }
        private static bool IsComparison(TokenKind kind) => kind == TokenKind.EqualEqual ||
            kind == TokenKind.NotEqual || kind == TokenKind.Less || kind == TokenKind.LessEqual ||
            kind == TokenKind.Greater || kind == TokenKind.GreaterEqual || kind == TokenKind.In;
        private Expression ReadAdditive()
        {
            Expression left = ReadMultiplicative();
            while (Is(TokenKind.Plus) || Is(TokenKind.Minus))
            { var op = Advance(); var right = ReadMultiplicative(); left = Join(left, op, right); }
            return left;
        }
        private Expression ReadMultiplicative()
        {
            Expression left = ReadUnary();
            while (Is(TokenKind.Star) || Is(TokenKind.Slash) || Is(TokenKind.DoubleSlash) || Is(TokenKind.Percent))
            { var op = Advance(); var right = ReadUnary(); left = Join(left, op, right); }
            return left;
        }
        private Expression ReadUnary()
        {
            var operators = new List<DScriptToken>();
            while (Is(TokenKind.Plus) || Is(TokenKind.Minus))
            {
                if (operators.Count >= options.MaxNestingDepth)
                {
                    Error("DS2002", "Maximum unary nesting depth exceeded.", Current.Span);
                    throw new BadSyntax();
                }
                operators.Add(Advance());
            }
            Expression value = ReadPower();
            for (int i = operators.Count - 1; i >= 0; i--)
                value = new UnaryExpression(operators[i].Text, value,
                    SourceSpan.Through(operators[i].Span, value.Span));
            return value;
        }
        private Expression ReadPower()
        {
            if (++expressionDepth > options.MaxNestingDepth)
            {
                expressionDepth--;
                Error("DS2002", "Maximum power-expression depth exceeded.", Current.Span);
                throw new BadSyntax();
            }
            try
            {
                Expression left = ReadPostfix();
                if (Is(TokenKind.DoubleStar))
                { var op = Advance(); var right = ReadUnary(); left = Join(left, op, right); }
                return left;
            }
            finally { expressionDepth--; }
        }
        private Expression ReadPostfix()
        {
            Expression expression = ReadPrimary();
            while (true)
            {
                if (Match(TokenKind.Dot))
                {
                    var attr = Expect(TokenKind.Identifier, "Expected attribute name.");
                    expression = new AttributeExpression(expression, attr.Text,
                        SourceSpan.Through(expression.Span, attr.Span));
                }
                else if (Match(TokenKind.LeftParen))
                {
                    var args = new List<Expression>();
                    if (!Is(TokenKind.RightParen))
                    {
                        do { args.Add(ReadExpression()); }
                        while (Match(TokenKind.Comma) && !Is(TokenKind.RightParen));
                    }
                    var end = Expect(TokenKind.RightParen, "Expected ')' after call arguments.");
                    expression = new CallExpression(expression, args, SourceSpan.Through(expression.Span, end.Span));
                }
                else if (Match(TokenKind.LeftBracket))
                {
                    var index = ReadExpression();
                    var end = Expect(TokenKind.RightBracket, "Expected ']' after index.");
                    expression = new IndexExpression(expression, index, SourceSpan.Through(expression.Span, end.Span));
                }
                else break;
            }
            return expression;
        }
        private Expression ReadPrimary()
        {
            var t = Advance();
            switch (t.Kind)
            {
                case TokenKind.Identifier: return new NameExpression(t.Text, t.Span);
                case TokenKind.Integer: return new LiteralExpression(LiteralKind.Integer, t.Text, t.Span);
                case TokenKind.Decimal: return new LiteralExpression(LiteralKind.Decimal, t.Text, t.Span);
                case TokenKind.String: return new LiteralExpression(LiteralKind.String, t.Text, t.Span);
                case TokenKind.True: case TokenKind.False: return new LiteralExpression(LiteralKind.Bool, t.Text, t.Span);
                case TokenKind.None: return new LiteralExpression(LiteralKind.None, t.Text, t.Span);
                case TokenKind.LeftParen: return ReadParentheses(t);
                case TokenKind.LeftBracket: return ReadList(t);
                case TokenKind.LeftBrace: return ReadBraces(t);
                default:
                    Error("DS2005", "Expected expression, found " + t.Kind + ".", t.Span);
                    throw new BadSyntax();
            }
        }
        private Expression ReadParentheses(DScriptToken start)
        {
            var entries = new List<Expression>();
            if (!Is(TokenKind.RightParen)) entries.Add(ReadExpression());
            bool tuple = Match(TokenKind.Comma);
            if (tuple)
            {
                while (!Is(TokenKind.RightParen) && !Is(TokenKind.End))
                {
                    entries.Add(ReadExpression());
                    if (!Match(TokenKind.Comma)) break;
                }
            }
            var end = Expect(TokenKind.RightParen, "Expected ')' after expression.");
            if (!tuple && entries.Count == 1) return entries[0];
            return new CollectionExpression(CollectionKind.Tuple, entries, Array.Empty<Expression>(),
                SourceSpan.Through(start.Span, end.Span));
        }
        private Expression ReadList(DScriptToken start)
        {
            var entries = ReadElements(TokenKind.RightBracket);
            var end = Expect(TokenKind.RightBracket, "Expected ']' after list literal.");
            return new CollectionExpression(CollectionKind.List, entries, Array.Empty<Expression>(),
                SourceSpan.Through(start.Span, end.Span));
        }
        private Expression ReadBraces(DScriptToken start)
        {
            var values = new List<Expression>();
            var keys = new List<Expression>();
            CollectionKind kind = CollectionKind.Dictionary; // {} is an empty dict.
            if (!Is(TokenKind.RightBrace))
            {
                Expression first = ReadExpression();
                if (Match(TokenKind.Colon))
                {
                    keys.Add(first);
                    values.Add(ReadExpression());
                    while (Match(TokenKind.Comma) && !Is(TokenKind.RightBrace))
                    {
                        keys.Add(ReadExpression());
                        Expect(TokenKind.Colon, "Expected ':' between dictionary key and value.");
                        values.Add(ReadExpression());
                    }
                }
                else
                {
                    kind = CollectionKind.Set;
                    values.Add(first);
                    while (Match(TokenKind.Comma) && !Is(TokenKind.RightBrace)) values.Add(ReadExpression());
                }
            }
            var end = Expect(TokenKind.RightBrace, "Expected '}' after collection literal.");
            return new CollectionExpression(kind, values, keys, SourceSpan.Through(start.Span, end.Span));
        }
        private List<Expression> ReadElements(TokenKind end)
        {
            var entries = new List<Expression>();
            if (!Is(end))
            {
                do { entries.Add(ReadExpression()); }
                while (Match(TokenKind.Comma) && !Is(end));
            }
            return entries;
        }
        private static BinaryExpression Join(Expression left, DScriptToken op, Expression right)
            => new BinaryExpression(left, op.Text, right, SourceSpan.Through(left.Span, right.Span));

        private DScriptToken Current => Peek(0);
        private DScriptToken Previous => position > 0 ? tokens[position - 1] : Current;
        private DScriptToken Peek(int delta)
        {
            int index = position + delta;
            return tokens[Math.Min(index, tokens.Count - 1)];
        }
        private bool Is(TokenKind kind) => Current.Kind == kind;
        private bool Match(TokenKind kind) { if (!Is(kind)) return false; Advance(); return true; }
        private DScriptToken Advance()
        {
            var current = Current;
            if (position < tokens.Count - 1) position++;
            return current;
        }
        private DScriptToken Expect(TokenKind kind, string message)
        {
            if (Is(kind)) return Advance();
            Error("DS2000", message, Current.Span);
            throw new BadSyntax();
        }
        private void Error(string code, string message, SourceSpan span)
        {
            if (diagnostics.Count < options.MaxDiagnostics)
                diagnostics.Add(new DScriptDiagnostic(module, code, message, span));
        }
    }
}
