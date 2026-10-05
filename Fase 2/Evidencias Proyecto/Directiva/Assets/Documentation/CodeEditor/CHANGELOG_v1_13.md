# Directiva Code UI v1.13

Delayed localized help tooltips.

Changes:

- Added `Tooltip Delay Seconds` to `DirectivaCodeUIController` (default 1.2 s).
- Added one centralized `HoverTooltipController` for the whole code UI.
- Symbol controls show localized help after the pointer remains over them.
- Disabled symbol controls also receive tooltips because hover detection is
  centralized by screen bounds instead of depending on button events.
- Covered controls include:
  - Create Script / Create Folder
  - Save / Revert (top bar and dirty-script quick actions)
  - Stop / Pause / Continue
  - Step / Step In / Step Out
  - Back / Options
  - Expand / Collapse folder
  - Expand / Collapse Output
  - Clear Output
- Tooltip text is resolved at display time, so runtime locale changes are respected.
- Native immediate Unity tooltips are disabled for registered controls.
- Tooltip placement prefers below-right of the pointer, flips left/up near edges,
  then hard-clamps with an 8 px margin.
- Tooltip max width is also constrained to the actual UI width.
- Changing `Tooltip Delay Seconds` during Play Mode applies immediately.

No storage, execution, refactoring or editor-metric behavior changed.
