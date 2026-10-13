# Directiva - Preferences and persistence (v1)

This version introduces the persistence and UI binding layer, **not real changes to Unity subsystems**.
The menu displays and saves preferences but deliberately does **not** call `Screen.SetResolution`,
`Screen.fullScreen`, `Application.runInBackground`, `Cursor.lockState`, AudioMixer, or localization APIs.

## Installation

Extract the incremental ZIP at the project root, keeping its `Assets/` prefix. No scene needs to be
recreated, no GameObject or prefab needs to be added, and existing UnityEvents and menu sprites remain.
The ZIP includes the current LevelMenuEntry class, because the uploaded full-assets snapshot predates
the immediately preceding level-requirements update. It is safe to overwrite that class if you already
installed the preceding patch.

## Files and architecture

- `Assets/Scripts/Settings/GameSettings.cs`: serializable DTO, nested classes and named constants.
- `Assets/Scripts/Settings/SettingsStorage.cs`: **abstract base class** providing the shared JSON serializer/deserializer and backup recovery protocol. Subclasses supply raw read, backup read and write.
- `Assets/Scripts/Settings/JsonFileSettingsStorage.cs`: pretty-printed UTF-8 `settings.json` on desktop, temporary write, backup and damaged-file preservation.
- `Assets/Scripts/Settings/PlayerPrefsSettingsStorage.cs`: same JSON schema stored with `PlayerPrefs` for WebGL (browser storage; it does not create a normal accessible JSON file).
- `Assets/Scripts/Settings/SettingsManager.cs`: static, lazy-loaded service; validation, default creation, persistence, snapshot reads, mutators, and change notifications.
- `Assets/Scripts/UI/MainMenu/MainMenuController.cs`: menu binding, visual controls and resolution confirmation dialog.
- `Assets/Resources/Styles/MainMenu/MainMenu.uss`: confirmation dialog styling.

On normal desktop builds, the file is at `Path.Combine(Application.persistentDataPath, "settings.json")`.
Use `SettingsManager.DesktopSettingsPath` to inspect the exact directory for your build. Unity chooses
different persistent-data directories for Windows, Linux, macOS, etc.

## JSON example

The actual resolution is automatically initialized to the display's current resolution; the following
numbers are only an example:

```json
{
  "version": 1,
  "general": {
    "language": "es",
    "simulateWhenInactive": false
  },
  "audio": {
    "masterVolume": 100,
    "musicVolume": 50,
    "uiVolume": 100,
    "simulationVolume": 50
  },
  "graphics": {
    "fullscreen": true,
    "resolutionWidth": 1920,
    "resolutionHeight": 1080
  },
  "controls": {
    "mouseCapture": "FullscreenOnly"
  }
}
```

- Language: `"es"` or `"en"`.
- Mouse capture: `"Always"`, `"FullscreenOnly"`, or `"Never"`.
- All volume values are integer percentages from 0 to 100.
- Initial settings: Spanish, inactive simulation disabled, audio 100/50/100/50, full-screen **preference** on, detected desktop display size, and mouse capture only in full screen.
- The OS/Unity player setting still decides the actual launch display mode for now.

## UI behavior

Options is still one scrollable page, now with these sections in order:
1. General: language, simulate when inactive.
2. Sonido: general volume, music, UI, game/simulation.
3. Gráficos: full screen preference, resolution.
4. Controles: mouse capture.

All non-resolution changes are written immediately. Changes to resolution **only stage a selection** and
show a modal with a 15-second default countdown. Confirm saves the selected resolution. Revert, timeout,
closing the scene, or pressing Stop in the Editor keeps the previous setting. This is a UI-only preview:
there is **no actual resolution switch or real display rollback** until the graphics applier is implemented.

The resolution dropdown is generated from `Screen.resolutions`, with duplicate refresh-rate variants removed.
It also includes `Screen.currentResolution` (the active desktop resolution). Platforms with no reported modes
show only the detected resolution instead of arbitrary high-resolution options.
If a previously stored size is unavailable (e.g. monitor changed), the service resets it to the detected
native size and persists the repaired value.

## Using settings from other components

```csharp
using Directiva.Settings;
using UnityEngine;

public sealed class SettingsReaderExample : MonoBehaviour
{
    private void Start()
    {
        // Lazy initialization: can be called from ANY scene.
        GameSettings snapshot = SettingsManager.Current;
        Debug.Log($"Music: {snapshot.audio.musicVolume}%, language: {snapshot.general.language}");
        // Changing snapshot does NOT change the service. Use setters instead:
        SettingsManager.SetMusicVolume(35);
    }

    private void OnEnable() => SettingsManager.SettingsChanged += OnSettingsChanged;
    private void OnDisable() => SettingsManager.SettingsChanged -= OnSettingsChanged;

    private void OnSettingsChanged(GameSettings snapshot)
    {
        // React here only when the real application layer is implemented.
    }
}
```

For custom persistence before the first `Initialize()` call, derive from `SettingsStorage` and call
`SettingsManager.UseStorage(customBackend)`. The built-in selection already switches to PlayerPrefs
for `UNITY_WEBGL && !UNITY_EDITOR`, while the Editor and desktop use JSON files.

## Recovery and compatibility

- First launch, no file: create settings from the active display and write JSON.
- Valid file: read, retain missing-field defaults and validate values.
- Invalid values: normalize (language, capture policy and audio), reset an unsupported resolution to the detected current display, then save repaired preferences.
- Corrupt primary JSON: attempt `settings.json.bak` first; when a replacement is saved the corrupt primary is preserved alongside the file as `settings.json.corrupt-<UTC timestamp>`.
- Corrupt primary and backup: regenerate safe defaults, retaining the corrupt primary on desktop.
- Each subsequent successful save preserves the previously valid primary as `.bak`.
- Newer file `version`: use in-memory defaults **without overwriting the unknown format**, and log a warning; manual replacement or an updated game is required for persistent edits.
- Saving failure: in-memory values still update, with a console warning, but the disk data may remain old.

`JsonUtility` supports JSON but not JSON comments; edit with a normal JSON editor and keep the syntax valid.

## Suggested manual verification in Unity

1. Play MainMenu for the first time. Check JSON was created under `Application.persistentDataPath`.
2. Edit language, inactive simulation, sound, fullscreen preference and mouse capture; stop and re-enter Play to confirm restoration.
3. Select a new resolution, do **not** confirm, and wait for timeout: the JSON must retain the old resolution.
4. Confirm another resolution: the JSON must change but **the display must not**.
5. Hand-edit `musicVolume` to `500`, run again, and confirm it clamps to `100`.
6. Hand-edit an unsupported resolution, run again, and confirm a native supported one is stored.
7. Corrupt `settings.json`, run again, check backup recovery and presence of a `.corrupt-...` file.
8. Ensure levels, sprites, scrollbars, events and the 3D menu scene behave as before.

**Testing note:** this patch was prepared by inspecting the provided assets and verified statically, but
it was not built or run in the Unity Editor in this environment.
