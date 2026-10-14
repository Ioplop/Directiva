using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UIElements;
using Directiva.Settings;
using Directiva.UI;

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
        [SerializeField] private int maxStars = 3;

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

        [Header("Levels (editable from the Inspector)")]
        [SerializeField] private List<LevelMenuEntry> levels = new List<LevelMenuEntry>();

        private DirectivaUITheme _theme;
        private UIDocument document;
        private VisualElement root;
        private VisualElement home;
        private VisualElement options;
        private VisualElement levelSelection;
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
            string caption = ButtonText(action, "Atrás").TrimStart('‹', ' ');
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
            SettingsManager.Initialize();
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
            actions.Add(CreateButton(ButtonText(buttons?.play, "Jugar"), () => TriggerButton(buttons?.play, ShowLevelsScreen), "dm-big-button"));
            actions.Add(CreateButton(ButtonText(buttons?.options, "Opciones"), () => TriggerButton(buttons?.options, ShowOptionsScreen), "dm-big-button"));
            actions.Add(CreateButton(ButtonText(buttons?.exit, "Salir"), () => TriggerButton(buttons?.exit, QuitApplication), "dm-big-button"));
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

            var general = AddOptionsSection(SectionTitle(generalSectionTitle, buttons?.generalTab, "General"));
            languageDropdown = AddDropdown(general, TextOrDefault(languageSettingLabel, "Idioma"),
                new List<string> { "Español", "Inglés" },
                values.general.language == "en" ? "Inglés" : "Español",
                label => SettingsManager.SetLanguage(label == "Inglés" ? "en" : "es"));
            simulateInactiveButton = AddBooleanButton(general, "",
                buttons?.simulateWhenInactive,
                () => SettingsManager.SetSimulateWhenInactive(!SettingsManager.Current.general.simulateWhenInactive));

            var sound = AddOptionsSection(SectionTitle(audioSectionTitle, null, "Sonido"));
            AddVolume(sound, "master", TextOrDefault(masterVolumeSettingLabel, "Volumen general"),
                values.audio.masterVolume, SettingsManager.SetMasterVolume);
            AddVolume(sound, "music", TextOrDefault(musicVolumeSettingLabel, "Volumen de la música"),
                values.audio.musicVolume, SettingsManager.SetMusicVolume);
            AddVolume(sound, "ui", TextOrDefault(uiVolumeSettingLabel, "Volumen de la interfaz"),
                values.audio.uiVolume, SettingsManager.SetUiVolume);
            AddVolume(sound, "simulation", TextOrDefault(simulationVolumeSettingLabel, "Volumen de la partida / simulación"),
                values.audio.simulationVolume, SettingsManager.SetSimulationVolume);

            var graphics = AddOptionsSection(SectionTitle(graphicsSectionTitle, buttons?.graphicsTab, "Gráficos"));
            fullScreenButton = AddBooleanButton(graphics, "", buttons?.fullScreen,
                () => SettingsManager.SetFullscreen(!SettingsManager.Current.graphics.fullscreen));
            resolutionDropdown = AddDropdown(graphics, TextOrDefault(resolutionSettingLabel, "Resolución"),
                new List<string> { "..." }, "...", RequestResolutionChange);

            var controls = AddOptionsSection(SectionTitle(controlsSectionTitle, buttons?.controlsTab, "Controles"));
            mouseCaptureDropdown = AddDropdown(controls, TextOrDefault(mouseCaptureSettingLabel, "Atrapar el mouse"),
                new List<string> { "Siempre", "En pantalla completa", "Nunca" },
                MouseCaptureLabel(values.controls.mouseCapture),
                label => SettingsManager.SetMouseCapture(MouseCaptureValue(label)));

            BuildResolutionConfirmation();
            RefreshResolutionChoices();
            RefreshOptionValues();
        }

        private static string SectionTitle(string overrideTitle, MainMenuButtonAction oldTab, string fallback)
        {
            return string.IsNullOrWhiteSpace(overrideTitle) ? ButtonText(oldTab, fallback) : overrideTitle;
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
            button.text = ButtonText(action, fallback) + (enabled ? ": Sí" : ": No");
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
                case "Always": return "Siempre";
                case "Never": return "Nunca";
                default: return "En pantalla completa";
            }
        }

        private static string MouseCaptureValue(string label)
        {
            switch (label)
            {
                case "Siempre": return "Always";
                case "Nunca": return "Never";
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
            languageDropdown?.SetValueWithoutNotify(values.general.language == "en" ? "Inglés" : "Español");
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
            dialog.Add(Label("¿Conservar esta resolución?", "dm-resolution-question"));
            dialog.Add(Label("Si no confirmas, se restaurará la selección anterior.", "dm-muted"));
            resolutionCountdownLabel = Label("", "dm-resolution-countdown");
            dialog.Add(resolutionCountdownLabel);
            var actions = Container("dm-resolution-actions");
            actions.Add(CreateButton(ButtonText(buttons?.confirmResolution, "Conservar resolución"),
                () => TriggerButton(buttons?.confirmResolution, ConfirmResolution), "dm-resolution-button"));
            actions.Add(CreateButton(ButtonText(buttons?.revertResolution, "Revertir"),
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
                Debug.LogWarning("[Directiva Settings] Unable to preview selected resolution.");
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
            resolutionCountdownLabel.text = "Tiempo restante: " + remaining + " s";
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
                Debug.LogWarning("[Directiva Settings] Resolution preview expired or display mode became unavailable.");
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
            var title = Label(TextOrDefault(levelsTitle, "Niveles"), "dm-level-title");
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
            var grid = Container("dm-level-grid");
            scroll.Add(grid);
            levelSelection.Add(scroll);
            for (int i = 0; i < levels.Count; i++)
            {
                var entry = levels[i];
                if (entry == null) continue;
                var button = CreateButton("", () => Load(entry), "dm-level-button");
                button.SetEnabled(!entry.locked);
                button.Add(Label(entry.name, "dm-level-name"));
                var score = Container("dm-score");
                if (entry.locked)
                {
                    // Zero means "automatic" for compatibility with existing scenes: levels
                    // 4, 5, 6 display 9, 12, 15 for the usual three-star progression.
                    int required = entry.requiredStars > 0
                        ? entry.requiredStars
                        : i * Mathf.Max(0, maxStars);
                    score.Add(Label(required.ToString(), "dm-required-stars-count"));
                    score.Add(UIIconView.Create(
                        GetIcon(_theme?.icons?.requiredStar, requiredStarsSprite), "dm-score-sprite"));
                }
                else
                {
                    AddScoreStars(score, entry.stars);
                }
                button.Add(score);
                if (entry.locked) button.AddToClassList("dm-locked");
                SetButtonColor(button, ButtonVisualState.Normal);
                grid.Add(button);
            }
            UpdateStars();
        }

        /// <summary>Displays icon slots using assigned sprites only, never Unicode glyphs.</summary>
        private void AddScoreStars(VisualElement score, int count)
        {
            int slots = Mathf.Max(0, maxStars);
            int earned = Mathf.Clamp(count, 0, slots);
            for (int i = 0; i < slots; i++)
            {
                Sprite icon = i < earned
                    ? GetIcon(_theme?.icons?.earnedStar, earnedStarSprite)
                    : GetIcon(_theme?.icons?.unearnedStar, unearnedStarSprite);
                score.Add(UIIconView.Create(icon, "dm-score-sprite"));
            }
        }

        private void UpdateStars()
        {
            if (starTotal == null) return;
            int total = 0;
            foreach (var entry in levels)
                if (entry != null && !entry.locked) total += Mathf.Max(0, entry.stars);
            starTotal.text = total.ToString();
        }

        private void Load(LevelMenuEntry entry)
        {
            if (entry == null || entry.locked) return;
            if (string.IsNullOrWhiteSpace(entry.sceneName))
            {
                Debug.LogError($"[Directiva] Level '{entry.name}' has no destination scene.", this);
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(entry.sceneName))
            {
                Debug.LogError($"[Directiva] Scene '{entry.sceneName}' is not available in Build Profiles / Build Settings.", this);
                return;
            }
            LevelLaunchContext.Clear();
            try
            {
                entry.onBeforeLoad?.Invoke();
                LevelLaunchContext.SetPending(entry.launchId, entry.name);
                SceneManager.LoadScene(entry.sceneName);
            }
            catch (Exception exception)
            {
                LevelLaunchContext.Clear();
                Debug.LogException(exception, this);
            }
        }
    }
}
