using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Modules;
using DSExecution.Native;

namespace DSCompilation.DScript.Semantics
{
    /// <summary>Multi-pass static name resolver; deliberately does not infer runtime data types.</summary>
    public sealed class DScriptBinder
    {
        private readonly IReadOnlyDictionary<string, ModuleNode> nodes;
        private readonly DScriptFrontendOptions options;
        private readonly Dictionary<string, DScriptModuleSymbols> modules =
            new Dictionary<string, DScriptModuleSymbols>(StringComparer.Ordinal);
        private readonly Dictionary<FunctionStatement, DScriptFunctionSymbols> functions =
            new Dictionary<FunctionStatement, DScriptFunctionSymbols>();
        private readonly Dictionary<Expression, DScriptSymbol> bindings = new Dictionary<Expression, DScriptSymbol>();
        private readonly Dictionary<ForStatement, DScriptSymbol> forTargets = new Dictionary<ForStatement, DScriptSymbol>();
        private readonly List<DScriptSymbol> globalSlots = new List<DScriptSymbol>();
        private readonly Dictionary<string, DScriptSymbol> sharedByName =
            new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        private readonly Dictionary<SharedStatement, DScriptSymbol> sharedDeclarations =
            new Dictionary<SharedStatement, DScriptSymbol>();
        private readonly Dictionary<string, SharedStatement> firstInitializer =
            new Dictionary<string, SharedStatement>(StringComparer.Ordinal);
        private readonly List<DScriptDiagnostic> errors = new List<DScriptDiagnostic>();
        private readonly Dictionary<string, DScriptSymbol> builtins = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
        private DScriptModuleSymbols currentModule;
        private DScriptFunctionSymbols currentFunction;
        private int loopDepth;
        private int walkDepth;

        public IReadOnlyList<DScriptDiagnostic> Diagnostics => errors;
        public IReadOnlyDictionary<string, DScriptModuleSymbols> Modules => modules;
        public IReadOnlyDictionary<FunctionStatement, DScriptFunctionSymbols> Functions => functions;
        public IReadOnlyDictionary<Expression, DScriptSymbol> Bindings => bindings;
        public IReadOnlyDictionary<ForStatement, DScriptSymbol> ForTargets => forTargets;
        public IReadOnlyList<DScriptSymbol> GlobalSlots => globalSlots;
        public IReadOnlyDictionary<SharedStatement, DScriptSymbol> SharedDeclarations => sharedDeclarations;

        public DScriptBinder(IReadOnlyDictionary<string, ModuleNode> parsed, DScriptFrontendOptions options = null)
        {
            nodes = parsed ?? throw new ArgumentNullException(nameof(parsed));
            this.options = options ?? new DScriptFrontendOptions();
            foreach (var qname in NativeLibrary.GetFunctionNames(string.Empty))
                builtins.Add(qname, new DScriptSymbol(qname, qname, SymbolKind.NativeFunction));
        }

        public void Bind()
        {
            errors.Clear(); modules.Clear(); functions.Clear(); bindings.Clear(); forTargets.Clear(); globalSlots.Clear();
            sharedByName.Clear(); sharedDeclarations.Clear(); firstInitializer.Clear();
            // Declare every module's exports before processing imports or function bodies.
            foreach (var entry in nodes.OrderBy(p => p.Key, StringComparer.Ordinal))
                modules.Add(entry.Key, new DScriptModuleSymbols(entry.Key, entry.Value));
            foreach (var module in modules.Values)
                Declare(module);
            // Exactly one shared storage slot per name, regardless of how many modules declare it.
            foreach (var symbol in sharedByName.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                symbol.Slot = globalSlots.Count;
                globalSlots.Add(symbol);
            }
            foreach (var module in modules.Values)
                RegisterImports(module);
            foreach (var module in modules.Values)
            {
                currentModule = module;
                currentFunction = null;
                foreach (var statement in module.Ast.Statements)
                {
                    if (statement is SharedStatement shared && shared.Initializer != null)
                        VisitExpression(shared.Initializer);
                    else if (statement is FunctionStatement function)
                        BindFunction(function, module);
                    else if (!(statement is ImportStatement || statement is FromImportStatement ||
                        statement is SharedStatement))
                        Error("DS4001", "Only imports, function declarations and shared declarations are allowed at module scope.", statement.Span);
                }
            }
            currentFunction = null;
            currentModule = null;
        }

