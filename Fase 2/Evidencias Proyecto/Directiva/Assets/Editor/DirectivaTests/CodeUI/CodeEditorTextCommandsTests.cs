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
        }
    }
}
