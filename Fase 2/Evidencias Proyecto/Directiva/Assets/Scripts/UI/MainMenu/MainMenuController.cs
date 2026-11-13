using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UIElements;
using Directiva.Settings;
using Directiva.Localization;
using Directiva.UI;
using Directiva.Levels;
using Directiva.Progress;

namespace Directiva.MainMenu
{
    /// <summary>Configurable UnityEvent and optional built-in action for a menu button.</summary>
    [Serializable]
    public sealed class MainMenuButtonAction
    {
        [Tooltip("Visible button text. Leave empty to use the original Spanish label.")]
        public string label;

        [Tooltip("Keep the button's normal behavior (navigate, change tab or quit) after the custom event.")]
        public bool executeDefaultAction = true;

        [Tooltip("Called before the default behavior. Can reference objects in this menu scene, including camera controllers.")]
        public UnityEvent onClick = new UnityEvent();

        public MainMenuButtonAction() { }

        public MainMenuButtonAction(string label)
        {
            this.label = label;
        }

        public void Invoke(Action defaultAction)
        {
            onClick?.Invoke();
            if (executeDefaultAction) defaultAction?.Invoke();
        }
    }

    /// <summary>Inspector events for all fixed navigation buttons; level entries have their own events.</summary>
    [Serializable]
    public sealed class MainMenuButtonActions
    {
        public MainMenuButtonAction play = new MainMenuButtonAction("Jugar");
        public MainMenuButtonAction options = new MainMenuButtonAction("Opciones");
        public MainMenuButtonAction exit = new MainMenuButtonAction("Salir");
        public MainMenuButtonAction backFromOptions = new MainMenuButtonAction("‹ Atrás");
        public MainMenuButtonAction backFromLevels = new MainMenuButtonAction("‹ Atrás");
        [HideInInspector] public MainMenuButtonAction generalTab = new MainMenuButtonAction("General");
        [HideInInspector] public MainMenuButtonAction controlsTab = new MainMenuButtonAction("Controles");
        [HideInInspector] public MainMenuButtonAction audioTab = new MainMenuButtonAction("Audio");
        [HideInInspector] public MainMenuButtonAction graphicsTab = new MainMenuButtonAction("Gráficos");
        public MainMenuButtonAction fullScreen = new MainMenuButtonAction("Pantalla completa");
        public MainMenuButtonAction simulateWhenInactive = new MainMenuButtonAction("Simular en ventana inactiva");
        public MainMenuButtonAction confirmResolution = new MainMenuButtonAction("Conservar resolución");
        public MainMenuButtonAction revertResolution = new MainMenuButtonAction("Revertir");
    }

    /// <summary>Inspector-controlled tints; the alpha channel determines how much 3D is visible behind the menu.</summary>
    [Serializable]
    public sealed class MainMenuAppearance
    {
        [Tooltip("Full-screen overlay. Set alpha to 0 for an unobstructed 3D background.")]
        [ColorUsage(true, false)] public Color screenOverlay = new Color32(0, 0, 0, 0);

        [Tooltip("Large frame covering most of the screen. Alpha 0 makes it entirely transparent.")]
        [ColorUsage(true, false)] public Color frameBackground = new Color32(27, 33, 45, 85);

        [ColorUsage(true, false)] public Color frameBorder = new Color32(67, 80, 106, 175);
        [ColorUsage(true, false)] public Color buttonBackground = new Color32(49, 61, 83, 215);
        [ColorUsage(true, false)] public Color buttonHover = new Color32(70, 91, 124, 235);
        [ColorUsage(true, false)] public Color buttonPressed = new Color32(43, 110, 171, 245);
        [ColorUsage(true, false)] public Color activeTabBackground = new Color32(42, 107, 169, 225);
        [ColorUsage(true, false)] public Color disabledButtonBackground = new Color32(37, 42, 52, 170);
    }

    /// <summary>Builds the main menu and connects settings UI to the persistent settings service.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Shared UI Theme")]
        [SerializeField] private DirectivaUITheme uiTheme;

        [Header("Presentation")]
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private string gameTitle = "DIRECTIVA";
        [SerializeField] private string levelsTitle = "Niveles";
        // Every level has exactly three objectives, independent of old maxStars scene data.

        // Legacy serialized values remain hidden for non-destructive migration.
        // Assign new icons in DirectivaUITheme instead. No character fallback is used.
        [HideInInspector, SerializeField] private Sprite earnedStarSprite;
        [HideInInspector, SerializeField] private Sprite unearnedStarSprite;

