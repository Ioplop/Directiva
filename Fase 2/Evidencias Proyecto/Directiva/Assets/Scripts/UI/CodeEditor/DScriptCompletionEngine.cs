using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Parsing;
using DSCompilation.DScript.Semantics;
using DSExecution.DataTypes;
using DSExecution.Native;
using DOrchestration;
using DSimulation;

namespace Directiva.CodeUI
{
    /// <summary>One non-executable suggestion. Types are hints, not static guarantees.</summary>
    public sealed class DScriptCompletionItem
    {
        public string Name { get; }
        public string Kind { get; }
        public string Detail { get; }
        public string Type { get; }
        public bool IsCallable { get; }
        public bool TakesArguments { get; }
        public DScriptCompletionItem(string name, string kind, string detail = null, string type = null,
            bool isCallable = false, bool takesArguments = false)
        {
            Name = name; Kind = kind; Detail = detail ?? string.Empty; Type = type ?? string.Empty;
            IsCallable = isCallable; TakesArguments = takesArguments;
        }
    }

    public sealed class DScriptCompletionResult
    {
        public int ReplaceStart { get; }
        public int ReplaceEnd { get; }
        public IReadOnlyList<DScriptCompletionItem> Items { get; }
        public DScriptCompletionResult(int start, int end, IReadOnlyList<DScriptCompletionItem> items)
        { ReplaceStart = start; ReplaceEnd = end; Items = items; }
    }

    /// <summary>
    /// IDE-only, best-effort semantic completion. Uses the compiler's recoverable AST and
    /// existing native/simulation reflection catalogs. No script is ever executed.
    /// Unknown or conflicting return types deliberately suppress member suggestions.
    /// </summary>
    public static class DScriptCompletionEngine
    {
        private const string EntityType = "SimulationObject";
        private const string ModulePrefix = "module:";
        private static readonly string[] TypeNames =
            { "SimulationObject", "Int", "Decimal", "Bool", "String", "Tuple", "List", "Set", "Dict", "Range", "None" };
        private static readonly string[] Keywords =
            { "def", "return", "if", "elif", "else", "for", "while", "in", "break", "continue", "pass",
              "shared", "import", "from", "as", "and", "or", "not", "True", "False", "None" };
        // Only these declarations can begin a statement at module scope. `global`
        // remains reserved/invalid in DScript; `import` and `from` are valid today.
        private static readonly string[] ModuleKeywords = { "def", "shared", "import", "from" };
        private static readonly string[] InitializerKeywords = { "True", "False", "None", "not", "and", "or" };

