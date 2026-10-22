using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Semantics;

namespace Directiva.EditorTests.DScript
{
    internal static class DScriptSemanticTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "Semantics", id, description, action);

            var valid = new (string Name, string Code)[]
            {
                ("SimpleFunction", "def f(x):\n    return x + 1\n"),
                ("ForwardFunction", "def main():\n    return later(4)\ndef later(x):\n    return x+1\n"),
                ("NativeCall", "def main():\n    return max(abs(-5), int('3'))\n"),
                ("SharedAcrossFunctions", "shared action\ndef change():\n    action = runner\ndef runner():\n    return 1\ndef call():\n    return action()\n"),
                ("IndirectFunction", "def f(x):\n    return x\ndef main():\n    g = f\n    return g(5)\n"),
                ("FunctionInDict", "def add(a,b):\n    return a+b\ndef main():\n    funcs = {'sum': add}\n    return funcs['sum'](2,3)\n"),
                ("LoopAndControl", "def main():\n    total = 0\n    for i in range(10):\n        if i == 5:\n            continue\n        if i == 8:\n            break\n        total += i\n    return total\n"),
                ("DynamicMembers", "def command(unit, target):\n    unit.move_to(target.x, target.y)\n    return unit.health\n"),
                ("DifferentCollections", "def main():\n    return [{1,2}, {1:'a'}, [2], (3,)]\n"),
                ("SharedAfterFunctions", "def f():\n    signal = 3\nshared signal\n"),
                ("BuiltinShadowedLocally", "def f():\n    print = 7\n    return print\n"),
                ("BuiltinShadowedByShared", "shared print = 7\ndef f():\n    return print\n"),
                ("NoneInIterable", "def f(values):\n    for item in values:\n        if item == None:\n            print(item)\n"),
                ("ConventionalAlgorithm", "import math\ndef direction(a,b):\n    dx = b.x - a.x\n    dy = b.y - a.y\n    return math.atan2(dy, dx)\n"),
                ("SharedInitialized", "shared counter = 0\ndef tick():\n    counter += 1\n    return counter\n"),
                ("ParenthesizedMultiline", "def f():\n    return max(\n        3,\n        5\n    )\n"),
                ("NotIn", "def f(v):\n    return v not in [1,2]\n"),
                ("NegativeRanges", "def f():\n    total = 0\n    for i in range(10, 0, -1):\n        total += i\n    return total\n")
            };
            foreach (var fixture in valid)
                Add(fixture.Name, "Valid semantic program: " + fixture.Name,
                    t => DScriptTestHarness.Ok(t, fixture.Code));

            var invalid = new (string Name, string Code, string Diagnostic)[]
            {
                ("UndefinedLocal", "def f():\n    return nowhere\n", "DS4016"),
                ("DuplicateFunction", "def f():\n    pass\ndef f():\n    pass\n", "DS4002"),
                ("FunctionSharedCollision", "shared f\ndef f():\n    pass\n", "DS4002"),
                ("DuplicateParameter", "def f(x,x):\n    return x\n", "DS4005"),
                ("ParameterSharedConflict", "shared x\ndef f(x):\n    return x\n", "DS4006"),
                ("BreakWithoutLoop", "def f():\n    break\n", "DS4008"),
                ("ContinueWithoutLoop", "def f():\n    continue\n", "DS4008"),
                ("NestedFunction", "def f():\n    def nested():\n        return 1\n", "DS4009"),
                ("ModuleWhile", "while True:\n    pass\n", "DS2013"),
                ("ModuleFor", "for x in [1,2]:\n    pass\n", "DS2013"),
                ("ModuleExpression", "print(10)\n", "DS2013"),
                ("ModuleMemberAssignment", "thing.x = 3\n", "DS2013"),
                ("ModuleReturn", "return 3\n", "DS2013"),
                ("DirectArity", "def f(x):\n    return x\ndef main():\n    return f(1,2)\n", "DS4014"),
                ("ImportedArity", "from util import twice\ndef main():\n    return twice()\n", "DS4014"),
                ("ImportedModuleAsCall", "import math\ndef f():\n    return math(1)\n", "DS4013"),
                ("ModuleNamespaceAsValue", "import util.mod\ndef f():\n    return util.mod\n", "DS4013")
            };
            foreach (var fixture in invalid)
                Add(fixture.Name, "Reject invalid static construct: " + fixture.Name, t =>
                {
                    Dictionary<string, string> extra = fixture.Name == "ImportedArity"
                        ? new Dictionary<string, string> { ["util"] = "def twice(x):\n    return x+x\n" }
                        : fixture.Name == "ModuleNamespaceAsValue"
                            ? new Dictionary<string, string> { ["util.mod"] = "def f():\n    pass\n" }
                            : null;
                    DScriptTestHarness.Error(t, fixture.Code, fixture.Diagnostic, extra);
                });

            Add("LocalLayout", "Parameters first, local slots assigned by lexical first binding", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def calc(a,b):\n    out = 0\n    if a:\n        branch = 1\n    else:\n        other = 2\n    for item in [a,b]:\n        out += item\n    return out\n");
                var f = a.FunctionSymbols[DScriptTestHarness.Function(a, "calc")];
                t.Equal(6, f.LocalCount, "local count includes 2 parameters");
                foreach (var item in new[]{("a",0), ("b",1), ("out",2), ("branch",3), ("other",4), ("item",5)})
                    t.Equal(item.Item2, f.Locals[item.Item1].Slot, "slot of " + item.Item1);
            });
            Add("ReadBeforeAssignmentIsLocal", "Statically resolve an assigned local even if it is read first", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def f():\n    print(x)\n    x = 1\n");
                var call = (CallExpression)((ExpressionStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value;
                t.Equal(SymbolKind.Local, a.ExpressionSymbols[call.Arguments[0]].Kind, "local binding");
                t.Equal(0, a.ExpressionSymbols[call.Arguments[0]].Slot, "slot 0");
            });
            Add("OneSlotAcrossModules", "Same shared name in different modules binds one storage slot", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "import util\nshared counter = 10\ndef f():\n    counter += util.counter\n",
                    new Dictionary<string, string> { ["util"] = "shared counter\ndef f():\n    return counter\n" });
                t.Equal(1, a.GlobalSlots.Count, "one program-wide shared slot");
                t.Equal("shared.counter", a.GlobalSlots[0].QualifiedName, "canonical shared identity");
                t.Check(object.ReferenceEquals(a.ModuleSymbols["test"].Globals["counter"],
                    a.ModuleSymbols["util"].Globals["counter"]), "both modules bind same symbol instance");
                t.Equal(0, a.GlobalSlots[0].Slot, "single slot index");
                t.Equal(1, a.SharedInitializers.Count, "only one initializer");
            });
            Add("SharedDeclaredAfterFunction", "A later shared declaration binds earlier function assignments", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def f():\n    action = f\nshared action\n");
                t.Equal(1, a.GlobalSlots.Count, "shared count");
                t.Equal("shared.action", a.GlobalSlots[0].QualifiedName, "canonical name");
                var assignment = (AssignmentStatement)DScriptTestHarness.Function(a, "f").Body[0];
                t.Equal(SymbolKind.Function, a.ExpressionSymbols[assignment.Value].Kind, "function as value");
                t.Equal(SymbolKind.Global, a.ExpressionSymbols[assignment.Target].Kind, "shared assignment");
            });
            Add("AssignmentUsesShared", "An assignment to module-declared shared does not allocate local", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared counter\ndef f():\n    counter += 1\n");
                var table = a.FunctionSymbols[DScriptTestHarness.Function(a, "f")];
                t.Equal(0, table.LocalCount, "no local slots");
                var lhs = ((AssignmentStatement)table.Ast.Body[0]).Target;
                t.Equal(SymbolKind.Global, a.ExpressionSymbols[lhs].Kind, "writes shared");
            });
            Add("ShadowFunctionLocally", "A local can hide a function, making the call indirect", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def action(x):\n    return x\ndef f():\n    action = abs\n    return action(-3)\n");
                var table = a.FunctionSymbols[DScriptTestHarness.Function(a, "f")];
                var call = ((ReturnStatement)table.Ast.Body[1]).Value as CallExpression;
                t.Equal(SymbolKind.Local, a.ExpressionSymbols[call.Callee].Kind, "local wins");
            });
            Add("ResolutionAcrossFiles", "Imported static function arity and module-qualified identity", t =>
            {
                var a = DScriptTestHarness.Ok(t,
                    "from helper import f as apply\ndef main():\n    return apply(1,2)\n",
                    new Dictionary<string, string> { ["helper"] = "def f(a,b):\n    return a+b\n" });
                var call = ((ReturnStatement)DScriptTestHarness.Function(a, "main").Body[0]).Value as CallExpression;
                t.Equal("helper.f", a.ExpressionSymbols[call.Callee].QualifiedName, "unique full name");
                t.Equal(2, a.ExpressionSymbols[call.Callee].ParameterCount, "known arity");
            });
            Add("ImportedGlobalWrite", "Imported module global can be changed through its qualified name", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import state\ndef f():\n    state.action = f\n    return state.action\n",
                    new Dictionary<string, string> { ["state"] = "shared action = None\n" });
                var table = a.FunctionSymbols[DScriptTestHarness.Function(a, "f")];
                var lhs = ((AssignmentStatement)table.Ast.Body[0]).Target;
                t.Equal(SymbolKind.Global, a.ExpressionSymbols[lhs].Kind, "assignment target is imported global");
                t.Equal("shared.action", a.ExpressionSymbols[lhs].QualifiedName, "target identity");
            });
            Add("ImportedGlobalDynamicMember", "A member of an imported global remains a dynamic attribute", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import state\ndef f():\n    state.unit.health = 7\n    return state.unit.health\n",
                    new Dictionary<string, string> { ["state"] = "shared unit = None\n" });
                var body = DScriptTestHarness.Function(a, "f").Body;
                var outer = ((AssignmentStatement)body[0]).Target as AttributeExpression;
                t.Check(outer != null, "dynamic health assignment parsed");
                t.Equal("shared.unit", a.ExpressionSymbols[outer.Receiver].QualifiedName, "imported shared prefix binding");
                t.Equal(SymbolKind.Global, a.ExpressionSymbols[outer.Receiver].Kind, "global prefix type");
            });
            Add("MangledGlobalIdentifier", "Global DIL name mangling is reversible in practice and collision-free", t =>
            {
                var a = DScriptTestHarness.Ok(t, "import x.y\nshared foo = 2\n",
                    new Dictionary<string, string> { ["x.y"] = "shared bar = 3\n" });
                var names = a.GlobalSlots.Select(g => g.DILGlobalName).ToArray();
                t.Equal(2, names.Length, "global count");
                t.Check(names.All(n => n.StartsWith("__dsg_", StringComparison.Ordinal)), "legal plain DIL identifier");
                t.Check(names.Distinct(StringComparer.Ordinal).Count() == names.Length, "no collision");
                t.Check(DSCompilation.DScript.Semantics.DScriptNameMangler.Global("a.b_c") !=
                    DSCompilation.DScript.Semantics.DScriptNameMangler.Global("a_b.c"), "different qualified names");
            });
            Add("StringLiteralSpan", "Expressions preserve exact source span for future debug info", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def f():\n    return 'hola'\n");
                var literal = ((ReturnStatement)DScriptTestHarness.Function(a, "f").Body[0]).Value;
                t.Equal(2, literal.Span.Line, "line");
                t.Equal(12, literal.Span.Column, "column");
                t.Equal(6, literal.Span.Length, "literal with quotes length");
            });
            Add("SignedMinimumInt", "The Int64 minimum literal is accepted with a leading minus", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def f():\n    return -9223372036854775808\n");
                t.Check(a.Success, "signed Int64 minimum is in range");
            });
            Add("PositiveBeyondInt64", "The positive magnitude 2^63 is out of range for Int64", t =>
                DScriptTestHarness.Error(t, "def f():\n    return 9223372036854775808\n", "DS4017"));
            Add("DecimalOverRange", "Decimal literals outside fixed-point Int64 range are rejected", t =>
                DScriptTestHarness.Error(t, "def f():\n    return 99999999999999999.0\n", "DS1004"));
            Add("NativeFunctionValue", "Builtins can be stored as ordinary function values", t =>
            {
                var a = DScriptTestHarness.Ok(t, "def main():\n    op = abs\n    return op(-12)\n");
                var rhs = ((AssignmentStatement)DScriptTestHarness.Function(a, "main").Body[0]).Value;
                t.Equal(SymbolKind.NativeFunction, a.ExpressionSymbols[rhs].Kind, "native function value");
                t.Equal("abs", a.ExpressionSymbols[rhs].QualifiedName, "builtin identity");
            });
            Add("DiagnosticLocation", "Semantic diagnostics identify exact line and column", t =>
            {
                var a = DScriptTestHarness.Analyze(t, "def f():\n    return missing\n");
                var error = a.Diagnostics.Single(d => d.Code == "DS4016");
                t.Equal(2, error.Span.Line, "diagnostic line");
                t.Equal(12, error.Span.Column, "diagnostic column");
                t.Equal("test", error.Module, "module identity");
            });
            Add("GlobalsSorted", "Global slot numbering does not depend on import traversal order", t =>
            {
                var a = DScriptTestHarness.Ok(t, "shared z = 1\nshared a = 2\n", new Dictionary<string, string>());
                t.Equal("shared.a,shared.z", string.Join(",", a.GlobalSlots.Select(s => s.QualifiedName)), "sorted slots");
            });
        }
    }
}
