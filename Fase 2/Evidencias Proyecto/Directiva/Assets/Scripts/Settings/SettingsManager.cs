using System;
using System.Collections.Generic;
using UnityEngine;

namespace Directiva.Settings
{
    /// <summary>
    /// Public, scene-independent access to stored preferences. Does not apply settings
    /// to AudioMixer, Screen, Application, Cursor, or localization services.
    /// </summary>
    public static class SettingsManager
    {
        private static SettingsStorage storage;
        private static GameSettings current;
        private static bool initialized;
        private static bool readOnlyFutureVersion;

        /// <summary>Raised whenever the in-memory settings change. Each listener gets a copy.</summary>
        public static event Action<GameSettings> SettingsChanged;

        // Also resets statics when entering Play with Domain Reload disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            storage = null;
            current = null;
            initialized = false;
            readOnlyFutureVersion = false;
            SettingsChanged = null;
        }

        /// <summary>Defensive snapshot. Change settings only through the Set* methods.</summary>
        public static GameSettings Current
        {
            get { Initialize(); return current.Copy(); }
        }

        /// <summary>Where desktop settings are stored (WebGL uses PlayerPrefs instead).</summary>
        public static string DesktopSettingsPath =>
            System.IO.Path.Combine(Application.persistentDataPath, "settings.json");

        /// <summary>
        /// Inject a custom storage backend before first initialization, e.g. for another platform.
        /// A subclass of SettingsStorage only has to implement JSON read/write operations.
        /// </summary>
        public static void UseStorage(SettingsStorage backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (initialized) throw new InvalidOperationException("Configure settings storage before Initialize().");
            storage = backend;
        }

        public static void Initialize()
        {
            if (initialized) return;
            if (storage == null)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                storage = new PlayerPrefsSettingsStorage();
#else
                storage = new JsonFileSettingsStorage();
#endif
            }

            Vector2Int native = NativeResolution();
            var defaults = GameSettings.CreateDefaults(native.x, native.y);
            var state = storage.Load(defaults, out GameSettings loaded);
            current = loaded;
            bool repaired = Normalize(current, native, GetSupportedResolutions());
            readOnlyFutureVersion = state == SettingsLoadResult.NewerVersion;
            initialized = true;

            if (readOnlyFutureVersion)
            {
                current = defaults;
                Debug.LogWarning("[Directiva Settings] Settings file belongs to a newer game version. " +
                    "Using safe defaults without overwriting that file.");
                return;
            }

            if (state == SettingsLoadResult.RecoveredFromBackup)
                Debug.LogWarning("[Directiva Settings] Recovered settings from backup.");
            else if (state == SettingsLoadResult.InvalidData)
                Debug.LogWarning("[Directiva Settings] Settings were damaged. Restoring defaults; " +
                    "the desktop backend preserves the damaged file for manual inspection.");

