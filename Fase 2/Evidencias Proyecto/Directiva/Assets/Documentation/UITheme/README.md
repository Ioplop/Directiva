# Directiva - Shared UI Theme (icons and typography)

This update builds on the **Simulation + Editor integrated** differential.
It does **not** recreate either scene, change existing GameObjects, or reset scene UnityEvents.

## Where to configure it

1. In Unity, choose **Directiva > UI > Select Shared Theme**.
2. Edit `Assets/Resources/UI/DirectivaUITheme.asset` in the Inspector.
3. Assign sprites under **Icons** and UI Toolkit `FontAsset` (SDF) assets under **Typography**.
4. Re-enter Play Mode or reload a scene to rebuild dynamically created UI elements.

The shared resource is loaded automatically when the `uiTheme` reference is left empty on:

- `MainMenuController`
- `SimulationUIController`
- `DirectivaCodeUIController` (standalone or embedded)

You may assign a **different DirectivaUITheme asset** to a particular controller to override the shared default. When the simulation controller has an explicit theme, it passes it to the hosted code editor before mounting it, keeping them consistent.

**No icon glyph fallback:** an unassigned sprite means an empty icon. Buttons remain clickable and have tooltips where applicable. You must populate the icons you need before shipping. Ordinary *textual labels* such as `Menú principal`, `Editor`, a level name or a file name are unchanged.

## Fonts

- **Interface Font**: menus, HUD, explorer, output, controls, tooltips and dialogs. `None` keeps the current USS/Unity font.
- **Code Font**: the actual editable text field and gutter. Use a **monospaced SDF FontAsset** so the indentation guides and cursor stay accurate. If `None`, the code editor preserves the `normalFont` reference already stored on its controller.
- **Code Bold Font**: reserved for future syntax highlighting / bold spans (not yet used by the existing plain TextField). Existing `boldFont` is preserved if this is `None`.

Font *sizes* are still controlled by the existing editor, explorer and output zoom settings.

## Sprite entries

| Section | Sprite fields | Used by |
| --- | --- | --- |
| Simulation | Play, Stop, Pause, Slow Motion, Double Speed, Quintuple Speed | Top simulation bar (visual + UnityEvents only) |
| Panels | Open Editor, Minimize, Maximize, Restore, Arrow Up/Down/Left/Right | Docked/fullscreen editor and editor explorer/output collapse |
| Code | Save, Revert, Add Script, Add Folder, Script, Folder, Step, Step In, Step Out, Breakpoint, Back, Options | Code editor toolbar, tree and breakpoint gutter |
| Checkboxes | Checked Box, Unchecked Box | Console filters and DIL debug toggle |
| Levels | Earned Star, Unearned Star, Required Star, Locked Level | Level selection (Locked Level reserved) |

To reuse one sprite in several places, drag the same sprite into several fields.

### Existing level sprites: compatibility and migration

Sprites you previously assigned directly to `MainMenuController` are **kept serialized** and remain in use only when their new shared theme slot is empty. This prevents existing finished star icons from suddenly disappearing. No fallback **character** is ever used.

Optional one-time migration:

1. Open the scene containing `MainMenuController`.
2. Choose **Directiva > UI > Copy Existing Level Sprites To Theme**.
3. Check `DirectivaUITheme.asset`. Any empty icon slots are populated from your scene's existing stars (existing theme assignments are not overwritten).
4. From then on, edit the icons in the shared theme.

The old fields remain hidden solely to preserve the existing scene's serialized data. They do not overwrite theme icons.

## Window behavior

- The big `EDITOR` header and its *internal* Menú principal button are removed from the simulation editor pane.
- **Minimize**: always hides the editor completely, from *Docked* or *Fullscreen*.
- **Maximize**: Docked -> Fullscreen.
- **Restore**: Fullscreen -> Docked, preserving the earlier docked width.
- The **Menú principal** button belongs to the host, stays visible at the top right even in fullscreen, and is not adjacent to the pane controls.
- The standalone `BasicCoding` scene now creates its **own outer navigation** automatically and hides the code editor's internal navigation buttons; no scene modification is needed.

The simulation playback controls remain visual-only and still invoke their configured `UnityEvents`.

## What this update does not do

- It does not connect simulation buttons to actual simulation logic.
- It does not change the executable program or VM execution lifecycle.
- It does not enable live hot-reload of the theme while playing. Reopen a scene to pick up changes.

## Integration check

Open `MainMenu` then `Game` and verify the shared icon assignments render. Open the editor docked; expand, minimize from fullscreen, reopen, and restore fullscreen to docked. Open `BasicCoding` separately to confirm its outer Menu button works. Test checkboxes both checked and unchecked, tree folder toggles, and the code font's indentation guides. The code has been statically reviewed, but this package was not compiled or executed inside a Unity Editor environment.
