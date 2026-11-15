using System;
using System.Collections.Generic;
using System.Linq;
using Directiva.CodeUI;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Ctrl+click uses binder identity, not matching identifier text.</summary>
    internal static class GoToDefinitionTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "GoToDefinition", id, description, run));

            int TokenAt(string source, string module, string name, int occurrence = 0)
            {
                var found = new DScriptLexer(module, source.Replace("\r\n", "\n")).Tokenize()
                    .Where(token => token.Kind == TokenKind.Identifier && token.Text == name).ToArray();
                if (occurrence < 0 || occurrence >= found.Length)
                    throw new InvalidOperationException("Missing token " + module + "." + name + " #" + occurrence);
                return found[occurrence].Span.Start;
            }

            void Expect(DirectivaTestContext t, Dictionary<string, string> sources,
                string clickedModule, string clickedName, int clickedOccurrence,
                string destinationModule, string destinationName, int declarationOccurrence = 0)
            {
                int caret = TokenAt(sources[clickedModule], clickedModule, clickedName, clickedOccurrence);
                bool ok = DScriptGoToDefinition.TryResolve(clickedModule,
                    new InMemoryDScriptSourceProvider(sources), caret, out var location);
                t.Check(ok, "bound reference should navigate: " + clickedModule + "." + clickedName);
                if (!ok) return;
                t.Equal(destinationModule, location.Module, "destination module");
                t.Equal(TokenAt(sources[destinationModule], destinationModule,
                    destinationName, declarationOccurrence), location.Span.Start, "declaration offset");
                t.Equal(destinationName.Length, location.Span.Length, "only the identifier is selected");
            }

            void Reject(DirectivaTestContext t, Dictionary<string, string> sources,
                string module, int sourceOffset)
            {
                bool ok = DScriptGoToDefinition.TryResolve(module,
                    new InMemoryDScriptSourceProvider(sources), sourceOffset, out var location);
                t.Check(!ok, "non-user reference must not navigate");
                t.Check(location == null, "no destination should be returned");
            }

            Add("ClickGesture", "Only Ctrl/Command + left click requests navigation", t =>
            {
                t.Check(CodeEditorView.IsGoToDefinitionClick(0, true, false, false, false), "Ctrl + left click");
                t.Check(CodeEditorView.IsGoToDefinitionClick(0, false, true, false, false), "Command + left click");
                t.Check(!CodeEditorView.IsGoToDefinitionClick(0, false, false, false, false), "plain click");
                t.Check(!CodeEditorView.IsGoToDefinitionClick(1, true, false, false, false), "right click");
                t.Check(!CodeEditorView.IsGoToDefinitionClick(0, true, false, true, false), "Ctrl+Shift click");
                t.Check(!CodeEditorView.IsGoToDefinitionClick(0, true, false, false, true), "Ctrl+Alt click");
            });

            Add("LocalVariable", "A local read navigates to the assignment", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def run():\n    value = 10\n    return value\n"
                }, "main", "value", 1, "main", "value"));

            Add("Parameter", "A parameter use navigates to its function signature", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def run(item):\n    return item + 1\n"
                }, "main", "item", 1, "main", "item"));

            Add("TypedParameter", "Type annotations do not obscure the parameter declaration", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def run(item: Number) -> Number:\n    return item\n"
                }, "main", "item", 1, "main", "item"));

            Add("LocalAssignedLater", "Predeclared locals navigate to their first assignment", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def run():\n    print(value)\n    value = 4\n"
                }, "main", "value", 0, "main", "value", 1));

            Add("ForLoopTarget", "A loop variable use navigates to the for target", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def run(items):\n    for item in items:\n        print(item)\n"
                }, "main", "item", 1, "main", "item"));

            Add("LocalShadowing", "Identical local names in different functions remain independent", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def first():\n    value = 1\n    return value\ndef second():\n    value = 2\n    return value\n"
                }, "main", "value", 3, "main", "value", 2));

            Add("ForwardFunction", "A call can navigate to a later function definition", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def start():\n    return calculate()\ndef calculate():\n    return 5\n"
                }, "main", "calculate", 0, "main", "calculate", 1));

            Add("ModuleMember", "A module attribute navigates across files", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib.calc"] = "def evaluate(x):\n    return x\n",
                    ["main"] = "import lib.calc as calc\ndef start():\n    return calc.evaluate(3)\n"
                }, "main", "evaluate", 0, "lib.calc", "evaluate"));

            Add("FromImport", "A from-import use navigates to the exported declaration", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "def fetch():\n    return 1\n",
                    ["main"] = "from lib import fetch\ndef start():\n    return fetch()\n"
                }, "main", "fetch", 1, "lib", "fetch"));

            Add("AliasedImportUse", "An alias reference navigates to the original function", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "def fetch():\n    return 1\n",
                    ["main"] = "from lib import fetch as get\ndef start():\n    return get()\n"
                }, "main", "get", 1, "lib", "fetch"));

            Add("AliasedImportDeclaration", "Ctrl+click on an import alias reaches its original definition", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "def fetch():\n    return 1\n",
                    ["main"] = "from lib import fetch as get\ndef start():\n    return get()\n"
                }, "main", "get", 0, "lib", "fetch"));

            Add("ImportedName", "Ctrl+click on a from-import symbol reaches its declaration", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "def fetch():\n    return 1\n",
                    ["main"] = "from lib import fetch\ndef start():\n    return fetch()\n"
                }, "main", "fetch", 0, "lib", "fetch"));

            Add("TransitiveModule", "Imports are located by owning AST, not overlapping source offsets", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "def fetch():\n    return 1\n",
                    ["middle"] = "from lib import fetch\ndef middle():\n    return fetch()\n",
                    ["main"] = "import middle\ndef start():\n    return middle.middle()\n"
                }, "middle", "fetch", 1, "lib", "fetch"));

            Add("SharedImported", "A shared variable use navigates to its module declaration", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "shared counter = 0\n",
                    ["main"] = "from lib import counter\ndef start():\n    return counter\n"
                }, "main", "counter", 1, "lib", "counter"));

            Add("SharedThroughAlias", "A module-qualified shared reference navigates to its source", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "shared counter = 0\n",
                    ["main"] = "import lib as l\ndef start():\n    return l.counter\n"
                }, "main", "counter", 0, "lib", "counter"));

            Add("SharedPreferLocal", "Multiple declarations of a shared slot prefer the current file", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "shared count = 0\n",
                    ["main"] = "import lib\nshared count\ndef start():\n    return count\n"
                }, "main", "count", 1, "main", "count"));

            Add("SharedPreferInitializer", "Imported shared declarations prefer the initializer", t =>
                Expect(t, new Dictionary<string, string> {
                    ["empty"] = "shared count\n",
                    ["filled"] = "shared count = 4\n",
                    ["main"] = "import empty\nimport filled\ndef start():\n    return empty.count\n"
                }, "main", "count", 0, "filled", "count"));

            Add("VmGlobal", "A VM-private global use reaches its module declaration", t =>
                Expect(t, new Dictionary<string, string> {
                    ["lib"] = "global attempts = 0\n",
                    ["main"] = "import lib as l\ndef start():\n    return l.attempts\n"
                }, "main", "attempts", 0, "lib", "attempts"));

            Add("UnrelatedSemanticError", "Valid bindings remain navigable alongside other errors", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def start(value):\n    print(missing)\n    return value\n"
                }, "main", "value", 1, "main", "value"));

            Add("UnrelatedSyntaxError", "Recovered statements can still navigate with broken neighbors", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def start(value):\n    broken =\n    return value\n"
                }, "main", "value", 1, "main", "value"));

            Add("CRLF", "Offsets match the editor's normalized newline representation", t =>
                Expect(t, new Dictionary<string, string> {
                    ["main"] = "def start(value):\r\n    return value\r\n"
                }, "main", "value", 1, "main", "value"));

            Add("UnknownName", "An unresolved name is not a definition link", t =>
            {
                var files = new Dictionary<string, string> { ["main"] = "def run():\n    return missing\n" };
                Reject(t, files, "main", TokenAt(files["main"], "main", "missing"));
            });

            Add("NativeFunction", "Native functions are not navigable DScript declarations", t =>
            {
                var files = new Dictionary<string, string> { ["main"] = "def run():\n    print(1)\n" };
                Reject(t, files, "main", TokenAt(files["main"], "main", "print"));
            });

            Add("ModuleAlias", "Module aliases by themselves do not navigate as variables", t =>
            {
                var files = new Dictionary<string, string> {
                    ["lib"] = "def f():\n    return 1\n",
                    ["main"] = "import lib as l\ndef run():\n    return l.f()\n"
                };
                Reject(t, files, "main", TokenAt(files["main"], "main", "l", 1));
            });

            Add("ObjectAttribute", "Runtime object members cannot be mistaken for module symbols", t =>
            {
                var files = new Dictionary<string, string> { ["main"] = "def run(unit):\n    return unit.health\n" };
                Reject(t, files, "main", TokenAt(files["main"], "main", "health"));
            });

            Add("StringAndComment", "Strings and comments do not contain code links", t =>
            {
                var files = new Dictionary<string, string> {
                    ["main"] = "def run():\n    value = 1\n    print('value')  # value\n"
                };
                Reject(t, files, "main", files["main"].IndexOf("'value'", StringComparison.Ordinal) + 1);
                Reject(t, files, "main", files["main"].LastIndexOf("value", StringComparison.Ordinal));
            });

            Add("IdentifierBoundary", "Whitespace after a token is not part of the link", t =>
            {
                var files = new Dictionary<string, string> { ["main"] = "def run():\n    value = 1\n    return value + 1\n" };
                int end = TokenAt(files["main"], "main", "value", 1) + "value".Length;
                Reject(t, files, "main", end);
            });
        }
    }
}
