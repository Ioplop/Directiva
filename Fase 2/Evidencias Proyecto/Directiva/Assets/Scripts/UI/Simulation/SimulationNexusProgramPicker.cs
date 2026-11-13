using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Directiva.Localization;

namespace Directiva.SimulationUI
{
    /// <summary>
    /// Compact, world-anchored program selector inside the existing simulation UIDocument.
    /// Its parent overlay remains click-through outside the panel.
    /// </summary>
    public sealed class SimulationNexusProgramPicker
    {
        private readonly VisualElement panel;
        private readonly Label heading;
        private readonly Label unsavedWarning;
        private readonly Label lockedWarning;
        private readonly Label scriptLabel;
        private readonly Label functionLabel;
        private readonly Label status;
        private readonly DropdownField script;
        private readonly DropdownField function;
        private readonly Button close;
        private bool filling;
        private bool visible;
        private bool hasUnsavedScripts;
        private bool selectionLocked;
        private string statusLocalizationKey;

        private static string T(string key) => DirectivaLocalization.Get("Simulation", key);

        public event Action<string> ScriptChanged;
        public event Action<string, string> FunctionChanged;
        public event Action RefreshRequested;

        public bool Visible => visible;
        public bool SelectionLocked => selectionLocked;
        public string SelectedScript => script.value;
        public string SelectedFunction => function.value;

        public SimulationNexusProgramPicker(VisualElement overlay)
        {
            panel = new VisualElement { pickingMode = PickingMode.Position };
            panel.AddToClassList("sim-nexus-program-panel");
            panel.style.display = DisplayStyle.None;
            // UI Toolkit's later siblings render and receive pointer input first.
            // Always sit behind the editor/HUD, regardless of creation order.
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

            // Keep labels outside the DropdownFields. BaseField's automatic label
            // widths made the entry field narrower than the script field and
            // clipped names despite unused space in the panel.
            scriptLabel = new Label();
            var scriptRow = CreateRow(scriptLabel);
            script = new DropdownField { name = "nexus-script-dropdown", choices = new List<string>() };
            script.AddToClassList("sim-nexus-panel-dropdown");
            script.RegisterValueChangedCallback(evt =>
            {
                script.tooltip = evt.newValue;
                if (!filling) ScriptChanged?.Invoke(evt.newValue);
            });
            // Refresh saved files before the popup builds its choices. Updating
            // choices programmatically never assigns/restarts a Nexus program.
            script.RegisterCallback<PointerDownEvent>(_ => RefreshRequested?.Invoke(),
                TrickleDown.TrickleDown);
            scriptRow.Add(script);

            functionLabel = new Label();
            var functionRow = CreateRow(functionLabel);
            function = new DropdownField { name = "nexus-function-dropdown", choices = new List<string>() };
            function.AddToClassList("sim-nexus-panel-dropdown");
            function.RegisterValueChangedCallback(evt =>
            {
                function.tooltip = evt.newValue;
                if (!filling) FunctionChanged?.Invoke(script.value, evt.newValue);
            });
            // A function added while this panel is open should appear after save.
            function.RegisterCallback<PointerDownEvent>(_ => RefreshRequested?.Invoke(),
                TrickleDown.TrickleDown);
            functionRow.Add(function);

            status = new Label();
            status.AddToClassList("sim-nexus-panel-status");
            status.style.display = DisplayStyle.None;
            panel.Add(status);
            Relocalize();
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
        }

        public void SetUnsavedChanges(bool hasChanges)
        {
            if (hasUnsavedScripts == hasChanges) return;
            hasUnsavedScripts = hasChanges;
            UpdateWarningVisibility();
        }

        /// <summary>
        /// Only Stop unlocks the selector. Pausing or finishing a match must not
        /// allow swapping the Nexus program without resetting its simulation.
        /// </summary>
        public void SetSelectionLocked(bool locked)
        {
            if (selectionLocked == locked) return;
            selectionLocked = locked;
            UpdateSelectionAvailability();
            UpdateWarningVisibility();
        }

        private void UpdateSelectionAvailability()
        {
            script.SetEnabled(!selectionLocked && script.choices.Count != 0);
            function.SetEnabled(!selectionLocked && function.choices.Count != 0);
        }

        private void UpdateWarningVisibility()
        {
            // Show the reason selection is disabled instead of stacking notices.
            lockedWarning.style.display = selectionLocked ? DisplayStyle.Flex : DisplayStyle.None;
            unsavedWarning.style.display = !selectionLocked && hasUnsavedScripts
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetScripts(IReadOnlyList<string> options, string preferred)
        {
            filling = true;
            if (!SameChoices(script.choices, options)) script.choices = new List<string>(options);
            // An empty preferred value is an intentional unassigned state.
            // Merely listing scripts must not claim that the first one is running.
            string chosen = !string.IsNullOrEmpty(preferred) && script.choices.Contains(preferred)
                ? preferred : string.Empty;
            script.SetValueWithoutNotify(chosen);
            script.tooltip = chosen;
            UpdateSelectionAvailability();
            filling = false;
        }

        public void SetFunctions(IReadOnlyList<string> options, string preferred)
        {
            filling = true;
            if (!SameChoices(function.choices, options)) function.choices = new List<string>(options);
            string chosen = preferred != null && function.choices.Contains(preferred)
                ? preferred : (function.choices.Count > 0 ? function.choices[0] : string.Empty);
            function.SetValueWithoutNotify(chosen);
            function.tooltip = chosen;
            UpdateSelectionAvailability();
            filling = false;
        }

        private static bool SameChoices(IReadOnlyList<string> current, IReadOnlyList<string> updated)
        {
            if (current == null || updated == null || current.Count != updated.Count) return false;
            for (int i = 0; i < current.Count; i++)
                if (!string.Equals(current[i], updated[i], StringComparison.Ordinal)) return false;
            return true;
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

        /// <summary>
        /// A dropdown can retain keyboard focus after another window is clicked.
        /// Release only focus owned by this selector, leaving editor focus intact.
        /// </summary>
        public void ReleaseKeyboardFocus()
        {
            var focused = panel.panel?.focusController?.focusedElement as VisualElement;
            if (focused != null && panel.Contains(focused))
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
            // The extra width fits script names, but never escapes a narrow game view.
            float width = Mathf.Min(430f, Mathf.Max(0f, rect.width - 12f));
            panel.style.width = width;
            float height = panel.resolvedStyle.height > 20f ? panel.resolvedStyle.height : 145f;
            // Prefer above the Nexus, but flip below it if space is insufficient.
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
