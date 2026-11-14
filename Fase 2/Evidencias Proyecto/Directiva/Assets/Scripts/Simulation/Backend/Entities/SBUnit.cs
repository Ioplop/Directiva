using DMath;
using DSEvents;

namespace DSimulation
{
    public class SBUnit : SBVMEntity
    {
        // Snapshot the unit-specific rules when this entity is created. Values are
        // never fetched from global mutable state during deterministic updates.
        private readonly Fixed followDistance;
        private readonly Fixed maxSpeed;
        private readonly Fixed acceleration;
        private readonly Fixed angularVelocity;
        private readonly Fixed rotationSoftness;
        private readonly Fixed gatherDistance;
        private readonly Fixed unloadDistance;
        private readonly Fixed cargoSlowdown;
        private readonly int maxCargo;
        private readonly Fixed followGain;
        private readonly Fixed aimAlignment;
        public int MaxCargo => maxCargo;

        // Speed
        internal FixedVector2 linearVelocity = FixedVector2.Zero;

        // Movement status
        internal FixedVector2 positionMoveTarget;
        internal SBEntity entityMoveTarget;
        internal MoveMode moveMode = MoveMode.Hold;

        // Attack status
        internal ActionMode actionMode = ActionMode.Hold;
        internal SBEntity actionTarget;
        internal readonly Fixed aimDistance;
        private bool dropResource = false;
        internal bool DropResource
        {
            get { return dropResource; }
            set
            {
                dropResource = value;

                // Dropping cargo is incompatible with gathering, so we need to stop gathering.
                // It does not interrupt attacking or movement.
                if (value && actionMode == ActionMode.Gather)
                    StopAction();
            }
        }
        private bool isGathering = false;
        internal bool IsGathering
        {
            get { return isGathering; }
            set
            {
                // We already know that we are gathering from this resource or that we are not gathering at all.
                if (isGathering == value && (!value || actionTarget == attachedResource))
                    return;
                timeGathering = 0;
                if (value)
                {
                    if (isGathering)
                    {
                        // We were gathering from a different resource, so we need to detach from it first and inform the frontend.
                        attachedResource.DetachGatherer(this);
                        isGathering = false;
                        NotifyGatheringStatus();
                    }
                    // We are now gathering from a resource, so we need to set the attached resource to the action target.
                    attachedResource = actionTarget as SBResource;
                }
                else
                {
                    // We are no longer gathering, so we need to detach from the resource if we were attached to one.
                    if (attachedResource != null)
                    {
                        attachedResource.DetachGatherer(this);
                        attachedResource = null;
                    }
                }
                isGathering = value;
                NotifyGatheringStatus();
            }
        }

        // Gathering status
        protected int cargo = 0;
        internal int Cargo
        {
            get { return cargo; }
            private set
            {
                if (cargo == value)
                    return;
                cargo = value;
                currentMaxSpeed = maxSpeed;
                for (int i = 1; i <= cargo; i++)
                    currentMaxSpeed *= cargoSlowdown;

                NotifyCargo();
            }
        }
        internal Fixed timeGathering = 0;
        internal Fixed currentMaxSpeed;
        internal SBResource attachedResource = null;


        internal enum ActionMode
        {
            Hold,
            Attack,
            Gather
        }

        internal enum MoveMode
        {
            Hold,
            Follow,
            Position,
        }

        public SBUnit(SBSide side, FixedVector2 position, Fixed rotation) : base(
            side,
            side.Settings.UnitDamage, side.Settings.UnitRange, side.Settings.UnitAttackSpeed,
            position, rotation,
            side.Settings.UnitRadius, side.Settings.UnitPriority,
            side.Settings.UnitHealth, side.Settings.UnitShields, side.Settings.UnitShieldRegen
            )
        {
            var settings = side.Settings;
            followDistance = settings.FollowDistance;
            maxSpeed = settings.MaxSpeed;
            acceleration = settings.Acceleration;
            angularVelocity = settings.AngularVelocity;
            rotationSoftness = settings.RotationSoftness;
            gatherDistance = settings.GatherDistance;
            unloadDistance = settings.UnloadDistance;
            cargoSlowdown = settings.CargoSlowdown;
            maxCargo = settings.MaxCargo;
            followGain = settings.FollowGain;
            aimAlignment = settings.AimAlignment;
            aimDistance = settings.UnitRange * settings.AimDistanceFactor;
            currentMaxSpeed = maxSpeed;
        }

