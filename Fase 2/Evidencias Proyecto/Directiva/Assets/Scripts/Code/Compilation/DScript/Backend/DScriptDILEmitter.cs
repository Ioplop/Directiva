using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Semantics;

namespace DSCompilation.DScript.Backend
{
    /// <summary>
    /// One-function DIL emitter. Every executable instruction carries its original
    /// DScript location; jump labels land BEFORE those explicit DEBUG instructions.
    /// This emitter does not do name resolution or infer runtime DataTypes.
    /// </summary>
    internal sealed class DScriptDILEmitter
    {
        private sealed class LoopLabels
        {
            internal string Continue;
            internal string Break;
            internal LoopLabels(string next, string end) { Continue = next; Break = end; }
        }

        private static readonly Dictionary<string, string> BinaryInstructions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["+"] = "ADD", ["-"] = "SUBTRACT", ["*"] = "MULTIPLY",
                ["/"] = "DIVIDE", ["//"] = "FLOOR_DIVIDE", ["%"] = "MODULO",
                ["**"] = "POWER", ["=="] = "VALUE_EQUALS", ["!="] = "VALUE_EQUALS",
                [">"] = "GREATER_THAN", [">="] = "GREATER_OR_EQUAL",
                ["<"] = "LESS_THAN", ["<="] = "LESS_OR_EQUAL",
                ["in"] = "CONTAINS", ["not in"] = "CONTAINS"
            };

        private readonly DScriptAnalysis analysis;
        private readonly string module;
        private readonly List<string> body = new List<string>();
        private readonly Stack<LoopLabels> loops = new Stack<LoopLabels>();
        private int nextLocal;
        private int labelCounter;
        private readonly int maxLocals;

        internal DScriptDILEmitter(DScriptAnalysis analysis, string module,
            int initialLocals, int maxLocals)
        {
            this.analysis = analysis;
            this.module = module;
            nextLocal = initialLocals;
            this.maxLocals = maxLocals;
            if (nextLocal > maxLocals)
                throw Failure(new SourceSpan(0, 0, 1, 1), "Function needs too many local slots.");
        }

        internal string EmitFunction(FunctionStatement function, DScriptFunctionSymbols symbols)
        {
            foreach (Statement statement in function.Body) EmitStatement(statement);
            Op("PUSH NONE", function.Span);
            Op("RETURN", function.Span);
            return FunctionText(function.Name, function.Parameters.Count);
        }

        internal string EmitInitializer(string functionName, DScriptSharedInitializer initializer)
        {
            EmitExpression(initializer.Expression);
            Store(initializer.Symbol, initializer.Span);
            Op("PUSH NONE", initializer.Span);
            Op("RETURN", initializer.Span);
            return FunctionText(functionName, 0);
        }

