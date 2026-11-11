using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Directiva.Localization;

namespace Directiva.SimulationUI
{
    /// <summary>
    /// A world-anchored UI Toolkit panel inside the existing simulation UIDocument.
    /// The transparent overlay remains click-through outside this actual panel.
    /// </summary>
    public sealed class SimulationNexusProgramPicker
    {
        private readonly VisualElement panel;
        private readonly Label heading;
        private readonly Label help;
        private readonly Label status;
        private readonly DropdownField script;
        private readonly DropdownField function;
        private readonly Button apply;
        private readonly Button refresh;
        private readonly Button close;
        private bool filling;
        private bool visible;

        private static string T(string key) => DirectivaLocalization.Get("Simulation", key);

        public event Action<string> ScriptChanged;
        public event Action<string, string> ApplyRequested;
        public event Action RefreshRequested;

        public bool Visible => visible;
        public string SelectedScript => script.value;
        public string SelectedFunction => function.value;

        public SimulationNexusProgramPicker(VisualElement overlay)
        {
            panel = new VisualElement { pickingMode = PickingMode.Position };
            panel.AddToClassList("sim-nexus-program-panel");
            panel.style.display = DisplayStyle.None;
            overlay.Add(panel);

            var header = new VisualElement();
            header.AddToClassList("sim-nexus-panel-header");
            panel.Add(header);
            heading = new Label();
            heading.AddToClassList("sim-nexus-panel-title");
            header.Add(heading);
            close = new Button(() => Hide()) { text = "X" };
            close.AddToClassList("sim-nexus-panel-close");
            header.Add(close);

            help = new Label();
            help.AddToClassList("sim-nexus-panel-help");
            panel.Add(help);

            script = new DropdownField { choices = new List<string>() };
            script.AddToClassList("sim-nexus-panel-dropdown");
            script.RegisterValueChangedCallback(evt =>
            {
                if (!filling) ScriptChanged?.Invoke(evt.newValue);
            });
            panel.Add(script);

            function = new DropdownField { choices = new List<string>() };
            function.AddToClassList("sim-nexus-panel-dropdown");
            panel.Add(function);

            apply = new Button(() => ApplyRequested?.Invoke(script.value, function.value));
            apply.AddToClassList("sim-nexus-panel-apply");
            panel.Add(apply);
            refresh = new Button(() => RefreshRequested?.Invoke());
            refresh.AddToClassList("sim-nexus-panel-refresh");
            panel.Add(refresh);

            status = new Label();
            status.AddToClassList("sim-nexus-panel-status");
            panel.Add(status);
            Relocalize();
        }

        public void Relocalize()
        {
            heading.text = T("nexus_program_title");
            help.text = T("nexus_program_help");
            script.label = T("nexus_script");
            function.label = T("nexus_function");
            apply.text = T("nexus_apply");
            refresh.text = T("nexus_refresh");
            close.tooltip = T("nexus_close");
        }

        public void SetScripts(IReadOnlyList<string> options, string preferred)
        {
            filling = true;
            script.choices = new List<string>(options);
            string chosen = preferred != null && script.choices.Contains(preferred)
                ? preferred : (script.choices.Count > 0 ? script.choices[0] : string.Empty);
            script.SetValueWithoutNotify(chosen);
            script.SetEnabled(script.choices.Count != 0);
            filling = false;
            UpdateEnabled();
        }

        public void SetFunctions(IReadOnlyList<string> options, string preferred)
        {
            filling = true;
            function.choices = new List<string>(options);
            string chosen = preferred != null && function.choices.Contains(preferred)
                ? preferred : (function.choices.Count > 0 ? function.choices[0] : string.Empty);
            function.SetValueWithoutNotify(chosen);
            function.SetEnabled(function.choices.Count != 0);
            filling = false;
            UpdateEnabled();
        }

        public void SetStatus(string message, bool isError = false)
        {
            status.text = message ?? string.Empty;
            status.EnableInClassList("sim-nexus-panel-error", isError);
        }

        public void Show()
        {
            visible = true;
            panel.style.display = DisplayStyle.Flex;
        }
        public void Hide()
        {
            visible = false;
            panel.style.display = DisplayStyle.None;
        }

        /// <summary>Anchor over a Nexus, in screen pixels with bottom-left origin.</summary>
        public void PositionAt(Vector2 screenPoint, UnityEngine.UIElements.IPanel uiPanel)
        {
            if (!Visible || uiPanel == null) return;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(uiPanel,
                new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            float width = panel.resolvedStyle.width > 20f ? panel.resolvedStyle.width : 310f;
            float height = panel.resolvedStyle.height > 20f ? panel.resolvedStyle.height : 250f;
            var rect = uiPanel.visualTree.worldBound;
            // Prefer above the Nexus, but flip below it if space is insufficient.
            float left = Mathf.Clamp(panelPoint.x - width * 0.5f, 6f,
                Mathf.Max(6f, rect.width - width - 6f));
            float top = panelPoint.y - height - 24f;
            if (top < 68f) top = panelPoint.y + 28f;
            top = Mathf.Clamp(top, 68f, Mathf.Max(68f, rect.height - height - 6f));
            panel.style.left = left;
            panel.style.top = top;
        }

        private void UpdateEnabled() =>
            apply.SetEnabled(script.choices.Count != 0 && function.choices.Count != 0);
    }
}
