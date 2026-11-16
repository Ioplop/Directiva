using System;
using System.Collections.Generic;
using System.IO;
using Directiva.CodeUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.EditorTests.CodeUITests
{
    internal static class ScriptExplorerActionsTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "ExplorerActions", id, description, run));

            string SamplePath() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Directiva Scripts"));

            Add("EmptyDocumentHintVisibility",
                "An unopened editor displays its hint; opening even an empty script hides it", t =>
            {
                var hint = new VisualElement();
                DirectivaCodeUIController.SetEmptyEditorOverlayState(hint, null);
                t.Equal(DisplayStyle.Flex, hint.style.display.value, "no selected script");
                DirectivaCodeUIController.SetEmptyEditorOverlayState(hint, "");
                t.Equal(DisplayStyle.Flex, hint.style.display.value, "blank path");
                DirectivaCodeUIController.SetEmptyEditorOverlayState(hint, "main.dscript");
                t.Equal(DisplayStyle.None, hint.style.display.value, "open empty file is still a file");
                DirectivaCodeUIController.SetEmptyEditorOverlayState(hint, null);
                t.Equal(DisplayStyle.Flex, hint.style.display.value, "hint returns when script is closed");
            });

            Add("EmptyDocumentHintLocalized", "The no-script hint is available in Spanish and English", t =>
            {
                var locale = new LocalizationService("es");
                string spanishTitle = locale.Get("ScriptExplorer", "no_script_open_title");
                string spanishMessage = locale.Get("ScriptExplorer", "no_script_open_message");
                t.Contains("script", spanishTitle, "Spanish title");
                t.Contains("explorador", spanishMessage, "Spanish guidance");
                locale.SetLocale("en");
                string englishTitle = locale.Get("ScriptExplorer", "no_script_open_title");
                string englishMessage = locale.Get("ScriptExplorer", "no_script_open_message");
                t.Contains("script", englishTitle, "English title");
                t.Contains("explorer", englishMessage, "English guidance");
                t.Check(spanishTitle != englishTitle, "title changes with selected language");
                t.Check(spanishMessage != englishMessage, "instruction changes with selected language");
            });

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

            Add("WindowsPlayerUsesNativeShell", "Windows uses the native desktop folder handler", t =>
            {
                var request = ScriptsFolderOpener.CreateOpenRequest(SamplePath(), RuntimePlatform.WindowsPlayer);
                t.Equal(SamplePath(), request.FolderPath, "opens the actual folder");
                t.Equal(ScriptsFolderOpenMode.WindowsShell, request.Mode, "Windows uses ShellExecute");
            });

            Add("WindowsEditorUsesNativeShell", "Windows Editor uses the same native opener", t =>
            {
                var request = ScriptsFolderOpener.CreateOpenRequest(SamplePath(), RuntimePlatform.WindowsEditor);
                t.Equal(ScriptsFolderOpenMode.WindowsShell, request.Mode, "Editor uses Windows shell");
            });

            Add("LinuxPlayerUsesDefaultManager", "Linux opens file URIs with the configured file manager", t =>
            {
                var request = ScriptsFolderOpener.CreateOpenRequest(SamplePath(), RuntimePlatform.LinuxPlayer);
                t.Equal(ScriptsFolderOpenMode.LinuxDefaultFileManager, request.Mode, "GIO default handler");
                t.Check(request.FileUri.StartsWith("file:", StringComparison.Ordinal), "local file URI");
                t.Contains("Directiva%20Scripts", request.FileUri, "spaces URI-escaped");
            });

            Add("LinuxEditorUsesDefaultManager", "Linux Editor uses the native desktop opener", t =>
            {
                var request = ScriptsFolderOpener.CreateOpenRequest(SamplePath(), RuntimePlatform.LinuxEditor);
                t.Equal(ScriptsFolderOpenMode.LinuxDefaultFileManager, request.Mode, "same GIO route");
            });

            Add("LinuxSpecialCharacters", "File URI encodes spaces and special characters", t =>
            {
                string path = Path.Combine(Path.GetTempPath(), "Scripts with spaces & symbols");
                var request = ScriptsFolderOpener.CreateOpenRequest(path, RuntimePlatform.LinuxPlayer);
                t.Contains("%20", request.FileUri, "spaces encoded in URI");
                t.Check(!request.FileUri.Contains("\""), "no unescaped quotes");
                if (Path.DirectorySeparatorChar == '/')
                {
                    path = Path.Combine(Path.GetTempPath(), "Scripts with \"quotes\"");
                    request = ScriptsFolderOpener.CreateOpenRequest(path, RuntimePlatform.LinuxPlayer);
                    t.Contains("%22", request.FileUri, "Unix quotes encoded in URI");
                }
            });

            Add("MissingPathRejected", "A missing script directory path is rejected", t =>
            {
                bool threw = false;
                try { ScriptsFolderOpener.CreateOpenRequest("", RuntimePlatform.WindowsPlayer); }
                catch (ArgumentException) { threw = true; }
                t.Check(threw, "invalid path rejected");
            });

            Add("UnsupportedPlatform", "Only supported desktop platforms have a native opener", t =>
            {
                bool threw = false;
                try { ScriptsFolderOpener.CreateOpenRequest(SamplePath(), RuntimePlatform.Android); }
                catch (PlatformNotSupportedException) { threw = true; }
                t.Check(threw, "mobile opener rejected");
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
