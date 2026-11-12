using System;
using UnityEngine;
using UnityEngine.UIElements;
using Directiva.UI;

namespace Directiva.CodeUI
{
    public sealed class TopBarView : VisualElement
    {
        private readonly LocalizationService _locale;
        private readonly DirectivaUIIcons _icons;
        private readonly IScriptExecutionStateProvider _executionState;
        private bool _hasDocument;
        private bool _documentLocked;

        private readonly Label _title;
        private readonly Button _save;
        private readonly Button _revert;

        private readonly TextField _entryFunction;
        private readonly Label _entryFunctionLabel;
        private ScriptExecutionState? _externalDebugState;
        private bool _externalDebugHasSelectedVM;
        private bool _compactLayout;

        private readonly Button _stop;
        private readonly Button _pause;
        private readonly Button _continue;
        private readonly Button _step;
        private readonly Button _stepIn;
        private readonly Button _stepOut;
        private readonly Label _executionMetrics;
        private long _lastLogicalSteps;
        private double _lastElapsedMilliseconds;
        private bool _hasExecutionMetrics;
        private bool _simulationExecutionMetrics;

        private readonly Button _back;
        private readonly Button _options;
        private readonly VisualElement _executionGroup;
        private readonly VisualElement _navigationGroup;

        public event Action SaveClicked;
        public event Action RevertClicked;
        public event Action StopClicked;
        public event Action PauseClicked;
        public event Action ContinueClicked;
        public event Action StepClicked;
        public event Action BackClicked;
        public event Action OptionsClicked;
        public event Action<string> EntryFunctionChanged;

        public string EntryFunctionName => (_entryFunction.value ?? string.Empty).Trim();

        public TopBarView(
            LocalizationService locale,
            IScriptExecutionStateProvider executionState,
            DirectivaUITheme theme = null)
        {
            _locale = locale;
            _icons = theme?.icons;
            _executionState = executionState;
            AddToClassList("top-area");

            _title = new Label("-");
            _title.AddToClassList("script-title");
            Add(_title);

            var bars = new VisualElement();
            bars.AddToClassList("toolbar-row");

            var fileControls = new VisualElement();
            fileControls.AddToClassList("toolbar-group");

            _save = new Button(() => SaveClicked?.Invoke());
            _revert = new Button(() => RevertClicked?.Invoke());

            _save.AddToClassList("file-symbol-button");
            _revert.AddToClassList("file-symbol-button");

            fileControls.Add(_save);
            fileControls.Add(_revert);

            var execution = new VisualElement();
            execution.AddToClassList("toolbar-group");
            _executionGroup = execution;

            var entryLabel = new Label("fn:");
            _entryFunctionLabel = entryLabel;
            entryLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            entryLabel.style.marginLeft = 4f;
            entryLabel.style.marginRight = 2f;

            _entryFunction = new TextField
            {
                value = string.Empty
            };
            _entryFunction.style.width = 150f;
            _entryFunction.style.minWidth = 90f;
            _entryFunction.style.maxWidth = 240f;
            _entryFunction.style.height = 24f;
            _entryFunction.tooltip = _locale.Get("Tooltips", "entry_function");
            StyleEntryFunctionField();
            _entryFunction.RegisterCallback<AttachToPanelEvent>(_ => StyleEntryFunctionField());
            _entryFunction.RegisterValueChangedCallback(evt =>
                EntryFunctionChanged?.Invoke((evt.newValue ?? string.Empty).Trim()));

            _stop = new Button(() => StopClicked?.Invoke());
            _pause = new Button(() => PauseClicked?.Invoke());
            _continue = new Button(() => ContinueClicked?.Invoke());
            _step = new Button(() => StepClicked?.Invoke());
            _stepIn = new Button();
            _stepOut = new Button();

            _stop.AddToClassList("execution-symbol-button");
            _stop.AddToClassList("stop-symbol-button");

            _pause.AddToClassList("execution-symbol-button");
            _pause.AddToClassList("pause-symbol-button");

            _continue.AddToClassList("execution-symbol-button");
            _continue.AddToClassList("continue-symbol-button");

            _step.AddToClassList("execution-symbol-button");
            _step.AddToClassList("step-symbol-button");

            _stepIn.AddToClassList("execution-symbol-button");
            _stepIn.AddToClassList("step-symbol-button");

            _stepOut.AddToClassList("execution-symbol-button");
            _stepOut.AddToClassList("step-symbol-button");

            _stepIn.SetEnabled(false);
            _stepOut.SetEnabled(false);

            execution.Add(entryLabel);
            execution.Add(_entryFunction);
            execution.Add(_stop);
            execution.Add(_pause);
            execution.Add(_continue);
            execution.Add(_step);
            execution.Add(_stepIn);
            execution.Add(_stepOut);

            _executionMetrics = new Label();
            _executionMetrics.style.height = 24f;
            _executionMetrics.style.marginLeft = 10f;
            _executionMetrics.style.marginRight = 8f;
            _executionMetrics.style.unityTextAlign = TextAnchor.MiddleLeft;
            _executionMetrics.style.color = new Color(0.64f, 0.68f, 0.75f, 1f);
            _executionMetrics.style.display = DisplayStyle.None;
            _executionMetrics.tooltip = _locale.Get("Tooltips", "execution_metrics");

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;

            var navigation = new VisualElement();
            navigation.AddToClassList("toolbar-group");
            _navigationGroup = navigation;
            _back = new Button(() => BackClicked?.Invoke());
            _options = new Button(() => OptionsClicked?.Invoke());

            _back.AddToClassList("navigation-icon-button");
            _back.AddToClassList("back-icon-button");

            _options.AddToClassList("navigation-icon-button");
            _options.AddToClassList("options-icon-button");
            navigation.Add(_back);
            navigation.Add(_options);

            bars.Add(fileControls);
            bars.Add(execution);
            bars.Add(_executionMetrics);
            bars.Add(spacer);
            bars.Add(navigation);

            Add(bars);

            _locale.LocaleChanged += _ => RefreshLocale();

            if (_executionState != null)
                _executionState.StateChanged += _ => RefreshExecutionButtons();

            RefreshLocale();
            SetDocument(null, false);
        }

