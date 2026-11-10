using DMath;

namespace DSimulation
{
    public enum SimulationMoveTargetKind { Entity, Position }

    /// <summary>
    /// Describes the target of a move order without returning an SBEntity reference.
    /// The orchestrator converts this tagged value into either a SimulationObject or a tuple.
    /// A Hold order is represented by a null SimulationMoveTarget.
    /// </summary>
    public readonly struct SimulationMoveTarget
    {
        public SimulationMoveTargetKind Kind { get; }
        public int EntityId { get; }
        public FixedVector2 Position { get; }

        private SimulationMoveTarget(SimulationMoveTargetKind kind, int entityId, FixedVector2 position)
        {
            Kind = kind;
            EntityId = entityId;
            Position = position;
        }

        public static SimulationMoveTarget ForEntity(int entityId) =>
            new SimulationMoveTarget(SimulationMoveTargetKind.Entity, entityId, FixedVector2.Zero);

        public static SimulationMoveTarget ForPosition(FixedVector2 position) =>
            new SimulationMoveTarget(SimulationMoveTargetKind.Position, 0, position);
    }
}
