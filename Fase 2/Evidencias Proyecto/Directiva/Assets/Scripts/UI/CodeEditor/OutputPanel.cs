using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Directiva.UI;

namespace Directiva.CodeUI
{
    public sealed class OutputPanel : VisualElement
    {
        private readonly OutputService _output;
        private readonly LocalizationService _locale;
        private readonly DirectivaUIIcons _icons;

        private readonly Button _collapseButton;
        private readonly Button _title;
        private readonly Toggle _messagesToggle;
        private readonly Toggle _warningsToggle;
        private readonly Toggle _errorsToggle;
        private readonly Label _messagesLabel;
        private readonly Label _warningsLabel;
        private readonly Label _errorsLabel;
        private readonly Button _clearButton;
        private readonly VisualElement _header;
        private readonly ScrollView _scroll;
        private readonly ScrollView _debugScroll;
        private readonly VisualElement _consoleControls;
        private readonly VisualElement _body;
        private readonly VisualElement _resizeHandle;

        private const float CollapsedHeight = 32f;
        private const float DefaultExpandedHeight = 210f;
        private const float MinimumFontSize = 6f;
        private const float MaximumFontSize = 48f;
        private const float ZoomStep = 1f;

        private float _fontSize;

        private bool _collapsed;
        private bool _debuggerMode;
        private DebuggerVariableSnapshot _debugSnapshot;

        private const string DebuggerLocalsKey = "debug_locals";
        private const string DebuggerGlobalsKey = "debug_globals";
        private const string DebuggerSharedKey = "debug_shared";
        private const string DebuggerSelfKey = "debug_self";

        private readonly Dictionary<string, bool> _debuggerGroupExpanded =
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [DebuggerLocalsKey] = true,
                [DebuggerGlobalsKey] = true,
                [DebuggerSharedKey] = true,
                [DebuggerSelfKey] = true
            };

        public bool IsDebuggerMode => _debuggerMode;
        private float _expandedHeight = DefaultExpandedHeight;
        private OutputSeverity? _pendingSeverity;
        private int _flashGeneration;
        private bool _resizing;
        private float _resizeStartY;
        private float _resizeStartHeight;

        // Keep a slot for every buffered message, including filtered-out entries.
        // This lets us evict the correct visual row when OutputService drops the
        // oldest message, without rebuilding the entire panel for every print().
        private readonly Queue<Label> _renderedEntries = new Queue<Label>();
        private readonly List<OutputEntry> _pendingEntries = new List<OutputEntry>();
        private bool _refreshScheduled;
        private bool _fullRebuildRequired;
        private bool _scrollScheduled;

        public event Action<float> FontSizeChanged;
        public event Action<OutputSourceLocation> SourceRequested;