        /// <summary>Only changes toolbar presentation; VM state remains untouched.</summary>
        public void SetCompactLayout(bool compact)
        {
            _compactLayout = compact;
            UpdateExecutionGroupVisibility();
        }

        /// <summary>
        /// Hosted editor toolbar controls a selected entity VM, not the isolated
        /// standalone runner. Hide the irrelevant function selector in this mode.
        /// </summary>
        public void SetSimulationDebugState(ScriptExecutionState? state, bool hasSelectedVM)
        {
            if (_externalDebugState == state && _externalDebugHasSelectedVM == hasSelectedVM)
                return;
            _externalDebugState = state;
            _externalDebugHasSelectedVM = hasSelectedVM;
            _entryFunction.style.display = state.HasValue ? DisplayStyle.None : DisplayStyle.Flex;
            _entryFunctionLabel.style.display = state.HasValue ? DisplayStyle.None : DisplayStyle.Flex;
            UpdateExecutionGroupVisibility();
            RefreshExecutionButtons();
        }

        private void UpdateExecutionGroupVisibility() =>
            _executionGroup.style.display = _compactLayout && !_externalDebugState.HasValue
                ? DisplayStyle.None : DisplayStyle.Flex;

        /// <summary>Hide duplicate navigation when the outer host owns scene navigation.</summary>
        public void SetHosted(bool hosted)
        {
            _navigationGroup.style.display = hosted ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void StyleEntryFunctionField()
        {
            // TextField has its own internal input element. Styling only the outer field leaves
            // Unity's default padding/background on the actual editable area, which can make
            // short toolbar fields almost unreadable.
            _entryFunction.style.marginLeft = 0f;
            _entryFunction.style.marginRight = 0f;
            _entryFunction.style.marginTop = 0f;
            _entryFunction.style.marginBottom = 0f;
            _entryFunction.style.paddingLeft = 0f;
            _entryFunction.style.paddingRight = 0f;
            _entryFunction.style.paddingTop = 0f;
            _entryFunction.style.paddingBottom = 0f;
            _entryFunction.style.color = new Color(0.88f, 0.90f, 0.94f, 1f);

            var input = _entryFunction.Q<VisualElement>(className: "unity-text-field__input")
                        ?? _entryFunction.Q<VisualElement>(className: "unity-base-text-field__input");

            if (input == null)
                return;

            input.style.marginLeft = 0f;
            input.style.marginRight = 0f;
            input.style.marginTop = 0f;
            input.style.marginBottom = 0f;
            input.style.paddingLeft = 6f;
            input.style.paddingRight = 6f;
            input.style.paddingTop = 0f;
            input.style.paddingBottom = 0f;
            input.style.minHeight = 22f;
            input.style.height = 24f;
            input.style.unityTextAlign = TextAnchor.MiddleLeft;
            input.style.color = new Color(0.88f, 0.90f, 0.94f, 1f);
            input.style.backgroundColor = new Color(0.075f, 0.086f, 0.105f, 1f);

            var border = new Color(0.25f, 0.28f, 0.33f, 1f);
            input.style.borderLeftColor = border;
            input.style.borderRightColor = border;
            input.style.borderTopColor = border;
            input.style.borderBottomColor = border;
            input.style.borderLeftWidth = 1f;
            input.style.borderRightWidth = 1f;
            input.style.borderTopWidth = 1f;
            input.style.borderBottomWidth = 1f;
            input.style.borderTopLeftRadius = 2f;
            input.style.borderTopRightRadius = 2f;
            input.style.borderBottomLeftRadius = 2f;
            input.style.borderBottomRightRadius = 2f;
        }

        public void SetEntryFunctionName(string functionName)
        {
            _entryFunction.SetValueWithoutNotify(functionName ?? string.Empty);
        }

        public void RegisterTooltips(HoverTooltipController tooltips)
        {
            if (tooltips == null)
                return;

            tooltips.Register(_save, () => _locale.Get("Tooltips", "save"));
            tooltips.Register(_revert, () => _documentLocked
                ? _locale.Get("ScriptExplorer", "locked_during_execution")
                : _locale.Get("Tooltips", "revert"));

            tooltips.Register(_stop, () => _locale.Get("Tooltips", "stop"));
            tooltips.Register(_pause, () => _locale.Get("Tooltips", "pause"));
            tooltips.Register(_continue, () => _locale.Get("Tooltips", "continue"));
            tooltips.Register(_step, () => _locale.Get("Tooltips", "step"));
            tooltips.Register(_stepIn, () => _locale.Get("Tooltips", "step_in"));
            tooltips.Register(_stepOut, () => _locale.Get("Tooltips", "step_out"));

            tooltips.Register(_back, () => _locale.Get("Tooltips", "back"));
            tooltips.Register(_options, () => _locale.Get("Tooltips", "options"));
        }

        public void ClearExecutionMetrics()
        {
            _hasExecutionMetrics = false;
            _simulationExecutionMetrics = false;
            _executionMetrics.text = string.Empty;
            _executionMetrics.style.display = DisplayStyle.None;
        }

        public void SetExecutionMetrics(long logicalSteps, double elapsedMilliseconds)
        {
            _simulationExecutionMetrics = false;
            _lastLogicalSteps = logicalSteps;
            _lastElapsedMilliseconds = elapsedMilliseconds;
            _hasExecutionMetrics = true;
            UpdateExecutionMetricsLabel();
            _executionMetrics.style.display = DisplayStyle.Flex;
        }

        public void SetSimulationExecutionMetrics(long logicalSteps)
        {
            _simulationExecutionMetrics = true;
            _lastLogicalSteps = logicalSteps;
            _hasExecutionMetrics = true;
            UpdateExecutionMetricsLabel();
            _executionMetrics.style.display = DisplayStyle.Flex;
        }

        private void UpdateExecutionMetricsLabel()
        {
            string steps = _locale.Get("Common", "execution_steps");
            _executionMetrics.text = _simulationExecutionMetrics
                ? $"{_lastLogicalSteps:N0} {steps}"
                : $"{_lastLogicalSteps:N0} {steps} · {_lastElapsedMilliseconds:0.##} ms";
        }

        public void SetDocument(string path, bool isDirty, bool isLocked = false)
        {
            _hasDocument = !string.IsNullOrWhiteSpace(path);
            _documentLocked = isLocked;
            _title.text = _hasDocument ? System.IO.Path.GetFileName(path) : "-";
            _save.SetEnabled(_hasDocument && isDirty);
            _revert.SetEnabled(_hasDocument && isDirty && !isLocked);
            _revert.tooltip = isLocked
                ? _locale.Get("ScriptExplorer", "locked_during_execution")
                : _locale.Get("Common", "revert");
            RefreshExecutionButtons();
        }

        private void RefreshExecutionButtons()
        {
            var state = _externalDebugState ?? _executionState?.State ?? ScriptExecutionState.Stopped;

            switch (state)
            {
                case ScriptExecutionState.Running:
                    _entryFunction.SetEnabled(false);
                    _stop.SetEnabled(true);
                    _pause.SetEnabled(true);
                    _continue.SetEnabled(false);
                    _step.SetEnabled(false);
                    break;

                case ScriptExecutionState.Paused:
                    _entryFunction.SetEnabled(false);
                    _stop.SetEnabled(true);
                    _pause.SetEnabled(false);
                    _continue.SetEnabled(true);
                    _step.SetEnabled(!_externalDebugState.HasValue || _externalDebugHasSelectedVM);
                    break;

                default:
                    _entryFunction.SetEnabled(_hasDocument && !_externalDebugState.HasValue);
                    _stop.SetEnabled(false);
                    _pause.SetEnabled(false);
                    _continue.SetEnabled(_hasDocument);
                    _step.SetEnabled(_hasDocument &&
                        (!_externalDebugState.HasValue || _externalDebugHasSelectedVM));
                    break;
            }

            // Step currently advances one VM instruction; source-level Step In/Out
            // require their own debugger and remain disabled for now.
            _stepIn.SetEnabled(false);
            _stepOut.SetEnabled(false);
        }

        private void RefreshLocale()
        {
            UIIconView.SetButton(_save, _icons?.save);
            UIIconView.SetButton(_revert, _icons?.revert);

            _entryFunction.tooltip = _locale.Get("Tooltips", "entry_function");
            _executionMetrics.tooltip = _locale.Get("Tooltips", "execution_metrics");
            if (_hasExecutionMetrics)
                UpdateExecutionMetricsLabel();
            _save.tooltip = _locale.Get("Common", "save");
            _revert.tooltip = _documentLocked
                ? _locale.Get("ScriptExplorer", "locked_during_execution")
                : _locale.Get("Common", "revert");

            UIIconView.SetButton(_stop, _icons?.stop);
            UIIconView.SetButton(_pause, _icons?.pause);
            UIIconView.SetButton(_continue, _icons?.play);

            // Step advances VM instructions; source-level stepping remains future work.
            UIIconView.SetButton(_step, _icons?.step);
            UIIconView.SetButton(_stepIn, _icons?.stepIn);
            UIIconView.SetButton(_stepOut, _icons?.stepOut);

            _stop.tooltip = _locale.Get("Common", "stop");
            _pause.tooltip = _locale.Get("Common", "pause");
            _continue.tooltip = _locale.Get("Common", "continue");
            _step.tooltip = _locale.Get("Common", "step");
            _stepIn.tooltip = _locale.Get("Common", "step_in");
            _stepOut.tooltip = _locale.Get("Common", "step_out");

            UIIconView.SetButton(_back, _icons?.back);
            UIIconView.SetButton(_options, _icons?.options);

            _back.tooltip = _locale.Get("Common", "back");
            _options.tooltip = _locale.Get("Common", "options");
        }
    }
}
