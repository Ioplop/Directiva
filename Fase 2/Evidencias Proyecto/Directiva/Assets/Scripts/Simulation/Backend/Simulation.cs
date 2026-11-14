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
        // Maximum tangential correction per contact (0.015 world units).
        // This breaks exact head-on symmetry without perturbing normal separation.
        private static readonly Fixed CollisionSlideStep = Fixed.Decimal(15, 3);
        // Note on object pooling: While Object pooling is a valid strategy for storing unused entities, for now, building a new one is preffered for simplicity, as no
        // simulation should have more than a couple hundred entities at any given time and more than a single removal / instantiation is already expected
        // to be a rare occurence.
        private int nextId = 1;
        private int NextId { get { return nextId++; } }
        // Request IDs are separate from entity IDs; no unit ID is reserved before registration.
        private int nextBuildRequestId = 1;

        // Sides are indexed by their ID: neutral (0), player 1 (1), player 2 (2).
        private readonly SBSide[] sides;
        public SimulationParameters Parameters { get; }

        public SBSide GetSide(int side) => sides[side];
        // Allows nexus spawn calculations to inspect all existing nexuses.
        internal IReadOnlyList<SBSide> Sides => sides;

        // Maintains entities on a strict ordered structure to sequentially update and do as needed.
        // ID does not match position after entities are removed from the simulation.
        private List<SBEntity> entityList = new();
        // Read-only access for targeting, preserving the deterministic registration order.
        internal IReadOnlyList<SBEntity> Entities => entityList;

        // Read-only observation API for the future orchestrator.
        public SimulationObserver Observer { get; }

        // Public order API; the future orchestrator is responsible for authorization.
        public SimulationController Controller { get; }

        // Commands and active-world queries only see living registrations.
        internal bool TryGetEntity(int id, out SBEntity entity) => entityRegistry.TryGetValue(id, out entity);

        /// <summary>
        /// Read-only identity lookup. A dead entity remains observable at its final
        /// state, but never returns to the active registry, collision grid or sides.
        /// This archive lives only as long as its simulation match.
        /// </summary>
        internal bool TryGetRecordedEntity(int id, out SBEntity entity) =>
            entityRegistry.TryGetValue(id, out entity) || deadEntityRegistry.TryGetValue(id, out entity);

        internal int? GetLastNexusId(SBSide side)
        {
            int id = lastNexusIds[side.id];
            return id != 0 && TryGetRecordedEntity(id, out _) ? id : null;
        }

        // Maintains entities on a dictionary for quick fixed access time by ID
        private Dictionary<int, SBEntity> entityRegistry = new();
        // Dead entities are archived separately: queries can still read their last
        // state without exposing them to simulation updates or entity enumeration.
        private readonly Dictionary<int, SBEntity> deadEntityRegistry = new();
        private readonly int[] lastNexusIds = new int[3];
        
        // A set of all entities which need to be removed. Order *should not* matter, because removing entities has no immediate consequences.
        // Note: If order DOES end up mattering, change this into a list and ensure that no one entity is removed twice. No further changes are necessary.
        private HashSet<int> nextRemovals = new();
        // A list of all entities that need to be added into the simulation.
        private List<SBEntity> nextCreations = new();
        // Links built units to their requests until they receive an actual entity ID.
        private readonly Dictionary<SBUnit, int> pendingBuildRegistrations = new();
        private readonly Queue<UnitCreationResult> createdUnits = new();

        // Wether or not Start was called at least once
        private bool started = false;
        public Fixed Time { get; private set; }
        public long Tick { get; private set; }
        public ulong seed { get; }
        internal readonly DeterministicRandom random;
        // A pair must keep its preferred passing side from one tick to the next.
        // Keyed by ordered entity IDs; collisions do not consume the script RNG.
        private readonly Dictionary<ulong, int> collisionSlideDirections = new();

        // The spatial grid where everything lives in the simulation.
        private readonly List<SBEntity>[,] worldSpace;
        // The size of the world. The world is a rectangle starting at (0, 0) and ending at this value.
        private readonly FixedVector2 dimensions;
        internal FixedVector2 Dimensions => dimensions;

        // This is how the frontend sees the simulation.
        private readonly Queue<Event> events = new();
        
        /// <summary>
        /// Creates a new simulation world.
        /// The seed is used
        /// </summary>
        /// <param name="seed">Starting seed for all random number generations in the simulation.</param>
        public Simulation(ulong seed, FixedVector2 dimensions, SimulationParameters parameters = null)
        {
            Parameters = (parameters ?? new SimulationParameters()).Clone();
            Parameters.Validate();
            sides = new[] { new SBSide(0, Parameters), new SBSide(1, Parameters),
                new SBSide(2, Parameters) };
            Observer = new SimulationObserver(this);
            Controller = new SimulationController(this);

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
        /// Starts a unit build at the given nexus if it is alive, idle and has enough resources.
        /// A request ID is assigned only after the build is accepted.
        /// </summary>
        public bool TryRequestUnitBuild(int nexusId, out UnitBuildRequest request)
        {
            request = default;
            if (!entityRegistry.TryGetValue(nexusId, out SBEntity entity) ||
                entity is not SBNexus nexus || !nexus.alive || nexus.side.id == 0 ||
                nexus.IsBuilding || nexus.side.Resources < Parameters.UnitCost)
                return false;

            request = new UnitBuildRequest(nextBuildRequestId++, nexusId);
            nexus.side.Resources -= Parameters.UnitCost;
            nexus.BeginBuild(request.requestId);
            return true;
        }

        /// <summary>
        /// Consumes the next completed unit construction, independently of frontend events.
        /// The returned unit ID is already registered in this simulation.
        /// </summary>
        public bool TryConsumeCreatedUnit(out UnitCreationResult result)
        {
            return createdUnits.TryDequeue(out result);
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
            Time += Parameters.DeltaTime;
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

        /// <summary>
        /// Enqueues a constructed unit and remembers its request until registration assigns an ID.
        /// </summary>
        internal void EnqueueBuiltUnit(SBUnit unit, int requestId)
        {
            EnqueueCreation(unit);
            pendingBuildRegistrations.Add(unit, requestId);
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
                int unitId = RegisterEntity(entity);
                if (entity is SBUnit unit &&
                    pendingBuildRegistrations.TryGetValue(unit, out int requestId))
                {
                    createdUnits.Enqueue(new UnitCreationResult(requestId, unitId));
                    pendingBuildRegistrations.Remove(unit);
                }
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
            if (!entity.alive)
                deadEntityRegistry.Add(id, entity);

            if (entity is SBUnit unit)
                unit.side.units.Remove(unit);
            else if (entity is SBResource resource)
                resource.side.resourceEntities.Remove(resource);
            else if (entity is SBNexus nexus)
                nexus.side.Nexus = null;

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

            // Register each entity with its side and notify its creation.
            if (entity is SBUnit unit)
            {
                unit.side.units.Add(unit);
                NotifyEvent(unit.CreateUnitEvent());
            }
            else if (entity is SBResource resource)
            {
                resource.side.resourceEntities.Add(resource);
                resource.NotifyCreateResource();
            }
            else if (entity is SBNexus nexus)
            {
                lastNexusIds[entity.side.id] = id;
                nexus.side.Nexus = nexus;
                NotifyEvent(nexus.CreateNexusEvent());
            }

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

        private static ulong CollisionPairKey(int aId, int bId)
        {
            uint low = (uint)Math.Min(aId, bId);
            uint high = (uint)Math.Max(aId, bId);
            return ((ulong)low << 32) | high;
        }

        /// <summary>
        /// The seeded left/right preference for a colliding pair remains stable
        /// across ticks and solver passes. Using an independent PCG stream keyed
        /// by the pair avoids consuming the simulation RNG used by DScript.
        /// </summary>
        private int GetCollisionSlideDirection(ulong pairKey)
        {
            if (!collisionSlideDirections.TryGetValue(pairKey, out int direction))
            {
                ulong pairSeed = unchecked(seed ^ (pairKey * 0x9E3779B97F4A7C15UL));
                var pairRandom = new DeterministicRandom(pairSeed);
                direction = pairRandom.NextInt(0, 2) == 0 ? -1 : 1;
                collisionSlideDirections.Add(pairKey, direction);
            }
            return direction;
        }

        /// <summary>
        /// Checks whether the clearance needed to pass on a given side would
        /// leave the world. Test the full contact radius here, NOT the tiny slide
        /// step: waiting until the unit reaches the wall can trap it permanently.
        /// </summary>
        private int CountBlockedSidewaysMoves(SBEntity a, SBEntity b, FixedVector2 sideways)
        {
            if (a.CollisionPriority > b.CollisionPriority)
                return WouldLeaveWorld(a, -sideways) ? 1 : 0;
            if (b.CollisionPriority > a.CollisionPriority)
                return WouldLeaveWorld(b, sideways) ? 1 : 0;

            int blocked = 0;
            if (WouldLeaveWorld(a, -sideways / 2)) blocked++;
            if (WouldLeaveWorld(b, sideways / 2)) blocked++;
            return blocked;
        }

        private bool WouldLeaveWorld(SBEntity entity, FixedVector2 displacement)
        {
            FixedVector2 pos = entity.Position + displacement;
            Fixed radius = entity.Radius;
            return pos.X - radius < 0 || pos.X + radius > dimensions.X ||
                pos.Y - radius < 0 || pos.Y + radius > dimensions.Y;
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

            // Preserve the exact normal correction and add a tiny sideways one.
            // The signed tangent is chosen ONCE per entity pair: rerolling each
            // tick would cancel the slide and leave centered routes stuck.
            Fixed tangentStep = Fixed.Min(CollisionSlideStep, overlap / 4);
            FixedVector2 tangent = normalDistance.Rotate90();
            ulong pairKey = CollisionPairKey(a.id, b.id);
            int side = GetCollisionSlideDirection(pairKey);

            // Prefer the side with enough room for the entire contact radius.
            // A 0.015-unit probe alone would only notice the wall when it is too
            // late to pass the obstacle. The actual displacement stays tiny.
            FixedVector2 passage = tangent * (minDistance * side);
            if (CountBlockedSidewaysMoves(a, b, passage) >
                CountBlockedSidewaysMoves(a, b, -passage))
            {
                side = -side;
                collisionSlideDirections[pairKey] = side;
            }
            FixedVector2 sideways = tangent * (tangentStep * side);
            FixedVector2 correction = normalDistance * overlap + sideways;

            // Preserve the original collision-priority convention: higher
            // priority yields, equal priorities split the total correction.
            if (a.CollisionPriority > b.CollisionPriority)
            {
                a.Position -= correction;
                EnsureWorldLimits(a);
            }
            else if (b.CollisionPriority > a.CollisionPriority)
            {
                b.Position += correction;
                EnsureWorldLimits(b);
            }
            else
            {
                a.Position -= correction / 2;
                b.Position += correction / 2;
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
