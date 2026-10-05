# Directiva Code UI v1.3

Explorer root and creation-target patch.

Changes:

- Added an explicit, always-visible root row named `/`.
- Root `/` can be selected as the target for new scripts/folders.
- Root `/` accepts drag & drop to move scripts/folders out of nested folders.
- Dropping on unused explorer space also moves the dragged item to root `/`.
- Clicking unused explorer space selects root `/` as the creation target.
- Root `/` cannot be dragged, renamed, duplicated, deleted or opened through a context menu.
- Separated the currently active script from the folder selected as the creation target.
- Selecting a folder makes it the creation target without changing the active script.
- Selecting a script keeps it active and sets its parent folder as the creation target.
- Added a visual highlight for the current creation/drop target.
