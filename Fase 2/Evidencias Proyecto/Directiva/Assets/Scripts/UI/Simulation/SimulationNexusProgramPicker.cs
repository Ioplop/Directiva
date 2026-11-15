using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Directiva.Localization;

namespace Directiva.SimulationUI
{
    /// <summary>
    /// A folder tree built from saved relative script paths. The UI presents the
    /// names without .dscript, while leaves retain their exact filesystem paths.
    /// It never selects a program merely because one is available.
    /// </summary>
    internal sealed class NexusScriptMenuNode
    {
        internal string Name { get; }
        internal string ScriptPath { get; }
        internal bool IsFolder => ScriptPath == null;
        internal List<NexusScriptMenuNode> Children { get; } = new List<NexusScriptMenuNode>();

        private NexusScriptMenuNode(string name, string scriptPath)
        {
            Name = name;
            ScriptPath = scriptPath;
        }

        internal static NexusScriptMenuNode Build(IReadOnlyList<string> paths)
        {
            var root = new NexusScriptMenuNode(string.Empty, null);
            if (paths == null) return root;

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string normalized = path.Replace('\\', '/');
                string[] parts = normalized.Split('/');
                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[parts.Length - 1]))
                    continue;

                NexusScriptMenuNode parent = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (string.IsNullOrWhiteSpace(parts[i])) continue;
                    NexusScriptMenuNode child = parent.Children.Find(x =>
                        x.IsFolder && string.Equals(x.Name, parts[i], StringComparison.Ordinal));
                    if (child == null)
                    {
                        child = new NexusScriptMenuNode(parts[i], null);
                        parent.Children.Add(child);
                    }
                    parent = child;
                }

                string name = parts[parts.Length - 1];
                const string extension = ".dscript";
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - extension.Length);
                // Preserve the original path for the compilation/assignment API.
                if (!parent.Children.Exists(x => !x.IsFolder &&
                    string.Equals(x.ScriptPath, path, StringComparison.Ordinal)))
                    parent.Children.Add(new NexusScriptMenuNode(name, path));
            }

            Sort(root);
            return root;
        }

        private static void Sort(NexusScriptMenuNode node)
        {
            node.Children.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                int result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
            foreach (NexusScriptMenuNode child in node.Children)
                if (child.IsFolder) Sort(child);
        }
    }

    /// <summary>
    /// World-anchored Nexus program selector. Menus are drawn into a separate
    /// overlay so cascade submenus are not clipped by the compact Nexus panel.
    /// </summary>
    public sealed class SimulationNexusProgramPicker
    {
        private const float MenuWidth = 262f;
        private const float MenuRowHeight = 30f;
        private const float MenuPadding = 8f;
        private const float ViewportPadding = 6f;
        private const int MaxRowsBeforeScrolling = 14;

        private readonly VisualElement panel;
        private readonly VisualElement overlay;
        private readonly VisualElement menuLayer;
        private readonly Label heading;
        private readonly Label unsavedWarning;
        private readonly Label lockedWarning;
        private readonly Label scriptLabel;
        private readonly Label functionLabel;
        private readonly Label status;
        private readonly Button script;
        private readonly Button function;
        private readonly Label scriptValue;
        private readonly Label functionValue;
        private readonly Button close;
        private readonly List<string> scriptChoices = new List<string>();
        private readonly List<string> functionChoices = new List<string>();
        private readonly List<VisualElement> popupColumns = new List<VisualElement>();
        private readonly List<NexusScriptMenuNode> openFolders = new List<NexusScriptMenuNode>();
        private readonly List<Button> openFolderRows = new List<Button>();
        private NexusScriptMenuNode scriptTree = NexusScriptMenuNode.Build(null);

        private string selectedScript = string.Empty;
        private string selectedFunction = string.Empty;
        private bool visible;
        private bool hasUnsavedScripts;
        private bool selectionLocked;
        private string statusLocalizationKey;
        private PopupKind activePopup;

        private enum PopupKind { None, Scripts, Functions }
        private static string T(string key) => DirectivaLocalization.Get("Simulation", key);

        public event Action<string> ScriptChanged;
        public event Action<string, string> FunctionChanged;
        public event Action RefreshRequested;

        public bool Visible => visible;
        public bool SelectionLocked => selectionLocked;
        public string SelectedScript => selectedScript;
        public string SelectedFunction => selectedFunction;
        internal bool IsPopupOpen => activePopup != PopupKind.None;
        internal int OpenMenuCount => popupColumns.Count;

        public SimulationNexusProgramPicker(VisualElement overlay)
        {
            this.overlay = overlay;
            panel = new VisualElement { pickingMode = PickingMode.Position };
            panel.AddToClassList("sim-nexus-program-panel");
            panel.style.display = DisplayStyle.None;
            // The selector remains behind the editor/HUD even if created later.
            overlay.Insert(0, panel);

            var header = new VisualElement();
            header.AddToClassList("sim-nexus-panel-header");
            panel.Add(header);
            heading = new Label();
            heading.AddToClassList("sim-nexus-panel-title");
            header.Add(heading);
            close = new Button(Hide) { text = "X" };
            close.AddToClassList("sim-nexus-panel-close");
            header.Add(close);

            unsavedWarning = new Label();
            unsavedWarning.AddToClassList("sim-nexus-panel-warning");
            unsavedWarning.style.display = DisplayStyle.None;
            panel.Add(unsavedWarning);

            lockedWarning = new Label { name = "nexus-locked-warning" };
            lockedWarning.AddToClassList("sim-nexus-panel-warning");
            lockedWarning.style.display = DisplayStyle.None;
            panel.Add(lockedWarning);

            scriptLabel = new Label();
            var scriptRow = CreateRow(scriptLabel);
            script = CreateSelector("nexus-script-dropdown", () => OpenPopup(PopupKind.Scripts),
                out scriptValue);
            scriptRow.Add(script);

            functionLabel = new Label();
            var functionRow = CreateRow(functionLabel);
            function = CreateSelector("nexus-function-dropdown", () => OpenPopup(PopupKind.Functions),
                out functionValue);
            functionRow.Add(function);

            status = new Label();
            status.AddToClassList("sim-nexus-panel-status");
            status.style.display = DisplayStyle.None;
            panel.Add(status);

            // This layer is above all regular controls only while a menu is open.
            // Its transparent dismissal surface closes the menu on outside clicks.
            menuLayer = new VisualElement { name = "nexus-popup-layer", pickingMode = PickingMode.Ignore };
            menuLayer.AddToClassList("sim-nexus-menu-layer");
            menuLayer.style.display = DisplayStyle.None;
            overlay.Add(menuLayer);
            var dismissSurface = new VisualElement { name = "nexus-popup-dismiss" };
            dismissSurface.AddToClassList("sim-nexus-menu-dismiss");
            dismissSurface.RegisterCallback<PointerDownEvent>(_ => ClosePopup());
            menuLayer.Add(dismissSurface);
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (activePopup != PopupKind.None && evt.keyCode == KeyCode.Escape)
                {
                    ClosePopup();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            overlay.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (IsPopupOpen) ClosePopup(); // Avoid stale positions after resize.
            });

            Relocalize();
            UpdateSelectionAvailability();
        }

        private VisualElement CreateRow(Label rowLabel)
        {
            var row = new VisualElement();
            row.AddToClassList("sim-nexus-panel-row");
            rowLabel.AddToClassList("sim-nexus-panel-field-label");
            row.Add(rowLabel);
            panel.Add(row);
            return row;
        }

        private static Button CreateSelector(string name, Action onClick, out Label value)
        {
            var selector = new Button(onClick) { name = name };
            selector.AddToClassList("sim-nexus-panel-select");
            value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("sim-nexus-panel-select-text");
            selector.Add(value);
            var arrow = new Label("\u25bc") { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("sim-nexus-panel-select-arrow");
            selector.Add(arrow);
            return selector;
        }

        private void UpdateSelectorLabels()
        {
            scriptValue.text = string.IsNullOrEmpty(selectedScript)
                ? T("nexus_choose_script") : WithoutExtension(selectedScript);
            functionValue.text = string.IsNullOrEmpty(selectedFunction)
                ? T("nexus_choose_function") : selectedFunction;
            script.tooltip = selectedScript;
            function.tooltip = selectedFunction;
        }

        private static string WithoutExtension(string path)
        {
            const string extension = ".dscript";
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - extension.Length) : path;
        }

        public void Relocalize()
        {
            heading.text = T("nexus_program_title");
            scriptLabel.text = T("nexus_script");
            functionLabel.text = T("nexus_function");
            unsavedWarning.text = T("nexus_unsaved_warning");
            lockedWarning.text = T("nexus_locked_warning");
            close.tooltip = T("nexus_close");
            if (!string.IsNullOrEmpty(statusLocalizationKey))
                status.text = T(statusLocalizationKey);
            UpdateSelectorLabels();
        }

        public void SetUnsavedChanges(bool hasChanges)
        {
            if (hasUnsavedScripts == hasChanges) return;
            hasUnsavedScripts = hasChanges;
            UpdateWarningVisibility();
        }

        /// <summary>Only Stop unlocks the selector; pause and match end do not.</summary>
        public void SetSelectionLocked(bool locked)
        {
            if (selectionLocked == locked) return;
            selectionLocked = locked;
            if (locked) ClosePopup();
            UpdateSelectionAvailability();
            UpdateWarningVisibility();
        }

        private void UpdateSelectionAvailability()
        {
            script.SetEnabled(!selectionLocked && scriptChoices.Count > 0);
            function.SetEnabled(!selectionLocked && functionChoices.Count > 0);
        }

        private void UpdateWarningVisibility()
        {
            lockedWarning.style.display = selectionLocked ? DisplayStyle.Flex : DisplayStyle.None;
            unsavedWarning.style.display = !selectionLocked && hasUnsavedScripts
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetScripts(IReadOnlyList<string> options, string preferred)
        {
            ClosePopup();
            if (!SameChoices(scriptChoices, options))
            {
                scriptChoices.Clear();
                if (options != null) scriptChoices.AddRange(options);
                scriptTree = NexusScriptMenuNode.Build(scriptChoices);
            }
            // No implicit selection or simulated program assignment on refresh.
            selectedScript = !string.IsNullOrEmpty(preferred) && scriptChoices.Contains(preferred)
                ? preferred : string.Empty;
            UpdateSelectorLabels();
            UpdateSelectionAvailability();
        }

        public void SetFunctions(IReadOnlyList<string> options, string preferred)
        {
            ClosePopup();
            if (!SameChoices(functionChoices, options))
            {
                functionChoices.Clear();
                if (options != null) functionChoices.AddRange(options);
            }
            selectedFunction = preferred != null && functionChoices.Contains(preferred)
                ? preferred : (functionChoices.Count > 0 ? functionChoices[0] : string.Empty);
            UpdateSelectorLabels();
            UpdateSelectionAvailability();
        }

        private static bool SameChoices(IReadOnlyList<string> oldValues, IReadOnlyList<string> newValues)
        {
            if (newValues == null) return oldValues.Count == 0;
            if (oldValues.Count != newValues.Count) return false;
            for (int i = 0; i < oldValues.Count; i++)
                if (!string.Equals(oldValues[i], newValues[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        private void OpenPopup(PopupKind kind)
        {
            if (!visible || selectionLocked) return;
            // Reload saved sources before showing choices. This is not an assignment.
            RefreshRequested?.Invoke();
            if (!visible || selectionLocked) return;
            if (kind == PopupKind.Scripts && scriptChoices.Count == 0 ||
                kind == PopupKind.Functions && functionChoices.Count == 0) return;

            if (activePopup == kind) { ClosePopup(); return; }
            ClosePopup();
            activePopup = kind;
            menuLayer.style.display = DisplayStyle.Flex;
            if (kind == PopupKind.Scripts)
                AddMenu(scriptTree.Children, 0, script, false);
            else
            {
                var names = new List<NexusScriptMenuNode>();
                foreach (string item in functionChoices)
                    names.Add(NexusScriptMenuNode.Build(new[] { item }).Children[0]);
                AddMenu(names, 0, function, false);
            }
        }

        private void AddMenu(IReadOnlyList<NexusScriptMenuNode> nodes, int depth,
            VisualElement anchor, bool isSubmenu)
        {
            var menu = new VisualElement { name = "nexus-popup-menu-" + depth };
            menu.AddToClassList("sim-nexus-popup-menu");
            menu.pickingMode = PickingMode.Position;
            menuLayer.Add(menu);
            popupColumns.Add(menu);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("sim-nexus-popup-scroll");
            list.verticalScrollerVisibility = ScrollerVisibility.Auto;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // If a parent list scrolls, its child popup is no longer anchored
            // to the same visible row. Ignore layout's initial no-op updates.
            float previousScroll = list.verticalScroller.value;
            list.verticalScroller.valueChanged += value =>
            {
                if (Mathf.Abs(value - previousScroll) > 0.1f)
                    CloseMenusFrom(depth + 1);
                previousScroll = value;
            };
            menu.Add(list);

            foreach (NexusScriptMenuNode node in nodes)
            {
                NexusScriptMenuNode currentNode = node;
                var item = new Button();
                item.AddToClassList("sim-nexus-popup-item");
                item.tooltip = currentNode.ScriptPath ?? currentNode.Name;

                bool selected = currentNode.IsFolder == false &&
                    (activePopup == PopupKind.Scripts
                        ? currentNode.ScriptPath == selectedScript
                        : currentNode.Name == selectedFunction);
                if (selected) item.AddToClassList("sim-nexus-popup-selected");

                var title = new Label(currentNode.Name) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("sim-nexus-popup-item-text");
                item.Add(title);
                var marker = new Label(currentNode.IsFolder ? "\u25b6" : selected ? "\u2713" : string.Empty)
                { pickingMode = PickingMode.Ignore };
                marker.AddToClassList("sim-nexus-popup-item-marker");
                item.Add(marker);

                if (currentNode.IsFolder)
                {
                    item.RegisterCallback<PointerEnterEvent>(_ => OpenFolder(depth, currentNode, item));
                    item.clicked += () => OpenFolder(depth, currentNode, item);
                }
                else
                {
                    item.RegisterCallback<PointerEnterEvent>(_ => CloseMenusFrom(depth + 1));
                    item.clicked += () => SelectLeaf(currentNode);
                }
                list.Add(item);
            }

            PositionMenu(menu, anchor, isSubmenu, nodes.Count);
        }

        private void OpenFolder(int parentDepth, NexusScriptMenuNode folder, Button row)
        {
            if (activePopup != PopupKind.Scripts || !folder.IsFolder) return;
            if (openFolders.Count > parentDepth && ReferenceEquals(openFolders[parentDepth], folder))
                return;

            CloseMenusFrom(parentDepth + 1);
            openFolders.Add(folder);
            openFolderRows.Add(row);
            row.AddToClassList("sim-nexus-popup-expanded");
            AddMenu(folder.Children, parentDepth + 1, row, true);
        }

        private void SelectLeaf(NexusScriptMenuNode node)
        {
            if (selectionLocked) { ClosePopup(); return; }
            PopupKind kind = activePopup;
            ClosePopup();
            if (kind == PopupKind.Scripts)
            {
                if (node.ScriptPath == selectedScript) return;
                selectedScript = node.ScriptPath;
                UpdateSelectorLabels();
                ScriptChanged?.Invoke(selectedScript);
            }
            else if (kind == PopupKind.Functions)
            {
                if (node.Name == selectedFunction) return;
                selectedFunction = node.Name;
                UpdateSelectorLabels();
                FunctionChanged?.Invoke(selectedScript, selectedFunction);
            }
        }

        private void PositionMenu(VisualElement menu, VisualElement anchor, bool submenu, int count)
        {
            Rect bounds = overlay.worldBound;
            Rect from = anchor.worldBound;
            float availableWidth = bounds.width > 0f ? bounds.width : Screen.width;
            float availableHeight = bounds.height > 0f ? bounds.height : Screen.height;
            float width = Mathf.Min(MenuWidth, Mathf.Max(80f, availableWidth - ViewportPadding * 2f));
            float height = Mathf.Min(Mathf.Min(count, MaxRowsBeforeScrolling) * MenuRowHeight +
                MenuPadding, Mathf.Max(35f, availableHeight - ViewportPadding * 2f));

            float localX = from.xMin - bounds.xMin;
            float localY = from.yMin - bounds.yMin;
            float x;
            float y;
            if (submenu)
            {
                float right = from.xMax - bounds.xMin;
                // Prefer right, and flip left when near the viewport's edge.
                x = ChooseSubmenuX(localX, right, width, availableWidth);
                y = localY;
            }
            else
            {
                x = localX;
                y = from.yMax - bounds.yMin;
                if (y + height > availableHeight - ViewportPadding)
                    y = localY - height;
            }
            menu.style.width = width;
            menu.style.height = height;
            menu.style.left = Mathf.Clamp(x, ViewportPadding,
                Mathf.Max(ViewportPadding, availableWidth - width - ViewportPadding));
            menu.style.top = Mathf.Clamp(y, ViewportPadding,
                Mathf.Max(ViewportPadding, availableHeight - height - ViewportPadding));
        }

        internal static float ChooseSubmenuX(float anchorLeft, float anchorRight,
            float menuWidth, float viewportWidth)
        {
            return anchorRight + menuWidth <= viewportWidth - ViewportPadding
                ? anchorRight : anchorLeft - menuWidth;
        }

        private void CloseMenusFrom(int depth)
        {
            for (int i = popupColumns.Count - 1; i >= depth; i--)
            {
                popupColumns[i].RemoveFromHierarchy();
                popupColumns.RemoveAt(i);
            }
            for (int i = openFolderRows.Count - 1; i >= Mathf.Max(0, depth - 1); i--)
            {
                openFolderRows[i].RemoveFromClassList("sim-nexus-popup-expanded");
                openFolderRows.RemoveAt(i);
                openFolders.RemoveAt(i);
            }
        }

        private void ClosePopup()
        {
            CloseMenusFrom(0);
            activePopup = PopupKind.None;
            menuLayer.style.display = DisplayStyle.None;
        }

        public void SetLocalizedStatus(string localizationKey, bool isError = false)
        {
            SetStatus(T(localizationKey), isError);
            statusLocalizationKey = localizationKey;
        }

        public void SetStatus(string message, bool isError = false)
        {
            statusLocalizationKey = null;
            status.text = message ?? string.Empty;
            status.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None : DisplayStyle.Flex;
            status.EnableInClassList("sim-nexus-panel-error", isError);
        }

        public void ReleaseKeyboardFocus()
        {
            ClosePopup();
            var focused = panel.panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && (panel.Contains(focused) || menuLayer.Contains(focused)))
                focused.Blur();
        }

        public void Show()
        {
            visible = true;
            panel.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            ReleaseKeyboardFocus();
            visible = false;
            panel.style.display = DisplayStyle.None;
        }

        /// <summary>Anchor over a Nexus, in screen pixels with bottom-left origin.</summary>
        public void PositionAt(Vector2 screenPoint, UnityEngine.UIElements.IPanel uiPanel)
        {
            if (!Visible || uiPanel == null) return;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(uiPanel,
                new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            var rect = uiPanel.visualTree.worldBound;
            float width = Mathf.Min(430f, Mathf.Max(0f, rect.width - 12f));
            panel.style.width = width;
            float height = panel.resolvedStyle.height > 20f ? panel.resolvedStyle.height : 145f;
            float left = Mathf.Clamp(panelPoint.x - width * 0.5f, 6f,
                Mathf.Max(6f, rect.width - width - 6f));
            float top = panelPoint.y - height - 24f;
            if (top < 68f) top = panelPoint.y + 28f;
            top = Mathf.Clamp(top, 68f, Mathf.Max(68f, rect.height - height - 6f));
            panel.style.left = left;
            panel.style.top = top;
        }
    }
}
