using System;
using System.IO;

namespace DSCompilation.DScript.Modules
{
    /// <summary>Ordinal-case, path-segment-validated .dscript provider rooted in one directory.</summary>
    public sealed class FileSystemDScriptSourceProvider : IDScriptSourceProvider
    {
        public const string Extension = ".dscript";
        public string RootPath { get; }
        public FileSystemDScriptSourceProvider(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            RootPath = Path.GetFullPath(rootPath);
        }

        public bool TryReadModule(string moduleName, out string source)
        {
            if (!DScriptModuleLoader.IsValidName(moduleName))
                throw new ArgumentException("Invalid DScript module name: " + moduleName, nameof(moduleName));
            string[] parts = moduleName.Split('.');
            string current = RootPath;
            // FileSystemScriptStorage owns the script root; never follow a symlink/junction
            // inside that tree, since lexical path validation alone cannot confine it.
            if (!Directory.Exists(current)) { source = null; return false; }
            RejectReparsePoint(current);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!Directory.Exists(current)) { source = null; return false; }
                string next = FindExact(current, parts[i], true);
                if (next == null) { source = null; return false; }
                RejectReparsePoint(next);
                current = next;
            }
            if (!Directory.Exists(current)) { source = null; return false; }
            string file = FindExact(current, parts[parts.Length - 1] + Extension, false);
            if (file == null) { source = null; return false; }
            RejectReparsePoint(file);
            source = File.ReadAllText(file);
            return true;
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("DScript source links/junctions are not allowed: " + path);
        }

        private static string FindExact(string parent, string name, bool directory)
        {
            foreach (string entry in directory ? Directory.GetDirectories(parent) : Directory.GetFiles(parent))
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal)) return entry;
            return null;
        }
    }
}
