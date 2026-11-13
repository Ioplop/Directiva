using System;

namespace Directiva.CodeUI
{
    /// <summary>A text-only edit, with a collapsed caret in the resulting source.</summary>
    public readonly struct CodeEditorTextEdit
    {
        public string Text { get; }
        public int Caret { get; }

        public CodeEditorTextEdit(string text, int caret)
        {
            Text = text ?? string.Empty;
            Caret = Math.Max(0, Math.Min(caret, Text.Length));
        }
    }

    /// <summary>
    /// A whole-line move retains the caret/selection direction and reports the
    /// original line range so line-based annotations can follow the source.
    /// </summary>
    public readonly struct CodeEditorLineMoveEdit
    {
        public string Text { get; }
        public int Cursor { get; }
        public int Selection { get; }
        public int FirstLine { get; }
        public int LastLine { get; }
        public bool MovingDown { get; }
        public bool Changed { get; }

        public CodeEditorLineMoveEdit(string text, int cursor, int selection,
            int firstLine, int lastLine, bool movingDown, bool changed)
        {
            Text = text ?? string.Empty;
            Cursor = Math.Max(0, Math.Min(cursor, Text.Length));
            Selection = Math.Max(0, Math.Min(selection, Text.Length));
            FirstLine = firstLine;
            LastLine = lastLine;
            MovingDown = movingDown;
            Changed = changed;
        }

        /// <summary>Map a one-based line number when swapping the selected block with a neighbor.</summary>
        public int MapLine(int line)
        {
            if (!Changed || line <= 0)
                return line;

            if (MovingDown)
            {
                if (line >= FirstLine && line <= LastLine) return line + 1;
                if (line == LastLine + 1) return FirstLine;
            }
            else
            {
                if (line >= FirstLine && line <= LastLine) return line - 1;
                if (line == FirstLine - 1) return LastLine;
            }
            return line;
        }
    }

    /// <summary>
    /// Pure text transformations for editor shortcuts. The UI applies the result
    /// through its normal value-change and undo/redo pipeline.
    /// </summary>
    public static class CodeEditorTextCommands
    {
        /// <summary>
        /// VS Code-style Alt+Up/Down: move the whole line or selected contiguous
        /// block one position without changing its contents or indentation.
        /// A selection ending immediately after a newline excludes the next line.
        /// </summary>
        public static CodeEditorLineMoveEdit MoveSelectedLines(
            string text, int cursor, int selection, bool moveDown)
        {
            text = NormalizeLineEndings(text);
            cursor = Clamp(cursor, text.Length);
            selection = Clamp(selection, text.Length);

            int start = Math.Min(cursor, selection);
            int end = Math.Max(cursor, selection);
            int lastRelevant = end > start && end > 0 && text[end - 1] == '\n'
                ? end - 1 : end;

            // Split preserves a trailing empty logical line, unlike many
            // line enumerators. This makes moves around a final newline safe.
            var lines = text.Split('\n');
            int firstLine = LineNumberAt(text, start);
            int lastLine = LineNumberAt(text, lastRelevant);

            if ((moveDown && lastLine == lines.Length) || (!moveDown && firstLine == 1))
                return new CodeEditorLineMoveEdit(text, cursor, selection,
                    firstLine, lastLine, moveDown, false);

            int shift;
            if (moveDown)
            {
                // The following line moves above the block.
                int after = lastLine; // zero-based index
                shift = lines[after].Length + 1;
                string neighbor = lines[after];
                for (int i = after; i > firstLine - 1; i--)
                    lines[i] = lines[i - 1];
                lines[firstLine - 1] = neighbor;
            }
            else
            {
                // The preceding line moves below the block.
                int before = firstLine - 2; // zero-based index
                shift = -(lines[before].Length + 1);
                string neighbor = lines[before];
                for (int i = before; i < lastLine - 1; i++)
                    lines[i] = lines[i + 1];
                lines[lastLine - 1] = neighbor;
            }

            string updated = string.Join("\n", lines);
            // Both endpoints travel with the moved block. Clamping matters
            // when that block moves past an EOF line without a trailing LF.
            return new CodeEditorLineMoveEdit(updated,
                Clamp(cursor + shift, updated.Length),
                Clamp(selection + shift, updated.Length),
                firstLine, lastLine, moveDown, true);
        }

