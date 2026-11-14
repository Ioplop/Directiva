using System.Collections.Generic;
using DMath;

namespace DSimulation
{
    /// <summary>
    /// Read-only interface to active and archived simulation state. All operations receive the ID of
    /// the entity whose perspective is being observed. No SBEntity or SBSide references
    /// leave this interface; entity references are represented by IDs.
    ///
    /// Dead entities retain their final state for ordinary getters and geometric
    /// observations. Discovery queries still require a living receiver and only
    /// enumerate living entities. Unknown IDs and inapplicable types return null.
    /// </summary>
    public sealed class SimulationObserver
    {
        private readonly Simulation simulation;

        /// <summary>Creates a read-only observer for the given simulation.</summary>
        internal SimulationObserver(Simulation simulation)
        {
            this.simulation = simulation;
        }

        /// <summary>Resolves an entity ID only when the entity exists and is alive.</summary>
        private bool TryAlive(int id, out SBEntity entity)
        {
            if (simulation.TryGetEntity(id, out entity) && entity.alive)
                return true;
            entity = null;
            return false;
        }

        /// <summary>Resolves a registered or archived entity, including one which died.</summary>
        internal bool IsRecorded(int id) => simulation.TryGetRecordedEntity(id, out _);

        private bool TryRecorded(int id, out SBEntity entity) =>
            simulation.TryGetRecordedEntity(id, out entity);

        /// <summary>Resolves an ID to a unit, including its last state after death.</summary>
        private bool TryUnit(int id, out SBUnit unit)
        {
            bool valid = TryRecorded(id, out SBEntity entity) && entity is SBUnit;
            unit = valid ? (SBUnit)entity : null;
            return valid;
        }

        /// <summary>Resolves an ID to a nexus, including its last state after death.</summary>
        private bool TryNexus(int id, out SBNexus nexus)
        {
            bool valid = TryRecorded(id, out SBEntity entity) && entity is SBNexus;
            nexus = valid ? (SBNexus)entity : null;
            return valid;
        }

        /// <summary>Resolves an ID to a resource, including its last state after death.</summary>
        private bool TryResource(int id, out SBResource resource)
        {
            bool valid = TryRecorded(id, out SBEntity entity) && entity is SBResource;
            resource = valid ? (SBResource)entity : null;
            return valid;
        }

        /// <summary>Resolves an ID to a unit or nexus, including its final state.</summary>
        private bool TryVMEntity(int id, out SBVMEntity vmEntity)
        {
            bool valid = TryRecorded(id, out SBEntity entity) && entity is SBVMEntity;
            vmEntity = valid ? (SBVMEntity)entity : null;
            return valid;
        }

        /// <summary>Identifies the concrete simulation entity type.</summary>
        private static SimulationEntityType? TypeOf(SBEntity entity)
        {
            if (entity is SBUnit) return SimulationEntityType.Unit;
            if (entity is SBNexus) return SimulationEntityType.Nexus;
            if (entity is SBResource) return SimulationEntityType.Resource;
            return null;
        }

        /// <summary>Checks whether an entity matches an optional entity type filter.</summary>
        private static bool MatchesType(SBEntity entity, SimulationEntityType? type)
        {
            SimulationEntityType? actual = TypeOf(entity);
            return actual.HasValue && (!type.HasValue || actual.Value == type.Value);
        }

        /// <summary>Determines the side relationship between two entities.</summary>
        private static SimulationSideRelation Relate(SBEntity receiver, SBEntity other)
        {
            // Neutral entities are neutral to every side, including one another.
            if (receiver.side.id == 0 || other.side.id == 0)
                return SimulationSideRelation.Neutral;
            return receiver.side == other.side
                ? SimulationSideRelation.Ally
                : SimulationSideRelation.Enemy;
        }

        /// <summary>Checks whether two entities have the requested side relationship.</summary>
        private static bool MatchesRelation(SBEntity receiver, SBEntity other, SimulationSideRelation side)
        {
            return Relate(receiver, other) == side;
        }

