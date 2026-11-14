using System;
using Directiva.CodeUI;
using Directiva.Localization;
using Directiva.MainMenu;
using Directiva.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Directiva.SimulationUI
{
    public enum EditorDisplayMode
    {
        Hidden,
        Docked,
        Fullscreen
    }

    /// <summary>Input events raised by the simulation HUD; hosts subscribe without replacing the UI.</summary>
    [Serializable]
    public sealed class SimulationControlEvents
    {
        public UnityEvent play = new UnityEvent();
        public UnityEvent stop = new UnityEvent();
        public UnityEvent pause = new UnityEvent();
        public UnityEvent slowMotion = new UnityEvent();
        public UnityEvent normalSpeed = new UnityEvent();
        public UnityEvent doubleSpeed = new UnityEvent();
        public UnityEvent quintupleSpeed = new UnityEvent();
    }

    /// <summary>
    /// Composes a simulation HUD and exactly one reusable code editor inside a single UIDocument.
    /// Exposes input exclusion checks for future 3D selection/raycast controllers.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SimulationUIController : MonoBehaviour
    {
        [Header("Shared UI Theme")]
        [SerializeField] private DirectivaUITheme uiTheme;

        [Header("Editor")]
        [SerializeField] private DirectivaCodeUIController codeEditor;
        [SerializeField] private EditorDisplayMode initialEditorMode = EditorDisplayMode.Hidden;
        [SerializeField] private string editorButtonLabel = "Editor";
        [SerializeField, Range(0.3f, 0.7f)] private float dockedWidthFraction = 0.42f;
        [SerializeField, Min(280f)] private float minDockedWidth = 460f;
        [SerializeField, Min(120f)] private float minVisibleSimulationWidth = 300f;

        [Header("Navigation")]
        [SerializeField] private string menuSceneName = "MainMenu";
        [SerializeField] private string defaultLevelTitle = "Simulación 1";
        [SerializeField] private UnityEvent onBeforeReturnToMenu = new UnityEvent();

        [Header("Simulation control events")]
        [SerializeField] private SimulationControlEvents controlEvents = new SimulationControlEvents();

        public EditorDisplayMode CurrentEditorMode { get; private set; }
        public DirectivaCodeUIController CodeEditor => codeEditor;
        public SimulationControlEvents Controls => controlEvents;
        public SimulationNexusProgramPicker NexusPicker => _nexusPicker;
        public SimulationMatchStatsBar MatchStats => _matchStats;

        private DirectivaUITheme _theme;
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _editorPane;
        private VisualElement _editorMount;
        private Button _openEditorTab;
        private Button _minimizeButton;
        private Button _fullscreenButton;
        private Button _mainMenuButton;
        private SimulationEditorResizeHandle _resizeHandle;
        private Label _statusLabel;
        private Label _editorTabLabel;
        private readonly System.Collections.Generic.Dictionary<string, Button> _controlButtons =
            new System.Collections.Generic.Dictionary<string, Button>();
        private int _playbackState = 1;
        private Button _activePlaybackButton;
        private Button _activeSpeedButton;
        private Button _playButton;
        // Disabled UI Toolkit buttons do not reliably receive hover events.
        // The enabled transparent sibling keeps the explanatory tooltip usable.
        private VisualElement _playDisabledHover;
        private HoverTooltipController _hudTooltips;
        private string _playBlockReasonKey = "nexus_select_before_play";
        private float _dockedWidth;
        private bool _initialWidthMeasured;
        private bool _editorMounted;
        private bool _pointerPressStartedOnUI;
        private SimulationNexusProgramPicker _nexusPicker;
        private SimulationMatchStatsBar _matchStats;
        private float _nextNexusWarningCheckAt;
        private SimulationHudLayout _hudLayout = SimulationHudLayout.Standard;

        private static string T(string key, params object[] arguments) =>
            DirectivaLocalization.Get("Simulation", key, arguments);

        private void OnEnable()
        {
            DirectivaLocalization.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            DirectivaLocalization.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(string _)
        {
            if (_editorTabLabel != null)
                _editorTabLabel.text = string.IsNullOrWhiteSpace(editorButtonLabel) || editorButtonLabel == "Editor"
                    ? T("editor") : editorButtonLabel;
            if (_mainMenuButton != null) _mainMenuButton.text = T("main_menu");
            foreach (var entry in _controlButtons)
                entry.Value.tooltip = T(entry.Key);
            RefreshPlayAvailability();
            _hudTooltips?.Hide();
            SelectPlayback(_playbackState);
            _nexusPicker?.Relocalize();
            _matchStats?.Relocalize();
            if (_editorPane != null) SetEditorMode(CurrentEditorMode);
        }

        private void Awake()
        {
            _theme = DirectivaUITheme.Resolve(uiTheme);
            _document = GetComponent<UIDocument>();
            _root = _document.rootVisualElement;
            BuildUI();
        }

        private void Start()
        {
            if (codeEditor == null)
                codeEditor = GetComponentInChildren<DirectivaCodeUIController>(true);

            if (codeEditor != null)
            {
                // The code controller has no UIDocument here. Its single instance is
                // mounted in our child VisualElement, never cloned or recreated on mode changes.
                if (uiTheme != null)
                    codeEditor.SetTheme(_theme);
                codeEditor.Mount(_editorMount);
                codeEditor.SetHosted(true);
                codeEditor.SetExplorerCollapsed(true);
                _editorMounted = true;
            }
            else
            {
                Debug.LogError("[Directiva] Simulation UI requires a DirectivaCodeUIController child.", this);
            }

            // Keep the level name available without reserving permanent HUD space.
            // The status readout can expose it as hover text.
            _statusLabel.tooltip = string.IsNullOrWhiteSpace(LevelLaunchContext.CurrentLevelName)
                ? defaultLevelTitle
                : LevelLaunchContext.CurrentLevelName;

            float rootWidth = _root.resolvedStyle.width;
            if (rootWidth > 1f)
            {
                _initialWidthMeasured = true;
                _dockedWidth = rootWidth * dockedWidthFraction;
            }
            else
            {
                _dockedWidth = minDockedWidth;
            }
            if (rootWidth > 1f)
                ApplyHudLayout(rootWidth, _root.resolvedStyle.height);
            SetEditorMode(initialEditorMode);
        }

        private void BuildUI()
        {
            _root.Clear();
            _root.AddToClassList("sim-ui-root");
            _root.pickingMode = PickingMode.Ignore;
            _theme?.ApplyInterfaceFont(_root);

            var stylesheet = Resources.Load<StyleSheet>("Styles/Simulation/SimulationUI");
            if (stylesheet != null)
                _root.styleSheets.Add(stylesheet);

            var overlay = new VisualElement();
            overlay.AddToClassList("sim-overlay");
            overlay.pickingMode = PickingMode.Ignore;
            _root.Add(overlay);

            // The window header belongs to the host: only presentation controls
            // live there. The main menu button remains separate so it can later
            // evolve independently from the match HUD layout.
            _editorPane = new VisualElement();
            _editorPane.AddToClassList("sim-editor-pane");
            _editorPane.pickingMode = PickingMode.Position;
            overlay.Add(_editorPane);

            var editorHeader = new VisualElement();
            editorHeader.AddToClassList("sim-editor-header");
            _editorPane.Add(editorHeader);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.pickingMode = PickingMode.Ignore;
            editorHeader.Add(spacer);

            _minimizeButton = new Button(() => SetEditorMode(EditorDisplayMode.Hidden));
            _minimizeButton.AddToClassList("sim-editor-header-button");
            _minimizeButton.tooltip = T("hide_editor");
            UIIconView.SetButton(_minimizeButton, _theme?.icons?.minimize, 16f);
            editorHeader.Add(_minimizeButton);

            _fullscreenButton = new Button(() =>
                SetEditorMode(CurrentEditorMode == EditorDisplayMode.Fullscreen
                    ? EditorDisplayMode.Docked : EditorDisplayMode.Fullscreen));
            _fullscreenButton.AddToClassList("sim-editor-header-button");
            editorHeader.Add(_fullscreenButton);

            _editorMount = new VisualElement();
            _editorMount.AddToClassList("sim-editor-mount");
            _editorPane.Add(_editorMount);

            _resizeHandle = new SimulationEditorResizeHandle(ResizeDockedEditor);
            _editorPane.Add(_resizeHandle);

            // Match information sits at the top, leaving the bottom edge for
            // simulation controls. Its editor button shares exactly the same row.
            var topInfoRow = new VisualElement { name = "simulation-top-info-row" };
            topInfoRow.AddToClassList("sim-top-info-row");
            topInfoRow.pickingMode = PickingMode.Ignore;
            overlay.Add(topInfoRow);

            // The editor toggle now lives beside the match data panel so the
            // lower HUD resembles the compact control strips common in RTS games.
            _openEditorTab = new Button(() =>
                SetEditorMode(CurrentEditorMode == EditorDisplayMode.Hidden
                    ? EditorDisplayMode.Docked : EditorDisplayMode.Hidden));
            _openEditorTab.AddToClassList("sim-editor-tab");
            UIIconView.SetButton(_openEditorTab, _theme?.icons?.openEditor, 22f);
            _openEditorTab.style.flexDirection = FlexDirection.Row;
            var editorTabLabel = new Label(string.IsNullOrWhiteSpace(editorButtonLabel) || editorButtonLabel == "Editor"
                ? T("editor") : editorButtonLabel);
            _editorTabLabel = editorTabLabel;
            editorTabLabel.pickingMode = PickingMode.Ignore;
            editorTabLabel.style.marginLeft = _theme?.icons?.openEditor != null ? 8f : 0f;
            _openEditorTab.Add(editorTabLabel);
            topInfoRow.Add(_openEditorTab);

            _matchStats = new SimulationMatchStatsBar(topInfoRow);

            var controlsRow = new VisualElement { name = "simulation-controls-row" };
            controlsRow.AddToClassList("sim-controls-row");
            controlsRow.pickingMode = PickingMode.Ignore;
            overlay.Add(controlsRow);

            var controlsCluster = new VisualElement { name = "simulation-controls-cluster" };
            controlsCluster.AddToClassList("sim-controls-cluster");
            controlsCluster.pickingMode = PickingMode.Ignore;
            controlsRow.Add(controlsCluster);

            var controls = new VisualElement();
            controls.AddToClassList("sim-controls");
            controls.pickingMode = PickingMode.Ignore;
            controlsCluster.Add(controls);

            // A hoverable slot is needed when Play is disabled: the disabled
            // Button itself cannot be the reliable tooltip target.
            var playSlot = new VisualElement { name = "simulation-play-slot" };
            playSlot.AddToClassList("sim-play-slot");
            _playButton = CreateControlButton(_theme?.icons?.play, "play",
                () => SelectPlayback(0), controlEvents.play, true);
            _playButton.name = "simulation-play-button";
            playSlot.Add(_playButton);
            _playDisabledHover = new VisualElement
            {
                name = "simulation-play-disabled-hover",
                pickingMode = PickingMode.Position
            };
            _playDisabledHover.AddToClassList("sim-play-disabled-hover");
            playSlot.Add(_playDisabledHover);
            controls.Add(playSlot);
            RefreshPlayAvailability();
            controls.Add(CreateControlButton(_theme?.icons?.stop, "stop", () => SelectPlayback(1), controlEvents.stop, true));
            controls.Add(CreateControlButton(_theme?.icons?.pause, "pause", () => SelectPlayback(2), controlEvents.pause, true));

            var separator = new VisualElement();
            separator.AddToClassList("sim-control-separator");
            separator.pickingMode = PickingMode.Ignore;
            controls.Add(separator);

            controls.Add(CreateControlButton(_theme?.icons?.slowMotion, "slow_motion", () => SelectSpeed("0.25×"), controlEvents.slowMotion, false));
            controls.Add(CreateControlButton(null, "normal_speed", () => SelectSpeed("1×"), controlEvents.normalSpeed, false, "1×"));
            controls.Add(CreateControlButton(_theme?.icons?.doubleSpeed, "double_speed", () => SelectSpeed("2×"), controlEvents.doubleSpeed, false));
            controls.Add(CreateControlButton(_theme?.icons?.quintupleSpeed, "quintuple_speed", () => SelectSpeed("5×"), controlEvents.quintupleSpeed, false));

            _statusLabel = new Label(T("status_stopped") + " · 1×");
            _statusLabel.AddToClassList("sim-status");
            _statusLabel.pickingMode = PickingMode.Ignore;
            controlsCluster.Add(_statusLabel);

            // This button remains accessible above the fullscreen editor. It is
            // owned by the simulation UI, never by the reusable code editor.
            _mainMenuButton = new Button(ReturnToMainMenu) { text = T("main_menu") };
            _mainMenuButton.AddToClassList("sim-menu-button");
            overlay.Add(_mainMenuButton);
            // Inside this UIDocument, the picker inserts itself behind all other
            // panels, so an overlapping code editor always receives the click.
            _nexusPicker = new SimulationNexusProgramPicker(overlay);

            // VisualElement.tooltip is Editor-only. Reuse the editor's runtime
            // tooltip implementation for Play, including when its Button is disabled.
            _hudTooltips = new HoverTooltipController(_root, overlay, 0.65f);
            _hudTooltips.Register(playSlot, () => T(string.IsNullOrEmpty(_playBlockReasonKey)
                ? "play" : _playBlockReasonKey));

            // Clicking anywhere in the editor gives it priority over keyboard
            // focus previously held by a dropdown in the Nexus selector.
            _editorPane.RegisterCallback<PointerDownEvent>(
                _ => _nexusPicker?.ReleaseKeyboardFocus(), TrickleDown.TrickleDown);

            // Observe UI-initiated drags so future 3D code can ignore their continuation.
            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.target is VisualElement target &&
                    target != _root && target != overlay)
                    _pointerPressStartedOnUI = true;
            }, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 0) _pointerPressStartedOnUI = false;
            }, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerCancelEvent>(_ => _pointerPressStartedOnUI = false);
            _root.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float available = evt.newRect.width;
                if (available <= 1f) return;
                ApplyHudLayout(available, evt.newRect.height);
                if (!_initialWidthMeasured)
                {
                    _initialWidthMeasured = true;
                    _dockedWidth = available * dockedWidthFraction;
                }
                if (CurrentEditorMode == EditorDisplayMode.Docked &&
                    _resizeHandle != null && !_resizeHandle.IsDragging)
                    ResizeDockedEditor(_dockedWidth);
            });
        }

        private void ApplyHudLayout(float width, float height)
        {
            SimulationHudLayout next = ResponsiveUILayout.ForSimulation(width, height);
            if (next == _hudLayout && _root.ClassListContains("sim-hud-responsive-ready"))
                return;
            _hudLayout = next;
            _root.AddToClassList("sim-hud-responsive-ready");
            bool stacked = next == SimulationHudLayout.Stacked || next == SimulationHudLayout.Narrow;
            _root.EnableInClassList("sim-hud-stacked", stacked);
            _root.EnableInClassList("sim-hud-narrow", next == SimulationHudLayout.Narrow);
            _root.EnableInClassList("sim-hud-large", next == SimulationHudLayout.Large);
            if (_editorPane != null && _openEditorTab != null)
                SetEditorMode(CurrentEditorMode);
        }

        private void Update()
        {
            // Checking closed .dscript.back files involves filesystem access;
            // refresh this lightweight warning a few times per second, not per frame.
            if (_nexusPicker != null && _nexusPicker.Visible &&
                Time.unscaledTime >= _nextNexusWarningCheckAt)
            {
                RefreshNexusPickerUnsavedState();
                _nextNexusWarningCheckAt = Time.unscaledTime + 0.2f;
            }

            // A pointer released outside the UI may not deliver PointerUp to our panel.
            // Avoid leaving the world-input guard latched after such a release.
#if ENABLE_INPUT_SYSTEM
            if (_pointerPressStartedOnUI && (Mouse.current == null || !Mouse.current.leftButton.isPressed))
                _pointerPressStartedOnUI = false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (_pointerPressStartedOnUI && !Input.GetMouseButton(0))
                _pointerPressStartedOnUI = false;
#endif
        }

        private Button CreateControlButton(Sprite sprite, string tooltip, Action setVisualState,
            UnityEvent callback, bool playback, string fallbackLabel = null)
        {
            Button button = null;
            button = new Button(() =>
            {
                callback?.Invoke();
                if (playback)
                {
                    if (_activePlaybackButton != null)
                        _activePlaybackButton.RemoveFromClassList("sim-selected");
                    _activePlaybackButton = null;
                }
                else
                {
                    if (_activeSpeedButton != null)
                        _activeSpeedButton.RemoveFromClassList("sim-selected");
                    _activeSpeedButton = null;
                }
                setVisualState?.Invoke();
                // Control events are consumed by the scene synchronizer.
                button.AddToClassList("sim-selected");
                if (playback) _activePlaybackButton = button;
                else _activeSpeedButton = button;
            }) { tooltip = T(tooltip) };
            _controlButtons[tooltip] = button;
            button.AddToClassList("sim-control-button");
            UIIconView.SetButton(button, sprite, 17f);
            if (sprite == null && !string.IsNullOrEmpty(fallbackLabel)) button.text = fallbackLabel;
            return button;
        }

        /// <summary>
        /// Null means runnable; otherwise this is a Simulation localization key.
        /// The disabled button cannot be clicked, while its enabled overlay can
        /// still be hovered to display the reason.
        /// </summary>
        public void SetPlayAvailability(string disabledReasonKey)
        {
            if (!string.Equals(_playBlockReasonKey, disabledReasonKey, StringComparison.Ordinal))
                _hudTooltips?.Hide();
            _playBlockReasonKey = disabledReasonKey;
            RefreshPlayAvailability();
        }

        private void RefreshPlayAvailability()
        {
            if (_playButton == null || _playDisabledHover == null) return;
            bool canPlay = string.IsNullOrEmpty(_playBlockReasonKey);
            _playButton.SetEnabled(canPlay);
            string tooltip = T(canPlay ? "play" : _playBlockReasonKey);
            // Keep the text on the Button for Editor inspection. Runtime tooltips
            // are supplied by _hudTooltips, which tracks the enabled play slot.
            _playButton.tooltip = tooltip;
            _playDisabledHover.style.display = canPlay ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private string _speed = "1×";
        private void SelectPlayback(int state)
        {
            _playbackState = state;
            string key = state == 0 ? "status_playing" : state == 1 ? "status_stopped" : "status_paused";
            _statusLabel.text = T(key) + " · " + _speed;
        }
        /// <summary>
        /// Keep HUD playback controls in sync when the debugger pauses the game
        /// without a click on the HUD's own Pause button.
        /// </summary>
        public void SetPlaybackFromRuntime(ScriptExecutionState state)
        {
            // Close only after the runtime confirms Play actually started.
            // A rejected Play must leave the selector open for corrections.
            if (state == ScriptExecutionState.Running)
                _nexusPicker?.Hide();
            int index = state == ScriptExecutionState.Running ? 0 :
                state == ScriptExecutionState.Paused ? 2 : 1;
            SelectPlayback(index);
            string key = index == 0 ? "play" : index == 2 ? "pause" : "stop";
            if (_activePlaybackButton != null)
                _activePlaybackButton.RemoveFromClassList("sim-selected");
            if (_controlButtons.TryGetValue(key, out Button button))
            {
                _activePlaybackButton = button;
                button.AddToClassList("sim-selected");
            }
        }

        private void SelectSpeed(string speed)
        {
            _speed = speed;
            SelectPlayback(_playbackState);
        }

        /// <summary>Changes presentation only; editor contents, workspace and execution are retained.</summary>
        public void SetEditorMode(EditorDisplayMode mode)
        {
            CurrentEditorMode = mode;
            bool hidden = mode == EditorDisplayMode.Hidden;
            bool fullscreen = mode == EditorDisplayMode.Fullscreen;
            if (fullscreen) _nexusPicker?.Hide();
            _editorPane.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            // The editor button stays in the top statistics row in every mode.
            _openEditorTab.EnableInClassList("sim-editor-tab-active", !hidden);
            _openEditorTab.tooltip = hidden ? T("show_editor") : T("hide_editor");
            _resizeHandle.style.display = mode == EditorDisplayMode.Docked ? DisplayStyle.Flex : DisplayStyle.None;
            UIIconView.SetButton(_fullscreenButton,
                fullscreen ? _theme?.icons?.restore : _theme?.icons?.maximize, 17f);
            _fullscreenButton.tooltip = fullscreen ? T("restore_editor") : T("expand_editor");
            _minimizeButton.tooltip = T("hide_editor");
            // Respect the top HUD's row count at the current resolution.
            // The code editor itself keeps all of its existing layout rules.
            float topInset = ResponsiveUILayout.EditorTopInset(_hudLayout);
            float bottomInset = ResponsiveUILayout.EditorBottomInset(_hudLayout);
            if (fullscreen)
            {
                _editorPane.AddToClassList("sim-editor-fullscreen");
                _editorPane.style.top = topInset;
                _editorPane.style.bottom = bottomInset;
                _editorPane.style.left = 0;
                _editorPane.style.right = 0;
                _editorPane.style.width = StyleKeyword.Auto;
            }
            else
            {
                _editorPane.RemoveFromClassList("sim-editor-fullscreen");
                _editorPane.style.top = topInset;
                _editorPane.style.bottom = bottomInset;
                _editorPane.style.left = 8;
                _editorPane.style.right = StyleKeyword.Auto;
                if (hidden)
                    _editorPane.style.width = _dockedWidth;
                else
                    ResizeDockedEditor(_dockedWidth > 0f ? _dockedWidth : minDockedWidth);
            }

            if (_editorMounted)
                codeEditor.SetCompactLayout(mode != EditorDisplayMode.Fullscreen);

            if (hidden)
            {
                // Keyboard focus must not remain in an invisible code text field.
                var focused = _root.panel?.focusController?.focusedElement as VisualElement;
                if (focused != null && _editorMount.Contains(focused))
                    focused.Blur();
            }
        }

        private void ResizeDockedEditor(float width)
        {
            float available = _root.resolvedStyle.width;
            float maxWidth = available > 1f
                ? Mathf.Max(250f, available - minVisibleSimulationWidth - 16f)
                : Mathf.Max(250f, width);
            float minWidth = Mathf.Min(minDockedWidth, maxWidth);
            _dockedWidth = Mathf.Clamp(width, minWidth, maxWidth);
            if (CurrentEditorMode != EditorDisplayMode.Fullscreen)
                _editorPane.style.width = _dockedWidth;
        }

        /// <summary>
        /// Screen-space coordinates use Unity's bottom-left origin (e.g. Input.mousePosition).
        /// Returns true for actual HUD/editor regions, not for the transparent root overlay.
        /// A UI-initiated drag keeps blocking 3D interaction until its release.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (_root?.panel == null)
                return false;
            if (_resizeHandle != null && _resizeHandle.IsDragging)
                return true;
            if (_pointerPressStartedOnUI)
                return true;
            Vector2 fromTopLeft = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_root.panel, fromTopLeft);
            var picked = _root.panel.Pick(panelPoint);
            return picked != null && picked != _root;
        }

        /// <summary>True while a text field has keyboard focus; used by future game input code.</summary>
        public bool IsKeyboardCaptured()
        {
            var focused = _root?.panel?.focusController?.focusedElement as VisualElement;
            return focused is TextField || focused?.GetFirstAncestorOfType<TextField>() != null;
        }

        /// <summary>Warn only when the embedded editor actually has unsaved DScript.</summary>
        public void RefreshNexusPickerUnsavedState() =>
            _nexusPicker?.SetUnsavedChanges(codeEditor != null && codeEditor.HasUnsavedScripts);

        /// <summary>Place the world-anchored Nexus program panel above its visual.</summary>
        public void PositionNexusPicker(Vector3 worldPosition, Camera worldCamera)
        {
            if (_nexusPicker == null || !_nexusPicker.Visible || worldCamera == null ||
                _root?.panel == null) return;
            Vector3 point = worldCamera.WorldToScreenPoint(worldPosition);
            if (point.z <= 0f)
            {
                _nexusPicker.Hide();
                return;
            }
            _nexusPicker.PositionAt(new Vector2(point.x, point.y), _root.panel);
        }

        private void ReturnToMainMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
            {
                Debug.LogError(T("menu_scene_unavailable", menuSceneName), this);
                return;
            }
            onBeforeReturnToMenu?.Invoke();
            LevelLaunchContext.Clear();
            SceneManager.LoadScene(menuSceneName);
        }
    }
}
