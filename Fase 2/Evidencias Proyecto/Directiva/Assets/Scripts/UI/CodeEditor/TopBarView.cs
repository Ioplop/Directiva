using System;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    public sealed class TopBarView : VisualElement
    {
        private readonly LocalizationService _locale;
        private readonly IScriptExecutionStateProvider _executionState;
        private bool _hasDocument;

        private readonly Label _title;
        private readonly Button _save;
        private readonly Button _revert;

        private readonly Button _stop;
        private readonly Button _pause;
        private readonly Button _continue;
        private readonly Button _step;
        private readonly Button _stepIn;
        private readonly Button _stepOut;

        private readonly Button _back;
        private readonly Button _options;

        public event Action SaveClicked;
        public event Action RevertClicked;
        public event Action StopClicked;
        public event Action PauseClicked;
        public event Action ContinueClicked;
        public event Action BackClicked;
        public event Action OptionsClicked;

        public TopBarView(
            LocalizationService locale,
            IScriptExecutionStateProvider executionState)
        {
            _locale = locale;
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

            _stop = new Button(() => StopClicked?.Invoke());
            _pause = new Button(() => PauseClicked?.Invoke());
            _continue = new Button(() => ContinueClicked?.Invoke());
            _step = new Button();
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

            _step.SetEnabled(false);
            _stepIn.SetEnabled(false);
            _stepOut.SetEnabled(false);

            execution.Add(_stop);
            execution.Add(_pause);
            execution.Add(_continue);
            execution.Add(_step);
            execution.Add(_stepIn);
            execution.Add(_stepOut);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;

            var navigation = new VisualElement();
            navigation.AddToClassList("toolbar-group");
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
            bars.Add(spacer);
            bars.Add(navigation);

            Add(bars);

            _locale.LocaleChanged += _ => RefreshLocale();

            if (_executionState != null)
                _executionState.StateChanged += _ => RefreshExecutionButtons();

            RefreshLocale();
            SetDocument(null, false);
        }

        public void RegisterTooltips(HoverTooltipController tooltips)
        {
            if (tooltips == null)
                return;

            tooltips.Register(_save, () => _locale.Get("Tooltips", "save"));
            tooltips.Register(_revert, () => _locale.Get("Tooltips", "revert"));

            tooltips.Register(_stop, () => _locale.Get("Tooltips", "stop"));
            tooltips.Register(_pause, () => _locale.Get("Tooltips", "pause"));
            tooltips.Register(_continue, () => _locale.Get("Tooltips", "continue"));
            tooltips.Register(_step, () => _locale.Get("Tooltips", "step"));
            tooltips.Register(_stepIn, () => _locale.Get("Tooltips", "step_in"));
            tooltips.Register(_stepOut, () => _locale.Get("Tooltips", "step_out"));

            tooltips.Register(_back, () => _locale.Get("Tooltips", "back"));
            tooltips.Register(_options, () => _locale.Get("Tooltips", "options"));
        }

        public void SetDocument(string path, bool isDirty)
        {
            _hasDocument = !string.IsNullOrWhiteSpace(path);
            _title.text = _hasDocument ? System.IO.Path.GetFileName(path) : "-";
            _save.SetEnabled(_hasDocument && isDirty);
            _revert.SetEnabled(_hasDocument && isDirty);
            RefreshExecutionButtons();
        }

        private void RefreshExecutionButtons()
        {
            var state = _executionState?.State ?? ScriptExecutionState.Stopped;

            switch (state)
            {
                case ScriptExecutionState.Running:
                    _stop.SetEnabled(true);
                    _pause.SetEnabled(true);
                    _continue.SetEnabled(false);
                    break;

                case ScriptExecutionState.Paused:
                    _stop.SetEnabled(true);
                    _pause.SetEnabled(false);
                    _continue.SetEnabled(_hasDocument);
                    break;

                default:
                    _stop.SetEnabled(false);
                    _pause.SetEnabled(false);
                    _continue.SetEnabled(_hasDocument);
                    break;
            }

            // Debug stepping remains deliberately unavailable in v1.
            _step.SetEnabled(false);
            _stepIn.SetEnabled(false);
            _stepOut.SetEnabled(false);
        }

        private void RefreshLocale()
        {
            _save.text = "✓";
            _revert.text = "↶";

            _save.tooltip = _locale.Get("Common", "save");
            _revert.tooltip = _locale.Get("Common", "revert");

            _stop.text = "■";
            _pause.text = "Ⅱ";
            _continue.text = "▶";

            // Debug controls are placeholders in v1, but already use their final visual language.
            _step.text = "↷";
            _stepIn.text = "↓";
            _stepOut.text = "↑";

            _stop.tooltip = _locale.Get("Common", "stop");
            _pause.tooltip = _locale.Get("Common", "pause");
            _continue.tooltip = _locale.Get("Common", "continue");
            _step.tooltip = _locale.Get("Common", "step");
            _stepIn.tooltip = _locale.Get("Common", "step_in");
            _stepOut.tooltip = _locale.Get("Common", "step_out");

            _back.text = "←";

            // U+FE0E asks for text presentation instead of a colored emoji glyph where supported.
            _options.text = "⚙︎";

            _back.tooltip = _locale.Get("Common", "back");
            _options.tooltip = _locale.Get("Common", "options");
        }
    }
}
