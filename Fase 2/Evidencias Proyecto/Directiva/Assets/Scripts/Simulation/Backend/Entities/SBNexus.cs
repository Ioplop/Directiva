using DMath;
using DSEvents;

namespace DSimulation
{
    /// <summary>
    /// The nexus belonging to a side. Automatically attacks enemies and builds units.
    /// </summary>
    public class SBNexus : SBVMEntity
    {
        private SBEntity lockedTarget;
        private int? buildingRequestId;
        private Fixed buildTime;

        internal SBEntity LockedTarget => lockedTarget;
        internal Fixed BuildProgress => buildTime;
        internal int? CurrentBuildRequestId => buildingRequestId;

        public SBNexus(SBSide side, FixedVector2 position) : base(
            side,
            side.Settings.NexusDamage, side.Settings.NexusRange, side.Settings.NexusAttackSpeed,
            position, 0,
            side.Settings.NexusRadius, side.Settings.NexusPriority,
            side.Settings.NexusHealth, side.Settings.NexusShield, side.Settings.NexusShieldRegen)
        {
        }

        internal bool IsBuilding => buildingRequestId.HasValue;

        /// <summary>
        /// Begins a construction that has already been accepted and paid for by Simulation.
        /// </summary>
        internal void BeginBuild(int requestId)
        {
            buildingRequestId = requestId;
            buildTime = 0;
        }

        /// <summary>
        /// Chooses a spawn point tangent to this nexus, toward the geometric center of all existing nexuses.
        /// </summary>
        private FixedVector2 GetSpawnPosition()
        {
            FixedVector2 sum = FixedVector2.Zero;
            int count = 0;
            foreach (SBSide candidateSide in simulation.Sides)
            {
                if (candidateSide.Nexus != null)
                {
                    sum += candidateSide.Nexus.Position;
                    count++;
                }
            }

            FixedVector2 direction = sum / count - Position;
            // A lone nexus (or coincident centers) has no defined direction.
            direction = direction == FixedVector2.Zero ? FixedVector2.Right : direction.Normalized;
            return Position + direction * (Radius + simulation.Parameters.UnitRadius);
        }

        /// <summary>
        /// Advances the active build and enqueues a unit when its construction is complete.
        /// </summary>
        internal void BuildUnit()
        {
            if (!buildingRequestId.HasValue)
                return;

            buildTime += simulation.Parameters.DeltaTime;
            if (buildTime < simulation.Parameters.UnitBuildTime)
                return;

            FixedVector2 spawnPosition = GetSpawnPosition();
            SBUnit unit = new SBUnit(side, spawnPosition, Trigonometry.Atan2(spawnPosition - Position));
            simulation.EnqueueBuiltUnit(unit, buildingRequestId.Value);
            buildingRequestId = null;
            buildTime = 0;
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
        /// Updates the nexus, its automatic attacks and its current construction.
        /// </summary>
        internal override void Update()
        {
            base.Update();
            if (!alive)
                return;

            // Automatically attack the locked enemy or acquire the nearest one.
            AutoAttack();
            // Continue constructing a unit without interrupting defensive attacks.
            BuildUnit();
        }

        internal EventCreateNexus CreateNexusEvent()
        {
            return new EventCreateNexus(id, side.id, MaxHealth, MaxShields, Position);
        }
    }
}
