using DMath;

namespace DSimulation
{
    /// <summary>
    /// Public interface for issuing orders to simulation entities through their IDs.
    /// Orders change the entity's decisions; physical actions occur on later ticks.
    /// The controller does not know about DScript or VM ownership. The future
    /// orchestrator must authorize the receiver before invoking an order.
    ///
    /// Methods return null for an absent, dead or incompatible receiver; false for
    /// a valid receiver whose order cannot be accepted; true for an accepted order.
    /// </summary>
    public sealed class SimulationController
    {
        private readonly Simulation simulation;

        /// <summary>Creates a controller for the given simulation.</summary>
        internal SimulationController(Simulation simulation)
        {
            this.simulation = simulation;
        }

        /// <summary>Resolves a living unit that can receive an order.</summary>
        private bool TryUnit(int id, out SBUnit unit)
        {
            bool valid = simulation.TryGetEntity(id, out SBEntity entity) && entity.alive && entity is SBUnit;
            unit = valid ? (SBUnit)entity : null;
            return valid;
        }

        /// <summary>Resolves a living nexus that can receive a build order.</summary>
        private bool TryNexus(int id)
        {
            return simulation.TryGetEntity(id, out SBEntity entity) && entity.alive && entity is SBNexus;
        }

        /// <summary>Orders a unit to travel toward a position, without changing its action.</summary>
        [SimulationOrder("move", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryMoveToPosition(int receiverId, Fixed x, Fixed y)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;

            unit.positionMoveTarget = new FixedVector2(x, y);
            unit.entityMoveTarget = null;
            unit.moveMode = SBUnit.MoveMode.Position;
            return true;
        }

        /// <summary>Orders a unit to follow a living entity, without changing its action.</summary>
        [SimulationOrder("move", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryMoveToEntity(int receiverId, [SimulationEntityArgument] int targetId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            if (!simulation.TryGetEntity(targetId, out SBEntity target) || !target.alive || target == unit)
                return false;

            unit.entityMoveTarget = target;
            unit.moveMode = SBUnit.MoveMode.Follow;
            return true;
        }

        /// <summary>Sets movement to Hold, allowing the unit to brake naturally.</summary>
        [SimulationOrder("stop_move", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryStopMove(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;

            unit.moveMode = SBUnit.MoveMode.Hold;
            unit.entityMoveTarget = null;
            return true;
        }

        /// <summary>Orders a unit to attack any living entity, including allies or resources.</summary>
        [SimulationOrder("attack", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryAttack(int receiverId, [SimulationEntityArgument] int targetId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            if (!simulation.TryGetEntity(targetId, out SBEntity target) || !target.alive)
                return false;

            // An interrupted gathering order must release its reserved resource immediately.
            if (unit.actionMode == SBUnit.ActionMode.Gather)
                unit.StopAction();

            unit.actionTarget = target;
            unit.actionMode = SBUnit.ActionMode.Attack;
            return true;
        }

        /// <summary>Orders a unit to gather from a resource without moving automatically.</summary>
        [SimulationOrder("gather", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryGather(int receiverId, [SimulationEntityArgument(AppliesTo = SimulationEntityKinds.Resource)] int resourceId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            // Gathering cannot resume while a cargo drop is still pending.
            if (unit.DropResource)
                return false;
            if (!simulation.TryGetEntity(resourceId, out SBEntity entity) || !entity.alive || entity is not SBResource resource)
                return false;

            // Repeating a gather order must not reset the current extraction progress.
            if (unit.actionMode == SBUnit.ActionMode.Gather && unit.actionTarget == resource)
                return true;

            if (unit.actionMode == SBUnit.ActionMode.Gather)
                unit.StopAction();

            unit.actionTarget = resource;
            unit.actionMode = SBUnit.ActionMode.Gather;
            return true;
        }

        /// <summary>Stops attacking or gathering, but does not change movement orders.</summary>
        [SimulationOrder("stop_action", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryStopAction(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;

            unit.StopAction();
            return true;
        }

        /// <summary>Schedules the loss of all carried resources on the unit's next update.</summary>
        [SimulationOrder("drop_cargo", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? TryDropCargo(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;

            // Dropping cargo interrupts gathering, but never attack or movement.
            unit.DropResource = true;
            return true;
        }

        /// <summary>
        /// Requests one unit at a nexus and returns its accepted request ID.
        /// DScript build(entry_function) requires manual orchestration to associate
        /// that request with the entry function; DSimulation never receives DTFunction.
        /// </summary>
        [SimulationOrder("build", AppliesTo = SimulationEntityKinds.Nexus, ManualRegistration = true)]
        public bool? TryBuildUnit(int receiverId, out UnitBuildRequest request)
        {
            request = default;
            if (!TryNexus(receiverId))
                return null;

            return simulation.TryRequestUnitBuild(receiverId, out request);
        }
    }
}
