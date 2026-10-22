using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Directiva.UI;

namespace Directiva.CodeUI
{
    public sealed class CodeEditorView : VisualElement
    {
        private const int TabSize = 4;
        private const int UndoHistoryCapacity = 200;
        private const float UndoMergeWindowSeconds = 0.75f;

        // The editor uses one deterministic coordinate model:
        // - padding is configured explicitly in pixels;
        // - character width and line height are measured from the actual FontAsset;
        // - indentation guides use the same origin and metrics as the text;
        // - only character metrics scale when zooming; pixel padding/offsets do not.
        private const float FallbackLineHeight = 20f;
        private const float FallbackCharacterWidth = 8f;
        private const float MinimumFontSize = 6f;
        private const float MaximumFontSize = 48f;
        private const float ZoomStep = 1f;
        private const long SyntaxDebounceMilliseconds = 75;
        private const int SyntaxOverscanLines = 2;
        private const int SyntaxOverscanColumns = 3;
        // UI Toolkit can update TextField glyph geometry a frame after font-size
        // changes, even if the input's own bounds never fire GeometryChangedEvent.
        private const long ZoomLayoutFollowupMilliseconds = 34;
        // 0 = first visible line, 0.5 = viewport center. Keep the center stable
        // as glyph size changes; this also preserves partial-line positions.
        private const float ZoomAnchorViewportFraction = 0.5f;

        private readonly FontAsset _normalFont;
        private readonly FontAsset _boldFont;
        private readonly Sprite _breakpointSprite;

        private float _fontSize;
        private float _paddingLeftPx;
        private float _paddingTopPx;
        private float _paddingRightPx;
        private float _paddingBottomPx;
        private float _indentGuideFineOffsetPx;

        private float _textOriginX;
        private float _textOriginY;
        private float _lineHeight = FallbackLineHeight;
        private float _characterWidth = FallbackCharacterWidth;

        private readonly VisualElement _gutterViewport;
        private readonly VisualElement _gutterContent;
        private readonly VisualElement _editorStack;
        private readonly VisualElement _executionHighlightLayer;
        private readonly VisualElement _executionLineHighlight;
        private readonly VisualElement _indentGuideLayer;
        private readonly VisualElement _syntaxOverlay;
        private readonly List<Label> _syntaxLabels = new List<Label>();
        private readonly Dictionary<DScriptSyntaxKind, string> _syntaxColorHex =
            new Dictionary<DScriptSyntaxKind, string>();
        private DirectivaSyntaxColors _syntaxColors = new DirectivaSyntaxColors();
        private DScriptSyntaxDocument _syntaxDocument;
        private IVisualElementScheduledItem _syntaxRefreshTask;
        private IVisualElementScheduledItem _zoomLayoutSyncTask;
        private int _zoomLayoutSyncPassesRemaining;
        private bool _zoomAnchorPending;
        private float _zoomAnchorLogicalLine;
        private bool _nativeTextHidden;
        private readonly Label _metricsProbe;
        private readonly TextField _textField;
        private readonly HashSet<int> _breakpoints = new HashSet<int>();
        private readonly Dictionary<string, EditorUndoHistory> _undoHistories =
            new Dictionary<string, EditorUndoHistory>(StringComparer.OrdinalIgnoreCase);

        private EditorUndoHistory _activeUndoHistory;

        private ScrollView _internalScroll;
        private bool _textInputGeometryHooked;
        private bool _textInputKeyHooked;
        private bool _suppressChange;
        private bool _historyApplying;
        private bool _forceHistoryBoundaryNextChange;
        private int _executionLine = -1;

        public string DocumentPath { get; private set; } = string.Empty;

        public event Action<string> TextChanged;
        public event Action<int, bool> BreakpointChanged;
        public event Action<float> FontSizeChanged;

        public string Text => _textField.value ?? string.Empty;

