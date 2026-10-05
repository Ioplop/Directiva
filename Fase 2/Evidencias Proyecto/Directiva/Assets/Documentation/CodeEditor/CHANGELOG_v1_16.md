# Directiva Code UI v1.16

Thin unified scrollbar skin.

Changes:

- Added a dedicated `directiva-scroll-view` class to:
  - Script Explorer
  - Output console
  - Code Editor internal ScrollView
- Vertical scrollbar rail reduced to 8 px.
- Horizontal scrollbar rail reduced to 8 px.
- Removed Unity's default up/down/left/right arrow buttons.
- Replaced default bright scrollbar chrome with Directiva's dark interface tones.
- Added a narrow neutral thumb with a brighter hover/active state.
- Removed the default bright dragger border.
- Styling is scoped to Directiva scroll views and will not alter unrelated
  ScrollViews elsewhere in the game.

No scrolling behavior, storage, localization, execution or refactoring logic changed.
