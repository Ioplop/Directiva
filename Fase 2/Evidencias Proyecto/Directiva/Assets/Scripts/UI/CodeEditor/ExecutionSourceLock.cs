using System;
using System.Collections.Generic;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Tracks compiled source files for one active editor execution, in memory only.
    /// The VM executes a compiled snapshot; locking prevents source edits from making
    /// its DEBUG_FILE / DEBUG_LINE / DEBUG_SPAN locations misleading.
    /// </summary>
    public sealed class ExecutionSourceLock
    {
        private readonly HashSet<string> _files =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasLockedFiles => _files.Count != 0;

        public void LockModules(IEnumerable<string> moduleNames)
        {
            _files.Clear();
            if (moduleNames == null)
                return;

            foreach (string module in moduleNames)
            {
                if (string.IsNullOrWhiteSpace(module))
                    continue;

                _files.Add(Normalize(module.Replace('.', '/') +
                    FileSystemScriptStorage.ScriptExtension));
            }
        }

        public void Clear() => _files.Clear();

        public bool IsFileLocked(string relativePath) =>
            _files.Contains(Normalize(relativePath));

        /// <summary>Includes folders containing locked scripts (for rename/delete/move).</summary>
        public bool ContainsLockedFile(string relativePath)
        {
            string path = Normalize(relativePath).TrimEnd('/');
            if (path.Length == 0)
                return HasLockedFiles;
            if (_files.Contains(path))
                return true;

            string prefix = path + "/";
            foreach (string file in _files)
                if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Normalize(string path) =>
            (path ?? string.Empty).Replace('\\', '/');
    }
}
