using System;
using System.Collections.Generic;
using Directiva.CodeUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.EditorTests.CodeUITests
{
    /// <summary>Regression tests for code viewport stability during focus changes.</summary>
    internal static class EditorFocusScrollTests
    {
        internal static void Register(List<DirectivaTestCase> tests)
        {
            void Add(string id, string description, Action<DirectivaTestContext> run) =>
                tests.Add(new DirectivaTestCase("CodeUI", "FocusScroll", id, description, run));

            Add("NativeBlurResetIsSkipped", "The code field blocks Unity's native blur scroll reset", t =>
            {
                t.Check(CodeEditorView.ScrollPreservingTextField.SuppressesNativeBlurScroll(
                    BlurEvent.TypeId()), "native BlurEvent must not recenter the code viewport");
            });

            Add("OtherFocusEventsRemainNative", "Normal focus and input events still use TextField handling", t =>
            {
                t.Check(!CodeEditorView.ScrollPreservingTextField.SuppressesNativeBlurScroll(
                    FocusOutEvent.TypeId()), "FocusOutEvent is handled normally");
                t.Check(!CodeEditorView.ScrollPreservingTextField.SuppressesNativeBlurScroll(
                    FocusInEvent.TypeId()), "FocusInEvent is handled normally");
                t.Check(!CodeEditorView.ScrollPreservingTextField.SuppressesNativeBlurScroll(
                    FocusEvent.TypeId()), "FocusEvent is handled normally");
            });

            Add("SynchronousRestorationCompletes", "Focus restoration finishes without queued frame callbacks", t =>
            {
                var state = new EditorFocusScrollState();
                int blur = state.BeginFocusLoss(new Vector2(0f, 725f));
                t.Check(state.TryGetRestoration(blur, out _), "focus loss captured");
                state.FinishRestoration(blur);
                int focus = state.BeginFocusReturn();
                t.Check(state.TryGetRestoration(focus, out Vector2 saved), "return has snapshot");
                t.Equal(725f, saved.y, "viewport preserved");
                state.FinishRestoration(focus);
                t.Check(!state.TryGetRestoration(focus, out _), "nothing remains to restore next frame");
            });

            Add("BlurKeepsViewport", "Leaving the editor retains its previously scrolled code position", t =>
            {
                var state = new EditorFocusScrollState();
                var scrolled = new Vector2(15f, 760f);
                int token = state.BeginFocusLoss(scrolled);
                t.Check(state.TryGetRestoration(token, out Vector2 saved), "restoration pending");
                t.Equal(scrolled, saved, "scroll position before the native blur reset");
                state.FinishRestoration(token);
                t.Check(!state.TryGetRestoration(token, out _), "finished token cannot repeat");
            });

            Add("RefocusAfterNativeReset", "Clicking into code does not restore the native TextField's temporary top offset", t =>
            {
                var state = new EditorFocusScrollState();
                state.BeginFocusLoss(new Vector2(9f, 820f));
                int token = state.BeginFocusReturn();
                t.Check(state.TryGetRestoration(token, out Vector2 offset), "focus restore exists");
                t.Equal(new Vector2(9f, 820f), offset, "original offset survives focus-in at zero");
            });

            Add("CompetingFocusEvents", "Two focus callbacks cannot replace the original viewport with an intermediate reset", t =>
            {
                var state = new EditorFocusScrollState();
                int first = state.BeginFocusLoss(new Vector2(0f, 550f));
                int second = state.BeginFocusLoss(Vector2.zero);
                t.Check(!state.TryGetRestoration(first, out _), "superseded callback ignored");
                t.Check(state.TryGetRestoration(second, out Vector2 offset), "latest callback used");
                t.Equal(550f, offset.y, "original viewport retained");
            });

            Add("WheelBeatsFocusRestore", "A deliberate wheel scroll cancels any pending focus restoration", t =>
            {
                var state = new EditorFocusScrollState();
                int token = state.BeginFocusLoss(new Vector2(0f, 800f));
                state.NoteDeliberateScroll(new Vector2(0f, 920f));
                t.Check(!state.TryGetRestoration(token, out _), "old restoration cancelled");
                int newToken = state.BeginFocusReturn();
                t.Check(state.TryGetRestoration(newToken, out Vector2 position), "new position retained");
                t.Equal(920f, position.y, "manual wheel position wins");
            });

            Add("NavigationBeatsFocusRestore", "Clicking a console diagnostic cancels stale focus scroll restoration", t =>
            {
                var state = new EditorFocusScrollState();
                int token = state.BeginFocusLoss(new Vector2(0f, 600f));
                state.CancelForNavigation();
                t.Check(!state.TryGetRestoration(token, out _), "jump is never undone");
                t.Check(!state.HasSavedOffset, "future focus does not restore stale offset");
            });

            Add("NewDocumentResetsViewport", "Switching files cannot restore the previous script's viewport", t =>
            {
                var state = new EditorFocusScrollState();
                int token = state.BeginFocusLoss(new Vector2(20f, 2000f));
                state.ResetForDocument();
                t.Check(!state.TryGetRestoration(token, out _), "previous source's callback invalidated");
                int newToken = state.BeginFocusReturn();
                t.Check(!state.TryGetRestoration(newToken, out _),
                    "first focus of new source does not force a stale viewport");
                newToken = state.BeginFocusLoss(new Vector2(0f, 175f));
                t.Check(state.TryGetRestoration(newToken, out Vector2 offset),
                    "new source can capture its own viewport");
                t.Equal(175f, offset.y, "new source remembers its own scroll");
            });
        }
    }
}
