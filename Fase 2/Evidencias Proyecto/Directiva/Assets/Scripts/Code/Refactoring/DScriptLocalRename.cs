using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;
using DOrchestration;
using DSExecution.Native;

namespace Directiva.CodeUI
{
    /// <summary>
    /// First stage of semantic rename: local variables and parameters. Only source spans
    /// resolved to the SAME binder symbol are replaced; textual matches are never used
    /// to discover references. This deliberately refuses function/shared renames until
    /// those can be planned atomically across the entire workspace.
    /// </summary>
    public static class DScriptLocalRename
    {
        public sealed class Result
        {
            public string OldName { get; }
            public string NewName { get; }
            public string Content { get; }
            public int Count { get; }
            public int Caret { get; }

            internal Result(string oldName, string newName, string content, int count, int caret)
            { OldName = oldName; NewName = newName; Content = content; Count = count; Caret = caret; }
        }

        private sealed class Occurrence
        {
            public DScriptSymbol Symbol;
            public SourceSpan Span;
        }

        private sealed class Snapshot
        {
            public DScriptSymbol Symbol;
            public DScriptFunctionSymbols Function;
            public List<SourceSpan> Occurrences;
            public SourceSpan Selected;
            public DScriptAnalysis Analysis;
        }

        private sealed class OverriddenSource : IDScriptSourceProvider
        {
            private readonly IDScriptSourceProvider source;
            private readonly string module;
            private readonly string content;

            public OverriddenSource(IDScriptSourceProvider source, string module, string content)
            { this.source = source; this.module = module; this.content = content; }

            public bool TryReadModule(string name, out string value)
            {
                if (name == module) { value = content; return true; }
                return source.TryReadModule(name, out value);
            }
        }

        public static bool TryFind(string module, IDScriptSourceProvider provider, int caret,
            out string oldName, out string error)
        {
            oldName = null;
            if (!TrySnapshot(module, provider, caret, out var snapshot, out error)) return false;
            oldName = snapshot.Symbol.Name;
            return true;
        }

        public static bool TryRename(string module, IDScriptSourceProvider provider, int caret,
            string proposedName, out Result result, out string error)
        {
            result = null;
            if (!TrySnapshot(module, provider, caret, out var snapshot, out error)) return false;
            var target = snapshot.Symbol;
            string name = (proposedName ?? string.Empty).Trim();
            if (!ValidIdentifier(name))
            {
                error = "rename_invalid_identifier";
                return false;
            }

            if (name == target.Name)
            {
                if (!provider.TryReadModule(module, out var unmodified))
                {
                    error = "rename_unavailable_module";
                    return false;
                }
                result = new Result(target.Name, name, NormalizeNewlines(unmodified), 0, caret);
                error = null;
                return true;
            }
            if (snapshot.Function.Locals.TryGetValue(name, out var existing) &&
                !ReferenceEquals(existing, target))
            {
                error = "rename_collision_local";
                return false;
            }
            var owner = snapshot.Function.Module;
            if (owner.Globals.ContainsKey(name) || owner.Functions.ContainsKey(name) ||
                owner.Imports.ContainsKey(name) ||
                NativeLibrary.GetFunctionNames(string.Empty).Contains(name))
            {
                error = "rename_collision_scope";
                return false;
            }

            if (!provider.TryReadModule(module, out var original))
            {
                error = "rename_unavailable_module";
                return false;
            }
            if (!HasCompleteLocalCoverage(snapshot, module, NormalizeNewlines(original), name))
            {
                error = "rename_incomplete_analysis";
                return false;
            }
            // CodeEditorView normalizes line endings before calculating cursor offsets.
            original = NormalizeNewlines(original);
            string updated = original;
            // Descending character offsets avoid invalidating the remaining source spans.
            foreach (var span in snapshot.Occurrences.OrderByDescending(s => s.Start))
                updated = updated.Remove(span.Start, span.Length).Insert(span.Start, name);

            // Existing errors outside the renamed symbol are not a reason to reject
            // a refactor. Rebind the recovered AST, but ensure no new diagnostic or
            // extra reference to the renamed local appears after the change.
            var reanalysis = DScriptFrontend.AnalyzeForRefactoring(module,
                new OverriddenSource(provider, module, updated), SimulationDScriptProfile.Options);
            if (!reanalysis.ModuleSymbols.ContainsKey(module) ||
                DScriptRenameDiagnostics.IntroducesErrors(snapshot.Analysis, reanalysis,
                    (diagnosticModule, position) => diagnosticModule == module
                        ? DScriptRenameDiagnostics.MapPosition(position, snapshot.Occurrences, name.Length)
                        : position) ||
                !VerifyRenamedLocal(snapshot, reanalysis, module, name))
            {
                error = "rename_result_invalid";
                return false;
            }

            int newCaret = snapshot.Selected.Start + name.Length;
            foreach (var span in snapshot.Occurrences)
                if (span.Start < snapshot.Selected.Start)
                    newCaret += name.Length - span.Length;

            result = new Result(target.Name, name, updated, snapshot.Occurrences.Count, newCaret);
            error = null;
            return true;
        }

