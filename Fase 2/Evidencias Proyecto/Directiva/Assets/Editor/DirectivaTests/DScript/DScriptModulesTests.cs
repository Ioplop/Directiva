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
            Add("NestedModule", "import utils.calc exposes namespace chain utils.calc.square", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import utils.calc\ndef f():\n    return utils.calc.square(3)\n",
                    new Dictionary<string, string> { ["utils.calc"] = "def square(x):\n    return x*x\n" });
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("utils.calc.square", a.ExpressionSymbols[call.Callee].QualifiedName, "qualified nested module");
            });
            Add("ModuleAlias", "import utils.calc as c exposes c.square", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import utils.calc as c\ndef f():\n    return c.square(3)\n",
                    new Dictionary<string, string> { ["utils.calc"] = "def square(x):\n    return x*x\n" });
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value as CallExpression;
                t.Equal("utils.calc.square", a.ExpressionSymbols[call.Callee].QualifiedName, "renamed namespace");
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
            Add("NamespaceNotImported", "Missing intermediate import path is diagnosed", t =>
                DScriptTestHarness.Error(t, "import util.mod\ndef f():\n    return util.other.value\n", "DS4015",
                    new Dictionary<string, string> { ["util.mod"] = "def value():\n    return 1\n" }));
            Add("DependencyOrder", "Dependencies-first graph traversal is deterministic", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import a\ndef f():\n    pass\n",
                    new Dictionary<string, string> { ["a"] = "import b\ndef f():\n    pass\n",
                        ["b"] = "def f():\n    pass\n" });
                t.Equal("b,a,test", string.Join(",", a.DependenciesFirst), "compilation order");
            });
            Add("RepeatedRootImports", "Multiple submodule imports share their root namespace", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import util.one\nimport util.two\ndef f():\n    return util.one.a() + util.two.b()\n",
                    new Dictionary<string, string> { ["util.one"] = "def a():\n    return 1\n",
                        ["util.two"] = "def b():\n    return 2\n" });
                t.Equal(3, a.Modules.Count, "module count");
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
