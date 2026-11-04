using System;
using System.Collections.Generic;
using DMath;
using DSEvents;

namespace DSimulation
{
    public class Simulation
    {
        private static readonly Fixed GridScale = 2;
        private static readonly short SolverPasses = 3;
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

        // The spatial grid where everything lives in the simulation.
        private readonly List<SBEntity>[,] worldSpace;
        // The size of the world. The world is a rectangle starting at (0, 0) and ending at this value.
        private readonly FixedVector2 dimensions;

        // This is how the frontend sees the simulation.
        private readonly Queue<Event> events = new();
        
        /// <summary>
        /// Creates a new simulation world.
        /// The seed is used
        /// </summary>
        /// <param name="seed">Starting seed for all random number generations in the simulation.</param>
        public Simulation(ulong seed, FixedVector2 dimensions)
        {
            // Random initialization
            this.seed = seed;
            random = new DeterministicRandom(seed);

            // Worldspace initialization
            this.dimensions = dimensions;

            // We expect reasonable widths / heights that don't go too hard on the decimal part. Actually, whole values are preferred how
            // the scene would look with an overlapped grid in Armagetron style. (Decimal values would cut the grid between cells)
            // Passing dimensions that are not whole could end up actually loosing a 0.0000X amount which would actually cause an error where
            // a single cell column or row should exist but doesn't.
            // Finally, we also assume the dimensions are reasonable and won't somehow cause integer overflows or contain absurd precision levels.
            // Find how many grid cells we need horizontally and vertically.
            var fixedCellsX = dimensions.X / GridScale;
            var fixedCellsY = dimensions.Y / GridScale;
            int cellsX = checked((int)Fixed.CeilToLong(fixedCellsX));
            int cellsY = checked((int)Fixed.CeilToLong(fixedCellsY));

            // Instantiate the worldspace grid.
            worldSpace = new List<SBEntity>[cellsX, cellsY];
            for (int x = 0; x < cellsX; x++)
            {
                for (int y = 0;  y < cellsY; y++)
                {
                    worldSpace[x, y] = new();
                }
            }
        }

        internal void NotifyEvent(Event e){
            events.Enqueue(e);
        }

        /// <summary>
        /// Tries to consume a single event.
        /// </summary>
        /// <param name="e">Event consumed</param>
        /// <returns>true if an event was consumed. false otherwise.</returns>
        public bool TryConsumeEvent(out Event e)
        {
            return events.TryDequeue(out e);
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
            UpdateWorldSpace();
        }

        /// <summary>
        /// Performs a single tick simulation that lasts a fixed delta time declared in SimulationConstants.
        /// </summary>
        public void Update()
        {
            if (!started)
                Start();

            events.Enqueue(new EventAdvance(Time, Tick));

            // Update each entity according to their own behavior.
            foreach (var entity in entityList)
            {
                if (entity.alive)
                {
                    entity.Update();
                    // entity updates can cause movement. We must ensure the entity stays inside world bounds.
                    EnsureWorldLimits(entity);
                }
            }

            // Solve all collisions that could result from the previous movements
            MultipassSolveCollisions();

            // Process all enqueued removals and creations.
            ApplyRemovalQueue();
            ApplyCreationQueue();

            NotifyMovements();

            // Update time state
            Time += SConstants.deltaTime;
            Tick++;
            // Add LateUpdate here only if necessary
        }

        /// <summary>
        /// Records movement and rotation events for each entity that changed.
        /// </summary>
        private void NotifyMovements()
        {
            foreach (SBEntity entity in entityList)
            {
                if (entity.alive)
                    entity.NotifyTransform();
            }
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

            if (entity is SBUnit unit)
                NotifyEvent(EventCreateUnit.FromUnit(unit));
            // TODO: Add creation events for nexus and resources when those classes exist

            return id;
        }

        /// <summary>
        /// Returns the grid cell range where an entity belongs.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        private (int minX, int maxX, int minY, int maxY) GetEntityCellBounds(SBEntity entity)
        {
            FixedVector2 position = entity.Position;
            Fixed radius = entity.Radius;

            int minX = Math.Max(0, (int)Fixed.FloorToLong((position.X - radius) / GridScale));
            int maxX = Math.Min(worldSpace.GetLength(0) - 1, (int)Fixed.FloorToLong((position.X + radius) / GridScale));

            int minY = Math.Max(0, (int)Fixed.FloorToLong((position.Y - radius) / GridScale));
            int maxY = Math.Min(worldSpace.GetLength(1) - 1, (int)Fixed.FloorToLong((position.Y + radius) / GridScale));

            return (minX, maxX, minY, maxY);
        }

