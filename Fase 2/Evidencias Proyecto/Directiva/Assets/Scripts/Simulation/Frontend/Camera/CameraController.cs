using System;
using Directiva.SimulationUI;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Local-only camera controls. Camera movement never changes the deterministic
/// simulation; the focus is clamped to the visible simulation's X/Z bounds.
///
/// Existing rigs can assign focusPoint -> pitchPivot -> cameraPosition -> Camera.
/// If no rig has been assigned, the controller creates one around the current
/// Camera at runtime, preserving its initial view. The synchronizer installs
/// this component automatically on Camera.main when no controller is present.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Transform focusPoint;
    [SerializeField] private Transform pitchPivot;
    [SerializeField] private Transform cameraPosition;
    [SerializeField] private new Camera camera;

    [Header("Zoom options")]
    [Tooltip("Closest zoom as a multiple of the camera's initial distance (0.2 = 20%).")]
    [SerializeField, Min(0.01f)] private float maxZoom = 0.2f;
    [Tooltip("Farthest zoom as a multiple of the camera's initial distance (1.5 = 150%).")]
    [SerializeField, Min(0.01f)] private float minZoom = 1.5f;
    [SerializeField, Min(0f)] private float zoomSpeed = 0.1f;

    [Header("Move options")]
    [SerializeField, Min(0f)] private float moveSpeed = 1f;

    [Header("Rotate options")]
    [SerializeField, Min(0f)] private float yawSpeed = 360f;
    [SerializeField, Min(0f)] private float pitchSpeed = 30f;
    [SerializeField, Range(0f, 90f)] private float maxPitch = 90f;
    [SerializeField, Range(0f, 90f)] private float minPitch = 30f;

    private Rect worldBounds;
    private bool hasBounds;
    private float initialDistance;
    private SimulationUIController ui;
    private bool middleDragging;
    private Vector2 previousPointerPosition;

    /// <summary>World-space focus position on the simulation's horizontal X/Z plane.</summary>
    public Vector2 FocusPosition => focusPoint == null ? Vector2.zero : Position;
    public Rect WorldBounds => worldBounds;

    private float Distance
    {
        get => cameraPosition != null ? Mathf.Max(0.01f, -cameraPosition.localPosition.z) : 0f;
        set
        {
            if (cameraPosition == null) return;
            // The original camera settings are zoom multipliers, not world units.
            // maxZoom = 0.2 means closer, minZoom = 1.5 means farther.
            float near = Mathf.Max(0.01f, initialDistance * Mathf.Min(minZoom, maxZoom));
            float far = Mathf.Max(near, initialDistance * Mathf.Max(minZoom, maxZoom));
            Vector3 offset = cameraPosition.localPosition;
            offset.z = -Mathf.Clamp(value, near, far);
            cameraPosition.localPosition = offset;
        }
    }

    private float Yaw
    {
        get => focusPoint.localEulerAngles.y;
        set
        {
            Vector3 rotation = focusPoint.localEulerAngles;
            rotation.y = Mathf.Repeat(value, 360f);
            focusPoint.localEulerAngles = rotation;
        }
    }

    private float Pitch
    {
        // Signed angle avoids Euler's wraparound when pitched near 0 degrees.
        get => Mathf.DeltaAngle(0f, pitchPivot.localEulerAngles.x);
        set
        {
            Vector3 rotation = pitchPivot.localEulerAngles;
            rotation.x = Mathf.Clamp(value, Mathf.Min(minPitch, maxPitch), Mathf.Max(minPitch, maxPitch));
            pitchPivot.localEulerAngles = rotation;
        }
    }

    private Vector2 Position
    {
        get => new Vector2(focusPoint.position.x, focusPoint.position.z);
        set
        {
            if (focusPoint == null) return;
            Vector2 clamped = hasBounds ? ClampToBounds(value, worldBounds) : value;
            focusPoint.position = new Vector3(clamped.x, focusPoint.position.y, clamped.y);
        }
    }

    public static Vector2 ClampToBounds(Vector2 point, Rect bounds) => new Vector2(
        Mathf.Clamp(point.x, bounds.xMin, bounds.xMax),
        Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));

    /// <summary>Called by the simulation's scene synchronizer (including while stopped).</summary>
    public void SetBounds(Rect bounds)
    {
        if (bounds.width <= 0f || bounds.height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Camera world bounds must have positive size.");
        worldBounds = bounds;
        hasBounds = true;
        if (focusPoint != null) Position = Position;
    }

    /// <summary>Use the same UI hit testing and text-focus rules as entity selection.</summary>
    public void SetUI(SimulationUIController simulationUi) => ui = simulationUi;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Never destroy the focus point: a second controller could otherwise
            // destroy the entire rig, including the active camera.
            Destroy(this);
            return;
        }
        Instance = this;
        EnsureRig();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void EnsureRig()
    {
        if (camera == null)
        {
            camera = GetComponent<Camera>();
            if (camera == null) camera = GetComponentInChildren<Camera>();
            if (camera == null) camera = Camera.main;
        }
        if (focusPoint != null && pitchPivot != null && cameraPosition != null)
        {
            initialDistance = Mathf.Max(0.01f, Mathf.Abs(cameraPosition.localPosition.z));
            return;
        }
        if (camera == null)
        {
            Debug.LogError("[Directiva] CameraController needs a Camera or a complete rig.", this);
            enabled = false;
            return;
        }

        // Build a focus/yaw -> pitch -> zoom hierarchy without scene edits or
        // script GUID references. Keep the original Main Camera and its components.
        Transform cameraTransform = camera.transform;
        Vector3 originalPosition = cameraTransform.position;
        Vector3 originalForward = cameraTransform.forward;
        Vector3 originalEuler = cameraTransform.rotation.eulerAngles;
        float distanceToGround = originalForward.y < -0.001f
            ? -originalPosition.y / originalForward.y : -1f;
        Vector3 target = distanceToGround > 0f
            ? originalPosition + originalForward * distanceToGround
            : new Vector3(originalPosition.x, 0f, originalPosition.z);
        target.y = 0f;
        float distance = Mathf.Max(0.1f, Vector3.Distance(originalPosition, target));

        focusPoint = new GameObject("Camera Focus (runtime)").transform;
        focusPoint.position = target;
        focusPoint.rotation = Quaternion.Euler(0f, originalEuler.y, 0f);
        pitchPivot = new GameObject("Camera Pitch Pivot").transform;
        pitchPivot.SetParent(focusPoint, false);
        pitchPivot.localRotation = Quaternion.Euler(
            Mathf.Clamp(Mathf.DeltaAngle(0f, originalEuler.x), minPitch, maxPitch), 0f, 0f);
        cameraPosition = new GameObject("Camera Zoom Position").transform;
        cameraPosition.SetParent(pitchPivot, false);
        cameraPosition.localPosition = new Vector3(0f, 0f, -distance);
        cameraTransform.SetParent(cameraPosition, false);
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;
        initialDistance = distance;
    }

    private void Update()
    {
        if (focusPoint == null || pitchPivot == null || cameraPosition == null || !hasBounds)
            return;
        if (!Application.isFocused)
        {
            middleDragging = false;
            return;
        }
        Rotate();
        Zoom();
        Move();
    }

    private bool PointerIsOverUI()
    {
        if (ui == null || !TryGetPointerPosition(out Vector2 position)) return false;
        return ui.IsPointerOverUI(position);
    }

    private bool CameraInputIsBlocked() =>
        (ui != null && ui.IsKeyboardCaptured()) || PointerIsOverUI();

    private void Move()
    {
        if (CameraInputIsBlocked() || ModifierKeyPressed()) return;
        Vector2 direction = ReadMovementKeys();
        if (direction == Vector2.zero) return;

        // Translate relative to yaw, not world north. Pitch never affects panning.
        Vector3 move = focusPoint.right * direction.x + focusPoint.forward * direction.y;
        Vector2 flat = Vector2.ClampMagnitude(new Vector2(move.x, move.z), 1f);
        float maxSpeed = Distance * moveSpeed;
        Position += flat * maxSpeed * Time.unscaledDeltaTime;
    }

    private void Rotate()
    {
        if (!MiddleMouseIsPressed() || CameraInputIsBlocked())
        {
            middleDragging = false;
            return;
        }
        if (!TryGetPointerPosition(out Vector2 position)) return;
        if (!middleDragging)
        {
            previousPointerPosition = position;
            middleDragging = true;
            return; // Never jump on the first frame of a drag.
        }
        Vector2 mouseDelta = position - previousPointerPosition;
        previousPointerPosition = position;

        // Mouse motion is already a per-frame delta. Convert it to viewport
        // fractions per second so yawSpeed/pitchSpeed retain their degrees/second
        // meaning and the explicit * deltaTime formula does not depend on FPS.
        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f) return;
        float horizontal = (mouseDelta.x / Mathf.Max(1f, Screen.width)) / deltaTime;
        float vertical = (mouseDelta.y / Mathf.Max(1f, Screen.height)) / deltaTime;
        Pitch -= vertical * pitchSpeed * deltaTime;
        Yaw += horizontal * yawSpeed * deltaTime;
    }

    private void Zoom()
    {
        if (CameraInputIsBlocked()) return;
        float wheel = ReadWheelUnits();
        if (Mathf.Approximately(wheel, 0f)) return;
        // A scroll wheel produces a delta, not a continuous axis: no deltaTime.
        // Positive scroll moves the camera closer to its focus.
        Distance *= Mathf.Exp(-Mathf.Clamp(wheel, -20f, 20f) * zoomSpeed);
    }

    private static Vector2 ReadMovementKeys()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return Vector2.zero;
        return new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
#elif ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
#else
        return Vector2.zero;
#endif
    }

    private static bool ModifierKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed ||
            k.leftAltKey.isPressed || k.rightAltKey.isPressed ||
            k.leftMetaKey.isPressed || k.rightMetaKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
            Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
            Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
#else
        return false;
#endif
    }

    private static bool TryGetPointerPosition(out Vector2 position)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        position = Input.mousePosition;
        return true;
#endif
        position = Vector2.zero;
        return false;
    }

    private static bool MiddleMouseIsPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.middleButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(2);
#else
        return false;
#endif
    }

    private static float ReadWheelUnits()
    {
#if ENABLE_INPUT_SYSTEM
        // Windows mice generally report 120 units per wheel detent.
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y;
#else
        return 0f;
#endif
    }
}
