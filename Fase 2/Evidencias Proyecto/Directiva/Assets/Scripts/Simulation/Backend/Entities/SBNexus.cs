using DMath;
using DSEvents;

namespace DSimulation
{
    /// <summary>
    /// The nexus belonging to a side. Automatically attacks non-neutral enemies in range.
    /// </summary>
    public class SBNexus : SBVMEntity
    {
        private SBEntity lockedTarget;

        public SBNexus(SBSide side, FixedVector2 position) : base(
            side,
            SConstants.NexusDamage, SConstants.NexusRange, SConstants.NexusAttackSpeed,
            position, 0,
            SConstants.NexusRadius, SConstants.NexusPriority,
            SConstants.NexusHealth, SConstants.NexusShield, SConstants.NexusShieldRegen)
        {
        }

        /// <summary>
        /// Acquires the nearest enemy in range and attacks it until it dies or leaves range.
        /// Equally distant enemies are resolved by their deterministic registration order.
        /// </summary>
        internal void AutoAttack()
        {
            Fixed rangeSqr = range * range;

            // Keep the current target during cooldown; CanAttack also checks attack timing.
            if (lockedTarget == null || !lockedTarget.alive ||
                (lockedTarget.Position - Position).SqrMagnitude > rangeSqr)
            {
                lockedTarget = null;
                Fixed nearestDistanceSqr = rangeSqr;

                foreach (SBEntity candidate in simulation.Entities)
                {
                    // Neutral entities and entities on our own side are never targets.
                    if (!candidate.alive || candidate.side.id == 0 || candidate.side == side)
                        continue;

                    Fixed distanceSqr = (candidate.Position - Position).SqrMagnitude;
                    if (distanceSqr <= rangeSqr &&
                        (lockedTarget == null || distanceSqr < nearestDistanceSqr))
                    {
                        lockedTarget = candidate;
                        nearestDistanceSqr = distanceSqr;
                    }
                }
            }

            // Attack handles cooldown, damage and notifications without requiring alignment.
            if (lockedTarget != null)
                Attack(lockedTarget);
        }

        /// <summary>
        /// Updates the nexus and performs its automatic attack behavior.
        /// </summary>
        internal override void Update()
        {
            base.Update();
            if (!alive)
                return;

            // Automatically attack the locked enemy or acquire the nearest one.
            AutoAttack();
        }

        internal EventCreateNexus CreateNexusEvent()
        {
            return new EventCreateNexus(id, side.id, MaxHealth, MaxShields, Position);
        }
    }
}
