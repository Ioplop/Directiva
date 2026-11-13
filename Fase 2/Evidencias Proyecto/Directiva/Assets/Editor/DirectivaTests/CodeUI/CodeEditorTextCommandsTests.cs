using System;
using System.Collections.Generic;
using Directiva.CodeUI;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Regression tests for the editor's line-editing keyboard shortcuts.</summary>
    internal static class CodeEditorTextCommandsTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "TextShortcuts", id, description, run));

            Add("EnterIndentsCurrentLevel", "Enter preserves the current line's indentation", t =>
            {
                const string source = "def main():\n    move(self.nexus)";
                var edit = CodeEditorTextCommands.InsertLineBreak(source, source.Length, source.Length);
                t.Equal(source + "\n    ", edit.Text, "new source");
                t.Equal(edit.Text.Length, edit.Caret, "caret follows indentation");
            });

            Add("EnterAfterColon", "Enter after a colon adds exactly one four-space level", t =>
            {
                const string source = "def main():\n    if ready:  ";
                var edit = CodeEditorTextCommands.InsertLineBreak(source, source.Length, source.Length);
                t.Equal(source + "\n        ", edit.Text, "body indentation");
                t.Equal(edit.Text.Length, edit.Caret, "caret after body indentation");
            });

            Add("EnterAtRoot", "Root-level lines do not gain indentation", t =>
            {
                var edit = CodeEditorTextCommands.InsertLineBreak("shared go = True", 16, 16);
                t.Equal("shared go = True\n", edit.Text, "root new line");
                t.Equal(17, edit.Caret, "root caret");
            });

            Add("EnterSplitsLine", "Enter in the middle of a line preserves its suffix", t =>
            {
                const string source = "    move(enemy)";
                var edit = CodeEditorTextCommands.InsertLineBreak(source, 8, 8);
                t.Equal("    move\n    (enemy)", edit.Text, "split line");
                t.Equal(13, edit.Caret, "caret after indentation, before suffix");
            });

            Add("EnterReplacesSelection", "Enter replaces a selection instead of duplicating it", t =>
            {
                const string source = "    before something after";
                var edit = CodeEditorTextCommands.InsertLineBreak(source, 11, 21);
                t.Equal("    before \n    after", edit.Text, "replace selection");
                t.Equal(16, edit.Caret, "selection replaced and caret collapsed");
            });

            Add("EnterPreservesTabs", "Pasted tabs remain intact and colon adds four spaces", t =>
            {
                const string source = "\tif ready:";
                var edit = CodeEditorTextCommands.InsertLineBreak(source, source.Length, source.Length);
                t.Equal(source + "\n\t    ", edit.Text, "tab preserved");
                t.Equal(edit.Text.Length, edit.Caret, "caret after mixed indentation");
            });

            Add("EnterInsideIndent", "Enter at the start of indented text does not double its spaces", t =>
            {
                var edit = CodeEditorTextCommands.InsertLineBreak("def main():\n    pass", 12, 12);
                t.Equal("def main():\n\n    pass", edit.Text, "original indentation preserved");
                t.Equal(13, edit.Caret, "caret before original indentation");
            });

            Add("EnterNormalizesCRLF", "Newlines use LF even when the source arrived as CRLF", t =>
            {
                var edit = CodeEditorTextCommands.InsertLineBreak("a\r\n    b", 7, 7);
                t.Equal("a\n    b\n    ", edit.Text, "LF normalization");
                t.Equal(edit.Text.Length, edit.Caret, "caret in normalized source");
            });

            Add("DeleteMiddleLine", "Shift+Delete removes the current line and its line break", t =>
            {
                var edit = CodeEditorTextCommands.DeleteSelectedLines("first\nsecond\nthird", 8, 8);
                t.Equal("first\nthird", edit.Text, "middle line removed");
                t.Equal(6, edit.Caret, "caret at start of next line");
            });

            Add("DeleteFirstLine", "Shift+Delete removes the first line without blank space", t =>
            {
                var edit = CodeEditorTextCommands.DeleteSelectedLines("first\nsecond\nthird", 0, 0);
                t.Equal("second\nthird", edit.Text, "first line removed");
                t.Equal(0, edit.Caret, "caret at start");
            });

            Add("DeleteLastLine", "Shift+Delete removes the final line and its preceding newline", t =>
            {
                var edit = CodeEditorTextCommands.DeleteSelectedLines("first\nsecond\nthird", 16, 16);
                t.Equal("first\nsecond", edit.Text, "last line removed");
                t.Equal(12, edit.Caret, "caret at last position");
            });

            Add("DeleteOnlyLine", "Shift+Delete clears the whole document with one line", t =>
            {
                var edit = CodeEditorTextCommands.DeleteSelectedLines("only", 2, 2);
                t.Equal(string.Empty, edit.Text, "only line removed");
                t.Equal(0, edit.Caret, "caret at start");
            });

            Add("DeleteMultiLineSelection", "Shift+Delete removes every line touched by a selection", t =>
            {
                const string source = "alpha\nbeta\ngamma\ndelta";
                var edit = CodeEditorTextCommands.DeleteSelectedLines(source, 7, 13);
                t.Equal("alpha\ndelta", edit.Text, "selected beta and gamma lines removed");
                t.Equal(6, edit.Caret, "caret at next remaining line");
            });

            Add("DeleteSelectionAtNextLineStart", "The line at an exclusive selection end is preserved", t =>
            {
                const string source = "alpha\nbeta\ngamma\ndelta";
                var edit = CodeEditorTextCommands.DeleteSelectedLines(source, 7, 11);
                t.Equal("alpha\ngamma\ndelta", edit.Text, "gamma at selection end preserved");
            });

            Add("DeleteReversedSelection", "Selection direction does not change deleted lines", t =>
            {
                const string source = "alpha\nbeta\ngamma\ndelta";
                var edit = CodeEditorTextCommands.DeleteSelectedLines(source, 13, 7);
                t.Equal("alpha\ndelta", edit.Text, "backwards selection");
            });

            Add("DeleteThroughEnd", "Deleting a selected final line leaves no extra newline", t =>
            {
                const string source = "alpha\nbeta\ngamma";
                var edit = CodeEditorTextCommands.DeleteSelectedLines(source, 7, source.Length);
                t.Equal("alpha", edit.Text, "last selected lines removed");
                t.Equal(5, edit.Caret, "caret at end of prior line");
            });

            Add("DeleteTrailingEmptyLine", "Shift+Delete removes a trailing empty line", t =>
            {
                const string source = "alpha\nbeta\n";
                var edit = CodeEditorTextCommands.DeleteSelectedLines(source, source.Length, source.Length);
                t.Equal("alpha\nbeta", edit.Text, "empty last line removed");
                t.Equal(10, edit.Caret, "caret at end of beta");
            });

            Add("DeleteEmptyDocument", "Shift+Delete on an empty source remains a no-op", t =>
            {
                var edit = CodeEditorTextCommands.DeleteSelectedLines(string.Empty, 0, 0);
                t.Equal(string.Empty, edit.Text, "empty source");
                t.Equal(0, edit.Caret, "empty caret");
            });

            Add("DeleteConsecutiveLines", "Two sequential line deletions each produce a valid next state", t =>
            {
                var edit = CodeEditorTextCommands.DeleteSelectedLines("alpha\nbeta\ngamma", 6, 6);
                var next = CodeEditorTextCommands.DeleteSelectedLines(edit.Text, edit.Caret, edit.Caret);
                t.Equal("alpha", next.Text, "successive deletes");
                t.Equal(5, next.Caret, "final caret");
            });

            Add("MoveSingleUp", "Alt+Up swaps a complete line and keeps the caret column", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("uno\ndos\ntres", 5, 5, false);
                t.Check(edit.Changed, "line moved");
                t.Equal("dos\nuno\ntres", edit.Text, "line order");
                t.Equal(1, edit.Cursor, "caret stays inside moved line");
                t.Equal(1, edit.Selection, "selection stays collapsed");
                t.Equal(1, edit.MapLine(2), "breakpoint on moved line follows it");
                t.Equal(2, edit.MapLine(1), "breakpoint on neighbor follows it");
                t.Equal(3, edit.MapLine(3), "unrelated breakpoint stays");
            });

            Add("MoveSingleDown", "Alt+Down moves a whole line while preserving its caret", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("uno\ndos\ntres", 5, 5, true);
                t.Equal("uno\ntres\ndos", edit.Text, "line order");
                t.Equal(10, edit.Cursor, "caret follows dos");
                t.Equal(3, edit.MapLine(2), "moved breakpoint");
                t.Equal(2, edit.MapLine(3), "following breakpoint");
            });

            Add("MoveSelectedBlockUp", "Alt+Up moves all selected rows as a block", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("a\nbb\nccc\nd", 2, 9, false);
                t.Equal("bb\nccc\na\nd", edit.Text, "whole selected block moved");
                t.Equal(0, edit.Cursor, "selection start follows block");
                t.Equal(7, edit.Selection, "exclusive end excludes line a");
                t.Equal(2, edit.FirstLine, "original first line");
                t.Equal(3, edit.LastLine, "original last line");
                t.Equal(1, edit.MapLine(2), "first block breakpoint");
                t.Equal(2, edit.MapLine(3), "second block breakpoint");
                t.Equal(3, edit.MapLine(1), "preceding breakpoint relocated");
            });

            Add("MoveSelectedBlockDown", "Alt+Down moves selection to EOF without adding a newline", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("a\nbb\nccc\nd", 2, 9, true);
                t.Equal("a\nd\nbb\nccc", edit.Text, "block moved down");
                t.Equal(4, edit.Cursor, "selection moved to new block start");
                t.Equal(10, edit.Selection, "selection ends at EOF");
                t.Equal(3, edit.MapLine(2), "first block breakpoint follows block");
                t.Equal(4, edit.MapLine(3), "second block breakpoint follows block");
                t.Equal(2, edit.MapLine(4), "adjacent breakpoint moves up");
            });

            Add("MoveReversedSelection", "Moving lines preserves backward selection orientation", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("a\nbb\nccc\nd", 9, 2, false);
                t.Equal("bb\nccc\na\nd", edit.Text, "text moved");
                t.Equal(7, edit.Cursor, "cursor remains the higher endpoint");
                t.Equal(0, edit.Selection, "selection anchor remains lower endpoint");
            });

            Add("MoveSelectionEndsAtLineStart", "A selection ending at the next row start excludes that row", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("a\nb\nc", 2, 4, true);
                t.Equal("a\nc\nb", edit.Text, "only b moved, not c");
                t.Equal(4, edit.Cursor, "selection start follows b");
                t.Equal(5, edit.Selection, "selection end clamped to EOF");
                t.Equal(2, edit.LastLine, "exclusive selection ends on b");
            });

            Add("MovePreservesWhitespace", "Moving lines never changes indentation or trailing spaces", t =>
            {
                const string source = "if ok:\n    one()  \n\tsecond()\npass";
                var edit = CodeEditorTextCommands.MoveSelectedLines(source, 19, 19, false);
                t.Equal("if ok:\n\tsecond()\n    one()  \npass", edit.Text,
                    "unchanged tab, spaces and source contents");
            });

            Add("MoveAtBoundaries", "Moving first up or last down is a no-op", t =>
            {
                var up = CodeEditorTextCommands.MoveSelectedLines("a\nb", 0, 0, false);
                var down = CodeEditorTextCommands.MoveSelectedLines("a\nb", 3, 3, true);
                t.Check(!up.Changed && !down.Changed, "no text mutation at boundaries");
                t.Equal("a\nb", up.Text, "up no-op source");
                t.Equal("a\nb", down.Text, "down no-op source");
                t.Equal(0, up.Cursor, "up no-op cursor");
                t.Equal(3, down.Cursor, "down no-op cursor");
            });

            Add("MoveTrailingEmptyLine", "A trailing empty row can be moved without losing characters", t =>
            {
                var edit = CodeEditorTextCommands.MoveSelectedLines("a\n", 2, 2, false);
                t.Equal("\na", edit.Text, "empty line moved up");
                t.Equal(0, edit.Cursor, "caret follows empty line");
                var again = CodeEditorTextCommands.MoveSelectedLines(edit.Text, edit.Cursor, edit.Selection, true);
                t.Equal("a\n", again.Text, "inverse move returns original");
                t.Equal(2, again.Cursor, "inverse caret");
            });

            Add("MoveMultipleAndUndoEquivalent", "Moving the same block back restores text and selection", t =>
            {
                const string source = "a\nbb\nccc\nd";
                var first = CodeEditorTextCommands.MoveSelectedLines(source, 3, 7, true);
                var again = CodeEditorTextCommands.MoveSelectedLines(
                    first.Text, first.Cursor, first.Selection, false);
                t.Equal(source, again.Text, "inverse restores text");
                t.Equal(3, again.Cursor, "inverse restores caret");
                t.Equal(7, again.Selection, "inverse restores selection");
            });
        }
    }
}
