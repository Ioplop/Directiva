# Directiva Code UI v1.10

Direct FontAsset metrics.

Changes:

- The editor now reads indentation width directly from the active `FontAsset`.
- The horizontal unit is the space character's `GlyphMetrics.horizontalAdvance`.
- The line height is read directly from `FontAsset.faceInfo.lineHeight`.
- Both metrics are scaled from the FontAsset sampling point size to the current
  editor font size using:
  `currentFontSize / faceInfo.pointSize * faceInfo.scale`.
- Changing `Normal Font` therefore automatically changes indentation-guide
  spacing and line height without hard-coded character-width assumptions.
- The hidden UI Toolkit measurement label remains only as a safety fallback if:
  - no FontAsset is assigned,
  - the FontAsset has invalid face metrics,
  - or the FontAsset does not contain a usable space glyph.
- Padding and `Indent Guide Fine Offset Px` remain fixed pixel-space layout values
  and do not scale with zoom.

No storage, localization, execution or refactoring behavior changed.
