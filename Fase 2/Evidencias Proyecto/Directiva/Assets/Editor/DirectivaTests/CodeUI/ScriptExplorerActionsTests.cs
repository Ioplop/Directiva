using System;
using System.Collections.Generic;
using System.IO;
using Directiva.CodeUI;
using UnityEngine;

namespace Directiva.EditorTests.CodeUITests
{
    internal static class ScriptExplorerActionsTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "ExplorerActions", id, description, run));

            string SamplePath() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Directiva Scripts"));

            Add("ScriptNamesHideExtension", "Script labels omit .dscript without changing the stored path", t =>
            {
                t.Equal("main", ScriptDisplayNames.ForScript("main.dscript"), "root script");
                t.Equal("utils", ScriptDisplayNames.ForScript("lib/utils.dscript"), "nested script");
                t.Equal("MY_SCRIPT", ScriptDisplayNames.ForScript("lib/MY_SCRIPT.DSCRIPT"), "case-insensitive extension");
                t.Equal("file.v1", ScriptDisplayNames.ForScript("lib/file.v1.dscript"), "preserve other dots");
                t.Equal("other.txt", ScriptDisplayNames.ForScript("other.txt"), "do not strip unrelated extensions");
            });

            Add("FolderNamesStayIntact", "Folder labels preserve their full names, including dots", t =>
            {
                t.Equal("scripts.v2", ScriptDisplayNames.ForNode("lib/scripts.v2", true), "folder with a dot");
                t.Equal("scripts.dscript", ScriptDisplayNames.ForNode("lib/scripts.dscript", true), "folder named like an extension");
                t.Equal("scripts", ScriptDisplayNames.ForNode("lib/scripts.dscript", false), "script node");
            });

            Add("WindowsPlayerUsesShell", "Windows opens the directory with its configured shell", t =>
            {
                var info = ScriptsFolderOpener.CreateStartInfo(SamplePath(), RuntimePlatform.WindowsPlayer);
                t.Equal(SamplePath(), info.FileName, "Windows opens the actual folder");
                t.Check(info.UseShellExecute, "use the Windows system shell");
                t.Check(string.IsNullOrEmpty(info.Arguments), "no command-line injection or arguments");
            });

            Add("WindowsEditor", "Opening scripts in the Unity Windows Editor uses the same route", t =>
            {
                var info = ScriptsFolderOpener.CreateStartInfo(SamplePath(), RuntimePlatform.WindowsEditor);
                t.Equal(SamplePath(), info.FileName, "folder, not browser URL");
                t.Check(info.UseShellExecute, "uses system file association");
            });

            Add("LinuxPlayerUsesXdgOpen", "Linux launches default file manager without naming Nautilus or Nemo", t =>
            {
                var info = ScriptsFolderOpener.CreateStartInfo(SamplePath(), RuntimePlatform.LinuxPlayer);
                t.Equal("xdg-open", info.FileName, "freedesktop desktop opener");
                t.Check(!info.UseShellExecute, "does not invoke a command shell");
                t.Check(info.Arguments.StartsWith("\"file:", StringComparison.Ordinal),
                    "single quoted file URI argument");
                t.Check(info.Arguments.Contains("Directiva%20Scripts"), "spaces URI-escaped");
            });

            Add("LinuxEditor", "Unity's Linux Editor uses the same desktop opener", t =>
            {
                var info = ScriptsFolderOpener.CreateStartInfo(SamplePath(), RuntimePlatform.LinuxEditor);
                t.Equal("xdg-open", info.FileName, "xdg-open in Editor");
                t.Check(!info.UseShellExecute, "no shell string interpolation");
            });

            Add("LinuxSpecialCharacters", "Folder paths containing spaces and special characters remain one URI", t =>
            {
                // Test logic also runs inside the Windows Unity Editor, where a
                // quote is illegal in a path. Unix-specific quote test is conditional.
                var path = Path.Combine(Path.GetTempPath(), "Scripts with spaces & symbols");
                var info = ScriptsFolderOpener.CreateStartInfo(path, RuntimePlatform.LinuxPlayer);
                t.Check(info.Arguments.Contains("%20"), "spaces encoded in file URI");
                t.Check(info.Arguments.EndsWith("\"", StringComparison.Ordinal), "one complete argument");
                if (Path.DirectorySeparatorChar == '/')
                {
                    path = Path.Combine(Path.GetTempPath(), "Scripts with \"quotes\"");
                    info = ScriptsFolderOpener.CreateStartInfo(path, RuntimePlatform.LinuxPlayer);
                    t.Check(info.Arguments.Contains("%22"), "Unix quotes encoded in URI");
                }
            });

            Add("MissingPathRejected", "A missing script directory path is rejected", t =>
            {
                bool threw = false;
                try { ScriptsFolderOpener.CreateStartInfo("", RuntimePlatform.WindowsPlayer); }
                catch (ArgumentException) { threw = true; }
                t.Check(threw, "invalid path rejected");
            });

            Add("UnsupportedPlatform", "Only desktop OSes use the scripts folder launcher", t =>
            {
                bool threw = false;
                try { ScriptsFolderOpener.CreateStartInfo(SamplePath(), RuntimePlatform.Android); }
                catch (PlatformNotSupportedException) { threw = true; }
                t.Check(threw, "no invalid launch on mobile");
            });

            Add("NestedFolderCreation", "Context-targeted creation stays in the clicked folder", t =>
            {
                string root = Path.Combine(Path.GetTempPath(), "DirectivaExplorerActions_" + Guid.NewGuid().ToString("N"));
                try
                {
                    var storage = new FileSystemScriptStorage(root);
                    storage.EnsureRoot();
                    var workspace = new ScriptWorkspace(storage);
                    workspace.CreateFolder("", "outer");
                    workspace.CreateFolder("outer", "inner");
                    string script = workspace.CreateScript("outer/inner", "helper");
                    string folder = workspace.CreateFolder("outer/inner", "extra");
                    t.Equal("outer/inner/helper.dscript", script.Replace('\\', '/'), "script location");
                    t.Equal("outer/inner/extra", folder.Replace('\\', '/'), "folder location");
                    t.Check(!storage.ScriptExists("helper.dscript"), "no accidental root-level script");
                }
                finally
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            });
        }
    }
}
