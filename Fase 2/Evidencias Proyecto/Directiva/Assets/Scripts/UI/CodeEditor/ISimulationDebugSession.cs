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
        void Continue();
        void Pause();
        void Stop();
        void Step();
    }
}
