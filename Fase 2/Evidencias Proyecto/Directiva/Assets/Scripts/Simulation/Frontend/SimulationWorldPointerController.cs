using System;
using Directiva.SimulationUI;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Directiva.SimulationFrontend
{
    /// <summary>
    /// World-space input detection is independent from the action performed on
    /// a selected entity. UI Toolkit blocks raycasts only over actual controls.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class SimulationWorldPointerController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private SimulationUIController ui;
        [SerializeField] private LayerMask selectableLayers = ~0;
        [SerializeField, Min(1f)] private float maxDistance = 1000f;

        public event Action<SimulationEntityIdentity> HoverChanged;
        public event Action<SimulationEntityIdentity> EntityClicked;
        public event Action<SimulationEntityIdentity> SelectionChanged;

        public SimulationEntityIdentity Hovered { get; private set; }
        public SimulationEntityIdentity Selected { get; private set; }
        public Camera WorldCamera => worldCamera != null ? worldCamera : Camera.main;

        public void Initialize(SimulationUIController simulationUi, Camera camera = null)
        {
            ui = simulationUi;
            if (camera != null) worldCamera = camera;
        }

        private void Update()
        {
            if (Selected == null && !ReferenceEquals(Selected, null))
                SetSelected(null); // A previously selected visual was destroyed.
            if (!ReadMouse(out Vector2 screenPosition, out bool pressed)) return;

            bool overUI = ui != null && ui.IsPointerOverUI(screenPosition);
            var candidate = overUI ? null : RaycastEntity(screenPosition);
            if (!ReferenceEquals(Hovered, candidate))
            {
                Hovered = candidate;
                HoverChanged?.Invoke(candidate);
            }
            if (pressed && !overUI)
            {
                SetSelected(candidate);
                EntityClicked?.Invoke(candidate); // Null means a click on empty world.
            }
        }

        public void ClearSelection()
        {
            if (Hovered != null)
            {
                Hovered = null;
                HoverChanged?.Invoke(null);
            }
            SetSelected(null);
        }

        private void SetSelected(SimulationEntityIdentity entity)
        {
            if (ReferenceEquals(Selected, entity)) return;
            Selected = entity;
            SelectionChanged?.Invoke(entity);
        }

        private SimulationEntityIdentity RaycastEntity(Vector2 screenPosition)
        {
            Camera camera = WorldCamera;
            if (camera == null) return null;
            // Visuals are moved via Transform in LateUpdate, not Rigidbody.
            // Make raycasts see the most recent interpolated pose even while paused.
            Physics.SyncTransforms();
            var ray = camera.ScreenPointToRay(screenPosition);
            // RaycastAll allows an unselectable floor/effect to be in front of an entity.
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance,
                selectableLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                var identity = hit.collider.GetComponentInParent<SimulationEntityIdentity>();
                if (identity != null) return identity;
            }
            return null;
        }

        private static bool ReadMouse(out Vector2 screenPosition, out bool pressed)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            screenPosition = Input.mousePosition;
            pressed = Input.GetMouseButtonDown(0);
            return true;
#endif
            screenPosition = default;
            pressed = false;
            return false;
        }
    }
}
