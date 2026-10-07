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
    /// Read-only view of script execution state consumed by the UI.
    /// VMExecutionController provides the live implementation used by the code editor.
    /// </summary>
    public interface IScriptExecutionStateProvider
    {
        ScriptExecutionState State { get; }
        event Action<ScriptExecutionState> StateChanged;
    }
}
