using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Directiva.UI
{
    /// <summary>Sprites are intentionally optional. Missing sprites draw no character substitute.</summary>
    [Serializable]
    public sealed class DirectivaUIIcons
    {
        [Header("Simulation controls")]
        public Sprite play;
        public Sprite stop;
        public Sprite pause;
        public Sprite slowMotion;
        public Sprite doubleSpeed;
        public Sprite quintupleSpeed;

        [Header("Window and panel controls")]
        public Sprite openEditor;
        public Sprite minimize;
        public Sprite maximize;
        public Sprite restore;
        public Sprite arrowUp;
        public Sprite arrowDown;
        public Sprite arrowLeft;
        public Sprite arrowRight;

        [Header("Code editor")]
        public Sprite save;
        public Sprite revert;
        public Sprite addScript;
        public Sprite addFolder;
        public Sprite script;
        public Sprite folder;
        public Sprite step;
        public Sprite stepIn;
        public Sprite stepOut;
        public Sprite breakpoint;
        public Sprite options;
        public Sprite back;

        [Header("Checkboxes")]
        public Sprite checkedBox;
        public Sprite uncheckedBox;

        [Header("Level selection")]
        public Sprite earnedStar;
        public Sprite unearnedStar;
        public Sprite requiredStar;
        public Sprite lockedLevel;
    }

    /// <summary>
    /// Shared, inspector-editable appearance for MainMenu, Simulation and CodeEditor.
    /// Assign an explicit asset on a controller, or edit Resources/UI/DirectivaUITheme
    /// to change the default used by all three without editing scenes.
    /// </summary>
    [CreateAssetMenu(fileName = "DirectivaUITheme", menuName = "Directiva/UI/Theme")]
    public sealed class DirectivaUITheme : ScriptableObject
    {
        [Header("Typography (optional)")]
        [Tooltip("Font for menu labels, toolbars, explorer, console and modal UI. None = existing USS/Unity font.")]
        public FontAsset interfaceFont;
        [Tooltip("Font for the editable code text, gutter and indentation metrics. Prefer monospaced SDF fonts.")]
        public FontAsset codeFont;
        [Tooltip("Reserved for bold code glyphs; the editor currently uses one font for its text surface.")]
        public FontAsset codeBoldFont;

        [Header("Main Menu Branding")]
        [Tooltip("Optional title image for the main menu. None keeps the existing text title.")]
        public Sprite mainMenuLogo;
        [Tooltip("Height of the logo's bounding box in UI Toolkit points. The sprite keeps its aspect ratio. Zero uses the stylesheet default.")]
        [Min(0f)] public float mainMenuLogoHeight = 140f;
        [Tooltip("Maximum width of the logo's bounding box in UI Toolkit points. Zero uses the stylesheet default.")]
        [Min(0f)] public float mainMenuLogoMaxWidth = 720f;

        [Header("Icons (None means transparent / no glyph)")]
        public DirectivaUIIcons icons = new DirectivaUIIcons();

        public static DirectivaUITheme Resolve(DirectivaUITheme preferred)
        {
            return preferred != null ? preferred : Resources.Load<DirectivaUITheme>("UI/DirectivaUITheme");
        }

        public void ApplyInterfaceFont(VisualElement element)
        {
            if (element != null && interfaceFont != null)
                element.style.unityFontDefinition = new StyleFontDefinition(interfaceFont);
        }
    }
}
