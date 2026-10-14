using System;
using Directiva.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Host for the existing BasicCoding standalone scene. The code editor owns only
    /// the supplied child, while outer navigation belongs to this shell.
    /// </summary>
    public static class StandaloneEditorShell
    {
        public static VisualElement Create(VisualElement root, DirectivaUITheme theme, Action returnToMenu)
        {
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            root.style.minWidth = 0f;
            root.style.position = Position.Relative;
            theme?.ApplyInterfaceFont(root);

            var editorContainer = new VisualElement();
            editorContainer.style.flexGrow = 1f;
            editorContainer.style.minWidth = 0f;
            editorContainer.style.minHeight = 0f;
            root.Add(editorContainer);

            var menuButton = new Button(returnToMenu) { text = "Menú principal" };
            menuButton.tooltip = "Volver al menú principal";
            menuButton.style.position = Position.Absolute;
            menuButton.style.top = 6f;
            menuButton.style.right = 8f;
            menuButton.style.height = 30f;
            menuButton.style.minWidth = 115f;
            menuButton.style.paddingLeft = 8f;
            menuButton.style.paddingRight = 8f;
            menuButton.style.color = new Color(0.95f, 0.97f, 0.99f, 1f);
            menuButton.style.backgroundColor = new Color(0.18f, 0.23f, 0.31f, 1f);
            menuButton.style.borderTopWidth = 1f;
            menuButton.style.borderBottomWidth = 1f;
            menuButton.style.borderLeftWidth = 1f;
            menuButton.style.borderRightWidth = 1f;
            var border = new Color(0.37f, 0.46f, 0.60f, 1f);
            menuButton.style.borderTopColor = border;
            menuButton.style.borderBottomColor = border;
            menuButton.style.borderLeftColor = border;
            menuButton.style.borderRightColor = border;
            menuButton.style.borderTopLeftRadius = 5f;
            menuButton.style.borderTopRightRadius = 5f;
            menuButton.style.borderBottomLeftRadius = 5f;
            menuButton.style.borderBottomRightRadius = 5f;
            root.Add(menuButton);
            return editorContainer;
        }
    }
}
