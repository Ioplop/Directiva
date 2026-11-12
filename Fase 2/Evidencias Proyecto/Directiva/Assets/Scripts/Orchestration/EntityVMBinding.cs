using System;
using DSExecution.VirtualMachine;

namespace DOrchestration
{
    /// <summary>
    /// One script VM and the exact entity whose orders it is authorized to issue.
    /// The VM is private to this entity; its heap and shared slots belong to SideRuntime.
    /// </summary>
    internal sealed class EntityVMBinding
    {
        internal int EntityId { get; }
        internal int SideId { get; }
        internal int EntryFunctionId { get; }
        internal DirectivaVM VM { get; }
        internal long RecoverAtTick { get; set; } = -1;
        internal string LastSourceFile { get; set; }
        internal int LastSourceLine { get; set; } = -1;
        internal int LastSourceFunctionId { get; set; } = -1;

        /// <summary>Detect source-line transitions independently for every VM.</summary>
        internal bool EnteredNewSourceLine()
        {
            DirectivaVM vm = VM;
            if (vm.CurrentFunction == null || vm.Debug.Line < 0 ||
                string.IsNullOrWhiteSpace(vm.Debug.File))
                return false;
            int function = vm.CurrentFunction.Id;
            bool changed = LastSourceLine != vm.Debug.Line ||
                LastSourceFunctionId != function ||
                !string.Equals(LastSourceFile, vm.Debug.File, StringComparison.Ordinal);
            LastSourceFile = vm.Debug.File;
            LastSourceLine = vm.Debug.Line;
            LastSourceFunctionId = function;
            return changed;
        }

        internal void ResetSourceLine()
        {
            LastSourceFile = null;
            LastSourceLine = -1;
            LastSourceFunctionId = -1;
        }

        internal EntityVMBinding(int entityId, int sideId, int functionId, DirectivaVM vm)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            EntityId = entityId;
            SideId = sideId;
            EntryFunctionId = functionId;
            VM = vm ?? throw new ArgumentNullException(nameof(vm));
        }
    }
}
