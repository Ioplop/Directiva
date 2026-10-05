# Directiva Code UI v1.1

Compatibility patch after first Unity import.

Changes:

- Replaced `Convert.ToHexString` with a SHA-256 hexadecimal conversion compatible with Unity's .NET profile.
- Removed obsolete `EventBase.PreventDefault()` calls.
- Keyboard actions that intentionally override the control's normal behavior now use `StopImmediatePropagation()`.
- Replaced obsolete `VisualElement.transform.position` gutter scrolling with `style.translate`.
- No intended behavior or architecture changes from v1.