        // Keep the old reference serialized so a project update does not discard assigned assets.
        // The lock itself is no longer displayed; users can assign the new sprite independently.
        [HideInInspector, SerializeField] private Sprite lockedLevelSprite;

        [HideInInspector, SerializeField] private Sprite requiredStarsSprite;

        [Header("3D backdrop / tint colors (alpha supported)")]
        [SerializeField] private MainMenuAppearance appearance = new MainMenuAppearance();

        [Header("Per-button events and default actions")]
        [SerializeField] private MainMenuButtonActions buttons = new MainMenuButtonActions();

        [Header("Settings labels")]
        [SerializeField] private string languageSettingLabel = "Idioma";
        [SerializeField] private string masterVolumeSettingLabel = "Volumen general";
        [SerializeField] private string musicVolumeSettingLabel = "Volumen de la música";
        [SerializeField] private string uiVolumeSettingLabel = "Volumen de la interfaz";
        [SerializeField] private string simulationVolumeSettingLabel = "Volumen de la partida / simulación";
        [SerializeField] private string resolutionSettingLabel = "Resolución";
        [SerializeField] private string mouseCaptureSettingLabel = "Atrapar el mouse";

        [Header("Resolution confirmation")]
        [Tooltip("Time to confirm a temporary screen resolution before automatically restoring the saved one.")]
        [SerializeField, Range(5, 30)] private int resolutionConfirmSeconds = 15;

        // Kept only so existing Unity scene data is not discarded by this upgrade.
        // Actual choices now come exclusively from Screen.resolutions.
        [SerializeField, HideInInspector] private List<string> resolutionChoices = new List<string>();

        [Header("Option section titles (optional)")]
        [Tooltip("Leave empty to retain the old General tab label, including previous Inspector customizations.")]
        [SerializeField] private string generalSectionTitle;
        [SerializeField] private string controlsSectionTitle;
        [SerializeField] private string audioSectionTitle;
        [SerializeField] private string graphicsSectionTitle;

        [Header("Levels (ScriptableObjects)")]
        [SerializeField] private List<DirectivaLevel> levelDefinitions = new List<DirectivaLevel>();
        // Level unlocks are now based solely on earned progress in Save.bin.

        // Preserve old scene data for migration, but never use legacy entries as levels.
        [HideInInspector, SerializeField] private List<LevelMenuEntry> levels = new List<LevelMenuEntry>();

        private DirectivaUITheme _theme;
        private UIDocument document;
        private VisualElement root;
        private VisualElement home;
        private VisualElement options;
        private VisualElement levelSelection;
        private VisualElement levelGrid;
        private VisualElement optionsContent;
        private ScrollView optionsScroll;
        private Label starTotal;
        private Button fullScreenButton;
        private Button simulateInactiveButton;
        private DropdownField languageDropdown;
        private DropdownField mouseCaptureDropdown;
        private DropdownField resolutionDropdown;
        private readonly Dictionary<string, Slider> volumeSliders = new Dictionary<string, Slider>();
        private readonly Dictionary<string, Label> volumeLabels = new Dictionary<string, Label>();
        private readonly Dictionary<string, Vector2Int> resolutionByLabel = new Dictionary<string, Vector2Int>();

        private VisualElement resolutionConfirmation;
        private Label resolutionCountdownLabel;
        private IVisualElementScheduledItem resolutionTimer;
        private Vector2Int? pendingResolution;
        private string builtLocale;

        private static Label Label(string text, string className = null)
        {
            var label = new Label(text);
            if (className != null) label.AddToClassList(className);
            return label;
        }

        private static string ButtonText(MainMenuButtonAction action, string defaultText)
        {
            return string.IsNullOrWhiteSpace(action?.label) ? defaultText : action.label;
        }

        private static string TextOrDefault(string value, string defaultText)
        {
            return string.IsNullOrWhiteSpace(value) ? defaultText : value;
        }

        private static string T(string key, params object[] args) =>
            DirectivaLocalization.Get("MainMenu", key, args);

        // Inspector values can override a label; the legacy Spanish defaults are
        // treated as defaults, not as custom text that prevents changing language.
        private static string DefaultLabel(string value, string originalSpanish, string key)
        {
            return string.IsNullOrWhiteSpace(value) || value.Trim() == originalSpanish
                ? T(key) : value;
        }

