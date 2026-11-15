using System;
using System.Collections.Generic;
using DSEvents;
using DMath;
using DOrchestration;
using DSimulation;
using Directiva.Progress;
using Directiva.SimulationUI;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// Read-only projection of the match's ordered event stream, attributed to
    /// each side independently. Gathering counts resources extracted into cargo,
    /// even if later dropped; destruction includes combat kills and deposits
    /// exhausted by the side's gatherers.
    /// </summary>
    public sealed class SimulationMatchStatistics
    {
        private sealed class EntityState
        {
            internal int Side;
            internal Fixed Health;
            internal Fixed Shields;
            internal int Cargo;
        }

        private sealed class SideCounters
        {
            internal long ResourcesGathered;
            internal int UnitsBuilt;
            internal int EntitiesDestroyed;
            internal long EffectiveDamageRaw;
        }

        // Indices 1 and 2 match the simulation sides; neutral entities have no
        // counters, but they can still be damaged or harvested by either side.
        private readonly SideCounters[] sideCounters =
        {
            null, new SideCounters(), new SideCounters()
        };
        private SideCounters ForSide(int side) => side == 1 || side == 2
            ? sideCounters[side] : null;

        private readonly Dictionary<int, EntityState> entities = new Dictionary<int, EntityState>();
        private readonly Fixed resourceInitialShields;
        private readonly Dictionary<int, int> resourceGatherers = new Dictionary<int, int>();
        private readonly Dictionary<int, int> depletedResourcesBySide =
            new Dictionary<int, int>();
        private int attackerSide;
        private int attackTargetId;
        private long attackDamageRaw;

        // Keep the former side-1 accessors for existing tests and consumers.
        public long ResourcesGathered => sideCounters[1].ResourcesGathered;
        public int UnitsBuilt => sideCounters[1].UnitsBuilt;
        public int EntitiesDestroyed => sideCounters[1].EntitiesDestroyed;
        public long EffectiveDamageRaw => sideCounters[1].EffectiveDamageRaw;

        public long GetResourcesGathered(int side) => ForSide(side)?.ResourcesGathered ?? 0;
        public int GetUnitsBuilt(int side) => ForSide(side)?.UnitsBuilt ?? 0;
        public int GetEntitiesDestroyed(int side) => ForSide(side)?.EntitiesDestroyed ?? 0;
        public long GetEffectiveDamageRaw(int side) => ForSide(side)?.EffectiveDamageRaw ?? 0;

        public SimulationMatchStatistics(Simulation world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            resourceInitialShields = world.Parameters.ResourceShields;
            // The initial world was created before this statistics collector: starting
            // entities must not be counted as units built during the match.
            foreach (SBEntity entity in world.Entities)
            {
                entities[entity.id] = new EntityState
                {
                    Side = entity.side.id,
                    Health = entity.Health,
                    Shields = entity.Shields,
                    Cargo = entity is SBUnit unit ? unit.Cargo : 0
                };
            }
        }

        /// <summary>Observe each event exactly once, in its original simulation order.</summary>
        public void Record(Event message)
        {
            switch (message)
            {
                case EventCreateNexus created:
                    entities[created.id] = new EntityState
                    {
                        Side = created.side, Health = created.maxHealth, Shields = created.maxShields
                    };
                    ResetAttack();
                    break;
                case EventCreateUnit created:
                    entities[created.id] = new EntityState
                    {
                        Side = created.side, Health = created.maxHealth, Shields = created.maxShields
                    };
                    SideCounters buildingSide = ForSide(created.side);
                    if (buildingSide != null) buildingSide.UnitsBuilt++;
                    ResetAttack();
                    break;
                case EventCreateResource created:
                    // Resource shields are supplied by the initial world settings;
                    // newly created resources have no initial damage to attribute.
                    entities[created.id] = new EntityState
                    {
                        Side = 0, Health = created.maxHealth, Shields = resourceInitialShields
                    };
                    ResetAttack();
                    break;
                case EventAttack attack:
                    attackTargetId = attack.targetId;
                    attackerSide = entities.TryGetValue(attack.attackerId, out EntityState attacker)
                        ? attacker.Side : 0;
                    attackDamageRaw = 0;
                    break;
                case EventShields shields:
                    if (entities.TryGetValue(shields.id, out EntityState shieldTarget))
                    {
                        RecordDamage(shields.id, shieldTarget.Shields, shields.shields);
                        shieldTarget.Shields = shields.shields;
                    }
                    break;
                case EventHealth health:
                    if (entities.TryGetValue(health.id, out EntityState healthTarget))
                    {
                        RecordDamage(health.id, healthTarget.Health, health.health);
                        healthTarget.Health = health.health;
                    }
                    break;
                case EventGather gather:
                    resourceGatherers[gather.resourceId] = gather.gethererId;
                    ResetAttack();
                    break;
                case EventStopGather stop:
                    // Each unit gathers at most one resource at a time.
                    int stoppedResourceId = 0;
                    foreach (var pair in resourceGatherers)
                        if (pair.Value == stop.gethererId)
                        {
                            stoppedResourceId = pair.Key;
                            break;
                        }
                    if (stoppedResourceId != 0)
                        resourceGatherers.Remove(stoppedResourceId);
                    ResetAttack();
                    break;
                case EventResources available:
                    if (available.amount <= 0 &&
                        resourceGatherers.TryGetValue(available.id, out int gathererId) &&
                        entities.TryGetValue(gathererId, out EntityState gatherer) &&
                        ForSide(gatherer.Side) != null)
                        depletedResourcesBySide[available.id] = gatherer.Side;
                    ResetAttack();
                    break;
                case EventCargo cargo:
                    if (entities.TryGetValue(cargo.entityId, out EntityState carrier))
                    {
                        SideCounters gatheringSide = ForSide(carrier.Side);
                        if (gatheringSide != null && cargo.cargo > carrier.Cargo)
                            gatheringSide.ResourcesGathered += (long)cargo.cargo - carrier.Cargo;
                        carrier.Cargo = cargo.cargo;
                    }
                    ResetAttack();
                    break;
                case EventPosition moved:
                    // Death first publishes the victim's last transform. Keep
                    // attribution alive for that entity until EventDeath arrives.
                    if (moved.id != attackTargetId) ResetAttack();
                    break;
                case EventRotation rotated:
                    if (rotated.id != attackTargetId) ResetAttack();
                    break;
                case EventDeath death:
                    // A death belongs to the side responsible for the fatal attack,
                    // or to the side that depleted a neutral resource through gathering.
                    // Never credit a single death to both sides.
                    int destroyingSide = death.id == attackTargetId &&
                        attackDamageRaw > 0 ? attackerSide : 0;
                    if (depletedResourcesBySide.TryGetValue(death.id, out int gatherSide))
                    {
                        if (destroyingSide == 0) destroyingSide = gatherSide;
                        depletedResourcesBySide.Remove(death.id);
                    }
                    SideCounters destroyer = ForSide(destroyingSide);
                    if (destroyer != null) destroyer.EntitiesDestroyed++;
                    resourceGatherers.Remove(death.id);
                    entities.Remove(death.id);
                    ResetAttack();
                    break;
                default:
                    // Damage events from one attack are contiguous: an unrelated
                    // event (including the next physics tick) ends its attribution.
                    ResetAttack();
                    break;
            }
        }

        private void RecordDamage(int id, Fixed before, Fixed after)
        {
            if (id != attackTargetId) return;
            SideCounters attacker = ForSide(attackerSide);
            if (attacker == null) return;
            long amount = Math.Max(0L, before.RawValue - after.RawValue);
            // EventHealth/EventShields report already-clamped values. Overkill,
            // regeneration and healing therefore contribute no damage.
            attacker.EffectiveDamageRaw += amount;
            attackDamageRaw += amount;
        }

        private void ResetAttack()
        {
            attackTargetId = 0;
            attackerSide = 0;
            attackDamageRaw = 0;
        }

        public SimulationMatchResult CreateResult(Orchestrator match,
            LevelStarFlags earnedStars, int fastVictorySeconds)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return new SimulationMatchResult(match.Outcome, earnedStars,
                fastVictorySeconds, CreateSideResult(match, 1),
                CreateSideResult(match, 2),
                (double)match.Simulation.Time.RawValue / Constants.FixedScale);
        }

        private SimulationSideMatchResult CreateSideResult(Orchestrator match, int side)
        {
            SideCounters counters = sideCounters[side];
            return new SimulationSideMatchResult(counters.ResourcesGathered,
                match.GetErrorCount(side), counters.UnitsBuilt, counters.EntitiesDestroyed,
                counters.EffectiveDamageRaw, match.GetAcceptedUnitOrders(side));
        }
    }
}
