using DMath;
using DSEvents;

namespace DSimulation
{
    public class SBUnit : SBVMEntity
    {
        internal static readonly Fixed followDistance = Fixed.Decimal(1, 1); // 0.1
        internal static readonly Fixed maxSpeed = 5;
        internal static readonly Fixed acceleration = 5;
        internal static readonly Fixed angularVelocity = 360;
        internal static readonly Fixed rotationSoftness = 10;
        /// How aggressively we close the distance.
        internal static readonly Fixed followGain = 4;
        internal static readonly Fixed aimAlignment = Fixed.Decimal(99, 2); // ~8 degrees of missalignment or less required to shoot

        // Speed
        internal FixedVector2 linearVelocity = FixedVector2.Zero;

        // Movement status
        internal FixedVector2 positionMoveTarget;
        internal SBEntity entityMoveTarget;
        internal MoveMode moveMode = MoveMode.Hold;

        // Attack status
        internal AttackMode attackMode = AttackMode.Hold;
        internal SBEntity attackTarget;
        internal readonly Fixed aimDistance;

        internal enum AttackMode
        {
            Hold,
            Enitity
        }

        internal enum MoveMode
        {
            Hold,
            Follow,
            Position,
        }

        public SBUnit(int side, FixedVector2 position, Fixed rotation) : base(
            side,
            SConstants.UnitDamage, SConstants.UnitRange, SConstants.UnitAttackSpeed,
            position, rotation, 
            SConstants.UnitRadius, SConstants.UnitPriority, 
            SConstants.UnitHealth, SConstants.UnitShields, SConstants.UnitShieldRegen
            )
        {
            aimDistance = SConstants.UnitRange * Fixed.Decimal(15, 1);
        }

        /// <summary>
        /// Creates an event that informs the current entity shields.
        /// </summary>
        internal EventCreateUnit CreateUnitEvent()
        {
            return new EventCreateUnit(id, side, MaxHealth, MaxShields, Position, Rotation);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// This implementation also requires the unit to be aligned with the target.
        /// </remarks>
        protected override bool CanAttack(SBEntity target)
        {
            if (!base.CanAttack(target))
                return false;
            var dist = target.Position - Position;
            Fixed alignment = dist.Normalized.Dot(Forward);
            return alignment >= aimAlignment;
        }

        /// <summary>
        /// Changes current velocity to achieve a given one
        /// </summary>
        /// <param name="desiredVelocity">Velocity that we want to achieve.</param>
        private void ApproachVelocity(FixedVector2 desiredVelocity)
        {
            FixedVector2 change = desiredVelocity - linearVelocity;
            Fixed maxChange = acceleration * SConstants.deltaTime;

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

            desiredVelocity = desiredVelocity.ClampMagnitude(maxSpeed);

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
            Fixed step = angularVelocity * SConstants.deltaTime;

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
            if (attackMode == AttackMode.Enitity)
            {
                var direction = attackTarget.Position - Position;
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
            Position += linearVelocity * SConstants.deltaTime;
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
        /// If the current attack mode is "Entity" but the entity is either null or dead, change mode to Hold.
        /// </summary>
        private void EnsureAttackTarget()
        {
            if (attackMode == AttackMode.Enitity && (attackTarget == null || !attackTarget.alive))
            {
                attackTarget = null;
                attackMode = AttackMode.Hold;
            }
        }

        /// <summary>
        /// When the unit is in attack mode: Entity, it will try to attack the target if possible.
        /// </summary>
        private void AttackTarget()
        {
            if (attackMode == AttackMode.Enitity)
            {
                Attack(attackTarget);
            }
        }

        internal override void Update()
        {
            base.Update();
            // Don't do anything if we are dead.
            if (!alive)
                return;

            // Ensure that we are not pursuing a dead entity.
            EnsureMoveTarget();
            // Ensure that we are not attacking a dead entity.
            EnsureAttackTarget();
            // Updates velocity
            UpdateVelocity();
            // Applies velocity as movement.
            Move();
            // Rotates based on targets and velocity.
            Aim();
            // Attacks valid targets in range and aligned to this unit.
            AttackTarget();
        }
    }
}