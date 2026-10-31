using System.Collections.Generic;
using DMath;

namespace SSimulation
{
    public class Simulation
    {
        // Note on object pooling: While Object pooling is a valid strategy for storing unused entities, for now, building a new one is preffered for simplicity, as no
        // simulation should have more than a couple hundred entities at any given time and more than a single removal / instantiation is already expected
        // to be a rare occurence.
        private int nextId = 1;
        private int NextId { get { return nextId++; } }

        // Maintains entities on a strict ordered structure to sequentially update and do as needed.
        // ID does not match position after entities are removed from the simulation.
        private List<SBEntity> entityList = new();
        // Maintains entities on a dictionary for quick fixed access time by ID
        private Dictionary<int, SBEntity> entityRegistry = new();
        
        // A set of all entities which need to be removed. Order *should not* matter, because removing entities has no immediate consequences.
        // Note: If order DOES end up mattering, change this into a list and ensure that no one entity is removed twice. No further changes are necessary.
        private HashSet<int> nextRemovals = new();
        // A list of all entities that need to be added into the simulation.
        private List<SBEntity> nextCreations = new();

        // Wether or not Start was called at least once
        private bool started = false;
        public Fixed Time { get; private set; }
        public long Tick { get; private set; }
        public ulong seed { get; }
        internal readonly DeterministicRandom random;
        
        /// <summary>
        /// Creates a new simulation world.
        /// The seed is used
        /// </summary>
        /// <param name="seed">Starting seed for all random number generations in the simulation.</param>
        public Simulation(ulong seed)
        {
            this.seed = seed;
            random = new DeterministicRandom(seed);
        }

        /// <summary>
        /// Does the necessary processes to be ready to start updating the simulation. Should be called just before the first Update.
        /// </summary>
        public void Start()
        {
            if (started) return;
            ApplyCreationQueue();
            started = true;
            Time = 0;
            Tick = 0;
        }
        
        /// <summary>
        /// Performs a single tick simulation. delta should be fixed, constant through all updates.
        /// </summary>
        /// <param name="delta">Time elapsed since the last update.</param>
        public void Update(Fixed delta)
        {
            if (!started)
                Start();

            foreach (var entity in entityList)
            {
                if (entity.alive)
                    entity.Update(delta);
            }

            ApplyRemovalQueue();
            ApplyCreationQueue();

            Time += delta;
            Tick++;
            // Add LateUpdate here only if necessary
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entity"></param>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.InvalidOperationException"></exception>
        public void EnqueueCreation(SBEntity entity)
        {
            if (entity is null) 
                throw new System.ArgumentNullException(nameof(entity));

            if (entity.simulation is not null)
                throw new System.InvalidOperationException(
                    $"Tried to enqueue an already registered entity: {entity}"
                );

            if (nextCreations.Contains(entity)) 
                throw new System.InvalidOperationException(
                    $"Tried to enqueue {entity.ToString()} for registering multiple times."
                );

            nextCreations.Add(entity);
        }

        internal void MarkRemoval(int id)
        {
            nextRemovals.Add(id);
        }

        private void ApplyCreationQueue()
        {
            // Adds all new entities to the simulation
            foreach (var entity in nextCreations)
            {
                RegisterEntity(entity);
            }
            nextCreations.Clear();
        }

        private void ApplyRemovalQueue()
        {
            // Remove all marked entities
            foreach (var id in nextRemovals)
            {
                RemoveEntity(id);
            }
            nextRemovals.Clear();
        }

        // Note: The removal method is intentionally "unsafe" and WILL raise an error if an attempt is made at removing an entity that doesn't exist.
        // Since this should ONLY be used inside the simulation and by iterating over "nextRemovals", this error should be guaranteed to be impossible to
        // achieve and triggering is considered a bug.
        /// <summary>
        /// Removes an entity from the simulation based on an id.
        /// </summary>
        /// <param name="id">ID of the entity to be removed.</param>
        /// <returns>Entity that was removed.</returns>
        private SBEntity RemoveEntity(int id)
        {
            SBEntity entity = entityRegistry[id];
            entityRegistry.Remove(id);
            entityList.Remove(entity);
            return entity;
        }

        /// <summary>
        /// Adds an entity to the simulation. The entity is already instantiated. This makes the entity actually interact with everything else,
        /// by assigning it an id and registering it to our structures.
        /// </summary>
        /// <param name="entity">Entity to be added</param>
        /// <returns></returns>
        private int RegisterEntity(SBEntity entity)
        {
            if (entity.simulation is not null) { 
                throw new System.InvalidOperationException("Tried to register an entity that already has been registered in this simulation before.");
            }

            int id = NextId;
            entityList.Add(entity);
            entityRegistry.Add(id, entity);
            entity.Register(this, id);
            return id;
        }
    }
}
