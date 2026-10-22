using System;
using System.Collections.Generic;
using DSCompilation.DScript.Ast;

namespace DSCompilation.DScript.Semantics
{
    public enum SymbolKind { Parameter, Local, Global, Function, NativeFunction, NativeConstant, Module }

    /// <summary>Resolved, stable identity of a function, shared slot, import or native symbol.</summary>
    public sealed class DScriptSymbol
    {
        public string Name { get; }
        public string QualifiedName { get; }
        public SymbolKind Kind { get; }
        public int Slot { get; internal set; } = -1;
        public int ParameterCount { get; }
        /// <summary>Collision-free DIL identifier for a program-wide shared slot.</summary>
        public string DILGlobalName => Kind == SymbolKind.Global ? DScriptNameMangler.Global(QualifiedName) : null;
        public DScriptSymbol(string name, string qualifiedName, SymbolKind kind, int parameters = -1)
        { Name = name; QualifiedName = qualifiedName; Kind = kind; ParameterCount = parameters; }
        public override string ToString() => Kind + " " + QualifiedName + (Slot < 0 ? "" : " @" + Slot);
    }

    public sealed class DScriptModuleSymbols
    {
        internal readonly Dictionary<string, DScriptSymbol> ExportsInternal = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        internal readonly Dictionary<string, DScriptSymbol> ImportsInternal = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        internal readonly Dictionary<string, DScriptSymbol> FunctionsInternal = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        internal readonly Dictionary<string, DScriptSymbol> GlobalsInternal = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        internal readonly HashSet<string> ImportedPaths = new HashSet<string>(StringComparer.Ordinal);
        public string Name { get; }
        public ModuleNode Ast { get; }
        public IReadOnlyDictionary<string, DScriptSymbol> Exports => ExportsInternal;
        public IReadOnlyDictionary<string, DScriptSymbol> Imports => ImportsInternal;
        public IReadOnlyDictionary<string, DScriptSymbol> Functions => FunctionsInternal;
        public IReadOnlyDictionary<string, DScriptSymbol> Globals => GlobalsInternal;
        internal DScriptModuleSymbols(string name, ModuleNode ast) { Name = name; Ast = ast; }
    }

    public sealed class DScriptFunctionSymbols
    {
        public FunctionStatement Ast { get; }
        public DScriptModuleSymbols Module { get; }
        internal readonly Dictionary<string, DScriptSymbol> LocalsInternal = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, DScriptSymbol> Locals => LocalsInternal;
        public int LocalCount => LocalsInternal.Count;
        internal DScriptFunctionSymbols(FunctionStatement ast, DScriptModuleSymbols module)
        { Ast = ast; Module = module; }
    }

    /// <summary>
    /// A single ordered, independently recoverable shared initializer.
    /// The backend must compile each step as an isolated invocation so that a failed initializer
    /// can log an error without preventing subsequent initializers or normal VMs from running.
    /// </summary>
    public sealed class DScriptSharedInitializer
    {
        public int Order { get; }
        public string Module { get; }
        public SharedStatement Declaration { get; }
        public DScriptSymbol Symbol { get; }
        public Expression Expression => Declaration.Initializer;
        public SourceSpan Span => Declaration.Span;

        internal DScriptSharedInitializer(int order, string module, SharedStatement declaration, DScriptSymbol symbol)
        { Order = order; Module = module; Declaration = declaration; Symbol = symbol; }
    }

    /// <summary>
    /// Semantic side tables preserve the original AST; the backend will consume these bindings,
    /// already-assigned local/global slots, and source spans to emit DIL + debug metadata.
    /// </summary>
    public sealed class DScriptAnalysis
    {
        public string EntryModule { get; }
        public IReadOnlyDictionary<string, ModuleNode> Modules { get; }
        public IReadOnlyDictionary<string, DScriptModuleSymbols> ModuleSymbols { get; }
        public IReadOnlyDictionary<FunctionStatement, DScriptFunctionSymbols> FunctionSymbols { get; }
        public IReadOnlyDictionary<Expression, DScriptSymbol> ExpressionSymbols { get; }
        public IReadOnlyDictionary<ForStatement, DScriptSymbol> ForTargetSymbols { get; }
        public IReadOnlyList<DScriptSymbol> GlobalSlots { get; }
        /// <summary>Every declaration points at the canonical program-wide shared symbol.</summary>
        public IReadOnlyDictionary<SharedStatement, DScriptSymbol> SharedDeclarations { get; }
        /// <summary>Initialization steps in deterministic import/source order, one step per variable.</summary>
        public IReadOnlyList<DScriptSharedInitializer> SharedInitializers { get; }
        public IReadOnlyList<string> DependenciesFirst { get; }
        public IReadOnlyList<DScriptDiagnostic> Diagnostics { get; }
        public bool Success => Diagnostics.Count == 0;

        internal DScriptAnalysis(string entry, IReadOnlyDictionary<string, ModuleNode> modules,
            IReadOnlyDictionary<string, DScriptModuleSymbols> moduleSymbols,
            IReadOnlyDictionary<FunctionStatement, DScriptFunctionSymbols> functionSymbols,
            IReadOnlyDictionary<Expression, DScriptSymbol> expressions,
            IReadOnlyDictionary<ForStatement, DScriptSymbol> targets,
            IReadOnlyList<DScriptSymbol> globals,
            IReadOnlyDictionary<SharedStatement, DScriptSymbol> sharedDeclarations,
            IReadOnlyList<DScriptSharedInitializer> sharedInitializers,
            IReadOnlyList<string> order, IReadOnlyList<DScriptDiagnostic> diagnostics)
        {
            EntryModule = entry; Modules = modules; ModuleSymbols = moduleSymbols;
            FunctionSymbols = functionSymbols; ExpressionSymbols = expressions;
            ForTargetSymbols = targets; GlobalSlots = globals;
            SharedDeclarations = sharedDeclarations; SharedInitializers = sharedInitializers;
            DependenciesFirst = order; Diagnostics = diagnostics;
        }
    }
}
