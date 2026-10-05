using System;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Initial v1 execution-state provider.
    /// There is no real VM execution yet, therefore the state is always Stopped.
    /// </summary>
    public sealed class NoOpExecutionStateProvider : IScriptExecutionStateProvider
    {
        public ScriptExecutionState State => ScriptExecutionState.Stopped;

        // Required by the interface so a real provider can notify the UI later.
        public event Action<ScriptExecutionState> StateChanged
        {
            add { }
            remove { }
        }
    }
}
