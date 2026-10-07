using System;
using DSExecution.Errors;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Drives one DirectivaVM from the UI without blocking the Unity main thread.
    /// Execution advances by a bounded instruction budget on each Tick call.
    /// </summary>
    public sealed class VMExecutionController : IScriptExecutionStateProvider
    {
        private DirectivaVM _vm;

        public ScriptExecutionState State { get; private set; } = ScriptExecutionState.Stopped;
        public DirectivaVM CurrentVM => _vm;

        public event Action<ScriptExecutionState> StateChanged;
        public event Action<DataValue> Completed;
        public event Action<RuntimeError> Faulted;

        public void Start(DirectivaVM vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            SetState(ScriptExecutionState.Running);
        }

        public void Pause()
        {
            if (State == ScriptExecutionState.Running)
                SetState(ScriptExecutionState.Paused);
        }

        public void Continue()
        {
            if (State == ScriptExecutionState.Paused && _vm != null)
                SetState(ScriptExecutionState.Running);
        }

        public void Stop()
        {
            _vm = null;
            SetState(ScriptExecutionState.Stopped);
        }

        /// <summary>
        /// Advances the active VM by at most instructionBudget deterministic VM steps.
        /// Completion and runtime faults stop the controller and are reported through events.
        /// </summary>
        public void Tick(int instructionBudget)
        {
            if (State != ScriptExecutionState.Running || _vm == null)
                return;

            if (instructionBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(instructionBudget));

            var result = _vm.Run(instructionBudget);

            if (result.Status == VMExecutionStatus.Completed)
            {
                var returnValue = result.ReturnValue;
                _vm = null;
                SetState(ScriptExecutionState.Stopped);
                Completed?.Invoke(returnValue);
                return;
            }

            if (result.Status == VMExecutionStatus.Faulted)
            {
                var error = result.Error ?? RuntimeError.InternalVmError(
                    "VM reported Faulted without a RuntimeError."
                );

                _vm = null;
                SetState(ScriptExecutionState.Stopped);
                Faulted?.Invoke(error);
            }
        }

        private void SetState(ScriptExecutionState state)
        {
            if (State == state)
                return;

            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
