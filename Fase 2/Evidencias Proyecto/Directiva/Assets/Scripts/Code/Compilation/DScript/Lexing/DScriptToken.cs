namespace DSCompilation.DScript.Lexing
{
    public enum TokenKind
    {
        End, Newline, Indent, Dedent, Identifier, Integer, Decimal, String,
        Def, Return, If, Elif, Else, While, For, In, Break, Continue, Pass, Global, Shared,
        Import, From, As, And, Or, Not, True, False, None,
        LeftParen, RightParen, LeftBracket, RightBracket, LeftBrace, RightBrace,
        Dot, Comma, Colon, Plus, Minus, Star, Slash, DoubleSlash, Percent, DoubleStar,
        Equals, EqualEqual, NotEqual, Less, LessEqual, Greater, GreaterEqual,
        PlusEqual, MinusEqual, StarEqual, SlashEqual, DoubleSlashEqual, PercentEqual, DoubleStarEqual
    }

    public readonly struct DScriptToken
    {
        public TokenKind Kind { get; }
        public string Text { get; }
        public SourceSpan Span { get; }

        public DScriptToken(TokenKind kind, string text, SourceSpan span)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Span = span;
        }

        public override string ToString() => Kind + " '" + Text + "' @" + Span;
    }
}
