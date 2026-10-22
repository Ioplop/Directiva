using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DSCompilation.DIL;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;
using DSExecution.VirtualMachine;

namespace DSCompilation.DScript.Backend
{
    /// <summary>A successful, immutable program: one CodeContext reusable by separate teams.</summary>
    public sealed class DScriptCompiledProgram
    {
        public string EntryModule { get; }
        public string BootstrapModule { get; }
        public CodeContext Code { get; }
        public IReadOnlyDictionary<string, string> DILModules { get; }
        public IReadOnlyDictionary<string, int> FunctionIds { get; }
        public IReadOnlyList<DScriptCompiledInitializer> Initializers { get; }
        public IReadOnlyList<DScriptSymbol> SharedSlots { get; }

        internal DScriptCompiledProgram(DScriptAnalysis analysis, string bootstrapModule,
            Dictionary<string, string> dil, CodeContext code,
            IReadOnlyList<DScriptCompiledInitializer> initializers)
        {
            EntryModule = analysis.EntryModule;
            BootstrapModule = bootstrapModule;
            Code = code;
            DILModules = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(dil, StringComparer.Ordinal));
            FunctionIds = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                code.Functions.ToDictionary(f => f.Name, f => f.Id, StringComparer.Ordinal));
            Initializers = initializers;
            SharedSlots = Array.AsReadOnly(analysis.GlobalSlots.ToArray());
        }

        /// <summary>Qualified function identifiers are module.name (not just name).</summary>
        public bool TryGetFunctionId(string qualifiedName, out int id)
            => FunctionIds.TryGetValue(qualifiedName, out id);
    }

    public sealed class DScriptCompiledInitializer
    {
        public int Order { get; }
        public string Module { get; }
        public string Variable { get; }
        public int SharedSlot { get; }
        public int FunctionId { get; }
        public SourceSpan Span { get; }

        internal DScriptCompiledInitializer(DScriptSharedInitializer step, int id)
        {
            Order = step.Order; Module = step.Module; Variable = step.Symbol.Name;
            SharedSlot = step.Symbol.Slot; FunctionId = id; Span = step.Span;
        }
    }

    public sealed class DScriptCompilationResult
    {
        public DScriptAnalysis Analysis { get; }
        public IReadOnlyList<DScriptDiagnostic> Diagnostics { get; }
        public DScriptCompiledProgram Program { get; }
        public bool Success => Program != null && Diagnostics.Count == 0;

        internal DScriptCompilationResult(DScriptAnalysis analysis,
            DScriptCompiledProgram program, IReadOnlyList<DScriptDiagnostic> diagnostics)
        { Analysis = analysis; Program = program; Diagnostics = diagnostics; }
    }

    /// <summary>
    /// DScript backend. Imports are compile-time only; all DIL modules are generated
    /// in memory, then parsed together into one CodeContext. No VM runs here.
    /// The host can persist DILModules later after a completely successful compilation.
    /// </summary>
    public static class DScriptCompiler
    {
        public static DScriptCompilationResult Compile(string entryModule,
            IDScriptSourceProvider provider, DScriptFrontendOptions options = null,
            int maxLocalSlots = 4096)
            => Compile(DScriptFrontend.Analyze(entryModule, provider, options), maxLocalSlots);

        public static DScriptCompilationResult Compile(DScriptAnalysis analysis,
            int maxLocalSlots = 4096)
        {
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            if (maxLocalSlots < 1) throw new ArgumentOutOfRangeException(nameof(maxLocalSlots));
            if (!analysis.Success)
                return new DScriptCompilationResult(analysis, null, analysis.Diagnostics);

            try
            {
                // Avoid colliding with an actual user module even if they choose the reserved-looking name.
                string bootstrap = "__dscript_bootstrap__";
                while (analysis.Modules.ContainsKey(bootstrap)) bootstrap += "_";
                var modules = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string module in analysis.Modules.Keys.OrderBy(m => m, StringComparer.Ordinal))
                {
                    var source = new StringBuilder();
                    // Native imports must remain visible to DIL's native symbol linker.
                    foreach (string imported in ImportNames(analysis.Modules[module]))
                        source.Append("# IMPORT : ").Append(imported).Append('\n');
                    foreach (FunctionStatement function in analysis.Modules[module].Statements.OfType<FunctionStatement>())
                    {
                        if (!analysis.FunctionSymbols.TryGetValue(function, out DScriptFunctionSymbols symbols))
                            throw new DScriptBackendException(module, function.Span, "Missing function semantic table.");
                        source.Append(new DScriptDILEmitter(analysis, module, symbols.LocalCount, maxLocalSlots)
                            .EmitFunction(function, symbols));
                    }
                    modules.Add(module, source.ToString());
                }

                // Every shared declaration across all modules has ONE canonical slot.
                // Global annotations occur only here, in semantic slot order, so the
                // DIL parser's independent assignment cannot diverge.
                var bootstrapSource = new StringBuilder();
                foreach (string module in analysis.Modules.Keys.OrderBy(m => m, StringComparer.Ordinal))
                    bootstrapSource.Append("# IMPORT : ").Append(module).Append('\n');
                foreach (DScriptSymbol shared in analysis.GlobalSlots)
                    bootstrapSource.Append("# GLOBAL : ").Append(shared.DILGlobalName).Append('\n');

                // The default entry is an inert function. A host MUST select a real user
                // function explicitly and run initializers once before starting units.
                bootstrapSource.Append("# FUNCTION : __entry\n# PARAMS : 0\n# LOCALS : 0\n")
                    .Append("PUSH NONE\nRETURN\n# END_FUNCTION\n");
                var initializerNames = new List<string>();
                foreach (DScriptSharedInitializer step in analysis.SharedInitializers)
                {
                    string functionName = "__init_" + step.Order.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
                    initializerNames.Add(bootstrap + "." + functionName);
                    bootstrapSource.Append(new DScriptDILEmitter(analysis, step.Module, 0, maxLocalSlots)
                        .EmitInitializer(functionName, step));
                }
                modules.Add(bootstrap, bootstrapSource.ToString());
                var provider = new InMemoryDILSourceProvider(modules);
                // Explicit source maps must not be overwritten by auto-generated DIL positions.
                CodeContext code = new DILParser(provider, new DILParserOptions(generateDebugMetadata: false))
                    .Parse(bootstrap);
                var ids = code.Functions.ToDictionary(f => f.Name, f => f.Id, StringComparer.Ordinal);
                var initializers = new List<DScriptCompiledInitializer>();
                for (int i = 0; i < initializerNames.Count; i++)
                    initializers.Add(new DScriptCompiledInitializer(analysis.SharedInitializers[i], ids[initializerNames[i]]));
                return new DScriptCompilationResult(analysis,
                    new DScriptCompiledProgram(analysis, bootstrap, modules, code,
                        initializers.AsReadOnly()), Array.Empty<DScriptDiagnostic>());
            }
            catch (DScriptBackendException ex)
            {
                return Error(analysis, ex.Module, ex.Span, "DS5001", ex.Message);
            }
            catch (DILParseException ex)
            {
                return Error(analysis, ex.ModuleName, new SourceSpan(0, 0, 1, 1),
                    "DS5002", "Generated DIL is invalid: " + ex.Message);
            }
            catch (OverflowException ex)
            {
                return Error(analysis, analysis.EntryModule, new SourceSpan(0, 0, 1, 1),
                    "DS5003", "Compilation exceeded numeric limits: " + ex.Message);
            }
        }

        private static DScriptCompilationResult Error(DScriptAnalysis analysis, string module,
            SourceSpan span, string code, string message)
            => new DScriptCompilationResult(analysis, null,
                new[] { new DScriptDiagnostic(module, code, message, span) });

        private static IEnumerable<string> ImportNames(ModuleNode ast)
        {
            var imports = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Statement statement in ast.Statements)
            {
                if (statement is ImportStatement import) imports.Add(import.ModuleName);
                else if (statement is FromImportStatement from) imports.Add(from.ModuleName);
            }
            return imports;
        }

        private sealed class InMemoryDILSourceProvider : IDILSourceProvider
        {
            private readonly IReadOnlyDictionary<string, string> modules;
            internal InMemoryDILSourceProvider(IReadOnlyDictionary<string, string> modules)
            { this.modules = modules; }
            public bool TryReadModule(string name, out string source)
                => modules.TryGetValue(name, out source);
        }
    }
}
