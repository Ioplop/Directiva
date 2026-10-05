# Directiva Code UI v1.15

Deterministic editor scrolling.

Changes:

- The code editor now keeps its vertical scrollbar visible.
- Plain mouse-wheel input is handled explicitly by `CodeEditorView`.
- `Ctrl + wheel` continues to control editor zoom.
- The internal TextField ScrollView is explicitly constrained to the editor
  viewport instead of being allowed to grow with the whole document.
- The editor calculates document height from:
  - line count,
  - actual font line height,
  - configured top/bottom padding.
- That height is applied as the internal ScrollView content minimum height,
  guaranteeing a real vertical overflow range for long files.
- Document height is recalculated when:
  - text changes,
  - a document opens,
  - font size changes,
  - editor geometry changes,
  - editor padding changes.
- Added `min-height: 0` / flex shrinking across the full editor layout chain
  (`app-shell`, `main-area`, `right-area`, editor, stack and TextField).

Expected behavior:

- Long files show a vertical scrollbar.
- The scrollbar can be dragged.
- Mouse wheel scrolls vertically.
- Gutter line numbers and indentation guides remain synchronized with scrolling.
- Ctrl + wheel still zooms instead of scrolling.

No storage, localization, execution or refactoring behavior changed.