        /// <summary>Finds the closest living entity matching the given filters.</summary>
        private int? FindClosest(int receiverId, SimulationSideRelation? relation,
            SimulationEntityType? type)
        {
            // Side-relative filters need an affiliated receiver. Purely geometric/type
            // queries work from neutral resources too (e.g. resource.closest_entity(Type.Nexus)).
            if (!TryAlive(receiverId, out SBEntity receiver) ||
                (relation.HasValue && receiver.side.id == 0))
                return null;

            int? closestId = null;
            Fixed nearest = 0;
            foreach (SBEntity candidate in simulation.Entities)
            {
                if (!candidate.alive || candidate.id == receiverId || !MatchesType(candidate, type))
                    continue;
                if (relation.HasValue && !MatchesRelation(receiver, candidate, relation.Value))
                    continue;

                Fixed distance = (candidate.Position - receiver.Position).SqrMagnitude;
                // Strict comparison preserves registration order when distances are equal.
                if (!closestId.HasValue || distance < nearest)
                {
                    closestId = candidate.id;
                    nearest = distance;
                }
            }
            return closestId;
        }

        /// <summary>Collects a snapshot of living entity IDs matching the given filters.</summary>
        private List<int> FindEntities(int receiverId, SimulationSideRelation? relation,
            SimulationEntityType? type, Fixed? maximumDistance = null, bool excludeSelf = false)
        {
            if (!TryAlive(receiverId, out SBEntity receiver) ||
                (relation.HasValue && receiver.side.id == 0))
                return null;

            List<int> result = new();
            foreach (SBEntity candidate in simulation.Entities)
            {
                if (!candidate.alive || !MatchesType(candidate, type) ||
                    (excludeSelf && candidate.id == receiverId))
                    continue;
                if (relation.HasValue && !MatchesRelation(receiver, candidate, relation.Value))
                    continue;
                if (maximumDistance.HasValue &&
                    (candidate.Position - receiver.Position).SqrMagnitude > maximumDistance.Value * maximumDistance.Value)
                    continue;
                result.Add(candidate.id);
            }
            return result;
        }

        // Identity and relationships

        /// <summary>Stable entity ID, even for a dead or removed entity.</summary>
        [SimulationObservation("id", MemberKind = SimulationObservationMemberKind.Property, AllowsDeadReceiver = true)]
        public int GetId(int receiverId) => receiverId;

        /// <summary>Whether an entity is currently alive and registered.</summary>
        [SimulationObservation("alive", MemberKind = SimulationObservationMemberKind.Property, AllowsDeadReceiver = true)]
        public bool IsAlive(int receiverId) => TryAlive(receiverId, out _);