        /// <summary>
        /// 'source' is the current unsaved editor buffer. Other modules are read from provider,
        /// usually WorkspaceDScriptSourceProvider, which includes unsaved .dscript.back copies.
        /// </summary>
        public static DScriptCompletionResult Complete(string moduleName, string source, int caret,
            IDScriptSourceProvider provider, IEnumerable<string> modulePaths = null)
        {
            source ??= string.Empty;
            caret = Math.Max(0, Math.Min(source.Length, caret));
            if (string.IsNullOrEmpty(moduleName) || provider == null || !IsCodeAt(source, caret))
                return Empty(caret);
            int start = caret;
            while (start > 0 && IsIdentifierPart(source[start - 1])) start--;
            string prefix = source.Substring(start, caret - start);
            bool afterDot = start > 0 && source[start - 1] == '.';
            if (prefix.Length == 0 && !afterDot) return Empty(caret);
            // Do not offer completions in the middle of an existing token or selection.
            if (caret < source.Length && IsIdentifierPart(source[caret])) return Empty(caret);

            int lineStart = source.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            string before = source.Substring(lineStart, Math.Max(0, start - lineStart));
            // DScript uses indentation for suites. In particular, a previous function
            // may still be the "containing function" of an incomplete root-level line
            // in the recoverable AST. The *current* line's indentation is authoritative.
            bool atModuleScope = lineStart >= source.Length ||
                (source[lineStart] != ' ' && source[lineStart] != '\t');
            bool inSharedInitializer = atModuleScope && IsSharedInitializerContext(before);
            bool inTypeHint = IsTypeHintContext(before);
            bool inImport = IsImportContext(before, out _);

            // A bare token at module scope is the start of a declaration, never a
            // function call/expression. This fast path also works when the rest of
            // the module is not yet valid DScript.
            if (atModuleScope && !inSharedInitializer && !inTypeHint && !inImport)
            {
                if (afterDot || !string.IsNullOrWhiteSpace(before)) return Empty(caret);
                var declarations = ModuleKeywords
                    .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
                    .ThenBy(name => name.Length).ThenBy(name => name, StringComparer.Ordinal)
                    .Select(name => new DScriptCompletionItem(name, "keyword")).ToArray();
                return new DScriptCompletionResult(start, caret, declarations);
            }

            var candidates = new Dictionary<string, DScriptCompletionItem>(StringComparer.Ordinal);
            void Add(string name, string kind, string detail = null, string type = null)
            {
                if (!string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !candidates.ContainsKey(name))
                {
                    bool callable = kind == "function" || kind == "method";
                    string signature = detail ?? string.Empty;
                    bool hasArguments = callable && signature.StartsWith("(", StringComparison.Ordinal) &&
                        !signature.StartsWith("()", StringComparison.Ordinal) &&
                        !signature.StartsWith("(0 arguments", StringComparison.Ordinal) &&
                        !signature.StartsWith("(0-", StringComparison.Ordinal);
                    candidates.Add(name, new DScriptCompletionItem(name, kind, detail, type,
                        callable, hasArguments));
                }
            }
            try
            {
                var analysis = DScriptFrontend.AnalyzeForRefactoring(moduleName,
                    new EditorSourceOverride(provider, moduleName, source), SimulationDScriptProfile.Options);
                analysis.ModuleSymbols.TryGetValue(moduleName, out var symbols);
                analysis.Modules.TryGetValue(moduleName, out var ast);
                var function = atModuleScope ? null : FindContainingFunction(ast, source, caret);
                var context = new TypeContext(analysis, moduleName, function);
                var env = context.LocalsAt(caret);

                if (afterDot)
                {
                    string receiver = ReadReceiverText(source, start - 1);
                    // `self` only exists in entity functions; it is not an
                    // available value while initializing module-level `shared`.
                    if (atModuleScope && (receiver == "self" ||
                        receiver.StartsWith("self.", StringComparison.Ordinal)))
                        return Empty(caret);
                    Expression parsed = ParseReceiver(receiver);
                    string resolved = context.TypeOf(parsed, env, new HashSet<string>(StringComparer.Ordinal));
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        if (resolved.StartsWith(ModulePrefix, StringComparison.Ordinal))
                            AddModuleMembers(resolved.Substring(ModulePrefix.Length), analysis, Add);
                        else if (resolved == EntityType)
                            AddSimulationMembers(Add);
                        else
                            AddRuntimeMembers(resolved, Add);
                    }
                }
                else if (IsTypeHintContext(before))
                {
                    foreach (string name in TypeNames) Add(name, "type");
                }
                else if (IsImportContext(before, out string importedFrom))
                {
                    if (importedFrom != null)
                    {
                        AddModuleMembers(importedFrom, analysis, Add);
                        // A half-written `from x import ...` may not have produced an
                        // import AST node, so the loader might never have visited x.
                        // Read only that requested module as a fallback; do not execute it.
                        if (!analysis.ModuleSymbols.ContainsKey(importedFrom) &&
                            !NativeLibrary.IsNativeModule(importedFrom) &&
                            provider.TryReadModule(importedFrom, out string importedSource))
                        {
                            var tokens = new DScriptLexer(importedFrom, importedSource).Tokenize();
                            var parsed = new DScriptParser(importedFrom, tokens).Parse();
                            foreach (var declaration in parsed.Statements)
                            {
                                if (declaration is FunctionStatement method)
                                    Add(method.Name, "function", DescribeUserFunction(method), method.ReturnType);
                                else if (declaration is SharedStatement shared)
                                    Add(shared.Name, "shared");
                            }
                        }
                    }
                    else
                    {
                        foreach (string name in NativeLibrary.ModuleNames.Where(x => x == "math" || x == "random"))
                            Add(name, "module");
                        if (modulePaths != null)
                            foreach (string name in modulePaths) Add(name, "module");
                    }
                }
                else
                {
                    // Only expression keywords belong inside a top-level shared
                    // initializer; statements such as `return` are not legal there.
                    foreach (string keyword in atModuleScope ? InitializerKeywords : Keywords)
                        Add(keyword, "keyword");
                    foreach (string type in NativeLibrary.SimulationEnumModules) Add(type, "enum");
                    foreach (var native in NativeLibrary.GetFunctionSignatures(string.Empty))
                        Add(native.QualifiedName, "function", DescribeNative(native), native.ReturnType);
                    if (!atModuleScope) Add("self", "variable", EntityType, EntityType);

                    if (symbols != null)
                    {
                        foreach (var symbol in symbols.Exports.Values.Concat(symbols.Imports.Values))
                            Add(symbol.Name, SymbolLabel(symbol.Kind), DescribeSymbol(symbol, analysis),
                                context.SymbolType(symbol, new HashSet<string>(StringComparer.Ordinal)));
                    }
                    foreach (var variable in env)
                    {
                        if (atModuleScope && variable.Key == "self") continue;
                        Add(variable.Key, "variable", variable.Value, variable.Value);
                    }
                    if (!atModuleScope && function != null)
                        foreach (var method in SimulationApiCatalog.Operations.Where(op =>
                            op.Kind == SimulationApiKind.Order || op.MemberKind == SimulationObservationMemberKind.Method))
                            Add(method.Name, "method", DescribeSimulation(method), ResultOfSimulation(method));
                }
            }
            catch (Exception)
            {
                // Editor assistance must never prevent typing because another module is
                // incomplete, a file disappeared, or a native provider was unavailable.
                return Empty(caret);
            }
            var sorted = candidates.Values.OrderBy(i => i.Name.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(i => i.Name.Length).ThenBy(i => i.Name, StringComparer.Ordinal).Take(40).ToArray();
            return new DScriptCompletionResult(start, caret, sorted);
        }