        private string FunctionText(string name, int parameters)
        {
            var text = new StringBuilder();
            text.Append("# FUNCTION : ").Append(name).Append('\n');
            text.Append("# PARAMS : ").Append(parameters.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("# LOCALS : ").Append(nextLocal.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string line in body) text.Append(line).Append('\n');
            text.Append("# END_FUNCTION\n");
            return text.ToString();
        }

        private void Op(string command, SourceSpan span)
        {
            // All source offsets are UTF-16 indices from the original source text.
            // The VM uses zero-based line numbers; frontend SourceSpan is one-based.
            body.Add("DEBUG_FILE " + Quote(module.Replace('.', '/') + ".dscript"));
            body.Add("DEBUG_LINE " + (span.Line - 1).ToString(CultureInfo.InvariantCulture));
            body.Add("DEBUG_SPAN " + span.Start.ToString(CultureInfo.InvariantCulture) +
                " " + span.End.ToString(CultureInfo.InvariantCulture));
            body.Add(command);
        }

        private string Label() => "__ds_label_" + (labelCounter++).ToString(CultureInfo.InvariantCulture);
        private void Mark(string label) => body.Add(label + ":");
        private int Temp(SourceSpan span)
        {
            if (nextLocal >= maxLocals) throw Failure(span, "Too many temporary local slots.");
            return nextLocal++;
        }

        private void Load(DScriptSymbol symbol, SourceSpan span)
        {
            if (symbol.Kind == SymbolKind.Global)
                Op("LOAD_GLOBAL " + symbol.Slot, span);
            else if (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter)
                Op("LOAD_LOCAL " + symbol.Slot, span);
            else if (symbol.Kind == SymbolKind.Function)
                Op("PUSH_FUNCTION " + symbol.QualifiedName, span);
            else if (symbol.Kind == SymbolKind.NativeFunction || symbol.Kind == SymbolKind.NativeConstant)
                Op("PUSH_NATIVE " + symbol.QualifiedName, span);
            else
                throw Failure(span, "Module namespaces cannot be used as values.");
        }

        private void Store(DScriptSymbol symbol, SourceSpan span)
        {
            if (symbol.Kind == SymbolKind.Global)
                Op("STORE_GLOBAL " + symbol.Slot, span);
            else if (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter)
                Op("STORE_LOCAL " + symbol.Slot, span);
            else throw Failure(span, "Assignment to an immutable symbol.");
        }

        private DScriptSymbol Bound(Expression expression)
        {
            if (!analysis.ExpressionSymbols.TryGetValue(expression, out DScriptSymbol symbol))
                throw Failure(expression.Span, "Missing frontend binding for expression.");
            return symbol;
        }

        private void EmitStatement(Statement statement)
        {
            if (statement is AssignmentStatement assignment)
            {
                Assign(assignment);
            }
            else if (statement is ExpressionStatement expression)
            {
                EmitExpression(expression.Value);
                Op("POP", statement.Span);
            }
            else if (statement is ReturnStatement ret)
            {
                if (ret.Value == null) Op("PUSH NONE", ret.Span);
                else EmitExpression(ret.Value);
                Op("RETURN", ret.Span);
            }
            else if (statement is FlowStatement flow)
            {
                if (flow.Keyword == "pass") Op("PASS", flow.Span);
                else if (loops.Count == 0) throw Failure(flow.Span, "Flow statement outside loop.");
                else if (flow.Keyword == "break") Op("JUMP " + loops.Peek().Break, flow.Span);
                else if (flow.Keyword == "continue") Op("JUMP " + loops.Peek().Continue, flow.Span);
                else throw Failure(flow.Span, "Unknown flow statement.");
            }
            else if (statement is IfStatement branch)
            {
                string end = Label();
                foreach (ConditionalBranch alternative in branch.Branches)
                {
                    string otherwise = Label();
                    EmitExpression(alternative.Condition);
                    Op("IS_TRUTHY", alternative.Condition.Span);
                    Op("JUMP_IF_FALSE " + otherwise, alternative.Condition.Span);
                    foreach (Statement nested in alternative.Body) EmitStatement(nested);
                    Op("JUMP " + end, statement.Span);
                    Mark(otherwise);
                }
                foreach (Statement nested in branch.ElseBody) EmitStatement(nested);
                Mark(end);
                // Labels must point at a real instruction, even at the end of a function.
                Op("PASS", statement.Span);
            }
            else if (statement is WhileStatement loop)
            {
                string start = Label();
                string end = Label();
                Mark(start);
                EmitExpression(loop.Condition);
                Op("IS_TRUTHY", loop.Condition.Span);
                Op("JUMP_IF_FALSE " + end, loop.Condition.Span);
                loops.Push(new LoopLabels(start, end));
                foreach (Statement nested in loop.Body) EmitStatement(nested);
                loops.Pop();
                Op("JUMP " + start, loop.Span);
                Mark(end);
                Op("PASS", loop.Span);
            }
            else if (statement is ForStatement iterate)
            {
                if (!analysis.ForTargetSymbols.TryGetValue(iterate, out DScriptSymbol symbol))
                    throw Failure(iterate.VariableSpan, "Missing for-target symbol.");
                string next = Label();
                string cleanup = Label();
                string exhausted = Label();
                EmitExpression(iterate.Iterable);
                Op("ITER_INIT", iterate.Iterable.Span);
                Mark(next);
                Op("ITER_NEXT " + exhausted, iterate.Span);
                Store(symbol, iterate.VariableSpan);
                loops.Push(new LoopLabels(next, cleanup));
                foreach (Statement nested in iterate.Body) EmitStatement(nested);
                loops.Pop();
                Op("JUMP " + next, iterate.Span);
                Mark(cleanup);
                Op("POP", iterate.Span); // break must discard the active iterator.
                Mark(exhausted); // ITER_NEXT already discarded it on natural exhaustion.
                Op("PASS", iterate.Span);
            }
            else throw Failure(statement.Span, "Unsupported statement in backend.");
        }

        private void Assign(AssignmentStatement assignment)
        {
            Expression target = assignment.Target;
            bool augmented = assignment.Operator != "=";
            string operation = null;
            if (augmented)
            {
                string symbol = assignment.Operator.Substring(0, assignment.Operator.Length - 1);
                if (!BinaryInstructions.TryGetValue(symbol, out operation) ||
                    symbol == "in" || symbol == "not in" || symbol == "!=")
                    throw Failure(assignment.Span, "Unsupported augmented assignment operator.");
            }
            if (target is NameExpression || target is AttributeExpression &&
                analysis.ExpressionSymbols.ContainsKey(target))
            {
                DScriptSymbol symbol = Bound(target);
                if (augmented) Load(symbol, target.Span);
                EmitExpression(assignment.Value);
                if (augmented) Op(operation, assignment.Span);
                Store(symbol, target.Span);
                return;
            }
            if (target is AttributeExpression attribute)
            {
                // Regular assignment evaluates RHS before the target; augmented assignment
                // evaluates the receiver/getter only once before evaluating the RHS.
                int receiver = Temp(target.Span);
                int value = Temp(target.Span);
                if (augmented)
                {
                    EmitExpression(attribute.Receiver);
                    Op("STORE_LOCAL " + receiver, target.Span);
                    Op("LOAD_LOCAL " + receiver, target.Span);
                    Op("GET_PROPERTY " + attribute.Attribute, target.Span);
                    EmitExpression(assignment.Value);
                    Op(operation, assignment.Span);
                    Op("STORE_LOCAL " + value, target.Span);
                }
                else
                {
                    EmitExpression(assignment.Value);
                    Op("STORE_LOCAL " + value, target.Span);
                    EmitExpression(attribute.Receiver);
                    Op("STORE_LOCAL " + receiver, target.Span);
                }
                Op("LOAD_LOCAL " + receiver, target.Span);
                Op("LOAD_LOCAL " + value, target.Span);
                Op("SET_PROPERTY " + attribute.Attribute, target.Span);
                return;
            }
            if (target is IndexExpression index)
            {
                int receiver = Temp(target.Span);
                int key = Temp(target.Span);
                int value = Temp(target.Span);
                if (augmented)
                {
                    EmitExpression(index.Receiver);
                    Op("STORE_LOCAL " + receiver, target.Span);
                    EmitExpression(index.Index);
                    Op("STORE_LOCAL " + key, target.Span);
                    Op("LOAD_LOCAL " + receiver, target.Span);
                    Op("LOAD_LOCAL " + key, target.Span);
                    Op("GET", target.Span);
                    EmitExpression(assignment.Value);
                    Op(operation, assignment.Span);
                    Op("STORE_LOCAL " + value, target.Span);
                }
                else
                {
                    EmitExpression(assignment.Value);
                    Op("STORE_LOCAL " + value, target.Span);
                    EmitExpression(index.Receiver);
                    Op("STORE_LOCAL " + receiver, target.Span);
                    EmitExpression(index.Index);
                    Op("STORE_LOCAL " + key, target.Span);
                }
                Op("LOAD_LOCAL " + receiver, target.Span);
                Op("LOAD_LOCAL " + key, target.Span);
                Op("LOAD_LOCAL " + value, target.Span);
                Op("SET", target.Span);
                Op("POP", target.Span); // The native indexed SET returns None.
                return;
            }
            throw Failure(assignment.Span, "Invalid assignment target.");
        }

        private void EmitExpression(Expression expression)
        {
            if (expression is LiteralExpression literal)
            {
                switch (literal.Kind)
                {
                    case LiteralKind.Integer: Op("PUSH INT " + literal.Text, expression.Span); return;
                    case LiteralKind.Decimal: Op("PUSH DECIMAL " + literal.Text, expression.Span); return;
                    case LiteralKind.String: Op("PUSH STRING " + Quote(literal.Text), expression.Span); return;
                    case LiteralKind.Bool: Op("PUSH BOOL " + (literal.Text == "True" ? "TRUE" : "FALSE"), expression.Span); return;
                    case LiteralKind.None: Op("PUSH NONE", expression.Span); return;
                }
            }
            if (expression is NameExpression)
            {
                Load(Bound(expression), expression.Span);
                return;
            }
            if (expression is AttributeExpression attribute)
            {
                if (analysis.ExpressionSymbols.ContainsKey(attribute))
                    Load(Bound(attribute), attribute.Span);
                else
                {
                    EmitExpression(attribute.Receiver);
                    Op("GET_PROPERTY " + attribute.Attribute, attribute.Span);
                }
                return;
            }
            if (expression is IndexExpression index)
            {
                EmitExpression(index.Receiver);
                EmitExpression(index.Index);
                Op("GET", index.Span);
                return;
            }
            if (expression is UnaryExpression unary)
            {
                // The signed Int64 minimum cannot be represented as an unsigned positive Int64.
                if (unary.Operator == "-" && unary.Operand is LiteralExpression number &&
                    number.Kind == LiteralKind.Integer && number.Text == "9223372036854775808")
                {
                    Op("PUSH INT -9223372036854775808", unary.Span);
                    return;
                }
                if (unary.Operator == "-" )
                {
                    Op("PUSH INT 0", unary.Span);
                    EmitExpression(unary.Operand);
                    Op("SUBTRACT", unary.Span);
                }
                else if (unary.Operator == "not")
                {
                    EmitExpression(unary.Operand);
                    Op("IS_TRUTHY", unary.Span);
                    Op("NOT", unary.Span);
                }
                else if (unary.Operator == "+")
                {
                    // Validate through native numeric dispatch instead of silently
                    // accepting non-numeric operands as a no-op.
                    Op("PUSH INT 0", unary.Span);
                    EmitExpression(unary.Operand);
                    Op("ADD", unary.Span);
                }
                else throw Failure(unary.Span, "Unknown unary operator.");
                return;
            }
            if (expression is BinaryExpression binary)
            {
                if (binary.Operator == "and" || binary.Operator == "or")
                {
                    int left = Temp(binary.Span);
                    string keepLeft = Label();
                    string end = Label();
                    EmitExpression(binary.Left);
                    Op("STORE_LOCAL " + left, binary.Left.Span);
                    Op("LOAD_LOCAL " + left, binary.Left.Span);
                    Op("IS_TRUTHY", binary.Left.Span);
                    Op((binary.Operator == "and" ? "JUMP_IF_FALSE " : "JUMP_IF_TRUE ") + keepLeft, binary.Span);
                    EmitExpression(binary.Right);
                    Op("JUMP " + end, binary.Span);
                    Mark(keepLeft);
                    Op("LOAD_LOCAL " + left, binary.Span);
                    Mark(end);
                    Op("PASS", binary.Span);
                    return;
                }
                if (!BinaryInstructions.TryGetValue(binary.Operator, out string instruction))
                    throw Failure(binary.Span, "Unknown binary operator.");
                if (binary.Operator == "in" || binary.Operator == "not in")
                {
                    // Evaluate left then right exactly once; CONTAINS expects (container, item).
                    int item = Temp(binary.Span);
                    EmitExpression(binary.Left);
                    Op("STORE_LOCAL " + item, binary.Left.Span);
                    EmitExpression(binary.Right);
                    Op("LOAD_LOCAL " + item, binary.Left.Span);
                    Op("CONTAINS", binary.Span);
                }
                else
                {
                    EmitExpression(binary.Left);
                    EmitExpression(binary.Right);
                    Op(instruction, binary.Span);
                }
                if (binary.Operator == "!=" || binary.Operator == "not in") Op("NOT", binary.Span);
                return;
            }
            if (expression is CallExpression call)
            {
                if (call.Callee is AttributeExpression method &&
                    !analysis.ExpressionSymbols.ContainsKey(method))
                {
                    EmitExpression(method.Receiver);
                    foreach (Expression argument in call.Arguments) EmitExpression(argument);
                    Op("CALL_METHOD " + method.Attribute + " " + call.Arguments.Count, call.Span);
                }
                else
                {
                    EmitExpression(call.Callee);
                    foreach (Expression argument in call.Arguments) EmitExpression(argument);
                    Op("CALL_INDIRECT " + call.Arguments.Count, call.Span);
                }
                return;
            }
            if (expression is CollectionExpression collection)
            {
                if (collection.Kind == CollectionKind.Dictionary)
                {
                    for (int i = 0; i < collection.Items.Count; i++)
                    {
                        EmitExpression(collection.Keys[i]);
                        EmitExpression(collection.Items[i]);
                    }
                    Op("DICT " + collection.Items.Count, collection.Span);
                }
                else
                {
                    foreach (Expression item in collection.Items) EmitExpression(item);
                    string op = collection.Kind == CollectionKind.List ? "LIST" :
                        collection.Kind == CollectionKind.Tuple ? "TUPLE" : "SET";
                    Op(op + " " + collection.Items.Count, collection.Span);
                }
                return;
            }
            throw Failure(expression.Span, "Unsupported expression in backend.");
        }

        internal static string Quote(string value)
        {
            var escaped = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    case '\0': escaped.Append("\\0"); break;
                    default:
                        if (char.IsControl(c)) escaped.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else escaped.Append(c);
                        break;
                }
            }
            return escaped.Append('"').ToString();
        }

        private DScriptBackendException Failure(SourceSpan span, string message)
            => new DScriptBackendException(module, span, message);
    }

    internal sealed class DScriptBackendException : Exception
    {
        internal string Module { get; }
        internal SourceSpan Span { get; }
        internal DScriptBackendException(string module, SourceSpan span, string message) : base(message)
        { Module = module; Span = span; }
    }
}
