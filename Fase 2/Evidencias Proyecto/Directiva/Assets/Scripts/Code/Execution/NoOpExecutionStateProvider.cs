using System;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Fallback execution-state provider for contexts that do not attach a VM runner.
    /// The main code-editor UI now uses VMExecutionController instead.
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
