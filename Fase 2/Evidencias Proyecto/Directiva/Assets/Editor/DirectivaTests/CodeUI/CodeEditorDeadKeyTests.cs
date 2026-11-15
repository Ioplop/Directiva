using System;
using System.Collections.Generic;
using Directiva.CodeUI;
using UnityEngine;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Regression tests for OS dead-key text arriving after a Tab shortcut.</summary>
    internal static class CodeEditorDeadKeyTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "DeadKeys", id, description, run));

            Add("AcuteAfterTab", "Acute accent emitted after Tab cannot enter the completion text", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(guard.ShouldConsume(KeyCode.None, '\u00b4'), "standalone acute accent discarded");
                t.Check(guard.PendingTabCharacters, "Tab remains guarded until another physical key");
            });

            Add("CircumflexAfterTab", "Circumflex from another keyboard layout is also discarded", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(guard.ShouldConsume(KeyCode.None, '^'), "circumflex discarded");
            });

            Add("DiaeresisAfterTab", "Diaeresis dead-key output does not interrupt the Tab cycle", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(guard.ShouldConsume(KeyCode.None, '\u00a8'), "diaeresis discarded");
            });

            Add("CombiningAccentAfterTab", "Unicode combining diacritics are not special-cased by language", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(guard.ShouldConsume(KeyCode.None, '\u0301'), "combining accent discarded");
            });

            Add("ArbitraryPrintableAfterTab", "Any stray printable character from a consumed Tab is blocked", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(guard.ShouldConsume(KeyCode.None, '#'), "not tied to a hard-coded accent list");
            });

            Add("TabCycleCanContinue", "Another physical Tab still reaches the completion cycle", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(guard.ShouldConsume(KeyCode.None, '\u00b4'), "previous dead key discarded");
                t.Check(!guard.ShouldConsume(KeyCode.Tab, '\0'), "next Tab press is not suppressed");
                t.Check(!guard.ShouldConsume(KeyCode.None, '\t'), "native Tab control char uses existing deduplication");
                t.Check(guard.PendingTabCharacters, "pending accents from new Tab can still be discarded");
            });

            Add("ModifiersKeepGuard", "Shift and AltGr do not release pending Tab text", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(!guard.ShouldConsume(KeyCode.LeftShift, '\0'), "Shift key passes");
                t.Check(!guard.ShouldConsume(KeyCode.RightAlt, '\0'), "AltGr modifier passes");
                t.Check(guard.ShouldConsume(KeyCode.None, '\u00a8'), "stray accent still blocked");
            });

            Add("TypingAfterTab", "Real typing after Tab, including accented vowels, is left intact", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(!guard.ShouldConsume(KeyCode.A, '\0'), "physical A key passes");
                t.Check(!guard.PendingTabCharacters, "next physical key ends protection");
                t.Check(!guard.ShouldConsume(KeyCode.None, '\u00e1'), "real accented character passes");
            });

            Add("SpaceAfterTab", "Space still inserts normally after the completion cycle", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                guard.Arm();
                t.Check(!guard.ShouldConsume(KeyCode.Space, '\0'), "Space key passes");
                t.Check(!guard.ShouldConsume(KeyCode.None, ' '), "Space character passes");
            });

            Add("DeadKeyKeepsCycleOpen", "Physical dead keys cannot dismiss Tab preview before a text edit", t =>
            {
                t.Check(!CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.Quote),
                    "an accent key without character data keeps completion open");
                t.Check(!CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.BackQuote),
                    "accent on another keyboard layout keeps completion open");
                t.Check(!CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.A),
                    "a real typed edit is handled by the value-change callback");
                t.Check(!CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.LeftShift),
                    "Shift+Tab can still cycle backward");
            });

            Add("NavigationEndsCycle", "Navigation and deletion still dismiss the Tab preview", t =>
            {
                t.Check(CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.LeftArrow),
                    "moving the caret ends the preview");
                t.Check(CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.Backspace),
                    "deleting text ends the preview");
                t.Check(CodeEditorTabTextGuard.ShouldDismissPreviewForKey(KeyCode.F2),
                    "rename command ends the preview");
            });

            Add("NoTabOrAfterReset", "Normal text entry without Tab is never suppressed", t =>
            {
                var guard = new CodeEditorTabTextGuard();
                t.Check(!guard.ShouldConsume(KeyCode.None, '\u00b4'), "literal accent without Tab passes");
                guard.Arm();
                guard.Reset();
                t.Check(!guard.ShouldConsume(KeyCode.None, '^'), "switching focus or files resets protection");
            });
        }
    }
}
