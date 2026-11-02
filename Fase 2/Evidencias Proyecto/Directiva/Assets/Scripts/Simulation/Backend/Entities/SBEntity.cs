using DMath;

namespace DSimulation
{
    /// <summary>
    /// Representa cualquier entidad existente dentro de una simulacion de partida: Edificios, Unidades, Recursos
    /// </summary>
    public abstract class SBEntity
    {
        // IDENTITY
        public Simulation simulation { get; private set; }
        public int id { get; private set; }
        public bool alive { get; protected set; }

        // WORLD SPACE
        public FixedVector2 Position { get; internal set; }
        public Fixed Rotation { get; protected set; }

        // COLLISION BEHAVIOR
        public Fixed Radius {  get; protected set; }
        public bool Collides { get; protected set; }
        public int CollisionPriority { get; protected set; }
        
        // ENTITY HEALTH
        public Fixed MaxHealth { get; }
        public Fixed MaxShields { get; }
        public Fixed Health { get; protected set; }
        public Fixed Shields { get; protected set; }
        public Fixed ShieldRegen { get; protected set; }
        public Fixed LastDamage {  get; protected set; }

        public SBEntity()
        {
            alive = true;
            Position = FixedVector2.Zero;
            Rotation = 0;
            Radius = 1;
            CollisionPriority = 0;
            Collides = true;
            MaxHealth = 1;
            Health = 1;
            MaxShields = 0;
            Shields = 0;
            ShieldRegen = 0;
        }

        public SBEntity(FixedVector2 position, Fixed rotation, Fixed radius, int collisionPriority, Fixed maxHealth, Fixed maxShields, Fixed shieldRegen)
        {
            alive = true;
            Position = position;
            Rotation = rotation;
            Radius = radius;
            CollisionPriority = collisionPriority;
            Collides = collisionPriority >= 0;
            MaxHealth = maxHealth;
            Health = maxHealth;
            MaxShields = maxShields;
            Shields = maxShields;
            ShieldRegen = shieldRegen;
        }

        /// <summary>
        /// Called by Simulation to register an entity upon creation.
        /// </summary>
        /// <param name="simulation">Simulation to which this Entity belong.</param>
        /// <param name="id">ID given to this entity.</param>
        /// <exception cref="System.InvalidOperationException">The entity was already registered somewhere</exception>
        internal void Register(Simulation simulation, int id)
        {
            if (this.simulation != null)
                throw new System.InvalidOperationException(
                    "Entity is already registered."
                );

            this.simulation = simulation;
            this.id = id;
        }

        /// <summary>
        /// Runs once per tick.
        /// </summary>
        /// <param name="delta">The amount of time since the last Update call.</param>
        internal virtual void Update(Fixed delta)
        {
            // Do something every tick!
            RegenShields();
        }

        /// <summary>
        /// Kills the entity. This could be done by other entities, or perhaps the simulation itself.
        /// Tells the simulation to mark this entity for deletion.
        /// </summary>
        internal virtual void Die()
        {
            if (!alive)
                return;
            alive = false;
            simulation.MarkRemoval(id);
        }

        protected virtual void Heal(Fixed dmg)
        {
            Health = (Health + dmg).Clamp(0, MaxHealth);
        }

        /// <summary>
        /// Applies damage to the entity.
        /// First takes from shields. Then from health. If by the end health is <= 0, kills the entity.
        /// </summary>
        /// <param name="dmg">Damage to apply. Negative numbers heal, but only health.</param>
        protected virtual void Damage(Fixed dmg)
        {
            if (dmg < 0)
            {
                // This is healing rather than damage. No healing is planned but we will still implement that, by default, heal only applies to health.
                // (Like starcraft when you repair or heal a protoss!)
                Heal(dmg);
                return;
            }
            // TODO: Some sort of event thing that tells the front end to apply a damage animation?
            if (Shields >= dmg)
            {
                Shields -= dmg;
            }
            else
            {
                dmg -= Shields;
                Shields = 0;
                Health -= dmg;
                if (Health <= 0) Die();
            }
            LastDamage = simulation.Time;
        }

        /// <summary>
        /// Regenerates the shields only when they are less than the maximum and enough time has passed since damage was taken.
        /// </summary>
        protected virtual void RegenShields()
        {
            if (Shields < MaxShields && simulation.Time - LastDamage >= SConstants.ShieldRegenCooldown)
            {
                Shields = (Shields + ShieldRegen).Clamp(0, MaxShields);
            }
        }


        public override string ToString()
        {
            return $"({GetType().Name} {(id!=0?id.ToString():"Unregistered")})";
        }
    }
}
