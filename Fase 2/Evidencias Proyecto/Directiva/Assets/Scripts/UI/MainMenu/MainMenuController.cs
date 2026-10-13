using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UIElements;

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

    /// <summary>Builds the three main-menu screens and their placeholder options using UI Toolkit.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Presentation")]
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private string gameTitle = "DIRECTIVA";
        [SerializeField] private string levelsTitle = "Niveles";
        [SerializeField] private int maxStars = 3;

        [Header("Level icons (optional sprites)")]
        [Tooltip("Image for earned stars, including the total counter. None uses the original filled-star glyph.")]
        [SerializeField] private Sprite earnedStarSprite;

        [Tooltip("Image for unearned stars. None uses the original empty-star glyph.")]
        [SerializeField] private Sprite unearnedStarSprite;

        // Keep the old reference serialized so a project update does not discard assigned assets.
        // The lock itself is no longer displayed; users can assign the new sprite independently.
        [HideInInspector, SerializeField] private Sprite lockedLevelSprite;

        [Tooltip("Sprite displayed beside the star requirement on locked levels. None uses the filled star glyph (★). Independent of Earned Star Sprite.")]
        [SerializeField] private Sprite requiredStarsSprite;

        [Header("3D backdrop / tint colors (alpha supported)")]
        [SerializeField] private MainMenuAppearance appearance = new MainMenuAppearance();

        [Header("Per-button events and default actions")]
        [SerializeField] private MainMenuButtonActions buttons = new MainMenuButtonActions();

        [Header("Placeholder settings (visual only)")]
        [SerializeField] private string languageSettingLabel = "Idioma";
        [SerializeField] private string resolutionSettingLabel = "Resolución";
        [SerializeField] private List<string> resolutionChoices = new List<string>
        {
            "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440", "3840 x 2160"
        };

        [Header("Option section titles (optional)")]
        [Tooltip("Leave empty to retain the old General tab label, including previous Inspector customizations.")]
        [SerializeField] private string generalSectionTitle;
        [SerializeField] private string controlsSectionTitle;
        [SerializeField] private string audioSectionTitle;
        [SerializeField] private string graphicsSectionTitle;

        [Header("Levels (editable from the Inspector)")]
        [SerializeField] private List<LevelMenuEntry> levels = new List<LevelMenuEntry>();

        private UIDocument document;
        private VisualElement root;
        private VisualElement home;
        private VisualElement options;
        private VisualElement levelSelection;
        private VisualElement optionsContent;
        private ScrollView optionsScroll;
        private Label starTotal;
        private Button fullScreenButton;
        private bool previewFullScreen = true;
        private string selectedLanguage = "Español";
        private string selectedResolution = "1920 x 1080";

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
        private static VisualElement CreateLevelIcon(Sprite sprite, string fallbackGlyph,
            string fallbackClass, string spriteClass)
        {
            if (sprite == null) return Label(fallbackGlyph, fallbackClass);

            var icon = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList(spriteClass);
            icon.pickingMode = PickingMode.Ignore;
            return icon;
        }

        private void OnEnable()
        {
            if (appearance == null) appearance = new MainMenuAppearance();
            if (buttons == null) buttons = new MainMenuButtonActions();
            if (buttons.fullScreen == null) buttons.fullScreen = new MainMenuButtonAction("Pantalla completa");
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.Clear();
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet)) root.styleSheets.Add(styleSheet);
            root.AddToClassList("dm-root");
            root.style.backgroundColor = appearance != null ? appearance.screenOverlay : Color.clear;
            BuildHome();
            BuildOptions();
            BuildLevels();
            Show(home);
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
            home.Add(Label(gameTitle, "dm-title"));
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

            // Back stays fixed; only the options list itself scrolls.
            var header = Container("dm-options-header");
            header.Add(CreateButton(ButtonText(buttons?.backFromOptions, "‹ Atrás"),
                () => TriggerButton(buttons?.backFromOptions, ShowHomeScreen), "dm-back-button"));
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

            var general = AddOptionsSection(SectionTitle(generalSectionTitle, buttons?.generalTab, "General"));
            AddDropdown(general, TextOrDefault(languageSettingLabel, "Idioma"),
                new List<string> { "Español", "Inglés" }, selectedLanguage,
                value => selectedLanguage = value);

            var controls = AddOptionsSection(SectionTitle(controlsSectionTitle, buttons?.controlsTab, "Controles"));
            controls.Add(Label("Los ajustes de controles se implementarán más adelante.", "dm-muted"));

            var audio = AddOptionsSection(SectionTitle(audioSectionTitle, buttons?.audioTab, "Audio"));
            AddVolume(audio, "Volumen", 100);
            AddVolume(audio, "Volumen de la música", 50);
            AddVolume(audio, "Volumen SFX", 50);
            AddVolume(audio, "Volumen ambiental", 25);
            AddVolume(audio, "Volumen UI", 100);

            var graphics = AddOptionsSection(SectionTitle(graphicsSectionTitle, buttons?.graphicsTab, "Gráficos"));
            AddGraphicsOptions(graphics);
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

        /// <summary>Creates a preview-only dropdown; selections never modify game settings.</summary>
        private void AddDropdown(VisualElement section, string caption, List<string> choices,
            string selectedValue, Action<string> onChanged)
        {
            if (choices == null || choices.Count == 0) return;

            var row = Container("dm-option-row");
            row.Add(Label(caption, "dm-option-name"));

            var dropdown = new DropdownField
            {
                choices = choices
            };
            dropdown.AddToClassList("dm-dropdown");
            dropdown.SetValueWithoutNotify(choices.Contains(selectedValue) ? selectedValue : choices[0]);
            dropdown.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            row.Add(dropdown);
            section.Add(row);
        }

        private void AddGraphicsOptions(VisualElement section)
        {
            var fullScreenRow = Container("dm-option-row");
            fullScreenButton = CreateButton("", () => TriggerButton(buttons?.fullScreen, ToggleFullScreenPreview), "dm-option-toggle-button");
            fullScreenRow.Add(fullScreenButton);
            section.Add(fullScreenRow);
            UpdateFullScreenPreview();

            var choices = resolutionChoices != null && resolutionChoices.Count > 0
                ? new List<string>(resolutionChoices)
                : new List<string> { "1280 x 720", "1600 x 900", "1920 x 1080", "2560 x 1440", "3840 x 2160" };
            choices.RemoveAll(string.IsNullOrWhiteSpace);
            if (choices.Count == 0) choices.Add("1920 x 1080");
            AddDropdown(section, TextOrDefault(resolutionSettingLabel, "Resolución"), choices, selectedResolution,
                value => selectedResolution = value);
        }

        private void ToggleFullScreenPreview()
        {
            // Visual mockup only: intentionally does not call Screen.fullScreen.
            previewFullScreen = !previewFullScreen;
            UpdateFullScreenPreview();
        }

        private void UpdateFullScreenPreview()
        {
            if (fullScreenButton == null) return;
            fullScreenButton.text = ButtonText(buttons?.fullScreen, "Pantalla completa") +
                                    (previewFullScreen ? ": Sí" : ": No");
            fullScreenButton.EnableInClassList("dm-active", previewFullScreen);
            SetButtonColor(fullScreenButton, ButtonVisualState.Normal);
        }

        private void AddVolume(VisualElement section, string text, int defaultValue)
        {
            var row = Container("dm-volume-row");
            row.Add(Label(text, "dm-volume-name"));
            var value = Label(defaultValue.ToString(), "dm-value");
            row.Add(value);
            var slider = new Slider(0, 100) { value = defaultValue };
            slider.AddToClassList("dm-slider");
            slider.RegisterValueChangedCallback(evt => value.text = Mathf.RoundToInt(evt.newValue).ToString());
            row.Add(slider);
            section.Add(row);
        }

        private void BuildLevels()
        {
            levelSelection = BuildPage();
            var header = Container("dm-level-header");
            // The centered title has its own absolute layout, independent of the back button and stars.
            var title = Label(TextOrDefault(levelsTitle, "Niveles"), "dm-level-title");
            title.pickingMode = PickingMode.Ignore;
            header.Add(title);
            var back = CreateButton(ButtonText(buttons?.backFromLevels, "‹ Atrás"),
                () => TriggerButton(buttons?.backFromLevels, ShowHomeScreen), "dm-back-button");
            back.AddToClassList("dm-level-back-button");
            header.Add(back);
            if (earnedStarSprite == null)
            {
                // Keep the original single-label appearance when no custom sprite is supplied.
                starTotal = Label("\u2605 0", "dm-total-stars");
                header.Add(starTotal);
            }
            else
            {
                // A separate icon allows the badge to use exactly the same sprite as earned stars.
                var totalBadge = Container("dm-total-stars");
                totalBadge.AddToClassList("dm-total-stars-with-icon");
                totalBadge.pickingMode = PickingMode.Ignore;
                totalBadge.Add(CreateLevelIcon(earnedStarSprite, "\u2605", "dm-total-stars", "dm-total-star-icon"));
                starTotal = Label("0", "dm-total-star-count");
                totalBadge.Add(starTotal);
                header.Add(totalBadge);
            }
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
                    score.Add(CreateLevelIcon(requiredStarsSprite, "\u2605",
                        "dm-required-star-glyph", "dm-score-sprite"));
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

        /// <summary>Displays earned and unearned stars, independently falling back to the original glyphs.</summary>
        private void AddScoreStars(VisualElement score, int count)
        {
            // No overrides: preserve the existing compact text rendering exactly.
            if (earnedStarSprite == null && unearnedStarSprite == null)
            {
                score.Add(Label(Stars(count), "dm-stars"));
                return;
            }

            int slots = Mathf.Max(0, maxStars);
            int earned = Mathf.Clamp(count, 0, slots);
            for (int i = 0; i < slots; i++)
            {
                bool isEarned = i < earned;
                score.Add(CreateLevelIcon(isEarned ? earnedStarSprite : unearnedStarSprite,
                    isEarned ? "\u2605" : "\u2606", "dm-stars", "dm-score-sprite"));
            }
        }

        private string Stars(int count)
        {
            return new string('\u2605', Mathf.Clamp(count, 0, Mathf.Max(0, maxStars))) +
                   new string('\u2606', Mathf.Max(0, maxStars - Mathf.Clamp(count, 0, Mathf.Max(0, maxStars))));
        }

        private void UpdateStars()
        {
            if (starTotal == null) return;
            int total = 0;
            foreach (var entry in levels) if (entry != null && !entry.locked) total += Mathf.Max(0, entry.stars);
            starTotal.text = earnedStarSprite == null ? "\u2605 " + total : total.ToString();
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
                LevelLaunchContext.SetPending(entry.launchId);
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
