using UnityEngine;

namespace Directiva.Settings
{
    /// <summary>
    /// Applies only the supported runtime settings: display mode/resolution,
    /// background execution, and mouse confinement. Exists once across all scenes.
    /// Audio and language are intentionally not applied here.
    /// </summary>
    public sealed class SettingsRuntimeApplier : MonoBehaviour
    {
        private static SettingsRuntimeApplier instance;

        private bool hasFocus = true;
        private bool isPaused;
        private bool lastFullscreen;
        private bool previewActive;
        private Vector2Int previewResolution;
        private float previewDeadline;
        private bool warnedUnsupportedCursor;
        private bool displayInitialized;
        private int appliedWidth;
        private int appliedHeight;
        private bool appliedFullscreen;
        private bool backgroundInitialized;
        private bool appliedBackground;
        private string appliedMouseCapture;

        /// <summary>True while a temporary display mode awaits confirmation.</summary>
        public static bool IsResolutionPreviewActive => instance != null && instance.previewActive;

        /// <summary>Remaining seconds for the pending display change (unscaled real time).</summary>
        public static float ResolutionPreviewSecondsRemaining =>
            IsResolutionPreviewActive
                ? Mathf.Max(0f, instance.previewDeadline - Time.realtimeSinceStartup)
                : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateOnStartup()
        {
            if (instance != null) return;

            var gameObject = new GameObject("Directiva Settings Runtime");
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<SettingsRuntimeApplier>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            hasFocus = Application.isFocused;
            lastFullscreen = Screen.fullScreen;
            SettingsManager.SettingsChanged += ApplySettings;
            ApplySettings(SettingsManager.Current);
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            SettingsManager.SettingsChanged -= ApplySettings;
            CancelPreviewInternal(false);
            Cursor.lockState = CursorLockMode.None;
            instance = null;
        }

        private void Update()
        {
            // This remains active if the menu is hidden, destroyed, or another scene loads.
            // After an Alt+Tab, a paused player will check the deadline on resuming.
            if (previewActive && Time.realtimeSinceStartup >= previewDeadline)
                CancelPreviewInternal(true);

            // Screen.SetResolution changes mode at the end of the frame, not immediately.
            bool fullscreen = Screen.fullScreen;
            if (fullscreen != lastFullscreen)
            {
                lastFullscreen = fullscreen;
                ApplyMouseCapture(SettingsManager.Current);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (instance == this) ApplyMouseCapture(SettingsManager.Current);
        }

        private void OnApplicationPause(bool paused)
        {
            isPaused = paused;
            if (instance == this) ApplyMouseCapture(SettingsManager.Current);
        }

        private void ApplySettings(GameSettings settings)
        {
            // An external change to persisted preferences takes precedence over an
            // outstanding preview. The last confirmed settings are the source of truth.
            previewActive = false;

            if (!backgroundInitialized || appliedBackground != settings.general.simulateWhenInactive)
            {
                Application.runInBackground = settings.general.simulateWhenInactive;
                appliedBackground = settings.general.simulateWhenInactive;
                backgroundInitialized = true;
            }

            bool fullscreenSettingChanged = !displayInitialized ||
                appliedFullscreen != settings.graphics.fullscreen;
            ApplyDisplay(settings.graphics.resolutionWidth, settings.graphics.resolutionHeight,
                settings.graphics.fullscreen);

            // Do not reapply display settings for audio sliders or other unrelated
            // changes. In particular, Screen.SetResolution can cause a visible flash.
            if (fullscreenSettingChanged || appliedMouseCapture != settings.controls.mouseCapture)
                ApplyMouseCapture(settings);
            appliedMouseCapture = settings.controls.mouseCapture;
        }

        private void ApplyDisplay(int width, int height, bool fullscreen)
        {
            if (displayInitialized && appliedWidth == width && appliedHeight == height &&
                appliedFullscreen == fullscreen)
                return;

            SetDisplay(width, height, fullscreen);
            appliedWidth = width;
            appliedHeight = height;
            appliedFullscreen = fullscreen;
            displayInitialized = true;
        }

        private static void SetDisplay(int width, int height, bool fullscreen)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser fullscreen is user-gesture driven, and SetResolution is not a
            // reliable way to control the browser canvas. A WebGL applier can replace this.
            return;
#else
            var mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.SetResolution(width, height, mode);
#endif
        }

        /// <summary>
        /// Temporarily applies a supported resolution without persisting it.
        /// The active mode is restored if ConfirmResolutionPreview is not called in time.
        /// </summary>
        public static bool TryPreviewResolution(int width, int height, float seconds)
        {
            if (instance == null) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            var target = new Vector2Int(width, height);
            if (!SettingsManager.GetSupportedResolutions().Contains(target)) return false;

            // Repeated previews replace each other, always reverting to the saved mode.
            instance.previewResolution = target;
            instance.previewDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, seconds);
            instance.previewActive = true;
            instance.ApplyDisplay(width, height, SettingsManager.Current.graphics.fullscreen);
            return true;
#endif
        }

        /// <summary>Commits the previewed resolution to SettingsManager and persistent storage.</summary>
        public static bool ConfirmResolutionPreview()
        {
            if (!IsResolutionPreviewActive) return false;
            if (Time.realtimeSinceStartup >= instance.previewDeadline)
            {
                instance.CancelPreviewInternal(true);
                return false;
            }

            Vector2Int selected = instance.previewResolution;
            instance.previewActive = false;
            if (SettingsManager.TrySetResolution(selected.x, selected.y)) return true;

            instance.RestoreSavedDisplay();
            return false;
        }

        /// <summary>Aborts any pending preview and restores the last confirmed resolution.</summary>
        public static void CancelResolutionPreview()
        {
            if (instance != null) instance.CancelPreviewInternal(true);
        }

        private void CancelPreviewInternal(bool restoreDisplay)
        {
            if (!previewActive) return;
            previewActive = false;
            if (restoreDisplay) RestoreSavedDisplay();
        }

        private void RestoreSavedDisplay()
        {
            GameSettings settings = SettingsManager.Current;
            ApplyDisplay(settings.graphics.resolutionWidth, settings.graphics.resolutionHeight,
                settings.graphics.fullscreen);
        }

        private void ApplyMouseCapture(GameSettings settings)
        {
            // Never trap the OS cursor while another application has focus.
            bool shouldConfine = hasFocus && !isPaused &&
                (settings.controls.mouseCapture == SettingsValues.MouseAlways ||
                 (settings.controls.mouseCapture == SettingsValues.MouseFullscreenOnly && Screen.fullScreen));

#if (UNITY_STANDALONE_OSX || UNITY_WEBGL) && !UNITY_EDITOR
            // CursorLockMode.Confined is supported by Unity on Windows/Linux standalone,
            // not consistently on macOS or browser builds.
            if (shouldConfine && !warnedUnsupportedCursor)
            {
                Debug.LogWarning("[Directiva Settings] Mouse confinement is not supported on this platform.");
                warnedUnsupportedCursor = true;
            }
            shouldConfine = false;
#endif
            var desired = shouldConfine ? CursorLockMode.Confined : CursorLockMode.None;
            if (Cursor.lockState != desired) Cursor.lockState = desired;
        }
    }
}
