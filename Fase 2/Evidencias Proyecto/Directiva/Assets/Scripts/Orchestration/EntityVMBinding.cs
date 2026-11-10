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
