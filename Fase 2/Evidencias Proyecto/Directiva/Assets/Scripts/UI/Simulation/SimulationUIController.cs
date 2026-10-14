using System;
using Directiva.CodeUI;
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

    /// <summary>Optional inspector hooks for the future simulation controls. No simulation is run yet.</summary>
    [Serializable]
    public sealed class SimulationControlEvents
    {
        public UnityEvent play = new UnityEvent();
        public UnityEvent stop = new UnityEvent();
        public UnityEvent pause = new UnityEvent();
        public UnityEvent slowMotion = new UnityEvent();
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

        [Header("Visual-only simulation controls")]
        [SerializeField] private SimulationControlEvents controlEvents = new SimulationControlEvents();

        public EditorDisplayMode CurrentEditorMode { get; private set; }
        public DirectivaCodeUIController CodeEditor => codeEditor;

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
        private Button _activePlaybackButton;
        private Button _activeSpeedButton;
        private float _dockedWidth;
        private bool _initialWidthMeasured;
        private bool _editorMounted;
        private bool _pointerPressStartedOnUI;

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
            // The first slot of the HUD is now the editor toggle.
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

            var hud = new VisualElement();
            hud.AddToClassList("sim-hud");
            hud.pickingMode = PickingMode.Ignore;
            overlay.Add(hud);

            // The editor toggle belongs to the global HUD, not to the editor
            // pane. This keeps it reachable in every presentation mode.
            _openEditorTab = new Button(() =>
                SetEditorMode(CurrentEditorMode == EditorDisplayMode.Hidden
                    ? EditorDisplayMode.Docked : EditorDisplayMode.Hidden));
            _openEditorTab.AddToClassList("sim-editor-tab");
            UIIconView.SetButton(_openEditorTab, _theme?.icons?.openEditor, 22f);
            // SetButton clears Button.text because it is designed for icon-only controls.
            // This global toggle must remain identifiable even without a theme sprite.
            _openEditorTab.style.flexDirection = FlexDirection.Row;
            var editorTabLabel = new Label(string.IsNullOrWhiteSpace(editorButtonLabel)
                ? "Editor" : editorButtonLabel);
            editorTabLabel.pickingMode = PickingMode.Ignore;
            editorTabLabel.style.marginLeft = _theme?.icons?.openEditor != null ? 7f : 0f;
            _openEditorTab.Add(editorTabLabel);
            hud.Add(_openEditorTab);

            var controls = new VisualElement();
            controls.AddToClassList("sim-controls");
            hud.Add(controls);

            controls.Add(CreateControlButton(_theme?.icons?.play, "Play", () => SelectPlayback(0), controlEvents.play, true));
            controls.Add(CreateControlButton(_theme?.icons?.stop, "Stop", () => SelectPlayback(1), controlEvents.stop, true));
            controls.Add(CreateControlButton(_theme?.icons?.pause, "Pause", () => SelectPlayback(2), controlEvents.pause, true));

            var separator = new VisualElement();
            separator.AddToClassList("sim-control-separator");
            separator.pickingMode = PickingMode.Ignore;
            controls.Add(separator);

            controls.Add(CreateControlButton(_theme?.icons?.slowMotion, "Slowmo", () => SelectSpeed("0.25×"), controlEvents.slowMotion, false));
            controls.Add(CreateControlButton(_theme?.icons?.doubleSpeed, "Velocidad x2", () => SelectSpeed("2×"), controlEvents.doubleSpeed, false));
            controls.Add(CreateControlButton(_theme?.icons?.quintupleSpeed, "Velocidad x5", () => SelectSpeed("5×"), controlEvents.quintupleSpeed, false));

            _statusLabel = new Label("Detenida · 1×");
            _statusLabel.AddToClassList("sim-status");
            hud.Add(_statusLabel);

            _editorPane = new VisualElement();
            _editorPane.AddToClassList("sim-editor-pane");
            _editorPane.pickingMode = PickingMode.Position;
            overlay.Add(_editorPane);

            var editorHeader = new VisualElement();
            editorHeader.AddToClassList("sim-editor-header");
            _editorPane.Add(editorHeader);

            // The window header belongs to the host: only presentation controls
            // live here. The global menu button is outside the editor altogether.
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.pickingMode = PickingMode.Ignore;
            editorHeader.Add(spacer);

            _minimizeButton = new Button(() => SetEditorMode(EditorDisplayMode.Hidden));
            _minimizeButton.AddToClassList("sim-editor-header-button");
            _minimizeButton.tooltip = "Ocultar editor";
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

            // This button remains accessible above the fullscreen editor. It is
            // owned by the simulation UI, never by the reusable code editor.
            _mainMenuButton = new Button(ReturnToMainMenu) { text = "Menú principal" };
            _mainMenuButton.AddToClassList("sim-menu-button");
            overlay.Add(_mainMenuButton);

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
            _root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float available = _root.resolvedStyle.width;
                if (available <= 1f) return;
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

        private void Update()
        {
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
            UnityEvent callback, bool playback)
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
                // Only updates the mock UI; no Game/VM state is changed here.
                button.AddToClassList("sim-selected");
                if (playback) _activePlaybackButton = button;
                else _activeSpeedButton = button;
            }) { tooltip = tooltip };
            button.AddToClassList("sim-control-button");
            UIIconView.SetButton(button, sprite, 17f);
            return button;
        }

        private string _playback = "Detenida";
        private string _speed = "1×";
        private void SelectPlayback(int state)
        {
            _playback = state == 0 ? "Ejecutando" : state == 1 ? "Detenida" : "Pausada";
            _statusLabel.text = _playback + " · " + _speed;
        }
        private void SelectSpeed(string speed)
        {
            _speed = speed;
            _statusLabel.text = _playback + " · " + _speed;
        }

        /// <summary>Changes presentation only; editor contents, workspace and execution are retained.</summary>
        public void SetEditorMode(EditorDisplayMode mode)
        {
            CurrentEditorMode = mode;
            bool hidden = mode == EditorDisplayMode.Hidden;
            bool fullscreen = mode == EditorDisplayMode.Fullscreen;
            _editorPane.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            // Keep this button (and therefore the simulation controls beside it)
            // in the same position when the editor opens, hides or maximizes.
            _openEditorTab.EnableInClassList("sim-editor-tab-active", !hidden);
            _openEditorTab.tooltip = hidden ? "Abrir editor" : "Ocultar editor";
            _resizeHandle.style.display = mode == EditorDisplayMode.Docked ? DisplayStyle.Flex : DisplayStyle.None;
            UIIconView.SetButton(_fullscreenButton,
                fullscreen ? _theme?.icons?.restore : _theme?.icons?.maximize, 17f);
            _fullscreenButton.tooltip = fullscreen ? "Volver a ventana lateral" : "Expandir editor";
            _minimizeButton.tooltip = "Ocultar editor";
            // The global HUD is always visible: fullscreen editor begins below
            // the simulation controls and the host-owned menu button.
            if (fullscreen)
            {
                _editorPane.AddToClassList("sim-editor-fullscreen");
                _editorPane.style.top = 64;
                _editorPane.style.bottom = 0;
                _editorPane.style.left = 0;
                _editorPane.style.right = 0;
                _editorPane.style.width = StyleKeyword.Auto;
            }
            else
            {
                _editorPane.RemoveFromClassList("sim-editor-fullscreen");
                _editorPane.style.top = 70;
                _editorPane.style.bottom = 10;
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

        private void ReturnToMainMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
            {
                Debug.LogError($"[Directiva] Menu scene '{menuSceneName}' must be included in Build Profiles.", this);
                return;
            }
            onBeforeReturnToMenu?.Invoke();
            LevelLaunchContext.Clear();
            SceneManager.LoadScene(menuSceneName);
        }
    }
}
