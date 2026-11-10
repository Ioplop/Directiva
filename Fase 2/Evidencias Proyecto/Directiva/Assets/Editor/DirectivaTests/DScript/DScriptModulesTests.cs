using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Semantics;

namespace Directiva.EditorTests.DScript
{
    internal static class DScriptModulesTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "Modules", id, description, action);

            Add("BuiltinEnumNamespacesNotImports", "Enum constants resolve without polluting explicit module imports", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "def side():\n    return Side.Enemy\n" +
                    "def kind():\n    return Type.Unit\n" +
                    "def movement():\n    return MoveMode.Follow\n" +
                    "def action():\n    return ActionMode.Attack\n");
                t.Equal(0, a.ModuleSymbols["test"].Imports.Count, "no implicit imports");
                var compiled = DSCompilation.DScript.Backend.DScriptCompiler.Compile(a);
                t.Check(compiled.Success, "implicit enum constants must compile to DIL: " +
                    string.Join("; ", compiled.Diagnostics));
                foreach (var (function, qualified) in new[]
                {
                    ("side", "Side.Enemy"), ("kind", "Type.Unit"),
                    ("movement", "MoveMode.Follow"), ("action", "ActionMode.Attack")
                })
                {
                    var attribute = ((ReturnStatement)DScriptTestHarness.Function(a, function).Body[0]).Value;
                    t.Equal(qualified, a.ExpressionSymbols[attribute].QualifiedName, "built-in enum constant " + function);
                    t.Equal(SymbolKind.NativeConstant, a.ExpressionSymbols[attribute].Kind,
                        "enum constant symbol kind " + function);
                }
            });
            Add("ExplicitEnumImportsCountOnce", "Explicit native enum imports count only as declared", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import Side\nfrom Type import Unit\ndef f():\n    return Side.Enemy\n");
                t.Equal(2, a.ModuleSymbols["test"].Imports.Count, "two explicit bindings");
                t.Equal("Side", a.ModuleSymbols["test"].Imports["Side"].QualifiedName, "explicit Side module");
                t.Equal("Type.Unit", a.ModuleSymbols["test"].Imports["Unit"].QualifiedName, "explicit enum constant");
            });
            Add("BuiltinEnumShadowing", "Explicit aliases and declarations override implicit enum namespaces", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import math as Side\ndef f():\n    return Side.pi\n");
                t.Equal(1, a.ModuleSymbols["test"].Imports.Count, "one aliased import");
                var attribute = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value;
                t.Equal("math.pi", a.ExpressionSymbols[attribute].QualifiedName, "explicit import wins");

                var b = DScriptTestHarness.Ok(t,
                    "def Side():\n    return 7\ndef f():\n    return Side()\n");
                t.Equal(0, b.ModuleSymbols["test"].Imports.Count, "declaration is not an import");
                var call = ((ReturnStatement)DScriptTestHarness.Function(b, "f").Body[0]).Value as CallExpression;
                t.Equal("test.Side", b.ExpressionSymbols[call.Callee].QualifiedName, "function declaration wins");
            });
            Add("NativeMath", "math is a reserved native module, not a disk source", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import math\ndef f():\n    return math.sin(math.pi)\n");
                t.Equal(1, a.Modules.Count, "user modules count");
                var expr = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("math.sin", a.ExpressionSymbols[expr.Callee].QualifiedName, "resolved native function");
                t.Equal("math.pi", a.ExpressionSymbols[expr.Arguments[0]].QualifiedName, "resolved native constant");
            });
            Add("NativeRandom", "random native functions are members of imported module", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import random\ndef f():\n    return random.randint(1, 6)\n");
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("random.randint", a.ExpressionSymbols[call.Callee].QualifiedName, "native random function");
            });
            Add("AliasedNative", "from-import aliases bind to native functions", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from math import cos as c\ndef f():\n    return c(0)\n");
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("math.cos", a.ExpressionSymbols[call.Callee].QualifiedName, "aliased function");
            });
            Add("WildcardNative", "from math import * brings public functions/constants into scope", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from math import *\ndef f():\n    return sin(pi)\n");
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("math.sin", a.ExpressionSymbols[call.Callee].QualifiedName, "star imported function");
                t.Equal("math.pi", a.ExpressionSymbols[call.Arguments[0]].QualifiedName, "star imported constant");
            });
            Add("FromUserModule", "A from-import binds a function by full unique module path", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from utils.calc import square as sq\ndef f():\n    return sq(4)\n",
                    new Dictionary<string, string> { ["utils.calc"] = "def square(x):\n    return x*x\n" });
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("utils.calc.square", a.ExpressionSymbols[call.Callee].QualifiedName, "qualified function");
                t.Equal(2, a.Modules.Count, "module count");
            });
            Add("ShortModuleName", "import utils.calc exposes calc.square by the filename", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import utils.calc\ndef f():\n    return calc.square(3)\n",
                    new Dictionary<string, string> { ["utils.calc"] = "def square(x):\n    return x*x\n" });
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("utils.calc.square", a.ExpressionSymbols[call.Callee].QualifiedName, "qualified module function");
                t.Equal("utils.calc", a.ModuleSymbols["test"].Imports["calc"].QualifiedName, "full source module identity");
                t.Check(!a.ModuleSymbols["test"].Imports.ContainsKey("utils"), "parent path is not a namespace alias");
            });
            Add("ModuleAlias", "import utils.calc as c exposes c.square", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import utils.calc as c\ndef f():\n    return c.square(3)\n",
                    new Dictionary<string, string> { ["utils.calc"] = "def square(x):\n    return x*x\n" });
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("utils.calc.square", a.ExpressionSymbols[call.Callee].QualifiedName, "renamed namespace");
            });
            Add("UnaliasedPathIsNotNamespace", "Importing a file does not implicitly bind its parent directories", t =>
                DScriptTestHarness.Error(t,
                    "import utils.calc\ndef f():\n    return utils.calc.square(3)\n", "DS4016",
                    new Dictionary<string, string> { ["utils.calc"] = "def square(x):\n    return x*x\n" }));
            Add("SameBasenameCollision", "Distinct imported files with the same basename require aliases", t =>
            {
                var a = DScriptTestHarness.Analyze(t,
                    "import alpha.calc\nimport beta.calc\ndef f():\n    return calc.value()\n",
                    new Dictionary<string, string> {
                        ["alpha.calc"] = "def value():\n    return 1\n",
                        ["beta.calc"] = "def value():\n    return 2\n" });
                var conflict = a.Diagnostics.Single(d => d.Code == "DS4030");
                t.Equal("test", conflict.Module, "diagnostic belongs to importer");
                t.Equal(2, conflict.Span.Line, "second import is highlighted");
                t.Equal("module_name_collision", conflict.LocalizationKey, "translatable diagnostic");
                t.Equal("calc", conflict.LocalizationArguments[0], "colliding basename");
                t.Equal("alpha.calc", conflict.LocalizationArguments[1], "first module");
                t.Equal("beta.calc", conflict.LocalizationArguments[2], "second module");
            });
            Add("SameBasenameAliased", "Explicit aliases disambiguate equally named modules", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import alpha.calc as first\nimport beta.calc as second\ndef f():\n    return first.value() + second.value()\n",
                    new Dictionary<string, string> {
                        ["alpha.calc"] = "def value():\n    return 1\n",
                        ["beta.calc"] = "def value():\n    return 2\n" });
                var expression = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as BinaryExpression;
                t.Equal("alpha.calc.value", a.ExpressionSymbols[((CallExpression)expression.Left).Callee].QualifiedName,
                    "first module resolved uniquely");
                t.Equal("beta.calc.value", a.ExpressionSymbols[((CallExpression)expression.Right).Callee].QualifiedName,
                    "second module resolved uniquely");
            });
            Add("OneAliasDisambiguates", "Only one colliding module needs an explicit alias", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import alpha.calc\nimport beta.calc as other\ndef f():\n    return calc.value() + other.value()\n",
                    new Dictionary<string, string> {
                        ["alpha.calc"] = "def value():\n    return 1\n",
                        ["beta.calc"] = "def value():\n    return 2\n" });
                t.Equal(2, a.ModuleSymbols["test"].Imports.Count, "two distinct names");
            });
            Add("RepeatedModuleIsIdempotent", "Repeating an import of the same module is not ambiguous", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import alpha.calc\nimport alpha.calc\ndef f():\n    return calc.value()\n",
                    new Dictionary<string, string> { ["alpha.calc"] = "def value():\n    return 1\n" });
                t.Equal(1, a.ModuleSymbols["test"].Imports.Count, "same binding added only once");
            });
            Add("DuplicateExplicitModuleAlias", "Two different modules cannot reuse the same explicit alias", t =>
                DScriptTestHarness.Error(t,
                    "import alpha.calc as c\nimport beta.calc as c\n", "DS4030",
                    new Dictionary<string, string> {
                        ["alpha.calc"] = "def value():\n    return 1\n",
                        ["beta.calc"] = "def value():\n    return 2\n" }));
            Add("ShortNameDeclarationCollision", "A short module name cannot collide with a local declaration", t =>
                DScriptTestHarness.Error(t, "import alpha.calc\ndef calc():\n    pass\n", "DS4004",
                    new Dictionary<string, string> { ["alpha.calc"] = "def value():\n    return 1\n" }));
            Add("ImportNamesAreModuleScoped", "Different importers can use the same basename independently", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "from x import call_x\nfrom y import call_y\ndef f():\n    return call_x() + call_y()\n",
                    new Dictionary<string, string> {
                        ["x"] = "import alpha.calc\ndef call_x():\n    return calc.value()\n",
                        ["y"] = "import beta.calc\ndef call_y():\n    return calc.value()\n",
                        ["alpha.calc"] = "def value():\n    return 1\n",
                        ["beta.calc"] = "def value():\n    return 2\n" });
                t.Equal("alpha.calc", a.ModuleSymbols["x"].Imports["calc"].QualifiedName, "x sees alpha");
                t.Equal("beta.calc", a.ModuleSymbols["y"].Imports["calc"].QualifiedName, "y sees beta");
            });
            Add("FromUserWildcard", "User module wildcard imports functions and globals", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from util import *\ndef f():\n    return add(2, 3) + count\n",
                    new Dictionary<string, string> { ["util"] = "shared count = 5\ndef add(a,b):\n    return a+b\n" });
                var plus = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as BinaryExpression;
                var call = plus.Left as CallExpression;
                t.Equal("util.add", a.ExpressionSymbols[call.Callee].QualifiedName, "function");
                t.Equal("shared.count", a.ExpressionSymbols[plus.Right].QualifiedName, "shared variable");
            });
            Add("NoPrivateWildcard", "Wildcard excludes underscore-prefixed symbols", t =>
                DScriptTestHarness.Error(t, "from util import *\ndef f():\n    return _secret()\n", "DS4016",
                    new Dictionary<string, string> { ["util"] = "def _secret():\n    return 1\n" }));
            Add("ExplicitPrivate", "Explicit import can bind a private function", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from util import _secret\ndef f():\n    return _secret()\n",
                    new Dictionary<string, string> { ["util"] = "def _secret():\n    return 1\n" });
                t.Check(a.Success, "explicit import works");
            });
            Add("BuiltinShadowedByImport", "Explicit import can intentionally shadow a builtin", t =>
            {
                var a = DScriptTestHarness.Ok(t, "from math import sin as print\ndef f():\n    return print(0)\n");
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("math.sin", a.ExpressionSymbols[call.Callee].QualifiedName, "import alias takes precedence");
            });
            Add("MissingSource", "Missing .dscript modules fail before semantic analysis", t =>
                DScriptTestHarness.Error(t, "import vanished\ndef f():\n    pass\n", "DS3001"));
            Add("CircularDirect", "Direct import cycle is legal and each module is loaded once", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import util\ndef f():\n    pass\n",
                    new Dictionary<string, string> { ["util"] = "import test\ndef f():\n    pass\n" });
                t.Equal(2, a.Modules.Count, "all cycle modules loaded exactly once");
                t.Equal(2, a.DependenciesFirst.Count, "all cycle members present");
            });
            Add("CircularTransitive", "Three modules may import one another", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import a\ndef f():\n    pass\n",
                    new Dictionary<string, string> { ["a"] = "import b\ndef f():\n    pass\n",
                        ["b"] = "import test\ndef f():\n    pass\n" });
                t.Equal(3, a.Modules.Count, "transitive cycle modules");
                t.Equal(3, a.DependenciesFirst.Distinct(StringComparer.Ordinal).Count(), "unique order entries");
            });
            Add("MissingFromName", "Missing symbol in imported module is a semantic error", t =>
                DScriptTestHarness.Error(t, "from math import nonexistent\ndef f():\n    pass\n", "DS4003"));
            Add("DuplicateAlias", "Two imports cannot claim the same alias", t =>
                DScriptTestHarness.Error(t, "from math import sin as trig\nfrom math import cos as trig\ndef f():\n    pass\n", "DS4004"));
            Add("ImportDeclarationCollision", "Local module declarations cannot collide with import names", t =>
                DScriptTestHarness.Error(t, "import math\ndef math():\n    pass\n", "DS4004"));
            Add("MissingMember", "Missing native module member is an error", t =>
                DScriptTestHarness.Error(t, "import math\ndef f():\n    return math.nope\n", "DS4015"));
            Add("ModuleAsValue", "Modules are compile-time namespaces, not callable object values", t =>
                DScriptTestHarness.Error(t, "import math\ndef f():\n    return math\n", "DS4013"));
            Add("ModuleAssign", "Cannot overwrite native module constants", t =>
                DScriptTestHarness.Error(t, "import math\ndef f():\n    math.pi = 3\n", "DS4012"));
            Add("NestedImportNotLoaded", "Imports inside functions are not valid scope declarations", t =>
                DScriptTestHarness.Error(t, "def f():\n    import math\n", "DS4010"));
            Add("NamespaceNotImported", "A member outside the imported module is diagnosed", t =>
                DScriptTestHarness.Error(t, "import util.mod\ndef f():\n    return mod.other.value\n", "DS4015",
                    new Dictionary<string, string> { ["util.mod"] = "def value():\n    return 1\n" }));
            Add("DependencyOrder", "Dependencies-first graph traversal is deterministic", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import a\ndef f():\n    pass\n",
                    new Dictionary<string, string> { ["a"] = "import b\ndef f():\n    pass\n",
                        ["b"] = "def f():\n    pass\n" });
                t.Equal("b,a,test", string.Join(",", a.DependenciesFirst), "compilation order");
            });
            Add("DistinctModuleBasenames", "Different module basenames are independently accessible", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import util.one\nimport util.two\ndef f():\n    return one.a() + two.b()\n",
                    new Dictionary<string, string> { ["util.one"] = "def a():\n    return 1\n",
                        ["util.two"] = "def b():\n    return 2\n" });
                t.Equal(3, a.Modules.Count, "module count");
                t.Equal(2, a.ModuleSymbols["test"].Imports.Count, "independent names");
            });
            Add("MaximumModules", "Imported module graph obeys a bounded module count", t =>
            {
                var a = DScriptTestHarness.Analyze(t, "import a\ndef f():\n    pass\n",
                    new Dictionary<string, string> { ["a"] = "def f():\n    pass\n" },
                    new DScriptFrontendOptions(maxModules: 1));
                t.Check(a.Diagnostics.Any(d => d.Code == "DS3004"), "module limit");
            });
            Add("InvalidEntryName", "An entry module may not traverse paths", t =>
            {
                var result = DSCompilation.DScript.DScriptFrontend.Analyze("../test",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>()));
                t.Check(result.Diagnostics.Any(d => d.Code == "DS3000"), "name validation");
            });
            Add("FilesystemProvider", "Filesystem provider reads ordinal names and blocks invalid paths", t =>
            {
                string root = Path.Combine(Path.GetTempPath(), "directiva_dscript_" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(Path.Combine(root, "utils"));
                    File.WriteAllText(Path.Combine(root, "utils", "Math.dscript"), "def x():\n    pass\n");
                    var provider = new FileSystemDScriptSourceProvider(root);
                    t.Check(provider.TryReadModule("utils.Math", out string code), "exact-case filename resolves");
                    t.Contains("def x", code, "source text");
                    t.Check(!provider.TryReadModule("utils.math", out _), "wrong filename casing rejected");
                    bool invalidThrown = false;
                    try { provider.TryReadModule("..evil", out _); }
                    catch (ArgumentException) { invalidThrown = true; }
                    t.Check(invalidThrown, "traversal-like name rejected");
                }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            });
        }
    }
}
