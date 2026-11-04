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

        private sealed class RefactoringFileUpdate
        {
            public string Path;
            public string OldSaved;
            public string OldBackup;
            public bool HadBackup;
            public string NewSaved;
            public string NewWorking;
        }

        /// <summary>
        /// Apply a validated semantic rename to working copies and, for functions/shared,
        /// to their saved counterparts. A saved copy is independently refactored (never
        /// replaced with the working copy), so Revert discards unrelated pending edits
        /// without undoing the rename. A clean document becomes saved immediately.
        /// Local renames remain ordinary unsaved editor edits.
        /// </summary>
        public bool TryApplyRefactoring(DScriptSymbolRename.Plan workingPlan,
            DScriptSymbolRename.Plan savedPlan, Func<string, int, bool> applyActive,
            out string error)
        {
            error = null;
            if (workingPlan == null || applyActive == null || Active == null)
            { error = "rename_stale_source"; return false; }
            var changes = workingPlan.Changes;
            if (changes.Count == 0) return true;
            if (!changes.ContainsKey(Active.RelativePath))
            { error = "rename_stale_source"; return false; }
            if (savedPlan != null &&
                (savedPlan.Kind != workingPlan.Kind ||
                 savedPlan.OldName != workingPlan.OldName ||
                 savedPlan.NewName != workingPlan.NewName ||
                 savedPlan.OwnerModule != workingPlan.OwnerModule))
            { error = "rename_stale_source"; return false; }

            // No safe saved plan? Keep the entire rename in working copies,
            // rather than partially saving a project with inconsistent imports.
            bool persist = savedPlan != null &&
                (workingPlan.Kind == "function" || workingPlan.Kind == "shared");
            var savedChanges = persist ? savedPlan?.Changes : null;
            var savedOriginals = persist ? savedPlan?.Originals : null;
            var paths = changes.Keys.Union(savedChanges?.Keys ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var updates = new List<RefactoringFileUpdate>();
            try
            {
                // Stage the entire batch and check both snapshots before any write.
                foreach (string path in paths)
                {
                    if (!_storage.ScriptExists(path))
                    { error = "rename_stale_source"; return false; }
                    string oldWorking = GetWorkingContent(path);
                    string oldSaved = _storage.ReadSaved(path);
                    if (changes.TryGetValue(path, out string updatedWorking) &&
                        (!workingPlan.Originals.TryGetValue(path, out string expectedWorking) ||
                         !string.Equals(NormalizeNewlines(oldWorking), expectedWorking,
                            StringComparison.Ordinal)))
                    { error = "rename_stale_source"; return false; }
                    if (savedChanges != null && savedChanges.ContainsKey(path) &&
                        (savedOriginals == null || !savedOriginals.TryGetValue(path, out string expectedSaved) ||
                         !string.Equals(NormalizeNewlines(oldSaved), expectedSaved,
                             StringComparison.Ordinal)))
                    { error = "rename_stale_source"; return false; }

                    // Saved plans were independently bound and verified. If unavailable,
                    // keep the whole operation in working copies (the old behavior).
                    bool wasDirty = !string.Equals(NormalizeNewlines(oldWorking),
                        NormalizeNewlines(oldSaved), StringComparison.Ordinal);
                    string newSaved = oldSaved;
                    if (persist && savedChanges != null && savedChanges.TryGetValue(path, out var updatedSaved))
                        newSaved = updatedSaved;
                    else if (persist && !wasDirty && updatedWorking != null)
                        newSaved = updatedWorking;
                    bool hadBackup = _storage.BackupExists(path);
                    updates.Add(new RefactoringFileUpdate
                    {
                        Path = path,
                        OldSaved = oldSaved,
                        HadBackup = hadBackup,
                        OldBackup = hadBackup ? _storage.ReadBackup(path) : null,
                        NewSaved = newSaved,
                        NewWorking = updatedWorking ?? oldWorking
                    });
                }
            }
            catch
            {
                error = "rename_stale_source";
                return false;
            }

            string oldActiveSaved = Active.SavedContent;
            string oldActiveSavedHash = Active.SavedHash;
            string oldActiveWorking = Active.WorkingContent;
            string oldActiveWorkingHash = Active.WorkingHash;
            bool oldPreviousDirty = _previousDirty;
            var written = new List<RefactoringFileUpdate>();
            try
            {
                foreach (var update in updates)
                {
                    // Add before writing: an IO operation may fail after partially
                    // changing a file, and must therefore be included in rollback.
                    written.Add(update);
                    if (!string.Equals(update.OldSaved, update.NewSaved, StringComparison.Ordinal))
                        _storage.WriteSaved(update.Path, update.NewSaved);
                    if (string.Equals(NormalizeNewlines(update.NewSaved),
                        NormalizeNewlines(update.NewWorking), StringComparison.Ordinal))
                        _storage.DeleteBackup(update.Path);
                    else
                        _storage.WriteBackup(update.Path, update.NewWorking);
                }

                // Set the new base before dispatching the editor's TextChanged event,
                // so it compares its working buffer against the refactored saved copy.
                var activeUpdate = updates.First(u =>
                    string.Equals(u.Path, Active.RelativePath, StringComparison.OrdinalIgnoreCase));
                Active.SavedContent = activeUpdate.NewSaved;
                Active.SavedHash = Hash(activeUpdate.NewSaved);
                _previousDirty = Active.IsDirty;
                if (!applyActive(changes[Active.RelativePath], workingPlan.Caret))
                    throw new InvalidOperationException("Active editor rejected refactoring");
            }
            catch
            {
                // Best-effort rollback of both saved scripts and .back files.
                Active.SavedContent = oldActiveSaved;
                Active.SavedHash = oldActiveSavedHash;
                Active.WorkingContent = oldActiveWorking;
                Active.WorkingHash = oldActiveWorkingHash;
                _previousDirty = oldPreviousDirty;
                foreach (var update in written.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (!string.Equals(update.OldSaved, update.NewSaved, StringComparison.Ordinal))
                            _storage.WriteSaved(update.Path, update.OldSaved);
                        if (update.HadBackup) _storage.WriteBackup(update.Path, update.OldBackup);
                        else _storage.DeleteBackup(update.Path);
                    }
                    catch { /* Report the original failure rather than hiding it. */ }
                }
                error = "rename_apply_failed";
                return false;
            }

            _previousDirty = Active.IsDirty;
            foreach (var update in updates)
                ScriptStateChanged?.Invoke(update.Path);
            if (updates.Any(u => !string.Equals(u.OldSaved, u.NewSaved, StringComparison.Ordinal)))
                TreeChanged?.Invoke();
            return true;
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
