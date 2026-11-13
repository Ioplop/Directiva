using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Directiva.CodeUI;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DSExecution.Native;

namespace Directiva.EditorTests.DScript
{
    internal static class DScriptCompletionTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string name, string detail, Action<DirectivaTestContext> run) =>
                DScriptTestHarness.Add(tests, "Completion", name, detail, run);

            DScriptCompletionResult Complete(string source, string module = "test",
                Dictionary<string, string> imported = null)
            {
                var modules = new Dictionary<string, string>(StringComparer.Ordinal)
                    { [module] = source };
                if (imported != null)
                    foreach (var pair in imported) modules[pair.Key] = pair.Value;
                return DScriptCompletionEngine.Complete(module, source, source.Length,
                    new InMemoryDScriptSourceProvider(modules));
            }

            Add("OptionalTypeHints", "Optional parameter and return types survive parsing without changing runtime semantics", t =>
            {
                const string source = "def find(target: SimulationObject, count: Int) -> SimulationObject:\n    return target\n";
                var root = DScriptTestHarness.Parse(t, source, out var errors);
                t.Equal(0, errors.Count, "syntax errors");
                var function = (FunctionStatement)root.Statements[0];
                t.Equal(2, function.Parameters.Count, "parameter count");
                t.Equal("SimulationObject", function.ParameterTypes["target"], "parameter hint");
                t.Equal("Int", function.ParameterTypes["count"], "second hint");
                t.Equal("SimulationObject", function.ReturnType, "return hint");
                var analysis = DScriptTestHarness.Ok(t, source);
                t.Check(analysis.Success, "types are not runtime requirements");
            });

            Add("VisibleNames", "Completes locals, shared state, user functions, enum modules and builtins", t =>
            {
                const string source = "shared cargo = 2\ndef helper():\n    pass\ndef run(x: Int):\n    cargo_total = x\n    ca";
                var suggestions = Complete(source).Items.Select(i => i.Name).ToArray();
                t.Check(suggestions.Contains("cargo"), "shared variable");
                t.Check(suggestions.Contains("cargo_total"), "local variable");
                t.Check(!suggestions.Contains("random"), "irrelevant prefixes filtered");
                var names = Complete("def run():\n    pr").Items.Select(i => i.Name).ToArray();
                t.Check(names.Contains("print"), "builtin completion");
                t.Check(Complete("def run():\n    Ty").Items.Any(i => i.Name == "Type"), "enum module");
            });

            Add("ModuleScopeDeclarations", "At root, only valid module declarations are suggested", t =>
            {
                const string source = "def encontrar_recurso():\n    pass\n\nen";
                var names = Complete(source).Items.Select(i => i.Name).ToArray();
                t.Equal(0, names.Length, "an arbitrary root expression does not suggest user functions");
                t.Check(Complete("de").Items.Any(i => i.Name == "def"), "def is offered at root");
                t.Check(Complete("sha").Items.Any(i => i.Name == "shared"), "shared is offered at root");
                t.Check(Complete("imp").Items.Any(i => i.Name == "import"), "import is offered at root");
                t.Check(Complete("fro").Items.Any(i => i.Name == "from"), "from is offered at root");
                t.Equal(0, Complete("glo").Items.Count, "global is not yet supported in DScript");
                t.Equal(0, Complete("pri").Items.Count, "print is not a valid top-level statement");
                t.Equal(0, Complete("self.cl").Items.Count, "self members cannot be used at root");
            });

            Add("ModuleScopeContexts", "Top-level declarations still complete types, imports and shared initializers", t =>
            {
                var typed = Complete("def find(target: Sim").Items.Select(i => i.Name).ToArray();
                t.Check(typed.Contains("SimulationObject"), "function parameter type hint");
                var imported = Complete("from math import si").Items.Select(i => i.Name).ToArray();
                t.Check(imported.Contains("sin"), "from-import symbol");
                var initialized = Complete("shared items = li").Items.Select(i => i.Name).ToArray();
                t.Check(initialized.Contains("list"), "function call inside shared initializer");
                t.Check(!Complete("shared value = re").Items.Any(i => i.Name == "return"),
                    "return cannot be a module-level initializer");
                t.Equal(0, Complete("def run").Items.Count, "no expressions inside a function name");
                t.Check(!Complete("shared target = sel").Items.Any(i => i.Name == "self"),
                    "self is unavailable in a shared initializer");
            });

            Add("FunctionBodyCompletionsRemain", "Unindented lines stop suggesting function names but valid suites still do", t =>
            {
                const string source =
                    "def encontrar_recurso():\n    pass\ndef run():\n    en";
                t.Check(Complete(source).Items.Any(i => i.Name == "encontrar_recurso"),
                    "user function is suggested inside another function");
                t.Check(Complete("def run():\n    pr").Items.Any(i => i.Name == "print"),
                    "native functions remain available in a suite");
                t.Check(!Complete("def run():\n    pass\npr").Items.Any(i => i.Name == "print"),
                    "dedent stops expression completions even after a previous function");
            });

            Add("EnumMembers", "Reflects enum constants after the dot", t =>
            {
                var result = Complete("def run():\n    Type.");
                t.Check(result.Items.Any(i => i.Name == "Unit"), "Type.Unit");
                t.Check(result.Items.Any(i => i.Name == "Nexus"), "Type.Nexus");
            });

            Add("ImportAndNative", "Import aliases expose the correct native module members", t =>
            {
                var result = Complete("import math as m\ndef run():\n    m.si");
                t.Check(result.Items.Any(i => i.Name == "sin" && i.Type == "Decimal"), "math.sin() metadata");
                var imported = Complete("from random import ra");
                t.Check(imported.Items.Any(i => i.Name == "randint"), "from-import incomplete input");
            });

            Add("NativeReturnedCollection", "Infers type after native calls and recommends collection members", t =>
            {
                var result = Complete("def run():\n    items = list()\n    items.ap");
                t.Check(result.Items.Any(i => i.Name == "append"), "List.append");
                t.Check(!result.Items.Any(i => i.Name == "discard"), "exclude Set method");
                t.Check(Complete("def run():\n    list().le").Items.Any(i => i.Name == "length"),
                    "direct call chains");
            });

            Add("ImplicitSelfAndEntities", "Self, implicit observations and entity-returning calls offer entity members", t =>
            {
                var self = Complete("def run():\n    self.cl");
                t.Check(self.Items.Any(i => i.Name == "closest_enemy"), "simulation member catalog");
                var implicitCall = Complete("def run():\n    other = closest_enemy()\n    other.at");
                t.Check(implicitCall.Items.Any(i => i.Name == "attack_range"), "return from implicit self observation");
            });

            Add("UserFunctionInference", "Infers simple returns from user functions without an explicit hint", t =>
            {
                const string src = "def make():\n    return list()\ndef run():\n    result = make()\n    result.ap";
                t.Check(Complete(src).Items.Any(i => i.Name == "append"), "return from make()");
            });

            Add("UserReturnAnnotation", "Explicit return hint supports dynamic and conditional function bodies", t =>
            {
                const string src = "def choose(flag) -> SimulationObject:\n    if flag:\n        return self.closest_enemy()\n    return None\ndef run():\n    target = choose(True)\n    target.he";
                t.Check(Complete(src).Items.Any(i => i.Name == "health"), "return annotation");
            });

            Add("ConflictingReturnUnknown", "Conflicting returns do not invent an object's members", t =>
            {
                const string src = "def choose(flag):\n    if flag:\n        return list()\n    return set()\ndef run():\n    something = choose(True)\n    something.ap";
                t.Check(!Complete(src).Items.Any(i => i.Name == "append"), "unknown type suppresses List methods");
            });

            Add("ImportedUserReturn", "Follows annotations through module aliases without executing imports", t =>
            {
                var imports = new Dictionary<string, string> { ["lib"] =
                    "def get_list() -> List:\n    return []\n" };
                var result = Complete("import lib as l\ndef run():\n    values = l.get_list()\n    values.ap", "test", imports);
                t.Check(result.Items.Any(i => i.Name == "append"), "imported function return");
            });

            Add("IncompleteCode", "Completion works before a DScript program compiles", t =>
            {
                var result = Complete("def run():\n    value = list()\n    value.");
                t.Check(result.Items.Any(i => i.Name == "pop"), "list members despite dangling dot");
            });

            Add("NoCommentsOrStrings", "Do not offer completions inside comments or string literals", t =>
            {
                t.Equal(0, Complete("def run():\n    # sel").Items.Count, "comment");
                t.Equal(0, Complete("def run():\n    text = 'sel").Items.Count, "string");
            });

            Add("TypedFunctionCompiles", "Optional type hints also compile into executable DIL", t =>
            {
                const string source = "def identity(value: Int) -> Int:\n    return value\ndef main():\n    return identity(42)\n";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = source });
                var compilation = DScriptCompiler.Compile("test", provider);
                t.Check(compilation.Success, "typed functions compile without a runtime type system: " +
                    string.Join(" | ", compilation.Diagnostics));
            });

            Add("IncompleteFromImport", "Offers exports even before a from-import clause is complete", t =>
            {
                var imported = new Dictionary<string, string>
                    { ["helper"] = "def make_list():\n    return list()\nshared amount = 4\n" };
                var candidates = Complete("from helper import ma", "test", imported);
                t.Check(candidates.Items.Any(i => i.Name == "make_list"), "complete imported function");
            });

            Add("PartialReturnUnknown", "Implicit None makes a partially-returning function ambiguous", t =>
            {
                const string src = "def maybe(flag):\n    if flag:\n        return list()\ndef run():\n    x = maybe(True)\n    x.ap";
                t.Check(!Complete(src).Items.Any(i => i.Name == "append"),
                    "a missing return branch cannot guarantee List");
            });

            Add("CyclicSharedTypes", "Cyclic shared initializers cannot recurse infinitely in the editor", t =>
            {
                const string src = "shared a = b\nshared b = a\ndef run():\n    a";
                t.Check(Complete(src).Items != null, "completion safely handles cyclic shared types");
            });

            Add("ShadowingDoesNotInventTypes", "A local named like a builtin is not treated as that builtin", t =>
            {
                const string src = "def run():\n    list = None\n    x = list()\n    x.ap";
                t.Check(!Complete(src).Items.Any(i => i.Name == "append"),
                    "shadowed list() is not assumed to return a List");
            });

            Add("CompletionSelectionOptIn", "Completion stays unselected until arrow navigation", t =>
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                var navigation = typeof(CodeEditorView).GetMethod("StepCompletionSelection", flags);
                var canCommit = typeof(CodeEditorView).GetMethod("HasCompletionSelection", flags);
                t.Check(navigation != null && canCommit != null, "editor navigation policy exists");
                var selection = typeof(CodeEditorView).GetField("_completionSelected",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                t.Check(selection != null, "editor selection state exists");
                var editor = new CodeEditorView(null, null, 14f, 0f, 0f, 0f, 0f, 0f);
                t.Equal(-1, (int)selection.GetValue(editor), "popup starts without a selected row");

                int Step(int current, int count, bool down) =>
                    (int)navigation.Invoke(null, new object[] { current, count, down });
                bool Selected(int current, int count) =>
                    (bool)canCommit.Invoke(null, new object[] { current, count });

                t.Check(!Selected(-1, 3), "no implicit completion on Enter or Space");
                t.Equal(0, Step(-1, 3, true), "first Down selects first suggestion");
                t.Equal(2, Step(-1, 3, false), "first Up selects last suggestion");
                t.Equal(1, Step(0, 3, true), "Down advances selection");
                t.Equal(0, Step(2, 3, true), "Down wraps to beginning");
                t.Equal(2, Step(0, 3, false), "Up wraps to end");
                t.Check(Selected(0, 3), "arrow-selected suggestion can be accepted");
                t.Equal(-1, Step(-1, 0, true), "empty lists never select an item");
                t.Check(!Selected(3, 3), "out-of-range selection cannot be accepted");
            });

            Add("NativeMetadata", "Native signatures are script-facing, not unhelpful CLR NativeResult", t =>
            {
                t.Check(NativeLibrary.TryGetFunctionSignature("math.sin", out var sin), "math.sin exists");
                t.Equal("Decimal", sin.ReturnType, "math.sin return");
                t.Check(NativeLibrary.TryGetFunctionSignature("list", out var list), "list exists");
                t.Equal("List", list.ReturnType, "list return");
            });
        }
    }
}
