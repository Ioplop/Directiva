using UnityEngine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Retains the visible code viewport across UI Toolkit TextField focus changes.
    /// A focus transition is different from a deliberate scroll or source jump:
    /// only the former is allowed to restore a previously visible offset.
    /// This class has no UI event dependencies so the transition rules can be tested.
    /// </summary>
    internal sealed class EditorFocusScrollState
    {
        private Vector2 _savedOffset;
        private bool _hasSavedOffset;
        private bool _pending;
        private int _revision;

        internal bool HasSavedOffset => _hasSavedOffset;

        internal void ResetForDocument()
        {
            _revision++;
            _pending = false;
            _hasSavedOffset = false;
            _savedOffset = Vector2.zero;
        }

        // Capture on FocusOut's trickle phase, before the native TextField can
        // scroll its caret into view. A second focus event in the same transition
        // must not replace the saved offset with Unity's temporary 0,0.
        internal int BeginFocusLoss(Vector2 currentOffset)
        {
            if (!_pending || !_hasSavedOffset)
                _savedOffset = currentOffset;
            _hasSavedOffset = true;
            _pending = true;
            return ++_revision;
        }

        // FocusIn may occur after native UI Toolkit has already reset its
        // ScrollView. Do not capture that temporary offset over the saved one.
        internal int BeginFocusReturn()
        {
            // First focus of a newly opened file should let UI Toolkit place
            // the caret normally. There is nothing to restore until a previous
            // blur or an intentional scroll recorded a viewport position.
            if (!_hasSavedOffset)
                return -1;
            _pending = true;
            return ++_revision;
        }

        internal bool TryGetRestoration(int revision, out Vector2 offset)
        {
            offset = _savedOffset;
            return _pending && _hasSavedOffset && revision == _revision;
        }

        internal void FinishRestoration(int revision)
        {
            if (revision == _revision)
                _pending = false;
        }

        internal void NoteDeliberateScroll(Vector2 currentOffset)
        {
            _savedOffset = currentOffset;
            _hasSavedOffset = true;
            _pending = false;
            _revision++;
        }

        internal void CancelForNavigation()
        {
            _hasSavedOffset = false;
            _pending = false;
            _revision++;
        }
    }
}
