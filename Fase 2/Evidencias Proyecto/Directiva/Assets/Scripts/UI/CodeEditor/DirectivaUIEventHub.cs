using System;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Frontera de eventos de intención emitidos por la UI.
    /// Los sistemas externos pueden suscribirse sin que la UI los conozca.
    /// </summary>
    public sealed class DirectivaUIEventHub
    {
        public event Action CreateScriptRequested;
        public event Action CreateFolderRequested;

        public event Action<string> SaveRequested;
        public event Action<string> RevertRequested;
        public event Action<string, string> RenameRequested;
        public event Action<string> DuplicateRequested;
        public event Action<string> DeleteRequested;
        public event Action<string, string> MoveRequested;

        public event Action ContinueRequested;
        public event Action PauseRequested;
        public event Action StopRequested;

        public event Action StepRequested;
        public event Action StepInRequested;
        public event Action StepOutRequested;

        public event Action<string, int> BreakpointAdded;
        public event Action<string, int> BreakpointRemoved;

        public event Action BackRequested;
        public event Action OptionsRequested;

        public void RaiseCreateScriptRequested() => CreateScriptRequested?.Invoke();
        public void RaiseCreateFolderRequested() => CreateFolderRequested?.Invoke();

        public void RaiseSaveRequested(string path) => SaveRequested?.Invoke(path);
        public void RaiseRevertRequested(string path) => RevertRequested?.Invoke(path);
        public void RaiseRenameRequested(string path, string newName) => RenameRequested?.Invoke(path, newName);
        public void RaiseDuplicateRequested(string path) => DuplicateRequested?.Invoke(path);
        public void RaiseDeleteRequested(string path) => DeleteRequested?.Invoke(path);
        public void RaiseMoveRequested(string sourcePath, string targetFolder) => MoveRequested?.Invoke(sourcePath, targetFolder);

        public void RaiseContinueRequested() => ContinueRequested?.Invoke();
        public void RaisePauseRequested() => PauseRequested?.Invoke();
        public void RaiseStopRequested() => StopRequested?.Invoke();

        public void RaiseStepRequested() => StepRequested?.Invoke();
        public void RaiseStepInRequested() => StepInRequested?.Invoke();
        public void RaiseStepOutRequested() => StepOutRequested?.Invoke();

        public void RaiseBreakpointAdded(string path, int line) => BreakpointAdded?.Invoke(path, line);
        public void RaiseBreakpointRemoved(string path, int line) => BreakpointRemoved?.Invoke(path, line);

        public void RaiseBackRequested() => BackRequested?.Invoke();
        public void RaiseOptionsRequested() => OptionsRequested?.Invoke();
    }
}
