using System;
using System.Collections.Generic;
using DMath;
using DSEvents;
using DSimulation;
using UnityEngine;
using UnityEngine.UIElements;

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

        [Header("Entity status indicators")]
        [Tooltip("Optional camera override. Uses Camera.main when unset.")]
        [SerializeField] private Camera statusCamera;
        [SerializeField, Min(80f)] private float statusBarWidth = 124f;
        [Tooltip("World-space height above VisualEntity.Center.")]
        [SerializeField] private float statusBarVerticalOffset = 1f;

        private SimulationStatusOverlay statusOverlay;
        private readonly Dictionary<int, EntityVisual> entities = new Dictionary<int, EntityVisual>();
        private readonly Dictionary<int, ActiveGather> activeGather = new Dictionary<int, ActiveGather>();
        private Simulation simulation;
        private SimulationWorldPointerController pointerController;
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

        public Simulation CurrentWorld => simulation;
        public int VisualEntityCount => entities.Count;
        public int ActiveGatherCount => activeGather.Count;
        public int StatusIndicatorCount => statusOverlay != null ? statusOverlay.Count : 0;

        /// <summary>
        /// Attach an already started world and rebuild a visual snapshot. Past events
        /// are not replayed: the view can attach after simulation has already begun.
        /// The caller must then call ConsumeEvents after every Orchestrator.Update().
        /// </summary>
        public void Attach(Simulation world, int width, int height, UIDocument uiDocument = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Clear();
            simulation = world;
            worldWidth = width;
            worldHeight = height;
            // Use the SAME UIDocument as the main HUD: it renders above these bars
            // and its existing transparent panel remains click-through.
            if (uiDocument != null)
                statusOverlay = new SimulationStatusOverlay(uiDocument.rootVisualElement,
                    statusBarWidth, statusBarVerticalOffset);
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
                    Spawn(nexus.id, side, SimulationVisualKind.Nexus, nexusPrefab, nexus.Position, nexus.Rotation,
                        new SimulationStatusValues(nexus.Health, nexus.MaxHealth, nexus.Shields, nexus.MaxShields));
                }
                foreach (SBUnit unit in faction.Units)
                    if (unit.alive)
                        Spawn(unit.id, side, SimulationVisualKind.Unit, unitPrefab, unit.Position, unit.Rotation,
                            new SimulationStatusValues(unit.Health, unit.MaxHealth, unit.Shields, unit.MaxShields),
                            unit.Cargo, unit.MaxCargo);
            }
            foreach (SBResource resource in simulation.GetSide(0).ResourceEntities)
                if (resource.alive)
                    Spawn(resource.id, 0, SimulationVisualKind.Resource, resourcePrefab, resource.Position, resource.Rotation,
                        new SimulationStatusValues(resource.Health, resource.MaxHealth, resource.Shields, resource.MaxShields),
                        resource.Amount, resource.InitialAmount);

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
                        Spawn(createdNexus.id, createdNexus.side, SimulationVisualKind.Nexus, nexusPrefab, createdNexus.position, 0,
                            new SimulationStatusValues(createdNexus.maxHealth, createdNexus.maxHealth,
                                createdNexus.maxShields, createdNexus.maxShields));
                        break;
                    case EventCreateUnit createdUnit:
                        Spawn(createdUnit.id, createdUnit.side, SimulationVisualKind.Unit, unitPrefab, createdUnit.position, createdUnit.rotation,
                            new SimulationStatusValues(createdUnit.maxHealth, createdUnit.maxHealth,
                                createdUnit.maxShields, createdUnit.maxShields),
                            0, createdUnit.maxCargo);
                        break;
                    case EventCreateResource createdResource:
                        Spawn(createdResource.id, 0, SimulationVisualKind.Resource, resourcePrefab, createdResource.position, 0,
                            new SimulationStatusValues(createdResource.maxHealth, createdResource.maxHealth,
                                SConstants.ResourceShields, SConstants.ResourceShields),
                            createdResource.amount, createdResource.amount);
                        break;
                    case EventResources resourceChange:
                        statusOverlay?.SetResources(resourceChange.id, resourceChange.amount);
                        break;
                    case EventCargo cargoChange:
                        statusOverlay?.SetCargo(cargoChange.entityId, cargoChange.cargo);
                        break;
                    case EventPosition movedEvent:
                        if (entities.TryGetValue(movedEvent.id, out EntityVisual moved))
                            moved.Position = movedEvent.position;
                        break;
                    case EventRotation rotatedEvent:
                        if (entities.TryGetValue(rotatedEvent.id, out EntityVisual rotated))
                            rotated.Rotation = rotatedEvent.rotation;
                        break;
                    case EventHealth healthEvent:
                        statusOverlay?.SetHealth(healthEvent.id, healthEvent.health);
                        break;
                    case EventShields shieldsEvent:
                        statusOverlay?.SetShields(shieldsEvent.id, shieldsEvent.shields);
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
                    // Time is used by the backend but needs no visual bar.
                }
            }

            // Position and rotation arrive as separate events. Submitting here avoids
            // restarting interpolation twice for the same logical simulation tick.
            foreach (EntityVisual entity in entities.Values)
                entity.Interpolator.ReceiveState(ToUnity(entity.Position, entity.View.transform.position.y),
                    ToUnityRotation(entity.Rotation));
        }

        /// <summary>Show an error mark while this entity's VM is recovering.</summary>
        public void SetEntityFaulted(int entityId, bool faulted) =>
            statusOverlay?.SetFaulted(entityId, faulted);

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

        private void Spawn(int id, int side, SimulationVisualKind kind, VisualEntity prefab,
            FixedVector2 position, Fixed rotation, SimulationStatusValues status,
            int amount = 0, int capacity = 0)
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
            if (kind == SimulationVisualKind.Resource)
                statusOverlay?.TrackResource(id, visual.Center, status, amount, capacity);
            else if (kind == SimulationVisualKind.Unit)
                statusOverlay?.TrackUnit(id, visual.Center, status, amount, capacity);
            else
                statusOverlay?.TrackNexus(id, visual.Center, status);
        }

        private void LateUpdate()
        {
            if (statusOverlay == null) return;
            if (pointerController == null)
                pointerController = GetComponent<SimulationWorldPointerController>() ??
                    GetComponentInParent<SimulationWorldPointerController>() ??
                    UnityEngine.Object.FindFirstObjectByType<SimulationWorldPointerController>();
            statusOverlay.SetHoveredEntity(pointerController != null && pointerController.Hovered != null
                ? pointerController.Hovered.EntityId : 0);
            statusOverlay.SetSelectedEntity(pointerController != null && pointerController.Selected != null
                ? pointerController.Selected.EntityId : 0);

            // Construction has no per-tick event. Read the same public observer
            // that DScript uses, so the HUD follows the actual per-Nexus progress
            // and duration rather than reconstructing timing in Unity.
            if (simulation != null)
            {
                for (int side = 1; side <= 2; side++)
                {
                    SBNexus nexus = simulation.GetSide(side).Nexus;
                    if (nexus == null || !nexus.alive) continue;
                    Fixed? progress = simulation.Observer.GetBuildProgress(nexus.id);
                    Fixed duration = simulation.Observer.GetUnitBuildTime(nexus.id) ?? (Fixed)0;
                    statusOverlay.SetConstruction(nexus.id, progress, duration);
                }
            }
            statusOverlay.UpdatePositions(statusCamera != null ? statusCamera : Camera.main);
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
            statusOverlay?.Remove(entityId);
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
            statusOverlay?.Dispose();
            statusOverlay = null;

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
