using System;

namespace DSExecution.VirtualMachine
{
    /// <summary>Debugger movement relative to a DScript source line and call frame.</summary>
    public enum VMSourceStepMode
    {
        Over,
        Into,
        Out
    }

    /// <summary>
    /// Observes one VM; it does not execute instructions or alter scheduling.
    /// Call depth prevents Step Over from stopping inside a callee, while Step Into
    /// deliberately stops at the first executable instruction in that callee.
    /// </summary>
    public sealed class VMSourceStepPlan
    {
        private readonly VMSourceStepMode mode;
        private readonly int startDepth;
        private readonly int startFunction;
        private readonly string startFile;
        private readonly int startLine;

        public VMSourceStepPlan(DirectivaVM vm, VMSourceStepMode mode)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            this.mode = mode;
            startDepth = vm.CallDepth;
            startFunction = vm.CurrentFunction?.Id ?? -1;
            startFile = vm.Debug.File;
            startLine = vm.Debug.Line;
        }

        /// <summary>
        /// The initial source instruction is not a new breakpoint destination for
        /// this step, even if a callee returned here and the frame changed twice.
        /// Other breakpoints (including inside the callee) remain active.
        /// </summary>
        public bool IsStartingLocation(DirectivaVM vm) =>
            vm != null && vm.CallDepth == startDepth &&
            (vm.CurrentFunction?.Id ?? -1) == startFunction &&
            vm.Debug.Line == startLine &&
            string.Equals(vm.Debug.File, startFile, StringComparison.Ordinal);

        /// <summary>
        /// Call only after consuming leading DEBUG_* metadata. Return/completion may
        /// end a step even when there is no following executable instruction.
        /// </summary>
        public bool ReachedTarget(DirectivaVM vm)
        {
            if (vm == null || vm.Status != VMExecutionStatus.Running)
                return true;

            if (vm.CallDepth < startDepth)
                return true; // Returned from the original function.

            if (mode == VMSourceStepMode.Out)
                return false;

            // The callee may share its source line number with the caller, but it
            // is still a distinct frame and is a valid Step Into destination.
            if (vm.CallDepth > startDepth)
                return mode == VMSourceStepMode.Into && HasStableSource(vm);

            if (!HasStableSource(vm))
                return false;

            return (vm.CurrentFunction?.Id ?? -1) != startFunction ||
                vm.Debug.Line != startLine ||
                !string.Equals(vm.Debug.File, startFile, StringComparison.Ordinal);
        }

        private static bool HasStableSource(DirectivaVM vm) =>
            vm.Debug.Line >= 0 && !string.IsNullOrWhiteSpace(vm.Debug.File) &&
            vm.NextInstructionCost != 0;
    }
}
