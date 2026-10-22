using System;
using System.Collections.Generic;
using DSExecution.VirtualMachine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Treats a breakpoint as a transition into a DScript source line, not as
    /// every VM instruction emitted for that same line. Re-arms on leaving the
    /// line so later loop iterations can hit it again.
    /// </summary>
    public sealed class DScriptLineBreakpointGate
    {
        private string lastFile;
        private int lastLine = -1;
        private int lastFunctionId = -1;

        public void Reset()
        {
            lastFile = null;
            lastLine = -1;
            lastFunctionId = -1;
        }

        public bool ShouldPause(DirectivaVM vm,
            IReadOnlyDictionary<string, HashSet<int>> linesBySourcePath)
        {
            if (vm == null || vm.CurrentFunction == null || vm.Debug.Line < 0 ||
                string.IsNullOrWhiteSpace(vm.Debug.File))
                return false;

            // Metadata describes the NEXT executable instruction. Do not use
            // the transient location before DEBUG_LINE/DEBUG_SPAN completes.
            if (vm.CurrentFunction.TryGetInstruction(vm.InstructionPointer, out Instruction next) &&
                (next.Id == InstructionId.DebugFile ||
                 next.Id == InstructionId.DebugLine ||
                 next.Id == InstructionId.DebugSpan))
                return false;

            int functionId = vm.CurrentFunction.Id;
            string file = vm.Debug.File;
            int line = vm.Debug.Line;
            bool enteredNewLine = functionId != lastFunctionId || lastLine != line ||
                                  !string.Equals(lastFile, file, StringComparison.Ordinal);
            lastFunctionId = functionId;
            lastFile = file;
            lastLine = line;

            return enteredNewLine && linesBySourcePath != null &&
                   linesBySourcePath.TryGetValue(file, out HashSet<int> breakpoints) &&
                   breakpoints.Contains(line + 1);
        }
    }
}