        public OutputPanel(
            OutputService output,
            LocalizationService locale,
            float fontSize,
            DirectivaUITheme theme = null)
        {
            _output = output;
            _locale = locale;
            _icons = theme?.icons;
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

            _title = new Button(() => SetDebuggerMode(!_debuggerMode));
            _title.AddToClassList("output-title");
            _title.AddToClassList("output-tab-button");

            _messagesToggle = new Toggle { value = true };
            _warningsToggle = new Toggle { value = true };
            _errorsToggle = new Toggle { value = true };
            UIIconView.SetToggle(_messagesToggle, _icons?.checkedBox, _icons?.uncheckedBox);
            UIIconView.SetToggle(_warningsToggle, _icons?.checkedBox, _icons?.uncheckedBox);
            UIIconView.SetToggle(_errorsToggle, _icons?.checkedBox, _icons?.uncheckedBox);

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
            _consoleControls = new VisualElement();
            _consoleControls.style.flexDirection = FlexDirection.Row;
            _consoleControls.style.alignItems = Align.Center;
            _consoleControls.style.flexGrow = 1;
            _consoleControls.style.minWidth = 0;
            _consoleControls.Add(messagesFilter);
            _consoleControls.Add(warningsFilter);
            _consoleControls.Add(errorsFilter);
            _consoleControls.Add(new VisualElement { style = { flexGrow = 1 } });
            _consoleControls.Add(_clearButton);
            _header.Add(_consoleControls);

            _body = new VisualElement();
            _body.AddToClassList("output-body");

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("output-scroll");
            _scroll.AddToClassList("directiva-scroll-view");
            _body.Add(_scroll);
            _debugScroll = new ScrollView(ScrollViewMode.Vertical);
            _debugScroll.AddToClassList("output-scroll");
            _debugScroll.AddToClassList("directiva-scroll-view");
            _debugScroll.AddToClassList("debugger-scroll");
            _debugScroll.style.display = DisplayStyle.None;
            _body.Add(_debugScroll);

            Add(_header);
            Add(_body);

            _output.EntryAdded += OnEntryAdded;
            _output.Cleared += OnCleared;
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
            tooltips.Register(
                _title,
                () => _locale.Get("Output", _debuggerMode
                    ? "switch_console" : "switch_debugger"));
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
            RenderDebugger();
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
                RenderDebugger();
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
            _title.text = _locale.Get("Output", _debuggerMode ? "debugger" : "title");
            _messagesLabel.text = _locale.Get("Output", "messages");
            _warningsLabel.text = _locale.Get("Output", "warnings");
            _errorsLabel.text = _locale.Get("Output", "errors");
            _clearButton.text = _locale.Get("Output", "clear");
            UIIconView.SetButton(_collapseButton,
                _collapsed ? _icons?.arrowUp : _icons?.arrowDown, 16f);
            RenderDebugger();
        }

        private void OnEntryAdded(OutputEntry entry)
        {
            if (_collapsed)
            {
                // One flashing sequence per severity increase, not one for every
                // print(). Otherwise a hot loop creates unbounded UI timers.
                if (!_pendingSeverity.HasValue || entry.Severity > _pendingSeverity.Value)
                {
                    _pendingSeverity = entry.Severity;
                    FlashCollapsed(_pendingSeverity.Value);
                }
                return;
            }
            // The console remains buffered while the debugger tab is visible.
            if (_debuggerMode) return;

            // Batch visual work: a single VM tick can produce many print() calls.
            // Keep only the most recent entries if the batch itself exceeds capacity.
            if (!_fullRebuildRequired)
            {
                if (_pendingEntries.Count >= _output.Capacity)
                {
                    _pendingEntries.Clear();
                    _fullRebuildRequired = true;
                }
                else
                    _pendingEntries.Add(entry);
            }

            if (_refreshScheduled) return;
            _refreshScheduled = true;
            schedule.Execute(FlushPendingEntries);
        }

        private void FlushPendingEntries()
        {
            _refreshScheduled = false;
            if (_collapsed || _debuggerMode)
            {
                // Expanding the console tab will rebuild from the bounded OutputService.
                _pendingEntries.Clear();
                _fullRebuildRequired = false;
                return;
            }

            bool changed = _fullRebuildRequired || _pendingEntries.Count > 0;
            if (!changed) return;

            if (_fullRebuildRequired)
                Rebuild();
            else
            {
                foreach (OutputEntry entry in _pendingEntries)
                {
                    // One logical slot per buffered message; a hidden severity
                    // uses a null label, so filters never break capacity tracking.
                    if (_renderedEntries.Count >= _output.Capacity)
                        _renderedEntries.Dequeue()?.RemoveFromHierarchy();
                    _renderedEntries.Enqueue(AddEntryIfVisible(entry));
                }
                _pendingEntries.Clear();
            }
            _fullRebuildRequired = false;

            if (_scrollScheduled) return;
            _scrollScheduled = true;
            schedule.Execute(() =>
            {
                _scrollScheduled = false;
                if (!_collapsed)
                    _scroll.scrollOffset = new Vector2(_scroll.scrollOffset.x,
                        _scroll.contentContainer.layout.height);
            }).ExecuteLater(1);
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

                // Messages remain in OutputService even when hidden.
                if (_debuggerMode) RenderDebugger();
                else Rebuild();
            }

