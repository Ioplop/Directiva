using UnityEngine;

namespace Directiva.SimulationFrontend
{
    public enum SimulationVisualKind { Nexus, Unit, Resource }

    /// <summary>
    /// Selection metadata attached to an instantiated visual, never to the
    /// authoritative simulation entity. Mesh colliders may live in children.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimulationEntityIdentity : MonoBehaviour
    {
        public int EntityId { get; private set; }
        public int SideId { get; private set; }
        public SimulationVisualKind Kind { get; private set; }

        public void Initialize(int entityId, int sideId, SimulationVisualKind kind)
        {
            EntityId = entityId;
            SideId = sideId;
            Kind = kind;
        }
    }
}
