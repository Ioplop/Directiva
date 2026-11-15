using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DOrchestration;
using Directiva.Examples;
using Directiva.Settings;
using UnityEngine;

namespace Directiva.EditorTests.CodeUITests
{
    internal static class ExampleCodeTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> check) =>
                tests.Add(new DirectivaTestCase("CodeUI", "ExampleCode", id, description, check));

            Add("SystemLanguageDefaults",
                "First-run locale is es for all Spanish variants and en otherwise", t =>
            {
                t.Equal("en", new GameSettings().general.language, "neutral default is English");
                t.Equal("es", SettingsManager.PreferredLanguage("es", SystemLanguage.English), "es");
                t.Equal("es", SettingsManager.PreferredLanguage("es-CL", SystemLanguage.English), "Chile");
                t.Equal("es", SettingsManager.PreferredLanguage("es-ES", SystemLanguage.English), "Spain");
                t.Equal("es", SettingsManager.PreferredLanguage("es-MX", SystemLanguage.English), "Mexico");
                t.Equal("es", SettingsManager.PreferredLanguage("en-US", SystemLanguage.Spanish), "Unity language fallback");
                t.Equal("en", SettingsManager.PreferredLanguage("pt-BR", SystemLanguage.Portuguese), "Portuguese");
                t.Equal("en", SettingsManager.PreferredLanguage("de-DE", SystemLanguage.German), "German");
                t.Equal("en", SettingsManager.PreferredLanguage("", SystemLanguage.English), "unknown English");
            });

            Add("AllLocalizedScriptsPackaged",
                "Resources contains equal script paths under es/en, including nested directories", t =>
            {
                var english = ExampleCodeManager.LoadPackaged("en");
                var spanish = ExampleCodeManager.LoadPackaged("es");
                t.Check(english.Count >= 8, "at least eight example files were imported");
                t.Equal(english.Count, spanish.Count, "locales contain same count");
                for (int i = 0; i < english.Count; i++)
                {
                    t.Equal(english[i].RelativePath, spanish[i].RelativePath,
                        "identical path " + i);
                    t.Check(english[i].RelativePath.StartsWith("Examples/", StringComparison.Ordinal),
                        "all examples use installed root");
                    t.Check(english[i].RelativePath.EndsWith(".dscript", StringComparison.Ordinal),
                        "only dscript assets installed");
                    t.Equal(StripComments(english[i].Contents), StripComments(spanish[i].Contents),
                        "only comments translated: " + english[i].RelativePath);
                }
            });

            Add("LevelScriptsStillTextAssets",
                "Resources/Levels stays compatible with the existing TextAsset importer", t =>
            {
                var script = Resources.Load<TextAsset>("Levels/Level01/lvl01");
                t.Check(script != null, "enemy level script remains a TextAsset");
                t.Check(script.text.Contains("def "), "enemy script content is intact");
            });

            Add("EveryExampleCompiles",
                "Every packaged example compiles independently, except its documented import helper", t =>
            {
                foreach (string language in new[] { "es", "en" })
                {
                    var examples = ExampleCodeManager.LoadPackaged(language);
                    var modules = examples.ToDictionary(
                        f => f.RelativePath.Substring(0, f.RelativePath.Length - 8).Replace('/', '.'),
                        f => f.Contents, StringComparer.Ordinal);
                    var provider = new InMemoryDScriptSourceProvider(modules);
                    foreach (string module in modules.Keys.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        var result = DScriptCompiler.Compile(module, provider, SimulationDScriptProfile.Options);
                        t.Check(result.Success, language + " " + module + ": " +
                            string.Join("; ", result.Diagnostics));
                    }
                }
            });

            Add("FirstInstallAndNoRepeatedWrites",
                "The first boot installs examples; later starts do not change the folder", t =>
            {
                WithTestFolders((root, state) =>
                {
                    var files = TestSources("en");
                    t.Equal(ExampleInstallResult.Installed,
                        ExampleCodeInstaller.Ensure(root, state, "en", files), "first installation");
                    string installed = Path.Combine(root, "Examples", "Basics", "Hello.dscript");
                    t.Equal(files[0].Contents, File.ReadAllText(installed), "content written");
                    File.AppendAllText(installed, "\n# Player note\n");
                    t.Equal(ExampleInstallResult.Unchanged,
                        ExampleCodeInstaller.Ensure(root, state, "en", files), "no repeated install");
                    t.Check(File.ReadAllText(installed).Contains("Player note"), "player note retained");
                });
            });

            Add("UnchangedLanguageSwitch",
                "Switching es/en replaces untouched comments while preserving module paths", t =>
            {
                WithTestFolders((root, state) =>
                {
                    var en = TestSources("en");
                    var es = TestSources("es");
                    t.Equal(ExampleInstallResult.Installed,
                        ExampleCodeInstaller.Ensure(root, state, "en", en), "initial");
                    t.Equal(ExampleInstallResult.Updated,
                        ExampleCodeInstaller.Ensure(root, state, "es", es), "replace untouched");
                    t.Equal(es[0].Contents,
                        File.ReadAllText(Path.Combine(root, "Examples", "Basics", "Hello.dscript")),
                        "Spanish comment installed");
                    t.Equal(ExampleInstallResult.Updated,
                        ExampleCodeInstaller.Ensure(root, state, "en", en), "reverse replacement");
                });
            });

            Add("UserChangesNeverOverwrittenOnLanguageSwitch",
                "Edited scripts, deleted scripts, extra folders or backups prevent automatic replacement", t =>
            {
                WithTestFolders((root, state) =>
                {
                    var en = TestSources("en");
                    var es = TestSources("es");
                    ExampleCodeInstaller.Ensure(root, state, "en", en);
                    string path = Path.Combine(root, "Examples", "Basics", "Hello.dscript");
                    File.WriteAllText(path, "# My own program\ndef main():\n    pass\n");
                    t.Equal(ExampleInstallResult.ModifiedExamplesPreserved,
                        ExampleCodeInstaller.Ensure(root, state, "es", es), "modified file preserved");
                    t.Check(File.ReadAllText(path).Contains("My own program"), "edit survives");
                    File.WriteAllText(path, en[0].Contents);
                    File.WriteAllText(path + ".back", "unsaved changes");
                    t.Equal(ExampleInstallResult.ModifiedExamplesPreserved,
                        ExampleCodeInstaller.Ensure(root, state, "es", es), "backup preserves unsaved work");
                    File.Delete(path + ".back");
                    Directory.CreateDirectory(Path.Combine(root, "Examples", "CustomFolder"));
                    t.Equal(ExampleInstallResult.ModifiedExamplesPreserved,
                        ExampleCodeInstaller.Ensure(root, state, "es", es), "extra directory preserved");
                });
            });

            Add("EditorSidecarsAreNotSourceEdits",
                "Generated DIL and editor metadata do not block translation; metadata survives", t =>
            {
                WithTestFolders((root, state) =>
                {
                    ExampleCodeInstaller.Ensure(root, state, "en", TestSources("en"));
                    string script = Path.Combine(root, "Examples", "Basics", "Hello.dscript");
                    string editorMetadata = script + ".editor";
                    string generatedDil = script.Substring(0, script.Length - 8) + ".dil";
                    File.WriteAllText(editorMetadata, "{\"entryFunction\":\"main\"}");
                    File.WriteAllText(generatedDil,
                        Directiva.CodeUI.GeneratedDILCache.Marker + "\nFUNCTION main LOCALS 0\n");
                    t.Equal(ExampleInstallResult.Updated,
                        ExampleCodeInstaller.Ensure(root, state, "es", TestSources("es")),
                        "generated sidecars do not make the folder edited");
                    t.Check(File.Exists(editorMetadata), "entry function metadata retained");
                    t.Check(!File.Exists(generatedDil), "DIL is recompiled when needed");
                });
            });

            Add("ManualRegenerationKeepsBackups",
                "User-requested regeneration restores originals but backs up changed scripts", t =>
            {
                WithTestFolders((root, state) =>
                {
                    var en = TestSources("en");
                    ExampleCodeInstaller.Ensure(root, state, "en", en);
                    string original = Path.Combine(root, "Examples", "Basics", "Hello.dscript");
                    File.WriteAllText(original, "modified");
                    t.Equal(ExampleInstallResult.Installed,
                        ExampleCodeInstaller.Regenerate(root, state, "en", en, out string backup),
                        "regenerated");
                    t.Check(!string.IsNullOrEmpty(backup), "retained backup path");
                    t.Equal("modified", File.ReadAllText(Path.Combine(backup, "Basics", "Hello.dscript")),
                        "old program preserved");
                    t.Equal(en[0].Contents, File.ReadAllText(original), "original reinstalled");
                });
            });

            Add("MissingAndUnknownNeverOverwritten",
                "Removed examples and pre-existing custom Examples folders are respected", t =>
            {
                WithTestFolders((root, state) =>
                {
                    var en = TestSources("en");
                    Directory.CreateDirectory(Path.Combine(root, "Examples"));
                    File.WriteAllText(Path.Combine(root, "Examples", "mine.dscript"), "custom");
                    t.Equal(ExampleInstallResult.ExistingFolderPreserved,
                        ExampleCodeInstaller.Ensure(root, state, "en", en), "unmanaged folder");
                    t.Check(File.Exists(Path.Combine(root, "Examples", "mine.dscript")), "still there");
                    Directory.Delete(Path.Combine(root, "Examples"), true);
                    ExampleCodeInstaller.Ensure(root, state, "en", en);
                    Directory.Delete(Path.Combine(root, "Examples"), true);
                    t.Equal(ExampleInstallResult.MissingExamplesPreserved,
                        ExampleCodeInstaller.Ensure(root, state, "es", TestSources("es")),
                        "intentional deletion does not force reappearance");
                });
            });

            Add("InstallerRejectsUnexpectedPaths",
                "The installer rejects traversal, foreign extensions and duplicate file entries", t =>
            {
                WithTestFolders((root, state) =>
                {
                    t.Equal(ExampleInstallResult.NoPackagedExamples,
                        ExampleCodeInstaller.Ensure(root, state, "en",
                            new[] { new ExampleCodeFile("Examples/../unsafe.dscript", "oops") }), "traversal");
                    t.Equal(ExampleInstallResult.NoPackagedExamples,
                        ExampleCodeInstaller.Ensure(root, state, "en",
                            new[] { new ExampleCodeFile("Examples/Basics/file.txt", "oops") }), "non-dscript");
                    t.Equal(ExampleInstallResult.NoPackagedExamples,
                        ExampleCodeInstaller.Ensure(root, state, "en",
                            new[] { new ExampleCodeFile("Examples/Basics/Hello.dscript", "a"),
                                    new ExampleCodeFile("Examples/Basics/hello.dscript", "b") }), "duplicate");
                    t.Check(!Directory.Exists(Path.Combine(root, "Examples")), "no unsafe directory created");
                });
            });

            Add("MenuExampleLocalization",
                "Options labels, warnings and actions are localized in English and Spanish", t =>
            {
                var es = new Directiva.CodeUI.LocalizationService("es");
                string spanish = es.Get("MainMenu", "example_scripts_question");
                t.Check(!spanish.Contains("missing:"), "Spanish text exists");
                es.SetLocale("en");
                string english = es.Get("MainMenu", "example_scripts_question");
                t.Check(!english.Contains("missing:"), "English text exists");
                t.Check(spanish != english, "language changes dialog");
            });
        }

        private static List<ExampleCodeFile> TestSources(string language) =>
            new List<ExampleCodeFile>
            {
                new ExampleCodeFile("Examples/Basics/Hello.dscript", language + " example"),
                new ExampleCodeFile("Examples/Modules/Nested/Other.dscript", "def helper():\n    pass\n")
            };

        private static string StripComments(string source) =>
            string.Join("\n", source.Replace("\r\n", "\n").Split('\n')
                .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal) &&
                               !string.IsNullOrWhiteSpace(line)));

        private static void WithTestFolders(Action<string, string> check)
        {
            string temp = Path.Combine(Path.GetTempPath(), "DirectivaExampleTests_" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(temp, "DirectivaScripts");
            string state = Path.Combine(temp, "ExampleCodeState.json");
            try { check(root, state); }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
    }
}
