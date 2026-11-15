using UnityEngine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// UI Toolkit can dispatch the character left by an OS dead key (acute,
    /// circumflex, diaeresis, etc.) separately from the Tab key which completed
    /// a suggestion. Tab itself does not produce source text, so ignore its
    /// trailing character-only events before they reach TextField's editor.
    ///
    /// Do not guess which physical key is a dead key: that depends on the
    /// keyboard layout. Once a new physical typing key is pressed, normal text
    /// input (including real accented characters and IME text) resumes.
    /// </summary>
    internal sealed class CodeEditorTabTextGuard
    {
        private bool _pendingTabCharacters;

        internal bool PendingTabCharacters => _pendingTabCharacters;

        internal void Arm() => _pendingTabCharacters = true;

        internal void Reset() => _pendingTabCharacters = false;

        /// <summary>
        /// Call in the trickle-down phase, before TextField handles text input.
        /// The guard survives Tab's KeyUp because some platforms deliver the
        /// stray character later. It expires at the next physical typing key.
        /// </summary>
        internal bool ShouldConsume(KeyCode keyCode, char character)
        {
            if (!_pendingTabCharacters)
                return false;

            if (keyCode == KeyCode.None)
                // Tab's own control character is handled by the normal Tab route.
                // Only reject printable text, which Tab cannot legitimately type.
                return !char.IsControl(character);

            if (keyCode != KeyCode.Tab && !IsModifierKey(keyCode))
                Reset();

            return false;
        }

        /// <summary>
        /// A dead key produces a physical KeyDown without any text yet. Keep the
        /// Tab preview open until a real edit arrives through ChangeEvent;
        /// only navigation/editing commands must close it on their key-down.
        /// </summary>
        internal static bool ShouldDismissPreviewForKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                case KeyCode.Backspace:
                case KeyCode.Delete:
                case KeyCode.Insert:
                case KeyCode.F2:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsModifierKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand:
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows:
                case KeyCode.AltGr:
                    return true;
                default:
                    return false;
            }
        }
    }
}