        private void Declare(DScriptModuleSymbols module)
        {
            foreach (var statement in module.Ast.Statements)
            {
                if (statement is FunctionStatement function)
                {
                    if (module.ExportsInternal.ContainsKey(function.Name))
                        Error(module.Name, "DS4002", "Duplicate module symbol '" + function.Name + "'.", statement.Span);
                    else
                    {
                        var symbol = new DScriptSymbol(function.Name, module.Name + "." + function.Name,
                            SymbolKind.Function, function.Parameters.Count);
                        module.ExportsInternal.Add(function.Name, symbol);
                        module.FunctionsInternal.Add(function.Name, symbol);
                    }
                }
                else if (statement is SharedStatement declaration)
                    DeclareShared(module, declaration);
            }
        }

        private void DeclareShared(DScriptModuleSymbols module, SharedStatement declaration)
        {
            string name = declaration.Name;
            if (module.FunctionsInternal.ContainsKey(name))
            {
                Error(module.Name, "DS4002", "Function and shared variable share the name '" + name + "'.", declaration.NameSpan);
                return;
            }
            if (!sharedByName.TryGetValue(name, out DScriptSymbol symbol))
            {
                symbol = new DScriptSymbol(name, "shared." + name, SymbolKind.Global);
                sharedByName.Add(name, symbol);
            }
            module.GlobalsInternal[name] = symbol;
            module.ExportsInternal[name] = symbol;
            sharedDeclarations[declaration] = symbol;
            if (declaration.Initializer != null)
            {
                if (firstInitializer.ContainsKey(name))
                    Error(module.Name, "DS4018", "Shared variable '" + name +
                        "' has more than one initializer in the program.", declaration.NameSpan);
                else firstInitializer.Add(name, declaration);
            }
        }

        /// <summary>
        /// Inlined import-DFS over source statements. All names/slots are resolved beforehand.
        /// Each module is traversed once; cycles and repeated imports cannot duplicate initializers.
        /// The backend will compile these steps separately and run each despite previous failures.
        /// </summary>
        public IReadOnlyList<DScriptSharedInitializer> BuildSharedInitializationPlan(string entryModule)
        {
            var result = new List<DScriptSharedInitializer>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Visit(entryModule);
            return result;

            void Visit(string name)
            {
                if (!modules.TryGetValue(name, out DScriptModuleSymbols module) || !visited.Add(name)) return;
                foreach (var statement in module.Ast.Statements)
                {
                    if (statement is ImportStatement import) Visit(import.ModuleName);
                    else if (statement is FromImportStatement from) Visit(from.ModuleName);
                    else if (statement is SharedStatement declaration && declaration.Initializer != null &&
                        sharedDeclarations.TryGetValue(declaration, out DScriptSymbol symbol) &&
                        firstInitializer.TryGetValue(declaration.Name, out SharedStatement first) &&
                        ReferenceEquals(first, declaration))
                        result.Add(new DScriptSharedInitializer(result.Count, name, declaration, symbol));
                }
            }
        }

