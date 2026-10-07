using System;
using System.Diagnostics;
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
        private readonly Stopwatch _executionTimer = new();

        public ScriptExecutionState State { get; private set; } = ScriptExecutionState.Stopped;
        public DirectivaVM CurrentVM => _vm;
        public long TotalAdvances { get; private set; }
        public double ElapsedMilliseconds => _executionTimer.Elapsed.TotalMilliseconds;

        public event Action<ScriptExecutionState> StateChanged;
        public event Action<DataValue> Completed;
        public event Action<RuntimeError> Faulted;

        public void Start(DirectivaVM vm, bool paused = false)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            TotalAdvances = 0;
            _executionTimer.Restart();
            SetState(paused ? ScriptExecutionState.Paused : ScriptExecutionState.Running);
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
            if (_vm != null || State != ScriptExecutionState.Stopped)
                _executionTimer.Stop();

            _vm = null;
            SetState(ScriptExecutionState.Stopped);
        }

        /// <summary>
        /// Executes one visible DIL/VM step while paused. DEBUG_* metadata can be consumed
        /// transparently so parser-injected source metadata does not require extra button presses.
        /// Continued operations still advance one deterministic VM step at a time, so they remain
        /// highlighted on the same DIL instruction until their continuation completes.
        /// </summary>
        public void Step(bool skipDebugMetadata)
        {
            if (State != ScriptExecutionState.Paused || _vm == null)
                return;

            if (skipDebugMetadata && !ConsumeLeadingDebugMetadata())
                return;

            if (!AdvanceOnce())
                return;

            if (skipDebugMetadata)
                ConsumeLeadingDebugMetadata();
        }

        private bool ConsumeLeadingDebugMetadata()
        {
            while (_vm != null && IsNextInstructionDebugMetadata(_vm))
            {
                if (!AdvanceOnce())
                    return false;
            }

            return _vm != null;
        }

        private static bool IsNextInstructionDebugMetadata(DirectivaVM vm)
        {
            if (vm.CurrentFunction == null ||
                !vm.CurrentFunction.TryGetInstruction(vm.InstructionPointer, out Instruction instruction))
            {
                return false;
            }

            return instruction.Id == InstructionId.DebugFile ||
                   instruction.Id == InstructionId.DebugLine ||
                   instruction.Id == InstructionId.DebugSpan;
        }

        private bool AdvanceOnce()
        {
            if (_vm == null)
                return false;

            VMAdvanceResult result = _vm.Advance();
            TotalAdvances++;

            if (result.Status == VMExecutionStatus.Completed)
            {
                DataValue returnValue = result.ReturnValue;
                _executionTimer.Stop();
                _vm = null;
                SetState(ScriptExecutionState.Stopped);
                Completed?.Invoke(returnValue);
                return false;
            }

            if (result.Status == VMExecutionStatus.Faulted)
            {
                RuntimeError error = result.Error ?? RuntimeError.InternalVmError(
                    "VM reported Faulted without a RuntimeError."
                );

                _executionTimer.Stop();
                _vm = null;
                SetState(ScriptExecutionState.Stopped);
                Faulted?.Invoke(error);
                return false;
            }

            return true;
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
            TotalAdvances += result.StepsExecuted;

            if (result.Status == VMExecutionStatus.Completed)
            {
                var returnValue = result.ReturnValue;
                _executionTimer.Stop();
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

                _executionTimer.Stop();
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