        private static string MenuButtonText(MainMenuButtonAction action, string originalSpanish, string key)
            => DefaultLabel(action?.label, originalSpanish, key);

        private enum ButtonVisualState { Normal, Hover, Pressed }

        private Button CreateButton(string text, Action action, string className = null)
        {
            var button = new Button(action) { text = text };
            if (className != null) button.AddToClassList(className);

            // Inline colors make the button palette editable in the Inspector.
            // Hover/pressed are handled here too, because inline styles override USS pseudo-classes.
            SetButtonColor(button, ButtonVisualState.Normal);
            button.RegisterCallback<PointerEnterEvent>(_ => SetButtonColor(button, ButtonVisualState.Hover));
            button.RegisterCallback<PointerLeaveEvent>(_ => SetButtonColor(button, ButtonVisualState.Normal));
            button.RegisterCallback<PointerDownEvent>(_ => SetButtonColor(button, ButtonVisualState.Pressed));
            button.RegisterCallback<PointerUpEvent>(_ => SetButtonColor(button, ButtonVisualState.Hover));
            return button;
        }

        private void SetButtonColor(Button button, ButtonVisualState state)
        {
            if (appearance == null) return;
            Color color;
            if (!button.enabledSelf || button.ClassListContains("dm-locked"))
                color = appearance.disabledButtonBackground;
            else if (state == ButtonVisualState.Pressed)
                color = appearance.buttonPressed;
            else if (button.ClassListContains("dm-active"))
                color = appearance.activeTabBackground;
            else if (state == ButtonVisualState.Hover)
                color = appearance.buttonHover;
            else
                color = appearance.buttonBackground;

            button.style.backgroundColor = color;
        }

        private void TriggerButton(MainMenuButtonAction buttonAction, Action defaultAction)
        {
            if (buttonAction == null) defaultAction?.Invoke();
            else buttonAction.Invoke(defaultAction);
        }

        private static VisualElement Container(string className)
        {
            var element = new VisualElement();
            element.AddToClassList(className);
            return element;
        }

        /// <summary>Uses a UI Toolkit Image when a sprite is provided; otherwise keeps the original glyph.</summary>
        private Sprite GetIcon(Sprite fromTheme, Sprite previouslyAssigned)
        {
            // Preserve sprites already stored in users' existing scenes. With no
            // theme icon AND no previous sprite, the slot is deliberately blank.
            return fromTheme != null ? fromTheme : previouslyAssigned;
        }

        private Button CreateBackButton(MainMenuButtonAction action, Action onBack)
        {
            string caption = string.IsNullOrWhiteSpace(action?.label) ||
                action.label.Trim() == "‹ Atrás" || action.label.Trim() == "Atrás"
                ? T("back") : action.label.TrimStart('‹', ' ');
            var button = CreateButton(caption,
                () => TriggerButton(action, onBack), "dm-back-button");
            Sprite sprite = _theme?.icons?.back;
            if (sprite != null)
            {
                var icon = UIIconView.Create(sprite, "dm-back-icon");
                button.Insert(0, icon);
                button.style.flexDirection = FlexDirection.Row;
                button.style.alignItems = Align.Center;
                button.style.justifyContent = Justify.Center;
            }
            return button;
        }

