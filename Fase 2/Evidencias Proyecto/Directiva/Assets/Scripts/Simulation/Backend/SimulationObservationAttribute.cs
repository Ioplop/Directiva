using System;

namespace DSimulation
{
    /// <summary>Entity types accepted by an observation. Used for API discovery, not for access control.</summary>
    [Flags]
    public enum SimulationEntityKinds
    {
        None = 0,
        Unit = 1,
        Nexus = 2,
        Resource = 4,
        Player = Unit | Nexus,
        All = Player | Resource
    }

    /// <summary>Entity types exposed to DScript as Type.* values.</summary>
    public enum SimulationEntityType { Unit, Nexus, Resource }

    /// <summary>Relative side relationships exposed to DScript as Side.* values.</summary>
    public enum SimulationSideRelation { Ally, Enemy, Neutral }

    public enum SimulationObservationMemberKind { Method, Property }

    /// <summary>
    /// Result conversions that cannot be inferred from the CLR return type alone.
    /// Value is handled by the usual primitive, enum or FixedVector2 converters.
    /// </summary>
    public enum SimulationObservationResultKind { Value, Entity, EntityList, MoveTarget }

    /// <summary>
    /// Marks an observer method for future static discovery by the orchestration layer.
    /// The first int parameter is always the receiver's entity ID and is implicit in DScript.
    /// Inapplicable receiver types return null; the orchestrator is responsible for warnings.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SimulationObservationAttribute : Attribute
    {
        public string Name { get; }
        public SimulationEntityKinds AppliesTo { get; set; } = SimulationEntityKinds.All;
        public SimulationObservationResultKind ResultKind { get; set; } = SimulationObservationResultKind.Value;
        public SimulationObservationMemberKind MemberKind { get; set; } = SimulationObservationMemberKind.Method;
        public bool AllowsDeadReceiver { get; set; }

        public SimulationObservationAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Marks a non-receiver integer parameter as a SimulationObject ID, rather than a DScript Int.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class SimulationEntityArgumentAttribute : Attribute
    {
        public SimulationEntityKinds AppliesTo { get; set; } = SimulationEntityKinds.All;
    }
}