        private static bool TrySnapshot(string module, IDScriptSourceProvider provider, int caret,
            out Snapshot result, out string error)
        {
            result = null;
            error = null;
            if (provider == null || !provider.TryReadModule(module, out string text) || text == null)
            {
                error = "rename_unavailable_module";
                return false;
            }
            text = NormalizeNewlines(text);
            var effective = new OverriddenSource(provider, module, text);
            var analysis = DScriptFrontend.AnalyzeForRefactoring(module, effective,
                SimulationDScriptProfile.Options);
            if (!analysis.ModuleSymbols.ContainsKey(module))
            {
                error = "rename_incomplete_analysis";
                return false;
            }
            var functions = analysis.FunctionSymbols
                .Where(pair => pair.Value.Module.Name == module)
                .Select(pair => pair.Value).ToArray();
            var eligible = new Dictionary<DScriptSymbol, DScriptFunctionSymbols>();
            var occurrences = new List<Occurrence>();
            foreach (var table in functions)
                foreach (var symbol in table.Locals.Values)
                    eligible[symbol] = table;

            foreach (var binding in analysis.ExpressionSymbols)
            {
                if (binding.Key is NameExpression name && eligible.ContainsKey(binding.Value))
                    occurrences.Add(new Occurrence { Symbol = binding.Value, Span = name.Span });
            }
            foreach (var target in analysis.ForTargetSymbols)
                if (eligible.ContainsKey(target.Value))
                    occurrences.Add(new Occurrence { Symbol = target.Value, Span = target.Key.VariableSpan });

            // Parameter declarations have no separate AST NameExpression yet. Read only
            // identifier tokens in each function signature; their identities come from
            // the bound function's local symbol table, not the token text alone.
            var tokens = new DScriptLexer(module, text).Tokenize();
            foreach (var table in functions)
            {
                var function = table.Ast;
                int index = -1;
                for (int i = 0; i < tokens.Count; i++)
                    if (tokens[i].Kind == TokenKind.Def &&
                        tokens[i].Span.Start == function.Span.Start) { index = i; break; }
                if (index < 0 || index + 2 >= tokens.Count ||
                    tokens[index + 2].Kind != TokenKind.LeftParen) continue;
                for (int i = index + 3; i < tokens.Count &&
                    tokens[i].Kind != TokenKind.RightParen; i++)
                {
                    var token = tokens[i];
                    if (token.Kind == TokenKind.Identifier &&
                        table.Locals.TryGetValue(token.Text, out var symbol) &&
                        symbol.Kind == SymbolKind.Parameter)
                        occurrences.Add(new Occurrence { Symbol = symbol, Span = token.Span });
                }
            }

            Occurrence selected = null;
            foreach (var entry in occurrences)
                if (entry.Span.Start <= caret && caret <= entry.Span.End &&
                    (selected == null || entry.Span.Start > selected.Span.Start))
                    selected = entry;
            if (selected == null)
            {
                error = "rename_only_local";
                return false;
            }
            var spans = occurrences.Where(entry => ReferenceEquals(entry.Symbol, selected.Symbol))
                .Select(entry => entry.Span)
                .GroupBy(span => span.Start).Select(group => group.First())
                .OrderBy(span => span.Start).ToList();
            if (spans.Count == 0 || spans.Any(span => span.Start < 0 ||
                span.End > text.Length || text.Substring(span.Start, span.Length) != selected.Symbol.Name))
            {
                error = "rename_stale_source";
                return false;
            }
            result = new Snapshot
            {
                Symbol = selected.Symbol, Function = eligible[selected.Symbol],
                Occurrences = spans, Selected = selected.Span, Analysis = analysis
            };
            return true;
        }

