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

            Add("LiveUndefinedName", "Live analysis highlights undefined names in the current unsaved buffer", t =>
            {
                const string src = "def run():\n    atack(10)\n";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = "def run():\n    pass\n" });
                var issues = DScriptLiveDiagnostics.Analyze("test", src, provider);
                var missing = issues.FirstOrDefault(d => d.Code == "DS4016");
                t.Check(missing != null, "undefined name detected without saving the buffer");
                t.Equal("atack", src.Substring(missing.Span.Start, missing.Span.Length),
                    "only the bad identifier is underlined");
                t.Equal("attack", DScriptLiveDiagnostics.Suggest("test", src, missing, provider),
                    "optional hint comes from visible native methods");
            });

            Add("LiveKnownNames", "Known local names are not marked as undefined", t =>
            {
                const string src = "def run():\n    number = 10\n    print(number)\n";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = src });
                t.Check(!DScriptLiveDiagnostics.Analyze("test", src, provider)
                    .Any(d => d.Code == "DS4016"), "existing names are valid");
            });

            Add("LiveImportedErrorsStayInTheirModules", "The current editor only draws diagnostics from its own file", t =>
            {
                const string main = "import helper\ndef run():\n    pass\n";
                const string broken = "def run():\n    missing_name()\n";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = main, ["helper"] = broken });
                t.Check(!DScriptLiveDiagnostics.Analyze("test", main, provider)
                    .Any(d => d.Code == "DS4016"), "import errors do not decorate the wrong file");
            });

            Add("RealCallableMetadata", "Native and user functions expose call insertion metadata", t =>
            {
                var move = Complete("def run():\n    mo").Items.FirstOrDefault(i => i.Name == "move");
                t.Check(move != null && move.IsCallable && move.TakesArguments,
                    "movement order expects arguments");
                var none = Complete("def run():\n    resource_no").Items.FirstOrDefault(i =>
                    i.Name == "resource_nodes");
                t.Check(none != null && none.IsCallable && !none.TakesArguments,
                    "parameterless native call places caret after parentheses");
                var own = Complete("def custom(value):\n    return value\ndef run():\n    cus")
                    .Items.FirstOrDefault(i => i.Name == "custom");
                t.Check(own != null && own.IsCallable && own.TakesArguments,
                    "user-defined parameters are available to completion");
            });

            Add("CompletionCallParentheses", "Callable suggestions insert parentheses and place the caret correctly", t =>
            {
                var withArgs = new DScriptCompletionItem("move", "method", "(x, y)", "Bool", true, true);
                var noArgs = new DScriptCompletionItem("resource_nodes", "function", "()", "List", true, false);
                var a = CodeEditorCompletionText.Build("mo", 0, 2, withArgs, false);
                t.Equal("move()", a.Text, "method call");
                t.Equal(5, a.Caret, "cursor inside argument list");
                var b = CodeEditorCompletionText.Build("resource_no", 0, 11, noArgs, false);
                t.Equal("resource_nodes()", b.Text, "parameterless call");
                t.Equal(b.Text.Length, b.Caret, "cursor past final parenthesis");
                var c = CodeEditorCompletionText.Build("mo(10,20)", 0, 2, withArgs, false);
                t.Equal("move(10,20)", c.Text, "no duplicate opening or closing parentheses");
                var d = CodeEditorCompletionText.Build("mo", 0, 2, withArgs, true);
                t.Equal("move( )", d.Text, "Space accepts and inserts inside the call");
                t.Equal(6, d.Caret, "cursor after inserted space");
            });

            Add("EditorSettingsAreIndependent", "Preferences survive defensive copies without modifying DScript", t =>
            {
                var settings = Directiva.Settings.GameSettings.CreateDefaults(1280, 720);
                t.Check(settings.editor.showCompletion && settings.editor.showLiveErrors,
                    "completion and errors enabled initially");
                t.Equal(0.5f, settings.editor.liveErrorDelaySeconds, "diagnostic delay default");
                settings.editor.showErrorSuggestions = true;
                var copy = settings.Copy();
                copy.editor.showCompletion = false;
                t.Check(settings.editor.showCompletion && copy.editor.showErrorSuggestions,
                    "editor settings have their own copy");
            });

            Add("SignatureParametersFromSimulation", "A call displays script-facing parameter names and types", t =>
            {
                const string src = "def run():\n    move(";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = src });
                var info = DScriptCallSignatureHelp.Get("test", src, src.Length, provider);
                t.Check(info != null, "implicit self method shows a signature despite an open call");
                t.Equal("move", info.Name, "order name");
                t.Equal(2, info.Parameters.Count, "position overload has two parameters");
                t.Equal("x: Decimal", info.Parameters[0], "first parameter uses DScript type");
                t.Equal("y: Decimal", info.Parameters[1], "second parameter uses DScript type");
                t.Equal(0, info.ActiveParameter, "first argument is active");

                const string member = "def run():\n    self.move(10, ";
                var next = DScriptCallSignatureHelp.Get("test", member, member.Length, provider);
                t.Check(next != null, "explicit self call is recognized");
                t.Equal(1, next.ActiveParameter, "comma advances active argument");
                t.Equal("y: Decimal", next.Parameters[1], "second parameter label");
            });

            Add("SignatureSimulationEnums", "Signature help uses actual DScript enum namespaces, not CLR integers", t =>
            {
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = "def run():\n    pass\n" });
                const string allies = "def run():\n    allies(";
                var a = DScriptCallSignatureHelp.Get("test", allies, allies.Length, provider);
                t.Check(a != null, "allies signature is visible");
                t.Equal("entityType: Type?", a.Parameters[0], "entity filter uses script Type enum");
                t.Check(a.ActiveArgumentChoices.Contains("Type.Unit") &&
                    a.ActiveArgumentChoices.Contains("Type.Nexus") &&
                    a.ActiveArgumentChoices.Contains("Type.Resource"),
                    "the hint shows the real script-qualified alternatives");
                t.Check(!a.ActiveArgumentChoices.Contains("SimulationEntityType"),
                    "CLR names are not leaked to the player");

                const string side = "def run():\n    entities(";
                var b = DScriptCallSignatureHelp.Get("test", side, side.Length, provider);
                t.Check(b != null, "entities overload is recognized");
                t.Equal("side: Side", b.Parameters[0], "side filter uses Side enum");
                t.Check(b.ActiveArgumentChoices.Contains("Side.Enemy") &&
                    b.ActiveArgumentChoices.Contains("Side.Ally"),
                    "side values use their native DScript names");
                const string explicitType = "def run():\n    entities(Type.Unit";
                var typeOverload = DScriptCallSignatureHelp.Get("test", explicitType,
                    explicitType.Length, provider);
                t.Check(typeOverload != null && typeOverload.Parameters.Count == 1,
                    "explicit Type.Unit selects the one-argument overload");
                t.Equal("entityType: Type?", typeOverload.Parameters[0],
                    "explicit enum syntax chooses the correct overload");

                var returned = DScriptCompletionEngine.Complete("test",
                    "def run():\n    self.move_mo", "def run():\n    self.move_mo".Length, provider);
                t.Check(returned.Items.Any(i => i.Name == "move_mode" && i.Type == "MoveMode"),
                    "enum-valued observations are not mislabeled Int");
            });

            Add("SignaturePopupBelowCaret", "Signature help appears below the caret when space allows", t =>
            {
                var caret = new UnityEngine.Vector2(240f, 90f);
                var size = new UnityEngine.Vector2(280f, 90f);
                var panel = new UnityEngine.Vector2(800f, 600f);
                var pos = DScriptSignaturePopupPlacement.Resolve(caret, 20f, size, panel);
                t.Equal(100f, pos.x, "popup is centered below the caret, not right-aligned");
                t.Equal(116f, pos.y, "popup is below the current line");
                var bottom = DScriptSignaturePopupPlacement.Resolve(
                    new UnityEngine.Vector2(240f, 550f), 20f, size, panel);
                t.Check(bottom.y < 550f, "fallback above only near the panel bottom");
                var right = DScriptSignaturePopupPlacement.Resolve(
                    new UnityEngine.Vector2(790f, 90f), 20f, size, panel);
                t.Check(right.x + size.x <= panel.x - 3f, "popup stays inside the right edge");
            });

            Add("SignatureUserAndImports", "Optional type hints and imported native signatures are visible", t =>
            {
                const string src = "def custom(value: Int, ready: Bool) -> Bool:\n    return ready\ndef run():\n    custom(4, ";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = src });
                var info = DScriptCallSignatureHelp.Get("test", src, src.Length, provider);
                t.Check(info != null, "user function discovered from recoverable source");
                t.Equal("value: Int", info.Parameters[0], "explicit first parameter hint");
                t.Equal("ready: Bool", info.Parameters[1], "explicit second parameter hint");
                t.Equal("Bool", info.ReturnType, "return hint");

                const string imported = "import math\ndef run():\n    math.atan2(1, ";
                var native = DScriptCallSignatureHelp.Get("test", imported, imported.Length, provider);
                t.Check(native != null, "imported math function signature found");
                t.Equal("atan2", native.Name, "native name");
                t.Equal(1, native.ActiveParameter, "native argument index");
                t.Equal("x", native.Parameters[1], "parameter name from reflection");
            });

            Add("SignatureNestedAndLiterals", "Nested calls track their own comma depth and ignore strings", t =>
            {
                const string nested = "def run():\n    move(math.atan2(10, 20), ";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = nested });
                var result = DScriptCallSignatureHelp.Get("test", nested, nested.Length, provider);
                t.Check(result != null && result.Name == "move", "outer call survives nested commas");
                t.Equal(1, result.ActiveParameter, "outer call uses the comma after nested call");
                const string stringCall = "def run():\n    move(\"hello, world\", ";
                result = DScriptCallSignatureHelp.Get("test", stringCall, stringCall.Length, provider);
                t.Check(result != null, "string literal does not confuse delimiter scan");
                t.Equal(1, result.ActiveParameter, "comma inside string does not count");
                t.Check(DScriptCallSignatureHelp.Get("test", "def user(", 9, provider) == null,
                    "function definition is not a call");
            });

            Add("CaretAwareIncompleteCall", "Unclosed calls are marked only after the player leaves them", t =>
            {
                const string src = "def run():\n    move(\n\n    print('hello')\n\n    # farther down\n";
                var provider = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                    { ["test"] = src });
                int paren = src.IndexOf("move(", StringComparison.Ordinal) + 4;
                var near = DScriptLiveDiagnostics.Analyze("test", src, provider, paren + 1);
                t.Check(!near.Any(i => i.Code == "DS1006" && i.Span.Start == paren),
                    "typing the arguments does not cause a speculative underline");
                var far = DScriptLiveDiagnostics.Analyze("test", src, provider, src.Length);
                t.Check(far.Any(i => i.Code == "DS1006" && i.Span.Start == paren && i.Span.Length == 1),
                    "abandoned opening parenthesis is highlighted, not just end-of-file");
                const string continued = "def run():\n    move(\n        10,\n        20";
                t.Check(!DScriptLiveDiagnostics.Analyze("test", continued, provider, continued.Length)
                    .Any(i => i.Code == "DS1006" && i.Span.Length == 1),
                    "in-progress multiline call is not flagged");
                const string closed = "def run():\n    move(\n        10,\n        20)\n    print('ok')\n";
                t.Check(!DScriptLiveDiagnostics.Analyze("test", closed, provider, closed.Length)
                    .Any(i => i.Code == "DS1006"), "completed multiline call remains valid");
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