            DismissUnreadNotification();
            RefreshLocale();
        }

        /// <summary>Switch views without changing the expanded/collapsed state.</summary>
        public void SetDebuggerMode(bool debugger)
        {
            if (_debuggerMode == debugger) return;
            _debuggerMode = debugger;
            _consoleControls.style.display = debugger ? DisplayStyle.None : DisplayStyle.Flex;
            _scroll.style.display = debugger ? DisplayStyle.None : DisplayStyle.Flex;
            _debugScroll.style.display = debugger ? DisplayStyle.Flex : DisplayStyle.None;
            if (debugger) RenderDebugger();
            else Rebuild(); // The console was still receiving buffered messages.
            RefreshLocale();
        }

        /// <summary>Update the selected VM's values only when their display actually changes.</summary>
        public void SetDebuggerSnapshot(DebuggerVariableSnapshot snapshot)
        {
            if (SnapshotsEqual(_debugSnapshot, snapshot)) return;
            _debugSnapshot = snapshot;
            if (_debuggerMode && !_collapsed) RenderDebugger();
        }

        private static bool SnapshotsEqual(DebuggerVariableSnapshot first,
            DebuggerVariableSnapshot second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;
            return EntriesEqual(first.Locals, second.Locals) &&
                EntriesEqual(first.Globals, second.Globals) &&
                EntriesEqual(first.Shared, second.Shared) &&
                EntriesEqual(first.Self, second.Self);
        }

        private static bool EntriesEqual(IReadOnlyList<DebuggerVariableEntry> first,
            IReadOnlyList<DebuggerVariableEntry> second)
        {
            if (first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
                if (first[i].Name != second[i].Name || first[i].Value != second[i].Value)
                    return false;
            return true;
        }

        private void RenderDebugger()
        {
            if (_debugScroll == null)
                return;

            _debugScroll.Clear();
            if (_debugSnapshot == null)
            {
                var noVm = new Label(_locale.Get("Output", "debug_no_vm"));
                noVm.AddToClassList("debugger-empty");
                noVm.style.fontSize = _fontSize;
                _debugScroll.Add(noVm);
                return;
            }

            AddDebuggerGroup(DebuggerLocalsKey, _debugSnapshot.Locals);
            AddDebuggerGroup(DebuggerGlobalsKey, _debugSnapshot.Globals);
            AddDebuggerGroup(DebuggerSharedKey, _debugSnapshot.Shared);
            AddDebuggerGroup(DebuggerSelfKey, _debugSnapshot.Self);
        }

        private void AddDebuggerGroup(string localizationKey,
            IReadOnlyList<DebuggerVariableEntry> entries)
        {
            var group = new VisualElement();
            group.AddToClassList("debugger-group");

            var title = new Button(() => ToggleDebuggerGroup(localizationKey))
            {
                text = GetDebuggerGroupTitle(localizationKey)
            };
            title.AddToClassList("debugger-group-title");
            title.AddToClassList("debugger-group-toggle");
            title.style.fontSize = _fontSize;
            title.style.minHeight = GetDebuggerGroupHeaderHeight();
            group.Add(title);

            var body = new VisualElement();
            body.AddToClassList("debugger-group-body");
            body.style.display = IsDebuggerGroupExpanded(localizationKey)
                ? DisplayStyle.Flex : DisplayStyle.None;
            group.Add(body);

            float rowHeight = GetDebuggerRowHeight();
            float nameWidth = GetDebuggerNameColumnWidth();

            foreach (DebuggerVariableEntry entry in entries)
            {
                var row = new VisualElement();
                row.AddToClassList("debugger-table-row");
                row.tooltip = entry.Name + " = " + entry.Value;
                row.style.minHeight = rowHeight;
                row.RegisterCallback<MouseEnterEvent>(_ =>
                    row.AddToClassList("debugger-table-row-hover"));
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                    row.RemoveFromClassList("debugger-table-row-hover"));

                var name = new Label(entry.Name);
                name.AddToClassList("debugger-variable-name");
                name.style.fontSize = _fontSize;
                name.style.width = nameWidth;
                name.style.minWidth = nameWidth;
                name.style.maxWidth = nameWidth;

                var value = new Label(entry.Value);
                value.AddToClassList("debugger-variable-value");
                value.style.fontSize = _fontSize;

                row.Add(name);
                row.Add(value);
                body.Add(row);
            }

            _debugScroll.Add(group);
        }

        private void ToggleDebuggerGroup(string localizationKey)
        {
            bool expanded = IsDebuggerGroupExpanded(localizationKey);
            _debuggerGroupExpanded[localizationKey] = !expanded;
            if (_debuggerMode && !_collapsed)
                RenderDebugger();
        }

        private bool IsDebuggerGroupExpanded(string localizationKey) =>
            !_debuggerGroupExpanded.TryGetValue(localizationKey, out bool expanded) || expanded;

        private string GetDebuggerGroupTitle(string localizationKey) =>
            (IsDebuggerGroupExpanded(localizationKey) ? "▼ " : "▶ ") +
            _locale.Get("Output", localizationKey);

        private float GetDebuggerNameColumnWidth() =>
            UnityEngine.Mathf.Clamp((_fontSize * 11f) + 24f, 140f, 320f);

        private float GetDebuggerRowHeight() =>
            UnityEngine.Mathf.Max(18f, _fontSize + 4f);

        private float GetDebuggerGroupHeaderHeight() =>
            UnityEngine.Mathf.Max(20f, _fontSize + 6f);

        private void OnCleared()
        {
            // Clearing the buffered messages also acknowledges notifications
            // received while the panel was collapsed.
            DismissUnreadNotification();
            Rebuild();
        }

        private void DismissUnreadNotification()
        {
            _pendingSeverity = null;
            _flashGeneration++; // Invalidate any scheduled pulse from before Clear/Expand.
            RemoveFromClassList("output-flash-message");
            RemoveFromClassList("output-flash-warning");
            RemoveFromClassList("output-flash-error");
        }

        private void FlashCollapsed(OutputSeverity severity)
        {
            int generation = ++_flashGeneration;
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
                // A Clear, Expand or newer severity must win over an older pulse.
                if (generation != _flashGeneration)
                    return;

                RemoveFromClassList(className);

                if (_collapsed && _pendingSeverity.HasValue)
                {
                    schedule.Execute(() =>
                    {
                        if (generation == _flashGeneration && _collapsed && _pendingSeverity.HasValue)
                            FlashCollapsed(_pendingSeverity.Value);
                    }).ExecuteLater(550);
                }
            }).ExecuteLater(250);
        }

        private void Rebuild()
        {
            _pendingEntries.Clear();
            _fullRebuildRequired = false;
            _renderedEntries.Clear();
            _scroll.Clear();
            foreach (var entry in _output.Entries)
                _renderedEntries.Enqueue(AddEntryIfVisible(entry));
        }

        private Label AddEntryIfVisible(OutputEntry entry)
        {
            if (!IsVisible(entry.Severity))
                return null;

            var label = new Label(entry.Text);
            label.AddToClassList("output-entry");
            label.style.fontSize = _fontSize;
            label.focusable = true;

            label.AddToClassList(entry.Severity switch
            {
                OutputSeverity.Error => "output-error",
                OutputSeverity.Warning => "output-warning",
                _ => "output-message"
            });

            if (entry.Source.HasValue)
            {
                OutputSourceLocation source = entry.Source.Value;
                label.AddToClassList("output-entry-link");
                label.tooltip = source.FilePath + ":" + source.Line + ":" + source.Column;
            }

            // Copy every message, warning and error, including messages without
            // a source location. Preserve source navigation on the same click.
            label.RegisterCallback<ClickEvent>(evt =>
            {
                CopyMessageToClipboard(entry.Text);
                if (entry.Source.HasValue)
                    SourceRequested?.Invoke(entry.Source.Value);
                evt.StopPropagation();
            });
            label.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter &&
                    evt.keyCode != KeyCode.Space)
                    return;
                CopyMessageToClipboard(entry.Text);
                if (entry.Source.HasValue)
                    SourceRequested?.Invoke(entry.Source.Value);
                evt.StopPropagation();
            });

            _scroll.Add(label);
            return label;
        }

        /// <summary>Copies the exact visible message, preserving its original text.</summary>
        public static void CopyMessageToClipboard(string text) =>
            GUIUtility.systemCopyBuffer = text ?? string.Empty;

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
