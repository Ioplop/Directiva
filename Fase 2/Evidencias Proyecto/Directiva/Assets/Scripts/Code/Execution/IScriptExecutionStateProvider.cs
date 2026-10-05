using System;

namespace Directiva.CodeUI
{
    public enum ScriptExecutionState
    {
        Stopped = 0,
        Running = 1,
        Paused = 2
    }

    /// <summary>
    /// Read-only view of the execution state used by the UI.
    /// A future VM/orchestrator implementation can replace the no-op provider
    /// without changing TopBarView.
    /// </summary>
    public interface IScriptExecutionStateProvider
    {
        ScriptExecutionState State { get; }
        event Action<ScriptExecutionState> StateChanged;
    }
}
