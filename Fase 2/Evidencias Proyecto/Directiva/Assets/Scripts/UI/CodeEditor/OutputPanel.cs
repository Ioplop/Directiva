using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    public sealed class OutputPanel : VisualElement
    {
        private readonly OutputService _output;
        private readonly LocalizationService _locale;

        private readonly Button _collapseButton;
        private readonly Label _title;
        private readonly Toggle _messagesToggle;
        private readonly Toggle _warningsToggle;
        private readonly Toggle _errorsToggle;
        private readonly Label _messagesLabel;
        private readonly Label _warningsLabel;
        private readonly Label _errorsLabel;
        private readonly Button _clearButton;
        private readonly VisualElement _header;
        private readonly ScrollView _scroll;
        private readonly VisualElement _body;
        private readonly VisualElement _resizeHandle;

        private const float CollapsedHeight = 32f;
        private const float DefaultExpandedHeight = 210f;
        private const float MinimumFontSize = 6f;
        private const float MaximumFontSize = 48f;
        private const float ZoomStep = 1f;

        private float _fontSize;

        private bool _collapsed;
        private float _expandedHeight = DefaultExpandedHeight;
        private OutputSeverity? _pendingSeverity;
        private bool _resizing;
        private float _resizeStartY;
        private float _resizeStartHeight;

        public event Action<float> FontSizeChanged;

        public OutputPanel(
            OutputService output,
            LocalizationService locale,
            float fontSize)
        {
            _output = output;
            _locale = locale;
            _fontSize = UnityEngine.Mathf.Clamp(fontSize, MinimumFontSize, MaximumFontSize);

            AddToClassList("output-panel");
            style.fontSize = _fontSize;

            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);

            _resizeHandle = new VisualElement();
            _resizeHandle.AddToClassList("output-resize-handle");
            Add(_resizeHandle);

            _header = new VisualElement();
            _header.AddToClassList("output-header");

            _collapseButton = new Button(ToggleCollapsed);
            _collapseButton.AddToClassList("icon-button");

            _title = new Label();
            _title.AddToClassList("output-title");

            _messagesToggle = new Toggle { value = true };
            _warningsToggle = new Toggle { value = true };
            _errorsToggle = new Toggle { value = true };

            _messagesToggle.AddToClassList("output-filter-toggle");
            _warningsToggle.AddToClassList("output-filter-toggle");
            _errorsToggle.AddToClassList("output-filter-toggle");

            _messagesLabel = new Label();
            _warningsLabel = new Label();
            _errorsLabel = new Label();

            var messagesFilter = CreateFilterGroup(_messagesToggle, _messagesLabel);
            var warningsFilter = CreateFilterGroup(_warningsToggle, _warningsLabel);
            var errorsFilter = CreateFilterGroup(_errorsToggle, _errorsLabel);

            _clearButton = new Button(() => _output.Clear());

            ApplyFontSizeToStaticControls();

            _header.Add(_collapseButton);
            _header.Add(_title);
            _header.Add(messagesFilter);
            _header.Add(warningsFilter);
            _header.Add(errorsFilter);
            _header.Add(new VisualElement { style = { flexGrow = 1 } });
            _header.Add(_clearButton);

            _body = new VisualElement();
            _body.AddToClassList("output-body");

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("output-scroll");
            _scroll.AddToClassList("directiva-scroll-view");
            _body.Add(_scroll);

            Add(_header);
            Add(_body);

            _output.EntryAdded += OnEntryAdded;
            _output.Cleared += Rebuild;
            _locale.LocaleChanged += _ => RefreshLocale();

            _messagesToggle.RegisterValueChangedCallback(_ => Rebuild());
            _warningsToggle.RegisterValueChangedCallback(_ => Rebuild());
            _errorsToggle.RegisterValueChangedCallback(_ => Rebuild());

            _resizeHandle.RegisterCallback<PointerDownEvent>(OnResizeDown);
            _resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            _resizeHandle.RegisterCallback<PointerUpEvent>(OnResizeUp);
            _resizeHandle.RegisterCallback<PointerCaptureOutEvent>(_ => _resizing = false);

            RefreshLocale();
            Rebuild();
        }

        public void RegisterTooltips(HoverTooltipController tooltips)
        {
            if (tooltips == null)
                return;

            tooltips.Register(
                _collapseButton,
                () => _collapsed
                    ? _locale.Get("Tooltips", "expand_output")
                    : _locale.Get("Tooltips", "collapse_output"));

            tooltips.Register(
                _clearButton,
                () => _locale.Get("Tooltips", "clear_output"));
        }

        public void SetFontSize(float fontSize)
        {
            var clamped = UnityEngine.Mathf.Clamp(
                fontSize,
                MinimumFontSize,
                MaximumFontSize);

            if (UnityEngine.Mathf.Approximately(clamped, _fontSize))
                return;

            _fontSize = clamped;
            style.fontSize = _fontSize;
            ApplyFontSizeToStaticControls();
            Rebuild();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (!evt.ctrlKey)
                return;

            var direction = evt.delta.y < 0f ? 1f : -1f;
            var next = UnityEngine.Mathf.Clamp(
                _fontSize + (direction * ZoomStep),
                MinimumFontSize,
                MaximumFontSize);

            if (!UnityEngine.Mathf.Approximately(next, _fontSize))
            {
                _fontSize = next;
                style.fontSize = _fontSize;
                ApplyFontSizeToStaticControls();
                Rebuild();
                FontSizeChanged?.Invoke(_fontSize);
            }

            // Ctrl+wheel belongs to Output zoom, not Output scrolling.
            evt.StopImmediatePropagation();
        }

        private void ApplyFontSizeToStaticControls()
        {
            var controlHeight = UnityEngine.Mathf.Max(24f, _fontSize + 8f);
            var headerHeight = UnityEngine.Mathf.Max(32f, controlHeight + 4f);

            _header.style.height = headerHeight;
            _header.style.minHeight = headerHeight;

            _collapseButton.style.fontSize = _fontSize;
            _collapseButton.style.height = controlHeight;

            _title.style.fontSize = _fontSize;
            _title.style.height = controlHeight;
            _title.style.unityTextAlign = TextAnchor.MiddleLeft;

            _messagesToggle.style.fontSize = _fontSize;
            _warningsToggle.style.fontSize = _fontSize;
            _errorsToggle.style.fontSize = _fontSize;

            _messagesLabel.style.fontSize = _fontSize;
            _messagesLabel.style.height = controlHeight;
            _messagesLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            _warningsLabel.style.fontSize = _fontSize;
            _warningsLabel.style.height = controlHeight;
            _warningsLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            _errorsLabel.style.fontSize = _fontSize;
            _errorsLabel.style.height = controlHeight;
            _errorsLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            _clearButton.style.fontSize = _fontSize;
            _clearButton.style.height = controlHeight;

            if (_collapsed)
                style.height = headerHeight;
        }

        private float GetCollapsedHeight()
        {
            return UnityEngine.Mathf.Max(32f, _fontSize + 12f);
        }

        private static VisualElement CreateFilterGroup(Toggle toggle, Label label)
        {
            var group = new VisualElement();
            group.AddToClassList("output-filter-group");

            toggle.label = string.Empty;
            label.AddToClassList("output-filter-label");

            group.Add(toggle);
            group.Add(label);
            return group;
        }

        private void RefreshLocale()
        {
            _title.text = _locale.Get("Output", "title");
            _messagesLabel.text = _locale.Get("Output", "messages");
            _warningsLabel.text = _locale.Get("Output", "warnings");
            _errorsLabel.text = _locale.Get("Output", "errors");
            _clearButton.text = _locale.Get("Output", "clear");
            _collapseButton.text = _collapsed ? "▲" : "▼";
        }

        private void OnEntryAdded(OutputEntry entry)
        {
            if (_collapsed)
            {
                if (!_pendingSeverity.HasValue || entry.Severity > _pendingSeverity.Value)
                    _pendingSeverity = entry.Severity;

                FlashCollapsed(_pendingSeverity.Value);
                return;
            }

            AddEntryIfVisible(entry);
            schedule.Execute(() => _scroll.scrollOffset =
                new UnityEngine.Vector2(_scroll.scrollOffset.x, _scroll.contentContainer.layout.height))
                .ExecuteLater(1);
        }

        private void ToggleCollapsed()
        {
            if (!_collapsed)
            {
                var collapsedHeight = GetCollapsedHeight();

                if (resolvedStyle.height > collapsedHeight + 1f)
                    _expandedHeight = resolvedStyle.height;

                _collapsed = true;
                _body.style.display = DisplayStyle.None;
                _resizeHandle.style.display = DisplayStyle.None;
                style.height = GetCollapsedHeight();
            }
            else
            {
                _collapsed = false;
                _body.style.display = DisplayStyle.Flex;
                _resizeHandle.style.display = DisplayStyle.Flex;
                style.height = Mathf.Max(DefaultExpandedHeight, _expandedHeight);

                // Messages still enter OutputService while collapsed.
                // Rebuild the visual list so those buffered entries become visible now.
                Rebuild();
            }

            _pendingSeverity = null;
            RemoveFromClassList("output-flash-message");
            RemoveFromClassList("output-flash-warning");
            RemoveFromClassList("output-flash-error");
            RefreshLocale();
        }

        private void FlashCollapsed(OutputSeverity severity)
        {
            RemoveFromClassList("output-flash-message");
            RemoveFromClassList("output-flash-warning");
            RemoveFromClassList("output-flash-error");

            var className = severity switch
            {
                OutputSeverity.Error => "output-flash-error",
                OutputSeverity.Warning => "output-flash-warning",
                _ => "output-flash-message"
            };

            AddToClassList(className);

            schedule.Execute(() =>
            {
                RemoveFromClassList(className);

                if (_collapsed && _pendingSeverity.HasValue)
                {
                    schedule.Execute(() =>
                    {
                        if (_collapsed && _pendingSeverity.HasValue)
                            FlashCollapsed(_pendingSeverity.Value);
                    }).ExecuteLater(550);
                }
            }).ExecuteLater(250);
        }

        private void Rebuild()
        {
            _scroll.Clear();
            foreach (var entry in _output.Entries)
                AddEntryIfVisible(entry);
        }

        private void AddEntryIfVisible(OutputEntry entry)
        {
            if (!IsVisible(entry.Severity))
                return;

            var label = new Label(entry.Text);
            label.AddToClassList("output-entry");
            label.style.fontSize = _fontSize;

            label.AddToClassList(entry.Severity switch
            {
                OutputSeverity.Error => "output-error",
                OutputSeverity.Warning => "output-warning",
                _ => "output-message"
            });

            _scroll.Add(label);
        }

        private bool IsVisible(OutputSeverity severity)
        {
            return severity switch
            {
                OutputSeverity.Error => _errorsToggle.value,
                OutputSeverity.Warning => _warningsToggle.value,
                _ => _messagesToggle.value
            };
        }

        private void OnResizeDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;

            _resizing = true;
            _resizeStartY = evt.position.y;
            _resizeStartHeight = resolvedStyle.height;
            _resizeHandle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnResizeMove(PointerMoveEvent evt)
        {
            if (!_resizing || !_resizeHandle.HasPointerCapture(evt.pointerId))
                return;

            var delta = _resizeStartY - evt.position.y;
            style.height = UnityEngine.Mathf.Clamp(_resizeStartHeight + delta, 110f, 500f);
            evt.StopPropagation();
        }

        private void OnResizeUp(PointerUpEvent evt)
        {
            if (!_resizing)
                return;

            _resizing = false;
            _expandedHeight = resolvedStyle.height;

            if (_resizeHandle.HasPointerCapture(evt.pointerId))
                _resizeHandle.ReleasePointer(evt.pointerId);

            evt.StopPropagation();
        }
    }
}
