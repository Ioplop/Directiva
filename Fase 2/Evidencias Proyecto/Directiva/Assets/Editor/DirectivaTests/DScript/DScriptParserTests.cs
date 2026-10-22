using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript.Ast;

namespace Directiva.EditorTests.DScript
{
    internal static class DScriptParserTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action)
                => DScriptTestHarness.Add(tests, "Parser", id, description, action);

            var good = new (string Name, string Code, int Count)[]
            {
                ("Empty", "", 0),
                ("Functions", "def a():\n    pass\ndef b(x,y):\n    return x+y\n", 2),
                ("Imports", "import math\nfrom random import randint as dice\nfrom math import *\n", 3),
                ("ForWhile", "def work(items):\n    for x in items:\n        if x:\n            continue\n    while True:\n        break\n", 1),
                ("IfElifElse", "def f(x):\n    if x > 0:\n        return 1\n    elif x == 0:\n        return 0\n    else:\n        return -1\n", 1),
                ("Collections", "def f():\n    a = []\n    b = {}\n    c = {1,2}\n    d = {1:2}\n    e = (1,2)\n    f = (1,)\n    g = ()\n    h = [1,2,]\n", 1),
                ("CallsAndPostfix", "def f(unit):\n    unit.move(1, 2)\n    x = unit.grid[0].health\n    action = math.sin\n    return action(1)\n", 1),
                ("AugmentedAssignment", "def f():\n    a = 1\n    a += 2\n    a //= 3\n    a **= 2\n", 1),
                ("NestedBlocks", "def f():\n    while True:\n        for x in range(3):\n            if x:\n                pass\n", 1),
                ("SharedDeclaration", "shared total\nshared next_value = 7\ndef f():\n    total = 4\n", 3),
                ("StringAndComments", "# comment\ndef f():\n    x = 'abc' # tail\n    return x\n", 1),
                ("EmptyTupleAndDict", "def f():\n    return [(), {}]\n", 1),
                ("NotIn", "def f(x):\n    return x not in [1,2]\n", 1),
                ("MultilineList", "def f():\n    a = [\n        1,\n        2,\n    ]\n", 1)
            };
            foreach (var fixture in good)
                Add(fixture.Name, "Parse valid DScript: " + fixture.Name, t =>
                {
                    var ast = DScriptTestHarness.Parse(t, fixture.Code, out var diagnostics);
                    t.Equal(0, diagnostics.Count, "syntax errors: " + string.Join(" | ", diagnostics));
                    t.Equal(fixture.Count, ast.Statements.Count, "statement count");
                });

            var invalid = new (string Name, string Code, string Diagnostic)[]
            {
                ("MissingColon", "def f()\n    pass\n", "DS2000"),
                ("MissingIndent", "def f():\npass\n", "DS2000"),
                ("UnexpectedIndent", "    pass\n", "DS2001"),
                ("EmptySuite", "def f():\n    # comment\n", "DS2000"),
                ("InlineSuite", "def f(): pass\n", "DS2000"),
                ("BadAssignmentTarget", "def f():\n    (1+2) = 3\n", "DS2004"),
                ("ChainedCompare", "def f(x):\n    return 1 < x < 3\n", "DS2012"),
                ("CallMissingParen", "def f():\n    print(1\n", "DS2000"),
                ("MissingExpression", "def f():\n    x =\n", "DS2005"),
                ("MissingComma", "def f():\n    return [1 2]\n", "DS2000"),
                ("BadDict", "def f():\n    return {1:}\n", "DS2005"),
                ("UnsupportedKeywordArgs", "def f():\n    return f(x=2)\n", "DS2000"),
                ("TupleAssignmentUnsupported", "def f():\n    a, b = 1, 2\n", "DS2000"),
                ("NestedShared", "def f():\n    shared x\n", "DS2014"),
                ("NestedSharedInIf", "def f():\n    if True:\n        shared x = 7\n", "DS2014"),
                ("ModuleAssignment", "x = 1\n", "DS2013"),
                ("ModuleCall", "print(1)\n", "DS2013"),
                ("ModuleConditional", "if True:\n    pass\n", "DS2013"),
                ("LegacyGlobal", "global x\n", "DS2015"),
                ("SharedCommaUnsupported", "shared x, y\n", "DS2000")
            };
            foreach (var fixture in invalid)
                Add(fixture.Name, "Diagnose syntax issue: " + fixture.Name, t =>
                {
                    DScriptTestHarness.Parse(t, fixture.Code, out var diagnostics);
                    t.Check(diagnostics.Any(d => d.Code == fixture.Diagnostic), "Expected " + fixture.Diagnostic +
                        "; actual " + string.Join(" | ", diagnostics));
                });

            Add("SharedInitializerAst", "A shared declaration keeps its source expression and location", t =>
            {
                var ast = DScriptTestHarness.Parse(t, "shared value = sin(2)\nshared other\n", out var errors);
                t.Equal(0, errors.Count, "syntax errors");
                var first = (SharedStatement)ast.Statements[0];
                var second = (SharedStatement)ast.Statements[1];
                t.Equal("value", first.Name, "shared name");
                t.Check(first.Initializer is CallExpression, "expected call initializer");
                t.Equal(1, first.NameSpan.Line, "name line");
                t.Equal(8, first.NameSpan.Column, "name column");
                t.Check(second.Initializer == null, "uninitialized declaration");
            });
            Add("PrecedenceMultiply", "Multiplication binds more tightly than addition", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f():\n    return 1 + 2 * 3\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var ret = (ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0];
                var plus = ret.Value as BinaryExpression;
                t.Equal("+", plus?.Operator, "outer operation");
                t.Equal("*", (plus.Right as BinaryExpression)?.Operator, "nested operation");
            });
            Add("PrecedencePower", "Exponentiation is right-associative", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f():\n    return 2 ** 3 ** 2\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var power = ((ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0]).Value as BinaryExpression;
                t.Equal("**", power?.Operator, "first power");
                t.Equal("**", (power.Right as BinaryExpression)?.Operator, "right operand power");
            });
            Add("PrecedenceUnaryPower", "-2 ** 2 parses as -(2 ** 2)", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f():\n    return -2 ** 2\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var unary = ((ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0]).Value as UnaryExpression;
                t.Equal("-", unary?.Operator, "outer unary");
                t.Equal("**", (unary.Operand as BinaryExpression)?.Operator, "power inside unary");
            });
            Add("PrecedenceBoolean", "not, and, or follow Python-inspired order", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f(a,b,c):\n    return not a and b or c\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var disjunction = ((ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0]).Value as BinaryExpression;
                t.Equal("or", disjunction?.Operator, "outer or");
                var conjunction = disjunction.Left as BinaryExpression;
                t.Equal("and", conjunction?.Operator, "inner and");
                t.Equal("not", (conjunction.Left as UnaryExpression)?.Operator, "inner not");
            });
            Add("PrecedenceNotCompare", "not a == b means not (a == b)", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f(a,b):\n    return not a == b\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var unary = ((ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0]).Value as UnaryExpression;
                t.Equal("not", unary?.Operator, "not outer");
                t.Equal("==", (unary.Operand as BinaryExpression)?.Operator, "equality inner");
            });
            Add("CollectionShapes", "Dict keys/values and tuple/list/set have distinct AST kinds", t =>
            {
                var module = DScriptTestHarness.Parse(t,
                    "def f():\n    a = {1: 2, 3: 4}\n    b = (1,2)\n    c = {1,2}\n    d = []\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var body = ((FunctionStatement)module.Statements[0]).Body.Cast<AssignmentStatement>().ToArray();
                var kinds = body.Select(s => ((CollectionExpression)s.Value).Kind).ToArray();
                t.Equal("Dictionary,Tuple,Set,List", string.Join(",", kinds), "collection kinds");
                var dict = (CollectionExpression)body[0].Value;
                t.Equal(2, dict.Keys.Count, "dictionary key count");
                t.Equal(2, dict.Items.Count, "dictionary value count");
            });
            // Regression cases for optional trailing commas (list, def and call).
            Add("TrailingCommaParameters", "A def accepts its final optional comma", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f(a, b,):\n    return a\n", out var diag);
                t.Equal(0, diag.Count, "errors: " + string.Join(" | ", diag));
                var function = (FunctionStatement)module.Statements[0];
                t.Equal("a,b", string.Join(",", function.Parameters), "parameters");
            });
            Add("TrailingCommaArguments", "A call accepts its final optional comma", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f():\n    return print(1,)\n", out var diag);
                t.Equal(0, diag.Count, "errors: " + string.Join(" | ", diag));
                var call = ((ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0]).Value as CallExpression;
                t.Check(call != null, "expected call expression");
                t.Equal(1, call.Arguments.Count, "argument count");
            });
            Add("TrailingCommaNestedCollections", "Nested collections accept a final comma without an extra element", t =>
            {
                var module = DScriptTestHarness.Parse(t,
                    "def f():\n    return [[1,], (2,), {3,}, {4: 5,},]\n", out var diag);
                t.Equal(0, diag.Count, "errors: " + string.Join(" | ", diag));
                var outer = (CollectionExpression)((ReturnStatement)((FunctionStatement)module.Statements[0]).Body[0]).Value;
                t.Equal(CollectionKind.List, outer.Kind, "outer collection");
                t.Equal(4, outer.Items.Count, "outer item count");
                t.Equal("List,Tuple,Set,Dictionary", string.Join(",", outer.Items.Select(item =>
                    ((CollectionExpression)item).Kind)), "nested kinds");
            });
            Add("RepeatedCommaInListRejected", "A second comma is not another valid list element", t =>
            {
                DScriptTestHarness.Parse(t, "def f():\n    return [1,,2]\n", out var diag);
                t.Check(diag.Any(d => d.Code == "DS2005"), "expected DS2005, got " +
                    string.Join(" | ", diag));
            });
            Add("ForIteratorBody", "For loop variable and iterable are separate nodes", t =>
            {
                var module = DScriptTestHarness.Parse(t, "def f():\n    for x in range(3):\n        print(x)\n", out var diag);
                t.Equal(0, diag.Count, "errors");
                var loop = (ForStatement)((FunctionStatement)module.Statements[0]).Body[0];
                t.Equal("x", loop.Variable, "loop target");
                t.Check(loop.Iterable is CallExpression, "range(...) is a call");
                t.Equal(1, loop.Body.Count, "loop body");
            });
        }
    }
}
