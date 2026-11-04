using DMath;

namespace DSimulation
{
    class SBUnit : SBEntity
    {
        internal static readonly Fixed followDistance = Fixed.Decimal(1, 1); // 0.1
        internal static readonly Fixed maxSpeed = 5;
        internal static readonly Fixed acceleration = 5;
        internal static readonly Fixed angularVelocity = 360;
        internal static readonly Fixed rotationSoftness = 10;
        /// How aggressively we close the distance.
        internal static readonly Fixed followGain = 4;

        // Speed
        internal FixedVector2 linearVelocity = FixedVector2.Zero;

        // Movement status
        internal FixedVector2 positionTarget;
        internal SBEntity entityTarget;
        internal MoveMode moveMode = MoveMode.Hold;

        internal enum MoveMode
        {
            Hold,
            Follow,
            Position,
        }

        public SBUnit(FixedVector2 position, Fixed rotation) : base(
            position, rotation, 
            SConstants.UnitRadius, SConstants.UnitPriority, 
            SConstants.UnitHealth, SConstants.UnitShields, SConstants.UnitShieldRegen
            )
        {
            
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
        /// Uses the current move mode to decide how to move towards a target, changing linear velocity and rotating towards a the target.
        /// </summary>
        private void Target()
        {
            FixedVector2 target;
            FixedVector2 targetVelocity;
            Fixed minDistance;

            switch (moveMode)
            {
                case MoveMode.Follow:
                    target = entityTarget.Position;

                    // Capture velocity of a unit, if the target is one. Otherwise, it's velocity is zero.
                    targetVelocity = entityTarget is SBUnit unit
                        ? unit.linearVelocity
                        : FixedVector2.Zero;

                    minDistance = entityTarget.Radius + Radius + followDistance;
                    break;

                case MoveMode.Position:
                    target = positionTarget;
                    targetVelocity = FixedVector2.Zero;
                    minDistance = followDistance;
                    break;

                default:
                    Brake();
                    return;
            }

            FixedVector2 offset = target - Position;

            if (offset != FixedVector2.Zero)
                AimTarget(offset);

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
        /// If the current movement mode is "Entity" but the entity is either null or dead, we change to Hold.
        /// </summary>
        private void EnsureMoveTarget()
        {
            if (moveMode == MoveMode.Follow && (entityTarget == null || !entityTarget.alive))
            {
                entityTarget = null;
                moveMode = MoveMode.Hold;
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
            // Rotates and updates velocity
            Target();
            // Applies velocity
            Move();
        }
    }
}