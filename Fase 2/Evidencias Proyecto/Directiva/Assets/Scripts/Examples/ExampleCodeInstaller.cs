using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Directiva.Examples
{
    public sealed class ExampleCodeFile
    {
        public string RelativePath { get; }
        public string Contents { get; }

        public ExampleCodeFile(string relativePath, string contents)
        {
            RelativePath = relativePath;
            Contents = contents ?? string.Empty;
        }
    }

    public enum ExampleInstallResult
    {
        Installed,
        Updated,
        Unchanged,
        ModifiedExamplesPreserved,
        MissingExamplesPreserved,
        ExistingFolderPreserved,
        NoPackagedExamples,
        Failed
    }

    [Serializable]
    internal sealed class ExampleCodeFingerprint
    {
        public string relativePath;
        public string sha256;
    }

    [Serializable]
    internal sealed class ExampleCodeInstallState
    {
        public int version = 1;
        public string language;
        public List<ExampleCodeFingerprint> files = new List<ExampleCodeFingerprint>();
    }

    /// <summary>
    /// A non-destructive installer for immutable packaged examples. User edits,
    /// extra files, missing files, and editor backups all count as modifications.
    /// It never writes outside the chosen DirectivaScripts root.
    /// </summary>
    public static class ExampleCodeInstaller
    {
        public const string InstalledDirectoryName = "Examples";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

        public static ExampleInstallResult Ensure(string scriptsRoot, string statePath,
            string language, IReadOnlyList<ExampleCodeFile> sources)
        {
            try
            {
                if (!TryPrepare(scriptsRoot, language, sources, out string root,
                    out List<ExampleCodeFile> prepared)) return ExampleInstallResult.NoPackagedExamples;
                string target = Path.Combine(root, InstalledDirectoryName);
                ExampleCodeInstallState state = ReadState(statePath);

                if (state == null)
                {
                    // The player may already have a folder with this name. Never adopt or
                    // overwrite a directory whose provenance is unknown.
                    if (Directory.Exists(target) || File.Exists(target))
                        return ExampleInstallResult.ExistingFolderPreserved;
                    return Install(root, statePath, language, prepared, null, false, out _)
                        ? ExampleInstallResult.Installed : ExampleInstallResult.Failed;
                }

                if (!Directory.Exists(target))
                    return ExampleInstallResult.MissingExamplesPreserved;
                if (string.Equals(state.language, language, StringComparison.Ordinal))
                    return ExampleInstallResult.Unchanged;
                if (!IsUnmodified(target, state))
                    return ExampleInstallResult.ModifiedExamplesPreserved;

                return Install(root, statePath, language, prepared, state, false, out _)
                    ? ExampleInstallResult.Updated : ExampleInstallResult.Failed;
            }
            catch (Exception e) when (IsFileError(e))
            {
                Debug.LogWarning("[Directiva Examples] Installation failed: " + e.Message);
                return ExampleInstallResult.Failed;
            }
        }

        /// <summary>
        /// An explicit user action. Modified contents are moved to a uniquely named
        /// backup folder; they are NEVER silently deleted or overwritten.
        /// </summary>
        public static ExampleInstallResult Regenerate(string scriptsRoot, string statePath,
            string language, IReadOnlyList<ExampleCodeFile> sources, out string backupPath)
        {
            backupPath = null;
            try
            {
                if (!TryPrepare(scriptsRoot, language, sources, out string root,
                    out List<ExampleCodeFile> prepared)) return ExampleInstallResult.NoPackagedExamples;
                string target = Path.Combine(root, InstalledDirectoryName);
                ExampleCodeInstallState state = ReadState(statePath);
                bool modified = Directory.Exists(target) &&
                    (state == null || !IsUnmodified(target, state));
                if (File.Exists(target)) return ExampleInstallResult.ExistingFolderPreserved;
                return Install(root, statePath, language, prepared, state, modified, out backupPath)
                    ? ExampleInstallResult.Installed : ExampleInstallResult.Failed;
            }
            catch (Exception e) when (IsFileError(e))
            {
                Debug.LogWarning("[Directiva Examples] Regeneration failed: " + e.Message);
                return ExampleInstallResult.Failed;
            }
        }

        private static bool TryPrepare(string scriptsRoot, string language,
            IReadOnlyList<ExampleCodeFile> sources, out string root, out List<ExampleCodeFile> prepared)
        {
            root = Path.GetFullPath(scriptsRoot);
            prepared = new List<ExampleCodeFile>();
            if (sources == null || sources.Count == 0) return false;
            if (language != "en" && language != "es") return false;
            var used = new HashSet<string>(Paths);
            foreach (ExampleCodeFile file in sources)
            {
                if (file == null || !IsValidRelativePath(file.RelativePath) ||
                    !used.Add(file.RelativePath)) return false;
                prepared.Add(file);
            }
            prepared.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
            return prepared.Count > 0;
        }

        /// <summary>Restrict examples to valid DScript module path segments.</summary>
        private static bool IsValidRelativePath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Contains('\\') ||
                relative.Contains(':') || !relative.StartsWith(InstalledDirectoryName + "/",
                    StringComparison.Ordinal) ||
                !relative.EndsWith(".dscript", StringComparison.Ordinal)) return false;
            string modulePath = relative.Substring(0, relative.Length - ".dscript".Length);
            foreach (string segment in modulePath.Split('/'))
            {
                if (segment.Length == 0 || !(char.IsLetter(segment[0]) || segment[0] == '_'))
                    return false;
                foreach (char c in segment)
                    if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                        (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }

        private static bool Install(string root, string statePath, string language,
            IReadOnlyList<ExampleCodeFile> files, ExampleCodeInstallState oldState,
            bool keepBackup, out string retainedBackup)
        {
            retainedBackup = null;
            string target = Path.Combine(root, InstalledDirectoryName);
            if (Directory.Exists(root) && IsLink(root)) return false;
            Directory.CreateDirectory(root);
            string transactionId = Guid.NewGuid().ToString("N");
            string stage = Path.Combine(root, ".Examples_new_" + transactionId);
            string previous = Path.Combine(root, ".Examples_old_" + transactionId);
            bool previousMoved = false;
            bool newMoved = false;
            try
            {
                Directory.CreateDirectory(stage);
                var stamp = new ExampleCodeInstallState { language = language };
                foreach (ExampleCodeFile file in files)
                {
                    string relativeWithinExamples = file.RelativePath.Substring(InstalledDirectoryName.Length + 1);
                    string destination = Path.Combine(stage, relativeWithinExamples.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    byte[] bytes = Utf8.GetBytes(file.Contents);
                    File.WriteAllBytes(destination, bytes);
                    stamp.files.Add(new ExampleCodeFingerprint
                    {
                        relativePath = relativeWithinExamples,
                        sha256 = Hash(bytes)
                    });
                }

                // A locally selected entry function and editor history are not
                // source changes. Keep their sidecars during automatic localization.
                if (!keepBackup && oldState != null && Directory.Exists(target))
                    PreserveEditorMetadata(target, stage, oldState);

                if (Directory.Exists(target))
                {
                    if (IsLink(target)) return false;
                    Directory.Move(target, previous);
                    previousMoved = true;
                }
                else if (File.Exists(target)) return false;

                Directory.Move(stage, target);
                newMoved = true;
                SaveState(statePath, stamp);

                if (previousMoved)
                {
                    // The replacement and its state are committed. Failed cleanup
                    // must NOT roll back the directory without rolling back its state.
                    try
                    {
                        if (keepBackup)
                        {
                            string backup = UniqueBackupPath(root);
                            Directory.Move(previous, backup);
                            retainedBackup = backup;
                        }
                        else Directory.Delete(previous, true);
                    }
                    catch (Exception cleanupError) when (IsFileError(cleanupError))
                    {
                        retainedBackup = previous;
                        Debug.LogWarning("[Directiva Examples] Original examples retained at '" +
                            previous + "': " + cleanupError.Message);
                    }
                }
                return true;
            }
            catch (Exception e) when (IsFileError(e))
            {
                Debug.LogWarning("[Directiva Examples] Could not replace examples: " + e.Message);
                // If state couldn't be saved, restore the original tree.
                try
                {
                    if (newMoved && Directory.Exists(target)) Directory.Delete(target, true);
                    if (previousMoved && Directory.Exists(previous)) Directory.Move(previous, target);
                }
                catch (Exception restoreError) when (IsFileError(restoreError))
                {
                    Debug.LogError("[Directiva Examples] Original examples at '" +
                        previous + "' need manual recovery: " + restoreError.Message);
                }
                return false;
            }
            finally
            {
                if (Directory.Exists(stage))
                {
                    try { Directory.Delete(stage, true); }
                    catch (Exception cleanupError) when (IsFileError(cleanupError))
                    {
                        Debug.LogWarning("[Directiva Examples] Staging cleanup failed: " +
                            cleanupError.Message);
                    }
                }
            }
        }

        private static string UniqueBackupPath(string root)
        {
            string prefix = "Examples_backup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string candidate = Path.Combine(root, prefix);
            int suffix = 1;
            while (Directory.Exists(candidate) || File.Exists(candidate))
                candidate = Path.Combine(root, prefix + "_" + suffix++);
            return candidate;
        }

        private static bool IsUnmodified(string directory, ExampleCodeInstallState state)
        {
            if (state == null || state.files == null || IsLink(directory)) return false;
            var expected = new Dictionary<string, string>(Paths);
            var expectedDirectories = new HashSet<string>(Paths);
            foreach (ExampleCodeFingerprint entry in state.files)
            {
                if (entry == null || !IsValidRelativePath(InstalledDirectoryName + "/" + entry.relativePath) ||
                    !expected.TryAdd(entry.relativePath, entry.sha256)) return false;
                string current = Path.GetDirectoryName(entry.relativePath)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(current))
                {
                    expectedDirectories.Add(current);
                    current = Path.GetDirectoryName(current)?.Replace('\\', '/');
                }
            }
            foreach (string dir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                if (IsLink(dir) || !expectedDirectories.Remove(Relative(directory, dir))) return false;
            }
            if (expectedDirectories.Count != 0) return false;
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (IsLink(file)) return false;
                string relative = Relative(directory, file);
                if (expected.TryGetValue(relative, out string checksum))
                {
                    expected.Remove(relative);
                    if (!string.Equals(checksum, Hash(File.ReadAllBytes(file)), StringComparison.Ordinal))
                        return false;
                }
                else if (IsEditorSidecar(relative, state) || IsOwnedGeneratedDil(relative, file, state))
                {
                    // Editor metadata and owned DIL caches are generated by the game,
                    // not edits to the authoritative DScript source.
                }
                else return false;
            }
            return expected.Count == 0;
        }

        private static bool IsEditorSidecar(string relative, ExampleCodeInstallState state)
        {
            const string suffix = ".dscript.editor";
            return relative.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                state.files.Any(s => string.Equals(s.relativePath,
                    relative.Substring(0, relative.Length - ".editor".Length),
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOwnedGeneratedDil(string relative, string fullPath,
            ExampleCodeInstallState state)
        {
            if (!relative.EndsWith(".dil", StringComparison.OrdinalIgnoreCase)) return false;
            string scriptPath = relative.Substring(0, relative.Length - ".dil".Length) + ".dscript";
            if (!state.files.Any(s => string.Equals(s.relativePath, scriptPath,
                    StringComparison.OrdinalIgnoreCase))) return false;
            using (var stream = new StreamReader(fullPath))
                return string.Equals(stream.ReadLine(),
                    "// DIRECTIVA_GENERATED_DIL_V1 - source is .dscript; do not edit",
                    StringComparison.Ordinal);
        }

        private static void PreserveEditorMetadata(string oldDirectory, string stage,
            ExampleCodeInstallState state)
        {
            foreach (string file in Directory.GetFiles(oldDirectory, "*.dscript.editor",
                         SearchOption.AllDirectories))
            {
                string relative = Relative(oldDirectory, file);
                if (!IsEditorSidecar(relative, state)) continue;
                string destination = Path.Combine(stage,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination);
            }
        }

        private static string Relative(string parent, string path) =>
            path.Substring(parent.TrimEnd(Path.DirectorySeparatorChar).Length + 1)
                .Replace('\\', '/');

        private static bool IsLink(string path) =>
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        private static ExampleCodeInstallState ReadState(string statePath)
        {
            if (!File.Exists(statePath)) return null;
            try
            {
                var state = JsonUtility.FromJson<ExampleCodeInstallState>(File.ReadAllText(statePath));
                return state != null && state.version == 1 && state.files != null &&
                    (state.language == "es" || state.language == "en") ? state : null;
            }
            catch (Exception e) when (IsFileError(e) || e is ArgumentException)
            {
                return null;
            }
        }

        private static void SaveState(string statePath, ExampleCodeInstallState state)
        {
            string folder = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(state, true), Utf8);
                if (File.Exists(statePath))
                {
                    try { File.Replace(temp, statePath, null); }
                    catch (PlatformNotSupportedException) { File.Copy(temp, statePath, true); }
                    catch (IOException) { File.Copy(temp, statePath, true); }
                }
                else File.Move(temp, statePath);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static bool IsFileError(Exception e) =>
            e is IOException || e is UnauthorizedAccessException ||
            e is ArgumentException || e is NotSupportedException;
    }
}
