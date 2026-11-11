using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Parsing;
using DOrchestration;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Reads saved player scripts from one isolated workspace root. The embedded
    /// editor owns editing and saving; the match only reads files on disk.
    /// </summary>
    public static class PlayerNexusScriptCatalog
    {
        public const string Extension = ".dscript";

        public static List<string> ListScripts(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Missing scripts root.", nameof(root));
            if (!Directory.Exists(root)) return new List<string>();

            var result = new List<string>();
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.Count != 0)
            {
                string directory = pending.Pop();
                // Do not walk junctions/symlinks outside the player's workspace.
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string subfolder in Directory.GetDirectories(directory)) pending.Push(subfolder);
                foreach (string fullPath in Directory.GetFiles(directory, "*" + Extension))
                {
                    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0) continue;
                    string relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
                    if (!relative.EndsWith(Extension, StringComparison.Ordinal)) continue;
                    string module = relative.Substring(0, relative.Length - Extension.Length).Replace('/', '.');
                    // The selector recognizes exactly the module paths allowed by DScript.
                    if (DScriptModuleLoader.IsValidName(module)) result.Add(relative);
                }
            }
            result.Sort((left, right) =>
            {
                int result = StringComparer.OrdinalIgnoreCase.Compare(left, right);
                return result == 0 ? StringComparer.Ordinal.Compare(left, right) : result;
            });
            return result;
        }

        public static string ModuleFromPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) ||
                !relativePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected a relative .dscript file.", nameof(relativePath));
            string module = relativePath.Substring(0, relativePath.Length - Extension.Length)
                .Replace('\\', '/').Replace('/', '.');
            if (!DScriptModuleLoader.IsValidName(module))
                throw new ArgumentException("Invalid DScript module: " + relativePath, nameof(relativePath));
            return module;
        }

        /// <summary>Lists top-level no-argument functions, in source order.</summary>
        public static List<string> ListEntryFunctions(string root, string relativePath)
        {
            string module = ModuleFromPath(relativePath);
            var provider = new FileSystemDScriptSourceProvider(root);
            if (!provider.TryReadModule(module, out string source))
                throw new FileNotFoundException("The script was not found: " + relativePath);

            var lexer = new DScriptLexer(module, source);
            var tokens = lexer.Tokenize();
            if (lexer.Diagnostics.Count != 0)
                throw new InvalidOperationException(lexer.Diagnostics[0].ToString());
            var parser = new DScriptParser(module, tokens);
            ModuleNode ast = parser.Parse();
            if (parser.Diagnostics.Count != 0)
                throw new InvalidOperationException(parser.Diagnostics[0].ToString());

            return ast.Statements.OfType<FunctionStatement>()
                .Where(function => function.Parameters.Count == 0)
                .Select(function => function.Name)
                .Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>Compile only the selected player's workspace, including its imports.</summary>
        public static DScriptCompiledProgram Compile(string root, string relativePath, string function)
        {
            string module = ModuleFromPath(relativePath);
            if (string.IsNullOrWhiteSpace(function))
                throw new ArgumentException("Select an entry function.", nameof(function));
            if (!ListEntryFunctions(root, relativePath).Contains(function, StringComparer.Ordinal))
                throw new InvalidOperationException("The entry function '" + function +
                    "' does not exist or requires parameters in " + relativePath + ".");
            var compilation = DScriptCompiler.Compile(module,
                new FileSystemDScriptSourceProvider(root), SimulationDScriptProfile.Options);
            if (!compilation.Success)
                throw new InvalidOperationException("Player script compilation failed:\n" +
                    string.Join("\n", compilation.Diagnostics));
            if (!compilation.Program.TryGetFunctionId(module + "." + function, out _))
                throw new InvalidOperationException("Entry function not found: " + module + "." + function);
            return compilation.Program;
        }
    }
}