        /// <summary>
        /// Iterates over a range of grid cells. Min and Max inclusive
        /// </summary>
        private IEnumerable<List<SBEntity>> GetWorldSpaceCells((int minX, int maxX, int minY, int maxY) bounds)
        {
            for (int x = bounds.minX; x <= bounds.maxX; x++)
            {
                for (int y = bounds.minY; y <= bounds.maxY; y++)
                {
                    yield return worldSpace[x, y];
                }
            }
        }

        /// <summary>
        /// Adds a single entity to all the grid spaces in worldspace where it belongs.
        /// </summary>
        private void AddEntityToWorldSpace(SBEntity entity)
        {
            var bounds = GetEntityCellBounds(entity);

            foreach (var cell in GetWorldSpaceCells(bounds))
            {
                cell.Add(entity);
            }
        }

        /// <summary>
        /// Refresh the worldspace grid so all entities are registered to their corresponding grid cells.
        /// </summary>
        private void UpdateWorldSpace()
        {
            foreach (var cell in worldSpace)
                cell.Clear();
            foreach (var entity in entityList)
                if (entity.alive)
                    AddEntityToWorldSpace(entity);
        }

        private void EnsureWorldLimits(SBEntity entity)
        {
            var position = entity.Position;
            var radius = entity.Radius;
            Fixed newX = position.X;
            Fixed newY = position.Y;

            if (position.X - radius < 0)
                newX = radius;
            if (position.X + radius > dimensions.X)
                newX = dimensions.X - radius;
            if (position.Y - radius < 0)
                newY = radius;
            if (position.Y + radius > dimensions.Y)
                newY = dimensions.Y - radius;

            if (newX != position.X || newY != position.Y)
            {
                entity.Position = new FixedVector2(newX, newY);
            }
        }

        /// <summary>
        /// Solves the collision between two entities.
        /// </summary>
        /// <returns>true if a change was made. False otherwise.</returns>
        private bool SolveSingleCollision(SBEntity a, SBEntity b)
        {
            var originalA = a.Position;
            var originalB = b.Position;

            var minDistance = a.Radius + b.Radius;
            var distance = b.Position - a.Position;
            
            // Check that they collide
            if (distance.SqrMagnitude >= minDistance * minDistance)
                return false;

            // Find how much the entities need to separate so they stop colliding.
            Fixed scalarDistance = distance.Magnitude;
            Fixed overlap = minDistance - scalarDistance;

            // Find axis of movement
            FixedVector2 normalDistance;

            // Find a random cardinal direction to separate if both entities are in the exact same position.
            if (scalarDistance == 0)
            {
                normalDistance = random.NextInt(0, 4) switch
                {
                    0 => FixedVector2.Up,
                    1 => FixedVector2.Right,
                    2 => FixedVector2.Down,
                    _ => FixedVector2.Left
                };
            }
            else
            {
                normalDistance = distance / scalarDistance;
            }

            // Check if there are priorities involved.
            // Then separate accordingly
            if (a.CollisionPriority > b.CollisionPriority)
            {
                a.Position -= normalDistance * overlap;
                EnsureWorldLimits(a);
            }
            else if (b.CollisionPriority > a.CollisionPriority) 
            {
                b.Position += normalDistance * overlap;
                EnsureWorldLimits(b);
            }
            else
            {
                a.Position -= normalDistance * overlap / 2;
                b.Position += normalDistance * overlap / 2;
                EnsureWorldLimits(a);
                EnsureWorldLimits(b);
            }
            return originalA != a.Position || originalB != b.Position;
        }

        /// <summary>
        /// Does a single pass of collision detection and correction.
        /// Only solves a single collision per entity in worldspace.
        /// </summary>
        private bool SolveCollisions()
        {
            HashSet<int> collisions = new();
            foreach (var entity in entityList)
            {
                if (!entity.alive || !entity.Collides || collisions.Contains(entity.id))
                    continue;
                bool collided = false;
                var cellBounds = GetEntityCellBounds(entity);
                foreach (var cell in GetWorldSpaceCells(cellBounds))
                {
                    foreach (var other in cell)
                    {
                        if (!other.alive || !other.Collides || collisions.Contains(other.id) || other == entity)
                            continue;
                        if (SolveSingleCollision(entity, other))
                        {
                            collisions.Add(entity.id);
                            collisions.Add(other.id);
                            collided = true;
                            break;
                        }
                    }
                    if (collided)
                        break;
                }
            }

            return collisions.Count > 0;
        }

        /// <summary>
        /// Does multiple passes of the collision solver until we either hit the max pass count
        /// or there are no more collisions.
        /// </summary>
        private void MultipassSolveCollisions()
        {
            UpdateWorldSpace();
            for (int i = 0; i < SolverPasses; i++)
            {
                if (!SolveCollisions())
                    break;
                UpdateWorldSpace();
            }
        }
    }
}