        private static int LineNumberAt(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>
        /// Replace the selection with a line break followed by the current line's
        /// indentation. A colon before the caret adds one more indentation level.
        /// </summary>
        public static CodeEditorTextEdit InsertLineBreak(
            string text, int cursor, int selection, int tabSize = 4)
        {
            text = NormalizeLineEndings(text);
            int start = Math.Min(Clamp(cursor, text.Length), Clamp(selection, text.Length));
            int end = Math.Max(Clamp(cursor, text.Length), Clamp(selection, text.Length));
            int lineStart = FindLineStart(text, start);
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = text.Length;

            int indentEnd = lineStart;
            while (indentEnd < lineEnd &&
                   (text[indentEnd] == ' ' || text[indentEnd] == '\t'))
                indentEnd++;

            // Preserve existing whitespace exactly (including pasted tabs).
            // If the caret is inside indentation, reuse only the whitespace
            // before it. The rest already remains in the suffix of this line.
            string indentation = text.Substring(lineStart, Math.Min(start, indentEnd) - lineStart);

            int lastContent = start - 1;
            while (lastContent >= lineStart &&
                   (text[lastContent] == ' ' || text[lastContent] == '\t'))
                lastContent--;

            if (lastContent >= lineStart && text[lastContent] == ':')
                indentation += new string(' ', Math.Max(1, tabSize));

            string inserted = "\n" + indentation;
            string updated = text.Remove(start, end - start).Insert(start, inserted);
            return new CodeEditorTextEdit(updated, start + inserted.Length);
        }

        /// <summary>
        /// Delete whole logical lines intersecting the selection. If selection
        /// ends at the start of a line, that last line is not included.
        /// </summary>
        public static CodeEditorTextEdit DeleteSelectedLines(
            string text, int cursor, int selection)
        {
            text = NormalizeLineEndings(text);
            int start = Math.Min(Clamp(cursor, text.Length), Clamp(selection, text.Length));
            int end = Math.Max(Clamp(cursor, text.Length), Clamp(selection, text.Length));
            int firstLineStart = FindLineStart(text, start);

            // Conventional selection semantics: [start, end). A selection that
            // stops immediately after a newline does not touch the next line.
            int lastSelectedCharacter = end > start && end > 0 && text[end - 1] == '\n'
                ? end - 1
                : end;
            int lastLineStart = FindLineStart(text, lastSelectedCharacter);
            int finalNewline = text.IndexOf('\n', lastLineStart);

            int removeStart = firstLineStart;
            int removeEnd;
            if (finalNewline >= 0)
            {
                // Consume the trailing newline to join the remaining lines.
                removeEnd = finalNewline + 1;
            }
            else
            {
                // The final line has no trailing newline. Remove its preceding
                // newline instead, so deleting it leaves no empty last line.
                removeEnd = text.Length;
                if (firstLineStart > 0)
                    removeStart = firstLineStart - 1;
            }

            string updated = text.Remove(removeStart, removeEnd - removeStart);
            return new CodeEditorTextEdit(updated, Math.Min(removeStart, updated.Length));
        }

        private static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static int FindLineStart(string text, int index)
        {
            if (index <= 0)
                return 0;
            int previous = text.LastIndexOf('\n', Math.Min(index - 1, text.Length - 1));
            return previous < 0 ? 0 : previous + 1;
        }

        private static int Clamp(int index, int max)
        {
            return Math.Max(0, Math.Min(index, max));
        }
    }
}
