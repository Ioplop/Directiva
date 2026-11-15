using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;
using DOrchestration;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Resolve Ctrl+click navigation using the same bound symbols as compilation and
    /// symbol rename. Source spans are always interpreted in their owning module.
    /// Unknown names, native API members, strings and comments are not links.
    /// </summary>
    public static class DScriptGoToDefinition
    {
        public sealed class Location
        {
            public string Module { get; }
            public SourceSpan Span { get; }

            internal Location(string module, SourceSpan span)
            { Module = module; Span = span; }
        }

        // CodeEditorView stores LF-only text and reports offsets into that text.
        // Normalize dependencies as well so the destination span uses the same coordinates.
        private sealed class NormalizedProvider : IDScriptSourceProvider
        {
            private readonly IDScriptSourceProvider source;
            public NormalizedProvider(IDScriptSourceProvider source) { this.source = source; }
            public bool TryReadModule(string name, out string content)
            {
                if (!source.TryReadModule(name, out content) || content == null) return false;
                content = content.Replace("\r\n", "\n").Replace('\r', '\n');
                return true;
            }
        }

        public static bool TryResolve(string module, IDScriptSourceProvider sourceProvider,
            int sourceOffset, out Location destination)
        {
            destination = null;
            if (sourceProvider == null || string.IsNullOrEmpty(module) || sourceOffset < 0)
                return false;

            var provider = new NormalizedProvider(sourceProvider);
            if (!provider.TryReadModule(module, out string source) || sourceOffset >= source.Length)
                return false;
            var tokens = new DScriptLexer(module, source).Tokenize();
            DScriptToken selected = default;
            foreach (var token in tokens)
                if (token.Kind == TokenKind.Identifier && token.Span.Start <= sourceOffset &&
                    sourceOffset < token.Span.End)
                { selected = token; break; }
            if (selected.Kind != TokenKind.Identifier) return false;

            var analysis = DScriptFrontend.AnalyzeForRefactoring(module, provider,
                SimulationDScriptProfile.Options);
            if (!analysis.ModuleSymbols.TryGetValue(module, out var symbols)) return false;

            DScriptSymbol target = ResolveReference(analysis, symbols, tokens, selected);
            if (target == null || !IsUserSymbol(target.Kind)) return false;
            return FindDeclaration(analysis, provider, module, target, out destination);
        }

        private static bool IsUserSymbol(SymbolKind kind) =>
            kind == SymbolKind.Function || kind == SymbolKind.Global ||
            kind == SymbolKind.VmGlobal || kind == SymbolKind.Local ||
            kind == SymbolKind.Parameter;

        private static DScriptSymbol ResolveReference(DScriptAnalysis analysis,
            DScriptModuleSymbols symbols, IReadOnlyList<DScriptToken> tokens, DScriptToken selected)
        {
            DScriptSymbol resolved = null;
            SourceSpan clicked = selected.Span;
            WalkModule(symbols.Ast, statement =>
            {
                if (statement is ForStatement loop && SameSpan(loop.VariableSpan, clicked))
                    analysis.ForTargetSymbols.TryGetValue(loop, out resolved);
                else if (statement is FromImportStatement from)
                {
                    foreach (var imported in from.Names)
                    {
                        if (!SameSpan(imported.Span, clicked) &&
                            !IsImportAliasToken(tokens, imported, clicked)) continue;
                        symbols.Imports.TryGetValue(imported.Alias ?? imported.Name, out resolved);
                    }
                }
                // A declaration is also a navigable symbol (e.g. Ctrl+click on def).
                else if (statement is FunctionStatement function &&
                    symbols.Functions.TryGetValue(function.Name, out var functionSymbol) &&
                    TryFunctionNameToken(tokens, function, out var nameToken) &&
                    SameSpan(nameToken.Span, clicked))
                    resolved = functionSymbol;
                else if (statement is SharedStatement shared &&
                    SameSpan(shared.NameSpan, clicked))
                    analysis.SharedDeclarations.TryGetValue(shared, out resolved);
                else if (statement is VmGlobalStatement global &&
                    SameSpan(global.NameSpan, clicked))
                    analysis.VmGlobalDeclarations.TryGetValue(global, out resolved);
            }, expression =>
            {
                if (expression is NameExpression name && SameSpan(name.Span, clicked))
                    analysis.ExpressionSymbols.TryGetValue(name, out resolved);
                else if (expression is AttributeExpression attribute &&
                    attribute.Span.End == clicked.End && attribute.Attribute == selected.Text)
                    analysis.ExpressionSymbols.TryGetValue(attribute, out resolved);
            });

            // Parameters are declared in the signature, not as NameExpression nodes.
            if (resolved == null)
                foreach (var function in analysis.FunctionSymbols.Values)
                    if (ReferenceEquals(function.Module, symbols) &&
                        function.Locals.TryGetValue(selected.Text, out var parameter) &&
                        parameter.Kind == SymbolKind.Parameter &&
                        TryParameterToken(tokens, function.Ast, selected.Text, out var token) &&
                        SameSpan(token.Span, clicked))
                        return parameter;
            return resolved;
        }

        private static SourceSpan? GetBoundTargetSpan(Expression expression,
            DScriptSymbol symbol, DScriptAnalysis analysis)
        {
            if (expression is NameExpression name &&
                analysis.ExpressionSymbols.TryGetValue(name, out var bound) &&
                ReferenceEquals(bound, symbol)) return name.Span;
            if (expression is CollectionExpression tuple)
                foreach (Expression item in tuple.Items)
                {
                    SourceSpan? found = GetBoundTargetSpan(item, symbol, analysis);
                    if (found.HasValue) return found;
                }
            return null;
        }

        private static bool FindDeclaration(DScriptAnalysis analysis, IDScriptSourceProvider provider,
            string currentModule, DScriptSymbol target, out Location destination)
        {
            destination = null;
            if (target.Kind == SymbolKind.Local || target.Kind == SymbolKind.Parameter)
            {
                var owner = analysis.FunctionSymbols.Values.FirstOrDefault(function =>
                    function.Locals.TryGetValue(target.Name, out var candidate) &&
                    ReferenceEquals(candidate, target));
                if (owner == null || !provider.TryReadModule(owner.Module.Name, out var text)) return false;
                var tokens = new DScriptLexer(owner.Module.Name, text).Tokenize();
                if (target.Kind == SymbolKind.Parameter)
                {
                    if (!TryParameterToken(tokens, owner.Ast, target.Name, out var parameter)) return false;
                    destination = new Location(owner.Module.Name, parameter.Span);
                    return true;
                }

                // Locals are function-scoped and predeclared by the binder. The first
                // assignment/for-target is their declaration, even if a use precedes it.
                SourceSpan? first = null;
                foreach (var statement in owner.Ast.Body)
                    WalkStatement(statement, nested =>
                    {
                        SourceSpan? candidate = null;
                        if (nested is AssignmentStatement assignment)
                            candidate = GetBoundTargetSpan(assignment.Target, target, analysis);
                        else if (nested is ForStatement loop)
                        {
                            if (loop.Target is NameExpression &&
                                analysis.ForTargetSymbols.TryGetValue(loop, out var symbol) &&
                                ReferenceEquals(symbol, target))
                                candidate = loop.VariableSpan;
                            else if (loop.Target is CollectionExpression)
                                candidate = GetBoundTargetSpan(loop.Target, target, analysis);
                        }
                        if (candidate.HasValue && (!first.HasValue ||
                            candidate.Value.Start < first.Value.Start)) first = candidate;
                    }, _ => { });
                if (!first.HasValue) return false;
                destination = new Location(owner.Module.Name, first.Value);
                return true;
            }

            var declarations = new List<(string Module, SourceSpan Span, bool Initialized)>();
            foreach (var pair in analysis.ModuleSymbols.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string name = pair.Key;
                var symbols = pair.Value;
                IReadOnlyList<DScriptToken> tokens = null;
                foreach (var statement in symbols.Ast.Statements)
                {
                    if (statement is FunctionStatement function && target.Kind == SymbolKind.Function &&
                        symbols.Functions.TryGetValue(function.Name, out var symbol) &&
                        symbol.QualifiedName == target.QualifiedName)
                    {
                        if (tokens == null && provider.TryReadModule(name, out var text))
                            tokens = new DScriptLexer(name, text).Tokenize();
                        if (tokens != null && TryFunctionNameToken(tokens, function, out var token))
                            declarations.Add((name, token.Span, false));
                    }
                    else if (statement is SharedStatement shared && target.Kind == SymbolKind.Global &&
                        analysis.SharedDeclarations.TryGetValue(shared, out var sharedSymbol) &&
                        sharedSymbol.QualifiedName == target.QualifiedName)
                        declarations.Add((name, shared.NameSpan, shared.Initializer != null));
                    else if (statement is VmGlobalStatement vmGlobal && target.Kind == SymbolKind.VmGlobal &&
                        analysis.VmGlobalDeclarations.TryGetValue(vmGlobal, out var vmSymbol) &&
                        vmSymbol.QualifiedName == target.QualifiedName)
                        declarations.Add((name, vmGlobal.NameSpan, vmGlobal.Initializer != null));
                }
            }
            if (declarations.Count == 0) return false;
            // Shared declarations can occur in several modules. Prefer a declaration
            // in this file, then the initializing declaration, then a stable order.
            var chosen = declarations
                .OrderBy(item => item.Module == currentModule ? 0 : 1)
                .ThenBy(item => item.Initialized ? 0 : 1)
                .ThenBy(item => item.Module, StringComparer.Ordinal)
                .ThenBy(item => item.Span.Start).First();
            destination = new Location(chosen.Module, chosen.Span);
            return true;
        }

        private static bool SameSpan(SourceSpan a, SourceSpan b) =>
            a.Start == b.Start && a.Length == b.Length;

        private static bool IsImportAliasToken(IReadOnlyList<DScriptToken> tokens,
            ImportedName imported, SourceSpan clicked)
        {
            if (imported.Alias == null) return false;
            for (int i = 0; i + 1 < tokens.Count; i++)
                if (tokens[i].Span.Start == imported.Span.Start &&
                    tokens[i].Kind == TokenKind.Identifier &&
                    tokens[i + 1].Kind == TokenKind.As && i + 2 < tokens.Count &&
                    tokens[i + 2].Kind == TokenKind.Identifier &&
                    tokens[i + 2].Text == imported.Alias)
                    return SameSpan(tokens[i + 2].Span, clicked);
            return false;
        }

        private static bool TryFunctionNameToken(IReadOnlyList<DScriptToken> tokens,
            FunctionStatement function, out DScriptToken result)
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
                if (tokens[i].Kind == TokenKind.Def && tokens[i].Span.Start == function.Span.Start &&
                    tokens[i + 1].Kind == TokenKind.Identifier &&
                    tokens[i + 1].Text == function.Name)
                { result = tokens[i + 1]; return true; }
            result = default;
            return false;
        }

        private static bool TryParameterToken(IReadOnlyList<DScriptToken> tokens,
            FunctionStatement function, string name, out DScriptToken result)
        {
            result = default;
            for (int i = 0; i + 2 < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.Def ||
                    tokens[i].Span.Start != function.Span.Start ||
                    tokens[i + 2].Kind != TokenKind.LeftParen) continue;
                bool expectName = true;
                for (int j = i + 3; j < tokens.Count &&
                    tokens[j].Kind != TokenKind.RightParen; j++)
                {
                    if (tokens[j].Kind == TokenKind.Comma) expectName = true;
                    else if (expectName && tokens[j].Kind == TokenKind.Identifier)
                    {
                        if (tokens[j].Text == name) { result = tokens[j]; return true; }
                        expectName = false;
                    }
                }
                break;
            }
            return false;
        }

        // Walk only the AST owned by the selected module, because imported AST
        // nodes can have identical numeric offsets in their different source files.
        private static void WalkModule(ModuleNode module, Action<Statement> statement,
            Action<Expression> expression)
        {
            foreach (var item in module.Statements) WalkStatement(item, statement, expression);
        }

        private static void WalkStatement(Statement node, Action<Statement> statement,
            Action<Expression> expression)
        {
            statement(node);
            switch (node)
            {
                case FunctionStatement function:
                    foreach (var child in function.Body) WalkStatement(child, statement, expression);
                    break;
                case SharedStatement shared:
                    WalkExpression(shared.Initializer, expression);
                    break;
                case VmGlobalStatement global:
                    WalkExpression(global.Initializer, expression);
                    break;
                case AssignmentStatement assignment:
                    WalkExpression(assignment.Target, expression);
                    WalkExpression(assignment.Value, expression);
                    break;
                case ExpressionStatement value:
                    WalkExpression(value.Value, expression);
                    break;
                case ReturnStatement ret:
                    WalkExpression(ret.Value, expression);
                    break;
                case IfStatement conditional:
                    foreach (var branch in conditional.Branches)
                    {
                        WalkExpression(branch.Condition, expression);
                        foreach (var child in branch.Body) WalkStatement(child, statement, expression);
                    }
                    foreach (var child in conditional.ElseBody) WalkStatement(child, statement, expression);
                    break;
                case WhileStatement loop:
                    WalkExpression(loop.Condition, expression);
                    foreach (var child in loop.Body) WalkStatement(child, statement, expression);
                    break;
                case ForStatement iterator:
                    WalkExpression(iterator.Target, expression);
                    WalkExpression(iterator.Iterable, expression);
                    foreach (var child in iterator.Body) WalkStatement(child, statement, expression);
                    break;
            }
        }

        private static void WalkExpression(Expression node, Action<Expression> visit)
        {
            if (node == null) return;
            visit(node);
            switch (node)
            {
                case UnaryExpression unary:
                    WalkExpression(unary.Operand, visit);
                    break;
                case BinaryExpression binary:
                    WalkExpression(binary.Left, visit);
                    WalkExpression(binary.Right, visit);
                    break;
                case CallExpression call:
                    WalkExpression(call.Callee, visit);
                    foreach (var arg in call.Arguments) WalkExpression(arg, visit);
                    break;
                case AttributeExpression attribute:
                    WalkExpression(attribute.Receiver, visit);
                    break;
                case IndexExpression index:
                    WalkExpression(index.Receiver, visit);
                    WalkExpression(index.Index, visit);
                    break;
                case CollectionExpression collection:
                    foreach (var key in collection.Keys) WalkExpression(key, visit);
                    foreach (var item in collection.Items) WalkExpression(item, visit);
                    break;
            }
        }
    }
}
