using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Directiva.Settings;
using UnityEngine;

namespace Directiva.Examples
{
    /// <summary>
    /// Bootstraps starter DScript AFTER loading the preferred/saved language and
    /// BEFORE a scene's script explorer enumerates DirectivaScripts.
    /// </summary>
    public static class ExampleCodeManager
    {
        private static bool initialized;
        private static string installedForLanguage;

        public static string ScriptsRoot =>
            Path.Combine(Application.persistentDataPath, "DirectivaScripts");

        public static string ManifestPath =>
            Path.Combine(Application.persistentDataPath, "ExampleCodeState.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (initialized) SettingsManager.SettingsChanged -= OnSettingsChanged;
            initialized = false;
            installedForLanguage = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (initialized) return;
            // SettingsManager resolves OS language only for fresh settings files.
            string language = SettingsManager.Current.general.language;
            installedForLanguage = language;
            initialized = true;
            SettingsManager.SettingsChanged += OnSettingsChanged;
            EnsureFor(language);
        }

        private static void OnSettingsChanged(GameSettings settings)
        {
            string language = settings.general.language;
            if (string.Equals(language, installedForLanguage, StringComparison.Ordinal))
                return;
            installedForLanguage = language;
            EnsureFor(language);
        }

        private static void EnsureFor(string language)
        {
            ExampleInstallResult result = ExampleCodeInstaller.Ensure(
                ScriptsRoot, ManifestPath, language, ValidatedPackaged(language));
            if (result == ExampleInstallResult.NoPackagedExamples ||
                result == ExampleInstallResult.Failed)
                Debug.LogWarning(Directiva.Localization.DirectivaLocalization.Get(
                    "MainMenu", "example_scripts_failed") + " (" + result + ")");
        }

        public static ExampleInstallResult Regenerate(out string backupPath)
        {
            EnsureInitialized();
            return ExampleCodeInstaller.Regenerate(ScriptsRoot, ManifestPath,
                SettingsManager.Current.general.language,
                ValidatedPackaged(SettingsManager.Current.general.language), out backupPath);
        }

        private static List<ExampleCodeFile> ValidatedPackaged(string language)
        {
            // A localization may translate comments, never change module paths or
            // executable code. Reject incomplete packages rather than break imports.
            var english = LoadPackaged("en");
            var spanish = LoadPackaged("es");
            if (english.Count == 0 || spanish.Count != english.Count)
                return new List<ExampleCodeFile>();
            var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ExampleCodeFile entry in english)
            {
                if (byPath.ContainsKey(entry.RelativePath)) return new List<ExampleCodeFile>();
                byPath.Add(entry.RelativePath, WithoutComments(entry.Contents));
            }
            foreach (ExampleCodeFile entry in spanish)
            {
                if (!byPath.Remove(entry.RelativePath, out string englishCode) ||
                    !string.Equals(englishCode, WithoutComments(entry.Contents), StringComparison.Ordinal))
                    return new List<ExampleCodeFile>();
            }
            return byPath.Count == 0 ? (language == "es" ? spanish : english)
                : new List<ExampleCodeFile>();
        }

        private static string WithoutComments(string source)
        {
            var code = new List<string>();
            foreach (string line in (source ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                char quote = '\0';
                bool escaped = false;
                int length = line.Length;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (quote != '\0')
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == quote) quote = '\0';
                    }
                    else if (c == '\'' || c == '"') quote = c;
                    else if (c == '#') { length = i; break; }
                }
                string statement = line.Substring(0, length).TrimEnd();
                if (!string.IsNullOrWhiteSpace(statement)) code.Add(statement);
            }
            return string.Join("\n", code);
        }

        public static List<ExampleCodeFile> LoadPackaged(string language)
        {
            var scripts = Resources.LoadAll<ExampleCodeAsset>("ExampleCode/" + language);
            return scripts.Where(s => s != null)
                .Select(s => new ExampleCodeFile(s.RelativePath, s.Source))
                .OrderBy(s => s.RelativePath, StringComparer.Ordinal)
                .ToList();
        }
    }
}
