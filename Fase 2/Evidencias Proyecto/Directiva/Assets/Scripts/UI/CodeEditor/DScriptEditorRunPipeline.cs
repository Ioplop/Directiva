using System;
using DSCompilation.DScript.Ast;
using DSCompilation.DScript.Backend;
using DSCompilation.DScript.Modules;
using DSExecution.Errors;
using DSExecution.VirtualMachine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Fully prepared editor execution. Compilation and DIL parsing happen in the
    /// backend; initialization runs exactly once on a fresh shared memory context.
    /// The caller owns VM scheduling (VMExecutionController) and console presentation.
    /// </summary>
    public sealed class DScriptEditorPreparedRun
    {
        public DScriptCompilationResult Compilation { get; }
        public DScriptCompiledProgram Program => Compilation?.Program;
        public VMMemoryContext Memory { get; }
        public DScriptInitializationReport Initialization { get; }
        public DirectivaVM VM { get; }
        public string FunctionName { get; }
        public string Error { get; }
        public bool Success => VM != null;

        internal DScriptEditorPreparedRun(DScriptCompilationResult compilation,
            VMMemoryContext memory, DScriptInitializationReport initialization,
            DirectivaVM vm, string functionName, string error)
        {
            Compilation = compilation;
            Memory = memory;
            Initialization = initialization;
            VM = vm;
            FunctionName = functionName;
            Error = error;
        }
    }

    public static class DScriptEditorRunPipeline
    {
        public static DScriptEditorPreparedRun Prepare(string entryModule, string entryFunction,
            IDScriptSourceProvider provider, int initializerBudget = 10000,
            Action<string> onInitializerOutput = null,
            Action<DScriptCompiledInitializer, RuntimeError> onInitializerError = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (initializerBudget <= 0) throw new ArgumentOutOfRangeException(nameof(initializerBudget));

            // Compile() also parses all generated DIL into one CodeContext. The .dil
            // files on disk are purely inspectable artifacts, never inputs to execution.
            DScriptCompilationResult compilation = DScriptCompiler.Compile(entryModule, provider);
            if (!compilation.Success)
                return new DScriptEditorPreparedRun(compilation, null, null, null, null, null);

            string function = (entryFunction ?? string.Empty).Trim();
            if (function.Length == 0)
            {
                // A blank fn: selects the first DEF in the selected source module,
                // not the first function in DIL/CodeContext (which includes imports
                // and internal bootstrap functions). Keep declaration order from AST.
                foreach (Statement statement in compilation.Analysis.Modules[entryModule].Statements)
                {
                    if (statement is FunctionStatement declaration)
                    {
                        function = declaration.Name;
                        break;
                    }
                }

                if (function.Length == 0)
                    return new DScriptEditorPreparedRun(compilation, null, null, null, null,
                        "El módulo '" + entryModule + "' no declara ninguna función ejecutable.");
            }

            string qualified = entryModule + "." + function;
            if (!compilation.Program.TryGetFunctionId(qualified, out int functionId))
                return new DScriptEditorPreparedRun(compilation, null, null, null, qualified,
                    "No existe la función '" + function + "' en el módulo '" + entryModule +
                    "'. El editor ejecuta únicamente funciones del archivo seleccionado.");

            FunctionContext entry = compilation.Program.Code.GetFunction(functionId);
            if (entry.ParameterCount != 0)
                return new DScriptEditorPreparedRun(compilation, null, null, null, qualified,
                    "La función '" + function + "' requiere " + entry.ParameterCount +
                    " parámetro(s). El editor solo puede ejecutar funciones sin parámetros.");

            var memory = new VMMemoryContext(compilation.Program.Code.GlobalCount);
            DScriptInitializationReport initialization = DScriptBootstrapRunner.Initialize(
                compilation.Program, memory, initializerBudget, onError: onInitializerError,
                onOutput: onInitializerOutput);
            var vm = new DirectivaVM(compilation.Program.Code, memory, null, functionId);
            return new DScriptEditorPreparedRun(compilation, memory, initialization, vm, qualified, null);
        }
    }
}
