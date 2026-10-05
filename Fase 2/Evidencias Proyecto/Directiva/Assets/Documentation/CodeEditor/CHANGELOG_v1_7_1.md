# Directiva Code UI v1.7.1

Indentation-guide alignment hotfix.

Changes:

- The code editor no longer reconstructs the horizontal text origin from
  `unity-text-field__input` padding alone.
- `CodeEditorView` now locates the internal `TextElement` used by UI Toolkit
  and measures its actual runtime position relative to the editor surface.
- Scroll offset is accounted for so the measured origin remains content-space
  rather than viewport-space.
- A fallback uses the resolved input border + padding if Unity does not expose
  the internal `TextElement`.
- Indentation guides now use VS Code-style placement:
  - indentation level 1 guide: text column 0
  - level 2 guide: text column 4
  - level 3 guide: text column 8
  - etc.
- Removed the old half-character visual compensation.
- Gutter vertical origin now follows the same measured text origin.

No storage, language, execution, localization or refactoring behavior changed.
