using Directiva.Settings;
using UnityEngine;

/// <summary>
/// Keeps the prefab's pitch randomization and routes its existing 3D AudioSource
/// to the Simulation mixer bus (attack, gather and death effects).
/// </summary>
public class SoundPitchRandomizer : MonoBehaviour
{
    [SerializeField] private float randomMagnitude = 0.1f;
    [SerializeField] private AudioSource source;

    private void Awake()
    {
        if (source == null)
            source = GetComponent<AudioSource>();
        if (source == null) return;

        // Do not override the prefab's volume, distance attenuation or spatial blend.
        DirectivaAudio.Route(source, DirectivaAudioBus.Simulation);
        source.pitch *= 1.0f + Random.Range(-randomMagnitude, randomMagnitude);
    }
}
