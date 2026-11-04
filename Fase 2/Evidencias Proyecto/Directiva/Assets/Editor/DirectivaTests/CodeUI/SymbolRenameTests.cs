using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using Directiva.CodeUI;

namespace Directiva.EditorTests.CodeUITests
{
    internal static class SymbolRenameTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("CodeUI", "SymbolRename", id, description, action));

            void ExpectRename(DirectivaTestContext t, string original, string selectedText,
                string newName, string expected, Dictionary<string, string> imports = null,
                int occurrence = 0)
            {
                var sources = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["test"] = original };
                if (imports != null)
                    foreach (var entry in imports) sources.Add(entry.Key, entry.Value);
                // Select an actual identifier token, never a substring of a longer
                // identifier (for example, "item" must not select "items").
                var matches = new DScriptLexer("test", original).Tokenize()
                    .Where(token => token.Kind == TokenKind.Identifier &&
                        string.Equals(token.Text, selectedText, StringComparison.Ordinal))
                    .ToArray();
                t.Check(occurrence >= 0 && occurrence < matches.Length,
                    "test fixture contains selected identifier token");
                if (occurrence < 0 || occurrence >= matches.Length) return;
                int caret = matches[occurrence].Span.Start;
                var provider = new InMemoryDScriptSourceProvider(sources);
                bool ok = DScriptLocalRename.TryRename("test", provider, caret,
                    newName, out var plan, out string error);
                t.Check(ok, "rename succeeds: " + error);
                if (!ok) return;
                t.Equal(expected, plan.Content, "only references of the bound symbol are changed");
                t.Equal(original, sources["test"], "planning does not mutate sources");
                t.Check(DScriptFrontend.Analyze("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string>(sources) { ["test"] = plan.Content })).Success,
                    "result compiles");
            }

            Add("LocalUses", "Rename assignments and reads, never unrelated identical text", t =>
                ExpectRename(t,
                    "def f():\n    value = 3\n    print(value)\n    return value\n",
                    "value", "total",
                    "def f():\n    total = 3\n    print(total)\n    return total\n"));

            Add("Parameter", "Rename parameter declaration and all its uses", t =>
                ExpectRename(t,
                    "def f(value):\n    return value * value\n",
                    "value", "count",
                    "def f(count):\n    return count * count\n"));

            Add("OtherFunction", "Two local variables with identical spellings remain independent", t =>
                ExpectRename(t,
                    "def a():\n    value = 1\n    return value\ndef b():\n    value = 2\n    return value\n",
                    "value", "first",
                    "def a():\n    first = 1\n    return first\ndef b():\n    value = 2\n    return value\n"));

            Add("ForTarget", "Rename a for-loop target and its body uses", t =>
                ExpectRename(t,
                    "def f(items):\n    for item in items:\n        print(item)\n",
                    "item", "element",
                    "def f(items):\n    for element in items:\n        print(element)\n"));

            Add("ForBodyReference", "Rename from the loop body, leaving a similarly named parameter unchanged", t =>
                ExpectRename(t,
                    "def f(items):\n    for item in items:\n        print(item)\n",
                    "item", "element",
                    "def f(items):\n    for element in items:\n        print(element)\n",
                    occurrence: 1));

            Add("ForIterableParameter", "Rename the iterable parameter without touching the loop target", t =>
                ExpectRename(t,
                    "def f(items):\n    for item in items:\n        print(item)\n",
                    "items", "values",
                    "def f(values):\n    for item in values:\n        print(item)\n"));

            Add("StringAndComment", "Literal strings and comments are not treated as references", t =>
                ExpectRename(t,
                    "def f():\n    value = 1\n    print('value', value) # value\n",
                    "value", "result",
                    "def f():\n    result = 1\n    print('value', result) # value\n"));

            Add("ImportedNames", "Import and module members remain unchanged", t =>
                ExpectRename(t,
                    "import util\ndef f():\n    value = util.work()\n    return value\n",
                    "value", "result",
                    "import util\ndef f():\n    result = util.work()\n    return result\n",
                    new Dictionary<string, string> { ["util"] = "def work():\n    return 1\n" }));

            Add("SharedIsNotLocal", "A shared name is not accidentally renamed as a local", t =>
            {
                const string code = "shared counter = 0\ndef f():\n    counter += 1\n";
                bool ok = DScriptLocalRename.TryRename("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] = code }),
                    code.IndexOf("counter", StringComparison.Ordinal), "ticks", out _, out _);
                t.Check(!ok, "global rename requires a workspace-wide operation");
            });

            Add("RejectCollision", "Cannot merge two existing locals into one", t =>
            {
                const string code = "def f():\n    a = 1\n    b = 2\n    return a + b\n";
                bool ok = DScriptLocalRename.TryRename("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] = code }),
                    code.IndexOf("a =", StringComparison.Ordinal), "b", out _, out _);
                t.Check(!ok, "local collision is rejected");
            });

            Add("RejectKeyword", "Keywords cannot become identifiers", t =>
            {
                const string code = "def f(a):\n    return a\n";
                bool ok = DScriptLocalRename.TryRename("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] = code }),
                    code.IndexOf("a)", StringComparison.Ordinal), "return", out _, out _);
                t.Check(!ok, "keyword rejected");
            });

            Add("WindowsNewlines", "Editor cursor offsets match normalized CRLF source", t =>
            {
                const string crlf = "def f(value):\r\n    return value\r\n";
                bool ok = DScriptLocalRename.TryRename("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] = crlf }),
                    "def f(".Length, "item", out var result, out string error);
                t.Check(ok, "rename against a CRLF original: " + error);
                if (ok) t.Equal("def f(item):\n    return item\n", result.Content,
                    "source matches the normalized editor buffer");
            });

            Add("UnrelatedSyntaxError", "Locals can be renamed when another statement has a syntax error", t =>
            {
                const string source = "def f(value):\n    broken =\n    return value * 2\n";
                bool ok = DScriptLocalRename.TryRename("test",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                        { ["test"] = source }), source.IndexOf("value", StringComparison.Ordinal),
                    "amount", out var plan, out string error);
                t.Check(ok, "rename despite existing syntax error: " + error);
                if (ok) t.Equal("def f(amount):\n    broken =\n    return amount * 2\n",
                    plan.Content, "syntax error remains; valid references are renamed");
            });

            Add("UnrelatedSemanticError", "Local renames do not require an error-free workspace", t =>
            {
                const string source = "def f(value):\n    return value + unknown\n";
                bool ok = DScriptLocalRename.TryRename("test",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                        { ["test"] = source }), source.IndexOf("value", StringComparison.Ordinal),
                    "amount", out var plan, out string error);
                t.Check(ok, "rename despite existing undefined name: " + error);
                if (ok) t.Equal("def f(amount):\n    return amount + unknown\n",
                    plan.Content, "existing error is preserved");
            });

            Add("RejectDiscardedLocalReference", "A malformed expression might contain an unbound local use", t =>
            {
                const string source = "def f(value):\n    invalid = value +\n    return value\n";
                bool ok = DScriptLocalRename.TryRename("test",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                        { ["test"] = source }), source.IndexOf("value", StringComparison.Ordinal),
                    "amount", out var plan, out _);
                t.Check(!ok, "cannot prove all uses of local were discovered");
                t.Check(plan == null, "reject before modifying code");
            });

            Add("RejectCaptureUndefined", "Renaming a local must not capture another previously undefined name", t =>
            {
                const string source = "def f(value):\n    return value + missing\n";
                bool ok = DScriptLocalRename.TryRename("test",
                    new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                        { ["test"] = source }), source.IndexOf("value", StringComparison.Ordinal),
                    "missing", out var plan, out _);
                t.Check(!ok, "unresolved reference must not become a use of renamed parameter");
                t.Check(plan == null, "reject before producing edits");
            });

            Add("NoOp", "Renaming to the same name is a no-op", t =>
            {
                const string code = "def f(x):\n    return x\n";
                bool ok = DScriptLocalRename.TryRename("test", new InMemoryDScriptSourceProvider(
                    new Dictionary<string, string> { ["test"] = code }),
                    code.IndexOf("x)", StringComparison.Ordinal), "x", out var result, out string error);
                t.Check(ok, "same name accepted: " + error);
                if (ok) { t.Equal(0, result.Count, "zero replacements"); t.Equal(code, result.Content, "unchanged content"); }
            });
        }
    }
}
