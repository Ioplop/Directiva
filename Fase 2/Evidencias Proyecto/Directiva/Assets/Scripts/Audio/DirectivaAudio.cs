using UnityEngine;
using UnityEngine.Audio;

namespace Directiva.Settings
{
    public enum DirectivaAudioBus { Simulation, Music, UI }

    /// <summary>
    /// All gameplay emitters route through DirectivaMixer. SettingsManager owns
    /// the saved percentages; SettingsRuntimeApplier applies them after Awake.
    /// The mixer asset lives under Resources/Audio so scene assets need no GUID
    /// references and the effect prefabs can keep their individual sound settings.
    /// </summary>
    public static class DirectivaAudio
    {
        private const string MixerPath = "Audio/DirectivaMixer";
        private const float MinimumDecibels = -80f;
        private static AudioMixer mixer;
        private static bool triedLoading;
        private static bool warnedMissing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            mixer = null;
            triedLoading = false;
            warnedMissing = false;
        }

        public static float ToDecibels(int percentage)
        {
            if (percentage <= 0) return MinimumDecibels;
            return Mathf.Max(MinimumDecibels,
                20f * Mathf.Log10(Mathf.Clamp(percentage, 0, 100) / 100f));
        }

        private static AudioMixer GetMixer()
        {
            if (!triedLoading)
            {
                triedLoading = true;
                mixer = Resources.Load<AudioMixer>(MixerPath);
                if (mixer == null && !warnedMissing)
                {
                    warnedMissing = true;
                    Debug.LogWarning("[Directiva Audio] DirectivaMixer.mixer is missing; " +
                        "check Assets/Resources/Audio before building the game.");
                }
            }
            return mixer;
        }

        public static AudioMixerGroup GetGroup(DirectivaAudioBus bus)
        {
            AudioMixer audioMixer = GetMixer();
            if (audioMixer == null) return null;
            string groupName = bus == DirectivaAudioBus.Simulation ? "Simulation" :
                bus == DirectivaAudioBus.Music ? "Music" : "UI";
            AudioMixerGroup[] groups = audioMixer.FindMatchingGroups(groupName);
            foreach (AudioMixerGroup group in groups)
                if (group.name == groupName) return group;
            return null;
        }

        public static void Route(AudioSource source, DirectivaAudioBus bus)
        {
            if (source == null) return;
            AudioMixerGroup group = GetGroup(bus);
            if (group != null)
                source.outputAudioMixerGroup = group;
        }

        /// <summary>
        /// Call from Start or a settings-change callback, never from Awake.
        /// Unity's exposed AudioMixer volume controls use decibels, not percentages.
        /// </summary>
        public static void ApplyVolumes(AudioSettings audio)
        {
            AudioMixer audioMixer = GetMixer();
            if (audioMixer == null || audio == null) return;
            ApplyParameter(audioMixer, "MasterVolume", audio.masterVolume);
            ApplyParameter(audioMixer, "SimulationVolume", audio.simulationVolume);
            ApplyParameter(audioMixer, "MusicVolume", audio.musicVolume);
            ApplyParameter(audioMixer, "UIVolume", audio.uiVolume);
        }

        private static void ApplyParameter(AudioMixer audioMixer, string parameter, int volume)
        {
            if (!audioMixer.SetFloat(parameter, ToDecibels(volume)))
                Debug.LogWarning("[Directiva Audio] Missing exposed mixer parameter: " + parameter);
        }
    }
}
