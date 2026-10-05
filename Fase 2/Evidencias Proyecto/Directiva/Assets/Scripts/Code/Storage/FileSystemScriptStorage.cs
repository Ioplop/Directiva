using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Implementación desktop basada en filesystem.
    /// Todas las rutas públicas son relativas a DirectivaScripts.
    /// </summary>
    public sealed class FileSystemScriptStorage : IScriptStorage
    {
        public const string ScriptExtension = ".dscript";
        public const string BackupSuffix = ".back";

        public string RootPath { get; }

        public FileSystemScriptStorage(string rootPath)
        {
            RootPath = Path.GetFullPath(rootPath ?? throw new ArgumentNullException(nameof(rootPath)));
        }

        public void EnsureRoot()
        {
            Directory.CreateDirectory(RootPath);
        }

        public IEnumerable<string> EnumerateDirectories(string relativeFolder = "")
        {
            var full = ToFullDirectory(relativeFolder);
            if (!Directory.Exists(full))
                yield break;

            foreach (var dir in Directory.GetDirectories(full).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                yield return ToRelative(dir);
        }

        public IEnumerable<string> EnumerateScripts(string relativeFolder = "")
        {
            var full = ToFullDirectory(relativeFolder);
            if (!Directory.Exists(full))
                yield break;

            foreach (var file in Directory.GetFiles(full, "*" + ScriptExtension)
                         .Where(p => !p.EndsWith(ScriptExtension + BackupSuffix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                yield return ToRelative(file);
            }
        }

        public bool DirectoryExists(string relativePath) => Directory.Exists(ToFullDirectory(relativePath));
        public bool ScriptExists(string relativeScriptPath) => File.Exists(ToFullScript(relativeScriptPath));
        public bool BackupExists(string relativeScriptPath) => File.Exists(ToFullBackup(relativeScriptPath));

        public string ReadSaved(string relativeScriptPath)
        {
            var path = ToFullScript(relativeScriptPath);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        public string ReadBackup(string relativeScriptPath)
        {
            var path = ToFullBackup(relativeScriptPath);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        public void WriteSaved(string relativeScriptPath, string content)
        {
            var path = ToFullScript(relativeScriptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content ?? string.Empty);
        }

        public void WriteBackup(string relativeScriptPath, string content)
        {
            var path = ToFullBackup(relativeScriptPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content ?? string.Empty);
        }

        public void DeleteBackup(string relativeScriptPath)
        {
            var path = ToFullBackup(relativeScriptPath);
            if (File.Exists(path))
                File.Delete(path);
        }

        public string CreateScript(string relativeFolder, string baseName, string initialContent = "")
        {
            baseName = ValidateSimpleName(baseName);
            var folder = ToFullDirectory(relativeFolder);
            Directory.CreateDirectory(folder);

            var fileName = baseName.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase)
                ? baseName
                : baseName + ScriptExtension;

            var full = EnsureInsideRoot(Path.Combine(folder, fileName));
            if (File.Exists(full))
                throw new DirectivaLocalizedException("Storage", "script_already_exists", fileName);

            File.WriteAllText(full, initialContent ?? string.Empty);
            return ToRelative(full);
        }

        public string CreateFolder(string relativeFolder, string folderName)
        {
            folderName = ValidateSimpleName(folderName);
            var full = EnsureInsideRoot(Path.Combine(ToFullDirectory(relativeFolder), folderName));

            if (Directory.Exists(full))
                throw new DirectivaLocalizedException("Storage", "folder_already_exists", folderName);

            Directory.CreateDirectory(full);
            return ToRelative(full);
        }

        public void DeleteScript(string relativeScriptPath)
        {
            var script = ToFullScript(relativeScriptPath);
            var backup = ToFullBackup(relativeScriptPath);

            if (File.Exists(script))
                File.Delete(script);
            if (File.Exists(backup))
                File.Delete(backup);
        }

        public void DeleteFolder(string relativeFolderPath)
        {
            var folder = ToFullDirectory(relativeFolderPath);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        public string RenameScript(string relativeScriptPath, string newBaseName)
        {
            newBaseName = ValidateSimpleName(newBaseName);
            var oldFull = ToFullScript(relativeScriptPath);
            var oldBackup = ToFullBackup(relativeScriptPath);
            var directory = Path.GetDirectoryName(oldFull)!;

            var fileName = newBaseName.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase)
                ? newBaseName
                : newBaseName + ScriptExtension;

            var newFull = EnsureInsideRoot(Path.Combine(directory, fileName));
            var newBackup = newFull + BackupSuffix;

            EnsureTargetFree(newFull, newBackup);
            File.Move(oldFull, newFull);

            if (File.Exists(oldBackup))
                File.Move(oldBackup, newBackup);

            return ToRelative(newFull);
        }

        public string RenameFolder(string relativeFolderPath, string newName)
        {
            newName = ValidateSimpleName(newName);
            var oldFull = ToFullDirectory(relativeFolderPath);
            var parent = Path.GetDirectoryName(oldFull)!;
            var newFull = EnsureInsideRoot(Path.Combine(parent, newName));

            if (Directory.Exists(newFull))
                throw new IOException($"Folder already exists: {newName}");

            Directory.Move(oldFull, newFull);
            return ToRelative(newFull);
        }

        public string MoveScript(string relativeScriptPath, string targetFolder)
        {
            var oldFull = ToFullScript(relativeScriptPath);
            var oldBackup = ToFullBackup(relativeScriptPath);
            var targetDir = ToFullDirectory(targetFolder);
            Directory.CreateDirectory(targetDir);

            var newFull = EnsureInsideRoot(Path.Combine(targetDir, Path.GetFileName(oldFull)));
            var newBackup = newFull + BackupSuffix;

            if (PathsEqual(oldFull, newFull))
                return ToRelative(oldFull);

            EnsureTargetFree(newFull, newBackup);
            File.Move(oldFull, newFull);

            if (File.Exists(oldBackup))
                File.Move(oldBackup, newBackup);

            return ToRelative(newFull);
        }

        public string MoveFolder(string relativeFolderPath, string targetFolder)
        {
            var oldFull = ToFullDirectory(relativeFolderPath);
            var targetDir = ToFullDirectory(targetFolder);

            if (IsSameOrDescendant(targetDir, oldFull))
                throw new DirectivaLocalizedException("Storage", "folder_inside_itself");

            Directory.CreateDirectory(targetDir);
            var newFull = EnsureInsideRoot(Path.Combine(targetDir, Path.GetFileName(oldFull)));

            if (PathsEqual(oldFull, newFull))
                return ToRelative(oldFull);

            if (Directory.Exists(newFull))
                throw new DirectivaLocalizedException("Storage", "folder_already_exists", Path.GetFileName(newFull));

            Directory.Move(oldFull, newFull);
            return ToRelative(newFull);
        }

        public string DuplicateScript(string relativeScriptPath, string desiredBaseName, string contentToDuplicate)
        {
            desiredBaseName = ValidateSimpleName(desiredBaseName);
            var source = ToFullScript(relativeScriptPath);
            var folder = Path.GetDirectoryName(source)!;

            var fileName = desiredBaseName.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase)
                ? desiredBaseName
                : desiredBaseName + ScriptExtension;

            var target = EnsureInsideRoot(Path.Combine(folder, fileName));
            if (File.Exists(target))
                throw new DirectivaLocalizedException("Storage", "script_already_exists", fileName);

            File.WriteAllText(target, contentToDuplicate ?? string.Empty);
            return ToRelative(target);
        }

        public string DuplicateFolder(string relativeFolderPath, string desiredName)
        {
            desiredName = ValidateSimpleName(desiredName);
            var source = ToFullDirectory(relativeFolderPath);
            var parent = Path.GetDirectoryName(source)!;
            var target = EnsureInsideRoot(Path.Combine(parent, desiredName));

            if (Directory.Exists(target))
                throw new DirectivaLocalizedException("Storage", "folder_already_exists", desiredName);

            CopyFolderWorkingState(source, target);
            return ToRelative(target);
        }

        private void CopyFolderWorkingState(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (var dir in Directory.GetDirectories(source))
                CopyFolderWorkingState(dir, Path.Combine(target, Path.GetFileName(dir)));

            foreach (var script in Directory.GetFiles(source, "*" + ScriptExtension))
            {
                if (script.EndsWith(ScriptExtension + BackupSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var backup = script + BackupSuffix;
                var content = File.Exists(backup) ? File.ReadAllText(backup) : File.ReadAllText(script);
                File.WriteAllText(Path.Combine(target, Path.GetFileName(script)), content);
            }
        }

        private string ToFullScript(string relativePath)
        {
            var full = EnsureInsideRoot(Path.Combine(RootPath, NormalizeRelative(relativePath)));
            if (!full.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase))
                throw new DirectivaLocalizedException("Storage", "invalid_script_path", relativePath);
            return full;
        }

        private string ToFullBackup(string relativePath) => ToFullScript(relativePath) + BackupSuffix;

        private string ToFullDirectory(string relativePath)
        {
            var normalized = NormalizeRelative(relativePath);
            return EnsureInsideRoot(Path.Combine(RootPath, normalized));
        }

        private string ToRelative(string fullPath)
        {
            var relative = Path.GetRelativePath(RootPath, fullPath);
            return relative.Replace('\\', '/');
        }

        private string NormalizeRelative(string relative)
        {
            relative ??= string.Empty;
            return relative.Replace('/', Path.DirectorySeparatorChar)
                           .Replace('\\', Path.DirectorySeparatorChar)
                           .TrimStart(Path.DirectorySeparatorChar);
        }

        private string EnsureInsideRoot(string path)
        {
            var full = Path.GetFullPath(path);
            var rootWithSlash = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;

            if (!full.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase) &&
                !PathsEqual(full, RootPath))
            {
                throw new DirectivaLocalizedException("Storage", "path_outside_root");
            }

            return full;
        }

        private static string ValidateSimpleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DirectivaLocalizedException("Storage", "name_empty");

            name = name.Trim();

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains("/") ||
                name.Contains("\\") ||
                name == "." ||
                name == "..")
            {
                throw new DirectivaLocalizedException("Storage", "invalid_name", name);
            }

            return name;
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                          Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase);

        private static bool IsSameOrDescendant(string candidate, string ancestor)
        {
            candidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
            ancestor = Path.GetFullPath(ancestor).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;

            return candidate.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureTargetFree(string scriptPath, string backupPath)
        {
            if (File.Exists(scriptPath) || File.Exists(backupPath))
                throw new DirectivaLocalizedException("Storage", "target_already_exists", Path.GetFileName(scriptPath));
        }
    }
}