        private void RegisterImports(DScriptModuleSymbols module)
        {
            foreach (var stmt in module.Ast.Statements)
            {
                if (stmt is ImportStatement import)
                {
                    module.ImportedPaths.Add(import.ModuleName);
                    string alias = import.Alias ?? import.ModuleName.Split('.')[0];
                    string target = import.Alias == null ? import.ModuleName.Split('.')[0] : import.ModuleName;
                    AddImport(module, alias, new DScriptSymbol(alias, target, SymbolKind.Module), stmt.Span, true);
                }
                else if (stmt is FromImportStatement from)
                {
                    module.ImportedPaths.Add(from.ModuleName);
                    var exports = GetExports(from.ModuleName);
                    if (from.Wildcard)
                    {
                        foreach (var symbol in exports.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
                            if (!symbol.Name.StartsWith("_", StringComparison.Ordinal))
                                AddImport(module, symbol.Name, symbol, stmt.Span, false);
                    }
                    else
                    {
                        foreach (var imported in from.Names)
                        {
                            if (!exports.TryGetValue(imported.Name, out DScriptSymbol symbol))
                            {
                                Error(module.Name, "DS4003", "Module '" + from.ModuleName + "' has no exported symbol '" + imported.Name + "'.", imported.Span);
                                continue;
                            }
                            AddImport(module, imported.Alias ?? imported.Name, symbol, imported.Span, false);
                        }
                    }
                }
            }
        }

        private Dictionary<string, DScriptSymbol> GetExports(string name)
        {
            if (modules.TryGetValue(name, out var mod))
                return new Dictionary<string, DScriptSymbol>(mod.ExportsInternal, StringComparer.Ordinal);
            var exports = new Dictionary<string, DScriptSymbol>(StringComparer.Ordinal);
            if (!NativeLibrary.IsNativeModule(name)) return exports;
            foreach (string qualified in NativeLibrary.GetFunctionNames(name))
            {
                string leaf = qualified.Substring(name.Length + 1);
                exports[leaf] = new DScriptSymbol(leaf, qualified, SymbolKind.NativeFunction);
            }
            foreach (string qualified in NativeLibrary.GetConstantNames(name))
            {
                string leaf = qualified.Substring(name.Length + 1);
                exports[leaf] = new DScriptSymbol(leaf, qualified, SymbolKind.NativeConstant);
            }
            return exports;
        }

        private void AddImport(DScriptModuleSymbols module, string alias, DScriptSymbol target,
            SourceSpan span, bool allowSameModulePrefix)
        {
            if (module.ExportsInternal.TryGetValue(alias, out DScriptSymbol declared))
            {
                // Importing the same shared variable that this module already declared is harmless.
                if (declared.Kind == SymbolKind.Global && target.Kind == SymbolKind.Global &&
                    declared.QualifiedName == target.QualifiedName) return;
                Error(module.Name, "DS4004", "Import '" + alias + "' conflicts with a declaration in this module.", span);
                return;
            }
            if (module.ImportsInternal.TryGetValue(alias, out var existing))
            {
                if (allowSameModulePrefix && target.Kind == SymbolKind.Module &&
                    existing.Kind == SymbolKind.Module && existing.QualifiedName == target.QualifiedName) return;
                Error(module.Name, "DS4004", "Import alias '" + alias + "' is already defined.", span);
                return;
            }
            module.ImportsInternal.Add(alias, new DScriptSymbol(alias, target.QualifiedName, target.Kind, target.ParameterCount)
            { Slot = target.Slot });
        }

        private void BindFunction(FunctionStatement function, DScriptModuleSymbols module)
        {
            var table = new DScriptFunctionSymbols(function, module);
            functions[function] = table;
            currentFunction = table;
            loopDepth = 0;
            walkDepth = 0;
            foreach (string name in function.Parameters)
            {
                if (table.LocalsInternal.ContainsKey(name))
                    Error("DS4005", "Duplicate parameter '" + name + "'.", function.Span);
                else if (module.GlobalsInternal.ContainsKey(name))
                    Error("DS4006", "Parameter '" + name + "' conflicts with a shared variable.", function.Span);
                else AddLocal(name, SymbolKind.Parameter);
            }
            CollectLocalAssignments(function.Body);
            foreach (var statement in function.Body) VisitStatement(statement);
            currentFunction = null;
        }

        private void CollectLocalAssignments(IReadOnlyList<Statement> statements)
        {
            foreach (var statement in statements)
            {
                if (statement is AssignmentStatement assignment && assignment.Target is NameExpression name)
                {
                    if (!currentModule.GlobalsInternal.ContainsKey(name.Name)) AddLocal(name.Name, SymbolKind.Local);
                }
                else if (statement is ForStatement iterator)
                {
                    if (!currentModule.GlobalsInternal.ContainsKey(iterator.Variable)) AddLocal(iterator.Variable, SymbolKind.Local);
                    CollectLocalAssignments(iterator.Body);
                }
                else if (statement is IfStatement conditional)
                {
                    foreach (var branch in conditional.Branches) CollectLocalAssignments(branch.Body);
                    CollectLocalAssignments(conditional.ElseBody);
                }
                else if (statement is WhileStatement loop) CollectLocalAssignments(loop.Body);
            }
        }

        private void AddLocal(string name, SymbolKind kind)
        {
            if (currentFunction.LocalsInternal.ContainsKey(name)) return;
            int index = currentFunction.LocalsInternal.Count;
            currentFunction.LocalsInternal.Add(name,
                new DScriptSymbol(name, currentModule.Name + "." + currentFunction.Ast.Name + "." + name, kind) { Slot = index });
        }

        private void VisitStatement(Statement statement)
        {
            if (++walkDepth > options.MaxNestingDepth)
            {
                Error("DS4029", "Maximum semantic nesting depth exceeded.", statement.Span);
                walkDepth--; return;
            }
            try { VisitStatementCore(statement); }
            finally { walkDepth--; }
        }

        private void VisitStatementCore(Statement statement)
        {
            if (statement is AssignmentStatement assignment)
            {
                VisitExpression(assignment.Value);
                VisitTarget(assignment.Target, assignment.Operator != "=");
            }
            else if (statement is ExpressionStatement expr) VisitExpression(expr.Value);
            else if (statement is ReturnStatement ret)
            {
                if (currentFunction == null) Error("DS4007", "Return is only allowed in a function.", statement.Span);
                if (ret.Value != null) VisitExpression(ret.Value);
            }
            else if (statement is IfStatement conditional)
            {
                foreach (var branch in conditional.Branches)
                {
                    VisitExpression(branch.Condition);
                    foreach (var nested in branch.Body) VisitStatement(nested);
                }
                foreach (var nested in conditional.ElseBody) VisitStatement(nested);
            }
            else if (statement is WhileStatement loop)
            {
                VisitExpression(loop.Condition);
                loopDepth++;
                foreach (var nested in loop.Body) VisitStatement(nested);
                loopDepth--;
            }
            else if (statement is ForStatement iterator)
            {
                VisitExpression(iterator.Iterable);
                DScriptSymbol target = FindName(iterator.Variable, iterator.VariableSpan, true);
                if (target != null) forTargets[iterator] = target;
                loopDepth++;
                foreach (var nested in iterator.Body) VisitStatement(nested);
                loopDepth--;
            }
            else if (statement is FlowStatement flow)
            {
                if ((flow.Keyword == "break" || flow.Keyword == "continue") && loopDepth == 0)
                    Error("DS4008", "'" + flow.Keyword + "' requires an enclosing loop.", statement.Span);
            }
            else if (statement is FunctionStatement)
                Error("DS4009", "Nested functions and closures are not supported.", statement.Span);
            else if (statement is ImportStatement || statement is FromImportStatement)
                Error("DS4010", "Imports must be declared at module scope.", statement.Span);
            else if (statement is SharedStatement)
                Error("DS4019", "Shared variables must be declared at module scope.", statement.Span);
        }

        private void VisitTarget(Expression target, bool readBeforeWrite)
        {
            if (target is NameExpression variable)
            {
                var symbol = FindName(variable.Name, variable.Span, true);
                if (symbol == null || symbol.Kind != SymbolKind.Local && symbol.Kind != SymbolKind.Parameter && symbol.Kind != SymbolKind.Global)
                    Error("DS4011", "Cannot assign to '" + variable.Name + "'.", variable.Span);
                else bindings[variable] = symbol;
            }
            else if (target is IndexExpression index)
            {
                VisitExpression(index.Receiver);
                VisitExpression(index.Index);
            }
            else if (target is AttributeExpression attribute)
            {
                if (TryResolveStaticModule(attribute, out DScriptSymbol member, out _))
                {
                    if (member != null && member.Kind == SymbolKind.Global)
                        bindings[attribute] = member;
                    else Error("DS4012", "Only variables (not functions/constants) can be assigned through imported modules.", attribute.Span);
                }
                else VisitExpression(attribute.Receiver);
            }
            else Error("DS4011", "Invalid assignment target.", target.Span);
        }

        private void VisitExpression(Expression expression)
        {
            if (expression == null) return;
            if (++walkDepth > options.MaxNestingDepth)
            {
                Error("DS4029", "Maximum semantic expression depth exceeded.", expression.Span);
                walkDepth--; return;
            }
            try { VisitExpressionCore(expression); }
            finally { walkDepth--; }
        }

        private void VisitExpressionCore(Expression expression)
        {
            if (expression is LiteralExpression literal && literal.Kind == LiteralKind.Integer)
            {
                if (ulong.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong magnitude)
                    && magnitude > long.MaxValue)
                    Error("DS4017", "Integer literal exceeds Int64.MaxValue; use -9223372036854775808 only as a signed literal.", literal.Span);
            }
            else if (expression is NameExpression name)
            {
                DScriptSymbol symbol = FindName(name.Name, name.Span, false);
                if (symbol == null) return;
                if (symbol.Kind == SymbolKind.Module)
                    Error("DS4013", "Modules are compile-time namespaces, not runtime values: '" + name.Name + "'.", name.Span);
                bindings[name] = symbol;
            }
            else if (expression is UnaryExpression unary)
            {
                if (unary.Operator == "-" && unary.Operand is LiteralExpression number &&
                    number.Kind == LiteralKind.Integer && number.Text == "9223372036854775808")
                    return; // Valid Int64.MinValue (the positive magnitude is otherwise invalid).
                VisitExpression(unary.Operand);
            }
            else if (expression is BinaryExpression binary)
            {
                VisitExpression(binary.Left);
                VisitExpression(binary.Right);
            }
            else if (expression is CallExpression call)
            {
                VisitExpression(call.Callee);
                foreach (var arg in call.Arguments) VisitExpression(arg);
                if (bindings.TryGetValue(call.Callee, out DScriptSymbol callee) &&
                    callee.Kind == SymbolKind.Function && callee.ParameterCount >= 0 &&
                    callee.ParameterCount != call.Arguments.Count)
                    Error("DS4014", "Function '" + callee.QualifiedName + "' expects " +
                        callee.ParameterCount + " arguments, got " + call.Arguments.Count + ".", call.Span);
            }
            else if (expression is AttributeExpression attribute)
            {
                if (TryResolveStaticModule(attribute, out DScriptSymbol symbol, out string error))
                {
                    if (symbol != null)
                    {
                        if (symbol.Kind == SymbolKind.Module)
                            Error("DS4013", "A module namespace cannot be used as a value.", attribute.Span);
                        bindings[attribute] = symbol;
                    }
                    else Error("DS4015", error, attribute.Span);
                }
                else VisitExpression(attribute.Receiver);
            }
            else if (expression is IndexExpression index)
            {
                VisitExpression(index.Receiver);
                VisitExpression(index.Index);
            }
            else if (expression is CollectionExpression collection)
            {
                foreach (var key in collection.Keys) VisitExpression(key);
                foreach (var item in collection.Items) VisitExpression(item);
            }
        }