        public CodeEditorView(
            FontAsset normalFont,
            FontAsset boldFont,
            float fontSize,
            float paddingLeftPx,
            float paddingTopPx,
            float paddingRightPx,
            float paddingBottomPx,
            float indentGuideFineOffsetPx,
            Sprite breakpointSprite = null)
        {
            _normalFont = normalFont;
            _boldFont = boldFont;
            _breakpointSprite = breakpointSprite;
            _fontSize = Mathf.Clamp(fontSize, MinimumFontSize, MaximumFontSize);
            _paddingLeftPx = Mathf.Max(0f, paddingLeftPx);
            _paddingTopPx = Mathf.Max(0f, paddingTopPx);
            _paddingRightPx = Mathf.Max(0f, paddingRightPx);
            _paddingBottomPx = Mathf.Max(0f, paddingBottomPx);
            _indentGuideFineOffsetPx = indentGuideFineOffsetPx;

            AddToClassList("code-editor");

            if (_normalFont != null)
                style.unityFontDefinition = new StyleFontDefinition(_normalFont);

            _gutterViewport = new VisualElement();
            _gutterViewport.AddToClassList("code-gutter-viewport");

            _gutterContent = new VisualElement();
            _gutterContent.AddToClassList("code-gutter-content");
            _gutterViewport.Add(_gutterContent);

            _editorStack = new VisualElement();
            _editorStack.AddToClassList("code-editor-stack");

            _executionHighlightLayer = new VisualElement();
            _executionHighlightLayer.pickingMode = PickingMode.Ignore;
            _executionHighlightLayer.style.position = Position.Absolute;
            _executionHighlightLayer.style.left = 0f;
            _executionHighlightLayer.style.right = 0f;
            _executionHighlightLayer.style.top = 0f;
            _executionHighlightLayer.style.bottom = 0f;

            _executionLineHighlight = new VisualElement();
            _executionLineHighlight.pickingMode = PickingMode.Ignore;
            _executionLineHighlight.style.position = Position.Absolute;
            _executionLineHighlight.style.left = 0f;
            _executionLineHighlight.style.right = 0f;
            _executionLineHighlight.style.display = DisplayStyle.None;
            _executionLineHighlight.style.backgroundColor = new Color(0.20f, 0.48f, 0.82f, 0.20f);
            _executionLineHighlight.style.borderLeftWidth = 2f;
            _executionLineHighlight.style.borderLeftColor = new Color(0.40f, 0.70f, 1.00f, 0.90f);
            _executionHighlightLayer.Add(_executionLineHighlight);

            _indentGuideLayer = new VisualElement();
            _indentGuideLayer.AddToClassList("indent-guide-layer");
            _indentGuideLayer.pickingMode = PickingMode.Ignore;

            // One non-interactive text layer mirrors only the visible portion of
            // TextField. TextField remains the sole source of text, caret, selection,
            // keyboard input and undo/redo; Unity TextField cannot render rich text.
            _syntaxOverlay = new VisualElement();
            _syntaxOverlay.AddToClassList("code-syntax-overlay");
            _syntaxOverlay.pickingMode = PickingMode.Ignore;
            _syntaxOverlay.style.position = Position.Absolute;
            _syntaxOverlay.style.left = 0f;
            _syntaxOverlay.style.top = 0f;
            _syntaxOverlay.style.bottom = 0f;
            _syntaxOverlay.style.overflow = Overflow.Hidden;
            _syntaxOverlay.style.display = DisplayStyle.None;
            CacheSyntaxColors();

            // Invisible zero-padding label used only as a fallback when direct
            // FontAsset metrics cannot be read (for example, a missing space glyph).
            _metricsProbe = new Label();
            _metricsProbe.AddToClassList("code-metrics-probe");
            _metricsProbe.pickingMode = PickingMode.Ignore;

            _textField = new TextField
            {
                multiline = true,
                isDelayed = false,
                selectAllOnFocus = false,
                selectAllOnMouseUp = false,
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible
            };
            _textField.AddToClassList("code-text-field");

            if (_normalFont != null)
                _textField.style.unityFontDefinition = new StyleFontDefinition(_normalFont);

            ApplyFontStyles();
            ApplyEditorLayoutStyles();

            _editorStack.Add(_textField);
            _editorStack.Add(_executionHighlightLayer);
            _editorStack.Add(_indentGuideLayer);
            _editorStack.Add(_syntaxOverlay);
            _editorStack.Add(_metricsProbe);

            Add(_gutterViewport);
            Add(_editorStack);

            _textField.RegisterValueChangedCallback(evt =>
            {
                if (_suppressChange)
                    return;

                var value = evt.newValue ?? string.Empty;
                var normalized = NormalizeLineEndings(value);

                // Keep one canonical newline representation inside the editor. UI Toolkit
                // presents CRLF as a single visual line break, while string-based edit
                // helpers count both characters. Letting CRLF survive in the backing value
                // can therefore make cursor indices drift by one character per preceding
                // line when TAB is handled manually.
                if (!string.Equals(value, normalized, StringComparison.Ordinal))
                {
                    var cursor = _textField.textSelection.cursorIndex;
                    var select = _textField.textSelection.selectIndex;

                    _suppressChange = true;
                    _textField.SetValueWithoutNotify(normalized);
                    _suppressChange = false;

                    var length = normalized.Length;
                    _textField.textSelection.SelectRange(
                        Mathf.Clamp(cursor, 0, length),
                        Mathf.Clamp(select, 0, length));
                }

                if (!_historyApplying && _activeUndoHistory != null)
                {
                    var cursor = Mathf.Clamp(_textField.textSelection.cursorIndex, 0, normalized.Length);
                    var select = Mathf.Clamp(_textField.textSelection.selectIndex, 0, normalized.Length);

                    _activeUndoHistory.Record(
                        normalized,
                        cursor,
                        select,
                        _forceHistoryBoundaryNextChange,
                        Time.realtimeSinceStartup,
                        UndoMergeWindowSeconds);

                    _forceHistoryBoundaryNextChange = false;
                    ScheduleHistorySelectionRefresh();
                }

                UpdateInternalScrollContentSize();
                RebuildGutter();
                RebuildIndentGuides();
                ScheduleSyntaxRefresh();
                ValidateExecutionLine();
                UpdateExecutionHighlightPosition();
                TextChanged?.Invoke(normalized);
            });

            HookTextInputKeyHandling();
            _textField.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                HookInternalScroll();

                // The TextField changes geometry when it is first enabled/opened.
                // Observe that real geometry so guides/gutter are rebuilt only after
                // UI Toolkit has laid out the editable input.
                schedule.Execute(() =>
                {
                    HookTextInputGeometry();
                    HookTextInputKeyHandling();
                    RefreshFontMetricsAndLayout();
                }).ExecuteLater(1);
            });

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // A paused schedule must not leave the syntax layer hidden on
                // the next attach. AttachToPanel will rebuild its alignment.
                _zoomLayoutSyncPassesRemaining = 0;
                _zoomAnchorPending = false;
                _zoomLayoutSyncTask?.Pause();
            });

            SetEnabled(false);
            SetTextWithoutNotify(string.Empty);
        }

        public void OpenDocument(
            string path,
            string content,
            IEnumerable<int> breakpoints = null)
        {
            OpenDocument(path, content, breakpoints, null);
        }

        internal void OpenDocument(
            string path,
            string content,
            IEnumerable<int> breakpoints,
            EditorUndoHistoryData persistedHistory)
        {
            CaptureActiveHistorySelection();

            // An anchor from the previous source file must never scroll the newly
            // opened document when a pending zoom callback arrives.
            _zoomAnchorPending = false;
            _zoomLayoutSyncPassesRemaining = 0;
            _zoomLayoutSyncTask?.Pause();
            DocumentPath = path ?? string.Empty;
            _breakpoints.Clear();
            ClearExecutionLine();

            if (breakpoints != null)
                foreach (var line in breakpoints.Where(x => x > 0))
                    _breakpoints.Add(line);

            var normalized = NormalizeLineEndings(content ?? string.Empty);
            if (!_undoHistories.TryGetValue(DocumentPath, out _activeUndoHistory))
            {
                _activeUndoHistory = persistedHistory != null
                    ? EditorUndoHistory.Import(persistedHistory, normalized, UndoHistoryCapacity)
                    : new EditorUndoHistory(normalized, 0, 0, UndoHistoryCapacity);

                _undoHistories[DocumentPath] = _activeUndoHistory;
            }
            else if (!string.Equals(_activeUndoHistory.Current.Text, normalized, StringComparison.Ordinal))
            {
                // External/editor-level buffer replacement (notably Revert) is kept as
                // one undoable action instead of destroying the document history.
                var previous = _activeUndoHistory.Current;
                _activeUndoHistory.Record(
                    normalized,
                    Mathf.Clamp(previous.Cursor, 0, normalized.Length),
                    Mathf.Clamp(previous.Select, 0, normalized.Length),
                    true,
                    Time.realtimeSinceStartup,
                    UndoMergeWindowSeconds);
            }

            SetEnabled(true);
            SetTextWithoutNotify(_activeUndoHistory.Current.Text);

            // Enabling the first document changes the internal TextField layout.
            // Recalculate on the next layout pass instead of trusting the geometry
            // that existed while the editor was disabled.
            SchedulePostLayoutRefresh();
            ScheduleSelectionRestore(_activeUndoHistory.Current);

            _textField.Focus();
        }

        public void ClearDocument()
        {
            CaptureActiveHistorySelection();
            _zoomAnchorPending = false;
            _zoomLayoutSyncPassesRemaining = 0;
            _zoomLayoutSyncTask?.Pause();
            _activeUndoHistory = null;
            DocumentPath = string.Empty;
            _breakpoints.Clear();
            ClearExecutionLine();
            SetTextWithoutNotify(string.Empty);
            RebuildGutter();
            RebuildIndentGuides();
            SetEnabled(false);
        }

        public void RemapDocumentHistory(string oldPath, string newPath)
        {
            oldPath = oldPath ?? string.Empty;
            newPath = newPath ?? string.Empty;

            var exact = _undoHistories.Keys
                .Where(key => string.Equals(key, oldPath, StringComparison.OrdinalIgnoreCase) ||
                              key.StartsWith(oldPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var key in exact)
            {
                var history = _undoHistories[key];
                _undoHistories.Remove(key);

                var mapped = string.Equals(key, oldPath, StringComparison.OrdinalIgnoreCase)
                    ? newPath
                    : newPath.TrimEnd('/') + key.Substring(oldPath.TrimEnd('/').Length);

                _undoHistories[mapped] = history;
            }

            if (!string.IsNullOrEmpty(DocumentPath) &&
                (string.Equals(DocumentPath, oldPath, StringComparison.OrdinalIgnoreCase) ||
                 DocumentPath.StartsWith(oldPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            {
                DocumentPath = string.Equals(DocumentPath, oldPath, StringComparison.OrdinalIgnoreCase)
                    ? newPath
                    : newPath.TrimEnd('/') + DocumentPath.Substring(oldPath.TrimEnd('/').Length);
            }
        }

        public void ForgetDocumentHistory(string path, bool includeChildren)
        {
            path = path ?? string.Empty;
            var prefix = path.TrimEnd('/') + "/";

            foreach (var key in _undoHistories.Keys
                         .Where(key => string.Equals(key, path, StringComparison.OrdinalIgnoreCase) ||
                                       (includeChildren && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                         .ToArray())
            {
                if (ReferenceEquals(_activeUndoHistory, _undoHistories[key]))
                    _activeUndoHistory = null;

                _undoHistories.Remove(key);
            }
        }

        internal EditorUndoHistoryData ExportDocumentHistory(string path)
        {
            path ??= string.Empty;

            if (string.Equals(DocumentPath, path, StringComparison.OrdinalIgnoreCase))
                CaptureActiveHistorySelection();

            return _undoHistories.TryGetValue(path, out var history)
                ? history.Export()
                : null;
        }

        public void SetTextWithoutNotify(string text)
        {
            _suppressChange = true;
            _textField.SetValueWithoutNotify(NormalizeLineEndings(text));
            _suppressChange = false;
            UpdateInternalScrollContentSize();
            RebuildGutter();
            RebuildIndentGuides();
            RefreshSyntaxImmediately();
            ValidateExecutionLine();
            UpdateExecutionHighlightPosition();
        }

        /// <summary>
        /// Highlights a one-based source line reported by VM debug metadata.
        /// The highlight follows vertical scrolling and optionally scrolls the line into view.
        /// </summary>
        public void SetExecutionLine(int oneBasedLine, bool ensureVisible = true)
        {
            if (oneBasedLine <= 0 || oneBasedLine > Math.Max(1, CountLines(_textField.value)))
            {
                ClearExecutionLine();
                return;
            }

            _executionLine = oneBasedLine;
            _executionLineHighlight.style.display = DisplayStyle.Flex;

            if (ensureVisible)
                ScrollExecutionLineIntoView();

            UpdateExecutionHighlightPosition();
        }

        public void ClearExecutionLine()
        {
            _executionLine = -1;
            _executionLineHighlight.style.display = DisplayStyle.None;
        }

        private void ValidateExecutionLine()
        {
            if (_executionLine <= 0)
                return;

            if (_executionLine > Math.Max(1, CountLines(_textField.value)))
                ClearExecutionLine();
        }

        private void ScrollExecutionLineIntoView()
        {
            if (_internalScroll == null || _executionLine <= 0)
                return;

            float viewportHeight = _internalScroll.contentViewport != null
                ? _internalScroll.contentViewport.resolvedStyle.height
                : _internalScroll.resolvedStyle.height;

            if (viewportHeight <= 0f)
                return;

            float lineTop = _textOriginY + ((_executionLine - 1) * _lineHeight);
            float lineBottom = lineTop + _lineHeight;
            Vector2 offset = _internalScroll.scrollOffset;
            float targetY = offset.y;

            if (lineTop < offset.y)
                targetY = lineTop;
            else if (lineBottom > offset.y + viewportHeight)
                targetY = lineBottom - viewportHeight;

            if (!Mathf.Approximately(targetY, offset.y))
            {
                _internalScroll.scrollOffset = new Vector2(
                    offset.x,
                    Mathf.Max(0f, targetY)
                );
            }
        }

        private void UpdateExecutionHighlightPosition()
        {
            if (_executionLine <= 0)
                return;

            float scrollY = _internalScroll?.scrollOffset.y ?? 0f;
            _executionLineHighlight.style.top =
                _textOriginY + ((_executionLine - 1) * _lineHeight) - scrollY;
            _executionLineHighlight.style.height = _lineHeight;
        }

        public bool Undo()
        {
            if (_activeUndoHistory == null)
                return false;

            var cursor = Mathf.Clamp(_textField.textSelection.cursorIndex, 0, Text.Length);
            var select = Mathf.Clamp(_textField.textSelection.selectIndex, 0, Text.Length);
            if (!_activeUndoHistory.TryUndo(cursor, select, out var snapshot))
                return false;

            ApplyHistorySnapshot(snapshot);
            return true;
        }

        public bool Redo()
        {
            if (_activeUndoHistory == null)
                return false;

            var cursor = Mathf.Clamp(_textField.textSelection.cursorIndex, 0, Text.Length);
            var select = Mathf.Clamp(_textField.textSelection.selectIndex, 0, Text.Length);
            if (!_activeUndoHistory.TryRedo(cursor, select, out var snapshot))
                return false;

            ApplyHistorySnapshot(snapshot);
            return true;
        }

        private void ApplyHistorySnapshot(EditorUndoHistory.Snapshot snapshot)
        {
            _forceHistoryBoundaryNextChange = false;
            _historyApplying = true;
            _textField.value = snapshot.Text;
            _historyApplying = false;
            ScheduleSelectionRestore(snapshot);
        }

        private void CaptureActiveHistorySelection()
        {
            if (_activeUndoHistory == null)
                return;

            var length = Text.Length;
            _activeUndoHistory.UpdateCurrentSelection(
                Mathf.Clamp(_textField.textSelection.cursorIndex, 0, length),
                Mathf.Clamp(_textField.textSelection.selectIndex, 0, length));
        }

        private void ScheduleHistorySelectionRefresh()
        {
            if (panel == null)
                return;

            schedule.Execute(CaptureActiveHistorySelection).ExecuteLater(1);
        }

        private void ScheduleSelectionRestore(EditorUndoHistory.Snapshot snapshot)
        {
            if (panel == null)
                return;

            schedule.Execute(() =>
            {
                var len = Text.Length;
                _textField.textSelection.SelectRange(
                    Mathf.Clamp(snapshot.Cursor, 0, len),
                    Mathf.Clamp(snapshot.Select, 0, len));
                _textField.Focus();
            }).ExecuteLater(1);
        }

        private void SchedulePostLayoutRefresh()
        {
            if (panel == null)
                return;

            schedule.Execute(() =>
            {
                HookTextInputGeometry();
                HookTextInputKeyHandling();
                RefreshFontMetricsAndLayout();
            }).ExecuteLater(1);
        }

        private void HookTextInputGeometry()
        {
            if (_textInputGeometryHooked)
                return;

            var input = GetTextInput();
            if (input == null)
                return;

            _textInputGeometryHooked = true;

            input.RegisterCallback<GeometryChangedEvent>(_ => RefreshAfterGeometryChange());
        }

        private void RefreshAfterGeometryChange()
        {
            // Font changes and splitter drags may change the input or viewport
            // while retaining the same TextField bounds. Always resample the
            // original native input rather than estimating its new position.
            RefreshTextOrigin();
            UpdateInternalScrollContentSize();
            RestoreZoomAnchor();
            RebuildGutter();
            RebuildIndentGuides();
            UpdateExecutionHighlightPosition();
            RefreshVisibleSyntaxLines();
        }

        private void HookTextInputKeyHandling()
        {
            if (_textInputKeyHooked)
                return;

            var input = GetTextInput();
            if (input == null)
                return;

            // Handle TAB on the actual editable input instead of the outer TextField.
            // This keeps our manual indentation logic tied to the same selection state
            // that UI Toolkit uses to draw and edit the caret.
            input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            input.RegisterCallback<KeyUpEvent>(_ => CaptureActiveHistorySelection());
            input.RegisterCallback<PointerUpEvent>(_ => CaptureActiveHistorySelection());
            _textInputKeyHooked = true;
        }

        public void FocusEditor() => _textField.Focus();

        public IReadOnlyCollection<int> GetBreakpoints() => _breakpoints;

        public void SetFontSize(float fontSize)
        {
            var clamped = Mathf.Clamp(fontSize, MinimumFontSize, MaximumFontSize);
            if (Mathf.Approximately(clamped, _fontSize))
                return;

            CaptureZoomAnchor();
            _fontSize = clamped;
            ApplyFontStyles();
            RequestZoomLayoutSync();
        }

        public void SetEditorPadding(
            float leftPx,
            float topPx,
            float rightPx,
            float bottomPx)
        {
            leftPx = Mathf.Max(0f, leftPx);
            topPx = Mathf.Max(0f, topPx);
            rightPx = Mathf.Max(0f, rightPx);
            bottomPx = Mathf.Max(0f, bottomPx);

            if (Mathf.Approximately(leftPx, _paddingLeftPx) &&
                Mathf.Approximately(topPx, _paddingTopPx) &&
                Mathf.Approximately(rightPx, _paddingRightPx) &&
                Mathf.Approximately(bottomPx, _paddingBottomPx))
            {
                return;
            }

            // Padding changes shift the native text as well; keep the same
            // document location in view while the layout is resampled.
            CaptureZoomAnchor();
            _paddingLeftPx = leftPx;
            _paddingTopPx = topPx;
            _paddingRightPx = rightPx;
            _paddingBottomPx = bottomPx;

            ApplyEditorLayoutStyles();
            RefreshTextOrigin();
            RebuildGutter();
            RebuildIndentGuides();
            RefreshVisibleSyntaxLines();
            // Input padding can also shift native glyphs on the next layout pass.
            RequestZoomLayoutSync();
        }

        public void SetIndentGuideFineOffsetPx(float offsetPx)
        {
            if (Mathf.Approximately(offsetPx, _indentGuideFineOffsetPx))
                return;

            _indentGuideFineOffsetPx = offsetPx;
            RebuildIndentGuides();
        }

        /// <summary>
        /// Runtime-friendly entry point for future Settings > Editor options.
        /// A private copy is used: changing user options cannot dirty the shared asset.
        /// </summary>
        public void SetSyntaxColors(DirectivaSyntaxColors colors)
        {
            _syntaxColors = (colors ?? new DirectivaSyntaxColors()).Copy();
            CacheSyntaxColors();
            _syntaxOverlay.style.display = _syntaxColors.enabled
                ? DisplayStyle.Flex : DisplayStyle.None;
            if (_syntaxColors.enabled) RefreshSyntaxImmediately();
            else
            {
                _syntaxRefreshTask?.Pause();
                UpdateSyntaxInputAppearance(false);
            }
        }

        public void SetSyntaxHighlightingEnabled(bool enabled)
        {
            if (_syntaxColors.enabled == enabled) return;
            _syntaxColors.enabled = enabled;
            _syntaxOverlay.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            if (enabled) RefreshSyntaxImmediately();
            else
            {
                _syntaxRefreshTask?.Pause();
                UpdateSyntaxInputAppearance(false);
            }
        }

        private void CacheSyntaxColors()
        {
            _syntaxColorHex.Clear();
            foreach (DScriptSyntaxKind kind in Enum.GetValues(typeof(DScriptSyntaxKind)))
            {
                Color color;
                switch (kind)
                {
                    case DScriptSyntaxKind.Keyword: color = _syntaxColors.keyword; break;
                    case DScriptSyntaxKind.Literal: color = _syntaxColors.literal; break;
                    case DScriptSyntaxKind.Number: color = _syntaxColors.number; break;
                    case DScriptSyntaxKind.String: color = _syntaxColors.stringLiteral; break;
                    case DScriptSyntaxKind.Comment: color = _syntaxColors.comment; break;
                    case DScriptSyntaxKind.Operator: color = _syntaxColors.operation; break;
                    case DScriptSyntaxKind.Variable: color = _syntaxColors.variable; break;
                    case DScriptSyntaxKind.Shared: color = _syntaxColors.sharedVariable; break;
                    case DScriptSyntaxKind.Function: color = _syntaxColors.function; break;
                    case DScriptSyntaxKind.Module: color = _syntaxColors.module; break;
                    case DScriptSyntaxKind.Member: color = _syntaxColors.member; break;
                    default: color = _syntaxColors.plain; break;
                }
                _syntaxColorHex[kind] = "#" + ColorUtility.ToHtmlStringRGBA(color);
            }
        }

        private string SyntaxHex(DScriptSyntaxKind kind) =>
            kind == DScriptSyntaxKind.Plain ? null : _syntaxColorHex[kind];

        private void ScheduleSyntaxRefresh()
        {
            if (!_syntaxColors.enabled) return;
            // Most scripts are small: color synchronously to avoid any visible
            // lag between caret movement and glyphs while the user is typing.
            if (panel == null || (_textField.value?.Length ?? 0) <= 16384)
            {
                _syntaxRefreshTask?.Pause();
                RefreshSyntaxImmediately();
                return;
            }
            // For very large drafts, debounce tokenization and show native text
            // temporarily rather than leaving old colored glyphs under the caret.
            _syntaxOverlay.style.display = DisplayStyle.None;
            UpdateSyntaxInputAppearance(false);
            if (_syntaxRefreshTask == null)
                _syntaxRefreshTask = schedule.Execute(RefreshSyntaxImmediately);
            _syntaxRefreshTask.ExecuteLater(SyntaxDebounceMilliseconds);
        }

        private void RefreshSyntaxImmediately()
        {
            if (!_syntaxColors.enabled) return;
            // No parser or compiler state is mutated: this is a tolerant preview.
            _syntaxDocument = DScriptSyntaxHighlighter.Analyze(_textField.value);
            RefreshVisibleSyntaxLines();
        }

        private void UpdateSyntaxInputAppearance(bool visibleOverlay)
        {
            if (_nativeTextHidden == visibleOverlay) return;
            var input = GetTextInput();
            if (input == null) return;
            // Keep the native caret and selection (customized via USS) while
            // hiding only the native glyphs, which the overlay replaces.
            input.style.color = visibleOverlay
                ? new StyleColor(Color.clear)
                : new StyleColor(StyleKeyword.Null);
            _nativeTextHidden = visibleOverlay;
        }

        private Label AcquireSyntaxLabel(int index)
        {
            while (_syntaxLabels.Count <= index)
            {
                var label = new Label();
                label.enableRichText = true;
                // A DScript literal containing the two characters \n must stay on
                // this line; only actual source newlines are laid out as lines.
                label.parseEscapeSequences = false;
                label.pickingMode = PickingMode.Ignore;
                label.AddToClassList("code-syntax-line");
                label.style.position = Position.Absolute;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.fontSize = _fontSize;
                label.style.marginLeft = 0f;
                label.style.marginTop = 0f;
                label.style.marginRight = 0f;
                label.style.marginBottom = 0f;
                label.style.paddingLeft = 0f;
                label.style.paddingTop = 0f;
                label.style.paddingRight = 0f;
                label.style.paddingBottom = 0f;
                label.style.unityTextAlign = TextAnchor.UpperLeft;
                if (_normalFont != null)
                    label.style.unityFontDefinition = new StyleFontDefinition(_normalFont);
                _syntaxOverlay.Add(label);
                _syntaxLabels.Add(label);
            }
            return _syntaxLabels[index];
        }

        private void RefreshVisibleSyntaxLines()
        {
            // During zoom the TextField/ScrollView may still contain pre-layout
            // glyph and scroll geometry. Draw native uncolored text until both
            // post-layout samples have completed; never show stale colored text
            // over the real caret and selection.
            if (_zoomLayoutSyncPassesRemaining > 0)
            {
                _syntaxOverlay.style.display = DisplayStyle.None;
                UpdateSyntaxInputAppearance(false);
                return;
            }

            if (!_syntaxColors.enabled || _syntaxDocument == null || _internalScroll == null || panel == null)
            {
                UpdateSyntaxInputAppearance(false);
                return;
            }

            var viewport = _internalScroll.contentViewport;
            if (viewport == null || viewport.resolvedStyle.height <= 0f ||
                viewport.resolvedStyle.width <= 0f || _lineHeight <= 0f || _characterWidth <= 0f)
            {
                UpdateSyntaxInputAppearance(false);
                return;
            }

            var offset = _internalScroll.scrollOffset;
            float height = viewport.resolvedStyle.height;
            float width = viewport.resolvedStyle.width;
            int first = Mathf.Max(0, Mathf.FloorToInt((offset.y - _textOriginY) / _lineHeight) - SyntaxOverscanLines);
            int last = Mathf.Min(_syntaxDocument.LineCount - 1,
                Mathf.CeilToInt((offset.y + height - _textOriginY) / _lineHeight) + SyntaxOverscanLines);
            int firstColumn = Mathf.Max(0, Mathf.FloorToInt((offset.x - _textOriginX) / _characterWidth) - SyntaxOverscanColumns);
            int lastColumn = Mathf.Max(firstColumn + 1,
                Mathf.CeilToInt((offset.x + width - _textOriginX) / _characterWidth) + SyntaxOverscanColumns);
            int count = 0;

            // Never paint over the scrollbars; x is relative to the editor stack.
            _syntaxOverlay.style.width = Mathf.Max(0f,
                viewport.worldBound.xMax - _editorStack.worldBound.xMin);
            _syntaxOverlay.style.display = DisplayStyle.Flex;
            for (int line = first; line <= last; line++)
            {
                var label = AcquireSyntaxLabel(count++);
                label.style.display = DisplayStyle.Flex;
                label.style.left = _textOriginX + firstColumn * _characterWidth - offset.x;
                label.style.top = _textOriginY + line * _lineHeight - offset.y;
                label.style.height = _lineHeight;
                label.style.color = _syntaxColors.plain;
                label.text = _syntaxDocument.FormatLine(line, firstColumn,
                    lastColumn - firstColumn, SyntaxHex);
            }
            for (int i = count; i < _syntaxLabels.Count; i++)
                _syntaxLabels[i].style.display = DisplayStyle.None;

            // Only make TextField glyphs transparent once a drawn overlay is ready.
            UpdateSyntaxInputAppearance(count > 0);
        }

        private void ApplyFontStyles()
        {
            style.fontSize = _fontSize;
            _gutterViewport.style.fontSize = _fontSize;
            _textField.style.fontSize = _fontSize;
            _metricsProbe.style.fontSize = _fontSize;
            foreach (var label in _syntaxLabels)
                label.style.fontSize = _fontSize;

            if (_normalFont != null)
            {
                var definition = new StyleFontDefinition(_normalFont);
                style.unityFontDefinition = definition;
                _gutterViewport.style.unityFontDefinition = definition;
                _textField.style.unityFontDefinition = definition;
                _metricsProbe.style.unityFontDefinition = definition;
                foreach (var label in _syntaxLabels)
                    label.style.unityFontDefinition = definition;
            }

            // The editable input has its own style node, so make font size and
            // FontAsset explicit there as well. This keeps it synchronized with
            // the metrics probe at every zoom level.
            var input = GetTextInput();
            if (input != null)
            {
                input.style.fontSize = _fontSize;

                if (_normalFont != null)
                    input.style.unityFontDefinition = new StyleFontDefinition(_normalFont);
            }
        }

        private void ApplyEditorLayoutStyles()
        {
            var input = GetTextInput();
            if (input == null)
                return;

            // These are the real padding values used by the editor. They are also
            // the single source of truth used when calculating guide positions.
            input.style.paddingLeft = _paddingLeftPx;
            input.style.paddingTop = _paddingTopPx;
            input.style.paddingRight = _paddingRightPx;
            input.style.paddingBottom = _paddingBottomPx;
        }

        private VisualElement GetTextInput()
        {
            return _textField.Q<VisualElement>(className: "unity-text-field__input");
        }

        private void OnWheel(WheelEvent evt)
        {
            if (evt.ctrlKey)
            {
                var direction = evt.delta.y < 0f ? 1f : -1f;
                var nextSize = Mathf.Clamp(
                    _fontSize + (direction * ZoomStep),
                    MinimumFontSize,
                    MaximumFontSize);

                if (!Mathf.Approximately(nextSize, _fontSize))
                {
                    CaptureZoomAnchor();
                    _fontSize = nextSize;
                    ApplyFontStyles();
                    RequestZoomLayoutSync();
                    FontSizeChanged?.Invoke(_fontSize);
                }

                // Ctrl+wheel belongs to editor zoom.
                evt.StopImmediatePropagation();
                return;
            }

            if (_internalScroll == null)
                return;

            // A deliberate scroll after zoom takes priority over an outstanding
            // anchor restoration; the scheduled geometry refresh still finishes.
            _zoomAnchorPending = false;

            // Handle vertical wheel scrolling explicitly. This avoids depending on
            // which internal TextField child happens to receive the WheelEvent.
            var wheelUnits = Mathf.Clamp(evt.delta.y, -3f, 3f);
            var deltaPixels = wheelUnits * Mathf.Max(24f, _lineHeight * 3f);

            var current = _internalScroll.scrollOffset;
            _internalScroll.scrollOffset = new Vector2(
                current.x,
                Mathf.Max(0f, current.y + deltaPixels));

            evt.StopImmediatePropagation();
        }

        // Measure a stable document coordinate BEFORE changing the font size.
        // This coordinate is measured in fractional source lines, not pixels, so
        // a partial line at the viewport center stays at the same screen position.
        // During rapid Ctrl+wheel events keep the first anchor, since intermediate
        // font layouts have not necessarily been committed yet.
        private void CaptureZoomAnchor()
        {
            if (_zoomAnchorPending || _internalScroll == null || panel == null)
                return;

            var viewport = _internalScroll.contentViewport;
            if (viewport == null || viewport.resolvedStyle.height <= 0f ||
                _lineHeight <= 0f)
                return;

            var viewportTop = viewport.worldBound.yMin - _editorStack.worldBound.yMin;
            _zoomAnchorLogicalLine = CalculateZoomAnchorLine(
                _internalScroll.scrollOffset.y, viewportTop,
                viewport.resolvedStyle.height, _textOriginY, _lineHeight);
            _zoomAnchorPending = true;
        }

        // Pure helpers so the zoom anchor can be tested without a live UI panel.
        // All viewport/origin positions use the same editor-stack coordinate space.
        internal static float CalculateZoomAnchorLine(
            float scrollY, float viewportTop, float viewportHeight,
            float textOriginY, float lineHeight)
        {
            if (lineHeight <= 0f || viewportHeight <= 0f)
                return 0f;

            return (scrollY + viewportTop +
                    ZoomAnchorViewportFraction * viewportHeight - textOriginY) /
                   lineHeight;
        }

        internal static float CalculateZoomAnchoredScrollY(
            float logicalLine, float viewportTop, float viewportHeight,
            float textOriginY, float lineHeight, float documentHeight)
        {
            if (lineHeight <= 0f || viewportHeight <= 0f)
                return 0f;

            var desired = textOriginY + logicalLine * lineHeight -
                          viewportTop - ZoomAnchorViewportFraction * viewportHeight;
            return Mathf.Clamp(desired, 0f,
                Mathf.Max(0f, documentHeight - viewportHeight));
        }

        private void RestoreZoomAnchor()
        {
            if (!_zoomAnchorPending || _internalScroll == null)
                return;

            var viewport = _internalScroll.contentViewport;
            if (viewport == null || viewport.resolvedStyle.height <= 0f)
                return;

            var viewportTop = viewport.worldBound.yMin - _editorStack.worldBound.yMin;
            var documentHeight = _paddingTopPx +
                Math.Max(1, CountLines(_textField.value)) * _lineHeight +
                _paddingBottomPx;
            var targetY = CalculateZoomAnchoredScrollY(
                _zoomAnchorLogicalLine, viewportTop, viewport.resolvedStyle.height,
                _textOriginY, _lineHeight, documentHeight);
            var current = _internalScroll.scrollOffset;
            if (!Mathf.Approximately(current.y, targetY))
                _internalScroll.scrollOffset = new Vector2(current.x, targetY);
        }

        private void RequestZoomLayoutSync()
        {
            if (panel == null)
            {
                _zoomAnchorPending = false;
                return; // AttachToPanel always rebuilds layout before display.
            }

            // An immediate refresh from WheelEvent would sample TextField's old
            // coordinates, before font reflow/scroll clamping. Reschedule rather
            // than accumulating callbacks when several wheel ticks arrive quickly.
            _zoomLayoutSyncPassesRemaining = 2;
            _syntaxOverlay.style.display = DisplayStyle.None;
            UpdateSyntaxInputAppearance(false);
            if (_zoomLayoutSyncTask == null)
            {
                // schedule.Execute() defaults to a one-shot item. Calling
                // ExecuteLater() from that item's own callback does not reliably
                // keep it alive for a second pass. Use a repeating item and
                // explicitly pause it after both post-layout samples.
                _zoomLayoutSyncTask = schedule.Execute(ContinueZoomLayoutSync)
                    .Every(ZoomLayoutFollowupMilliseconds);
            }
            _zoomLayoutSyncTask.ExecuteLater(1);
        }

        private void ContinueZoomLayoutSync()
        {
            if (panel == null || _zoomLayoutSyncPassesRemaining <= 0)
            {
                _zoomLayoutSyncPassesRemaining = 0;
                _zoomAnchorPending = false;
                _zoomLayoutSyncTask?.Pause();
                return;
            }

            // The repeating scheduler supplies the follow-up frame; do not
            // reschedule a one-shot callback from inside itself. Keep the native
            // text visible during the first layout sample and restore the colored
            // overlay on the final sample.
            _zoomLayoutSyncPassesRemaining--;
            if (_zoomLayoutSyncPassesRemaining == 0)
                _zoomLayoutSyncTask?.Pause();

            RefreshFontMetricsAndLayout();
            if (_zoomLayoutSyncPassesRemaining == 0)
                _zoomAnchorPending = false;
        }

        private void RefreshFontMetricsAndLayout()
        {
            ApplyFontStyles();
            ApplyEditorLayoutStyles();

            if (!TryReadFontAssetMetrics())
                ReadFallbackMeasuredMetrics();

            RefreshTextOrigin();
            UpdateInternalScrollContentSize();
            RestoreZoomAnchor();
            RebuildGutter();
            RebuildIndentGuides();
            UpdateExecutionHighlightPosition();
            RefreshVisibleSyntaxLines();
        }

        private bool TryReadFontAssetMetrics()
        {
            if (_normalFont == null)
                return false;

            var face = _normalFont.faceInfo;
            if (face.pointSize <= 0.01f)
                return false;

            // TextCore stores glyph metrics at the FontAsset's sampling point size.
            // Convert them to the editor's current point size using the same base
            // scale relationship used by Unity's text layout:
            //
            // currentSize / sampledPointSize * faceScale
            var faceScale = Mathf.Approximately(face.scale, 0f) ? 1f : face.scale;
            var fontScale = (_fontSize / face.pointSize) * faceScale;

            var characters = _normalFont.characterTable;
            if (characters == null)
                return false;

            var spaceCharacter = characters.FirstOrDefault(
                character => character != null && character.unicode == 32u);

            if (spaceCharacter == null || spaceCharacter.glyph == null)
                return false;

            var spaceAdvance = spaceCharacter.glyph.metrics.horizontalAdvance;
            var lineHeight = face.lineHeight;

            if (spaceAdvance <= 0.01f || lineHeight <= 0.01f)
                return false;

            // The editor indents with literal spaces, so the space glyph advance is
            // the exact horizontal unit needed by indentation guides. This also
            // means changing FontAsset automatically changes the guide spacing.
            _characterWidth = spaceAdvance * fontScale;
            _lineHeight = lineHeight * fontScale;

            return _characterWidth > 0.01f && _lineHeight > 0.01f;
        }

        private void ReadFallbackMeasuredMetrics()
        {
            // Safety fallback only. The normal path above reads metrics directly
            // from FontAsset / GlyphMetrics / FaceInfo.
            var spaces = "    ";
            var measuredSpaces = _metricsProbe.MeasureTextSize(
                spaces,
                0f,
                VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined);

            if (measuredSpaces.x > 0.01f)
                _characterWidth = measuredSpaces.x / spaces.Length;

            var twoLines = _metricsProbe.MeasureTextSize(
                "M\\nM",
                0f,
                VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined);

            if (twoLines.y > 0.01f)
                _lineHeight = twoLines.y / 2f;
        }

        // Pure coordinate conversion, shared by syntax, line numbers and guides.
        // IMPORTANT: "unity-text-field__input" is the STATIONARY parent of the
        // multiline TextField's ScrollView, not its scrolling contentContainer.
        // The native input bounds must therefore NOT be combined with scrollOffset.
        // The actual scrolling displacement is applied exactly once by each layer
        // when rendering (e.g. labelTop = originY + line * lineHeight - offsetY).
        internal static Vector2 CalculateTextOrigin(
            Rect inputWorldBounds, Rect editorStackWorldBounds,
            float borderLeftPx, float borderTopPx, float paddingLeftPx, float paddingTopPx)
        {
            return new Vector2(
                inputWorldBounds.xMin - editorStackWorldBounds.xMin +
                borderLeftPx + paddingLeftPx,
                inputWorldBounds.yMin - editorStackWorldBounds.yMin +
                borderTopPx + paddingTopPx);
        }

        private void RefreshTextOrigin()
        {
            var input = GetTextInput();
            if (input == null)
                return;

            var origin = CalculateTextOrigin(
                input.worldBound, _editorStack.worldBound,
                input.resolvedStyle.borderLeftWidth,
                input.resolvedStyle.borderTopWidth,
                _paddingLeftPx, _paddingTopPx);
            _textOriginX = origin.x;
            _textOriginY = origin.y;
        }

        private void HookInternalScroll()
        {
            schedule.Execute(() =>
            {
                _internalScroll = _textField.Q<ScrollView>();
                if (_internalScroll == null)
                    return;

                _internalScroll.AddToClassList("directiva-scroll-view");

                // A flex child with the default minimum height can grow to the
                // whole text content, leaving nothing for the ScrollView to scroll.
                // Force the internal viewport to stay inside the editor bounds.
                _internalScroll.style.flexGrow = 1f;
                _internalScroll.style.flexShrink = 1f;
                _internalScroll.style.minHeight = 0f;
                _internalScroll.style.height = Length.Percent(100);

                _internalScroll.verticalScrollerVisibility =
                    ScrollerVisibility.AlwaysVisible;

                _internalScroll.horizontalScrollerVisibility =
                    ScrollerVisibility.Auto;

                UpdateInternalScrollContentSize();

                _internalScroll.verticalScroller.valueChanged += value =>
                {
                    _gutterContent.style.translate = new Translate(0f, -value);
                    UpdateIndentGuideTranslation();
                    UpdateExecutionHighlightPosition();
                    RefreshVisibleSyntaxLines();
                };

                _internalScroll.horizontalScroller.valueChanged += _ =>
                {
                    UpdateIndentGuideTranslation();
                    RefreshVisibleSyntaxLines();
                };

                if (_internalScroll.contentViewport != null)
                    _internalScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(
                        _ => RefreshAfterGeometryChange());

                RefreshTextOrigin();
                UpdateInternalScrollContentSize();
                RebuildGutter();
                RebuildIndentGuides();
                UpdateIndentGuideTranslation();
                UpdateExecutionHighlightPosition();
                RefreshVisibleSyntaxLines();
            }).ExecuteLater(10);
        }

        private void UpdateInternalScrollContentSize()
        {
            if (_internalScroll == null)
                return;

            var lineCount = Math.Max(1, CountLines(_textField.value));

            var documentHeight =
                _paddingTopPx +
                (lineCount * _lineHeight) +
                _paddingBottomPx;

            // Give ScrollView a deterministic content height. Without this,
            // TextField can sometimes expand its internal content to fit all text,
            // leaving the ScrollView with no overflow range.
            _internalScroll.contentContainer.style.minHeight = documentHeight;

            // Keep the current offset valid if document size shrinks.
            var viewportHeight =
                _internalScroll.contentViewport != null
                    ? _internalScroll.contentViewport.resolvedStyle.height
                    : _internalScroll.resolvedStyle.height;

            if (viewportHeight > 0f)
            {
                var maxY = Mathf.Max(0f, documentHeight - viewportHeight);
                var offset = _internalScroll.scrollOffset;

                if (offset.y > maxY)
                    _internalScroll.scrollOffset =
                        new Vector2(offset.x, maxY);
            }
        }

        private void RebuildIndentGuides()
        {
            _indentGuideLayer.Clear();

            var source = _textField.value ?? string.Empty;
            var lines = source.Replace("\r\n", "\n").Split('\n');

            var maxDepth = 0;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var leadingSpaces = 0;

                while (leadingSpaces < line.Length && line[leadingSpaces] == ' ')
                    leadingSpaces++;

                if (leadingSpaces <= 0)
                    continue;

                // Any partial tab stop still visually belongs to that indentation depth.
                var depth = (leadingSpaces + TabSize - 1) / TabSize;
                maxDepth = Mathf.Max(maxDepth, depth);

                for (var level = 1; level <= depth; level++)
                {
                    var guide = new VisualElement();
                    guide.AddToClassList("indent-guide");
                    guide.pickingMode = PickingMode.Ignore;

                    guide.style.left =
                        _textOriginX +
                        ((level - 1) * TabSize * _characterWidth) +
                        _indentGuideFineOffsetPx;

                    guide.style.top = _textOriginY + (lineIndex * _lineHeight);
                    guide.style.height = _lineHeight;

                    _indentGuideLayer.Add(guide);
                }
            }

            _indentGuideLayer.style.width =
                Mathf.Max(
                    resolvedStyle.width,
                    _textOriginX +
                    ((maxDepth + 1) * TabSize * _characterWidth) +
                    Mathf.Max(0f, _indentGuideFineOffsetPx));

            _indentGuideLayer.style.height =
                _textOriginY + Mathf.Max(1, lines.Length) * _lineHeight;

            UpdateIndentGuideTranslation();
        }

        private void UpdateIndentGuideTranslation()
        {
            if (_internalScroll == null)
                return;

            var offset = _internalScroll.scrollOffset;
            _indentGuideLayer.style.translate = new Translate(-offset.x, -offset.y);
        }

        private void RebuildGutter()
        {
            _gutterContent.Clear();

            _gutterContent.style.paddingTop = _textOriginY;

            var lineCount = Math.Max(1, CountLines(_textField.value));
            for (var line = 1; line <= lineCount; line++)
            {
                var lineNumber = line;
                var row = new VisualElement();
                row.AddToClassList("gutter-row");
                row.style.height = _lineHeight;
                row.style.minHeight = _lineHeight;

                var breakpoint = new Button(() => ToggleBreakpoint(lineNumber));
                breakpoint.AddToClassList("breakpoint-button");
                breakpoint.style.height = _lineHeight;
                breakpoint.style.minHeight = _lineHeight;
                breakpoint.style.fontSize = _fontSize;
                UIIconView.SetButton(breakpoint,
                    _breakpoints.Contains(lineNumber) ? _breakpointSprite : null,
                    Mathf.Max(8f, Mathf.Min(14f, _fontSize)));
                breakpoint.tooltip = "Breakpoint";

                var number = new Label(lineNumber.ToString());
                number.AddToClassList("line-number");
                number.style.fontSize = _fontSize;

                if (_normalFont != null)
                    number.style.unityFontDefinition = new StyleFontDefinition(_normalFont);

                row.Add(breakpoint);
                row.Add(number);
                _gutterContent.Add(row);
            }

            foreach (var bp in _breakpoints.Where(x => x > lineCount).ToArray())
                _breakpoints.Remove(bp);

            // A zoom may clamp ScrollView.scrollOffset without emitting another
            // slider callback. Rebuild the gutter at the actual committed offset,
            // instead of keeping the translation from before the font reflow.
            if (_internalScroll != null)
                _gutterContent.style.translate = new Translate(
                    0f, -_internalScroll.scrollOffset.y);
        }

        private void ToggleBreakpoint(int line)
        {
            bool added;
            if (_breakpoints.Contains(line))
            {
                _breakpoints.Remove(line);
                added = false;
            }
            else
            {
                _breakpoints.Add(line);
                added = true;
            }

            RebuildGutter();
            BreakpointChanged?.Invoke(line, added);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            var actionKey = evt.ctrlKey || evt.commandKey;

            if (actionKey && evt.keyCode == KeyCode.Z)
            {
                evt.StopImmediatePropagation();
                if (evt.shiftKey)
                    Redo();
                else
                    Undo();
                return;
            }

            if (actionKey && evt.keyCode == KeyCode.Y)
            {
                evt.StopImmediatePropagation();
                Redo();
                return;
            }

            // Paste/cut and line breaks should form their own undo action rather than
            // merging into the surrounding run of normal typing.
            if (actionKey && (evt.keyCode == KeyCode.V || evt.keyCode == KeyCode.X))
                _forceHistoryBoundaryNextChange = true;
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                _forceHistoryBoundaryNextChange = true;

            if (evt.keyCode != KeyCode.Tab)
                return;

            evt.StopImmediatePropagation();
            _forceHistoryBoundaryNextChange = true;

            var text = NormalizeLineEndings(_textField.value);
            var cursor = Mathf.Clamp(_textField.textSelection.cursorIndex, 0, text.Length);
            var select = Mathf.Clamp(_textField.textSelection.selectIndex, 0, text.Length);

            if (cursor != select)
            {
                ApplyBlockIndent(text, cursor, select, evt.shiftKey);
                return;
            }

            if (evt.shiftKey)
                ApplySingleLineOutdent(text, cursor);
            else
                ApplySingleTab(text, cursor);
        }

        private void ApplySingleTab(string text, int caret)
        {
            // TAB indents the whole current line, regardless of where the caret is.
            // Keep the caret at the same logical position within the line by shifting it
            // along with the text that was moved to the right.
            var lineStart = FindLineStart(text, caret);
            var spaces = new string(' ', TabSize);
            var updated = text.Insert(lineStart, spaces);
            var newCaret = caret + TabSize;
            ApplyEdit(updated, newCaret, newCaret);
        }

        private void ApplySingleLineOutdent(string text, int caret)
        {
            var lineStart = FindLineStart(text, caret);
            var leading = CountLeadingSpaces(text, lineStart);

            if (leading <= 0)
                return;

            var remove = leading % TabSize;
            if (remove == 0)
                remove = Math.Min(TabSize, leading);

            var updated = text.Remove(lineStart, remove);
            var newCaret = Math.Max(lineStart, caret - remove);
            ApplyEdit(updated, newCaret, newCaret);
        }

        private void ApplyBlockIndent(string text, int a, int b, bool outdent)
        {
            var start = Math.Min(a, b);
            var end = Math.Max(a, b);

            var firstLineStart = FindLineStart(text, start);
            var lastRelevant = end;

            if (end > firstLineStart && end <= text.Length &&
                end > 0 && text[end - 1] == '\n')
            {
                lastRelevant = end - 1;
            }

            var starts = GetLineStarts(text, firstLineStart, lastRelevant);
            var updated = text;
            var totalDelta = 0;

            if (!outdent)
            {
                for (var i = starts.Count - 1; i >= 0; i--)
                    updated = updated.Insert(starts[i], new string(' ', TabSize));

                totalDelta = TabSize * starts.Count;
                ApplyEdit(updated, firstLineStart, end + totalDelta);
                return;
            }

            var removedTotal = 0;
            for (var i = starts.Count - 1; i >= 0; i--)
            {
                var lineStart = starts[i];
                var leading = CountLeadingSpaces(updated, lineStart);
                if (leading <= 0)
                    continue;

                var remove = leading % TabSize;
                if (remove == 0)
                    remove = Math.Min(TabSize, leading);

                updated = updated.Remove(lineStart, remove);
                removedTotal += remove;
            }

            ApplyEdit(updated, firstLineStart, Math.Max(firstLineStart, end - removedTotal));
        }

        private void ApplyEdit(string updated, int cursor, int select)
        {
            _forceHistoryBoundaryNextChange = true;
            _textField.value = updated;
            schedule.Execute(() =>
            {
                var len = _textField.value?.Length ?? 0;
                _textField.textSelection.SelectRange(
                    Mathf.Clamp(cursor, 0, len),
                    Mathf.Clamp(select, 0, len));
                _textField.Focus();
            }).ExecuteLater(1);
        }

        private static int FindLineStart(string text, int index)
        {
            index = Mathf.Clamp(index, 0, text.Length);
            if (index == 0)
                return 0;

            var previous = text.LastIndexOf('\n', Math.Max(0, index - 1));
            return previous < 0 ? 0 : previous + 1;
        }

        private static int CountLeadingSpaces(string text, int lineStart)
        {
            var count = 0;
            for (var i = lineStart; i < text.Length; i++)
            {
                if (text[i] == ' ')
                    count++;
                else
                    break;
            }
            return count;
        }

        private static List<int> GetLineStarts(string text, int firstStart, int lastRelevant)
        {
            var starts = new List<int> { firstStart };
            var i = firstStart;

            while (i < text.Length && i < lastRelevant)
            {
                var newline = text.IndexOf('\n', i);
                if (newline < 0 || newline + 1 > lastRelevant)
                    break;

                starts.Add(newline + 1);
                i = newline + 1;
            }

            return starts;
        }

        private static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 1;

            var lines = 1;
            foreach (var c in text)
                if (c == '\n')
                    lines++;

            return lines;
        }
    }
}
