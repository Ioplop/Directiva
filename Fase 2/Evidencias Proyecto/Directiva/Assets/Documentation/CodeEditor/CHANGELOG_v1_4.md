# Directiva Code UI v1.4

Bug-fix release that includes all v1.3 root/explorer changes.

Changes:

- Output filters now render as explicit checkbox + label pairs, removing the visual mismatch.
- Expanding Output now rebuilds its visual list from the buffered OutputService entries.
  Messages received while collapsed therefore appear correctly after expanding.
- Back and Options now use compact symbols (`←` and `⚙`) with localized tooltips.
- Inline rename text now uses a dark field, bright text, visible caret and visible selection.
- Fixed inline rename being destroyed immediately by a tree rebuild.
- Enter now commits an inline rename; Escape cancels it.
- Retains all v1.3 behavior:
  - explicit `/` root,
  - click empty explorer space to target root,
  - drop on empty explorer space to move to root,
  - root cannot be renamed/moved/duplicated/deleted,
  - creation target separated from active script.

Refactoring note:

- The current `NoOpRefactorer` deliberately reports that no refactor is required.
- Therefore rename/move operations do not show refactor prompts yet.
- Real import/refactor prompts will begin once DirectivaCode + a real IRefactorer can detect affected references.
