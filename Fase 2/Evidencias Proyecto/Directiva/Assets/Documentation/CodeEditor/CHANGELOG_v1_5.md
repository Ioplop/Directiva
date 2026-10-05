# Directiva Code UI v1.5

Visual control-symbol and localization patch.

Changes:

- Options now uses a text-presentation gear (`⚙︎`) and is explicitly styled white.
- Back is a red `←`.
- Execution controls are now symbols:
  - Stop: red `■`
  - Pause: yellow `Ⅱ`
  - Continue: green `▶`
- Future debugging controls are now blue symbols:
  - Step: `↷`
  - Step In: `↓`
  - Step Out: `↑`
- All symbol-only buttons keep localized tooltips.
- Added `DirectivaLocalizedException`.
- Known storage/filesystem validation failures now carry localization keys instead of English user-facing messages.
- Added Spanish and English Storage locale files.
- `SafeAction` now localizes structured storage errors directly.
- Example fixed:
  `Folder already exists: New Folder`
  now becomes:
  `Ya existe una carpeta llamada New Folder.`
