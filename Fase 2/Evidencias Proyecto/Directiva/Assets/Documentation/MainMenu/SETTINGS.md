# Directiva - Preferences, persistence and runtime application (v2)

Settings data and storage remain separate from the game/menu. Four settings now affect the Unity
player: resolution, fullscreen, simulation while unfocused and pointer confinement.
Language and sound volumes remain **stored only**, until their own subsystems are implemented.

## Installation

Install the **Settings Persistence** differential first if it is not already in your project.
Then extract this incremental ZIP at the project root, preserving its `Assets/` prefix.
No scene needs to be recreated, no GameObject or prefab needs to be added, and existing
UnityEvents and menu sprites remain unchanged.

## Files and architecture

- `Assets/Scripts/Settings/GameSettings.cs`: serializable DTO, nested classes and named constants.
- `Assets/Scripts/Settings/SettingsStorage.cs`: **abstract base class** providing the shared JSON serializer/deserializer and backup recovery protocol. Subclasses supply raw read, backup read and write.
- `Assets/Scripts/Settings/JsonFileSettingsStorage.cs`: pretty-printed UTF-8 `settings.json` on desktop, temporary write, backup and damaged-file preservation.
- `Assets/Scripts/Settings/PlayerPrefsSettingsStorage.cs`: same JSON schema stored with `PlayerPrefs` for WebGL (browser storage; it does not create a normal accessible JSON file).
- `Assets/Scripts/Settings/SettingsManager.cs`: static, lazy-loaded service; validation, default creation, persistence, snapshot reads, mutators, and change notifications (unchanged in this patch).
- `Assets/Scripts/Settings/SettingsRuntimeApplier.cs`: **new** persistent `MonoBehaviour` that listens to `SettingsChanged` and applies selected Unity APIs. It bootstraps automatically before the first scene, in menu and gameplay scenes alike.
- `Assets/Scripts/UI/MainMenu/MainMenuController.cs`: menu binding; now uses the runtime applier to preview and confirm real resolution changes.
- `Assets/Resources/Styles/MainMenu/MainMenu.uss`: the existing confirmation dialog styles (unchanged in this patch).

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
- Initial settings: Spanish, inactive simulation disabled, audio 100/50/100/50, full screen on, detected desktop display size, and pointer confinement only in fullscreen.
- These defaults are now applied at player startup; a valid saved configuration overrides them.

## UI behavior

Options is still one scrollable page, now with these sections in order:
1. General: language, simulate when inactive.
2. Sonido: general volume, music, UI, game/simulation.
3. Gráficos: full screen preference, resolution.
4. Controles: mouse capture.

All non-resolution changes are written immediately. Display and mouse/background preferences also
affect the running player immediately:

- `simulateWhenInactive` sets `Application.runInBackground`. Default `false`.
- `fullscreen` changes mode through `Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow/Windowed)`.
  Full screen here means **borderless fullscreen**; the OS desktop display mode remains native, while
  a lower selected resolution can change the game's render resolution.
- `mouseCapture` uses `Cursor.lockState = CursorLockMode.Confined` for `Always`, or only when
  `Screen.fullScreen` is actually true for `FullscreenOnly`; `Never` sets `None`. The cursor
  stays visible, unlike `CursorLockMode.Locked`. Confinement is lifted on lost focus/pause.
  Unity supports confined cursor on Windows/Linux standalone builds; the OS/editor can behave differently,
  and unsupported macOS/WebGL builds intentionally fall back to `None`.

A resolution choice now calls `Screen.SetResolution` **temporarily** and opens the existing
15-second confirmation dialog. Confirm writes the selected resolution into `SettingsManager` / JSON;
Revert or expiration re-applies the last saved resolution. The timeout uses real unscaled time and
is also checked by the persistent runtime component (not just the menu). Leaving the options
screen, disabling the menu, or loading another scene while a preview is pending reverts it.
If the application is suspended while unfocused, the deadline is checked again when it resumes.
A crash or forced exit during preview does not persist the proposed resolution, so the previously
saved configuration is applied again next launch.

Only display-related setting changes call `Screen.SetResolution`; changing audio/language does not
reconfigure the display. Language, audio sliders and their persistence work as before, but do not
yet affect localized text or actual audio output.

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
2. In a **desktop build**, toggle fullscreen; it should switch between a borderless fullscreen and a windowed game. Reenter the game to verify it persists.
3. Select a new resolution: the display should preview it. Do **not** confirm; after the timeout the saved resolution and actual display should be restored, with no JSON change.
4. Confirm another resolution: the live display changes and the JSON saves the new size.
5. Hand-edit `musicVolume` to `500`, run again, and confirm it clamps to `100`.
6. Hand-edit an unsupported resolution, run again, and confirm a native supported one is stored.
7. Corrupt `settings.json`, run again, check backup recovery and presence of a `.corrupt-...` file.
8. Toggle Simular en ventana inactiva, Alt+Tab, and observe whether the game keeps advancing only when enabled.
9. With two monitors, test mouse confinement in a Windows/Linux **standalone** build: Always within the focused window, only fullscreen when selected, or Never. Alt+Tab must release the cursor. Confined should not hide the pointer.
10. Change unrelated audio/language settings while fullscreen; confirm there is no new display mode transition.
11. Confirm levels, sprites, scrollbars, button events and the 3D menu scene behave as before.

**Platform note:** Fullscreen and the resolution API are not directly applied on WebGL because browsers
control canvas size and fullscreen permission separately. `Application.runInBackground` may also be
throttled by the browser. A future WebGL runtime applier can implement these semantics without changing
`GameSettings` or the storage base class. Desktop builds are the target of this patch.

**Testing note:** this patch was prepared by inspecting the provided assets and verified statically, but
it was not built or run in the Unity Editor in this environment.
