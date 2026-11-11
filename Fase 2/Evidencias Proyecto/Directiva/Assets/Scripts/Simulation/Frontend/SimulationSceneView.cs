using System;
using System.Collections.Generic;
using DMath;
using DSEvents;
using DSimulation;
using UnityEngine;

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// A read-only Unity projection of DSimulation's event stream. It never issues
    /// orders or changes the authoritative world. One call to ConsumeEvents per
    /// simulation tick produces one interpolation target per surviving entity.
    /// </summary>
    public sealed class SimulationSceneView : MonoBehaviour
    {
        [Header("Entity prefabs")]
        [SerializeField] private VisualEntity unitPrefab;
        [SerializeField] private VisualEntity nexusPrefab;
        [SerializeField] private VisualEntity resourcePrefab;

        [Header("Effect prefabs")]
        [SerializeField] private AttackAligner attackPrefab;
        [SerializeField] private GatherAligner gatherPrefab;

        [Header("World projection")]
        [Tooltip("Center of the simulated X/Z rectangle. Usually WorldScene / Floor.")]
        [SerializeField] private Transform worldCenter;
        [SerializeField] private Transform entityContainer;
        [SerializeField] private Transform effectContainer;

        private readonly Dictionary<int, EntityVisual> entities = new Dictionary<int, EntityVisual>();
        private readonly Dictionary<int, ActiveGather> activeGather = new Dictionary<int, ActiveGather>();
        private Simulation simulation;
        private int worldWidth;
        private int worldHeight;
        private float playbackSpeed;

        private sealed class EntityVisual
        {
            internal VisualEntity View;
            internal SimulationTransformInterpolator Interpolator;
            internal FixedVector2 Position;
            internal Fixed Rotation;
        }

        private sealed class ActiveGather
        {
            internal int ResourceId;
            internal GatherAligner Effect;
        }

        public int VisualEntityCount => entities.Count;
        public int ActiveGatherCount => activeGather.Count;

        /// <summary>
        /// Attach an already started world and rebuild a visual snapshot. Past events
        /// are not replayed: the view can attach after simulation has already begun.
        /// The caller must then call ConsumeEvents after every Orchestrator.Update().
        /// </summary>
        public void Attach(Simulation world, int width, int height)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Clear();
            simulation = world;
            worldWidth = width;
            worldHeight = height;
            RebuildSnapshot();
        }

        private void RebuildSnapshot()
        {
            // An Orchestrator has already called Simulation.Start(). Never assume
            // its creation events are still buffered when a view attaches.
            for (int side = 1; side <= 2; side++)
            {
                SBSide faction = simulation.GetSide(side);
                if (faction.Nexus != null && faction.Nexus.alive)
                {
                    SBNexus nexus = faction.Nexus;
                    Spawn(nexus.id, side, SimulationVisualKind.Nexus, nexusPrefab, nexus.Position, nexus.Rotation);
                }
                foreach (SBUnit unit in faction.Units)
                    if (unit.alive)
                        Spawn(unit.id, side, SimulationVisualKind.Unit, unitPrefab, unit.Position, unit.Rotation);
            }
            foreach (SBResource resource in simulation.GetSide(0).ResourceEntities)
                if (resource.alive)
                    Spawn(resource.id, 0, SimulationVisualKind.Resource, resourcePrefab, resource.Position, resource.Rotation);

            // Previously generated events describe past changes already reflected
            // by the snapshot. In particular, do not replay obsolete attack effects.
            while (simulation.TryConsumeEvent(out DSEvents.Event ignored)) { }

            // An attached mid-match view also needs the effects for ongoing gathers.
            for (int side = 1; side <= 2; side++)
                foreach (SBUnit unit in simulation.GetSide(side).Units)
                    if (unit.alive && unit.IsGathering && unit.actionTarget is SBResource attachedDeposit)
                        BeginGather(unit.id, attachedDeposit.id);
        }

        public void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = Mathf.Max(0f, speed);
            foreach (EntityVisual entity in entities.Values)
                entity.Interpolator.SetPlaybackSpeed(playbackSpeed);
        }

        /// <summary>Consume events and submit exactly one new transform target per entity.</summary>
        public void ConsumeEvents()
        {
            if (simulation == null) return;

            while (simulation.TryConsumeEvent(out DSEvents.Event message))
            {
                switch (message)
                {
                    case EventCreateNexus createdNexus:
                        Spawn(createdNexus.id, createdNexus.side, SimulationVisualKind.Nexus, nexusPrefab, createdNexus.position, 0);
                        break;
                    case EventCreateUnit createdUnit:
                        Spawn(createdUnit.id, createdUnit.side, SimulationVisualKind.Unit, unitPrefab, createdUnit.position, createdUnit.rotation);
                        break;
                    case EventCreateResource createdResource:
                        Spawn(createdResource.id, 0, SimulationVisualKind.Resource, resourcePrefab, createdResource.position, 0);
                        break;
                    case EventPosition movedEvent:
                        if (entities.TryGetValue(movedEvent.id, out EntityVisual moved))
                            moved.Position = movedEvent.position;
                        break;
                    case EventRotation rotatedEvent:
                        if (entities.TryGetValue(rotatedEvent.id, out EntityVisual rotated))
                            rotated.Rotation = rotatedEvent.rotation;
                        break;
                    case EventAttack attackEvent:
                        SpawnAttack(attackEvent.attackerId, attackEvent.targetId);
                        break;
                    case EventGather gatherEvent:
                        BeginGather(gatherEvent.gethererId, gatherEvent.resourceId);
                        break;
                    case EventStopGather stoppedEvent:
                        EndGather(stoppedEvent.gethererId);
                        break;
                    case EventDeath deathEvent:
                        RemoveEntity(deathEvent.id);
                        break;
                    // Health, shields, cargo, resource amounts and time have no
                    // visual consumers in this first frontend. They remain backend events.
                }
            }

            // Position and rotation arrive as separate events. Submitting here avoids
            // restarting interpolation twice for the same logical simulation tick.
            foreach (EntityVisual entity in entities.Values)
                entity.Interpolator.ReceiveState(ToUnity(entity.Position, entity.View.transform.position.y),
                    ToUnityRotation(entity.Rotation));
        }

        public bool TryGetVisual(int entityId, out VisualEntity visual)
        {
            if (entities.TryGetValue(entityId, out EntityVisual entity) && entity.View != null)
            {
                visual = entity.View;
                return true;
            }
            visual = null;
            return false;
        }

        private void Spawn(int id, int side, SimulationVisualKind kind, VisualEntity prefab, FixedVector2 position, Fixed rotation)
        {
            if (prefab == null)
                throw new InvalidOperationException("A required simulation entity prefab is not assigned.");
            if (entities.ContainsKey(id))
                throw new InvalidOperationException("Duplicate visual entity ID " + id);

            Vector3 worldPosition = ToUnity(position, prefab.transform.position.y);
            Quaternion worldRotation = ToUnityRotation(rotation);
            VisualEntity visual = Instantiate(prefab, worldPosition, worldRotation,
                entityContainer != null ? entityContainer : transform);
            visual.gameObject.name = prefab.gameObject.name + " #" + id;
            // Metadata is attached only to the runtime instance. Colliders on any
            // mesh child can now be resolved by the generic world pointer controller.
            var identity = visual.GetComponent<SimulationEntityIdentity>();
            if (identity == null) identity = visual.gameObject.AddComponent<SimulationEntityIdentity>();
            identity.Initialize(id, side, kind);
            if (side != 0 && visual.Side != null)
                visual.Side.SetSide(side);

            SimulationTransformInterpolator interpolator = visual.GetComponent<SimulationTransformInterpolator>();
            if (interpolator == null)
                throw new InvalidOperationException(prefab.name + " requires SimulationTransformInterpolator.");
            interpolator.SnapTo(worldPosition, worldRotation);
            interpolator.SetPlaybackSpeed(playbackSpeed);
            entities.Add(id, new EntityVisual
            {
                View = visual,
                Interpolator = interpolator,
                Position = position,
                Rotation = rotation
            });
        }

        private void SpawnAttack(int attackerId, int targetId)
        {
            if (attackPrefab == null) return;
            if (!TryGetVisual(attackerId, out VisualEntity attacker) ||
                !TryGetVisual(targetId, out VisualEntity target)) return;

            // The prefab owns playback and eventual destruction.
            AttackAligner effect = Instantiate(attackPrefab,
                effectContainer != null ? effectContainer : transform);
            effect.Align(attacker.NozzlePosition, target.CenterPosition);
        }

        private void BeginGather(int unitId, int resourceId)
        {
            if (activeGather.TryGetValue(unitId, out ActiveGather existing))
            {
                if (existing.ResourceId == resourceId && existing.Effect != null) return;
                EndGather(unitId);
            }
            if (gatherPrefab == null ||
                !TryGetVisual(unitId, out VisualEntity gatherer) ||
                !TryGetVisual(resourceId, out VisualEntity resource)) return;

            GatherAligner effect = Instantiate(gatherPrefab,
                effectContainer != null ? effectContainer : transform);
            activeGather.Add(unitId, new ActiveGather { ResourceId = resourceId, Effect = effect });
            effect.SetAlignment(gatherer.Nozzle, resource.Center);
        }

        private void EndGather(int unitId)
        {
            if (!activeGather.TryGetValue(unitId, out ActiveGather gather)) return;
            activeGather.Remove(unitId);
            if (gather.Effect != null)
                gather.Effect.End();
        }

        private void RemoveEntity(int entityId)
        {
            // A dying resource can also terminate another entity's gather effect.
            var affected = new List<int>();
            foreach (KeyValuePair<int, ActiveGather> item in activeGather)
                if (item.Key == entityId || item.Value.ResourceId == entityId)
                    affected.Add(item.Key);
            foreach (int unitId in affected) EndGather(unitId);

            if (!entities.TryGetValue(entityId, out EntityVisual entity)) return;
            entities.Remove(entityId);
            if (entity.View != null)
                entity.View.Die();
        }

        /// <summary>Clear the old match without producing death effects.</summary>
        public void Clear()
        {
            simulation = null;
            foreach (ActiveGather gather in activeGather.Values)
                if (gather.Effect != null) Destroy(gather.Effect.gameObject);
            activeGather.Clear();
            foreach (EntityVisual entity in entities.Values)
                if (entity.View != null) Destroy(entity.View.gameObject);
            entities.Clear();

            // Remove transient attack effects as well when a match is stopped/restarted.
            Transform parent = effectContainer != null ? effectContainer : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private Vector3 ToUnity(FixedVector2 point, float preservedHeight)
        {
            Vector3 center = worldCenter != null ? worldCenter.position : Vector3.zero;
            float scale = Constants.FixedScale;
            return new Vector3(center.x - worldWidth * 0.5f + point.X.RawValue / scale,
                preservedHeight,
                center.z - worldHeight * 0.5f + point.Y.RawValue / scale);
        }

        // Simulation angle 0 points along +X; Unity yaw 0 points along +Z.
        private static Quaternion ToUnityRotation(Fixed degrees) =>
            Quaternion.Euler(0f, 90f - degrees.RawValue / (float)Constants.FixedScale, 0f);

        private void OnDestroy() => Clear();
    }
}
