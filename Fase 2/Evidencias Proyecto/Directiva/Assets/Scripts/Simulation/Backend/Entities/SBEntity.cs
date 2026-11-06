using DMath;
using DSEvents;

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
        private Fixed rotation;
        public Fixed Rotation {
            get { return rotation; }
            protected set {
                rotation = value % 360;
                if (rotation < 0)
                    rotation += 360;
            } 
        }
        // These are the position and rotation before Update. Useful to see if anything changed and notify the frontend.
        internal FixedVector2 OriginalPosition { get; private set; }
        internal Fixed OriginalRotation { get; private set; }
        internal FixedVector2 Forward => FixedVector2.FromAngle(Rotation);

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
        /// Creates an event that informs the current entity position and rotation when they have changed.
        /// Then records the old position so any further changes trigger a new event.
        /// </summary>
        internal void NotifyTransform()
        {
            if (Position != OriginalPosition)
                simulation.NotifyEvent(new EventPosition(id, Position));
            if (Rotation != OriginalRotation)
                simulation.NotifyEvent(new EventRotation(id, Rotation));
            RecordTransform();
        }

        /// <summary>
        /// Creates an event that informs the current entity shields.
        /// </summary>
        internal void NotifyShields()
        {
            simulation.NotifyEvent(new EventShields(id, Shields));
        }

        /// <summary>
        /// Creates an event that informs the current entity health.
        /// </summary>
        internal void NotifyHealth()
        {
            simulation.NotifyEvent(new EventHealth(id, Health));
        }

        /// <summary>
        /// Creates an event that informs of this entities' death.
        /// </summary>
        internal void NotifyDeath()
        {
            simulation.NotifyEvent(new EventDeath(id));
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

            RecordTransform();
        }

        /// <summary>
        /// Records the current state of position and rotation so we can notify any changes in the future.
        /// </summary>
        internal void RecordTransform()
        {
            OriginalPosition = Position;
            OriginalRotation = Rotation;
        }

        /// <summary>
        /// Runs once per tick.
        /// </summary>
        internal virtual void Update()
        {
            if (!alive)
                return;

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
            NotifyTransform();
            alive = false;
            simulation.MarkRemoval(id);
            NotifyDeath();
        }
        
        /// <summary>
        /// Applies damage to the entity with the intention of healing it.
        /// This means it bypasses shields and doesn't apply regeneration cooldown.
        /// </summary>
        /// <param name="dmg"></param>
        protected virtual void Heal(Fixed dmg) 
        {
            Fixed newHealth = (Health + dmg).Clamp(0, MaxHealth);

            if (newHealth != Health)
            {
                Health = newHealth;
                NotifyHealth();
            }
        }

        /// <summary>
        /// Applies damage to the entity.
        /// First takes from shields. Then from health. If by the end health is <= 0, kills the entity.
        /// </summary>
        /// <param name="dmg">Damage to apply. Negative numbers heal, but only health.</param>
        internal virtual void Damage(Fixed dmg)
        {
            if (dmg == 0)
            {
                return;
            }
            if (dmg < 0)
            {
                // This is healing rather than damage. No healing is planned but we will still implement that, by default, heal only applies to health.
                // (Like starcraft when you repair or heal a protoss!)
                Heal(-dmg);
                return;
            }
            // TODO: Some sort of event thing that tells the front end to apply a damage animation?
            if (Shields >= dmg)
            {
                Shields -= dmg;
                NotifyShields();
            }
            else
            {
                if (Shields > 0){
                    dmg -= Shields;
                    Shields = 0;
                    NotifyShields();
                }
                Health = (Health - dmg).Clamp( 0, MaxHealth);
                NotifyHealth();
                if (Health <= 0) Die();
            }
            LastDamage = simulation.Time;
        }

        /// <summary>
        /// Regenerates the shields only when they are less than the maximum and enough time has passed since damage was taken.
        /// </summary>
        protected virtual void RegenShields()
        {
            if (ShieldRegen * SConstants.deltaTime != 0 &&
                simulation.Time - LastDamage >= SConstants.ShieldRegenCooldown)
            {
                Fixed newShields = (Shields + ShieldRegen * SConstants.deltaTime)
                    .Clamp(0, MaxShields);

                if (newShields != Shields)
                {
                    Shields = newShields;
                    NotifyShields();
                }
            }
        }

        public override string ToString()
        {
            return $"({GetType().Name} {(id!=0?id.ToString():"Unregistered")})";
        }
    }
}
