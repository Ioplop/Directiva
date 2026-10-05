# Directiva Code UI v1.8

Editor tuning and zoom.

Changes:

- Added Inspector field `Editor Font Size`.
- Added Inspector field `Indent Guide Offset Columns`.
- Guide correction is measured in monospaced character columns instead of pixels,
  so it scales naturally with font zoom.
- Default guide correction is +2 columns, matching the visual offset observed in v1.7.2.
- `Ctrl + mouse wheel` now zooms the code editor:
  - wheel up: +1 px
  - wheel down: -1 px
  - range: 6–48 px
- Font zoom recalculates:
  - actual character width,
  - line height,
  - text origin,
  - indentation guides,
  - gutter rows,
  - line numbers,
  - breakpoint symbol size.
- Runtime zoom updates the `Editor Font Size` field visible in the Inspector.
- `OnValidate` applies font-size and guide-offset changes live during Play Mode.

No storage, language, refactoring or execution behavior changed.
