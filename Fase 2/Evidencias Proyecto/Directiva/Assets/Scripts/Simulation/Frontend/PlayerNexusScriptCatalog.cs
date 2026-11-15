using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DSCompilation.DScript;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Lexing;
using DSCompilation.DScript.Modules;
using DSCompilation.DScript.Parsing;
using DOrchestration;
using Directiva.Localization;
using Directiva.CodeUI;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// A player-script compilation error with original DScript source positions.
    /// The Nexus picker and the editor console can show the same diagnostics,
    /// while the console retains a clickable file/line/column target.
    /// </summary>
    public sealed class PlayerNexusCompilationException : InvalidOperationException
    {
        public IReadOnlyList<DScriptDiagnostic> Diagnostics { get; }

        public PlayerNexusCompilationException(string message,
            IEnumerable<DScriptDiagnostic> diagnostics) : base(message)
        {
            Diagnostics = new List<DScriptDiagnostic>(diagnostics ?? Array.Empty<DScriptDiagnostic>());
        }
    }

    /// <summary>Known Nexus catalog failures carry localizable metadata without
    /// making the compiler or its technical Exception.Message language-dependent.
    /// Keep the original exception base types for existing callers and tests.</summary>
    public interface IPlayerNexusLocalizedError
    {
        string LocalizationKey { get; }
        object[] LocalizationArguments { get; }
    }

    public sealed class PlayerNexusArgumentException : ArgumentException, IPlayerNexusLocalizedError
    {
        public string LocalizationKey { get; }
        public object[] LocalizationArguments { get; }

        public PlayerNexusArgumentException(string message, string parameterName,
            string key, params object[] args) : base(message, parameterName)
        {
            LocalizationKey = key;
            LocalizationArguments = args ?? Array.Empty<object>();
        }
    }

    public sealed class PlayerNexusOperationException : InvalidOperationException, IPlayerNexusLocalizedError
    {
        public string LocalizationKey { get; }
        public object[] LocalizationArguments { get; }

        public PlayerNexusOperationException(string message, string key, params object[] args)
            : base(message)
        {
            LocalizationKey = key;
            LocalizationArguments = args ?? Array.Empty<object>();
        }
    }

    public sealed class PlayerNexusFileNotFoundException : FileNotFoundException, IPlayerNexusLocalizedError
    {
        public string LocalizationKey { get; }
        public object[] LocalizationArguments { get; }

        public PlayerNexusFileNotFoundException(string message, string key, params object[] args)
            : base(message)
        {
            LocalizationKey = key;
            LocalizationArguments = args ?? Array.Empty<object>();
        }
    }

    /// <summary>Translate errors at the UI boundary; preserve raw technical messages
    /// and the complete compiler diagnostics for source navigation.</summary>
    public static class PlayerNexusErrorText
    {
        public static string Format(Exception exception)
        {
            if (exception == null) return string.Empty;
            if (exception is IPlayerNexusLocalizedError localized)
                return DirectivaLocalization.Get("Simulation", localized.LocalizationKey,
                    localized.LocalizationArguments);
            if (exception is DirectivaLocalizedException other)
                return DirectivaLocalization.Get(other.Component, other.Key, other.Arguments);
            if (exception is PlayerNexusCompilationException compilation)
            {
                if (compilation.Diagnostics.Count != 0)
                    return DirectivaLocalization.Format(compilation.Diagnostics[0]);
                return DirectivaLocalization.Get("Simulation", "nexus_compilation_failed");
            }
            // OS errors may contain localized platform-provided details.
            return exception.Message;
        }
    }

    /// <summary>
    /// Reads saved player scripts from one isolated workspace root. The embedded
    /// editor owns editing and saving; the match only reads files on disk.
    /// </summary>
    public static class PlayerNexusScriptCatalog
    {
        public const string Extension = ".dscript";

        public static List<string> ListScripts(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new PlayerNexusArgumentException("Missing scripts root.",
                nameof(root), "nexus_missing_root");
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
                throw new PlayerNexusArgumentException("Expected a relative .dscript file.",
                    nameof(relativePath), "nexus_invalid_script_path");
            string module = relativePath.Substring(0, relativePath.Length - Extension.Length)
                .Replace('\\', '/').Replace('/', '.');
            if (!DScriptModuleLoader.IsValidName(module))
                throw new PlayerNexusArgumentException("Invalid DScript module: " + relativePath,
                    nameof(relativePath), "nexus_invalid_module", relativePath);
            return module;
        }

        /// <summary>Lists top-level no-argument functions, in source order.</summary>
        public static List<string> ListEntryFunctions(string root, string relativePath)
        {
            string module = ModuleFromPath(relativePath);
            var provider = new FileSystemDScriptSourceProvider(root);
            if (!provider.TryReadModule(module, out string source))
                throw new PlayerNexusFileNotFoundException("The script was not found: " + relativePath,
                    "nexus_script_not_found", relativePath);

            var lexer = new DScriptLexer(module, source);
            var tokens = lexer.Tokenize();
            if (lexer.Diagnostics.Count != 0)
                throw new PlayerNexusCompilationException(
                    lexer.Diagnostics[0].ToString(), lexer.Diagnostics);
            var parser = new DScriptParser(module, tokens);
            ModuleNode ast = parser.Parse();
            if (parser.Diagnostics.Count != 0)
                throw new PlayerNexusCompilationException(
                    parser.Diagnostics[0].ToString(), parser.Diagnostics);

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
                throw new PlayerNexusArgumentException("Select an entry function.",
                    nameof(function), "nexus_select_function");
            if (!ListEntryFunctions(root, relativePath).Contains(function, StringComparer.Ordinal))
                throw new PlayerNexusOperationException("The entry function '" + function +
                    "' does not exist or requires parameters in " + relativePath + ".",
                    "nexus_entry_invalid", function, relativePath);
            var compilation = DScriptCompiler.Compile(module,
                new FileSystemDScriptSourceProvider(root), SimulationDScriptProfile.Options);
            if (!compilation.Success)
                throw new PlayerNexusCompilationException("Player script compilation failed:\n" +
                    string.Join("\n", compilation.Diagnostics), compilation.Diagnostics);
            if (!compilation.Program.TryGetFunctionId(module + "." + function, out _))
                throw new PlayerNexusOperationException("Entry function not found: " + module + "." + function,
                    "nexus_entry_not_found", module + "." + function);
            return compilation.Program;
        }
    }
}
