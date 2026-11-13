using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Semantics;
using DSExecution.Native;
using DOrchestration;
using DSimulation;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Conservative, editor-only semantic warnings. Type annotations describe an
    /// intended type but never change DScript compilation or runtime semantics.
    /// </summary>
    internal static class DScriptSemanticWarnings
    {
        private const int Limit = 40;

        internal static IReadOnlyList<DScriptDiagnostic> Analyze(string module, string source,
            DScriptAnalysis analysis)
        {
            if (analysis == null || source == null ||
                !analysis.Modules.TryGetValue(module, out ModuleNode root))
                return Array.Empty<DScriptDiagnostic>();

            var warnings = new List<DScriptDiagnostic>();
            var nativeNames = new HashSet<string>(NativeLibrary.GetFunctionNames(string.Empty),
                StringComparer.Ordinal);
            var methodNames = new HashSet<string>(SimulationApiCatalog.Operations
                .Where(o => o.Kind == SimulationApiKind.Order ||
                    o.MemberKind == SimulationObservationMemberKind.Method)
                .Select(o => o.Name), StringComparer.Ordinal);
            var userFunctions = new HashSet<string>(root.Statements.OfType<FunctionStatement>()
                .Select(f => f.Name), StringComparer.Ordinal);

            // Multiple modules can share the same slot. Trust a type annotation
            // only when all annotated declarations of that slot agree.
            var sharedHints = analysis.Modules.Values
                .SelectMany(m => m.Statements.OfType<SharedStatement>())
                .Where(s => !string.IsNullOrEmpty(s.DeclaredType))
                .GroupBy(s => s.Name, StringComparer.Ordinal)
                .Where(g => g.Select(s => DScriptCompletionEngine.CanonicalType(s.DeclaredType))
                    .Distinct(StringComparer.Ordinal).Count() == 1)
                .ToDictionary(g => g.Key,
                    g => DScriptCompletionEngine.CanonicalType(g.First().DeclaredType),
                    StringComparer.Ordinal);

            void Warn(string code, string message, SourceSpan span, string localizationKey,
                params object[] args)
            {
                if (warnings.Count >= Limit || span.Length == 0 || span.End > source.Length)
                    return;
                warnings.Add(new DScriptDiagnostic(module, code, message, span,
                    DScriptDiagnosticSeverity.Warning, localizationKey, args));
            }

            string ExistingName(string name)
            {
                if (methodNames.Contains(name)) return "self." + name;
                if (nativeNames.Contains(name)) return name + "()";
                if (userFunctions.Contains(name)) return name + "()";
                return null;
            }

            foreach (Statement statement in root.Statements)
            {
                if (statement is SharedStatement shared)
                {
                    string existing = ExistingName(shared.Name);
                    if (existing != null)
                        Warn("DS6201", "Shared shadows " + existing, shared.NameSpan,
                            "hint_shadow", shared.Name, existing);
                    if (shared.Initializer != null)
                    {
                        CheckExpected(shared.Name, shared.DeclaredType, shared.Initializer, null,
                            shared.Span.Start, module, analysis, Warn);
                        InspectExpression(shared.Initializer, null);
                    }
                }
                else if (statement is FunctionStatement function)
                {
                    string existing = methodNames.Contains(function.Name) ? "self." + function.Name :
                        nativeNames.Contains(function.Name) ? function.Name + "()" : null;
                    if (existing != null)
                        Warn("DS6201", "Function shadows " + existing,
                            HeaderNameSpan(function, source), "hint_shadow", function.Name, existing);

                    var declared = new Dictionary<string, string>(sharedHints, StringComparer.Ordinal);
                    foreach (var parameter in function.ParameterTypes)
                        declared[parameter.Key] = DScriptCompletionEngine.CanonicalType(parameter.Value);
                    CollectDeclaredTypes(function.Body, declared);
                    var seenLocals = new HashSet<string>(function.Parameters, StringComparer.Ordinal);
                    foreach (string parameter in function.Parameters)
                    {
                        string previous = ExistingName(parameter);
                        if (previous != null && !sharedHints.ContainsKey(parameter))
                            Warn("DS6201", "Parameter shadows " + previous,
                                HeaderParameterSpan(function, source, parameter),
                                "hint_shadow", parameter, previous);
                    }
                    foreach (Statement nested in function.Body)
                        InspectStatement(nested, function, declared, seenLocals);
                }
                if (warnings.Count >= Limit) break;
            }
            return warnings;

            void InspectStatement(Statement statement, FunctionStatement function,
                Dictionary<string, string> declared, HashSet<string> seenLocals)
            {
                if (warnings.Count >= Limit) return;
                if (statement is AssignmentStatement assignment)
                {
                    if (assignment.Target is NameExpression name)
                    {
                        if (seenLocals.Add(name.Name) &&
                            !(analysis.ModuleSymbols.TryGetValue(module, out var symbols) &&
                              symbols.Globals.ContainsKey(name.Name)))
                        {
                            string existing = ExistingName(name.Name);
                            if (existing != null)
                                Warn("DS6201", "Local shadows " + existing, name.Span,
                                    "hint_shadow", name.Name, existing);
                        }
                        if (assignment.Operator == "=" && declared.TryGetValue(name.Name, out var expected))
                            CheckExpected(name.Name, expected, assignment.Value, function,
                                assignment.Span.Start, module, analysis, Warn);
                    }
                    InspectExpression(assignment.Value, function);
                    InspectExpression(assignment.Target, function);
                }
                else if (statement is ExpressionStatement expression)
                    InspectExpression(expression.Value, function);
                else if (statement is ReturnStatement ret)
                {
                    if (ret.Value != null)
                    {
                        CheckExpected("return", function.ReturnType, ret.Value, function,
                            ret.Span.Start, module, analysis, Warn);
                        InspectExpression(ret.Value, function);
                    }
                }
                else if (statement is IfStatement conditional)
                {
                    foreach (var branch in conditional.Branches)
                    {
                        InspectExpression(branch.Condition, function);
                        foreach (Statement child in branch.Body)
                            InspectStatement(child, function, declared, seenLocals);
                    }
                    foreach (Statement child in conditional.ElseBody)
                        InspectStatement(child, function, declared, seenLocals);
                }
                else if (statement is WhileStatement whileLoop)
                {
                    InspectExpression(whileLoop.Condition, function);
                    foreach (Statement child in whileLoop.Body)
                        InspectStatement(child, function, declared, seenLocals);
                }
                else if (statement is ForStatement forLoop)
                {
                    if (seenLocals.Add(forLoop.Variable) &&
                        !(analysis.ModuleSymbols.TryGetValue(module, out var symbols) &&
                          symbols.Globals.ContainsKey(forLoop.Variable)))
                    {
                        string existing = ExistingName(forLoop.Variable);
                        if (existing != null)
                            Warn("DS6201", "Loop variable shadows " + existing, forLoop.VariableSpan,
                                "hint_shadow", forLoop.Variable, existing);
                    }
                    InspectExpression(forLoop.Iterable, function);
                    foreach (Statement child in forLoop.Body)
                        InspectStatement(child, function, declared, seenLocals);
                }
            }

            void InspectExpression(Expression expression, FunctionStatement function)
            {
                if (warnings.Count >= Limit || expression == null) return;
                if (expression is CallExpression call)
                {
                    if (call.Callee is NameExpression name &&
                        analysis.ExpressionSymbols.TryGetValue(name, out DScriptSymbol symbol) &&
                        (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter ||
                         (symbol.Kind == SymbolKind.Global && sharedHints.ContainsKey(name.Name))))
                    {
                        string actual = DScriptCompletionEngine.InferKnownType(analysis,
                            module, function, name, name.Span.Start);
                        if (actual != null && actual != "Function")
                            Warn("DS6203", "Value " + name.Name + " is not callable", name.Span,
                                "hint_noncallable", name.Name, actual);
                    }
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        Expression argument = call.Arguments[i];
                        string expected = ExpectedCallArgument(analysis, call, i);
                        if (expected != null)
                            CheckExpected("argument", expected, argument, function,
                                argument.Span.Start, module, analysis, Warn);
                        InspectExpression(argument, function);
                    }
                    InspectExpression(call.Callee, function);
                }
                else if (expression is BinaryExpression binary)
                {
                    InspectExpression(binary.Left, function);
                    InspectExpression(binary.Right, function);
                }
                else if (expression is UnaryExpression unary)
                    InspectExpression(unary.Operand, function);
                else if (expression is AttributeExpression attribute)
                    InspectExpression(attribute.Receiver, function);
                else if (expression is IndexExpression index)
                {
                    InspectExpression(index.Receiver, function);
                    InspectExpression(index.Index, function);
                }
                else if (expression is CollectionExpression collection)
                {
                    foreach (Expression key in collection.Keys) InspectExpression(key, function);
                    foreach (Expression item in collection.Items) InspectExpression(item, function);
                }
            }
        }

        private static void CollectDeclaredTypes(IReadOnlyList<Statement> statements,
            Dictionary<string, string> declared)
        {
            foreach (Statement statement in statements)
            {
                if (statement is AssignmentStatement assignment &&
                    assignment.Target is NameExpression name &&
                    !string.IsNullOrEmpty(assignment.DeclaredType))
                    declared[name.Name] = DScriptCompletionEngine.CanonicalType(assignment.DeclaredType);
                else if (statement is IfStatement conditional)
                {
                    foreach (var branch in conditional.Branches) CollectDeclaredTypes(branch.Body, declared);
                    CollectDeclaredTypes(conditional.ElseBody, declared);
                }
                else if (statement is WhileStatement loop) CollectDeclaredTypes(loop.Body, declared);
                else if (statement is ForStatement iterator) CollectDeclaredTypes(iterator.Body, declared);
            }
        }

        private static void CheckExpected(string name, string declaredType, Expression value,
            FunctionStatement function, int position, string module, DScriptAnalysis analysis,
            Action<string, string, SourceSpan, string, object[]> warn)
        {
            string expected = DScriptCompletionEngine.CanonicalType(declaredType);
            if (expected == null || value == null) return;
            // Untyped shared variables can change from another VM. Their initial
            // value is not evidence strong enough for a type mismatch warning.
            if (value is NameExpression named &&
                analysis.ExpressionSymbols.TryGetValue(named, out DScriptSymbol symbol) &&
                symbol.Kind == SymbolKind.Global &&
                !analysis.Modules.Values.SelectMany(m => m.Statements.OfType<SharedStatement>())
                    .Any(s => s.Name == named.Name && !string.IsNullOrEmpty(s.DeclaredType)))
                return;
            string actual = DScriptCompletionEngine.CanonicalType(
                DScriptCompletionEngine.InferKnownType(analysis, module, function, value, position));
            if (actual == null || Compatible(expected, actual)) return;
            if (actual == "Function" && expected != "Function" &&
                (value is NameExpression || value is AttributeExpression))
                warn("DS6204", "Function used where a different value is expected", value.Span,
                    expected == "NonFunction" ? "hint_function_argument" : "hint_function_value",
                    new object[] { expected });
            else
                warn("DS6202", "Type mismatch " + expected + " vs " + actual, value.Span,
                    "hint_incompatible", new object[] { name, expected, actual });
        }

        private static bool Compatible(string expected, string actual)
        {
            if (expected == "NonFunction") return actual != "Function";
            if (expected == actual || actual == "None") return true;
            return expected == "Decimal" && actual == "Int";
        }

        private static string ExpectedCallArgument(DScriptAnalysis analysis, CallExpression call, int index)
        {
            if (!analysis.ExpressionSymbols.TryGetValue(call.Callee, out DScriptSymbol symbol))
            {
                // self.method() binds through its receiver. Do not assume that
                // any other unresolved member has the simulation API signature.
                if (call.Callee is AttributeExpression attribute &&
                    attribute.Receiver is NameExpression self && self.Name == "self")
                    return ExpectedSimulationArgument(attribute.Attribute, index, call.Arguments.Count);
                return null;
            }
            if (symbol.Kind == SymbolKind.Function)
            {
                int dot = symbol.QualifiedName.LastIndexOf('.');
                if (dot > 0 && analysis.Modules.TryGetValue(symbol.QualifiedName.Substring(0, dot), out var ast))
                {
                    var fn = ast.Statements.OfType<FunctionStatement>().FirstOrDefault(
                        f => f.Name == symbol.QualifiedName.Substring(dot + 1));
                    if (fn != null && index < fn.Parameters.Count &&
                        fn.ParameterTypes.TryGetValue(fn.Parameters[index], out string type))
                        return DScriptCompletionEngine.CanonicalType(type);
                }
            }
            // A function/local/shared can shadow an implicit self method.
            // Only read that method's signature when the binder chose it.
            if (symbol.Kind == SymbolKind.ImplicitSelfMethod)
                return ExpectedSimulationArgument(symbol.Name, index, call.Arguments.Count);
            if (symbol.Kind == SymbolKind.NativeFunction &&
                NativeLibrary.TryGetFunctionSignature(symbol.QualifiedName, out var native) &&
                index < native.Parameters.Count)
            {
                string parameter = native.Parameters[index];
                int colon = parameter.LastIndexOf(':');
                return colon < 0 ? null : DScriptCompletionEngine.CanonicalType(parameter.Substring(colon + 1));
            }
            return null;
        }

        private static string ExpectedSimulationArgument(string operationName, int index, int count)
        {
            var entries = SimulationApiCatalog.Operations.Where(op => op.Name == operationName &&
                (op.Kind == SimulationApiKind.Order ||
                 op.MemberKind == SimulationObservationMemberKind.Method) &&
                count >= op.MinArguments && count <= op.MaxArguments).ToArray();
            if (entries.Length == 0) return null;
            var types = entries.Select(op => op.ManualRegistration ? "Function" :
                index < op.Arguments.Count ? DScriptExpectedArgument.ParameterType(op.Arguments[index]) : null)
                .ToArray();
            if (types.Any(type => type == null)) return null;
            return types.Distinct(StringComparer.Ordinal).Count() == 1
                ? types[0] : types.All(type => type != "Function") ? "NonFunction" : null;
        }

        private static SourceSpan HeaderNameSpan(FunctionStatement function, string source)
        {
            int start = function.Span.Start;
            int offset = source.IndexOf(function.Name, start,
                Math.Min(source.Length - start, Math.Max(function.Name.Length + 4, 128)),
                StringComparison.Ordinal);
            if (offset < 0) offset = start + 4;
            return new SourceSpan(offset, function.Name.Length, function.Span.Line,
                offset - start + function.Span.Column);
        }

        private static SourceSpan HeaderParameterSpan(FunctionStatement function, string source, string name)
        {
            int end = source.IndexOf('\n', function.Span.Start);
            if (end < 0) end = source.Length;
            int open = source.IndexOf('(', function.Span.Start, end - function.Span.Start);
            int offset = open >= 0 ? source.IndexOf(name, open, end - open,
                StringComparison.Ordinal) : -1;
            if (offset < 0) return HeaderNameSpan(function, source);
            return new SourceSpan(offset, name.Length, function.Span.Line,
                offset - function.Span.Start + function.Span.Column);
        }
    }
}