        private void OnEnable()
        {
            _theme = DirectivaUITheme.Resolve(uiTheme);
            if (appearance == null) appearance = new MainMenuAppearance();
            if (buttons == null) buttons = new MainMenuButtonActions();
            if (buttons.fullScreen == null) buttons.fullScreen = new MainMenuButtonAction("Pantalla completa");
            if (buttons.simulateWhenInactive == null) buttons.simulateWhenInactive = new MainMenuButtonAction("Simular en ventana inactiva");
            if (buttons.confirmResolution == null) buttons.confirmResolution = new MainMenuButtonAction("Conservar resolución");
            if (buttons.revertResolution == null) buttons.revertResolution = new MainMenuButtonAction("Revertir");
            // A newly opened main menu must not inherit a previously played level.
            LevelLaunchContext.Clear();
            SettingsManager.Initialize();
            builtLocale = DirectivaLocalization.CurrentLocale;
            pendingResolution = null;
            resolutionTimer = null;
            volumeSliders.Clear();
            volumeLabels.Clear();
            resolutionByLabel.Clear();
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.Clear();
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet)) root.styleSheets.Add(styleSheet);
            root.AddToClassList("dm-root");
            _theme?.ApplyInterfaceFont(root);
            root.style.backgroundColor = appearance != null ? appearance.screenOverlay : Color.clear;
            BuildHome();
            BuildOptions();
            BuildLevels();
            Show(home);
            SettingsManager.SettingsChanged += OnSettingsChanged;
        }

        private void OnDisable()
        {
            SettingsManager.SettingsChanged -= OnSettingsChanged;
            resolutionTimer?.Pause();
            resolutionTimer = null;
            pendingResolution = null;
            SettingsRuntimeApplier.CancelResolutionPreview();
        }

        private void OnSettingsChanged(GameSettings _)
        {
            if (!string.Equals(builtLocale, DirectivaLocalization.CurrentLocale, StringComparison.OrdinalIgnoreCase))
            {
                // The language dropdown belongs to the page being replaced.
                // Preserve the selected page, not the old controls or their callbacks.
                bool showingOptions = options != null && options.parent != null &&
                    options.parent.style.display.value == DisplayStyle.Flex;
                bool showingLevels = levelSelection != null && levelSelection.parent != null &&
                    levelSelection.parent.style.display.value == DisplayStyle.Flex;
                if (pendingResolution.HasValue) CancelResolution();
                volumeSliders.Clear();
                volumeLabels.Clear();
                resolutionByLabel.Clear();
                root.Clear();
                builtLocale = DirectivaLocalization.CurrentLocale;
                BuildHome();
                BuildOptions();
                BuildLevels();
                Show(showingOptions ? options : showingLevels ? levelSelection : home);
                return;
            }
            if (pendingResolution.HasValue && !SettingsRuntimeApplier.IsResolutionPreviewActive)
                CloseResolutionConfirmation();
            RefreshOptionValues();
        }

        private VisualElement BuildPage()
        {
            // The labels above the frames were mockup annotations, not part of the actual menu.
            var page = Container("dm-page");
            var frame = Container("dm-frame");
            if (appearance != null)
            {
                frame.style.backgroundColor = appearance.frameBackground;
                frame.style.borderTopColor = appearance.frameBorder;
                frame.style.borderBottomColor = appearance.frameBorder;
                frame.style.borderLeftColor = appearance.frameBorder;
                frame.style.borderRightColor = appearance.frameBorder;
            }
            page.Add(frame);
            root.Add(page);
            return frame;
        }

        private void Show(VisualElement chosen)
        {
            home.parent.style.display = chosen == home ? DisplayStyle.Flex : DisplayStyle.None;
            options.parent.style.display = chosen == options ? DisplayStyle.Flex : DisplayStyle.None;
            levelSelection.parent.style.display = chosen == levelSelection ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildHome()
        {
            home = BuildPage();
            home.AddToClassList("dm-home");
            // Prefer the shared-theme logo, but keep the existing text if none is assigned.
            // ScaleToFit ensures that even very wide logos are never distorted.
            Sprite logoSprite = _theme != null ? _theme.mainMenuLogo : null;
            if (logoSprite != null)
            {
                var logo = UIIconView.Create(logoSprite, "dm-main-logo");
                if (_theme.mainMenuLogoHeight > 0f)
                    logo.style.height = _theme.mainMenuLogoHeight;
                if (_theme.mainMenuLogoMaxWidth > 0f)
                    logo.style.maxWidth = _theme.mainMenuLogoMaxWidth;
                home.Add(logo);
            }
            else
            {
                home.Add(Label(gameTitle, "dm-title"));
            }
            var actions = Container("dm-home-actions");
            actions.Add(CreateButton(MenuButtonText(buttons?.play, "Jugar", "play"), () => TriggerButton(buttons?.play, ShowLevelsScreen), "dm-big-button"));
            actions.Add(CreateButton(MenuButtonText(buttons?.options, "Opciones", "options"), () => TriggerButton(buttons?.options, ShowOptionsScreen), "dm-big-button"));
            actions.Add(CreateButton(MenuButtonText(buttons?.exit, "Salir", "exit"), () => TriggerButton(buttons?.exit, QuitApplication), "dm-big-button"));
            home.Add(actions);
        }

        /// <summary>Switches to the start screen. Can be called by UnityEvents and animation events.</summary>
        public void ShowHomeScreen() => ShowScreen(home);

        /// <summary>Shows the combined options page, reset to its first section.</summary>
        public void ShowOptionsScreen()
        {
            SettingsManager.RefreshDisplayModes();
            RefreshResolutionChoices();
            RefreshOptionValues();
            ShowScreen(options);
            if (optionsScroll != null) optionsScroll.scrollOffset = Vector2.zero;
        }

        /// <summary>Switches to level selection. Can be called after a camera transition.</summary>
        public void ShowLevelsScreen()
        {
            RefreshLevelButtons();
            UpdateStars();
            ShowScreen(levelSelection);
        }

        private void ShowScreen(VisualElement screen)
        {
            if (home == null || options == null || levelSelection == null || screen == null) return;
            if (screen != options && pendingResolution.HasValue) CancelResolution();
            Show(screen);
        }

        /// <summary>Exits the application (or Play mode in the Unity editor).</summary>
        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildOptions()
        {
            options = BuildPage();
            options.AddToClassList("dm-options");

            // Only the content scrolls; Back stays fixed at the top.
            var header = Container("dm-options-header");
            header.Add(CreateBackButton(buttons?.backFromOptions, ShowHomeScreen));
            options.Add(header);

            optionsScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            optionsScroll.AddToClassList("dm-options-scroll");
            optionsContent = Container("dm-options-content");
            optionsScroll.Add(optionsContent);
            options.Add(optionsScroll);

            GameSettings values = SettingsManager.Current;

            var general = AddOptionsSection(SectionTitle(generalSectionTitle, buttons?.generalTab, "General", "general"));
            languageDropdown = AddDropdown(general, DefaultLabel(languageSettingLabel, "Idioma", "language"),
                new List<string> { T("lang_spanish"), T("lang_english") },
                values.general.language == "en" ? T("lang_english") : T("lang_spanish"),
                label => SettingsManager.SetLanguage(label == T("lang_english") ? "en" : "es"));
            simulateInactiveButton = AddBooleanButton(general, "",
                buttons?.simulateWhenInactive,
                () => SettingsManager.SetSimulateWhenInactive(!SettingsManager.Current.general.simulateWhenInactive));

            var sound = AddOptionsSection(SectionTitle(audioSectionTitle, null, "Sonido", "sound"));
            AddVolume(sound, "master", DefaultLabel(masterVolumeSettingLabel, "Volumen general", "volume_master"),
                values.audio.masterVolume, SettingsManager.SetMasterVolume);
            AddVolume(sound, "music", DefaultLabel(musicVolumeSettingLabel, "Volumen de la música", "volume_music"),
                values.audio.musicVolume, SettingsManager.SetMusicVolume);
            AddVolume(sound, "ui", DefaultLabel(uiVolumeSettingLabel, "Volumen de la interfaz", "volume_ui"),
                values.audio.uiVolume, SettingsManager.SetUiVolume);
            AddVolume(sound, "simulation", DefaultLabel(simulationVolumeSettingLabel, "Volumen de la partida / simulación", "volume_simulation"),
                values.audio.simulationVolume, SettingsManager.SetSimulationVolume);

            var graphics = AddOptionsSection(SectionTitle(graphicsSectionTitle, buttons?.graphicsTab, "Gráficos", "graphics"));
            fullScreenButton = AddBooleanButton(graphics, "", buttons?.fullScreen,
                () => SettingsManager.SetFullscreen(!SettingsManager.Current.graphics.fullscreen));
            resolutionDropdown = AddDropdown(graphics, DefaultLabel(resolutionSettingLabel, "Resolución", "resolution"),
                new List<string> { "..." }, "...", RequestResolutionChange);

            var controls = AddOptionsSection(SectionTitle(controlsSectionTitle, buttons?.controlsTab, "Controles", "controls"));
            mouseCaptureDropdown = AddDropdown(controls, DefaultLabel(mouseCaptureSettingLabel, "Atrapar el mouse", "mouse_capture"),
                new List<string> { T("mouse_always"), T("mouse_fullscreen"), T("mouse_never") },
                MouseCaptureLabel(values.controls.mouseCapture),
                label => SettingsManager.SetMouseCapture(MouseCaptureValue(label)));

            BuildResolutionConfirmation();
            RefreshResolutionChoices();
            RefreshOptionValues();
        }

        private static string SectionTitle(string overrideTitle, MainMenuButtonAction oldTab,
            string originalSpanish, string key)
        {
            if (!string.IsNullOrWhiteSpace(overrideTitle)) return overrideTitle;
            return MenuButtonText(oldTab, originalSpanish, key);
        }

        private VisualElement AddOptionsSection(string sectionTitle)
        {
            var section = Container("dm-options-section");
            section.Add(Label(sectionTitle, "dm-section-title"));
            optionsContent.Add(section);
            return section;
        }

        private DropdownField AddDropdown(VisualElement section, string caption, List<string> choices,
            string selectedValue, Action<string> onChanged)
        {
            var row = Container("dm-option-row");
            row.Add(Label(caption, "dm-option-name"));

            var dropdown = new DropdownField { choices = choices };
            dropdown.AddToClassList("dm-dropdown");
            dropdown.SetValueWithoutNotify(choices.Contains(selectedValue) ? selectedValue : choices[0]);
            dropdown.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            row.Add(dropdown);
            section.Add(row);
            return dropdown;
        }

        private Button AddBooleanButton(VisualElement section, string caption, MainMenuButtonAction buttonAction,
            Action toggle)
        {
            var row = Container("dm-option-row");
            if (!string.IsNullOrEmpty(caption)) row.Add(Label(caption, "dm-option-name"));
            var button = CreateButton("", () => TriggerButton(buttonAction, toggle), "dm-option-toggle-button");
            if (!string.IsNullOrEmpty(caption)) button.AddToClassList("dm-option-toggle-inline");
            row.Add(button);
            section.Add(row);
            return button;
        }

        private void SetBooleanButton(Button button, MainMenuButtonAction action, string fallback, bool enabled)
        {
            if (button == null) return;
            string key = button == fullScreenButton ? "fullscreen" : "simulate_inactive";
            string originalSpanish = button == fullScreenButton ? "Pantalla completa" : "Simular en ventana inactiva";
            button.text = MenuButtonText(action, originalSpanish, key) + (enabled ? ": " + T("yes") : ": " + T("no"));
            button.EnableInClassList("dm-active", enabled);
            SetButtonColor(button, ButtonVisualState.Normal);
        }

        private void AddVolume(VisualElement section, string key, string caption, int initialValue, Action<int> onChanged)
        {
            var row = Container("dm-volume-row");
            row.Add(Label(caption, "dm-volume-name"));
            var text = Label(initialValue.ToString(), "dm-value");
            row.Add(text);
            var slider = new Slider(0, 100);
            slider.AddToClassList("dm-slider");
            slider.SetValueWithoutNotify(initialValue);
            slider.RegisterValueChangedCallback(evt =>
            {
                int value = Mathf.RoundToInt(evt.newValue);
                text.text = value.ToString();
                onChanged(value);
            });
            row.Add(slider);
            section.Add(row);
            volumeSliders[key] = slider;
            volumeLabels[key] = text;
        }

        private static string MouseCaptureLabel(string value)
        {
            switch (value)
            {
                case "Always": return T("mouse_always");
                case "Never": return T("mouse_never");
                default: return T("mouse_fullscreen");
            }
        }

        private static string MouseCaptureValue(string label)
        {
            switch (label)
            {
                case var x when x == T("mouse_always"): return "Always";
                case var x when x == T("mouse_never"): return "Never";
                default: return "FullscreenOnly";
            }
        }

        private static string ResolutionLabel(Vector2Int value) => value.x + " × " + value.y;

        private void RefreshResolutionChoices()
        {
            if (resolutionDropdown == null) return;
            resolutionByLabel.Clear();
            var choices = new List<string>();
            foreach (var size in SettingsManager.GetSupportedResolutions())
            {
                string display = ResolutionLabel(size);
                resolutionByLabel.Add(display, size);
                choices.Add(display);
            }
            resolutionDropdown.choices = choices;
            var selected = SettingsManager.Current.graphics;
            resolutionDropdown.SetValueWithoutNotify(ResolutionLabel(
                new Vector2Int(selected.resolutionWidth, selected.resolutionHeight)));
        }

        private void RefreshOptionValues()
        {
            if (optionsContent == null) return;
            GameSettings values = SettingsManager.Current;
            languageDropdown?.SetValueWithoutNotify(values.general.language == "en" ? T("lang_english") : T("lang_spanish"));
            mouseCaptureDropdown?.SetValueWithoutNotify(MouseCaptureLabel(values.controls.mouseCapture));
            SetBooleanButton(fullScreenButton, buttons?.fullScreen, "Pantalla completa", values.graphics.fullscreen);
            SetBooleanButton(simulateInactiveButton, buttons?.simulateWhenInactive, "Simular en ventana inactiva",
                values.general.simulateWhenInactive);

            SetVolume("master", values.audio.masterVolume);
            SetVolume("music", values.audio.musicVolume);
            SetVolume("ui", values.audio.uiVolume);
            SetVolume("simulation", values.audio.simulationVolume);

            if (!pendingResolution.HasValue)
            {
                resolutionDropdown?.SetValueWithoutNotify(ResolutionLabel(
                    new Vector2Int(values.graphics.resolutionWidth, values.graphics.resolutionHeight)));
            }
        }

        private void SetVolume(string key, int value)
        {
            if (volumeSliders.TryGetValue(key, out Slider slider)) slider.SetValueWithoutNotify(value);
            if (volumeLabels.TryGetValue(key, out Label label)) label.text = value.ToString();
        }

        private void BuildResolutionConfirmation()
        {
            resolutionConfirmation = Container("dm-resolution-overlay");
            resolutionConfirmation.style.display = DisplayStyle.None;
            var dialog = Container("dm-resolution-dialog");
            dialog.Add(Label(T("resolution_question"), "dm-resolution-question"));
            dialog.Add(Label(T("resolution_info"), "dm-muted"));
            resolutionCountdownLabel = Label("", "dm-resolution-countdown");
            dialog.Add(resolutionCountdownLabel);
            var actions = Container("dm-resolution-actions");
            actions.Add(CreateButton(MenuButtonText(buttons?.confirmResolution, "Conservar resolución", "keep_resolution"),
                () => TriggerButton(buttons?.confirmResolution, ConfirmResolution), "dm-resolution-button"));
            actions.Add(CreateButton(MenuButtonText(buttons?.revertResolution, "Revertir", "revert"),
                () => TriggerButton(buttons?.revertResolution, CancelResolution), "dm-resolution-button"));
            dialog.Add(actions);
            resolutionConfirmation.Add(dialog);
            options.Add(resolutionConfirmation);
        }

        private void RequestResolutionChange(string text)
        {
            if (!resolutionByLabel.TryGetValue(text, out Vector2Int selected)) return;
            GameSettings values = SettingsManager.Current;
            if (selected.x == values.graphics.resolutionWidth && selected.y == values.graphics.resolutionHeight)
                return;

            if (!SettingsRuntimeApplier.TryPreviewResolution(selected.x, selected.y,
                    Mathf.Max(5, resolutionConfirmSeconds)))
            {
                Debug.LogWarning(T("resolution_preview_failed"));
                RefreshResolutionChoices();
                return;
            }

            resolutionTimer?.Pause();
            pendingResolution = selected;
            resolutionConfirmation.style.display = DisplayStyle.Flex;
            UpdateResolutionCountdown();
            resolutionTimer = resolutionConfirmation.schedule.Execute(UpdateResolutionCountdown).Every(100);
        }

        private void UpdateResolutionCountdown()
        {
            if (!pendingResolution.HasValue) return;
            int remaining = Mathf.CeilToInt(SettingsRuntimeApplier.ResolutionPreviewSecondsRemaining);
            resolutionCountdownLabel.text = T("time_remaining", remaining);
            if (!SettingsRuntimeApplier.IsResolutionPreviewActive || remaining == 0)
                CancelResolution();
        }

        private void ConfirmResolution()
        {
            if (!pendingResolution.HasValue) return;
            bool confirmed = SettingsRuntimeApplier.ConfirmResolutionPreview();
            CloseResolutionConfirmation();
            if (!confirmed)
            {
                Debug.LogWarning(T("resolution_preview_expired"));
                SettingsManager.RefreshDisplayModes();
            }
            RefreshResolutionChoices();
            RefreshOptionValues();
        }

        private void CancelResolution()
        {
            SettingsRuntimeApplier.CancelResolutionPreview();
            CloseResolutionConfirmation();
            RefreshOptionValues();
        }

        private void CloseResolutionConfirmation()
        {
            resolutionTimer?.Pause();
            resolutionTimer = null;
            pendingResolution = null;
            if (resolutionConfirmation != null)
                resolutionConfirmation.style.display = DisplayStyle.None;
        }

        private void BuildLevels()
        {
            levelSelection = BuildPage();
            var header = Container("dm-level-header");
            // The centered title has its own absolute layout, independent of the back button and stars.
            var title = Label(DefaultLabel(levelsTitle, "Niveles", "levels"), "dm-level-title");
            title.pickingMode = PickingMode.Ignore;
            header.Add(title);
            var back = CreateBackButton(buttons?.backFromLevels, ShowHomeScreen);
            back.AddToClassList("dm-level-back-button");
            header.Add(back);
            // The counter retains its number even when the sprite is not assigned.
            var totalBadge = Container("dm-total-stars");
            totalBadge.AddToClassList("dm-total-stars-with-icon");
            totalBadge.pickingMode = PickingMode.Ignore;
            totalBadge.Add(UIIconView.Create(
                GetIcon(_theme?.icons?.earnedStar, earnedStarSprite), "dm-total-star-icon"));
            starTotal = Label("0", "dm-total-star-count");
            totalBadge.Add(starTotal);
            header.Add(totalBadge);
            starTotal.pickingMode = PickingMode.Ignore;
            levelSelection.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("dm-level-scroll");
            levelGrid = Container("dm-level-grid");
            scroll.Add(levelGrid);
            levelSelection.Add(scroll);
            RefreshLevelButtons();
            UpdateStars();
        }

        /// <summary>Rebuild level cards from their asset definitions, not legacy scene entries.</summary>
        private void RefreshLevelButtons()
        {
            if (levelGrid == null) return;
            levelGrid.Clear();
            int earnedTotal = LevelProgress.CountStarsInLevels(levelDefinitions);
            foreach (DirectivaLevel level in DirectivaLevelOrder.Sort(levelDefinitions))
            {
                bool locked = earnedTotal < level.RequiredStars;
                var button = CreateButton("", () => Load(level), "dm-level-button");
                button.SetEnabled(!locked);
                button.Add(Label(level.LevelName, "dm-level-name"));
                var score = Container("dm-score");
                if (locked)
                {
                    score.Add(Label(level.RequiredStars.ToString(), "dm-required-stars-count"));
                    score.Add(UIIconView.Create(
                        GetIcon(_theme?.icons?.requiredStar, requiredStarsSprite), "dm-score-sprite"));
                    button.AddToClassList("dm-locked");
                }
                else
                {
                    AddScoreStars(score, level, LevelProgress.Current.GetStars(level.LevelNumber));
                }
                button.Add(score);
                SetButtonColor(button, ButtonVisualState.Normal);
                levelGrid.Add(button);
            }
        }

        /// <summary>Displays icon slots using assigned sprites only, never Unicode glyphs.</summary>
        private void AddScoreStars(VisualElement score, DirectivaLevel level, LevelStarFlags earned)
        {
            // Each slot represents its OWN objective; two earned stars need not be adjacent.
            LevelStarFlags[] objectives =
            {
                LevelStarFlags.Victory, LevelStarFlags.UntouchedNexus, LevelStarFlags.FastVictory
            };
            foreach (LevelStarFlags objective in objectives)
            {
                bool hasStar = (earned & objective) != 0;
                Sprite sprite = hasStar
                    ? GetIcon(_theme?.icons?.earnedStar, earnedStarSprite)
                    : GetIcon(_theme?.icons?.unearnedStar, unearnedStarSprite);
                var slot = UIIconView.Create(sprite, "dm-score-sprite");
                slot.tooltip = objective == LevelStarFlags.Victory ? T("star_victory") :
                    objective == LevelStarFlags.UntouchedNexus ? T("star_untouched") :
                    T("star_fast", level.FastVictorySeconds);
                score.Add(slot);
            }
        }

        private void UpdateStars()
        {
            if (starTotal != null)
                starTotal.text = LevelProgress.CountStarsInLevels(levelDefinitions).ToString();
        }

        private void Load(DirectivaLevel level)
        {
            if (level == null || LevelProgress.CountStarsInLevels(levelDefinitions) < level.RequiredStars)
                return;
            if (string.IsNullOrWhiteSpace(level.ScenePath))
            {
                Debug.LogError(T("level_missing_scene", level.LevelName), this);
                return;
            }
            // Resolve the exact asset path (including .unity). The string overload of
            // CanStreamedLevelBeLoaded can reject a path that SceneManager accepts.
            // Loading by the resolved index also avoids ambiguities between same-name scenes.
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath(level.ScenePath);
            if (sceneIndex < 0)
            {
                Debug.LogError(T("level_scene_unavailable", level.ScenePath), this);
                return;
            }
            LevelLaunchContext.Clear();
            try
            {
                LevelLaunchContext.SetPendingLevel(level);
                SceneManager.LoadScene(sceneIndex);
            }
            catch (Exception exception)
            {
                LevelLaunchContext.Clear();
                Debug.LogException(exception, this);
            }
        }
    }
}
