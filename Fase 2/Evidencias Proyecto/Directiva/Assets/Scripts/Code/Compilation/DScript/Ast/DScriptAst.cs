using System;
using System.Collections.Generic;

namespace DSCompilation.DScript.Ast
{
    public abstract class Node
    {
        public SourceSpan Span { get; }
        protected Node(SourceSpan span) { Span = span; }
    }

    public abstract class Statement : Node { protected Statement(SourceSpan span) : base(span) { } }
    public abstract class Expression : Node { protected Expression(SourceSpan span) : base(span) { } }

    public sealed class ModuleNode : Node
    {
        public string Name { get; }
        public IReadOnlyList<Statement> Statements { get; }
        public ModuleNode(string name, IReadOnlyList<Statement> statements, SourceSpan span) : base(span)
        { Name = name; Statements = statements; }
    }

    public sealed class ImportStatement : Statement
    {
        public string ModuleName { get; }
        public string Alias { get; }
        public ImportStatement(string module, string alias, SourceSpan span) : base(span)
        { ModuleName = module; Alias = alias; }
    }

    public sealed class ImportedName
    {
        public string Name { get; }
        public string Alias { get; }
        public SourceSpan Span { get; }
        public ImportedName(string name, string alias, SourceSpan span)
        { Name = name; Alias = alias; Span = span; }
    }

    public sealed class FromImportStatement : Statement
    {
        public string ModuleName { get; }
        public IReadOnlyList<ImportedName> Names { get; }
        public bool Wildcard { get; }
        public FromImportStatement(string module, IReadOnlyList<ImportedName> names, bool wildcard, SourceSpan span)
            : base(span) { ModuleName = module; Names = names; Wildcard = wildcard; }
    }

    public sealed class FunctionStatement : Statement
    {
        public string Name { get; }
        public IReadOnlyList<string> Parameters { get; }
        public IReadOnlyList<Statement> Body { get; }
        public FunctionStatement(string name, IReadOnlyList<string> parameters, IReadOnlyList<Statement> body, SourceSpan span)
            : base(span) { Name = name; Parameters = parameters; Body = body; }
    }

    /// <summary>
    /// Module-level declaration of a single shared storage slot. Its initializer, if supplied,
    /// is evaluated once by the future bootstrap runner, not during parsing or import.
    /// </summary>
    public sealed class SharedStatement : Statement
    {
        public string Name { get; }
        public SourceSpan NameSpan { get; }
        public Expression Initializer { get; }
        public SharedStatement(string name, SourceSpan nameSpan, Expression initializer, SourceSpan span)
            : base(span) { Name = name; NameSpan = nameSpan; Initializer = initializer; }
    }

    public sealed class AssignmentStatement : Statement
    {
        public Expression Target { get; }
        public Expression Value { get; }
        public string Operator { get; } // "=" or a supported augmented operator, e.g. "+=".
        public AssignmentStatement(Expression target, string operation, Expression value, SourceSpan span)
            : base(span) { Target = target; Operator = operation; Value = value; }
    }

    public sealed class ExpressionStatement : Statement
    {
        public Expression Value { get; }
        public ExpressionStatement(Expression value, SourceSpan span) : base(span) { Value = value; }
    }

    public sealed class ConditionalBranch
    {
        public Expression Condition { get; }
        public IReadOnlyList<Statement> Body { get; }
        public ConditionalBranch(Expression condition, IReadOnlyList<Statement> body)
        { Condition = condition; Body = body; }
    }

    public sealed class IfStatement : Statement
    {
        public IReadOnlyList<ConditionalBranch> Branches { get; }
        public IReadOnlyList<Statement> ElseBody { get; }
        public IfStatement(IReadOnlyList<ConditionalBranch> branches, IReadOnlyList<Statement> elseBody, SourceSpan span)
            : base(span) { Branches = branches; ElseBody = elseBody; }
    }

    public sealed class WhileStatement : Statement
    {
        public Expression Condition { get; }
        public IReadOnlyList<Statement> Body { get; }
        public WhileStatement(Expression condition, IReadOnlyList<Statement> body, SourceSpan span)
            : base(span) { Condition = condition; Body = body; }
    }

    public sealed class ForStatement : Statement
    {
        public string Variable { get; }
        public SourceSpan VariableSpan { get; }
        public Expression Iterable { get; }
        public IReadOnlyList<Statement> Body { get; }
        public ForStatement(string variable, SourceSpan variableSpan, Expression iterable,
            IReadOnlyList<Statement> body, SourceSpan span) : base(span)
        { Variable = variable; VariableSpan = variableSpan; Iterable = iterable; Body = body; }
    }

    public sealed class ReturnStatement : Statement
    {
        public Expression Value { get; } // null means return None
        public ReturnStatement(Expression value, SourceSpan span) : base(span) { Value = value; }
    }

    public sealed class FlowStatement : Statement
    {
        public string Keyword { get; } // break, continue, pass
        public FlowStatement(string keyword, SourceSpan span) : base(span) { Keyword = keyword; }
    }

    public sealed class NameExpression : Expression
    {
        public string Name { get; }
        public NameExpression(string name, SourceSpan span) : base(span) { Name = name; }
    }

    public enum LiteralKind { Integer, Decimal, String, Bool, None }
    public sealed class LiteralExpression : Expression
    {
        public LiteralKind Kind { get; }
        public string Text { get; } // String is decoded; numeric text is invariant.
        public LiteralExpression(LiteralKind kind, string text, SourceSpan span) : base(span)
        { Kind = kind; Text = text; }
    }

    public sealed class UnaryExpression : Expression
    {
        public string Operator { get; }
        public Expression Operand { get; }
        public UnaryExpression(string op, Expression operand, SourceSpan span) : base(span)
        { Operator = op; Operand = operand; }
    }

    public sealed class BinaryExpression : Expression
    {
        public string Operator { get; }
        public Expression Left { get; }
        public Expression Right { get; }
        public BinaryExpression(Expression left, string op, Expression right, SourceSpan span) : base(span)
        { Left = left; Operator = op; Right = right; }
    }

    public sealed class CallExpression : Expression
    {
        public Expression Callee { get; }
        public IReadOnlyList<Expression> Arguments { get; }
        public CallExpression(Expression callee, IReadOnlyList<Expression> arguments, SourceSpan span) : base(span)
        { Callee = callee; Arguments = arguments; }
    }

    public sealed class AttributeExpression : Expression
    {
        public Expression Receiver { get; }
        public string Attribute { get; }
        public AttributeExpression(Expression receiver, string attribute, SourceSpan span) : base(span)
        { Receiver = receiver; Attribute = attribute; }
    }

    public sealed class IndexExpression : Expression
    {
        public Expression Receiver { get; }
        public Expression Index { get; }
        public IndexExpression(Expression receiver, Expression index, SourceSpan span) : base(span)
        { Receiver = receiver; Index = index; }
    }

    public enum CollectionKind { List, Tuple, Set, Dictionary }
    public sealed class CollectionExpression : Expression
    {
        public CollectionKind Kind { get; }
        public IReadOnlyList<Expression> Items { get; }
        public IReadOnlyList<Expression> Keys { get; } // keys for dictionary only; otherwise empty
        public CollectionExpression(CollectionKind kind, IReadOnlyList<Expression> items,
            IReadOnlyList<Expression> keys, SourceSpan span) : base(span)
        { Kind = kind; Items = items; Keys = keys; }
    }
}