        private DScriptSymbol FindName(string name, SourceSpan at, bool write)
        {
            if (currentFunction != null)
            {
                if (currentFunction.LocalsInternal.TryGetValue(name, out DScriptSymbol local)) return local;
            }
            if (currentModule.GlobalsInternal.TryGetValue(name, out DScriptSymbol global)) return global;
            if (currentModule.FunctionsInternal.TryGetValue(name, out DScriptSymbol function)) return function;
            if (currentModule.ImportsInternal.TryGetValue(name, out DScriptSymbol imported)) return imported;
            if (builtins.TryGetValue(name, out DScriptSymbol builtin)) return builtin;
            if (!write) Error("DS4016", "Undefined name '" + name + "'.", at);
            return null;
        }

        /// <summary>
        /// If the root is an imported module (not a local/global shadow), resolve static namespace
        /// chains. Returns false for ordinary runtime object attributes (e.g. unit.health).
        /// </summary>
        private bool TryResolveStaticModule(AttributeExpression attr, out DScriptSymbol symbol, out string error)
        {
            symbol = null; error = null;
            var parts = new List<string>();
            Expression root = attr;
            while (root is AttributeExpression access) { parts.Insert(0, access.Attribute); root = access.Receiver; }
            if (!(root is NameExpression name)) return false;
            if (currentFunction != null && currentFunction.LocalsInternal.ContainsKey(name.Name)) return false;
            if (currentModule.GlobalsInternal.ContainsKey(name.Name)) return false;
            if (!currentModule.ImportsInternal.TryGetValue(name.Name, out DScriptSymbol imported) ||
                imported.Kind != SymbolKind.Module) return false;
            string path = imported.QualifiedName;
            foreach (string part in parts) path += "." + part;
            int lastDot = path.LastIndexOf('.');
            string moduleName = lastDot < 0 ? "" : path.Substring(0, lastDot);
            string memberName = path.Substring(lastDot + 1);
            if (currentModule.ImportedPaths.Contains(moduleName))
            {
                var exports = GetExports(moduleName);
                if (exports.TryGetValue(memberName, out symbol)) return true;
                error = "Module '" + moduleName + "' has no symbol '" + memberName + "'.";
                return true;
            }
            // A path may begin with a statically imported value and continue with dynamic
            // runtime members, e.g. shared.unit.health or math.sin.some_member.
            // The caller then visits the receiver, which binds the imported prefix.
            for (int prefixLength = parts.Count - 1; prefixLength >= 1; prefixLength--)
            {
                string prefix = imported.QualifiedName;
                for (int i = 0; i < prefixLength; i++) prefix += "." + parts[i];
                int separator = prefix.LastIndexOf('.');
                string prefixModule = prefix.Substring(0, separator);
                string prefixMember = prefix.Substring(separator + 1);
                if (currentModule.ImportedPaths.Contains(prefixModule) &&
                    GetExports(prefixModule).ContainsKey(prefixMember)) return false;
            }
            // An intermediate prefix is not a value; the full imported module is needed.
            foreach (string importedPath in currentModule.ImportedPaths)
                if (importedPath.StartsWith(path + ".", StringComparison.Ordinal) || importedPath == path)
                {
                    symbol = new DScriptSymbol(path, path, SymbolKind.Module);
                    return true;
                }
            error = "Namespace '" + path + "' is not imported.";
            return true;
        }

        private void Error(string code, string message, SourceSpan span)
            => Error(currentModule != null ? currentModule.Name : string.Empty, code, message, span);
        private void Error(string module, string code, string message, SourceSpan span)
        {
            if (errors.Count < options.MaxDiagnostics)
                errors.Add(new DScriptDiagnostic(module, code, message, span));
        }
    }
}