        /// <summary>
        /// Stops the current action, whether it is attacking or gathering, and sets the unit to hold mode.
        /// </summary>
        internal void StopAction()
        {
            actionMode = ActionMode.Hold;
            actionTarget = null;
            IsGathering = false;
        }

        /// <summary>
        /// Drops all carried resources when a drop order is pending.
        /// </summary>
        private void DropCargo()
        {
            if (!dropResource)
                return;

            dropResource = false;
            Cargo = 0;
            simulation.NotifyEvent(new EventDropCargo(id));
        }

        /// <summary>
        /// Automatically unloads carried resources when the unit is close enough to its nexus.
        /// </summary>
        private void UnloadCargo()
        {
            SBNexus nexus = side.Nexus;
            if (cargo == 0 || nexus == null || !nexus.alive)
                return;

            var dist = nexus.Position - Position;
            var maxDist = Radius + nexus.Radius + unloadDistance;
            if (dist.SqrMagnitude > maxDist * maxDist)
                return;

            side.Resources += Cargo;
            Cargo = 0;
            simulation.NotifyEvent(new EventUnloadCargo(id, nexus.id));
        }

        /// <summary>
        /// Creates an event that informs the current entity shields.
        /// </summary>
        internal EventCreateUnit CreateUnitEvent()
        {
            return new EventCreateUnit(id, side.id, MaxHealth, MaxShields, Position, Rotation, MaxCargo);
        }

        /// <summary>
        /// Creates an event that informs the current entity cargo.
        /// </summary>
        protected void NotifyCargo()
        {
            simulation.NotifyEvent(new EventCargo(id, Cargo));
        }

        /// <summary>
        /// Creates an event that informs that the unit is gathering from a resource or has stopped doing so.
        /// </summary>
        protected void NotifyGatheringStatus()
        {
            if (isGathering)
                simulation.NotifyEvent(new EventGather(id, actionTarget.id));
            else
                simulation.NotifyEvent(new EventStopGather(id));
        }

