using DMath;
using DSEvents;

namespace DSimulation
{
    /// <summary>
    /// The nexus belonging to a side. Its behavior will be implemented later.
    /// </summary>
    public class SBNexus : SBVMEntity
    {
        public SBNexus(SBSide side, FixedVector2 position) : base(
            side,
            SConstants.NexusDamage, SConstants.NexusRange, SConstants.NexusAttackSpeed,
            position, 0,
            SConstants.NexusRadius, SConstants.NexusPriority,
            SConstants.NexusHealth, SConstants.NexusShield, SConstants.NexusShieldRegen)
        {
        }

        internal EventCreateNexus CreateNexusEvent()
        {
            return new EventCreateNexus(id, side.id, MaxHealth, MaxShields, Position);
        }
    }
}
