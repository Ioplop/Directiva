# Directiva Code UI v1.9

Deterministic editor layout pass.

- Removed the zoom-scaled indentation correction in character columns.
- Added explicit Inspector parameters for real editor padding:
  - left, top, right and bottom, all in pixels.
- Added `Indent Guide Fine Offset Px`, also fixed in pixels.
- Default guide fine offset is 0 px.
- The configured padding is applied directly to the UI Toolkit text input and is also the single source of truth for guide origin calculations.
- Text origin no longer depends on the internal `TextElement` renderer.
- The origin is calculated from the actual text input bounds + border + configured padding, in editor content coordinates.
- Added an invisible zero-padding metrics probe using the same FontAsset and font size as the editor.
- Character width and line height are measured from that probe, so padding/borders cannot contaminate font metrics.
- Ctrl+wheel still changes only font size; pixel padding and guide fine offset remain constant.
- Removed redundant hard-coded editor font sizes and paddings from USS so the controller is the authoritative source.
