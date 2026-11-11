using UnityEngine;

/// <summary>
/// Smooths visual positions and rotations received from a fixed-tick simulation.
/// Never changes the GameObject's Y position; only X and Z are synchronized.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)] // Update visuals before effects align in LateUpdate.
public sealed class SimulationTransformInterpolator : MonoBehaviour
{
    [Header("Simulation interpolation")]
    [SerializeField, Min(1f)] private float simulationTicksPerSecond = 50f;
    [SerializeField] private bool synchronizeRotation = true;

    private Vector3 previousPosition;
    private Vector3 targetPosition;
    private Quaternion previousRotation;
    private Quaternion targetRotation;
    private float elapsedTime;
    private float playbackSpeed = 1f;
    private bool initialized;

    public bool SynchronizeRotation
    {
        get => synchronizeRotation;
        set => synchronizeRotation = value;
    }

    /// <summary>
    /// Sets the initial state, or teleports an entity without interpolation.
    /// The current Y position is always preserved.
    /// </summary>
    public void SnapTo(Vector3 position, Quaternion rotation)
    {
        previousPosition = targetPosition = position;
        previousRotation = targetRotation = rotation;
        elapsedTime = 1f / Mathf.Max(1f, simulationTicksPerSecond);
        initialized = true;
        Apply(1f);
    }

    /// <summary>
    /// Receives the new authoritative state after one simulation tick.
    /// Position is given in Unity world coordinates (X/Z are the simulation plane).
    /// </summary>
    public void ReceiveState(Vector3 position, Quaternion rotation)
    {
        if (!initialized)
        {
            SnapTo(position, rotation);
            return;
        }

        previousPosition = targetPosition;
        previousRotation = targetRotation;
        targetPosition = position;
        targetRotation = rotation;
        elapsedTime = 0f;
    }

    /// <summary>
    /// Matches visual interpolation to simulation speed (0 = paused, 1 = normal, 2 = x2, etc.).
    /// </summary>
    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Max(0f, speed);
    }

    private void LateUpdate()
    {
        if (!initialized)
            return;

        // The synchronizer already applies speed via Time.timeScale for particles.
        // Drive interpolation from unscaled time to avoid applying the rate twice.
        elapsedTime += Time.unscaledDeltaTime * playbackSpeed;
        float alpha = Mathf.Clamp01(elapsedTime * Mathf.Max(1f, simulationTicksPerSecond));
        Apply(alpha);
    }

    private void Apply(float alpha)
    {
        Vector3 interpolated = Vector3.Lerp(previousPosition, targetPosition, alpha);
        Vector3 current = transform.position;

        // Preserve visual height, including any bobbing driven by Floater.
        transform.position = new Vector3(interpolated.x, current.y, interpolated.z);

        // When disabled, leave rotation fully to components such as Rotator.
        if (synchronizeRotation)
            transform.rotation = Quaternion.Slerp(previousRotation, targetRotation, alpha);
    }
}