        private static DScriptCompletionResult Empty(int caret) =>
            new DScriptCompletionResult(caret, caret, Array.Empty<DScriptCompletionItem>());
        private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);

        private static bool IsCodeAt(string source, int caret)
        {
            int line = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;
            char quote = '\0'; bool escaped = false;
            for (int i = line; i < caret; i++)
            {
                char c = source[i];
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) quote = '\0';
                }
                else if (c == '#') return false;
                else if (c == '\'' || c == '"') quote = c;
            }
            return quote == '\0';
        }

        private static bool IsTypeHintContext(string before)
        {
            int def = before.IndexOf("def ", StringComparison.Ordinal);
            if (def < 0) return false;
            int arrow = before.LastIndexOf("->", StringComparison.Ordinal);
            if (arrow >= 0) return true;
            int colon = before.LastIndexOf(':');
            int close = before.LastIndexOf(')');
            return colon >= 0 && colon > before.LastIndexOf('(') && close < colon;
        }

        private static bool IsSharedInitializerContext(string before)
        {
            string text = before.TrimStart();
            return text.StartsWith("shared ", StringComparison.Ordinal) && text.IndexOf('=') >= 7;
        }

        private static bool IsImportContext(string before, out string from)
        {
            from = null;
            string trimmed = before.TrimStart();
            if (trimmed.StartsWith("import ", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("from ", StringComparison.Ordinal))
            {
                int import = trimmed.IndexOf(" import ", StringComparison.Ordinal);
                if (import > 5) { from = trimmed.Substring(5, import - 5).Trim(); return true; }
                return true;
            }
            return false;
        }

        private static string ReadReceiverText(string source, int dot)
        {
            // Walk backwards over a dotted chain with balanced calls/indexers.
            int i = dot - 1, depthParen = 0, depthBracket = 0;
            while (i >= 0)
            {
                char c = source[i];
                if (c == ')') { depthParen++; i--; continue; }
                if (c == ']') { depthBracket++; i--; continue; }
                if (c == '(' && depthParen > 0) { depthParen--; i--; continue; }
                if (c == '[' && depthBracket > 0) { depthBracket--; i--; continue; }
                if (depthParen > 0 || depthBracket > 0) { i--; continue; }
                if (IsIdentifierPart(c) || c == '.') { i--; continue; }
                break;
            }
            return source.Substring(i + 1, dot - i - 1).Trim();
        }

        private static Expression ParseReceiver(string receiver)
        {
            if (string.IsNullOrEmpty(receiver)) return null;
            // Reuse the real language parser. No hand-written expression grammar.
            const string preamble = "def __completion__():\n    return ";
            string sample = preamble + receiver + "\n";
            var lex = new DScriptLexer("__completion__", sample);
            var tokens = lex.Tokenize();
            if (lex.Diagnostics.Count > 0) return null;
            var parser = new DScriptParser("__completion__", tokens);
            var module = parser.Parse();
            if (parser.Diagnostics.Count > 0) return null;
            if (module.Statements.Count != 1 || !(module.Statements[0] is FunctionStatement function) ||
                function.Body.Count != 1 || !(function.Body[0] is ReturnStatement statement)) return null;
            return statement.Value;
        }

        private static FunctionStatement FindContainingFunction(ModuleNode ast, string source, int caret)
        {
            if (ast == null) return null;
            for (int i = 0; i < ast.Statements.Count; i++)
            {
                if (!(ast.Statements[i] is FunctionStatement function) || function.Span.Start >= caret) continue;
                int next = i + 1 < ast.Statements.Count ? ast.Statements[i + 1].Span.Start : source.Length + 1;
                if (caret >= next) continue;
                int line = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;
                int spaces = 0;
                while (line + spaces < source.Length && source[line + spaces] == ' ') spaces++;
                if (spaces >= 1 || caret <= function.Span.End) return function;
            }
            return null;
        }

        private static string SymbolLabel(SymbolKind kind) => kind switch
        {
            SymbolKind.Function => "function", SymbolKind.NativeFunction => "function",
            SymbolKind.Module => "module", SymbolKind.NativeConstant => "constant",
            SymbolKind.Global => "shared", _ => "variable"
        };
        private static string DescribeSymbol(DScriptSymbol symbol, DScriptAnalysis analysis)
        {
            if (symbol.Kind == SymbolKind.NativeFunction &&
                NativeLibrary.TryGetFunctionSignature(symbol.QualifiedName, out var native))
                return DescribeNative(native);
            if (symbol.Kind == SymbolKind.Function)
            {
                var function = FindFunction(analysis, symbol.QualifiedName);
                if (function != null) return DescribeUserFunction(function);
            }
            return symbol.Kind.ToString();
        }
        private static string DescribeUserFunction(FunctionStatement function) =>
            "(" + string.Join(", ", function.Parameters.Select(name =>
                function.ParameterTypes.TryGetValue(name, out string type) && !string.IsNullOrEmpty(type)
                    ? name + ": " + type : name)) + ")" +
            (string.IsNullOrEmpty(function.ReturnType) ? "" : " -> " + function.ReturnType);

        private static string DescribeNative(NativeFunctionSignature native) =>
            "(" + (native.Parameters.Count > 0 ? string.Join(", ", native.Parameters) :
                native.MinArguments == native.MaxArguments ? native.MinArguments + " arguments" :
                    native.MinArguments + "-" + native.MaxArguments + " arguments") + ")" +
            (string.IsNullOrEmpty(native.ReturnType) ? "" : " -> " + native.ReturnType);

        private static FunctionStatement FindFunction(DScriptAnalysis analysis, string qualified)
        {
            int dot = qualified.LastIndexOf('.');
            if (dot <= 0 || !analysis.Modules.TryGetValue(qualified.Substring(0, dot), out var module)) return null;
            string leaf = qualified.Substring(dot + 1);
            return module.Statements.OfType<FunctionStatement>().FirstOrDefault(f => f.Name == leaf);
        }

        private static void AddModuleMembers(string module, DScriptAnalysis analysis,
            Action<string, string, string, string> add)
        {
            if (NativeLibrary.IsNativeModule(module))
            {
                foreach (var native in NativeLibrary.GetFunctionSignatures(module))
                    add(native.QualifiedName.Substring(module.Length + 1), "function",
                        DescribeNative(native), native.ReturnType);
                foreach (string constant in NativeLibrary.GetConstantNames(module))
                    add(constant.Substring(module.Length + 1), "constant", module, "");
            }
            else if (analysis.ModuleSymbols.TryGetValue(module, out var symbols))
            {
                foreach (var symbol in symbols.Exports.Values)
                    add(symbol.Name, SymbolLabel(symbol.Kind), DescribeSymbol(symbol, analysis), "");
            }
        }

        private static string ResultOfSimulation(SimulationApiOperation operation)
        {
            if (operation.Kind == SimulationApiKind.Order) return "Bool";
            switch (operation.ResultKind)
            {
                case SimulationObservationResultKind.Entity: return EntityType;
                case SimulationObservationResultKind.EntityList: return "List";
                case SimulationObservationResultKind.MoveTarget: return "Tuple";
            }
            Type clr = Nullable.GetUnderlyingType(operation.Method.ReturnType) ?? operation.Method.ReturnType;
            if (clr == typeof(bool)) return "Bool";
            if (clr == typeof(int) || clr == typeof(long)) return "Int";
            if (clr == typeof(DMath.Fixed)) return "Decimal";
            if (clr == typeof(DMath.FixedVector2)) return "Tuple";
            // Values from observation enums remain script enums, not integers.
            return DScriptSimulationEnumDisplay.ModuleName(clr);
        }

        private static string DescribeSimulation(SimulationApiOperation op)
        {
            string arguments = op.ManualRegistration ? "entry_function" :
                string.Join(", ", op.Arguments.Select(a => a.Name));
            return (op.MemberKind == SimulationObservationMemberKind.Property && op.Kind == SimulationApiKind.Observation
                ? "property" : "(" + arguments + ")") +
                " -> " + (ResultOfSimulation(op) ?? "?");
        }

        private static void AddSimulationMembers(Action<string, string, string, string> add)
        {
            foreach (var group in SimulationApiCatalog.Operations.GroupBy(op => op.Name, StringComparer.Ordinal))
            {
                var first = group.First();
                var label = first.Kind == SimulationApiKind.Observation &&
                    first.MemberKind == SimulationObservationMemberKind.Property ? "property" : "method";
                add(group.Key, label, DescribeSimulation(first), ResultOfSimulation(first));
            }
        }

        private static Type GetDataType(string name) => name switch
        {
            "List" => typeof(DTList), "Tuple" => typeof(DTTuple), "Set" => typeof(DTSet),
            "Dict" => typeof(DTDict), "String" => typeof(DTString), "Range" => typeof(DTRange),
            _ => null
        };

        private static void AddRuntimeMembers(string type, Action<string, string, string, string> add)
        {
            Type dataType = GetDataType(type);
            if (dataType == null) return;
            foreach (var method in dataType.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                var attribute = method.GetCustomAttribute<DScriptMethodAttribute>(true);
                if (attribute != null)
                {
                    string arity = attribute.MaximumArgumentCount < 0
                        ? attribute.MinimumArgumentCount + "+ arguments" :
                        attribute.MinimumArgumentCount == attribute.MaximumArgumentCount
                            ? attribute.MinimumArgumentCount + " arguments"
                            : attribute.MinimumArgumentCount + "-" + attribute.MaximumArgumentCount + " arguments";
                    add(attribute.Name, "method", "(" + arity + ")" +
                        (attribute.ReturnType == null ? "" : " -> " + attribute.ReturnType), attribute.ReturnType);
                }
                var getter = method.GetCustomAttribute<DScriptGetterAttribute>(true);
                if (getter != null) add(getter.Name, "property", getter.ReturnType, getter.ReturnType);
            }
        }

        private sealed class EditorSourceOverride : IDScriptSourceProvider
        {
            private readonly IDScriptSourceProvider inner;
            private readonly string module;
            private readonly string content;
            public EditorSourceOverride(IDScriptSourceProvider inner, string module, string content)
            { this.inner = inner; this.module = module; this.content = content; }
            public bool TryReadModule(string name, out string text)
            {
                if (name == module) { text = content; return true; }
                return inner.TryReadModule(name, out text);
            }
        }

        private sealed class TypeContext
        {
            private readonly DScriptAnalysis analysis;
            private readonly string module;
            private readonly FunctionStatement function;

            public TypeContext(DScriptAnalysis analysis, string module, FunctionStatement function)
            { this.analysis = analysis; this.module = module; this.function = function; }

            public Dictionary<string, string> LocalsAt(int caret)
            {
                var types = NewEnvironment(module, function);
                if (function != null) Walk(function.Body, types, caret, new HashSet<string>(StringComparer.Ordinal));
                return types;
            }

            private Dictionary<string, string> NewEnvironment(string name, FunctionStatement fn)
            {
                var types = new Dictionary<string, string>(StringComparer.Ordinal) { ["self"] = EntityType };
                if (analysis.ModuleSymbols.TryGetValue(name, out var symbols))
                {
                    foreach (var sym in symbols.Imports.Values)
                        types[sym.Name] = SymbolType(sym, new HashSet<string>(StringComparer.Ordinal));
                    foreach (var sym in symbols.Globals.Values)
                        types[sym.Name] = SymbolType(sym, new HashSet<string>(StringComparer.Ordinal));
                }
                if (fn != null)
                {
                    foreach (string param in fn.Parameters)
                        types[param] = fn.ParameterTypes.TryGetValue(param, out var hint) ? hint : null;
                }
                return types;
            }

            public string SymbolType(DScriptSymbol symbol, HashSet<string> visited)
            {
                if (symbol.Kind == SymbolKind.Module) return ModulePrefix + symbol.QualifiedName;
                if (symbol.Kind == SymbolKind.NativeConstant)
                    return symbol.QualifiedName.StartsWith("math.", StringComparison.Ordinal) ? "Decimal" : "Int";
                if (symbol.Kind == SymbolKind.Global)
                {
                    string identity = "shared:" + symbol.Name;
                    if (!visited.Add(identity)) return null;
                    try
                    {
                        foreach (var part in analysis.Modules)
                            foreach (var shared in part.Value.Statements.OfType<SharedStatement>())
                                if (shared.Name == symbol.Name && shared.Initializer != null)
                                    return TypeOf(shared.Initializer, NewEnvironmentWithoutShared(part.Key), visited);
                    }
                    finally { visited.Remove(identity); }
                }
                return null;
            }

            private Dictionary<string, string> NewEnvironmentWithoutShared(string name)
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["self"] = EntityType };
                if (analysis.ModuleSymbols.TryGetValue(name, out var symbols))
                    foreach (var sym in symbols.Imports.Values)
                        if (sym.Kind == SymbolKind.Module) result[sym.Name] = ModulePrefix + sym.QualifiedName;
                return result;
            }

            public string TypeOf(Expression expr, Dictionary<string, string> types, HashSet<string> visited)
            {
                if (expr == null) return null;
                if (expr is LiteralExpression literal) return literal.Kind switch
                {
                    LiteralKind.Integer => "Int", LiteralKind.Decimal => "Decimal",
                    LiteralKind.String => "String", LiteralKind.Bool => "Bool", _ => "None"
                };
                if (expr is CollectionExpression collection) return collection.Kind.ToString() switch
                { "Dictionary" => "Dict", _ => collection.Kind.ToString() };
                if (expr is NameExpression name)
                {
                    if (types.TryGetValue(name.Name, out var typ)) return typ;
                    if (NativeLibrary.SimulationEnumModules.Contains(name.Name)) return ModulePrefix + name.Name;
                    if (analysis.ModuleSymbols.TryGetValue(module, out var symbols))
                    {
                        if (symbols.Exports.TryGetValue(name.Name, out var sym) ||
                            symbols.Imports.TryGetValue(name.Name, out sym)) return SymbolType(sym, visited);
                    }
                    return null;
                }
                if (expr is UnaryExpression unary) return TypeOf(unary.Operand, types, visited);
                if (expr is BinaryExpression binary)
                {
                    if (binary.Operator == "and" || binary.Operator == "or") return null;
                    if (binary.Operator == "==" || binary.Operator == "!=" || binary.Operator == "<" ||
                        binary.Operator == ">" || binary.Operator == "<=" || binary.Operator == ">=" ||
                        binary.Operator == "in" || binary.Operator == "not in") return "Bool";
                    string l = TypeOf(binary.Left, types, visited);
                    string r = TypeOf(binary.Right, types, visited);
                    if (l == r && l != null) return l;
                    if ((l == "Int" || l == "Decimal") && (r == "Int" || r == "Decimal")) return "Decimal";
                    return null;
                }
                if (expr is AttributeExpression property)
                {
                    string receiver = TypeOf(property.Receiver, types, visited);
                    if (receiver == null) return null;
                    if (receiver.StartsWith(ModulePrefix, StringComparison.Ordinal))
                    {
                        string qualified = receiver.Substring(ModulePrefix.Length) + "." + property.Attribute;
                        if (NativeLibrary.TryGetConstant(qualified, out _)) return qualified.StartsWith("math.") ? "Decimal" : "Int";
                        if (analysis.ModuleSymbols.TryGetValue(receiver.Substring(ModulePrefix.Length), out var mod) &&
                            mod.Exports.TryGetValue(property.Attribute, out var sym)) return SymbolType(sym, visited);
                    }
                    else if (receiver == EntityType)
                    {
                        var entry = SimulationApiCatalog.Operations.FirstOrDefault(op => op.Name == property.Attribute &&
                            op.Kind == SimulationApiKind.Observation && op.MemberKind == SimulationObservationMemberKind.Property);
                        if (entry != null) return ResultOfSimulation(entry);
                    }
                    else
                    {
                        Type dataType = GetDataType(receiver);
                        if (dataType != null)
                            foreach (var member in dataType.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                BindingFlags.NonPublic))
                            {
                                var getter = member.GetCustomAttribute<DScriptGetterAttribute>(true);
                                if (getter?.Name == property.Attribute) return getter.ReturnType;
                            }
                    }
                    return null;
                }
                if (expr is CallExpression call)
                {
                    if (call.Callee is NameExpression named)
                    {
                        // Local declarations shadow all callable names, even when their
                        // value has an unknown type (including inferred None).
                        if (function != null &&
                            (function.Parameters.Contains(named.Name) ||
                             (analysis.FunctionSymbols.TryGetValue(function, out var locals) &&
                              locals.Locals.ContainsKey(named.Name)))) return null;
                        if (analysis.ModuleSymbols.TryGetValue(module, out var symbols))
                        {
                            if (symbols.Exports.TryGetValue(named.Name, out var sym) ||
                                symbols.Imports.TryGetValue(named.Name, out sym))
                                return TypeOfCallableSymbol(sym, visited);
                        }
                        if (NativeLibrary.TryGetFunctionSignature(named.Name, out var native)) return native.ReturnType;
                        var op = SimulationApiCatalog.Operations.FirstOrDefault(o => o.Name == named.Name &&
                            (o.Kind == SimulationApiKind.Order || o.MemberKind == SimulationObservationMemberKind.Method));
                        if (op != null) return ResultOfSimulation(op);
                    }
                    else if (call.Callee is AttributeExpression member)
                    {
                        string receiver = TypeOf(member.Receiver, types, visited);
                        if (receiver != null && receiver.StartsWith(ModulePrefix, StringComparison.Ordinal))
                        {
                            string qualified = receiver.Substring(ModulePrefix.Length) + "." + member.Attribute;
                            if (NativeLibrary.TryGetFunctionSignature(qualified, out var native)) return native.ReturnType;
                            if (analysis.ModuleSymbols.TryGetValue(receiver.Substring(ModulePrefix.Length), out var mod) &&
                                mod.Exports.TryGetValue(member.Attribute, out var sym)) return TypeOfCallableSymbol(sym, visited);
                        }
                        if (receiver == EntityType)
                        {
                            var op = SimulationApiCatalog.Operations.FirstOrDefault(o => o.Name == member.Attribute &&
                                (o.Kind == SimulationApiKind.Order || o.MemberKind == SimulationObservationMemberKind.Method));
                            if (op != null) return ResultOfSimulation(op);
                        }
                        Type runtime = GetDataType(receiver);
                        if (runtime != null)
                            foreach (var method in runtime.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                BindingFlags.NonPublic))
                            {
                                var annotation = method.GetCustomAttribute<DScriptMethodAttribute>(true);
                                if (annotation?.Name == member.Attribute) return annotation.ReturnType;
                            }
                    }
                    return null;
                }
                if (expr is IndexExpression index)
                {
                    // Containers are dynamic. We cannot soundly infer the element type.
                    return null;
                }
                return null;
            }

            private string TypeOfCallableSymbol(DScriptSymbol sym, HashSet<string> visited)
            {
                if (sym.Kind == SymbolKind.NativeFunction &&
                    NativeLibrary.TryGetFunctionSignature(sym.QualifiedName, out var native)) return native.ReturnType;
                if (sym.Kind != SymbolKind.Function) return null;
                string key = sym.QualifiedName;
                if (!visited.Add(key)) return null; // recursive or cyclic inference
                try
                {
                    FunctionStatement fn = FindFunction(analysis, key);
                    if (fn == null) return null;
                    if (!string.IsNullOrEmpty(fn.ReturnType)) return fn.ReturnType;
                    string fnModule = key.Substring(0, key.LastIndexOf('.'));
                    var nested = new TypeContext(analysis, fnModule, fn);
                    var vars = nested.NewEnvironment(fnModule, fn);
                    var returned = new List<string>();
                    nested.Walk(fn.Body, vars, int.MaxValue, visited, returned);
                    if (returned.Count == 0) return "None";
                    // A function without an unconditional return can also fall
                    // through to None; do not pretend that every call returns List.
                    if (!AlwaysReturns(fn.Body)) returned.Add("None");
                    if (returned.Any(x => x == null)) return null;
                    return returned.Distinct(StringComparer.Ordinal).Count() == 1 ? returned[0] : null;
                }
                finally { visited.Remove(key); }
            }

            private static bool AlwaysReturns(IReadOnlyList<Statement> statements)
            {
                foreach (Statement statement in statements)
                {
                    if (statement is ReturnStatement) return true;
                    if (statement is IfStatement branches && branches.ElseBody.Count > 0 &&
                        branches.Branches.All(branch => AlwaysReturns(branch.Body)) &&
                        AlwaysReturns(branches.ElseBody)) return true;
                    // Loops are deliberately not assumed to execute even once.
                }
                return false;
            }

            private void Walk(IReadOnlyList<Statement> statements, Dictionary<string, string> vars,
                int caret, HashSet<string> visited, List<string> returned = null)
            {
                foreach (var statement in statements)
                {
                    if (statement.Span.Start >= caret) break;
                    if (statement is AssignmentStatement assignment && assignment.Target is NameExpression target)
                    {
                        if (assignment.Operator == "=")
                            vars[target.Name] = TypeOf(assignment.Value, vars, visited);
                        else
                            vars[target.Name] = null;
                    }
                    else if (statement is ForStatement loop)
                    {
                        string iterable = TypeOf(loop.Iterable, vars, visited);
                        vars[loop.Variable] = iterable == "Range" ? "Int" : null;
                        Walk(loop.Body, vars, caret, visited, returned);
                    }
                    else if (statement is WhileStatement whileLoop)
                        Walk(whileLoop.Body, vars, caret, visited, returned);
                    else if (statement is IfStatement conditional)
                    {
                        // Merge only compatible variable types from alternative branches.
                        var branches = conditional.Branches.Select(branch => branch.Body)
                            .Concat(new[] { conditional.ElseBody }).ToArray();
                        var results = new List<Dictionary<string, string>>();
                        foreach (var branch in branches)
                        {
                            var next = new Dictionary<string, string>(vars, StringComparer.Ordinal);
                            Walk(branch, next, caret, visited, returned);
                            results.Add(next);
                        }
                        foreach (string name in results.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal))
                        {
                            var values = results.Select(r => r.TryGetValue(name, out var value) ? value : null).ToArray();
                            vars[name] = values.Distinct(StringComparer.Ordinal).Count() == 1 ? values[0] : null;
                        }
                    }
                    else if (statement is ReturnStatement result && returned != null)
                        returned.Add(result.Value == null ? "None" : TypeOf(result.Value, vars, visited));
                }
            }
        }
    }
}
