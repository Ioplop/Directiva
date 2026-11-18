using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Directiva.CodeUI;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Folder imports are path-based; duplicate and rename have different scopes.</summary>
    internal static class FolderImportRefactorTests
    {
        private static void WithWorkspace(Action<ScriptWorkspace, FileSystemScriptStorage> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "DirectivaFolderImports_" +
                Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new FileSystemScriptStorage(root);
                storage.EnsureRoot();
                test(new ScriptWorkspace(storage), storage);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "FolderImports", id, description, run));

            Add("OnlyValidImportsAreRewritten",
                "Imports are rewritten by syntax, not by matching text in comments or literals", t =>
            {
                const string original =
                    "import F.A\nimport F.B as bee\nfrom F.C import f as alias, g\n" +
                    "# import F.A\nshared note = \"from F.B import f\"\n" +
                    "import Foo.A\nimport F_copy.A\nfrom F import x\n";
                string result = DScriptFolderImportRefactor.Rewrite(original, "F", "F_copy");
                t.Contains("import F_copy.A\nimport F_copy.B as bee", result, "ordinary and aliased import");
                t.Contains("from F_copy.C import f as alias, g", result, "from-import aliases preserved");
                t.Contains("# import F.A", result, "comment remains unchanged");
                t.Contains("\"from F.B import f\"", result, "string remains unchanged");
                t.Contains("import Foo.A", result, "similar folder is not rewritten");
                t.Contains("from F import x", result, "F.dscript is not a child of folder F");
                t.Equal(2, result.Split(new[] { "import F_copy.A" }, StringSplitOptions.None).Length - 1,
                    "both existing and rewritten imports preserved");
            });

            Add("DuplicateRetargetsInsideOnly",
                "A duplicated folder changes only imports inside the new copy", t => WithWorkspace((w, s) =>
            {
                w.CreateFolder("", "F");
                w.CreateScript("F", "A");
                w.CreateScript("F", "B");
                w.CreateScript("", "Outside");
                s.WriteSaved("F/A.dscript", "import F.B\nfrom Outside import other\n# import F.B\n");
                s.WriteSaved("F/B.dscript", "import F.A\n");
                s.WriteSaved("Outside.dscript", "from F.A import main\n");
                s.WriteBackup("F/A.dscript", "import F.B\nfrom Outside import other\n# import F.B\n# pending edit\n");

                string copy = w.DuplicateFolder("F", "F_copy");
                t.Equal("F_copy", copy, "duplicate folder name");
                t.Equal("import F.B\nfrom Outside import other\n# import F.B\n",
                    s.ReadSaved("F/A.dscript"), "original saved content untouched");
                t.Contains("import F.B", s.ReadBackup("F/A.dscript"), "original working copy untouched");
                t.Contains("import F_copy.B", s.ReadSaved("F_copy/A.dscript"), "copy targets copied B");
                t.Contains("import F_copy.A", s.ReadSaved("F_copy/B.dscript"), "circular import copied");
                t.Contains("from Outside import other", s.ReadSaved("F_copy/A.dscript"),
                    "external import unchanged");
                t.Contains("# pending edit", s.ReadSaved("F_copy/A.dscript"),
                    "duplicate uses latest working content");
                t.Check(!s.BackupExists("F_copy/A.dscript"), "duplicated script starts clean");
                t.Contains("from F.A import main", s.ReadSaved("Outside.dscript"),
                    "external file untouched by duplication");
            }));

            Add("RenameRewritesWholeSolution",
                "Renaming updates circular imports and references from outside the folder", t => WithWorkspace((w, s) =>
            {
                w.CreateFolder("", "F");
                w.CreateScript("F", "A");
                w.CreateScript("F", "B");
                w.CreateScript("", "Outside");
                s.WriteSaved("F/A.dscript", "import F.B\n");
                s.WriteSaved("F/B.dscript", "from F.A import main as entry\n");
                s.WriteSaved("Outside.dscript", "from F.B import helper as h\n# import F.A\n");

                int savedNotifications = 0;
                w.SavedScriptsChanged += () => savedNotifications++;
                string renamed = w.RenameFolder("F", "NewF");
                t.Equal("NewF", renamed, "folder renamed");
                t.Contains("import NewF.B", s.ReadSaved("NewF/A.dscript"), "internal import");
                t.Contains("from NewF.A import main as entry", s.ReadSaved("NewF/B.dscript"),
                    "circular import and alias");
                t.Contains("from NewF.B import helper as h", s.ReadSaved("Outside.dscript"),
                    "external importer updated");
                t.Contains("# import F.A", s.ReadSaved("Outside.dscript"), "comments unchanged");
                t.Equal(1, savedNotifications, "single persisted-source notification");
                t.Check(!s.DirectoryExists("F"), "old folder removed");
            }));

            Add("DuplicateNestedFolders",
                "Deeply nested scripts keep their relative folders and import the duplicated modules", t =>
                WithWorkspace((w, s) =>
            {
                w.CreateFolder("", "Top");
                w.CreateFolder("Top", "F");
                w.CreateFolder("Top/F", "Inner");
                w.CreateScript("Top/F/Inner", "A");
                w.CreateScript("Top/F/Inner", "B");
                s.WriteSaved("Top/F/Inner/A.dscript",
                    "import Top.F.Inner.B as partner\n");
                s.WriteSaved("Top/F/Inner/B.dscript",
                    "from Top.F.Inner.A import entry\n");
                w.DuplicateFolder("Top/F", "F_copy");
                t.Check(s.ScriptExists("Top/F_copy/Inner/A.dscript"), "nested tree recreated");
                t.Contains("Top.F_copy.Inner.B", s.ReadSaved("Top/F_copy/Inner/A.dscript"),
                    "nested import copied");
                t.Contains("Top.F_copy.Inner.A", s.ReadSaved("Top/F_copy/Inner/B.dscript"),
                    "nested circular import copied");
                t.Contains("Top.F.Inner.B", s.ReadSaved("Top/F/Inner/A.dscript"),
                    "source nested import unchanged");
            }));

            Add("RenameDirtyFilesPreservesUnsavedEdits",
                "Both saved sources and dirty working copies receive the import rename independently", t =>
                WithWorkspace((w, s) =>
            {
                w.CreateFolder("", "F");
                w.CreateScript("F", "A");
                w.CreateScript("", "Caller");
                s.WriteSaved("F/A.dscript", "def run():\n    pass\n");
                s.WriteSaved("Caller.dscript", "import F.A\n# saved\n");
                w.Open("Caller.dscript");
                w.UpdateActiveWorkingContent("import F.A\n# saved\n# pending\n");
                string renamed = w.RenameFolder("F", "F_new");
                t.Equal("F_new", renamed, "renamed folder");
                t.Equal("import F_new.A\n# saved\n", s.ReadSaved("Caller.dscript"),
                    "saved copy only has import refactor");
                t.Equal("import F_new.A\n# saved\n# pending\n", s.ReadBackup("Caller.dscript"),
                    "working copy retains unrelated edits");
                t.Check(w.Active.IsDirty, "active remains unsaved");
                t.Contains("# pending", w.Active.WorkingContent, "active buffer updated");
                t.Check(!w.Active.SavedContent.Contains("pending"), "active saved base unchanged");
            }));

            Add("RenameDirtyInsideRemapsActive",
                "An edited script inside a renamed folder keeps its path, changes and dirty state", t =>
                WithWorkspace((w, s) =>
            {
                w.CreateFolder("", "A");
                w.CreateScript("A", "main");
                w.CreateScript("A", "helper");
                s.WriteSaved("A/main.dscript", "import A.helper\n");
                w.Open("A/main.dscript");
                w.UpdateActiveWorkingContent("import A.helper\n# unsaved\n");
                w.RenameFolder("A", "A2");
                t.Equal("A2/main.dscript", w.Active.RelativePath, "active path remapped");
                t.Equal("import A2.helper\n", s.ReadSaved("A2/main.dscript"), "saved script changed");
                t.Contains("# unsaved", w.Active.WorkingContent, "pending changes remain");
                t.Check(w.Active.IsDirty, "active stays dirty");
                t.Contains("import A2.helper", s.ReadBackup("A2/main.dscript"),
                    "backup moved and rewritten");
            }));

            Add("NestedFolderAndPrefixBoundary",
                "Only the exact nested directory prefix is changed, not similarly named modules", t =>
            {
                const string code =
                    "import Top.F.A\nimport Top.F2.A\nfrom Top.F.Inner.B import f\n";
                string updated = DScriptFolderImportRefactor.Rewrite(code, "Top/F", "Top/F_copy");
                t.Contains("import Top.F_copy.A", updated, "nested path");
                t.Contains("import Top.F2.A", updated, "adjacent folder unchanged");
                t.Contains("from Top.F_copy.Inner.B import f", updated, "deep import");
            });

            Add("PreviewAndRejectDuplicateName",
                "Preview is read-only and invalid folder operations leave originals untouched", t =>
                WithWorkspace((w, s) =>
            {
                w.CreateFolder("", "A");
                w.CreateScript("A", "b");
                w.CreateScript("", "external");
                s.WriteSaved("external.dscript", "import A.b\n");
                var preview = w.GetFolderRenameAffectedScripts("A", "A_copy");
                t.Check(preview.Contains("external.dscript"), "external importer reported");
                t.Equal("import A.b\n", s.ReadSaved("external.dscript"), "preview doesn't write");
                w.CreateFolder("", "A_copy");
                bool rejected = false;
                try { w.DuplicateFolder("A", "A_copy"); }
                catch (Exception) { rejected = true; }
                t.Check(rejected, "duplicate folder name rejected");
                t.Equal("import A.b\n", s.ReadSaved("external.dscript"),
                    "failed operation leaves import unchanged");
            }));

            Add("UnrelatedSyntaxErrorDoesNotBlock",
                "A broken unrelated statement does not prevent refactoring valid imports", t =>
            {
                const string code = "import F.good\ndef broken():\n    x = @\nfrom F.other import value\n";
                string result = DScriptFolderImportRefactor.Rewrite(code, "F", "F2");
                t.Contains("import F2.good", result, "first import rewritten");
                t.Contains("from F2.other import value", result, "recovered import rewritten");
                t.Contains("x = @", result, "unrelated syntax error retained");
            });
        }
    }
}
