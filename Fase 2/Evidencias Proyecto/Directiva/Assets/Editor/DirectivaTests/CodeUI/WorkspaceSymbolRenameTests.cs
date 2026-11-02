using System;
using System.Collections.Generic;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using Directiva.CodeUI;

namespace Directiva.EditorTests.CodeUITests
{
    internal static class WorkspaceSymbolRenameTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> action) =>
                tests.Add(new DirectivaTestCase("CodeUI", "WorkspaceRename", id, description, action));

            void Expect(DirectivaTestContext t, Dictionary<string, string> sources,
                string selectedModule, string identifier, int occurrence, string newName,
                Dictionary<string, string> expected)
            {
                int caret = TokenAt(sources[selectedModule], selectedModule, identifier, occurrence);
                var provider = new InMemoryDScriptSourceProvider(sources);
                bool ok = DScriptSymbolRename.TryRename(selectedModule, provider, sources.Keys,
                    caret, newName, out var plan, out var error);
                t.Check(ok, "workspace rename succeeds: " + error);
                if (!ok) return;
                t.Equal(expected.Count, plan.Changes.Count, "number of affected files");
                foreach (var pair in expected)
                {
                    string path = pair.Key.Replace('.', '/') + ".dscript";
                    t.Check(plan.Changes.ContainsKey(path), "modified file " + path);
                    t.Equal(pair.Value, plan.Changes[path], "text in " + path);
                }
                foreach (var pair in sources)
                    t.Equal(pair.Value, sources[pair.Key], "planning must not alter " + pair.Key);
                var result = new Dictionary<string, string>(sources);
                foreach (var pair in plan.Changes)
                    result[pair.Key.Substring(0, pair.Key.Length - ".dscript".Length).Replace('/', '.')] = pair.Value;
                var checkProvider = new InMemoryDScriptSourceProvider(result);
                foreach (var name in result.Keys)
                    t.Check(DScriptFrontend.Analyze(name, checkProvider).Success,
                        "all entry modules still compile: " + name);
            }

            void Reject(DirectivaTestContext t, Dictionary<string, string> sources,
                string selectedModule, string identifier, int occurrence, string newName)
            {
                int caret = TokenAt(sources[selectedModule], selectedModule, identifier, occurrence);
                bool ok = DScriptSymbolRename.TryRename(selectedModule,
                    new InMemoryDScriptSourceProvider(sources), sources.Keys,
                    caret, newName, out var plan, out _);
                t.Check(!ok, "unsafe rename must be refused");
                t.Check(plan == null, "no partial edit plan is returned");
            }

            Add("FunctionAllImports", "Rename definition, direct calls, module attributes and from-import uses", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib.calc"] = "def work(value):\n    return value + 1\n",
                    ["entry"] = "import lib.calc as c\ndef main():\n    return c.work(1)\n",
                    ["other"] = "from lib.calc import work\ndef use():\n    return work(2)\n",
                    ["aliased"] = "from lib.calc import work as do_work\ndef run():\n    return do_work(3)\n"
                }, "lib.calc", "work", 0, "solve", new Dictionary<string, string>
                {
                    ["lib.calc"] = "def solve(value):\n    return value + 1\n",
                    ["entry"] = "import lib.calc as c\ndef main():\n    return c.solve(1)\n",
                    ["other"] = "from lib.calc import solve\ndef use():\n    return solve(2)\n",
                    ["aliased"] = "from lib.calc import solve as do_work\ndef run():\n    return do_work(3)\n"
                }));

            Add("FunctionTransitiveImports", "References from a dependency cannot be mistaken for positions in the importing file", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["library"] = "def work(amount):\n    return amount + 1\n",
                    ["middle"] = "from library import work\ndef use():\n    return work(2)\n",
                    ["outer"] = "import middle\ndef main():\n    return middle.use()\n"
                }, "library", "work", 0, "solve", new Dictionary<string, string>
                {
                    ["library"] = "def solve(amount):\n    return amount + 1\n",
                    ["middle"] = "from library import solve\ndef use():\n    return solve(2)\n"
                }));

            Add("FunctionFromImportCursor", "F2 on imported function declaration selects the exported symbol", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "from lib import work\ndef test():\n    return work()\n"
                }, "user", "work", 0, "perform", new Dictionary<string, string>
                {
                    ["lib"] = "def perform():\n    return 1\n",
                    ["user"] = "from lib import perform\ndef test():\n    return perform()\n"
                }));

            Add("FunctionMemberCursor", "F2 on module.member selects the correct exported function", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "import lib\ndef test():\n    return lib.work()\n"
                }, "user", "work", 0, "perform", new Dictionary<string, string>
                {
                    ["lib"] = "def perform():\n    return 1\n",
                    ["user"] = "import lib\ndef test():\n    return lib.perform()\n"
                }));

            Add("FunctionIndependent", "Same-named functions from unrelated modules are not renamed", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["a"] = "def work():\n    return 1\n",
                    ["b"] = "def work():\n    return 2\n",
                    ["user"] = "import a\nimport b\ndef main():\n    return a.work() + b.work()\n"
                }, "a", "work", 0, "solve", new Dictionary<string, string>
                {
                    ["a"] = "def solve():\n    return 1\n",
                    ["user"] = "import a\nimport b\ndef main():\n    return a.solve() + b.work()\n"
                }));

            Add("FunctionWildcard", "Wildcard-import users follow a function rename", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "from lib import *\ndef main():\n    return work()\n"
                }, "lib", "work", 0, "run", new Dictionary<string, string>
                {
                    ["lib"] = "def run():\n    return 1\n",
                    ["user"] = "from lib import *\ndef main():\n    return run()\n"
                }));

            Add("FunctionReference", "Rename function references, not just call expressions", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "from lib import work\ndef main():\n    f = work\n    return f()\n"
                }, "lib", "work", 0, "run", new Dictionary<string, string>
                {
                    ["lib"] = "def run():\n    return 1\n",
                    ["user"] = "from lib import run\ndef main():\n    f = run\n    return f()\n"
                }));

            Add("SharedAllModules", "Rename one global slot across independent declarations and imports", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["a"] = "shared score = 1\ndef inc():\n    score += 1\n",
                    ["b"] = "shared score\ndef get():\n    return score\n",
                    ["c"] = "from a import score as points\ndef test():\n    return points\n",
                    ["d"] = "import a\ndef test():\n    return a.score\n",
                    ["e"] = "from b import score\ndef get():\n    return score\n"
                }, "a", "score", 0, "total", new Dictionary<string, string>
                {
                    ["a"] = "shared total = 1\ndef inc():\n    total += 1\n",
                    ["b"] = "shared total\ndef get():\n    return total\n",
                    ["c"] = "from a import total as points\ndef test():\n    return points\n",
                    ["d"] = "import a\ndef test():\n    return a.total\n",
                    ["e"] = "from b import total\ndef get():\n    return total\n"
                }));

            Add("SharedImportedInitializer", "Rename an imported shared used by an initializer in a transitive dependency", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["state"] = "shared amount = 2\n",
                    ["consumer"] = "from state import amount\nshared doubled = amount * 2\ndef read():\n    return doubled\n",
                    ["entry"] = "import consumer\ndef main():\n    return consumer.read()\n"
                }, "state", "amount", 0, "score", new Dictionary<string, string>
                {
                    ["state"] = "shared score = 2\n",
                    ["consumer"] = "from state import score\nshared doubled = score * 2\ndef read():\n    return doubled\n"
                }));

            Add("SharedFromReference", "Rename a shared variable starting at an imported use", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "shared score = 2\n",
                    ["user"] = "from lib import score\ndef main():\n    return score\n"
                }, "user", "score", 1, "points", new Dictionary<string, string>
                {
                    ["lib"] = "shared points = 2\n",
                    ["user"] = "from lib import points\ndef main():\n    return points\n"
                }));

            Add("SharedForTarget", "Global for-loop target is also a bound shared reference", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["main"] = "shared item\ndef f():\n    for item in range(2):\n        print(item)\n"
                }, "main", "item", 0, "element", new Dictionary<string, string>
                {
                    ["main"] = "shared element\ndef f():\n    for element in range(2):\n        print(element)\n"
                }));

            Add("SharedForAlias", "An explicitly aliased shared for-target keeps its local import spelling", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "shared item\n",
                    ["user"] = "from lib import item as current\ndef main():\n    for current in range(2):\n        print(current)\n"
                }, "lib", "item", 0, "element", new Dictionary<string, string>
                {
                    ["lib"] = "shared element\n",
                    ["user"] = "from lib import element as current\ndef main():\n    for current in range(2):\n        print(current)\n"
                }));

            Add("SharedCollision", "Two independent shared slots must not silently merge", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["a"] = "shared score\n",
                    ["b"] = "shared points\n"
                }, "a", "score", 0, "points"));

            Add("FunctionCollision", "Cannot rename a function over another declared function", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["main"] = "def f():\n    return 1\ndef g():\n    return 2\n"
                }, "main", "f", 0, "g"));

            Add("SharedLocalCapture", "Reject a valid-looking rename that changes local binding identity", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["main"] = "shared count = 1\ndef main():\n    value = 2\n    return value + count\n"
                }, "main", "count", 0, "value"));

            Add("ExplicitAliasNotGlobal", "F2 on an explicit alias must not silently rename its exporter", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "from lib import work as alias\ndef main():\n    return alias()\n"
                }, "user", "alias", 1, "solve"));

            Add("UnrelatedText", "Function rename ignores string literals, comments, and dynamic members", t =>
                Expect(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    print('work') # work\n    return 1\n",
                    ["user"] = "import lib\ndef main():\n    return lib.work()\n"
                }, "lib", "work", 0, "solve", new Dictionary<string, string>
                {
                    ["lib"] = "def solve():\n    print('work') # work\n    return 1\n",
                    ["user"] = "import lib\ndef main():\n    return lib.solve()\n"
                }));

            Add("CRLF", "Cross-file edits use LF positions even when source files have CRLF", t =>
            {
                var files = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\r\n    return 1\r\n",
                    ["user"] = "from lib import work\r\ndef main():\r\n    return work()\r\n"
                };
                int caret = TokenAt(files["lib"], "lib", "work", 0);
                bool ok = DScriptSymbolRename.TryRename("lib", new InMemoryDScriptSourceProvider(files),
                    files.Keys, caret, "solve", out var plan, out var error);
                t.Check(ok, "CRLF rename: " + error);
                if (!ok) return;
                t.Equal("def solve():\n    return 1\n", plan.Changes["lib.dscript"], "LF editor source");
                t.Equal("from lib import solve\ndef main():\n    return solve()\n",
                    plan.Changes["user.dscript"], "LF imported source");
            });

            Add("NoOp", "Same-name rename does not modify any files", t =>
            {
                var files = new Dictionary<string, string> { ["main"] = "shared score\n" };
                bool ok = DScriptSymbolRename.TryRename("main", new InMemoryDScriptSourceProvider(files),
                    files.Keys, TokenAt(files["main"], "main", "score", 0), "score",
                    out var plan, out var error);
                t.Check(ok, "no-op succeeds: " + error);
                if (ok) { t.Equal(0, plan.Count, "zero edits"); t.Equal(0, plan.Changes.Count, "zero files"); }
            });

            Add("UnrelatedErrors", "Refuse to partially rename if another workspace script cannot be analyzed", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["main"] = "def work():\n    return 1\n",
                    ["broken"] = "def broken(:\n    pass\n"
                }, "main", "work", 0, "solve"));

            Add("CommitBackups", "Coordinated edits modify working copies, not saved .dscript files", t =>
            {
                var files = new Dictionary<string, string>
                {
                    ["lib.dscript"] = "def work():\n    return 1\n",
                    ["main.dscript"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(files);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                var sources = new Dictionary<string, string>
                {
                    ["lib"] = files["lib.dscript"], ["main"] = files["main.dscript"]
                };
                bool planned = DScriptSymbolRename.TryRename("main", new InMemoryDScriptSourceProvider(sources),
                    sources.Keys, TokenAt(sources["main"], "main", "work", 0), "run",
                    out var plan, out string error);
                t.Check(planned, "plan: " + error);
                if (!planned) return;
                bool applied = workspace.TryApplyRefactoring(plan.Originals, plan.Changes,
                    plan.Caret, (content, _) => { workspace.UpdateActiveWorkingContent(content); return true; }, out error);
                t.Check(applied, "batch commit: " + error);
                t.Equal(files["lib.dscript"], storage.ReadSaved("lib.dscript"), "saved library unchanged");
                t.Equal(files["main.dscript"], storage.ReadSaved("main.dscript"), "saved main unchanged");
                t.Equal(plan.Changes["lib.dscript"], storage.ReadBackup("lib.dscript"), "library backup modified");
                t.Equal(plan.Changes["main.dscript"], workspace.Active.WorkingContent, "active buffer modified");
            });

            Add("AbortStale", "A stale source aborts the complete batch without writing backups", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib.dscript"] = "def work():\n    return 1\n",
                    ["main.dscript"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(saved);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                var sources = new Dictionary<string, string>
                {
                    ["lib"] = saved["lib.dscript"], ["main"] = saved["main.dscript"]
                };
                bool planned = DScriptSymbolRename.TryRename("main", new InMemoryDScriptSourceProvider(sources),
                    sources.Keys, TokenAt(sources["main"], "main", "work", 0), "run",
                    out var plan, out string error);
                t.Check(planned, "plan: " + error);
                if (!planned) return;
                workspace.UpdateActiveWorkingContent("def changed():\n    pass\n");
                bool applied = workspace.TryApplyRefactoring(plan.Originals, plan.Changes,
                    plan.Caret, (_, __) => true, out error);
                t.Check(!applied, "stale snapshot rejected");
                t.Check(!storage.BackupExists("lib.dscript"), "no unrelated backup written");
            });

            Add("AbortEditorFailure", "A failed active edit restores inactive backups", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib.dscript"] = "def work():\n    return 1\n",
                    ["main.dscript"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(saved);
                storage.WriteBackup("lib.dscript", "def work():\n    return 1\n");
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                var sources = new Dictionary<string, string>
                {
                    ["lib"] = saved["lib.dscript"], ["main"] = saved["main.dscript"]
                };
                bool planned = DScriptSymbolRename.TryRename("main", new InMemoryDScriptSourceProvider(sources),
                    sources.Keys, TokenAt(sources["main"], "main", "work", 0), "run",
                    out var plan, out string error);
                t.Check(planned, "plan: " + error);
                if (!planned) return;
                bool applied = workspace.TryApplyRefactoring(plan.Originals, plan.Changes,
                    plan.Caret, (_, __) => false, out error);
                t.Check(!applied, "editor rejects the commit");
                t.Equal(saved["lib.dscript"], storage.ReadBackup("lib.dscript"), "old backup restored");
            });

            Add("LocalStillWorks", "F2 keeps local rename behavior and does not scan unrelated roots", t =>
            {
                var files = new Dictionary<string, string>
                {
                    ["main"] = "def main(value):\n    return value\n",
                    ["broken"] = "def broken(:\n    pass\n"
                };
                bool ok = DScriptSymbolRename.TryRename("main", new InMemoryDScriptSourceProvider(files),
                    files.Keys, TokenAt(files["main"], "main", "value", 0), "number",
                    out var plan, out var error);
                t.Check(ok, "local rename still succeeds: " + error);
                if (ok) t.Equal("def main(number):\n    return number\n",
                    plan.Changes["main.dscript"], "local edit");
            });
        }

        private sealed class MemoryStorage : IScriptStorage
        {
            private readonly Dictionary<string, string> saved;
            private readonly Dictionary<string, string> backups =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string RootPath => string.Empty;
            public MemoryStorage(Dictionary<string, string> scripts)
            { saved = new Dictionary<string, string>(scripts, StringComparer.OrdinalIgnoreCase); }
            public void EnsureRoot() { }
            public IEnumerable<string> EnumerateDirectories(string folder = "") => Array.Empty<string>();
            public IEnumerable<string> EnumerateScripts(string folder = "") => saved.Keys;
            public bool DirectoryExists(string path) => false;
            public bool ScriptExists(string path) => saved.ContainsKey(path);
            public bool BackupExists(string path) => backups.ContainsKey(path);
            public string ReadSaved(string path) => saved[path];
            public string ReadBackup(string path) => backups[path];
            public void WriteSaved(string path, string content) => saved[path] = content;
            public void WriteBackup(string path, string content) => backups[path] = content;
            public void DeleteBackup(string path) => backups.Remove(path);
            public string CreateScript(string folder, string name, string content = "") => throw new NotSupportedException();
            public string CreateFolder(string folder, string name) => throw new NotSupportedException();
            public void DeleteScript(string path) => throw new NotSupportedException();
            public void DeleteFolder(string path) => throw new NotSupportedException();
            public string RenameScript(string path, string name) => throw new NotSupportedException();
            public string RenameFolder(string path, string name) => throw new NotSupportedException();
            public string MoveScript(string path, string folder) => throw new NotSupportedException();
            public string MoveFolder(string path, string folder) => throw new NotSupportedException();
            public string DuplicateScript(string path, string name, string content) => throw new NotSupportedException();
            public string DuplicateFolder(string path, string name) => throw new NotSupportedException();
        }

        private static int TokenAt(string text, string module, string name, int occurrence)
        {
            var matches = new DScriptLexer(module, text.Replace("\r\n", "\n")).Tokenize()
                .Where(token => token.Kind == TokenKind.Identifier && token.Text == name).ToArray();
            if (occurrence < 0 || occurrence >= matches.Length)
                throw new InvalidOperationException("Invalid test cursor: " + module + "." + name + " #" + occurrence);
            return matches[occurrence].Span.Start;
        }
    }
}
