using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Semantic F2 refactoring. Locals use the single-document planner; functions and
    /// shared slots are planned over a snapshot of ALL scripts in the workspace.
    /// Neither planning nor verification changes files or editor buffers.
    /// </summary>
    public static class DScriptSymbolRename
    {
        public sealed class Plan
        {
            public string OldName { get; }
            public string NewName { get; }
            public string Kind { get; }
            public string OwnerModule { get; }
            public int Count { get; }
            public int Caret { get; }
            // Keys are relative .dscript paths (not DScript module names).
            public IReadOnlyDictionary<string, string> Originals { get; }
            public IReadOnlyDictionary<string, string> Changes { get; }

            internal Plan(string oldName, string newName, string kind, string ownerModule, int count, int caret,
                Dictionary<string, string> originals, Dictionary<string, string> changes)
            {
                OldName = oldName; NewName = newName; Kind = kind; OwnerModule = ownerModule; Count = count; Caret = caret;
                Originals = originals; Changes = changes;
            }
        }

        private sealed class Edit
        {
            public SourceSpan Span;
            public string Module;
            public string Identity;
            public SymbolKind Kind;
            public bool Rename;
            public bool Selectable;
        }

        private sealed class Trace
        {
            public string Module;
            public int Start;
            public SymbolKind Kind;
            public string QualifiedName;
        }

        private sealed class Snapshot
        {
            public readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, DScriptAnalysis> Analyses = new Dictionary<string, DScriptAnalysis>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<DScriptToken>> Tokens = new Dictionary<string, List<DScriptToken>>(StringComparer.Ordinal);
            public readonly Dictionary<string, Edit> Occurrences = new Dictionary<string, Edit>(StringComparer.Ordinal);
            public readonly Dictionary<string, Trace> Traces = new Dictionary<string, Trace>(StringComparer.Ordinal);
        }

        public static bool TryFind(string module, IDScriptSourceProvider provider,
            IEnumerable<string> workspaceModules, int caret, out string name, out string error)
        {
            if (DScriptLocalRename.TryFind(module, provider, caret, out name, out _))
            { error = null; return true; }
            if (!TrySnapshot(provider, workspaceModules, out var snapshot, out error))
            { name = null; return false; }
            if (!TrySelect(snapshot, module, caret, out var selected))
            { name = null; error = "rename_no_symbol"; return false; }
            name = snapshot.Sources[module].Substring(selected.Span.Start, selected.Span.Length);
            error = null;
            return true;
        }

        public static bool TryRename(string module, IDScriptSourceProvider provider,
            IEnumerable<string> workspaceModules, int caret, string proposedName,
            out Plan plan, out string error)
        {
            plan = null;
            if (DScriptLocalRename.TryFind(module, provider, caret, out _, out _))
            {
                if (!DScriptLocalRename.TryRename(module, provider, caret, proposedName,
                    out var local, out error)) return false;
                var path = ToPath(module);
                if (!provider.TryReadModule(module, out var original))
                { error = "rename_unavailable_module"; return false; }
                plan = new Plan(local.OldName, local.NewName, "local", module, local.Count, local.Caret,
                    new Dictionary<string, string> { [path] = Normalize(original) },
                    local.Count == 0 ? new Dictionary<string, string>() :
                        new Dictionary<string, string> { [path] = local.Content });
                return true;
            }

            if (!TrySnapshot(provider, workspaceModules, out var snapshot, out error)) return false;
            if (!TrySelect(snapshot, module, caret, out var selected))
            { error = "rename_no_symbol"; return false; }
            string oldName = snapshot.Sources[module].Substring(selected.Span.Start, selected.Span.Length);
            string newName = (proposedName ?? string.Empty).Trim();
            if (!ValidIdentifier(newName))
            { error = "rename_invalid_identifier"; return false; }
            if (newName == oldName)
            {
                plan = new Plan(oldName, newName, selected.Kind == SymbolKind.Function ? "function" : "shared",
                    selected.Kind == SymbolKind.Function ? selected.Identity.Substring(0, selected.Identity.LastIndexOf('.')) : null,
                    0, caret, new Dictionary<string, string>(), new Dictionary<string, string>());
                return true;
            }

            // Shared symbols have one program-wide identity by name. A second shared
            // declaration with the requested name must NEVER be silently merged into it.
            if (selected.Kind == SymbolKind.Global && snapshot.Occurrences.Values.Any(e =>
                    e.Kind == SymbolKind.Global && e.Identity == "shared." + newName))
            { error = "rename_collision_shared"; return false; }

            var edits = snapshot.Occurrences.Values
                .Where(e => e.Kind == selected.Kind && e.Identity == selected.Identity && e.Rename)
                .GroupBy(e => e.Module, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Span.Start).ToArray(), StringComparer.Ordinal);
            if (edits.Count == 0)
            { error = "rename_stale_source"; return false; }
            var updated = new Dictionary<string, string>(snapshot.Sources, StringComparer.Ordinal);
            foreach (var pair in edits)
            {
                var text = snapshot.Sources[pair.Key];
                foreach (var edit in pair.Value.OrderByDescending(e => e.Span.Start))
                {
                    if (edit.Span.Start < 0 || edit.Span.End > text.Length ||
                        text.Substring(edit.Span.Start, edit.Span.Length) != oldName)
                    { error = "rename_stale_source"; return false; }
                    text = text.Remove(edit.Span.Start, edit.Span.Length).Insert(edit.Span.Start, newName);
                }
                updated[pair.Key] = text;
            }

            // Verify every program in the workspace on the proposed, uncommitted snapshot.
            // Also verify binding identities, since a valid rename can still accidentally
            // capture a reference by an existing local/import with the requested spelling.
            var newProvider = new InMemoryDScriptSourceProvider(updated);
            foreach (var root in snapshot.Sources.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                var analysis = DScriptFrontend.Analyze(root, newProvider);
                if (!analysis.Success)
                { error = "rename_result_invalid"; return false; }
                if (!VerifyBindings(root, analysis, snapshot, edits, selected, newName))
                { error = "rename_binding_changed"; return false; }
            }

            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in edits)
            {
                string path = ToPath(pair.Key);
                originals[path] = snapshot.Sources[pair.Key];
                changes[path] = updated[pair.Key];
            }
            int newCaret = selected.Span.Start + newName.Length;
            if (edits.TryGetValue(module, out var sameFile))
                foreach (var edit in sameFile)
                    if (edit.Span.Start < selected.Span.Start)
                        newCaret += newName.Length - edit.Span.Length;
            plan = new Plan(oldName, newName, selected.Kind == SymbolKind.Function ? "function" : "shared",
                selected.Kind == SymbolKind.Function ? selected.Identity.Substring(0, selected.Identity.LastIndexOf('.')) : null,
                edits.Values.Sum(items => items.Length), newCaret, originals, changes);
            error = null;
            return true;
        }

        private static bool TrySnapshot(IDScriptSourceProvider provider, IEnumerable<string> modules,
            out Snapshot snapshot, out string error)
        {
            snapshot = new Snapshot(); error = null;
            if (provider == null || modules == null)
            { error = "rename_unavailable_module"; return false; }
            foreach (var name in modules.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(name) || !provider.TryReadModule(name, out var source) || source == null)
                { error = "rename_unavailable_module"; return false; }
                snapshot.Sources.Add(name, Normalize(source));
            }
            if (snapshot.Sources.Count == 0)
            { error = "rename_unavailable_module"; return false; }
            var memory = new InMemoryDScriptSourceProvider(snapshot.Sources);
            foreach (var name in snapshot.Sources.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                var analysis = DScriptFrontend.Analyze(name, memory);
                if (!analysis.Success)
                { error = "rename_fix_errors"; return false; }
                snapshot.Analyses.Add(name, analysis);
                snapshot.Tokens.Add(name, new DScriptLexer(name, snapshot.Sources[name]).Tokenize().ToList());
            }
            foreach (var name in snapshot.Sources.Keys.OrderBy(n => n, StringComparer.Ordinal))
                CollectOccurrences(snapshot, name);
            return true;
        }

        private static void CollectOccurrences(Snapshot snapshot, string module)
        {
            var analysis = snapshot.Analyses[module];
            var symbols = analysis.ModuleSymbols[module];
            var tokens = snapshot.Tokens[module];
            foreach (var statement in symbols.Ast.Statements)
            {
                if (statement is FunctionStatement function &&
                    symbols.Functions.TryGetValue(function.Name, out var symbol))
                {
                    int index = tokens.FindIndex(token => token.Kind == TokenKind.Def &&
                        token.Span.Start == function.Span.Start);
                    if (index >= 0 && index + 1 < tokens.Count && tokens[index + 1].Text == function.Name)
                        AddOccurrence(snapshot, module, tokens[index + 1].Span, symbol, true, true);
                }
                else if (statement is SharedStatement shared &&
                    analysis.SharedDeclarations.TryGetValue(shared, out var global))
                    AddOccurrence(snapshot, module, shared.NameSpan, global, true, true);
                else if (statement is FromImportStatement from)
                {
                    foreach (var imported in from.Names)
                    {
                        if (symbols.Imports.TryGetValue(imported.Alias ?? imported.Name,
                            out var target) && (target.Kind == SymbolKind.Function || target.Kind == SymbolKind.Global))
                            AddOccurrence(snapshot, module, imported.Span, target, true, true);
                    }
                }
            }
            var explicitAliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var statement in symbols.Ast.Statements.OfType<FromImportStatement>())
                foreach (var imported in statement.Names)
                    if (imported.Alias != null) explicitAliases.Add(imported.Alias);

            // Analyze() binds the complete import graph, not only its entry module.
            // AST nodes and source spans from dependencies must NEVER be interpreted as
            // positions in this module. Walk only nodes owned by symbols.Ast.
            WalkModule(symbols.Ast, expression =>
            {
                if (!analysis.ExpressionSymbols.TryGetValue(expression, out var symbol)) return;
                SourceSpan location;
                if (expression is NameExpression name)
                    location = name.Span;
                else if (expression is AttributeExpression attr)
                {
                    var token = tokens.LastOrDefault(t => t.Kind == TokenKind.Identifier &&
                        t.Span.End == attr.Span.End && t.Text == attr.Attribute);
                    if (token.Kind != TokenKind.Identifier) return;
                    location = token.Span;
                }
                else return;
                AddTrace(snapshot, module, location.Start, symbol);
                if (symbol.Kind != SymbolKind.Function && symbol.Kind != SymbolKind.Global) return;
                // An explicit import alias remains local to the importing module.
                bool alias = expression is NameExpression aliasName &&
                    explicitAliases.Contains(aliasName.Name) &&
                    symbols.Imports.TryGetValue(aliasName.Name, out var imported) &&
                    imported.Kind == symbol.Kind && imported.QualifiedName == symbol.QualifiedName;
                AddOccurrence(snapshot, module, location, symbol, !alias, !alias);
            }, target =>
            {
                if (!analysis.ForTargetSymbols.TryGetValue(target, out var symbol)) return;
                AddTrace(snapshot, module, target.VariableSpan.Start, symbol);
                if (symbol.Kind != SymbolKind.Global) return;
                bool alias = explicitAliases.Contains(target.Variable) &&
                    symbols.Imports.TryGetValue(target.Variable, out var imported) &&
                    imported.Kind == SymbolKind.Global && imported.QualifiedName == symbol.QualifiedName;
                AddOccurrence(snapshot, module, target.VariableSpan, symbol, !alias, !alias);
            });
        }

        // Binder side tables cover transitive imports. These walks establish module
        // ownership by AST identity, rather than by a span that could belong to an
        // entirely different source file with the same character offsets.
        private static void WalkModule(ModuleNode root, Action<Expression> expression,
            Action<ForStatement> forTarget)
        {
            foreach (var statement in root.Statements)
                WalkStatement(statement, expression, forTarget);
        }

        private static void WalkStatement(Statement statement, Action<Expression> expression,
            Action<ForStatement> forTarget)
        {
            switch (statement)
            {
                case FunctionStatement function:
                    foreach (var nested in function.Body) WalkStatement(nested, expression, forTarget);
                    break;
                case SharedStatement shared:
                    WalkExpression(shared.Initializer, expression);
                    break;
                case AssignmentStatement assignment:
                    WalkExpression(assignment.Target, expression);
                    WalkExpression(assignment.Value, expression);
                    break;
                case ExpressionStatement call:
                    WalkExpression(call.Value, expression);
                    break;
                case ReturnStatement ret:
                    WalkExpression(ret.Value, expression);
                    break;
                case IfStatement conditional:
                    foreach (var branch in conditional.Branches)
                    {
                        WalkExpression(branch.Condition, expression);
                        foreach (var nested in branch.Body) WalkStatement(nested, expression, forTarget);
                    }
                    foreach (var nested in conditional.ElseBody) WalkStatement(nested, expression, forTarget);
                    break;
                case WhileStatement loop:
                    WalkExpression(loop.Condition, expression);
                    foreach (var nested in loop.Body) WalkStatement(nested, expression, forTarget);
                    break;
                case ForStatement iterator:
                    forTarget(iterator);
                    WalkExpression(iterator.Iterable, expression);
                    foreach (var nested in iterator.Body) WalkStatement(nested, expression, forTarget);
                    break;
            }
        }

        private static void WalkExpression(Expression node, Action<Expression> expression)
        {
            if (node == null) return;
            expression(node);
            switch (node)
            {
                case UnaryExpression unary:
                    WalkExpression(unary.Operand, expression);
                    break;
                case BinaryExpression binary:
                    WalkExpression(binary.Left, expression);
                    WalkExpression(binary.Right, expression);
                    break;
                case CallExpression call:
                    WalkExpression(call.Callee, expression);
                    foreach (var arg in call.Arguments) WalkExpression(arg, expression);
                    break;
                case AttributeExpression attribute:
                    WalkExpression(attribute.Receiver, expression);
                    break;
                case IndexExpression index:
                    WalkExpression(index.Receiver, expression);
                    WalkExpression(index.Index, expression);
                    break;
                case CollectionExpression collection:
                    foreach (var key in collection.Keys) WalkExpression(key, expression);
                    foreach (var item in collection.Items) WalkExpression(item, expression);
                    break;
            }
        }

        private static void AddOccurrence(Snapshot snapshot, string module, SourceSpan span,
            DScriptSymbol symbol, bool rename, bool selectable)
        {
            if (span.Length == 0 || span.End > snapshot.Sources[module].Length) return;
            string key = module + "\n" + span.Start;
            if (snapshot.Occurrences.TryGetValue(key, out var previous))
            {
                if (previous.Identity != symbol.QualifiedName || previous.Kind != symbol.Kind) return;
                previous.Rename |= rename; previous.Selectable |= selectable;
                return;
            }
            snapshot.Occurrences.Add(key, new Edit
            {
                Module = module, Span = span, Kind = symbol.Kind,
                Identity = symbol.QualifiedName, Rename = rename, Selectable = selectable
            });
        }

        private static void AddTrace(Snapshot snapshot, string module, int start, DScriptSymbol symbol)
        {
            string key = module + "\n" + start;
            if (!snapshot.Traces.ContainsKey(key))
                snapshot.Traces.Add(key, new Trace
                { Module = module, Start = start, Kind = symbol.Kind, QualifiedName = symbol.QualifiedName });
        }

        private static bool TrySelect(Snapshot snapshot, string module, int caret, out Edit selected)
        {
            selected = null;
            if (!snapshot.Sources.ContainsKey(module)) return false;
            foreach (var item in snapshot.Occurrences.Values)
                if (item.Module == module && item.Selectable &&
                    item.Span.Start <= caret && caret <= item.Span.End &&
                    (selected == null || item.Span.Start > selected.Span.Start))
                    selected = item;
            return selected != null;
        }

        private static bool VerifyBindings(string module, DScriptAnalysis updated,
            Snapshot original, Dictionary<string, Edit[]> edits, Edit target, string newName)
        {
            var newTokens = new DScriptLexer(module,
                ApplyEditsForVerification(original.Sources[module], edits, module, newName)).Tokenize();
            var actual = new Dictionary<int, DScriptSymbol>();
            // Bindings for imported modules have offsets relative to THEIR files.
            // Only compare references that belong to the module being verified.
            WalkModule(updated.ModuleSymbols[module].Ast, expression =>
            {
                if (!updated.ExpressionSymbols.TryGetValue(expression, out var symbol)) return;
                SourceSpan position;
                if (expression is NameExpression name) position = name.Span;
                else if (expression is AttributeExpression attribute)
                {
                    var token = newTokens.LastOrDefault(t => t.Kind == TokenKind.Identifier &&
                        t.Span.End == attribute.Span.End && t.Text == attribute.Attribute);
                    if (token.Kind != TokenKind.Identifier) return;
                    position = token.Span;
                }
                else return;
                actual[position.Start] = symbol;
            }, targetNode =>
            {
                if (updated.ForTargetSymbols.TryGetValue(targetNode, out var symbol))
                    actual[targetNode.VariableSpan.Start] = symbol;
            });
            foreach (var originalTrace in original.Traces.Values.Where(t => t.Module == module))
            {
                int at = originalTrace.Start;
                if (edits.TryGetValue(module, out var changes))
                    foreach (var edit in changes)
                        if (edit.Span.Start < originalTrace.Start)
                            at += newName.Length - edit.Span.Length;
                if (!actual.TryGetValue(at, out var symbol) || symbol.Kind != originalTrace.Kind)
                    return false;
                string expected = originalTrace.QualifiedName;
                if (expected == target.Identity)
                    expected = target.Kind == SymbolKind.Global ? "shared." + newName :
                        target.Identity.Substring(0, target.Identity.LastIndexOf('.') + 1) + newName;
                else if (target.Kind == SymbolKind.Function &&
                    (originalTrace.Kind == SymbolKind.Parameter || originalTrace.Kind == SymbolKind.Local) &&
                    expected.StartsWith(target.Identity + ".", StringComparison.Ordinal))
                    expected = target.Identity.Substring(0, target.Identity.LastIndexOf('.') + 1) +
                        newName + expected.Substring(target.Identity.Length);
                if (symbol.QualifiedName != expected) return false;
            }
            return true;
        }

        private static string ApplyEditsForVerification(string source,
            Dictionary<string, Edit[]> edits, string module, string newName)
        {
            if (!edits.TryGetValue(module, out var modifications)) return source;
            foreach (var entry in modifications.OrderByDescending(e => e.Span.Start))
                source = source.Remove(entry.Span.Start, entry.Span.Length).Insert(entry.Span.Start, newName);
            return source;
        }

        private static string Normalize(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        private static string ToPath(string module) =>
            module.Replace('.', '/') + FileSystemScriptStorage.ScriptExtension;

        private static bool ValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || !DScriptModuleLoader.IsValidName(name) || name.Contains("."))
                return false;
            var lexer = new DScriptLexer("rename", name);
            var tokens = lexer.Tokenize();
            return lexer.Diagnostics.Count == 0 && tokens.Count > 0 &&
                tokens[0].Kind == TokenKind.Identifier && tokens[0].Text == name;
        }
    }
}
