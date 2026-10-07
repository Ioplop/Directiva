using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    public sealed class CodeEditorView : VisualElement
    {
        private const int TabSize = 4;

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

        private readonly FontAsset _normalFont;
        private readonly FontAsset _boldFont;

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
        private readonly Label _metricsProbe;
        private readonly TextField _textField;
        private readonly HashSet<int> _breakpoints = new HashSet<int>();

        private ScrollView _internalScroll;
        private bool _textInputGeometryHooked;
        private bool _suppressChange;
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
            float indentGuideFineOffsetPx)
        {
            _normalFont = normalFont;
            _boldFont = boldFont;
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
            _editorStack.Add(_metricsProbe);

            Add(_gutterViewport);
            Add(_editorStack);

            _textField.RegisterValueChangedCallback(evt =>
            {
                if (_suppressChange)
                    return;

                UpdateInternalScrollContentSize();
                RebuildGutter();
                RebuildIndentGuides();
                ValidateExecutionLine();
                UpdateExecutionHighlightPosition();
                TextChanged?.Invoke(evt.newValue ?? string.Empty);
            });

            _textField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
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
                    RefreshFontMetricsAndLayout();
                }).ExecuteLater(1);
            });

            SetEnabled(false);
            SetTextWithoutNotify(string.Empty);
        }

        public void OpenDocument(string path, string content, IEnumerable<int> breakpoints = null)
        {
            DocumentPath = path ?? string.Empty;
            _breakpoints.Clear();
            ClearExecutionLine();

            if (breakpoints != null)
                foreach (var line in breakpoints.Where(x => x > 0))
                    _breakpoints.Add(line);

            SetEnabled(true);
            SetTextWithoutNotify(content ?? string.Empty);

            // Enabling the first document changes the internal TextField layout.
            // Recalculate on the next layout pass instead of trusting the geometry
            // that existed while the editor was disabled.
            SchedulePostLayoutRefresh();

            _textField.Focus();
        }

        public void ClearDocument()
        {
            DocumentPath = string.Empty;
            _breakpoints.Clear();
            ClearExecutionLine();
            SetTextWithoutNotify(string.Empty);
            RebuildGutter();
            RebuildIndentGuides();
            SetEnabled(false);
        }

        public void SetTextWithoutNotify(string text)
        {
            _suppressChange = true;
            _textField.SetValueWithoutNotify(text ?? string.Empty);
            _suppressChange = false;
            UpdateInternalScrollContentSize();
            RebuildGutter();
            RebuildIndentGuides();
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

        private void SchedulePostLayoutRefresh()
        {
            if (panel == null)
                return;

            schedule.Execute(() =>
            {
                HookTextInputGeometry();
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

            input.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // Geometry changes when the editor is first enabled, resized, or
                // relaid out. These are exactly the moments when text origin must
                // be sampled again.
                RefreshTextOrigin();
                UpdateInternalScrollContentSize();
                RebuildGutter();
                RebuildIndentGuides();
                UpdateExecutionHighlightPosition();
            });
        }

        public void FocusEditor() => _textField.Focus();

        public IReadOnlyCollection<int> GetBreakpoints() => _breakpoints;

        public void SetFontSize(float fontSize)
        {
            var clamped = Mathf.Clamp(fontSize, MinimumFontSize, MaximumFontSize);
            if (Mathf.Approximately(clamped, _fontSize))
                return;

            _fontSize = clamped;
            ApplyFontStyles();

            if (panel != null)
                schedule.Execute(RefreshFontMetricsAndLayout).ExecuteLater(1);
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

            _paddingLeftPx = leftPx;
            _paddingTopPx = topPx;
            _paddingRightPx = rightPx;
            _paddingBottomPx = bottomPx;

            ApplyEditorLayoutStyles();
            RefreshTextOrigin();
            RebuildGutter();
            RebuildIndentGuides();
        }

        public void SetIndentGuideFineOffsetPx(float offsetPx)
        {
            if (Mathf.Approximately(offsetPx, _indentGuideFineOffsetPx))
                return;

            _indentGuideFineOffsetPx = offsetPx;
            RebuildIndentGuides();
        }

        private void ApplyFontStyles()
        {
            style.fontSize = _fontSize;
            _gutterViewport.style.fontSize = _fontSize;
            _textField.style.fontSize = _fontSize;
            _metricsProbe.style.fontSize = _fontSize;

            if (_normalFont != null)
            {
                var definition = new StyleFontDefinition(_normalFont);
                style.unityFontDefinition = definition;
                _gutterViewport.style.unityFontDefinition = definition;
                _textField.style.unityFontDefinition = definition;
                _metricsProbe.style.unityFontDefinition = definition;
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
                    _fontSize = nextSize;
                    ApplyFontStyles();
                    RefreshFontMetricsAndLayout();
                    FontSizeChanged?.Invoke(_fontSize);
                }

                // Ctrl+wheel belongs to editor zoom.
                evt.StopImmediatePropagation();
                return;
            }

            if (_internalScroll == null)
                return;

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

        private void RefreshFontMetricsAndLayout()
        {
            ApplyFontStyles();
            ApplyEditorLayoutStyles();

            if (!TryReadFontAssetMetrics())
                ReadFallbackMeasuredMetrics();

            RefreshTextOrigin();
            UpdateInternalScrollContentSize();
            RebuildGutter();
            RebuildIndentGuides();
            UpdateExecutionHighlightPosition();
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

        private void RefreshTextOrigin()
        {
            var input = GetTextInput();
            if (input == null)
                return;

            var scrollOffset = _internalScroll?.scrollOffset ?? Vector2.zero;

            // Convert the input's actual top-left into editor-stack content
            // coordinates. Adding scrollOffset cancels the viewport translation,
            // leaving a stable origin independent of current scroll position.
            var inputX =
                input.worldBound.xMin -
                _editorStack.worldBound.xMin +
                scrollOffset.x;

            var inputY =
                input.worldBound.yMin -
                _editorStack.worldBound.yMin +
                scrollOffset.y;

            _textOriginX =
                inputX +
                input.resolvedStyle.borderLeftWidth +
                _paddingLeftPx;

            _textOriginY =
                inputY +
                input.resolvedStyle.borderTopWidth +
                _paddingTopPx;
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
                };

                _internalScroll.horizontalScroller.valueChanged += _ =>
                {
                    UpdateIndentGuideTranslation();
                };

                RefreshTextOrigin();
                UpdateInternalScrollContentSize();
                RebuildGutter();
                RebuildIndentGuides();
                UpdateIndentGuideTranslation();
                UpdateExecutionHighlightPosition();
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
                breakpoint.text = _breakpoints.Contains(lineNumber) ? "●" : string.Empty;
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
            if (evt.keyCode != KeyCode.Tab)
                return;

            evt.StopImmediatePropagation();

            var text = _textField.value ?? string.Empty;
            var cursor = Mathf.Clamp(_textField.cursorIndex, 0, text.Length);
            var select = Mathf.Clamp(_textField.selectIndex, 0, text.Length);

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
            var lineStart = FindLineStart(text, caret);
            var column = caret - lineStart;
            var count = TabSize - (column % TabSize);
            if (count == 0)
                count = TabSize;

            var spaces = new string(' ', count);
            var updated = text.Insert(caret, spaces);
            ApplyEdit(updated, caret + count, caret + count);
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
            _textField.value = updated;
            schedule.Execute(() =>
            {
                var len = _textField.value?.Length ?? 0;
                _textField.SelectRange(Mathf.Clamp(cursor, 0, len), Mathf.Clamp(select, 0, len));
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
