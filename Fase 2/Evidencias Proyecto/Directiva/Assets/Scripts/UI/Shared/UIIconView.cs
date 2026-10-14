using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.UI
{
    /// <summary>
    /// Sprite-only UI Toolkit icon helpers. A missing sprite leaves a blank icon;
    /// no emojis, font glyphs or fallback characters are synthesized.
    /// </summary>
    public static class UIIconView
    {
        private const string IconName = "directiva-sprite-icon";

        public static Image Create(Sprite sprite, string className = null)
        {
            var image = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit };
            image.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(className))
                image.AddToClassList(className);
            return image;
        }

        public static void SetButton(Button button, Sprite sprite, float pixelSize = 18f)
        {
            if (button == null) return;
            button.text = string.Empty;
            var image = button.Q<Image>(IconName);
            if (image == null)
            {
                image = Create(null);
                image.name = IconName;
                image.style.flexShrink = 0f;
                button.Add(image);
            }

            image.sprite = sprite;
            image.style.width = pixelSize;
            image.style.height = pixelSize;
            image.style.display = sprite == null ? DisplayStyle.None : DisplayStyle.Flex;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
        }

        /// <summary>
        /// Keeps the real UI Toolkit Toggle (click, keyboard and state changes),
        /// but replaces its native themed checkmark with a sprite-only Image.
        /// The input's built-in background and border are removed so transparent
        /// pixels in the sprite reveal the UI underneath, not a white square.
        /// </summary>
        public static void SetToggle(Toggle toggle, Sprite checkedSprite, Sprite uncheckedSprite)
        {
            if (toggle == null)
                return;

            const float inputSize = 24f;
            const float iconSize = 20f;
            const string toggleIconName = "directiva-toggle-sprite-icon";

            void UpdateIcon()
            {
                var nativeMark = toggle.Q<VisualElement>(className: "unity-toggle__checkmark");
                var input = toggle.Q<VisualElement>(className: "unity-toggle__input")
                            ?? nativeMark?.parent;
                if (input == null)
                    return;

                // Unity's default theme may paint a box behind the checkmark.
                // Clearing both layers also works when neither sprite is assigned.
                input.style.backgroundColor = Color.clear;
                input.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                input.style.borderLeftWidth = 0f;
                input.style.borderRightWidth = 0f;
                input.style.borderTopWidth = 0f;
                input.style.borderBottomWidth = 0f;
                input.style.paddingLeft = 0f;
                input.style.paddingRight = 0f;
                input.style.paddingTop = 0f;
                input.style.paddingBottom = 0f;
                input.style.marginLeft = 0f;
                input.style.marginRight = 0f;
                input.style.marginTop = 0f;
                input.style.marginBottom = 0f;
                input.style.width = inputSize;
                input.style.height = inputSize;
                input.style.minWidth = inputSize;
                input.style.minHeight = inputSize;
                input.style.flexShrink = 0f;
                input.style.alignItems = Align.Center;
                input.style.justifyContent = Justify.Center;
                input.style.overflow = Overflow.Visible;

                // Do not let the native checkmark draw its own sprite, background,
                // or Unity-provided check. It remains part of the Toggle internally.
                if (nativeMark != null)
                    nativeMark.style.display = DisplayStyle.None;

                var icon = input.Q<Image>(toggleIconName);
                if (icon == null)
                {
                    icon = Create(null);
                    icon.name = toggleIconName;
                    icon.style.flexShrink = 0f;
                    input.Add(icon);
                }

                var sprite = toggle.value ? checkedSprite : uncheckedSprite;
                icon.sprite = sprite;
                icon.scaleMode = ScaleMode.ScaleToFit;
                icon.style.width = iconSize;
                icon.style.height = iconSize;
                icon.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            toggle.RegisterValueChangedCallback(_ => UpdateIcon());
            // The native theme styles are in effect by the time the element
            // joins a panel, so repeat our explicit overrides on attachment.
            toggle.RegisterCallback<AttachToPanelEvent>(_ => UpdateIcon());
            UpdateIcon();
        }
    }
}
