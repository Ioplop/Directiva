using System;
using System.Collections.Generic;
using Directiva.CodeUI;
using DSCompilation.DScript.Modules;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Source locks use compiled module names, never the filesystem or serialized metadata.</summary>
    internal static class ExecutionSourceLockTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "ExecutionSourceLock", id, description, run));

            Add("CompiledModules", "Only compiled source modules and imports are locked", t =>
            {
                var sources = new InMemoryDScriptSourceProvider(new Dictionary<string, string>
                {
                    ["main"] = "import utils.helper\ndef main():\n    return 1\n",
                    ["utils.helper"] = "def ping():\n    return 2\n"
                });
                var prepared = DScriptEditorRunPipeline.Prepare("main", "main", sources);
                t.Check(prepared.Success, "program compiles");
                var sourceLock = new ExecutionSourceLock();
                sourceLock.LockModules(prepared.Compilation.Analysis.Modules.Keys);
                t.Check(sourceLock.IsFileLocked("main.dscript"), "main locked");
                t.Check(sourceLock.IsFileLocked("utils/helper.dscript"), "import locked");
                t.Check(!sourceLock.IsFileLocked("other.dscript"), "unrelated file editable");
            });

            Add("CaseAndSeparator", "Source locks normalize separators and case", t =>
            {
                var sourceLock = new ExecutionSourceLock();
                sourceLock.LockModules(new[] { "AI.Helpers" });
                t.Check(sourceLock.IsFileLocked("ai\\helpers.dscript"), "Windows path normalized");
                t.Check(sourceLock.IsFileLocked("AI/Helpers.dscript"), "case-insensitive");
                t.Check(!sourceLock.IsFileLocked("ai/helpers.dscript.back"), "backups not source files");
            });

            Add("ParentFolders", "A folder containing a locked script cannot be renamed, deleted or moved", t =>
            {
                var sourceLock = new ExecutionSourceLock();
                sourceLock.LockModules(new[] { "lib.utils.helper" });
                t.Check(sourceLock.ContainsLockedFile("lib"), "parent folder");
                t.Check(sourceLock.ContainsLockedFile("lib/utils"), "immediate folder");
                t.Check(sourceLock.ContainsLockedFile("lib/utils/helper.dscript"), "source itself");
                t.Check(!sourceLock.ContainsLockedFile("library"), "prefix without slash is unrelated");
                t.Check(!sourceLock.ContainsLockedFile("lib/other"), "sibling folder unaffected");
            });

            Add("ClearAndRecompile", "Stopping unlocks everything and a later execution replaces the source set", t =>
            {
                var sourceLock = new ExecutionSourceLock();
                sourceLock.LockModules(new[] { "main", "lib.util" });
                t.Check(sourceLock.HasLockedFiles, "active set");
                sourceLock.Clear();
                t.Check(!sourceLock.HasLockedFiles, "cleared");
                t.Check(!sourceLock.ContainsLockedFile(""), "root no longer locked");
                sourceLock.LockModules(new[] { "other" });
                t.Check(!sourceLock.IsFileLocked("main.dscript"), "old source remains unlocked");
                t.Check(sourceLock.IsFileLocked("other.dscript"), "new source locked");
            });

            Add("NoModules", "No execution and empty module collections leave sources editable", t =>
            {
                var sourceLock = new ExecutionSourceLock();
                sourceLock.LockModules(null);
                t.Check(!sourceLock.HasLockedFiles, "null modules");
                sourceLock.LockModules(new[] { "", "  " });
                t.Check(!sourceLock.HasLockedFiles, "blank modules");
                t.Check(!sourceLock.ContainsLockedFile("main.dscript"), "editable after failed compile");
            });
        }
    }
}
