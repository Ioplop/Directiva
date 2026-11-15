using UnityEngine;

/// <summary>
/// Applies random pictch change to an audio source
/// </summary>
public class SoundPitchRandomizer : MonoBehaviour
{
    [SerializeField] private float randomMagnitude = 0.1f;
    [SerializeField] private AudioSource source;

    private void Awake()
    {
        source.pitch *= 1.0f + Random.Range(-randomMagnitude, randomMagnitude);
    }
}
