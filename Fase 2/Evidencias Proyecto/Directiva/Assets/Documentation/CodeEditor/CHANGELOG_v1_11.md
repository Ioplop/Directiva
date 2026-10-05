# Directiva Code UI v1.11

Independent panel zoom.

Changes:

- Added `Explorer Font Size` to `DirectivaCodeUIController`.
- Added `Output Font Size` to `DirectivaCodeUIController`.
- `Ctrl + mouse wheel` over the Script Explorer changes only Explorer zoom.
- `Ctrl + mouse wheel` over Output changes only Output zoom.
- Editor zoom remains fully independent.
- All three zoom systems use:
  - minimum: 6 px
  - maximum: 48 px
  - step: 1 px
- Runtime zoom updates the corresponding Inspector field during Play Mode.
- Changing Explorer / Output font-size fields in the Inspector during Play Mode
  updates the corresponding panel immediately.
- Normal wheel scrolling is unchanged when Ctrl is not held.

No storage, language, execution, refactoring or editor-metric behavior changed.
