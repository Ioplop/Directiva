using System;
using System.Collections.Generic;
using DSExecution.Errors;
using DSExecution.VirtualMachine;

namespace DSCompilation.DScript.Backend
{
    /// <summary>Runtime outcome of one independent shared initializer invocation.</summary>
    public sealed class DScriptInitializerOutcome
    {
        public DScriptCompiledInitializer Initializer { get; }
        public VMExecutionStatus Status { get; }
        public RuntimeError Error { get; }
        public int LogicalSteps { get; }
        public bool Success => Status == VMExecutionStatus.Completed;

        internal DScriptInitializerOutcome(DScriptCompiledInitializer initializer,
            VMExecutionStatus status, RuntimeError error, int steps)
        {
            Initializer = initializer; Status = status; Error = error; LogicalSteps = steps;
        }
    }

    public sealed class DScriptInitializationReport
    {
        public IReadOnlyList<DScriptInitializerOutcome> Steps { get; }
        public bool HasErrors { get; }

        internal DScriptInitializationReport(IReadOnlyList<DScriptInitializerOutcome> steps)
        {
            Steps = steps;
            foreach (DScriptInitializerOutcome step in steps)
                if (!step.Success) { HasErrors = true; break; }
        }
    }

    /// <summary>
    /// Executes each shared initializer on a separate temporary VM over ONE shared heap
    /// and global storage. A runtime error only aborts that initializer; later steps
    /// run regardless. The caller creates unit VMs only after Initialize finishes.
    /// No shared globals are reset here; use a new VMMemoryContext for a new match/team.
    /// </summary>
    public static class DScriptBootstrapRunner
    {
        public static DScriptInitializationReport Initialize(
            DScriptCompiledProgram program,
            VMMemoryContext memory,
            int logicalBudgetPerInitializer = 100000,
            VMOptions options = null,
            Action<DScriptCompiledInitializer, RuntimeError> onError = null,
            Action<string> onOutput = null)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (logicalBudgetPerInitializer <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalBudgetPerInitializer));
            if (memory.GlobalCount != program.Code.GlobalCount)
                throw new ArgumentException("Shared memory size does not match the compiled program.", nameof(memory));
            if (!memory.TryBeginBootstrap(program.Code))
                throw new InvalidOperationException(
                    "This memory was already initialized, or belongs to another compiled program.");

            var outcomes = new List<DScriptInitializerOutcome>();
            foreach (DScriptCompiledInitializer step in program.Initializers)
            {
                var vm = new DirectivaVM(program.Code, memory, options, step.FunctionId);
                if (onOutput != null) vm.Printed += onOutput;
                VMRunResult result = vm.Run(logicalBudgetPerInitializer);
                if (onOutput != null) vm.Printed -= onOutput;
                RuntimeError error = result.Error;
                if (result.Status == VMExecutionStatus.Running)
                {
                    error = RuntimeError.InvalidOperation(
                        "Shared initializer '" + step.Variable + "' exceeded the execution budget of " +
                        logicalBudgetPerInitializer + " steps.");
                    vm.Abort(error); // Release GC roots from abandoned call stacks.
                }
                if (error != null) onError?.Invoke(step, error);
                outcomes.Add(new DScriptInitializerOutcome(step, vm.Status, error, result.StepsExecuted));
            }
            return new DScriptInitializationReport(outcomes.AsReadOnly());
        }
    }
}
