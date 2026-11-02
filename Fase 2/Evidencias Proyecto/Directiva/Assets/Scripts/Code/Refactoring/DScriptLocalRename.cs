using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;
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
            // CodeEditorView normalizes line endings before calculating cursor offsets.
            original = NormalizeNewlines(original);
            string updated = original;
            // Descending character offsets avoid invalidating the remaining source spans.
            foreach (var span in snapshot.Occurrences.OrderByDescending(s => s.Start))
                updated = updated.Remove(span.Start, span.Length).Insert(span.Start, name);

            // The rename must leave the original program semantically valid. A failed
            // reanalysis aborts without changing a single byte in the editor.
            var reanalysis = DScriptFrontend.Analyze(module,
                new OverriddenSource(provider, module, updated));
            if (!reanalysis.Success)
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
            var analysis = DScriptFrontend.Analyze(module, effective);
            if (!analysis.Success)
            {
                error = "rename_fix_errors";
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
                Occurrences = spans, Selected = selected.Span
            };
            return true;
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
