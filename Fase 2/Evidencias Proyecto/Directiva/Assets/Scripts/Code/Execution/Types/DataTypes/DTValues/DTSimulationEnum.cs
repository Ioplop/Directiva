using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DSExecution.DataTypes
{
    /// <summary>Immutable typed enum value, with its family encoded in DataValue.</summary>
    public sealed class DTSimulationEnum : DTValue, IOpReferenceEquality
    {
        public static DTSimulationEnum Instance { get; } = new DTSimulationEnum();
        private DTSimulationEnum() { }

        public override OperationResult Hash(OperationCall call)
        {
            if (call.Arguments.Length != 1 ||
                !call.Arguments[0].TryGetSimulationEnum(out _, out _))
                return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromInt(unchecked((long)call.Arguments[0].value)));
        }

        public OperationResult ReferenceEquals(OperationCall call)
        {
            if (call.Arguments.Length != 2 ||
                !call.Arguments[0].TryGetSimulationEnum(out _, out _) ||
                !call.Arguments[1].TryGetSimulationEnum(out _, out _))
                return OperationResult.NotImplemented();
            return OperationResult.Success(DataValue.FromBool(call.Arguments[0].value == call.Arguments[1].value));
        }

        public override OperationResult IsTruthy(OperationCall call) =>
            OperationResult.Success(DataValue.FromBool(true));

        public override CastResult CastTo(DataTypeId target, DataValue value, VMMemoryContext memory)
        {
            if (target == DataTypeId.String)
            {
                if (!value.TryGetSimulationEnum(out SimulationEnumKind kind, out int id))
                    return CastResult.Failure(RuntimeError.InvalidOperation("Invalid simulation enum."));
                string[] names = kind switch
                {
                    SimulationEnumKind.Side => new[] { "Ally", "Enemy", "Neutral" },
                    SimulationEnumKind.Type => new[] { "Unit", "Nexus", "Resource" },
                    SimulationEnumKind.MoveMode => new[] { "Hold", "Follow", "Position" },
                    SimulationEnumKind.ActionMode => new[] { "Hold", "Attack", "Gather" },
                    _ => System.Array.Empty<string>()
                };
                if (id < 0 || id >= names.Length)
                    return CastResult.Failure(RuntimeError.InvalidOperation("Invalid simulation enum value."));
                return DTString.CreateValue(kind + "." + names[id], memory);
            }
            return base.CastTo(target, value, memory);
        }
    }
}
