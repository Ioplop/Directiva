using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Parsing;
using DSExecution.Native;

namespace DSCompilation.DScript.Modules
{
    /// <summary>Deterministic, cycle-safe module discovery; imports never execute code.</summary>
    public sealed class DScriptModuleLoader
    {
        private readonly IDScriptSourceProvider provider;
        private readonly DScriptFrontendOptions options;
        private readonly Dictionary<string, ModuleNode> modules = new Dictionary<string, ModuleNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> states = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<DScriptDiagnostic> diagnostics = new List<DScriptDiagnostic>();
        private readonly List<string> order = new List<string>();

        public IReadOnlyDictionary<string, ModuleNode> Modules => modules;
        public IReadOnlyList<string> DependenciesFirst => order;
        public IReadOnlyList<DScriptDiagnostic> Diagnostics => diagnostics;

        public DScriptModuleLoader(IDScriptSourceProvider provider, DScriptFrontendOptions options = null)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.options = options ?? new DScriptFrontendOptions();
        }

        public void Load(string entryModule)
        {
            modules.Clear(); states.Clear(); diagnostics.Clear(); order.Clear();
            if (!IsValidName(entryModule))
            {
                Error(entryModule, "DS3000", "Invalid entry module name.", new SourceSpan(0, 0, 1, 1));
                return;
            }
            if (NativeLibrary.IsNativeModule(entryModule))
            {
                Error(entryModule, "DS3002", "Entry module cannot be a reserved native module.", new SourceSpan(0, 0, 1, 1));
                return;
            }
            Visit(entryModule, entryModule, new SourceSpan(0, 0, 1, 1));
        }

        private void Visit(string name, string importer, SourceSpan location)
        {
            if (NativeLibrary.IsNativeModule(name)) return;
            if (states.TryGetValue(name, out int state))
            {
                // Both in-progress and completed modules are already registered.
                // Cycles are legal: binding happens after every reachable module is parsed.
                return;
            }
            if (modules.Count >= options.MaxModules)
            {
                Error(importer, "DS3004", "Maximum imported module count exceeded.", location);
                return;
            }
            states.Add(name, 1);
            string source;
            try
            {
                if (!provider.TryReadModule(name, out source) || source == null)
                {
                    Error(importer, "DS3001", "Module '" + name + "' was not found.", location);
                    states[name] = 2;
                    return;
                }
            }
            catch (Exception ex)
            {
                Error(importer, "DS3005", "Cannot read module '" + name + "': " + ex.Message, location);
                states[name] = 2;
                return;
            }
            var lexer = new DScriptLexer(name, source, options);
            var tokens = lexer.Tokenize();
            diagnostics.AddRange(lexer.Diagnostics);
            var parser = new DScriptParser(name, tokens, options);
            var ast = parser.Parse();
            diagnostics.AddRange(parser.Diagnostics);
            modules[name] = ast;
            foreach (Statement statement in ast.Statements)
            {
                string target = statement is ImportStatement import ? import.ModuleName :
                    statement is FromImportStatement from ? from.ModuleName : null;
                if (target != null) Visit(target, name, statement.Span);
            }
            states[name] = 2;
            order.Add(name);
        }

        private void Error(string module, string code, string message, SourceSpan span)
        {
            if (diagnostics.Count < options.MaxDiagnostics)
                diagnostics.Add(new DScriptDiagnostic(module, code, message, span));
        }

        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string[] parts = name.Split('.');
            foreach (string part in parts)
            {
                if (part.Length == 0 || !(part[0] == '_' || AsciiLetter(part[0]))) return false;
                for (int i = 1; i < part.Length; i++)
                    if (!(AsciiLetter(part[i]) || part[i] == '_' || part[i] >= '0' && part[i] <= '9')) return false;
            }
            return true;
        }
        private static bool AsciiLetter(char c) => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
    }
}
