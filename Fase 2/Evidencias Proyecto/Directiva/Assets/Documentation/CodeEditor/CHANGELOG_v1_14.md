# Directiva Code UI v1.14

First-open layout, inline rename and editor scrolling fixes.

Changes:

- Indentation guides now listen to the real editable input's
  `GeometryChangedEvent`.
- Opening the first document schedules a post-layout metric/origin refresh.
  This fixes guides being wrong until the first zoom operation.
- The internal code-editor `ScrollView` is forced to shrink to the editor
  viewport (`flexGrow = 1`, `minHeight = 0`) and keeps an automatic vertical
  scrollbar.
- Editor stack and TextField also explicitly use `min-height: 0`, preventing a
  long document from expanding the input instead of scrolling.
- Inline rename fields now remove default vertical margins/padding and vertically
  center the text.
- Inline rename's internal input receives a controlled height and padding.
- Enter/Escape rename handling now runs in `TrickleDown`, before the internal
  TextField can consume the first Enter.
- A single Enter now commits the rename again.

No storage, localization, execution or refactoring behavior changed.