        private static bool HasCompleteLocalCoverage(Snapshot snapshot, string module,
            string source, string newName)
        {
            if (!snapshot.Analysis.Diagnostics.Any(d => d.Module == module &&
                (d.Code.StartsWith("DS1", StringComparison.Ordinal) ||
                 d.Code.StartsWith("DS2", StringComparison.Ordinal)))) return true;
            var statements = snapshot.Function.Module.Ast.Statements;
            int start = snapshot.Function.Ast.Span.Start;
            int end = source.Length;
            foreach (var statement in statements)
                if (statement.Span.Start > start && statement.Span.Start < end)
                    end = statement.Span.Start;
            var known = new HashSet<int>(snapshot.Occurrences.Select(span => span.Start));
            foreach (var token in new DScriptLexer(module, source).Tokenize())
                if (token.Kind == TokenKind.Identifier && token.Span.Start >= start &&
                    token.Span.Start < end &&
                    (token.Text == snapshot.Symbol.Name || token.Text == newName) &&
                    !known.Contains(token.Span.Start))
                    return false;
            return true;
        }

        private static bool VerifyRenamedLocal(Snapshot before, DScriptAnalysis after,
            string module, string newName)
        {
            string functionName = before.Function.Ast.Name;
            int functionAt = DScriptRenameDiagnostics.MapPosition(before.Function.Ast.Span.Start,
                before.Occurrences, newName.Length);
            var updatedFunction = after.FunctionSymbols.Values.FirstOrDefault(table =>
                table.Module.Name == module && table.Ast.Name == functionName &&
                table.Ast.Span.Start == functionAt);
            if (updatedFunction == null ||
                !updatedFunction.Locals.TryGetValue(newName, out var renamed) ||
                renamed.Kind != before.Symbol.Kind) return false;

            // Parameter declarations are tokens, not NameExpression bindings. All
            // *uses*, including for-loop targets and assignments, must have precisely
            // the same semantic identity after the rename. No previously unresolved
            // expression may be captured by the newly introduced spelling.
            string oldIdentity = before.Symbol.QualifiedName;
            var expected = new HashSet<int>();
            foreach (var pair in before.Analysis.ExpressionSymbols)
                if (pair.Key is NameExpression n && pair.Value.QualifiedName == oldIdentity)
                    expected.Add(DScriptRenameDiagnostics.MapPosition(n.Span.Start,
                        before.Occurrences, newName.Length));
            foreach (var pair in before.Analysis.ForTargetSymbols)
                if (pair.Value.QualifiedName == oldIdentity)
                    expected.Add(DScriptRenameDiagnostics.MapPosition(pair.Key.VariableSpan.Start,
                        before.Occurrences, newName.Length));

            var actual = new HashSet<int>();
            foreach (var pair in after.ExpressionSymbols)
                if (pair.Key is NameExpression n && pair.Value.QualifiedName == renamed.QualifiedName)
                    actual.Add(n.Span.Start);
            foreach (var pair in after.ForTargetSymbols)
                if (pair.Value.QualifiedName == renamed.QualifiedName)
                    actual.Add(pair.Key.VariableSpan.Start);
            return expected.SetEquals(actual);
        }

        private static string NormalizeNewlines(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        private static bool ValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || !DScriptModuleLoader.IsValidName(name) ||
                name.IndexOf('.') >= 0) return false;
            var lexer = new DScriptLexer("rename", name);
            var tokens = lexer.Tokenize();
            return lexer.Diagnostics.Count == 0 && tokens.Count > 0 &&
                tokens[0].Kind == TokenKind.Identifier && tokens[0].Text == name;
        }
    }
}
