# Directiva Code UI v1.6

Resizable panes, indentation guides and execution-state abstraction.

Changes:

- The Output panel's upper border is now a clearer 6 px draggable resize handle.
- The script explorer now has a draggable right-side divider.
  - Minimum width: 180 px
  - Maximum width: 520 px
- Added visual indentation guides to the code editor.
  - Guides are a separate visual layer and never modify `.dscript` text.
  - They follow vertical/horizontal editor scrolling.
  - A guide is drawn for every occupied 4-space indentation depth.
- Added `IScriptExecutionStateProvider`.
- Added `ScriptExecutionState` (`Stopped`, `Running`, `Paused`).
- Added `NoOpExecutionStateProvider`, which always reports `Stopped`.
- Top-bar execution controls now react to execution state:
  - Stopped: Stop disabled, Pause disabled, Continue enabled when a script is open.
  - Running: Stop enabled, Pause enabled, Continue disabled.
  - Paused: Stop enabled, Pause disabled, Continue enabled.
- Step / Step In / Step Out remain disabled in v1.
