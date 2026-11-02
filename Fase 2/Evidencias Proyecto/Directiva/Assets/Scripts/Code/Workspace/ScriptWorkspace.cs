using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Mantiene un único script activo y working copies persistidas como .dscript.back.
    /// </summary>
    public sealed class ScriptWorkspace
    {
        public sealed class ActiveDocument
        {
            public string RelativePath { get; internal set; }
            public string SavedContent { get; internal set; }
            public string WorkingContent { get; internal set; }
            public string SavedHash { get; internal set; }
            public string WorkingHash { get; internal set; }

            public bool IsDirty => !string.Equals(SavedHash, WorkingHash, StringComparison.Ordinal);

            internal ActiveDocument(string path, string saved, string working)
            {
                RelativePath = Normalize(path);
                SavedContent = saved ?? string.Empty;
                WorkingContent = working ?? string.Empty;
                SavedHash = Hash(SavedContent);
                WorkingHash = Hash(WorkingContent);
            }
        }

        private readonly IScriptStorage _storage;
        private readonly Dictionary<string, IReadOnlyList<CodeDiagnostic>> _diagnostics =
            new Dictionary<string, IReadOnlyList<CodeDiagnostic>>(StringComparer.OrdinalIgnoreCase);

        private float _lastBackupTime;
        private bool _previousDirty;

        public float BackupIntervalSeconds { get; set; } = 5f;
        public ActiveDocument Active { get; private set; }

        public event Action TreeChanged;
        public event Action<ActiveDocument> ActiveChanged;
        public event Action<string> ScriptStateChanged;
        public event Action<string, string> PathChanged;

        public ScriptWorkspace(IScriptStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _storage.EnsureRoot();
        }

        public void Tick(float unscaledTime)
        {
            if (Active == null || !Active.IsDirty)
                return;

            if (unscaledTime - _lastBackupTime < BackupIntervalSeconds)
                return;

            FlushActiveBackup();
            _lastBackupTime = unscaledTime;
        }

        public void Open(string relativeScriptPath)
        {
            relativeScriptPath = Normalize(relativeScriptPath);

            if (Active != null &&
                string.Equals(Active.RelativePath, relativeScriptPath, StringComparison.OrdinalIgnoreCase))
                return;

            FlushActiveBackup();

            var saved = _storage.ReadSaved(relativeScriptPath);
            var working = _storage.BackupExists(relativeScriptPath)
                ? _storage.ReadBackup(relativeScriptPath)
                : saved;

            Active = new ActiveDocument(relativeScriptPath, saved, working);
            _previousDirty = Active.IsDirty;
            ActiveChanged?.Invoke(Active);
            ScriptStateChanged?.Invoke(relativeScriptPath);
        }

        public void CloseActive()
        {
            FlushActiveBackup();
            Active = null;
            ActiveChanged?.Invoke(null);
        }

        public void UpdateActiveWorkingContent(string content)
        {
            if (Active == null)
                return;

            Active.WorkingContent = content ?? string.Empty;
            Active.WorkingHash = Hash(Active.WorkingContent);

            if (Active.IsDirty)
            {
                // El archivo se escribe periódicamente. El estado visual cambia inmediatamente.
            }
            else
            {
                _storage.DeleteBackup(Active.RelativePath);
            }

            if (_previousDirty != Active.IsDirty)
            {
                _previousDirty = Active.IsDirty;
                ScriptStateChanged?.Invoke(Active.RelativePath);
            }
        }

        public void FlushActiveBackup()
        {
            if (Active == null)
                return;

            if (Active.IsDirty)
                _storage.WriteBackup(Active.RelativePath, Active.WorkingContent);
            else
                _storage.DeleteBackup(Active.RelativePath);
        }

        public bool IsDirty(string relativeScriptPath)
        {
            relativeScriptPath = Normalize(relativeScriptPath);

            if (Active != null &&
                string.Equals(Active.RelativePath, relativeScriptPath, StringComparison.OrdinalIgnoreCase))
            {
                return Active.IsDirty;
            }

            return _storage.BackupExists(relativeScriptPath);
        }

        public bool HasErrors(string relativeScriptPath)
        {
            relativeScriptPath = Normalize(relativeScriptPath);
            return _diagnostics.TryGetValue(relativeScriptPath, out var items) &&
                   items.Any(d => d.Severity == DiagnosticSeverity.Error);
        }

        public void SetDiagnostics(string relativeScriptPath, IReadOnlyList<CodeDiagnostic> diagnostics)
        {
            relativeScriptPath = Normalize(relativeScriptPath);
            _diagnostics[relativeScriptPath] = diagnostics ?? Array.Empty<CodeDiagnostic>();
            ScriptStateChanged?.Invoke(relativeScriptPath);
        }

        public string GetWorkingContent(string relativeScriptPath)
        {
            relativeScriptPath = Normalize(relativeScriptPath);

            if (Active != null &&
                string.Equals(Active.RelativePath, relativeScriptPath, StringComparison.OrdinalIgnoreCase))
            {
                return Active.WorkingContent;
            }

            if (_storage.BackupExists(relativeScriptPath))
                return _storage.ReadBackup(relativeScriptPath);

            return _storage.ReadSaved(relativeScriptPath);
        }

        /// <summary>
        /// Commit a previously validated multi-script rename to working copies only.
        /// Saved .dscript files are NEVER overwritten. Inactive scripts receive .back
        /// copies; the active buffer is applied last through the editor's undo mechanism.
        /// All sources are checked for intervening edits before changing anything.
        /// </summary>
        public bool TryApplyRefactoring(IReadOnlyDictionary<string, string> originals,
            IReadOnlyDictionary<string, string> changes, int caret,
            Func<string, int, bool> applyActive, out string error)
        {
            error = null;
            if (changes == null || changes.Count == 0) return true;
            if (originals == null || Active == null || applyActive == null ||
                !changes.ContainsKey(Active.RelativePath))
            { error = "rename_stale_source"; return false; }

            var paths = changes.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (string path in paths)
            {
                if (!originals.TryGetValue(path, out var expected) ||
                    !_storage.ScriptExists(path) ||
                    !string.Equals(NormalizeNewlines(GetWorkingContent(path)), expected,
                        StringComparison.Ordinal))
                { error = "rename_stale_source"; return false; }
            }

            var previousBackups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var withoutBackup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var modified = new List<string>();
            try
            {
                foreach (string path in paths)
                {
                    if (string.Equals(path, Active.RelativePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (_storage.BackupExists(path))
                        previousBackups.Add(path, _storage.ReadBackup(path));
                    else withoutBackup.Add(path);
                    modified.Add(path);
                    if (string.Equals(NormalizeNewlines(_storage.ReadSaved(path)),
                        changes[path], StringComparison.Ordinal))
                        _storage.DeleteBackup(path);
                    else
                        _storage.WriteBackup(path, changes[path]);
                }
                if (!applyActive(changes[Active.RelativePath], caret))
                { error = "rename_apply_failed"; RestoreBackups(); return false; }
            }
            catch
            {
                RestoreBackups();
                error = "rename_apply_failed";
                return false;
            }
            foreach (string path in modified)
                ScriptStateChanged?.Invoke(path);
            return true;

            void RestoreBackups()
            {
                // Best effort rollback if storage failed partway through the batch.
                foreach (string path in modified)
                {
                    try
                    {
                        if (withoutBackup.Contains(path)) _storage.DeleteBackup(path);
                        else if (previousBackups.TryGetValue(path, out var old))
                            _storage.WriteBackup(path, old);
                    }
                    catch { /* The original exception is already reported. */ }
                }
            }
        }

        private static string NormalizeNewlines(string text) =>
            (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        public void Save(string relativeScriptPath)
        {
            relativeScriptPath = Normalize(relativeScriptPath);
            var content = GetWorkingContent(relativeScriptPath);

            _storage.WriteSaved(relativeScriptPath, content);
            _storage.DeleteBackup(relativeScriptPath);

            if (Active != null &&
                string.Equals(Active.RelativePath, relativeScriptPath, StringComparison.OrdinalIgnoreCase))
            {
                Active.SavedContent = content;
                Active.WorkingContent = content;
                Active.SavedHash = Hash(content);
                Active.WorkingHash = Active.SavedHash;
                _previousDirty = false;
            }

            ScriptStateChanged?.Invoke(relativeScriptPath);
            TreeChanged?.Invoke();
        }

        public void Revert(string relativeScriptPath)
        {
            relativeScriptPath = Normalize(relativeScriptPath);
            _storage.DeleteBackup(relativeScriptPath);

            if (Active != null &&
                string.Equals(Active.RelativePath, relativeScriptPath, StringComparison.OrdinalIgnoreCase))
            {
                var saved = _storage.ReadSaved(relativeScriptPath);
                Active.SavedContent = saved;
                Active.WorkingContent = saved;
                Active.SavedHash = Hash(saved);
                Active.WorkingHash = Active.SavedHash;
                _previousDirty = false;
                ActiveChanged?.Invoke(Active);
            }

            ScriptStateChanged?.Invoke(relativeScriptPath);
            TreeChanged?.Invoke();
        }

        public string CreateScript(string folder, string baseName)
        {
            var path = _storage.CreateScript(folder, baseName);
            TreeChanged?.Invoke();
            return path;
        }

        public string CreateFolder(string folder, string name)
        {
            var path = _storage.CreateFolder(folder, name);
            TreeChanged?.Invoke();
            return path;
        }

        public void DeleteScript(string path)
        {
            path = Normalize(path);
            if (Active != null &&
                string.Equals(Active.RelativePath, path, StringComparison.OrdinalIgnoreCase))
            {
                Active = null;
                ActiveChanged?.Invoke(null);
            }

            _storage.DeleteScript(path);
            _diagnostics.Remove(path);
            TreeChanged?.Invoke();
        }

        public void DeleteFolder(string folder)
        {
            folder = Normalize(folder);

            if (Active != null && IsPathInside(Active.RelativePath, folder))
            {
                Active = null;
                ActiveChanged?.Invoke(null);
            }

            _storage.DeleteFolder(folder);

            foreach (var key in _diagnostics.Keys.Where(k => IsPathInside(k, folder)).ToArray())
                _diagnostics.Remove(key);

            TreeChanged?.Invoke();
        }

        public string RenameScript(string path, string newBaseName)
        {
            FlushActiveBackup();
            var oldPath = Normalize(path);
            var newPath = Normalize(_storage.RenameScript(oldPath, newBaseName));
            RemapPath(oldPath, newPath, false);
            return newPath;
        }

        public string RenameFolder(string folder, string newName)
        {
            FlushActiveBackup();
            var oldPath = Normalize(folder);
            var newPath = Normalize(_storage.RenameFolder(oldPath, newName));
            RemapPath(oldPath, newPath, true);
            return newPath;
        }

        public string MoveScript(string path, string targetFolder)
        {
            FlushActiveBackup();
            var oldPath = Normalize(path);
            var newPath = Normalize(_storage.MoveScript(oldPath, targetFolder));
            RemapPath(oldPath, newPath, false);
            return newPath;
        }

        public string MoveFolder(string folder, string targetFolder)
        {
            FlushActiveBackup();
            var oldPath = Normalize(folder);
            var newPath = Normalize(_storage.MoveFolder(oldPath, targetFolder));
            RemapPath(oldPath, newPath, true);
            return newPath;
        }

        public string DuplicateScript(string path, string newBaseName)
        {
            var content = GetWorkingContent(path);
            var newPath = _storage.DuplicateScript(path, newBaseName, content);
            TreeChanged?.Invoke();
            return newPath;
        }

        public string DuplicateFolder(string folder, string newName)
        {
            FlushActiveBackup();
            var newPath = _storage.DuplicateFolder(folder, newName);
            TreeChanged?.Invoke();
            return newPath;
        }

        private void RemapPath(string oldPath, string newPath, bool isFolder)
        {
            if (Active != null)
            {
                if (!isFolder &&
                    string.Equals(Active.RelativePath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    Active.RelativePath = newPath;
                }
                else if (isFolder && IsPathInside(Active.RelativePath, oldPath))
                {
                    Active.RelativePath = newPath + Active.RelativePath.Substring(oldPath.Length);
                }
            }

            if (!isFolder)
            {
                if (_diagnostics.TryGetValue(oldPath, out var d))
                {
                    _diagnostics.Remove(oldPath);
                    _diagnostics[newPath] = d;
                }
            }
            else
            {
                foreach (var key in _diagnostics.Keys.Where(k => IsPathInside(k, oldPath)).ToArray())
                {
                    var value = _diagnostics[key];
                    _diagnostics.Remove(key);
                    var mapped = newPath + key.Substring(oldPath.Length);
                    _diagnostics[mapped] = value;
                }
            }

            PathChanged?.Invoke(oldPath, newPath);
            TreeChanged?.Invoke();

            if (Active != null)
                ActiveChanged?.Invoke(Active);
        }

        private static bool IsPathInside(string candidate, string folder)
        {
            candidate = Normalize(candidate);
            folder = Normalize(folder).TrimEnd('/');
            return candidate.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string Hash(string content)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            var digest = sha.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);

            foreach (var value in digest)
                builder.Append(value.ToString("x2"));

            return builder.ToString();
        }

        private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');
    }
}
