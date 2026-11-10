using System;
using DSimulation;
using DSExecution.DataTypes;
using DSExecution.Errors;
using DSExecution.Operations;
using DSExecution.Values;
using DSExecution.VirtualMachine;

namespace DOrchestration
{
    /// <summary>
    /// One entity's perspective on a simulation. Instances belong to individual VMs;
    /// shared team memory never stores or grants authority over a receiver.
    /// </summary>
    internal sealed class SimulationVMHostContext : IVMHostContext
    {
        private readonly SideRuntime side;
        private readonly SimulationBridge bridge;
        private readonly int controlledId;
        public DataValue Self => DataValue.FromSimulationObject(controlledId);

        internal SimulationVMHostContext(SideRuntime side, SimulationBridge bridge, int controlledId)
        {
            this.side = side ?? throw new ArgumentNullException(nameof(side));
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.controlledId = controlledId;
        }

        public OperationResult InvokeSimulationMember(DScriptMemberKind kind, string name, OperationCall call)
        {
            if (!call.Arguments[0].TryGetSimulationObjectId(out int receiverId))
                return OperationResult.OpError(RuntimeError.InvalidOperation("Invalid entity reference."));
            bool knownObservation = SimulationApiCatalog.TryGetObservation(name, out var observations);
            bool knownOrder = SimulationApiCatalog.TryGetOrder(name, out _);

            if (kind == DScriptMemberKind.Setter)
                return knownObservation
                    ? OperationResult.OpError(RuntimeError.ReadOnlyProperty("SimulationObject", name))
                    : OperationResult.OpError(RuntimeError.MemberNotFound("SimulationObject", name));
            if (kind == DScriptMemberKind.Getter)
            {
                if (!knownObservation || observations[0].MemberKind != SimulationObservationMemberKind.Property)
                    return OperationResult.OpError(RuntimeError.MemberNotFound("SimulationObject", name));
                if (call.Arguments.Length != 1)
                    return OperationResult.OpError(RuntimeError.InvalidMemberArgumentCount(
                        "SimulationObject", name, 0, call.Arguments.Length - 1));
                return ToVM(bridge.Observe(receiverId, name), call.Memory);
            }
            if (kind != DScriptMemberKind.Method)
                return OperationResult.OpError(RuntimeError.MemberNotFound("SimulationObject", name));
            if (!knownOrder && (!knownObservation ||
                observations[0].MemberKind != SimulationObservationMemberKind.Method))
                return OperationResult.OpError(RuntimeError.MemberNotFound("SimulationObject", name));

            if (knownOrder && name == "build")
                return Build(receiverId, call);

            var args = new object[call.Arguments.Length - 1];
            for (int i = 0; i < args.Length; i++)
                args[i] = SimulationValueConverter.ToHost(call.Arguments[i + 1], call.Memory);
            // A position tuple with coordinates outside the representable Fixed range
            // must fail with the same clear bounds diagnostic as move(x, y).
            if (knownOrder && name == "move" && args.Length == 1 &&
                call.Arguments[1].dataType == DataTypeId.Tuple && args[0] is not DMath.FixedVector2)
                return OperationResult.OpError(RuntimeError.InvalidOperation(
                    "move(position) only accepts positions inside world bounds and the safe Fixed numeric range."));
            return ToVM(knownOrder
                ? bridge.Order(controlledId, receiverId, name, args)
                : bridge.Observe(receiverId, name, args), call.Memory);
        }

        private OperationResult Build(int receiverId, OperationCall call)
        {
            if (receiverId != controlledId)
                return OperationResult.OpError(RuntimeError.InvalidOperation(
                    "A script cannot issue orders to an entity other than itself."));
            if (call.Arguments.Length != 2 ||
                !call.Arguments[1].TryGetFunctionId(out int functionId) ||
                !side.IsValidUnitEntry(functionId))
                return OperationResult.OpError(RuntimeError.InvalidOperation(
                    "build(entry_function) requires a compiled, zero-argument DScript function."));
            SimulationCallResult result = bridge.RequestBuild(controlledId, receiverId, out UnitBuildRequest request);
            if (result.Status == SimulationCallStatus.Success && result.Value is bool accepted && accepted)
                side.RememberBuild(request.requestId, functionId);
            return ToVM(result, call.Memory);
        }

        private OperationResult ToVM(SimulationCallResult result, IMemContext memory)
        {
            if (result.Status == SimulationCallStatus.Error)
                return OperationResult.OpError(RuntimeError.InvalidOperation(result.Message));
            if (result.Status == SimulationCallStatus.Warning)
            {
                side.ReportWarning(controlledId, result.Message);
                return OperationResult.Success(DataValue.None());
            }
            return SimulationValueConverter.FromHost(result.Value, memory);
        }
    }
}
