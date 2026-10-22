using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;

namespace Directiva.EditorTests.DScript
{
    /// <summary>Program-wide shared slots and deterministic, independently recoverable initializer plans.</summary>
    internal static class DScriptSharedTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "Shared", id, description, action);

            Add("NoInitializer", "A declaration reserves a slot without creating an initialization step", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared action\ndef f():\n    return action\n");
                t.Equal(1, a.GlobalSlots.Count, "slot count");
                t.Equal(0, a.SharedInitializers.Count, "initializers");
                t.Equal("shared.action", a.GlobalSlots[0].QualifiedName, "canonical name");
            });
            Add("LiteralInitializer", "An initializer is retained with its exact source span and symbol", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared value = 9\n");
                var init = a.SharedInitializers.Single();
                t.Equal(0, init.Order, "order");
                t.Equal("test", init.Module, "owner");
                t.Equal(1, init.Span.Line, "source line");
                t.Equal("value", init.Symbol.Name, "name");
                t.Equal(0, init.Symbol.Slot, "slot");
                t.Check(init.Expression is LiteralExpression, "literal AST kept");
                t.Check(object.ReferenceEquals(a.SharedDeclarations[init.Declaration], init.Symbol), "declaration binding");
            });
            Add("FunctionInitializer", "A shared initializer may call a function declared later", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared value = start(7)\ndef start(k):\n    return k+1\n");
                var init = a.SharedInitializers.Single();
                var call = (CallExpression)init.Expression;
                t.Equal("test.start", a.ExpressionSymbols[call.Callee].QualifiedName, "resolved function");
                t.Equal(1, a.ExpressionSymbols[call.Callee].ParameterCount, "arity");
            });
            Add("NativeInitializer", "Native imports and constants resolve in initializer expressions", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from math import sin, pi\nshared value = sin(pi)\n");
                var call = (CallExpression)a.SharedInitializers.Single().Expression;
                t.Equal("math.sin", a.ExpressionSymbols[call.Callee].QualifiedName, "function");
                t.Equal("math.pi", a.ExpressionSymbols[call.Arguments[0]].QualifiedName, "constant");
            });
            Add("BuiltinInitializer", "Builtins are allowed in shared initializer expressions", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared magnitude = abs(-4)\n");
                var call = (CallExpression)a.SharedInitializers.Single().Expression;
                t.Equal("abs", a.ExpressionSymbols[call.Callee].QualifiedName, "builtin");
            });
            Add("InitializerReadsLaterShared", "Forward references are resolved but not prematurely evaluated", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared first = second+1\nshared second = 3\n");
                var first = (BinaryExpression)a.SharedInitializers[0].Expression;
                t.Equal("shared.second", a.ExpressionSymbols[first.Left].QualifiedName, "resolved forward slot");
                t.Equal(1, a.ExpressionSymbols[first.Left].Slot, "sorted storage index");
                t.Equal("first,second", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)),
                    "initialization follows source, not sorted slots");
            });
            Add("OrderWithinModule", "Initializer order is textual and independent of sorted storage slots", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared z = 5\nshared a = 10\nshared k = 20\n");
                t.Equal("a,k,z", string.Join(",", a.GlobalSlots.Select(s => s.Name)), "sorted storage");
                t.Equal("z,a,k", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "execution order");
            });
            Add("ImportInterleaving", "An import expands in-place between declarations", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "shared a = 10\nimport b as module_b\nshared c = b + 5\nshared b\n",
                    new Dictionary<string, string> { ["b"] = "shared a\nshared b = a*2\nimport test\n" });
                t.Equal("a,b,c", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "init order");
                var cInitializer = a.SharedInitializers.Single(s => s.Symbol.Name == "c");
                var cExpression = (BinaryExpression)cInitializer.Expression;
                t.Equal("shared.b", a.ExpressionSymbols[cExpression.Left].QualifiedName,
                    "bare b resolves to the shared variable, not to the imported module");
                t.Equal(3, a.GlobalSlots.Count, "shared slots");
                t.Equal("test,b,test", string.Join(",", a.SharedInitializers.Select(s => s.Module)), "source owners");
            });
            Add("CycleEntrypointChangesOrder", "Starting at B runs B first even though imports are circular", t =>
            {
                var sources = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["test"] = "shared a = 10\nimport b as module_b\nshared c = b + 5\nshared b\n",
                    ["b"] = "shared a\nshared b = a*2\nimport test\n"
                };
                var analysis = DScriptFrontend.Analyze("b", new InMemoryDScriptSourceProvider(sources));
                t.Check(analysis.Success, "unexpected: " + string.Join(" | ", analysis.Diagnostics));
                t.Equal("b,a,c", string.Join(",", analysis.SharedInitializers.Select(s => s.Symbol.Name)),
                    "B-first static execution plan");
                t.Equal("b,test,test", string.Join(",", analysis.SharedInitializers.Select(s => s.Module)),
                    "initializer owner order");
            });
            Add("SharedModuleNameCollision", "Unaliased module import cannot use a declared shared name", t =>
                DScriptTestHarness.Error(t, "shared b\nimport b\n", "DS4004",
                    new Dictionary<string, string> { ["b"] = "def helper():\n    pass\n" }));
            Add("ImportBeforeDeclarations", "An import before local initializers schedules imported initializers first", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import a\nshared later = 2\n",
                    new Dictionary<string, string> { ["a"] = "shared early = 1\n" });
                t.Equal("early,later", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "order");
            });
            Add("FromImportOrders", "from-import expands a dependency at the statement position", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared a = 1\nfrom lib import fun\nshared c = 3\n",
                    new Dictionary<string, string> { ["lib"] = "shared b = 2\ndef fun():\n    pass\n" });
                t.Equal("a,b,c", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "order");
            });
            Add("RepeatedImportOnce", "Multiple imports never duplicate a module's initialization", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import lib\nfrom lib import f\nimport lib\nshared second = 2\n",
                    new Dictionary<string, string> { ["lib"] = "shared first = 1\ndef f():\n    pass\n" });
                t.Equal("first,second", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "once");
            });
            Add("DiamondImportsOnce", "Diamond dependencies schedule a shared initializer only once", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import left\nimport right\n",
                    new Dictionary<string, string> {
                        ["left"] = "import common\nshared left = 2\n",
                        ["right"] = "import common\nshared right = 3\n",
                        ["common"] = "shared common = 1\n" });
                t.Equal("common,left,right", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "order");
            });
            Add("SharedRepeatedDeclaration", "Multiple declarations without initializers are harmless", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared action\nshared action\nshared action = None\n");
                t.Equal(1, a.GlobalSlots.Count, "one slot");
                t.Equal(1, a.SharedInitializers.Count, "one initializer");
                t.Equal(3, a.SharedDeclarations.Count, "all declaration bindings available");
            });
            Add("SameSharedAcrossFiles", "Modules expose the identical canonical shared symbol", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import child\nshared action = None\n",
                    new Dictionary<string, string> { ["child"] = "shared action\n" });
                t.Check(object.ReferenceEquals(a.ModuleSymbols["test"].Globals["action"],
                    a.ModuleSymbols["child"].Globals["action"]), "same canonical symbol instance");
                t.Equal(1, a.GlobalSlots.Count, "one shared slot");
            });
            Add("DuplicateInitializerSameModule", "Two initializers for one name are a compilation error", t =>
                DScriptTestHarness.Error(t, "shared x = 1\nshared x = 2\n", "DS4018"));
            Add("DuplicateInitializerCrossModule", "Two imported initializers for one shared name fail", t =>
                DScriptTestHarness.Error(t, "import lib\nshared x = 1\n",
                    "DS4018", new Dictionary<string, string> { ["lib"] = "shared x = 2\n" }));
            Add("NoImplicitShared", "Assignment without shared declaration creates a local", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def f():\n    local = 1\n");
                t.Equal(0, a.GlobalSlots.Count, "no shared");
                var f = a.FunctionSymbols[DScriptTestHarness.Function(a, "f")];
                t.Equal(SymbolKind.Local, f.Locals["local"].Kind, "local variable");
            });
            Add("ForWritesShared", "For target updates a declared shared instead of creating a local", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared i\ndef f():\n    for i in [1,2]:\n        pass\n");
                var statement = (ForStatement)DScriptTestHarness.Function(a, "f").Body[0];
                t.Equal(SymbolKind.Global, a.ForTargetSymbols[statement].Kind, "target kind");
                t.Equal(0, a.FunctionSymbols[DScriptTestHarness.Function(a, "f")].LocalCount, "no local");
            });
            Add("ParameterConflict", "Parameter cannot silently shadow module shared", t =>
                DScriptTestHarness.Error(t, "shared i\ndef f(i):\n    return i\n", "DS4006"));
            Add("SharedFunctionNameCollision", "Function and shared declaration in one module conflict", t =>
                DScriptTestHarness.Error(t, "def f():\n    pass\nshared f\n", "DS4002"));
            Add("SharedNotImplicitlyImported", "A module must declare or import a shared name to use it", t =>
                DScriptTestHarness.Error(t, "import state\ndef f():\n    return x\n", "DS4016",
                    new Dictionary<string, string> { ["state"] = "shared x\n" }));
            Add("FromImportShared", "From-imported shared symbols reuse the original global slot", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from state import value as imported\ndef f():\n    return imported\n",
                    new Dictionary<string, string> { ["state"] = "shared value = 10\n" });
                var f = DScriptTestHarness.Function(a, "f");
                var target = (NameExpression)((ReturnStatement)f.Body[0]).Value;
                t.Equal(SymbolKind.Global, a.ExpressionSymbols[target].Kind, "global kind");
                t.Equal(0, a.ExpressionSymbols[target].Slot, "canonical slot");
                t.Equal("shared.value", a.ExpressionSymbols[target].QualifiedName, "canonical name");
            });
            Add("FromImportAndDeclaredSameShared", "Importing the same shared declared locally is not a collision", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from state import value\nshared value\n",
                    new Dictionary<string, string> { ["state"] = "shared value = 4\n" });
                t.Equal(1, a.GlobalSlots.Count, "one slot");
                t.Equal(1, a.SharedInitializers.Count, "one initializer");
            });
            Add("FunctionValueInitializer", "A function reference can initialize shared behavior", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared action = miner\ndef miner():\n    pass\n");
                var initial = (NameExpression)a.SharedInitializers.Single().Expression;
                t.Equal(SymbolKind.Function, a.ExpressionSymbols[initial].Kind, "function ref");
                t.Equal("test.miner", a.ExpressionSymbols[initial].QualifiedName, "target id");
            });
            Add("NativeNotAnInitStep", "Importing native modules never schedules an initialization step", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import math\nimport random\nshared x = 0\n");
                t.Equal(1, a.SharedInitializers.Count, "only user shared initializer");
            });
            Add("InitializationPolicyMetadata", "Each ordered initializer has its own diagnostic context", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared bad = missing_value + 1\nshared good = 7\nshared missing_value\n");
                t.Equal(2, a.SharedInitializers.Count, "independent steps");
                t.Equal("bad", a.SharedInitializers[0].Symbol.Name, "first potentially failing expression");
                t.Equal("good", a.SharedInitializers[1].Symbol.Name, "later step remains scheduled");
                t.Equal(1, a.SharedInitializers[0].Span.Line, "bad source span");
                t.Equal(2, a.SharedInitializers[1].Span.Line, "good source span");
                t.Equal(0, a.Diagnostics.Count, "reading uninitialized shared is runtime concern");
            });
            Add("UninitializedCascadesAllowed", "Static binding does not reject possible initialization cascades", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared b = a * 2\nshared a = 10\nshared c = b + 1\n");
                t.Equal("b,a,c", string.Join(",", a.SharedInitializers.Select(s => s.Symbol.Name)), "all steps retained");
                t.Equal(3, a.SharedInitializers.Select(s => s.Symbol.Slot).Distinct().Count(), "each separate slot");
            });
            Add("CyclesResolveFunctions", "Mutually imported modules bind functions across the cycle", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from helper import go\ndef start():\n    return go()\n",
                    new Dictionary<string, string> { ["helper"] = "from test import start\ndef go():\n    return start()\n" });
                var go = (CallExpression)((ReturnStatement)DScriptTestHarness.Function(a, "start").Body[0]).Value;
                t.Equal("helper.go", a.ExpressionSymbols[go.Callee].QualifiedName, "cycle resolution");
                t.Equal(2, a.Modules.Count, "loaded exactly once");
            });
            Add("AbsoluteImportsFromRoot", "Module path is resolved from the program root, never relative to its importer", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import folder.child\n",
                    new Dictionary<string, string> {
                        ["folder.child"] = "import tools\n",
                        ["tools"] = "def helper():\n    pass\n",
                        ["folder.tools"] = "def irrelevant():\n    pass\n" });
                t.Check(a.Modules.ContainsKey("tools"), "root module found");
                t.Check(!a.Modules.ContainsKey("folder.tools"), "relative sibling not imported");
            });
            Add("ImportNameValidation", "Traversal components are invalid in module names", t =>
            {
                t.Check(!DScriptModuleLoader.IsValidName("..outside"), "no parent traversal");
                t.Check(!DScriptModuleLoader.IsValidName("folder..outside"), "no empty path segment");
                t.Check(!DScriptModuleLoader.IsValidName("/absolute"), "no absolute filesystem path");
                t.Check(DScriptModuleLoader.IsValidName("folder.child"), "rooted dotted name");
            });
        }
    }
}
