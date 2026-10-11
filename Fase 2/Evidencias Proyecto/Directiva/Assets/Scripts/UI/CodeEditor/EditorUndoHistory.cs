using System;
using System.Collections.Generic;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Historial de snapshots de un documento del editor.
    /// Conserva texto, caret y seleccion, y agrupa escritura consecutiva en una sola accion.
    /// </summary>
    internal sealed class EditorUndoHistory
    {
        public readonly struct Snapshot
        {
            public string Text { get; }
            public int Cursor { get; }
            public int Select { get; }

            public Snapshot(string text, int cursor, int select)
            {
                Text = text ?? string.Empty;
                Cursor = EditorUndoHistory.Clamp(cursor, 0, Text.Length);
                Select = EditorUndoHistory.Clamp(select, 0, Text.Length);
            }
        }

        private readonly List<Snapshot> _states = new List<Snapshot>();
        private readonly int _capacity;
        private int _index = -1;
        private float _lastChangeTime = float.NegativeInfinity;
        private bool _lastChangeWasMergeable;

        public bool CanUndo => _index > 0;
        public bool CanRedo => _index >= 0 && _index < _states.Count - 1;
        public Snapshot Current => _states[_index];

        public EditorUndoHistory(string initialText, int cursor, int select, int capacity)
        {
            _capacity = Math.Max(2, capacity);
            _states.Add(new Snapshot(initialText, cursor, select));
            _index = 0;
        }

        public void UpdateCurrentSelection(int cursor, int select)
        {
            if (_index < 0)
                return;

            var current = _states[_index];
            _states[_index] = new Snapshot(current.Text, cursor, select);
        }

        public void Record(
            string text,
            int cursor,
            int select,
            bool forceBoundary,
            float now,
            float mergeWindowSeconds)
        {
            if (text == null)
                text = string.Empty;

            if (_index < 0)
            {
                _states.Add(new Snapshot(text, cursor, select));
                _index = 0;
                ResetMergeState();
                return;
            }

            var previous = _states[_index];
            if (string.Equals(previous.Text, text, StringComparison.Ordinal))
            {
                UpdateCurrentSelection(cursor, select);
                return;
            }

            if (_index < _states.Count - 1)
                _states.RemoveRange(_index + 1, _states.Count - _index - 1);

            var mergeable = !forceBoundary && IsMergeableTypingEdit(previous, text, cursor, select);
            var canMerge = mergeable &&
                           _lastChangeWasMergeable &&
                           _index == _states.Count - 1 &&
                           _states.Count > 1 &&
                           now - _lastChangeTime <= mergeWindowSeconds;

            var snapshot = new Snapshot(text, cursor, select);

            if (canMerge)
            {
                _states[_index] = snapshot;
            }
            else
            {
                _states.Add(snapshot);
                _index++;
                TrimToCapacity();
            }

            _lastChangeWasMergeable = mergeable;
            _lastChangeTime = now;
        }

        public bool TryUndo(int currentCursor, int currentSelect, out Snapshot snapshot)
        {
            UpdateCurrentSelection(currentCursor, currentSelect);

            if (!CanUndo)
            {
                snapshot = default;
                return false;
            }

            _index--;
            snapshot = _states[_index];
            ResetMergeState();
            return true;
        }

        public bool TryRedo(int currentCursor, int currentSelect, out Snapshot snapshot)
        {
            UpdateCurrentSelection(currentCursor, currentSelect);

            if (!CanRedo)
            {
                snapshot = default;
                return false;
            }

            _index++;
            snapshot = _states[_index];
            ResetMergeState();
            return true;
        }

        public EditorUndoHistoryData Export()
        {
            var data = new EditorUndoHistoryData
            {
                index = _index
            };

            foreach (var state in _states)
            {
                data.states.Add(new EditorUndoSnapshotData
                {
                    text = state.Text,
                    cursor = state.Cursor,
                    select = state.Select
                });
            }

            return data;
        }

        public static EditorUndoHistory Import(
            EditorUndoHistoryData data,
            string fallbackText,
            int capacity)
        {
            fallbackText ??= string.Empty;
            var history = new EditorUndoHistory(fallbackText, 0, 0, capacity);

            if (data?.states == null || data.states.Count == 0)
                return history;

            history._states.Clear();
            foreach (var state in data.states)
            {
                if (state == null)
                    continue;

                history._states.Add(new Snapshot(
                    state.text ?? string.Empty,
                    state.cursor,
                    state.select));
            }

            if (history._states.Count == 0)
            {
                history._states.Add(new Snapshot(fallbackText, 0, 0));
                history._index = 0;
                return history;
            }

            history._index = Clamp(data.index, 0, history._states.Count - 1);

            while (history._states.Count > history._capacity)
            {
                // Preserve the current state and as much surrounding history as possible.
                if (history._index > 0)
                {
                    history._states.RemoveAt(0);
                    history._index--;
                }
                else
                {
                    history._states.RemoveAt(history._states.Count - 1);
                }
            }

            history.ResetMergeState();
            return history;
        }

        public void BreakMergeChain()
        {
            ResetMergeState();
        }

        private void TrimToCapacity()
        {
            while (_states.Count > _capacity)
            {
                _states.RemoveAt(0);
                _index--;
            }
        }

        private void ResetMergeState()
        {
            _lastChangeWasMergeable = false;
            _lastChangeTime = float.NegativeInfinity;
        }

        private static bool IsMergeableTypingEdit(
            Snapshot previous,
            string nextText,
            int nextCursor,
            int nextSelect)
        {
            // Replacements of a selection, multiline changes and explicit editor operations
            // should remain individual undo actions. This path is for normal runs of typing,
            // Backspace and Delete only.
            if (previous.Cursor != previous.Select || nextCursor != nextSelect)
                return false;

            FindChangedRange(
                previous.Text,
                nextText,
                out var changeStart,
                out var removedLength,
                out var insertedLength);

            if (removedLength == 0 && insertedLength > 0)
            {
                if (ContainsNewline(nextText, changeStart, insertedLength))
                    return false;

                return changeStart == previous.Cursor;
            }

            if (insertedLength == 0 && removedLength > 0)
            {
                if (ContainsNewline(previous.Text, changeStart, removedLength))
                    return false;

                // Backspace removes immediately before the old caret; Delete removes
                // immediately after it. Both are natural consecutive edit runs.
                return changeStart + removedLength == previous.Cursor ||
                       changeStart == previous.Cursor;
            }

            return false;
        }

        private static void FindChangedRange(
            string before,
            string after,
            out int start,
            out int removedLength,
            out int insertedLength)
        {
            var beforeLength = before.Length;
            var afterLength = after.Length;
            var prefix = 0;
            var commonLimit = Math.Min(beforeLength, afterLength);

            while (prefix < commonLimit && before[prefix] == after[prefix])
                prefix++;

            var suffix = 0;
            while (suffix < beforeLength - prefix &&
                   suffix < afterLength - prefix &&
                   before[beforeLength - 1 - suffix] == after[afterLength - 1 - suffix])
            {
                suffix++;
            }

            start = prefix;
            removedLength = beforeLength - prefix - suffix;
            insertedLength = afterLength - prefix - suffix;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static bool ContainsNewline(string text, int start, int length)
        {
            var end = Math.Min(text.Length, start + length);
            for (var i = Math.Max(0, start); i < end; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                    return true;
            }

            return false;
        }
    }
}