            if (state != SettingsLoadResult.Loaded || repaired)
                Persist();
        }

        /// <summary>Re-reads settings from storage. Suitable for future settings import/reload UI.</summary>
        public static void Reload()
        {
            Initialize();
            Vector2Int native = NativeResolution();
            var defaults = GameSettings.CreateDefaults(native.x, native.y);
            var result = storage.Load(defaults, out GameSettings loaded);
            if (result == SettingsLoadResult.NewerVersion)
            {
                readOnlyFutureVersion = true;
                current = defaults;
            }
            else
            {
                readOnlyFutureVersion = false;
                current = loaded;
                bool fixedValues = Normalize(current, native, GetSupportedResolutions());
                if (result != SettingsLoadResult.Loaded || fixedValues) Persist();
            }
            SettingsChanged?.Invoke(current.Copy());
        }

        /// <summary>Restores platform-specific defaults and saves them, unless a newer file is protected.</summary>
        public static void ResetToDefaults()
        {
            Initialize();
            var native = NativeResolution();
            current = GameSettings.CreateDefaults(native.x, native.y);
            Persist();
            SettingsChanged?.Invoke(current.Copy());
        }

        /// <summary>
        /// Returns unique sizes supported by the connected display (deduplicated across refresh rates).
        /// If the platform provides no modes, falls back to the detected native display size.
        /// </summary>
        public static List<Vector2Int> GetSupportedResolutions()
        {
            var choices = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            Resolution[] reportedModes = Screen.resolutions;
            if (reportedModes != null)
            {
                foreach (Resolution resolution in reportedModes)
                {
                    if (resolution.width <= 0 || resolution.height <= 0) continue;
                    var size = new Vector2Int(resolution.width, resolution.height);
                    if (seen.Add(size)) choices.Add(size);
                }
            }

            Vector2Int native = NativeResolution();
            if (seen.Add(native)) choices.Add(native);
            choices.Sort((a, b) =>
            {
                int byWidth = a.x.CompareTo(b.x);
                return byWidth != 0 ? byWidth : a.y.CompareTo(b.y);
            });
            return choices;
        }

        /// <summary>Revalidate the saved resolution if available modes change during a session.</summary>
        public static void RefreshDisplayModes()
        {
            Initialize();
            var next = current.Copy();
            if (!Normalize(next, NativeResolution(), GetSupportedResolutions())) return;
            current = next;
            Persist();
            SettingsChanged?.Invoke(current.Copy());
        }

        public static void SetLanguage(string language) => Change(next => next.general.language = language);
        public static void SetSimulateWhenInactive(bool value) => Change(next => next.general.simulateWhenInactive = value);
        public static void SetMasterVolume(int value) => Change(next => next.audio.masterVolume = value);
        public static void SetMusicVolume(int value) => Change(next => next.audio.musicVolume = value);
        public static void SetUiVolume(int value) => Change(next => next.audio.uiVolume = value);
        public static void SetSimulationVolume(int value) => Change(next => next.audio.simulationVolume = value);
        public static void SetFullscreen(bool value) => Change(next => next.graphics.fullscreen = value);
        public static void SetMouseCapture(string mode) => Change(next => next.controls.mouseCapture = mode);

        /// <summary>
        /// Commit a previously confirmed resolution. UI is responsible for its countdown.
        /// Returns false for modes not reported by this display.
        /// </summary>
        public static bool TrySetResolution(int width, int height)
        {
            var desired = new Vector2Int(width, height);
            if (!GetSupportedResolutions().Contains(desired)) return false;
            Change(next =>
            {
                next.graphics.resolutionWidth = width;
                next.graphics.resolutionHeight = height;
            });
            return true;
        }

        private static void Change(Action<GameSettings> edit)
        {
            Initialize();
            var next = current.Copy();
            edit(next);
            Normalize(next, NativeResolution(), GetSupportedResolutions());
            if (JsonUtility.ToJson(next) == JsonUtility.ToJson(current)) return;
            current = next;
            Persist();
            SettingsChanged?.Invoke(current.Copy());
        }

        private static void Persist()
        {
            if (readOnlyFutureVersion)
            {
                Debug.LogWarning("[Directiva Settings] Not saving: a newer settings format is present.");
                return;
            }
            if (!storage.Save(current))
                Debug.LogWarning("[Directiva Settings] Changes are in memory, but could not be saved.");
        }

        private static bool Normalize(GameSettings data, Vector2Int native, List<Vector2Int> supported)
        {
            string before = JsonUtility.ToJson(data);
            var defaults = GameSettings.CreateDefaults(native.x, native.y);
            if (data.general == null) data.general = defaults.general;
            if (data.audio == null) data.audio = defaults.audio;
            if (data.graphics == null) data.graphics = defaults.graphics;
            if (data.controls == null) data.controls = defaults.controls;

            data.version = GameSettings.CurrentVersion;
            if (string.Equals(data.general.language, "en", StringComparison.OrdinalIgnoreCase))
                data.general.language = "en";
            else
                data.general.language = "es";

            data.audio.masterVolume = Mathf.Clamp(data.audio.masterVolume, 0, 100);
            data.audio.musicVolume = Mathf.Clamp(data.audio.musicVolume, 0, 100);
            data.audio.uiVolume = Mathf.Clamp(data.audio.uiVolume, 0, 100);
            data.audio.simulationVolume = Mathf.Clamp(data.audio.simulationVolume, 0, 100);

            if (string.Equals(data.controls.mouseCapture, "Always", StringComparison.OrdinalIgnoreCase))
                data.controls.mouseCapture = "Always";
            else if (string.Equals(data.controls.mouseCapture, "Never", StringComparison.OrdinalIgnoreCase))
                data.controls.mouseCapture = "Never";
            else
                data.controls.mouseCapture = "FullscreenOnly";

            var currentSize = new Vector2Int(data.graphics.resolutionWidth, data.graphics.resolutionHeight);
            if (!supported.Contains(currentSize))
            {
                data.graphics.resolutionWidth = native.x;
                data.graphics.resolutionHeight = native.y;
            }

            return before != JsonUtility.ToJson(data);
        }

        private static Vector2Int NativeResolution()
        {
            Resolution display = Screen.currentResolution;
            if (display.width > 0 && display.height > 0)
                return new Vector2Int(display.width, display.height);
            if (Screen.width > 0 && Screen.height > 0)
                return new Vector2Int(Screen.width, Screen.height);
            return new Vector2Int(1280, 720);
        }
    }
}
