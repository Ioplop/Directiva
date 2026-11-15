using DSExecution.VirtualMachine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Read-only source position and transport controls of a live simulation.
    /// The code editor does not own or schedule the simulation's entity VMs.
    /// </summary>
    public interface ISimulationDebugSession
    {
        ScriptExecutionState State { get; }
        bool HasSelectedVM { get; }
        long TotalLogicalSteps { get; }
        bool TryGetSelectedSource(out string file, out int oneBasedLine);
        /// <summary>A read-only snapshot of the selected VM and entity.</summary>
        bool TryGetSelectedVariables(out DebuggerVariableSnapshot snapshot);
        void Continue();
        void Pause();
        void Stop();
        // One scheduled charged VM instruction; intervening turns and physics
        // advance as necessary to preserve match determinism.
        void Step();
        void StepSource(VMSourceStepMode mode);
    }
}
