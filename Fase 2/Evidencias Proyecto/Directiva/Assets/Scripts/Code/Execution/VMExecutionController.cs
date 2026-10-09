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
        private int _suppressedBreakpointFunctionId = -1;
        private int _suppressedBreakpointInstructionPointer = -1;

        public ScriptExecutionState State { get; private set; } = ScriptExecutionState.Stopped;
        public DirectivaVM CurrentVM => _vm;
        public long TotalAdvances { get; private set; }
        public double ElapsedMilliseconds => _executionTimer.Elapsed.TotalMilliseconds;

        /// <summary>
        /// Optional source-level breakpoint predicate evaluated immediately before each VM Advance.
        /// Returning true pauses before the current instruction executes.
        /// </summary>
        public Func<DirectivaVM, bool> BreakpointPredicate { get; set; }

        public event Action<ScriptExecutionState> StateChanged;
        public event Action<DataValue> Completed;
        public event Action<RuntimeError> Faulted;
        public event Action<DirectivaVM> BreakpointHit;

        public void Start(DirectivaVM vm, bool paused = false)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            TotalAdvances = 0;
            ClearBreakpointSuppression();
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
            ClearBreakpointSuppression();
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
        /// Breakpoints are checked before every Advance so execution can pause before the
        /// source instruction represented by the current DEBUG_* metadata is executed.
        /// </summary>
        public void Tick(int instructionBudget)
        {
            if (State != ScriptExecutionState.Running || _vm == null)
                return;

            if (instructionBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(instructionBudget));

            // Preserve the existing fast batch path when no breakpoint checks are needed.
            if (BreakpointPredicate == null)
            {
                VMRunResult result = _vm.Run(instructionBudget);
                TotalAdvances += result.StepsExecuted;

                if (result.Status == VMExecutionStatus.Completed)
                {
                    DataValue returnValue = result.ReturnValue;
                    _executionTimer.Stop();
                    _vm = null;
                    ClearBreakpointSuppression();
                    SetState(ScriptExecutionState.Stopped);
                    Completed?.Invoke(returnValue);
                    return;
                }

                if (result.Status == VMExecutionStatus.Faulted)
                {
                    RuntimeError error = result.Error ?? RuntimeError.InternalVmError(
                        "VM reported Faulted without a RuntimeError."
                    );

                    _executionTimer.Stop();
                    _vm = null;
                    ClearBreakpointSuppression();
                    SetState(ScriptExecutionState.Stopped);
                    Faulted?.Invoke(error);
                }

                return;
            }

            for (int i = 0; i < instructionBudget; i++)
            {
                if (State != ScriptExecutionState.Running || _vm == null)
                    return;

                if (TryPauseAtBreakpoint())
                    return;

                if (!AdvanceOnce())
                    return;
            }
        }

        private bool TryPauseAtBreakpoint()
        {
            if (_vm == null || BreakpointPredicate == null)
                return false;

            int functionId = _vm.CurrentFunction?.Id ?? -1;
            int instructionPointer = _vm.InstructionPointer;

            // Continuing or stepping from a breakpoint must be allowed to execute the instruction
            // at which we stopped. Once execution leaves that exact instruction, re-arm it so a
            // loop can hit the same breakpoint again later.
            if (_suppressedBreakpointFunctionId >= 0)
            {
                if (functionId == _suppressedBreakpointFunctionId &&
                    instructionPointer == _suppressedBreakpointInstructionPointer)
                {
                    return false;
                }

                ClearBreakpointSuppression();
            }

            if (!BreakpointPredicate(_vm))
                return false;

            _suppressedBreakpointFunctionId = functionId;
            _suppressedBreakpointInstructionPointer = instructionPointer;
            SetState(ScriptExecutionState.Paused);
            BreakpointHit?.Invoke(_vm);
            return true;
        }

        private void ClearBreakpointSuppression()
        {
            _suppressedBreakpointFunctionId = -1;
            _suppressedBreakpointInstructionPointer = -1;
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
