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

            // For intentionally invalid fixtures, successful refactoring must preserve
            // existing errors; requiring every result to compile would defeat the test.
            void ExpectWithErrors(DirectivaTestContext t, Dictionary<string, string> sources,
                string selectedModule, string identifier, string newName,
                Dictionary<string, string> expected)
            {
                var provider = new InMemoryDScriptSourceProvider(sources);
                int caret = TokenAt(sources[selectedModule], selectedModule, identifier, 0);
                bool ok = DScriptSymbolRename.TryRename(selectedModule, provider,
                    sources.Keys, caret, newName, out var plan, out string error);
                t.Check(ok, "rename with pre-existing errors: " + error);
                if (!ok) return;
                t.Equal(expected.Count, plan.Changes.Count, "only expected files changed");
                foreach (var pair in expected)
                {
                    string path = pair.Key.Replace('.', '/') + ".dscript";
                    t.Check(plan.Changes.ContainsKey(path), "changed: " + path);
                    if (plan.Changes.TryGetValue(path, out var content))
                        t.Equal(pair.Value, content, "refactored " + path);
                }
                foreach (var pair in sources)
                    t.Equal(pair.Value, sources[pair.Key], "planning does not mutate " + pair.Key);
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

            Add("StrictCompilerUnchanged", "The IDE-only partial binder must not change normal compilation", t =>
            {
                var sources = new Dictionary<string, string>
                {
                    ["main"] = "def work():\n    broken =\n    return 1\n"
                };
                var provider = new InMemoryDScriptSourceProvider(sources);
                var strict = DScriptFrontend.Analyze("main", provider);
                var partial = DScriptFrontend.AnalyzeForRefactoring("main", provider);
                t.Check(!strict.Success, "normal compiler still reports syntax errors");
                t.Check(strict.ModuleSymbols.Count == 0, "normal compiler does not bind an invalid graph");
                t.Check(!partial.Success, "IDE analysis keeps original diagnostics");
                t.Check(partial.ModuleSymbols.ContainsKey("main"),
                    "IDE analysis recovers the available function declaration");
                t.Check(partial.ModuleSymbols["main"].Functions.ContainsKey("work"),
                    "IDE partial binder exposes the function symbol");
            });

            Add("UnrelatedErrors", "A syntax error in an unrelated script does not block renaming", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["main"] = "def work():\n    return 1\n",
                    ["broken"] = "def broken(:\n    pass\n"
                }, "main", "work", "solve", new Dictionary<string, string>
                {
                    ["main"] = "def solve():\n    return 1\n"
                }));

            Add("UnrelatedSemanticError", "An undefined name in another file does not block a function rename", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["consumer"] = "from lib import work\ndef use():\n    return work()\n",
                    ["broken"] = "def other():\n    return not_declared\n"
                }, "lib", "work", "solve", new Dictionary<string, string>
                {
                    ["lib"] = "def solve():\n    return 1\n",
                    ["consumer"] = "from lib import solve\ndef use():\n    return solve()\n"
                }));

            Add("OwnerSemanticError", "A function can be renamed when its body has unrelated semantic errors", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return not_declared\n",
                    ["consumer"] = "import lib\ndef use():\n    return lib.work()\n"
                }, "lib", "work", "solve", new Dictionary<string, string>
                {
                    ["lib"] = "def solve():\n    return not_declared\n",
                    ["consumer"] = "import lib\ndef use():\n    return lib.solve()\n"
                }));

            Add("RecoveredSyntaxReferences", "Parser recovery keeps references on valid lines before and after a broken line", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["consumer"] = "from lib import work\ndef use():\n    work()\n    invalid =\n    return work()\n"
                }, "lib", "work", "solve", new Dictionary<string, string>
                {
                    ["lib"] = "def solve():\n    return 1\n",
                    ["consumer"] = "from lib import solve\ndef use():\n    solve()\n    invalid =\n    return solve()\n"
                }));

            Add("MissingDependency", "An unrelated missing import does not hide a resolved function reference", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["consumer"] = "from lib import work\nimport missing_module\ndef use():\n    return work()\n"
                }, "lib", "work", "solve", new Dictionary<string, string>
                {
                    ["lib"] = "def solve():\n    return 1\n",
                    ["consumer"] = "from lib import solve\nimport missing_module\ndef use():\n    return solve()\n"
                }));

            Add("SharedUnrelatedSyntax", "Broken independent scripts do not block a workspace-wide shared rename", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["state"] = "shared score = 2\n",
                    ["other"] = "shared score\ndef use():\n    return score\n",
                    ["broken"] = "def unfinished(:\n    pass\n"
                }, "state", "score", "points", new Dictionary<string, string>
                {
                    ["state"] = "shared points = 2\n",
                    ["other"] = "shared points\ndef use():\n    return points\n"
                }));

            Add("SharedRecoveredSyntax", "A shared declaration can be renamed alongside an unrelated broken statement", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["state"] = "shared score = 2\nshared broken =\ndef use():\n    return score\n"
                }, "state", "score", "points", new Dictionary<string, string>
                {
                    ["state"] = "shared points = 2\nshared broken =\ndef use():\n    return points\n"
                }));

            Add("RejectDiscardedReference", "A possible reference lost by parser recovery must not be silently skipped", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["consumer"] = "from lib import work\ndef use():\n    return (work + )\n"
                }, "lib", "work", 0, "solve"));

            Add("RejectCaptureUndefined", "A new function name must not capture a previously undefined reference", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["main"] = "def work():\n    return 1\ndef use():\n    return missing()\n"
                }, "main", "work", 0, "missing"));

            Add("RejectCaptureUnresolvedImport", "A rename must not resolve an unrelated from-import that used to fail", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["consumer"] = "from lib import solve\ndef use():\n    return solve()\n"
                }, "lib", "work", 0, "solve"));

            Add("RejectCaptureUnresolvedMember", "A rename must not make a previously invalid module member resolve", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["consumer"] = "import lib\ndef use():\n    return lib.solve()\n"
                }, "lib", "work", 0, "solve"));

            Add("UnrelatedBrokenName", "A same-spelled name in an unrelated malformed script is not a reference", t =>
                ExpectWithErrors(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["broken"] = "def bad(:\n    print(work)\n"
                }, "lib", "work", "solve", new Dictionary<string, string>
                {
                    ["lib"] = "def solve():\n    return 1\n"
                }));

            Add("RejectNewImportCollision", "Pre-existing errors do not authorize a new import alias collision", t =>
                Reject(t, new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["other"] = "def solve():\n    return 2\n",
                    ["consumer"] = "from lib import work\nfrom other import solve\ndef use():\n    return work()\n",
                    ["broken"] = "def unfinished(:\n    pass\n"
                }, "lib", "work", 0, "solve"));

            Add("SavedWithUnrelatedErrors", "Saved-plan verification also tolerates independent broken scripts", t =>
            {
                var sources = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "from lib import work\ndef use():\n    return work()\n",
                    ["broken"] = "def broken(:\n    pass\n"
                };
                bool ok = DScriptSymbolRename.TryRename("lib",
                    new InMemoryDScriptSourceProvider(sources), sources.Keys,
                    TokenAt(sources["lib"], "lib", "work", 0), "solve",
                    out var working, out string error);
                t.Check(ok, "working plan: " + error);
                if (!ok) return;
                ok = DScriptSymbolRename.TryRenameSavedSymbol(working,
                    new InMemoryDScriptSourceProvider(sources), sources.Keys,
                    out var saved, out error);
                t.Check(ok, "saved plan: " + error);
                if (ok) t.Equal(working.Changes.Count, saved.Changes.Count,
                    "both versions change the same number of files");
            });

            bool Prepare(DirectivaTestContext t, Dictionary<string, string> working,
                Dictionary<string, string> saved, string selectedModule, string name, string newName,
                out DScriptSymbolRename.Plan workPlan, out DScriptSymbolRename.Plan diskPlan)
            {
                bool ok = DScriptSymbolRename.TryRename(selectedModule,
                    new InMemoryDScriptSourceProvider(working), working.Keys,
                    TokenAt(working[selectedModule], selectedModule, name, 0), newName,
                    out workPlan, out string error);
                t.Check(ok, "working rename plan: " + error);
                diskPlan = null;
                if (!ok) return false;
                ok = DScriptSymbolRename.TryRenameSavedSymbol(workPlan,
                    new InMemoryDScriptSourceProvider(saved), saved.Keys,
                    out diskPlan, out error);
                t.Check(ok, "saved rename plan: " + error);
                return ok;
            }

            bool Apply(ScriptWorkspace workspace, DScriptSymbolRename.Plan workPlan,
                DScriptSymbolRename.Plan diskPlan, out string error) =>
                workspace.TryApplyRefactoring(workPlan, diskPlan,
                    (content, _) => { workspace.UpdateActiveWorkingContent(content); return true; }, out error);

            Add("CommitClean", "A clean cross-file rename saves every affected .dscript automatically", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                if (!Prepare(t, saved, saved, "main", "work", "run", out var work, out var disk)) return;
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(ok, "commit: " + error);
                if (!ok) return;
                t.Equal(disk.Changes["lib.dscript"], storage.ReadSaved("lib.dscript"), "library saved");
                t.Equal(disk.Changes["main.dscript"], storage.ReadSaved("main.dscript"), "main saved");
                t.Check(!storage.BackupExists("lib.dscript"), "no library backup");
                t.Check(!storage.BackupExists("main.dscript"), "no main backup");
                t.Check(!workspace.Active.IsDirty, "active buffer is clean");
                workspace.Revert("main.dscript");
                t.Equal(disk.Changes["main.dscript"], workspace.Active.WorkingContent,
                    "reverting clean document preserves the refactor");
            });

            Add("CommitDirtyInactive", "A dirty imported script retains unrelated edits and a refactored saved base", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["lib"] = "def work():\n    return 2\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                storage.WriteBackup("lib.dscript", working["lib"]);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                if (!Prepare(t, working, saved, "main", "work", "run", out var work, out var disk)) return;
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(ok, "commit: " + error);
                if (!ok) return;
                t.Equal("def run():\n    return 1\n", storage.ReadSaved("lib.dscript"),
                    "saved library has rename but not pending edit");
                t.Equal("def run():\n    return 2\n", storage.ReadBackup("lib.dscript"),
                    "working library retains rename and pending edit");
                t.Check(workspace.IsDirty("lib.dscript"), "library stays dirty");
                workspace.Revert("lib.dscript");
                t.Equal("def run():\n    return 1\n", workspace.GetWorkingContent("lib.dscript"),
                    "revert drops only the unrelated edit");
            });

            Add("CommitDirtyActive", "The active buffer's saved base is refactored without saving other edits", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["main"] = "from lib import work\ndef main():\n    print('draft')\n    return work()\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                workspace.UpdateActiveWorkingContent(working["main"]);
                if (!Prepare(t, working, saved, "main", "work", "run", out var work, out var disk)) return;
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(ok, "commit: " + error);
                if (!ok) return;
                t.Equal("from lib import run\ndef main():\n    return run()\n",
                    storage.ReadSaved("main.dscript"), "saved main has only rename");
                t.Equal("from lib import run\ndef main():\n    print('draft')\n    return run()\n",
                    workspace.Active.WorkingContent, "draft kept in editor");
                t.Check(workspace.Active.IsDirty, "active file is still dirty");
                t.Check(storage.BackupExists("main.dscript"), "active draft persisted in .back");
                workspace.Revert("main.dscript");
                t.Equal("from lib import run\ndef main():\n    return run()\n",
                    workspace.Active.WorkingContent, "revert keeps rename and drops draft");
                t.Check(!workspace.Active.IsDirty, "revert leaves clean editor");
            });

            Add("SavedOnlyReference", "Saved-only references are renamed even when removed in the working copy", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["user"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["user"] = "def main():\n    return 7\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                storage.WriteBackup("user.dscript", working["user"]);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("lib.dscript");
                if (!Prepare(t, working, saved, "lib", "work", "run", out var work, out var disk)) return;
                t.Check(!work.Changes.ContainsKey("user.dscript"), "working user has no symbol to rename");
                t.Check(disk.Changes.ContainsKey("user.dscript"), "saved user still references symbol");
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(ok, "commit: " + error);
                if (!ok) return;
                t.Equal("from lib import run\ndef main():\n    return run()\n",
                    storage.ReadSaved("user.dscript"), "saved-only reference updated");
                t.Equal(working["user"], storage.ReadBackup("user.dscript"), "working code unchanged");
                workspace.Revert("user.dscript");
                t.Equal(storage.ReadSaved("user.dscript"), workspace.GetWorkingContent("user.dscript"),
                    "revert restores renamed reference");
            });

            Add("CommitShared", "Shared rename updates clean and dirty declarations independently", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["a"] = "shared score = 1\n",
                    ["b"] = "shared score\ndef get():\n    return score\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["b"] = "shared score\ndef get():\n    print('pending')\n    return score\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                storage.WriteBackup("b.dscript", working["b"]);
                var workspace = new ScriptWorkspace(storage);
                int persistedChanges = 0;
                workspace.SavedScriptsChanged += () => persistedChanges++;
                workspace.Open("a.dscript");
                if (!Prepare(t, working, saved, "a", "score", "total", out var work, out var disk)) return;
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(ok, "commit: " + error);
                if (!ok) return;
                t.Equal(1, persistedChanges, "multi-file refactor sends one persisted-source notification");
                t.Equal("shared total = 1\n", storage.ReadSaved("a.dscript"), "clean declaration saved");
                t.Equal("shared total\ndef get():\n    return total\n", storage.ReadSaved("b.dscript"),
                    "dirty declaration renamed without extra print");
                t.Equal("shared total\ndef get():\n    print('pending')\n    return total\n",
                    storage.ReadBackup("b.dscript"), "dirty copy retains print");
            });

            Add("UnrelatedDirty", "Refactor does not save unrelated dirty scripts", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n",
                    ["other"] = "def another():\n    return 0\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                string extra = "def another():\n    return 999\n";
                storage.WriteBackup("other.dscript", extra);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                if (!Prepare(t, saved, saved, "main", "work", "run", out var work, out var disk)) return;
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(ok, "commit: " + error);
                t.Equal(saved["other"], storage.ReadSaved("other.dscript"), "unrelated saved unchanged");
                t.Equal(extra, storage.ReadBackup("other.dscript"), "unrelated backup unchanged");
            });

            Add("FallbackInvalidSaved", "If a saved source no longer declares the symbol, only working copies are refactored", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def other():\n    return 1\n",
                    ["main"] = "def main(:\n    pass\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                storage.WriteBackup("lib.dscript", working["lib"]);
                var workspace = new ScriptWorkspace(storage);
                int persistedChanges = 0;
                workspace.SavedScriptsChanged += () => persistedChanges++;
                workspace.Open("main.dscript");
                workspace.UpdateActiveWorkingContent(working["main"]);
                bool planned = DScriptSymbolRename.TryRename("main",
                    new InMemoryDScriptSourceProvider(working), working.Keys,
                    TokenAt(working["main"], "main", "work", 0), "run", out var work, out string error);
                t.Check(planned, "working plan: " + error);
                if (!planned) return;
                bool savedOk = DScriptSymbolRename.TryRenameSavedSymbol(work,
                    new InMemoryDScriptSourceProvider(saved), saved.Keys, out _, out _);
                t.Check(!savedOk, "saved sources do not contain lib.work");
                bool ok = Apply(workspace, work, null, out error);
                t.Check(ok, "fallback commit: " + error);
                if (!ok) return;
                t.Equal(0, persistedChanges, "working-only refactor does not reload saved programs");
                t.Equal(saved["lib"], storage.ReadSaved("lib.dscript"),
                    "saved library left unchanged in fallback");
                t.Equal("def run():\n    return 1\n", storage.ReadBackup("lib.dscript"),
                    "library rename remains in backup");
                t.Equal(saved["main"], storage.ReadSaved("main.dscript"),
                    "invalid saved copy stays untouched");
                t.Equal("from lib import run\ndef main():\n    return run()\n",
                    workspace.Active.WorkingContent, "working rename still applied");
            });

            Add("SavedUnrelatedSyntaxAutosaved", "A saved script with unrelated syntax errors does not block a safe saved rename", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "def main(:\n    pass\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                workspace.UpdateActiveWorkingContent(working["main"]);
                bool planned = DScriptSymbolRename.TryRename("main",
                    new InMemoryDScriptSourceProvider(working), working.Keys,
                    TokenAt(working["main"], "main", "work", 0), "run", out var work, out string error);
                t.Check(planned, "working plan: " + error);
                if (!planned) return;
                bool savedOk = DScriptSymbolRename.TryRenameSavedSymbol(work,
                    new InMemoryDScriptSourceProvider(saved), saved.Keys, out var disk, out error);
                t.Check(savedOk, "saved symbol remains resolvable: " + error);
                if (!savedOk) return;
                bool ok = Apply(workspace, work, disk, out error);
                t.Check(ok, "commit: " + error);
                if (!ok) return;
                t.Equal("def run():\n    return 1\n", storage.ReadSaved("lib.dscript"),
                    "clean library saved despite unrelated invalid script");
                t.Check(!storage.BackupExists("lib.dscript"), "clean library needs no backup");
                t.Equal(saved["main"], storage.ReadSaved("main.dscript"),
                    "unrelated invalid saved main remains unchanged");
                t.Equal("from lib import run\ndef main():\n    return run()\n",
                    workspace.Active.WorkingContent, "working main updated");
                workspace.Revert("main.dscript");
                t.Equal(saved["main"], workspace.Active.WorkingContent,
                    "revert restores saved syntax error, not the old library name");
            });

            Add("AbortStale", "A stale working source aborts the complete batch without writing saved or backups", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                if (!Prepare(t, saved, saved, "main", "work", "run", out var work, out var disk)) return;
                workspace.UpdateActiveWorkingContent("def modified():\n    pass\n");
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(!ok, "stale snapshot rejected");
                t.Equal(saved["lib"], storage.ReadSaved("lib.dscript"), "saved library unchanged");
                t.Check(!storage.BackupExists("lib.dscript"), "no unrelated backup written");
            });

            Add("AbortStaleSaved", "Changing a saved base after planning aborts without touching working copies", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["lib"] = "def work():\n    return 2\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                storage.WriteBackup("lib.dscript", working["lib"]);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                if (!Prepare(t, working, saved, "main", "work", "run", out var work, out var disk)) return;
                storage.WriteSaved("lib.dscript", "def work():\n    return 3\n");
                bool ok = Apply(workspace, work, disk, out string error);
                t.Check(!ok, "stale saved snapshot rejected");
                t.Equal("def work():\n    return 3\n", storage.ReadSaved("lib.dscript"),
                    "external saved edit preserved");
                t.Equal(working["lib"], storage.ReadBackup("lib.dscript"), "backup remains original");
                t.Equal(saved["main"], storage.ReadSaved("main.dscript"), "other file not touched");
            });

            Add("AbortEditorFailure", "Editor failure restores saved scripts and backups", t =>
            {
                var saved = new Dictionary<string, string>
                {
                    ["lib"] = "def work():\n    return 1\n",
                    ["main"] = "from lib import work\ndef main():\n    return work()\n"
                };
                var working = new Dictionary<string, string>(saved)
                {
                    ["lib"] = "def work():\n    return 2\n"
                };
                var storage = new MemoryStorage(saved.ToDictionary(e => e.Key + ".dscript", e => e.Value));
                storage.WriteBackup("lib.dscript", working["lib"]);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                if (!Prepare(t, working, saved, "main", "work", "run", out var work, out var disk)) return;
                bool ok = workspace.TryApplyRefactoring(work, disk, (_, __) => false, out string error);
                t.Check(!ok, "editor rejects commit");
                t.Equal(saved["lib"], storage.ReadSaved("lib.dscript"), "library saved restored");
                t.Equal(saved["main"], storage.ReadSaved("main.dscript"), "main saved restored");
                t.Equal(working["lib"], storage.ReadBackup("lib.dscript"), "old backup restored");
                t.Check(!storage.BackupExists("main.dscript"), "no main backup after rollback");
                t.Check(!workspace.Active.IsDirty, "editor state stays clean");
            });

            Add("LocalNotAutosaved", "Renaming a local still behaves like an ordinary unsaved text edit", t =>
            {
                string initial = "def main(value):\n    return value\n";
                var saved = new Dictionary<string, string> { ["main.dscript"] = initial };
                var storage = new MemoryStorage(saved);
                var workspace = new ScriptWorkspace(storage);
                workspace.Open("main.dscript");
                var sources = new Dictionary<string, string> { ["main"] = initial };
                bool planned = DScriptSymbolRename.TryRename("main",
                    new InMemoryDScriptSourceProvider(sources), sources.Keys,
                    TokenAt(initial, "main", "value", 0), "number", out var work, out string error);
                t.Check(planned, "local plan: " + error);
                if (!planned) return;
                bool ok = Apply(workspace, work, null, out error);
                t.Check(ok, "local edit: " + error);
                t.Equal(initial, storage.ReadSaved("main.dscript"), "saved local remains unchanged");
                t.Check(workspace.Active.IsDirty, "local rename requires manual save");
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
