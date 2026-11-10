using DSExecution.DataTypes;
using DSExecution.Operations;
using DSExecution.Values;

namespace DSExecution.VirtualMachine
{
    /// <summary>
    /// Optional per-VM host services. Never place these on shared VMMemoryContext:
    /// VMs of the same side share memory but control distinct entities.
    /// This interface deliberately has no simulation, Unity or orchestration types.
    /// </summary>
    public interface IVMHostContext
    {
        DataValue Self { get; }
        OperationResult InvokeSimulationMember(DScriptMemberKind kind, string name, OperationCall call);
    }
}
