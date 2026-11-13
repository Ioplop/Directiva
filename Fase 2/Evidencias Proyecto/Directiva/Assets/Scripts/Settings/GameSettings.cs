using System;
using UnityEngine;

namespace Directiva.Settings
{
    /// <summary>Stable string values used in the editable settings.json.</summary>
    public static class SettingsValues
    {
        public const string Spanish = "es";
        public const string English = "en";
        public const string MouseAlways = "Always";
        public const string MouseFullscreenOnly = "FullscreenOnly";
        public const string MouseNever = "Never";
    }

    /// <summary>Serializable, human-editable settings data; contains no game-engine side effects.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public GeneralSettings general = new GeneralSettings();
        public AudioSettings audio = new AudioSettings();
        public GraphicsSettings graphics = new GraphicsSettings();
        public ControlSettings controls = new ControlSettings();
        public EditorSettings editor = new EditorSettings();

        public static GameSettings CreateDefaults(int screenWidth, int screenHeight)
        {
            var result = new GameSettings();
            result.graphics.resolutionWidth = screenWidth;
            result.graphics.resolutionHeight = screenHeight;
            return result;
        }

        public GameSettings Copy()
        {
            // The data is deliberately plain so each reader receives a defensive snapshot.
            return new GameSettings
            {
                version = version,
                general = new GeneralSettings
                {
                    language = general.language,
                    simulateWhenInactive = general.simulateWhenInactive
                },
                audio = new AudioSettings
                {
                    masterVolume = audio.masterVolume,
                    musicVolume = audio.musicVolume,
                    uiVolume = audio.uiVolume,
                    simulationVolume = audio.simulationVolume
                },
                graphics = new GraphicsSettings
                {
                    fullscreen = graphics.fullscreen,
                    resolutionWidth = graphics.resolutionWidth,
                    resolutionHeight = graphics.resolutionHeight
                },
                controls = new ControlSettings
                {
                    mouseCapture = controls.mouseCapture
                },
                editor = new EditorSettings
                {
                    showCompletion = editor.showCompletion,
                    showLiveErrors = editor.showLiveErrors,
                    showErrorSuggestions = editor.showErrorSuggestions,
                    liveErrorDelaySeconds = editor.liveErrorDelaySeconds
                }
            };
        }
    }

    [Serializable]
    public sealed class GeneralSettings
    {
        // Explicit strings, rather than numeric enums, keep the JSON easy to edit manually.
        public string language = "es";
        public bool simulateWhenInactive = false;
    }

    [Serializable]
    public sealed class AudioSettings
    {
        public int masterVolume = 100;
        public int musicVolume = 50;
        public int uiVolume = 100;
        public int simulationVolume = 50;
    }

    [Serializable]
    public sealed class GraphicsSettings
    {
        public bool fullscreen = true;
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
    }

    /// <summary>Editor assistance is optional and never changes compilation or execution.</summary>
    [Serializable]
    public sealed class EditorSettings
    {
        public bool showCompletion = true;
        public bool showLiveErrors = true;
        // Suggestions are displayed only while hovering an error; opt-in by default.
        public bool showErrorSuggestions = false;
        public float liveErrorDelaySeconds = 0.5f;
    }

    [Serializable]
    public sealed class ControlSettings
    {
        // "Always", "FullscreenOnly" or "Never".
        public string mouseCapture = "FullscreenOnly";
    }
}
