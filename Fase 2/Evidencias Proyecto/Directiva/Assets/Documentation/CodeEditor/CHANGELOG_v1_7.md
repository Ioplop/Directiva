# Directiva Code UI v1.7

Monospaced font integration and measured editor metrics.

Changes:

- Added two serialized `FontAsset` references to `DirectivaCodeUIController`:
  - `Normal Font`
  - `Bold Font`
- `CodeEditorView` receives both references.
- The normal font is explicitly applied to the code editor and inherited by its gutter.
- The bold font is wired into the editor and reserved for future semantic/syntax rendering.
- Removed the fixed 8.4 px character-width assumption.
- Character width is measured from the actual assigned font at runtime.
- Line height is measured from the actual assigned font at runtime.
- Text input padding is read from the resolved UI Toolkit input style.
- Indentation guides now use those measured values.
- Gutter rows and breakpoint rows use the same measured line height.
- No language, storage, execution or refactoring behavior changes.
