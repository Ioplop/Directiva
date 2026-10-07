using System;
using UnityEngine;
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

        private readonly TextField _entryFunction;
        private readonly Toggle _debugDIL;

        private readonly Button _stop;
        private readonly Button _pause;
        private readonly Button _continue;
        private readonly Button _step;
        private readonly Button _stepIn;
        private readonly Button _stepOut;
        private readonly Label _executionMetrics;

        private readonly Button _back;
        private readonly Button _options;

        public event Action SaveClicked;
        public event Action RevertClicked;
        public event Action StopClicked;
        public event Action PauseClicked;
        public event Action ContinueClicked;
        public event Action StepClicked;
        public event Action BackClicked;
        public event Action OptionsClicked;

        public string EntryFunctionName => (_entryFunction.value ?? string.Empty).Trim();
        public bool DebugDILEnabled => _debugDIL.value;

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

            var entryLabel = new Label("fn:");
            entryLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            entryLabel.style.marginLeft = 4f;
            entryLabel.style.marginRight = 2f;

            _entryFunction = new TextField
            {
                value = "main"
            };
            _entryFunction.style.width = 150f;
            _entryFunction.style.minWidth = 90f;
            _entryFunction.style.maxWidth = 240f;
            _entryFunction.style.height = 24f;
            _entryFunction.tooltip = "Nombre de la función DIL a ejecutar dentro del archivo seleccionado.";
            StyleEntryFunctionField();
            _entryFunction.RegisterCallback<AttachToPanelEvent>(_ => StyleEntryFunctionField());

            _debugDIL = new Toggle("debug")
            {
                value = false
            };
            _debugDIL.style.height = 24f;
            _debugDIL.style.marginLeft = 6f;
            _debugDIL.style.marginRight = 4f;
            _debugDIL.style.marginTop = 0f;
            _debugDIL.style.marginBottom = 0f;
            _debugDIL.style.unityTextAlign = TextAnchor.MiddleLeft;
            _debugDIL.tooltip =
                "Inyecta metadata DEBUG_* al parsear DIL para mostrar la línea que ejecuta la VM. " +
                "No modifica el archivo.";

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
            execution.Add(_debugDIL);
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
            _executionMetrics.tooltip =
                "Advances ejecutados y tiempo real transcurrido. El tiempo incluye pausas; " +
                "DEBUG_* también cuenta como Advances en esta versión.";

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

        public void RegisterTooltips(HoverTooltipController tooltips)
        {
            if (tooltips == null)
                return;

            tooltips.Register(_save, () => _locale.Get("Tooltips", "save"));
            tooltips.Register(_revert, () => _locale.Get("Tooltips", "revert"));

            tooltips.Register(_debugDIL, () =>
                "Inyectar DEBUG_* temporalmente al parsear DIL y seguir la línea actual.");

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
            _executionMetrics.text = string.Empty;
            _executionMetrics.style.display = DisplayStyle.None;
        }

        public void SetExecutionMetrics(long advances, double elapsedMilliseconds)
        {
            _executionMetrics.text =
                $"{advances:N0} Advances · {elapsedMilliseconds:0.##} ms";
            _executionMetrics.style.display = DisplayStyle.Flex;
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
                    _entryFunction.SetEnabled(false);
                    _debugDIL.SetEnabled(false);
                    _stop.SetEnabled(true);
                    _pause.SetEnabled(true);
                    _continue.SetEnabled(false);
                    _step.SetEnabled(false);
                    break;

                case ScriptExecutionState.Paused:
                    _entryFunction.SetEnabled(false);
                    _debugDIL.SetEnabled(false);
                    _stop.SetEnabled(true);
                    _pause.SetEnabled(false);
                    _continue.SetEnabled(true);
                    _step.SetEnabled(true);
                    break;

                default:
                    _entryFunction.SetEnabled(_hasDocument);
                    _debugDIL.SetEnabled(_hasDocument);
                    _stop.SetEnabled(false);
                    _pause.SetEnabled(false);
                    _continue.SetEnabled(_hasDocument);
                    _step.SetEnabled(_hasDocument);
                    break;
            }

            // DIL is already instruction-level, so Step In / Step Out do not have a useful
            // distinction here. Keep them reserved for future DScript debugging.
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

            // DIL currently supports simple single-step; Step In / Step Out remain reserved for DScript.
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
