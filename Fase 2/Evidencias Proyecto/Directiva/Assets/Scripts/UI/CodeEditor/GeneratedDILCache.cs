using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Optional inspectable .dil siblings of successfully compiled .dscript modules.
    /// Never used as execution inputs, and never overwrites old hand-written DIL.
    /// Internal bootstrap DIL deliberately remains in memory.
    /// </summary>
    public static class GeneratedDILCache
    {
        public const string Marker = "// DIRECTIVA_GENERATED_DIL_V1 - source is .dscript; do not edit";

        public static IReadOnlyList<string> Write(DScriptCompiledProgram program, string rootPath)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));

            string root = Path.GetFullPath(rootPath);
            var warnings = new List<string>();
            var staged = new List<(string Temp, string Destination)>();
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(root))
                    throw new DirectoryNotFoundException("Script root not found: " + root);
                RejectLink(root);

                foreach (var module in program.DILModules.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (module.Key == program.BootstrapModule) continue;
                    if (!DScriptModuleLoader.IsValidName(module.Key))
                        throw new InvalidOperationException("Invalid generated DIL module name: " + module.Key);
                    string full = Path.GetFullPath(Path.Combine(root,
                        module.Key.Replace('.', Path.DirectorySeparatorChar) + ".dil"));
                    string relative = Path.GetRelativePath(root, full);
                    if (Path.IsPathRooted(relative) || relative == ".." ||
                        relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("Generated DIL escapes the scripts root.");
                    if (!destinations.Add(full))
                        throw new InvalidOperationException("Two DIL modules use the same filesystem path.");

                    // The original .dscript is already on disk, hence the parent directory exists.
                    // Reject links in every component to avoid writing outside the scripts root.
                    string parent = Path.GetDirectoryName(full);
                    var segments = Path.GetRelativePath(root, parent)
                        .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                            StringSplitOptions.RemoveEmptyEntries);
                    string current = root;
                    foreach (string segment in segments)
                    {
                        if (segment == ".") continue;
                        current = Path.Combine(current, segment);
                        if (!Directory.Exists(current))
                            throw new DirectoryNotFoundException("Module directory not found: " + current);
                        RejectLink(current);
                    }

                    if (File.Exists(full))
                    {
                        RejectLink(full);
                        using (var reader = new StreamReader(full))
                        {
                            if (!string.Equals(reader.ReadLine(), Marker, StringComparison.Ordinal))
                            {
                                warnings.Add("No se reemplazó '" + relative.Replace('\\', '/') +
                                    "': contiene DIL que no fue generado por Directiva.");
                                continue;
                            }
                        }
                    }

                    string temp = full + ".tmp." + Guid.NewGuid().ToString("N");
                    File.WriteAllText(temp, Marker + "\n" + module.Value, new UTF8Encoding(false));
                    staged.Add((temp, full));
                }

                // Staging completes before any generated sibling is replaced.
                foreach (var item in staged)
                {
                    if (File.Exists(item.Destination))
                        File.Replace(item.Temp, item.Destination, null);
                    else
                        File.Move(item.Temp, item.Destination);
                }
            }
            finally
            {
                foreach (var item in staged)
                    if (File.Exists(item.Temp)) File.Delete(item.Temp);
            }
            return warnings.AsReadOnly();
        }

        /// <summary>
        /// Remove the cache of a deleted, renamed, or moved DScript module, but only if
        /// this cache was produced by Directiva. Legacy/manual .dil is never touched.
        /// </summary>
        public static void DeleteOwnedSibling(string scriptsRoot, string relativeScriptPath)
        {
            if (scriptsRoot == null) throw new ArgumentNullException(nameof(scriptsRoot));
            if (relativeScriptPath == null) throw new ArgumentNullException(nameof(relativeScriptPath));
            string root = Path.GetFullPath(scriptsRoot);
            if (!relativeScriptPath.EndsWith(FileSystemScriptStorage.ScriptExtension,
                StringComparison.OrdinalIgnoreCase))
                return;

            string relativeDIL = relativeScriptPath.Substring(0,
                relativeScriptPath.Length - FileSystemScriptStorage.ScriptExtension.Length) + ".dil";
            string path = Path.GetFullPath(Path.Combine(root,
                relativeDIL.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Generated DIL cache path is outside the script root.");
            if (!File.Exists(path)) return;
            RejectLink(path);
            using (var reader = new StreamReader(path))
                if (!string.Equals(reader.ReadLine(), Marker, StringComparison.Ordinal)) return;
            File.Delete(path);
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Symbolic links/junctions are not valid DIL cache destinations: " + path);
        }
    }
}