        /// <summary>Returns whether the entity is a unit, nexus, or resource.</summary>
        [SimulationObservation("type", MemberKind = SimulationObservationMemberKind.Property)]
        public SimulationEntityType? GetEntityType(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? TypeOf(entity) : null;

        /// <summary>Returns the relation between the receiver and another entity.</summary>
        [SimulationObservation("relation")]
        public SimulationSideRelation? GetRelation(int receiverId, [SimulationEntityArgument] int otherId)
        {
            if (!TryRecorded(receiverId, out SBEntity receiver) || !TryRecorded(otherId, out SBEntity other))
                return null;
            return Relate(receiver, other);
        }

        /// <summary>Checks whether another entity is an ally of the receiver.</summary>
        [SimulationObservation("is_ally")]
        public bool? IsAlly(int receiverId, [SimulationEntityArgument] int otherId)
        {
            SimulationSideRelation? relation = GetRelation(receiverId, otherId);
            return relation.HasValue ? relation == SimulationSideRelation.Ally : null;
        }

        /// <summary>Checks whether another entity is an enemy of the receiver.</summary>
        [SimulationObservation("is_enemy")]
        public bool? IsEnemy(int receiverId, [SimulationEntityArgument] int otherId)
        {
            SimulationSideRelation? relation = GetRelation(receiverId, otherId);
            return relation.HasValue ? relation == SimulationSideRelation.Enemy : null;
        }

        /// <summary>Checks whether the relation to another entity is neutral.</summary>
        [SimulationObservation("is_neutral")]
        public bool? IsNeutral(int receiverId, [SimulationEntityArgument] int otherId)
        {
            SimulationSideRelation? relation = GetRelation(receiverId, otherId);
            return relation.HasValue ? relation == SimulationSideRelation.Neutral : null;
        }

        // Geometry and range

        /// <summary>Returns the entity position in world coordinates.</summary>
        [SimulationObservation("position", MemberKind = SimulationObservationMemberKind.Property)]
        public FixedVector2? GetPosition(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.Position : null;

        /// <summary>Returns the center-to-center distance to another entity.</summary>
        [SimulationObservation("distance_to")]
        public Fixed? GetDistanceTo(int receiverId, [SimulationEntityArgument] int otherId)
        {
            if (!TryRecorded(receiverId, out SBEntity entity) || !TryRecorded(otherId, out SBEntity other))
                return null;
            return (other.Position - entity.Position).Magnitude;
        }

        /// <summary>Returns the squared center-to-center distance to another entity.</summary>
        [SimulationObservation("distance_squared_to")]
        public Fixed? GetDistanceSquaredTo(int receiverId, [SimulationEntityArgument] int otherId)
        {
            if (!TryRecorded(receiverId, out SBEntity entity) || !TryRecorded(otherId, out SBEntity other))
                return null;
            return (other.Position - entity.Position).SqrMagnitude;
        }

        /// <summary>Returns the entity collision radius.</summary>
        [SimulationObservation("radius", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetRadius(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.Radius : null;

        /// <summary>Returns the entity rotation angle.</summary>
        [SimulationObservation("rotation", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetRotation(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.Rotation : null;

        /// <summary>Returns the forward direction derived from the entity rotation.</summary>
        [SimulationObservation("forward", MemberKind = SimulationObservationMemberKind.Property)]
        public FixedVector2? GetForward(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.Forward : null;

        /// <summary>Returns the distance between entity surfaces, clamped to zero.</summary>
        [SimulationObservation("surface_distance_to")]
        public Fixed? GetSurfaceDistanceTo(int receiverId, [SimulationEntityArgument] int otherId)
        {
            if (!TryRecorded(receiverId, out SBEntity entity) || !TryRecorded(otherId, out SBEntity other))
                return null;
            return Fixed.Max(0, (other.Position - entity.Position).Magnitude - entity.Radius - other.Radius);
        }

        /// <summary>Checks whether another entity is within a given center-to-center distance.</summary>
        [SimulationObservation("within_distance")]
        public bool? IsWithinDistance(int receiverId, [SimulationEntityArgument] int otherId, Fixed distance)
        {
            Fixed? squared = GetDistanceSquaredTo(receiverId, otherId);
            return squared.HasValue ? distance >= 0 && squared.Value <= distance * distance : null;
        }

        /// <summary>Geometric attack reach only: no cooldown, orientation or allegiance checks.</summary>
        [SimulationObservation("in_attack_range", AppliesTo = SimulationEntityKinds.Player)]
        public bool? IsInAttackRange(int receiverId, [SimulationEntityArgument] int targetId)
        {
            if (!TryVMEntity(receiverId, out SBVMEntity attacker) ||
                !TryRecorded(targetId, out SBEntity target))
                return null;
            if (!attacker.alive || !target.alive)
                return false;
            return (target.Position - attacker.Position).SqrMagnitude <= attacker.range * attacker.range;
        }

        /// <summary>Gather reach uses both collision radii and the unit's extra gathering margin.</summary>
        [SimulationObservation("in_gather_range", AppliesTo = SimulationEntityKinds.Unit)]
        public bool? IsInGatherRange(int receiverId, [SimulationEntityArgument(AppliesTo = SimulationEntityKinds.Resource)] int resourceId)
        {
            if (!TryUnit(receiverId, out SBUnit unit) || !TryResource(resourceId, out SBResource resource))
                return null;
            if (!unit.alive || !resource.alive)
                return false;
            Fixed range = unit.Radius + resource.Radius + simulation.Parameters.GatherDistance;
            return (resource.Position - unit.Position).SqrMagnitude <= range * range;
        }

        // Discovery and entity lists. Every returned list is an independent snapshot.

        /// <summary>Returns a snapshot of enemies, optionally filtered by entity type.</summary>
        [SimulationObservation("enemies", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetEnemies(int receiverId, SimulationEntityType? entityType = null) =>
            FindEntities(receiverId, SimulationSideRelation.Enemy, entityType);

        /// <summary>Returns a snapshot of allies, excluding the receiver and optionally filtered by type.</summary>
        [SimulationObservation("allies", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetAllies(int receiverId, SimulationEntityType? entityType = null) =>
            FindEntities(receiverId, SimulationSideRelation.Ally, entityType, excludeSelf: true);

        /// <summary>Returns a snapshot of units belonging to the receiver side.</summary>
        [SimulationObservation("units", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetUnits(int receiverId) =>
            FindEntities(receiverId, SimulationSideRelation.Ally, SimulationEntityType.Unit);

        /// <summary>Returns the side's last known nexus, even if it was destroyed.</summary>
        [SimulationObservation("nexus", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetNexus(int receiverId)
        {
            if (!TryRecorded(receiverId, out SBEntity receiver) || receiver.side.id == 0)
                return null;
            // A previously destroyed nexus is still a stable, inspectable
            // SimulationObject, although no active nexus exists for orders.
            return simulation.GetLastNexusId(receiver.side);
        }

        /// <summary>Returns a snapshot of all living resource deposits.</summary>
        [SimulationObservation("resource_nodes", ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetResourceNodes(int receiverId) =>
            TryAlive(receiverId, out _) ? FindEntities(receiverId, null, SimulationEntityType.Resource) : null;

        /// <summary>Finds the closest enemy, optionally filtered by entity type.</summary>
        [SimulationObservation("closest_enemy", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity)]
        public int? GetClosestEnemy(int receiverId, SimulationEntityType? entityType = null) =>
            FindClosest(receiverId, SimulationSideRelation.Enemy, entityType);

        /// <summary>Finds the closest living resource deposit.</summary>
        [SimulationObservation("closest_resource", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity)]
        public int? GetClosestResource(int receiverId) =>
            FindClosest(receiverId, null, SimulationEntityType.Resource);

        /// <summary>Finds the closest ally other than the receiver.</summary>
        [SimulationObservation("closest_ally", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity)]
        public int? GetClosestAlly(int receiverId) =>
            FindClosest(receiverId, SimulationSideRelation.Ally, null);

        /// <summary>
        /// Returns all living entities or those of a requested type, from any side.
        /// Includes the receiver itself; works for neutral resources as well.
        /// </summary>
        [SimulationObservation("entities", ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetEntitiesByType(int receiverId, SimulationEntityType? entityType = null) =>
            FindEntities(receiverId, null, entityType);

        /// <summary>Returns entities matching a relative side and optional type filter.</summary>
        [SimulationObservation("entities", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetEntities(int receiverId, SimulationSideRelation side, SimulationEntityType? entityType = null) =>
            FindEntities(receiverId, side, entityType);

        /// <summary>
        /// Returns living entities within a distance, optionally restricted by type.
        /// No side filter is required, so neutral resources can also use this query.
        /// </summary>
        [SimulationObservation("entities_in_range", ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetEntitiesInRangeByType(int receiverId, Fixed distance,
            SimulationEntityType? entityType = null)
        {
            if (!TryAlive(receiverId, out _))
                return null;
            if (distance < 0)
                return new List<int>();
            return FindEntities(receiverId, null, entityType, maximumDistance: distance);
        }

        /// <summary>Returns entities within a distance that match the side and type filters.</summary>
        [SimulationObservation("entities_in_range", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.EntityList)]
        public List<int> GetEntitiesInRange(int receiverId, Fixed distance, SimulationSideRelation side,
            SimulationEntityType? entityType = null)
        {
            if (!TryAlive(receiverId, out SBEntity receiver) || receiver.side.id == 0)
                return null;
            if (distance < 0)
                return new List<int>();
            return FindEntities(receiverId, side, entityType, maximumDistance: distance);
        }

        /// <summary>
        /// Finds the closest other entity, optionally restricted by type, from any side.
        /// Unlike side-relative queries this is valid on neutral resource receivers.
        /// </summary>
        [SimulationObservation("closest_entity", ResultKind = SimulationObservationResultKind.Entity)]
        public int? GetClosestEntityByType(int receiverId, SimulationEntityType? entityType = null) =>
            FindClosest(receiverId, null, entityType);

        /// <summary>Finds the closest entity matching a side and optional type filter.</summary>
        [SimulationObservation("closest_entity", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity)]
        public int? GetClosestEntity(int receiverId, SimulationSideRelation side,
            SimulationEntityType? entityType = null) => FindClosest(receiverId, side, entityType);

        /// <summary>Finds the nearest living enemy nexus.</summary>
        [SimulationObservation("enemy_nexus", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity)]
        public int? GetEnemyNexus(int receiverId) =>
            FindClosest(receiverId, SimulationSideRelation.Enemy, SimulationEntityType.Nexus);

        /// <summary>Counts the living enemies visible from the receiver perspective.</summary>
        [SimulationObservation("count_enemies", AppliesTo = SimulationEntityKinds.Player)]
        public int? CountEnemies(int receiverId) => GetEnemies(receiverId)?.Count;

        /// <summary>Counts living allies other than the receiver.</summary>
        [SimulationObservation("count_allies", AppliesTo = SimulationEntityKinds.Player)]
        public int? CountAllies(int receiverId) => GetAllies(receiverId)?.Count;

        // Health, shields and combat

        /// <summary>Returns the entity current health.</summary>
        [SimulationObservation("health", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetHealth(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.Health : null;

        /// <summary>Returns the entity maximum health.</summary>
        [SimulationObservation("max_health", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetMaxHealth(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.MaxHealth : null;

        /// <summary>Returns the entity current shields.</summary>
        [SimulationObservation("shields", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetShields(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.Shields : null;

        /// <summary>Returns the entity maximum shields.</summary>
        [SimulationObservation("max_shields", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetMaxShields(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.MaxShields : null;

        /// <summary>Returns the entity shield regeneration rate.</summary>
        [SimulationObservation("shield_regen", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetShieldRegen(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.ShieldRegen : null;

        /// <summary>Returns the simulation time of the entity latest recorded damage.</summary>
        [SimulationObservation("last_damage_time", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetLastDamageTime(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.LastDamage : null;

        /// <summary>Returns the time remaining before shield regeneration resumes.</summary>
        [SimulationObservation("shield_regen_cooldown_remaining", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetShieldRegenCooldownRemaining(int receiverId)
        {
            if (!TryRecorded(receiverId, out SBEntity entity))
                return null;
            return entity.alive
                ? Fixed.Max(0, simulation.Parameters.ShieldRegenCooldown - (simulation.Time - entity.LastDamage))
                : (Fixed)0;
        }

        /// <summary>Returns the attack range of a unit or nexus.</summary>
        [SimulationObservation("attack_range", AppliesTo = SimulationEntityKinds.Player, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetAttackRange(int receiverId) =>
            TryVMEntity(receiverId, out SBVMEntity entity) ? entity.range : null;

        /// <summary>Returns the damage dealt per attack by a unit or nexus.</summary>
        [SimulationObservation("attack_damage", AppliesTo = SimulationEntityKinds.Player, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetAttackDamage(int receiverId) =>
            TryVMEntity(receiverId, out SBVMEntity entity) ? entity.damage : null;

        /// <summary>Returns the minimum interval between attacks.</summary>
        [SimulationObservation("attack_cooldown", AppliesTo = SimulationEntityKinds.Player, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetAttackCooldown(int receiverId) =>
            TryVMEntity(receiverId, out SBVMEntity entity) ? entity.attackSpeed : null;

        /// <summary>Returns the remaining time until the next attack is available.</summary>
        [SimulationObservation("attack_cooldown_remaining", AppliesTo = SimulationEntityKinds.Player, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetAttackCooldownRemaining(int receiverId) =>
            TryVMEntity(receiverId, out SBVMEntity entity) ? (entity.alive ? entity.AttackCooldownRemaining : (Fixed)0) : null;

        /// <summary>Checks whether the attack cooldown has elapsed.</summary>
        [SimulationObservation("attack_ready", AppliesTo = SimulationEntityKinds.Player, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsAttackReady(int receiverId) =>
            TryVMEntity(receiverId, out SBVMEntity entity) ? entity.alive && entity.AttackCooldownRemaining <= 0 : null;

        /// <summary>Returns the unit attack target or nexus locked target, if alive.</summary>
        [SimulationObservation("attack_target", AppliesTo = SimulationEntityKinds.Player, ResultKind = SimulationObservationResultKind.Entity, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetAttackTarget(int receiverId)
        {
            if (!TryRecorded(receiverId, out SBEntity entity))
                return null;
            SBEntity target = null;
            if (entity is SBUnit unit && unit.actionMode == SBUnit.ActionMode.Attack)
                target = unit.actionTarget;
            else if (entity is SBNexus nexus)
                target = nexus.LockedTarget;
            return target != null && target.alive ? target.id : null;
        }

        // Movement, action modes and unit status

        /// <summary>Returns the unit current linear velocity.</summary>
        [SimulationObservation("velocity", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public FixedVector2? GetVelocity(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.linearVelocity : null;

        /// <summary>Returns the unit current maximum speed, including cargo slowdown.</summary>
        [SimulationObservation("max_speed", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetMaxSpeed(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.currentMaxSpeed : null;

        /// <summary>Returns the unit current movement mode.</summary>
        [SimulationObservation("move_mode", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public SimulationMoveMode? GetMoveMode(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            return (SimulationMoveMode)unit.moveMode;
        }

        /// <summary>Returns the position or entity targeted by the unit movement order.</summary>
        [SimulationObservation("move_target", AppliesTo = SimulationEntityKinds.Unit, ResultKind = SimulationObservationResultKind.MoveTarget, MemberKind = SimulationObservationMemberKind.Property)]
        public SimulationMoveTarget? GetMoveTarget(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            if (unit.moveMode == SBUnit.MoveMode.Position)
                return SimulationMoveTarget.ForPosition(unit.positionMoveTarget);
            if (unit.moveMode == SBUnit.MoveMode.Follow &&
                unit.entityMoveTarget != null && unit.entityMoveTarget.alive)
                return SimulationMoveTarget.ForEntity(unit.entityMoveTarget.id);
            return null;
        }

        /// <summary>Returns the unit current action mode.</summary>
        [SimulationObservation("action_mode", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public SimulationActionMode? GetActionMode(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            return (SimulationActionMode)unit.actionMode;
        }

        /// <summary>Returns the living target of the unit attack or gather order.</summary>
        [SimulationObservation("action_target", AppliesTo = SimulationEntityKinds.Unit, ResultKind = SimulationObservationResultKind.Entity, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetActionTarget(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit) || unit.actionMode == SBUnit.ActionMode.Hold ||
                unit.actionTarget == null || !unit.actionTarget.alive)
                return null;
            return unit.actionTarget.id;
        }

        /// <summary>Indicates whether the unit is actively gathering a resource.</summary>
        [SimulationObservation("is_gathering", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsGathering(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.IsGathering : null;

        /// <summary>Indicates whether the unit has nonzero velocity.</summary>
        [SimulationObservation("is_moving", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsMoving(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.linearVelocity != FixedVector2.Zero : null;

        /// <summary>Indicates whether the unit has a movement order other than Hold.</summary>
        [SimulationObservation("has_move_order", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? HasMoveOrder(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.moveMode != SBUnit.MoveMode.Hold : null;

        /// <summary>Indicates whether the unit currently has an attack order.</summary>
        [SimulationObservation("is_attacking", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsAttacking(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.actionMode == SBUnit.ActionMode.Attack : null;

        /// <summary>Indicates whether the unit currently has a gather order.</summary>
        [SimulationObservation("is_gather_ordered", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsGatherOrdered(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.actionMode == SBUnit.ActionMode.Gather : null;

        /// <summary>Indicates whether the unit has a pending cargo drop order.</summary>
        [SimulationObservation("drop_pending", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsDropPending(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.DropResource : null;

        /// <summary>Returns the unit linear acceleration.</summary>
        [SimulationObservation("acceleration", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetAcceleration(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.Acceleration : null;

        /// <summary>Returns the unit maximum angular velocity.</summary>
        [SimulationObservation("turn_speed", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetTurnSpeed(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.AngularVelocity : null;

        // Cargo and gathering

        /// <summary>Returns the number of resources carried by the unit.</summary>
        [SimulationObservation("cargo", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetCargo(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.Cargo : null;

        /// <summary>Returns the maximum number of resources the unit can carry.</summary>
        [SimulationObservation("cargo_capacity", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetCargoCapacity(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.MaxCargo : null;

        /// <summary>Checks whether the unit cargo is at capacity.</summary>
        [SimulationObservation("is_cargo_full", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsCargoFull(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.Cargo >= simulation.Parameters.MaxCargo : null;

        /// <summary>Returns elapsed time toward the unit next resource extraction.</summary>
        [SimulationObservation("gather_progress", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetGatherProgress(int receiverId) =>
            TryUnit(receiverId, out SBUnit unit) ? unit.timeGathering : null;

        /// <summary>Returns the time required for one resource extraction.</summary>
        [SimulationObservation("gather_interval", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetGatherInterval(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.ResourceGatherSpeed : null;

        /// <summary>Returns the gathering margin beyond the combined entity radii.</summary>
        [SimulationObservation("gather_distance", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetGatherDistance(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.GatherDistance : null;

        /// <summary>Returns the automatic unloading margin beyond the combined entity radii.</summary>
        [SimulationObservation("unload_distance", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetUnloadDistance(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.UnloadDistance : null;

        /// <summary>Returns the multiplicative speed factor applied for each carried resource.</summary>
        [SimulationObservation("cargo_slowdown_factor", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetCargoSlowdownFactor(int receiverId) =>
            TryUnit(receiverId, out _) ? simulation.Parameters.CargoSlowdown : null;

        /// <summary>Checks whether a loaded unit is within unloading range of its living nexus.</summary>
        [SimulationObservation("can_unload", AppliesTo = SimulationEntityKinds.Unit, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? CanUnload(int receiverId)
        {
            if (!TryUnit(receiverId, out SBUnit unit))
                return null;
            SBNexus nexus = unit.side.Nexus;
            if (!unit.alive || unit.Cargo == 0 || nexus == null || !nexus.alive)
                return false;
            Fixed distance = unit.Radius + nexus.Radius + simulation.Parameters.UnloadDistance;
            return (nexus.Position - unit.Position).SqrMagnitude <= distance * distance;
        }

        // Resource deposits

        /// <summary>Returns the resources remaining in a deposit.</summary>
        [SimulationObservation("amount", AppliesTo = SimulationEntityKinds.Resource, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetAmount(int receiverId) =>
            TryResource(receiverId, out SBResource resource) ? resource.Amount : null;

        /// <summary>Returns the amount yielded by one extraction from a deposit.</summary>
        [SimulationObservation("richness", AppliesTo = SimulationEntityKinds.Resource, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetRichness(int receiverId) =>
            TryResource(receiverId, out SBResource resource) ? resource.GatherAmount : null;

        /// <summary>Returns the ID of the unit currently attached to a deposit, if alive.</summary>
        [SimulationObservation("gatherer", AppliesTo = SimulationEntityKinds.Resource, ResultKind = SimulationObservationResultKind.Entity, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetGatherer(int receiverId)
        {
            if (!TryResource(receiverId, out SBResource resource))
                return null;
            SBUnit unit = resource.GatheringUnit;
            return unit != null && unit.alive ? unit.id : null;
        }

        /// <summary>Checks whether a deposit is free of any living gatherer.</summary>
        [SimulationObservation("is_available_to_gather", AppliesTo = SimulationEntityKinds.Resource, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsAvailableToGather(int receiverId)
        {
            if (!TryResource(receiverId, out SBResource resource))
                return null;
            return resource.alive && (resource.GatheringUnit == null || !resource.GatheringUnit.alive);
        }

        // Economy and building

        /// <summary>Returns the resources currently owned by the receiver side.</summary>
        [SimulationObservation("resources", MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetResources(int receiverId) =>
            TryRecorded(receiverId, out SBEntity entity) ? entity.side.Resources : null;

        /// <summary>Indicates whether the nexus has a unit under construction.</summary>
        [SimulationObservation("is_building", AppliesTo = SimulationEntityKinds.Nexus, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? IsBuilding(int receiverId) =>
            TryNexus(receiverId, out SBNexus nexus) ? nexus.IsBuilding : null;

        /// <summary>Returns the resource cost of constructing a unit.</summary>
        [SimulationObservation("unit_cost", MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetUnitCost(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Parameters.UnitCost : null;

        /// <summary>Returns the time needed to construct a unit.</summary>
        [SimulationObservation("unit_build_time", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetUnitBuildTime(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Parameters.UnitBuildTime : null;

        /// <summary>Returns the time left in the nexus current construction.</summary>
        [SimulationObservation("build_time_remaining", AppliesTo = SimulationEntityKinds.Nexus, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetBuildTimeRemaining(int receiverId) =>
            TryNexus(receiverId, out SBNexus nexus) && nexus.IsBuilding
                ? Fixed.Max(0, simulation.Parameters.UnitBuildTime - nexus.BuildProgress) : null;

        /// <summary>Returns the elapsed time of the nexus current construction.</summary>
        [SimulationObservation("build_progress", AppliesTo = SimulationEntityKinds.Nexus, MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetBuildProgress(int receiverId) =>
            TryNexus(receiverId, out SBNexus nexus) && nexus.IsBuilding ? nexus.BuildProgress : null;

        /// <summary>Returns the ID of the nexus active build request, if any.</summary>
        [SimulationObservation("current_build_request_id", AppliesTo = SimulationEntityKinds.Nexus, MemberKind = SimulationObservationMemberKind.Property)]
        public int? GetCurrentBuildRequestId(int receiverId) =>
            TryNexus(receiverId, out SBNexus nexus) ? nexus.CurrentBuildRequestId : null;

        /// <summary>Checks whether a nexus can accept a new unit build request.</summary>
        [SimulationObservation("can_build", AppliesTo = SimulationEntityKinds.Nexus, MemberKind = SimulationObservationMemberKind.Property)]
        public bool? CanBuild(int receiverId) =>
            TryNexus(receiverId, out SBNexus nexus)
                ? nexus.alive && nexus.side.id != 0 && !nexus.IsBuilding && nexus.side.Resources >= simulation.Parameters.UnitCost
                : null;

        // Simulation time and world rules; archived receivers also retain access.

        /// <summary>Returns the current elapsed simulation time.</summary>
        [SimulationObservation("time", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetTime(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Time : null;

        /// <summary>Returns the current simulation tick.</summary>
        [SimulationObservation("tick", MemberKind = SimulationObservationMemberKind.Property)]
        public long? GetTick(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Tick : null;

        /// <summary>Returns the fixed duration of one simulation tick.</summary>
        [SimulationObservation("delta_time", MemberKind = SimulationObservationMemberKind.Property)]
        public Fixed? GetDeltaTime(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Parameters.DeltaTime : null;

        /// <summary>Returns the world dimensions.</summary>
        [SimulationObservation("world_size", MemberKind = SimulationObservationMemberKind.Property)]
        public FixedVector2? GetWorldSize(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Dimensions : null;

        /// <summary>Returns the geometric center of the world.</summary>
        [SimulationObservation("world_center", MemberKind = SimulationObservationMemberKind.Property)]
        public FixedVector2? GetWorldCenter(int receiverId) =>
            TryRecorded(receiverId, out _) ? simulation.Dimensions / 2 : null;

        /// <summary>Checks whether a coordinate lies within the world boundaries.</summary>
        [SimulationObservation("inside_world")]
        public bool? IsInsideWorld(int receiverId, Fixed x, Fixed y)
        {
            if (!TryRecorded(receiverId, out _))
                return null;
            return x >= 0 && y >= 0 && x <= simulation.Dimensions.X && y <= simulation.Dimensions.Y;
        }
    }

    /// <summary>Stable public counterparts of SBUnit's internal movement modes.</summary>
    public enum SimulationMoveMode { Hold, Follow, Position }

    /// <summary>Stable public counterparts of SBUnit's internal action modes.</summary>
    public enum SimulationActionMode { Hold, Attack, Gather }
}
