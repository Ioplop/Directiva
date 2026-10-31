using DMath;

namespace SSimulation
{
    /// <summary>
    /// Representa cualquier entidad existente dentro de una simulacion de partida: Edificios, Unidades, Recursos
    /// </summary>
    public abstract class SBEntity
    {
        public Simulation simulation { get; private set; }
        public int id { get; private set; }
        public bool alive { get; protected set; }
        public FixedVector2 Position { get; protected set; }
        public FixedVector2 velocity { get; protected set; }
        public Fixed rotation { get; protected set; }
        public Fixed angularVelocity { get; protected set; }

        public SBEntity()
        {
            alive = true;
        }

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
            // Update position and rotation!
            // Do something!
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

        public override string ToString()
        {
            return $"({GetType().Name} {(id!=0?id.ToString():"Unregistered")})";
        }
    }
}
