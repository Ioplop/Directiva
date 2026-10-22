using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;

namespace DSCompilation.DScript
{
    /// <summary>
    /// Phase 1 entry point: DScript source -> AST -> module graph -> bound symbols.
    /// No DIL generation, simulation side effects or VM execution occurs here.
    /// </summary>
    public static class DScriptFrontend
    {
        public static DScriptAnalysis Analyze(string entryModule, IDScriptSourceProvider sourceProvider,
            DScriptFrontendOptions options = null)
        {
            if (sourceProvider == null) throw new ArgumentNullException(nameof(sourceProvider));
            options = options ?? new DScriptFrontendOptions();
            var loader = new DScriptModuleLoader(sourceProvider, options);
            loader.Load(entryModule);
            var errors = new List<DScriptDiagnostic>(loader.Diagnostics);
            var moduleSymbols = new Dictionary<string, DScriptModuleSymbols>(StringComparer.Ordinal);
            var functions = new Dictionary<DSCompilation.DScript.Ast.FunctionStatement, DScriptFunctionSymbols>();
            var expressions = new Dictionary<DSCompilation.DScript.Ast.Expression, DScriptSymbol>();
            var targets = new Dictionary<DSCompilation.DScript.Ast.ForStatement, DScriptSymbol>();
            var globals = new List<DScriptSymbol>();
            var sharedDeclarations = new Dictionary<DSCompilation.DScript.Ast.SharedStatement, DScriptSymbol>();
            var sharedInitializers = new List<DScriptSharedInitializer>();
            if (errors.Count == 0)
            {
                var binder = new DScriptBinder(loader.Modules, options);
                binder.Bind();
                errors.AddRange(binder.Diagnostics);
                foreach (var item in binder.Modules) moduleSymbols[item.Key] = item.Value;
                foreach (var item in binder.Functions) functions[item.Key] = item.Value;
                foreach (var item in binder.Bindings) expressions[item.Key] = item.Value;
                foreach (var item in binder.ForTargets) targets[item.Key] = item.Value;
                globals.AddRange(binder.GlobalSlots);
                foreach (var item in binder.SharedDeclarations) sharedDeclarations[item.Key] = item.Value;
                sharedInitializers.AddRange(binder.BuildSharedInitializationPlan(entryModule));
            }
            // Stable order across machines and filesystem enumeration; diagnostics still retain source locations.
            errors.Sort((a, b) =>
            {
                int cmp = string.CompareOrdinal(a.Module, b.Module);
                if (cmp == 0) cmp = a.Span.Start.CompareTo(b.Span.Start);
                if (cmp == 0) cmp = string.CompareOrdinal(a.Code, b.Code);
                return cmp;
            });
            if (errors.Count > options.MaxDiagnostics) errors.RemoveRange(options.MaxDiagnostics, errors.Count - options.MaxDiagnostics);
            return new DScriptAnalysis(entryModule, loader.Modules, moduleSymbols, functions, expressions,
                targets, globals, sharedDeclarations, sharedInitializers, loader.DependenciesFirst, errors);
        }
    }
}