        /// <summary>
        /// Checks that the unit is properly aligned with the target to either attack or gather from it.
        /// This is used to prevent units from attacking or gathering from targets that are not in front of them.
        /// </summary>
        /// <param name="target">The target entity to check alignment with.</param>
        /// <returns>true if the unit is aligned with the target, false otherwise.</returns>
        protected bool Aligned(SBEntity target)
        {
            var dist = target.Position - Position;
            Fixed alignment = dist.Normalized.Dot(Forward);
            return alignment >= aimAlignment;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// This implementation also requires the unit to be aligned with the target.
        /// </remarks>
        protected override bool CanAttack(SBEntity target)
        {
            if (!base.CanAttack(target))
                return false;
            return Aligned(target);
        }

        /// <summary>
        /// Changes current velocity to achieve a given one
        /// </summary>
        /// <param name="desiredVelocity">Velocity that we want to achieve.</param>
        private void ApproachVelocity(FixedVector2 desiredVelocity)
        {
            FixedVector2 change = desiredVelocity - linearVelocity;
            Fixed maxChange = acceleration * simulation.Parameters.DeltaTime;

            linearVelocity += change.ClampMagnitude(maxChange);
        }

        /// <summary>
        /// Makes the unit stop as fast as possible using its acceleration.
        /// Prevents bouncing (braking so much we move in the opposite direction)
        /// </summary>
        private void Brake()
        {
            ApproachVelocity(FixedVector2.Zero);
        }

        /// <summary>
        /// Adjusts velocity to follow a target while maintaining a minimum distance.
        /// </summary>
        /// <param name="offset">Relative position of the target</param>
        /// <param name="targetVelocity">Absolute linear velocity of the target</param>
        /// <param name="minDistance">Distance we want to maintain</param>
        private void FollowTarget(
            FixedVector2 offset,
            FixedVector2 targetVelocity,
            Fixed minDistance)
        {
            // Match the target's velocity by default.
            FixedVector2 desiredVelocity = targetVelocity;

            // Close the distance only when outside the desired range.
            if (offset.SqrMagnitude > minDistance * minDistance)
            {
                Fixed distance = offset.Magnitude;
                Fixed excessDistance = distance - minDistance;

                desiredVelocity +=
                    offset.Normalized * (excessDistance * followGain);
            }

            desiredVelocity = desiredVelocity.ClampMagnitude(currentMaxSpeed);

            ApproachVelocity(desiredVelocity);
        }

        /// <summary>
        /// Makes the unit point at a given target
        /// </summary>
        /// <param name="direction">Desired direction or target to aim at.</param>
        private void AimTarget(FixedVector2 direction, bool instant = false)
        {
            // Target angle guaranteed to be in [0, 360)
            Fixed targetAngle = Trigonometry.Atan2(direction);
            if (instant)
            {
                Rotation = targetAngle;
                return;
            }

            // Find best way to rotate towards target
            var rotate = targetAngle - Rotation;
            if (rotate < -180)
                rotate += 360;
            if (rotate > 180)
                rotate -= 360;

            Fixed remaining = Fixed.Abs(rotate);
            Fixed step = angularVelocity * simulation.Parameters.DeltaTime;

            // Slow down when approaching the target.
            if (remaining < rotationSoftness)
                step *= remaining / rotationSoftness;

            // Never overshoot the target.
            if (step > remaining)
                step = remaining;

            Rotation += rotate < 0 ? -step : step;
        }

        /// <summary>
        /// Decides how to aim depending on current orders and applies the correct rotation.
        /// </summary>
        private void Aim()
        {
            if (actionMode == ActionMode.Attack || actionMode == ActionMode.Gather)
            {
                var direction = actionTarget.Position - Position;
                if (direction.SqrMagnitude <= aimDistance * aimDistance)
                {
                    AimTarget(direction);
                    return;
                }
            }
            if (linearVelocity != FixedVector2.Zero)
            {
                AimTarget(linearVelocity);
            }
        }

        /// <summary>
        /// Uses the current move mode to decide how to move towards a target, changing linear velocity towards a the target.
        /// </summary>
        private void UpdateVelocity()
        {
            FixedVector2 target;
            FixedVector2 targetVelocity;
            Fixed minDistance;

            switch (moveMode)
            {
                case MoveMode.Follow:
                    target = entityMoveTarget.Position;

                    // Capture velocity of a unit, if the target is one. Otherwise, it's velocity is zero.
                    targetVelocity = entityMoveTarget is SBUnit unit
                        ? unit.linearVelocity
                        : FixedVector2.Zero;

                    minDistance = entityMoveTarget.Radius + Radius + followDistance;
                    break;

                case MoveMode.Position:
                    target = positionMoveTarget;
                    targetVelocity = FixedVector2.Zero;
                    minDistance = followDistance;
                    break;

                default:
                    Brake();
                    return;
            }

            FixedVector2 offset = target - Position;
            FollowTarget(offset, targetVelocity, minDistance);
        }

        /// <summary>
        /// Applies unit movement based on current targets.
        /// </summary>
        internal void Move()
        {
            Position += linearVelocity * simulation.Parameters.DeltaTime;
        }

        /// <summary>
        /// If the current movement mode is "Entity" but the entity is either null or dead, change mode to Hold.
        /// </summary>
        private void EnsureMoveTarget()
        {
            if (moveMode == MoveMode.Follow && (entityMoveTarget == null || !entityMoveTarget.alive))
            {
                entityMoveTarget = null;
                moveMode = MoveMode.Hold;
            }
        }

        /// <summary>
        /// If the entity is in attack mode but the entity is either null or dead, change mode to Hold.
        /// </summary>
        private void EnsureActionTarget()
        {
            if ((actionMode == ActionMode.Attack || actionMode == ActionMode.Gather) && (actionTarget == null || !actionTarget.alive))
            {
                StopAction();
            }
        }

        /// <summary>
        /// When the unit is in attack mode, it will try to attack the target if possible.
        /// </summary>
        private void AttackTarget()
        {
            if (actionMode == ActionMode.Attack)
            {
                Attack(actionTarget);
            }
        }

        /// <summary>
        /// When the unit is in gather mode, it will try to gather from the target if possible.
        /// </summary>
        /// <returns>True if the unit is gathering, false otherwise.</returns>
        private void GatherTarget()
        {
            // We only try to gather if we are in gather mode. If we are not, we don't do anything.
            if (actionMode != ActionMode.Gather)
                return;

            // We don't try to gather if we are already full, we just stop gathering and go back to hold mode.
            if (cargo >= maxCargo)
            {
                StopAction();
                return;
            }

            if (actionTarget is SBResource resource)
            {
                // Find distance to the resource and check if we are close enough to gather from it.
                var dist = resource.Position - Position;
                var maxDist = Radius + resource.Radius + gatherDistance;
                
                // We can't gather yet because we are too far away.
                if (dist.SqrMagnitude > maxDist * maxDist)
                {
                    IsGathering = false;
                    return;
                }

                // We can't gather yet because we are not aligned with the resource.
                if (!Aligned(resource))
                {
                    IsGathering = false;
                    return;
                }

                // We meet the requirements to gather from the resource, so we can start gathering.
                // First check that we are attached to it.
                if (resource.GatheringUnit == this && resource == attachedResource) // Two way check. Sure either match both sides or none if the logic is good.
                {
                    timeGathering += simulation.Parameters.DeltaTime;
                    if (timeGathering >= simulation.Parameters.ResourceGatherSpeed)
                    {
                        Cargo += resource.TryGather(maxCargo - Cargo);
                        timeGathering = 0;
                        // Immediately stop gathering if we are full or the resource depletes.
                        // This makes the unit respond immediately to this rather than in the next update.
                        if (!resource.alive || Cargo >= maxCargo)
                            StopAction();
                    }
                    return;
                }

                // We are not attached to the resource yet. We can try to attach.
                IsGathering = false;
                if (resource.TryAttachGatherer(this))
                    IsGathering = true;
                return;
            }
            // Invalid target for gathering!
            // Just cancel the action.
            StopAction();
        }

        internal override void Update()
        {
            base.Update();
            // Don't do anything if we are dead.
            if (!alive)
                return;

            // Process a pending cargo drop without affecting movement or attacks.
            DropCargo();
            // Ensure that we are not pursuing a dead entity.
            EnsureMoveTarget();
            // Ensure that we are not attacking a dead entity.
            EnsureActionTarget();
            // Updates velocity
            UpdateVelocity();
            // Applies velocity as movement.
            Move();
            // Rotates based on targets and velocity.
            Aim();
            // Attacks valid targets in range and aligned to this unit.
            AttackTarget();
            // Gathers valid targets in range and aligned to this unit.
            GatherTarget();
            // Unloading is automatic and does not interrupt movement or other actions.
            UnloadCargo();
        }

        /// <summary>
        /// Ensures that the unit stops any action it was doing before dying, then calls the base Die method to handle the rest of the death process.
        /// Critical to ensure resource detachment and proper event notifications.
        /// </summary>
        internal override void Die()
        {
            if (!alive)
                return;
            StopAction();
            // A dead unit must never retain an active movement command, velocity,
            // target or pending drop. The final position and facing are preserved.
            moveMode = MoveMode.Hold;
            entityMoveTarget = null;
            positionMoveTarget = Position;
            linearVelocity = FixedVector2.Zero;
            dropResource = false;
            timeGathering = 0;
            base.Die();
        }
    }
}