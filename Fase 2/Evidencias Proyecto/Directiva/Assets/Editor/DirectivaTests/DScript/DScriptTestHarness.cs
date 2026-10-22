using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Parsing;
using DSCompilation.DScript.Semantics;

namespace Directiva.EditorTests.DScript
{
    /// <summary>Pure-source fixtures: no Play Mode, VM execution, scripts on disk or external tooling.</summary>
    internal static class DScriptTestHarness
    {
        internal static void Add(List<DirectivaTestCase> tests, string group, string id,
            string description, Action<DirectivaTestContext> action)
            => tests.Add(new DirectivaTestCase("DScript", group, id, description, action));

        internal static IReadOnlyList<DScriptToken> Lex(DirectivaTestContext t, string source,
            out IReadOnlyList<DScriptDiagnostic> diagnostics, DScriptFrontendOptions options = null)
        {
            t.Attach("test.dscript", source);
            var lexer = new DScriptLexer("test", source, options);
            var tokens = lexer.Tokenize();
            diagnostics = lexer.Diagnostics;
            t.Attach("Tokens", string.Join("\n", tokens.Select(token => token.ToString())));
            t.Attach("Diagnostics", string.Join("\n", diagnostics));
            return tokens;
        }

        internal static ModuleNode Parse(DirectivaTestContext t, string source,
            out IReadOnlyList<DScriptDiagnostic> diagnostics)
        {
            var tokens = Lex(t, source, out var lexErrors);
            var parser = new DScriptParser("test", tokens);
            var module = parser.Parse();
            diagnostics = lexErrors.Concat(parser.Diagnostics).ToArray();
            t.Attach("Syntax diagnostics", string.Join("\n", diagnostics));
            return module;
        }

        internal static DScriptAnalysis Analyze(DirectivaTestContext t, string source,
            Dictionary<string, string> additional = null, DScriptFrontendOptions options = null)
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["test"] = source };
            if (additional != null) foreach (var sourceModule in additional) sources.Add(sourceModule.Key, sourceModule.Value);
            foreach (var entry in sources.OrderBy(x => x.Key, StringComparer.Ordinal))
                t.Attach(entry.Key + ".dscript", entry.Value);
            var analysis = DScriptFrontend.Analyze("test", new InMemoryDScriptSourceProvider(sources), options);
            t.Attach("Frontend diagnostics", string.Join("\n", analysis.Diagnostics));
            t.Note("Modules: " + string.Join(",", analysis.Modules.Keys.OrderBy(x => x, StringComparer.Ordinal)));
            t.Note("Globals: " + string.Join(",", analysis.GlobalSlots.Select(s => s.ToString())));
            return analysis;
        }

        internal static DScriptAnalysis Ok(DirectivaTestContext t, string source,
            Dictionary<string, string> additional = null)
        {
            var analysis = Analyze(t, source, additional);
            t.Check(analysis.Success, "Expected successful analysis, got " + string.Join(" | ", analysis.Diagnostics));
            return analysis;
        }

        internal static void Error(DirectivaTestContext t, string source, string code,
            Dictionary<string, string> additional = null)
        {
            var analysis = Analyze(t, source, additional);
            t.Check(!analysis.Success, "Expected a frontend error: " + code);
            t.Check(analysis.Diagnostics.Any(d => d.Code == code), "Expected " + code + "; actual: " +
                string.Join("; ", analysis.Diagnostics));
        }

        internal static FunctionStatement Function(DScriptAnalysis analysis, string name, string module = "test")
            => analysis.Modules[module].Statements.OfType<FunctionStatement>().Single(f => f.Name == name);
    }
}
