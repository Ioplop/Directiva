using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>Non-heap entity identity. The VM has no SBEntity references.</summary>
    public sealed class DTSimulationObject : DTValue, IOpReferenceEquality
    {
        public static DTSimulationObject Instance { get; } = new DTSimulationObject();
        private DTSimulationObject() { }

        public override OperationResult Hash(OperationCall call)
        {
            if (call.Arguments.Length != 1 ||
                !call.Arguments[0].TryGetSimulationObjectId(out int id))
                return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromInt(id));
        }

        public OperationResult ReferenceEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2 ||
                !call.Arguments[0].TryGetSimulationObjectId(out int first) ||
                !call.Arguments[1].TryGetSimulationObjectId(out int second))
                return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromBool(first == second));
        }

        public override OperationResult IsTruthy(OperationCall call) =>
            OperationResult.Success(DataValue.FromBool(true));

        public override CastResult CastTo(DataTypeId target, DataValue value, VMMemoryContext memory)
        {
            if (target == DataTypeId.String)
            {
                if (!value.TryGetSimulationObjectId(out int id))
                    return CastResult.Failure(RuntimeError.InvalidOperation("Invalid SimulationObject ID."));
                return DTString.CreateValue("<entity #" + id + ">", memory);
            }
            return base.CastTo(target, value, memory);
        }

        public override OperationResult InvokeMember(DScriptMemberKind kind, string name, OperationCall call)
        {
            if (!call.Arguments[0].TryGetSimulationObjectId(out _))
                return OperationResult.OpError(RuntimeError.InvalidOperation("Invalid SimulationObject ID."));
            if (call.HostContext == null)
                return OperationResult.OpError(RuntimeError.InvalidOperation(
                    "SimulationObject members require an entity-bound VM."));
            return call.HostContext.InvokeSimulationMember(kind, name, call);
        }
    }
}
