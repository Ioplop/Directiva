using System;
using System.IO;

namespace Directiva.CodeUI
{
    /// <summary>Names shown in the editor; storage and navigation keep their real paths.</summary>
    internal static class ScriptDisplayNames
    {
        internal static string ForNode(string path, bool isFolder) =>
            isFolder ? Path.GetFileName(path) : ForScript(path);

        internal static string ForScript(string path)
        {
            string fileName = Path.GetFileName(path);
            const string extension = FileSystemScriptStorage.ScriptExtension;
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - extension.Length)
                : fileName;
        }
    }
}
