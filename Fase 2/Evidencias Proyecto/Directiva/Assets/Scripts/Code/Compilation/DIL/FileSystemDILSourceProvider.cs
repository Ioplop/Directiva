#nullable enable
using System;
using System.IO;

namespace DSCompilation.DIL
{
    /// <summary>
    /// Reads .dil modules from a fixed root directory. A module such as "utils.vectors"
    /// resolves to "utils/vectors.dil" below that root.
    /// </summary>
    public sealed class FileSystemDILSourceProvider : IDILSourceProvider
    {
        public const string Extension = ".dil";

        public string RootPath { get; }

        public FileSystemDILSourceProvider(string rootPath)
        {
            if (rootPath == null)
                throw new ArgumentNullException(nameof(rootPath));

            RootPath = Path.GetFullPath(rootPath);
        }

        public bool TryReadModule(string moduleName, out string sourceText)
        {
            if (!DILParser.IsValidQualifiedName(moduleName))
                throw new ArgumentException($"Invalid DIL module name '{moduleName}'.", nameof(moduleName));

            if (!TryResolveExactPath(moduleName, out string fullPath))
            {
                sourceText = string.Empty;
                return false;
            }

            sourceText = File.ReadAllText(fullPath);
            return true;
        }

        /// <summary>
        /// Resolves every path segment with ordinal casing so DIL remains case-sensitive even on
        /// case-insensitive filesystems such as the default Windows configuration.
        /// </summary>
        private bool TryResolveExactPath(string moduleName, out string fullPath)
        {
            string[] segments = moduleName.Split('.');
            string current = RootPath;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!Directory.Exists(current))
                {
                    fullPath = string.Empty;
                    return false;
                }

                string? next = FindExactEntry(current, segments[i], directory: true);
                if (next == null)
                {
                    fullPath = string.Empty;
                    return false;
                }

                current = next;
            }

            if (!Directory.Exists(current))
            {
                fullPath = string.Empty;
                return false;
            }

            string fileName = segments[^1] + Extension;
            string? file = FindExactEntry(current, fileName, directory: false);
            if (file == null)
            {
                fullPath = string.Empty;
                return false;
            }

            fullPath = file;
            return true;
        }

        private static string? FindExactEntry(string directoryPath, string name, bool directory)
        {
            string[] entries = directory
                ? Directory.GetDirectories(directoryPath)
                : Directory.GetFiles(directoryPath);

            foreach (string entry in entries)
            {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }
    }
}
