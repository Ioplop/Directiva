using System;
using Directiva.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.SimulationUI
{
    /// <summary>
    /// Optional, localized confirmation before leaving the running scene for levels.
    /// Pure UI: showing or dismissing it never changes the simulation state.
    /// </summary>
    public sealed class SimulationLeaveConfirmation
    {
        private readonly VisualElement backdrop;
        private readonly Label title;
        private readonly Label message;
        private readonly Button cancelButton;
        private readonly Button confirmButton;
        private readonly Action onConfirmed;

        private static string T(string key) =>
            DirectivaLocalization.Get("Simulation", key);

        public bool IsVisible => backdrop.style.display.value == DisplayStyle.Flex;

        public SimulationLeaveConfirmation(VisualElement overlay, Action onConfirmed)
        {
            if (overlay == null)
                throw new ArgumentNullException(nameof(overlay));

            this.onConfirmed = onConfirmed;
            backdrop = new VisualElement { name = "simulation-leave-confirmation" };
            backdrop.AddToClassList("sim-leave-backdrop");
            backdrop.pickingMode = PickingMode.Position;
            backdrop.style.display = DisplayStyle.None;
            overlay.Add(backdrop);

            var card = new VisualElement();
            card.AddToClassList("sim-leave-card");
            backdrop.Add(card);

            title = new Label();
            title.AddToClassList("sim-leave-title");
            card.Add(title);

            message = new Label();
            message.AddToClassList("sim-leave-message");
            card.Add(message);

            var actions = new VisualElement();
            actions.AddToClassList("sim-leave-actions");
            card.Add(actions);

            cancelButton = new Button(Hide);
            cancelButton.AddToClassList("sim-leave-button");
            actions.Add(cancelButton);

            confirmButton = new Button(Confirm);
            confirmButton.AddToClassList("sim-leave-button");
            confirmButton.AddToClassList("sim-leave-button-primary");
            actions.Add(confirmButton);

            // Esc cancels the dialog without triggering the editor's shortcuts.
            backdrop.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                Hide();
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            Relocalize();
        }

        public void Show()
        {
            Relocalize();
            backdrop.BringToFront();
            backdrop.style.display = DisplayStyle.Flex;
            // Default focus is Cancel, so accidental Enter cannot discard a match.
            if (backdrop.panel != null)
                cancelButton.Focus();
        }

        public void Hide()
        {
            if (cancelButton.focusController?.focusedElement == cancelButton)
                cancelButton.Blur();
            if (confirmButton.focusController?.focusedElement == confirmButton)
                confirmButton.Blur();
            backdrop.style.display = DisplayStyle.None;
        }

        public void Confirm()
        {
            if (!IsVisible)
                return;
            Hide();
            onConfirmed?.Invoke();
        }

        public void Relocalize()
        {
            title.text = T("leave_levels_title");
            message.text = T("leave_levels_message");
            cancelButton.text = T("leave_levels_cancel");
            confirmButton.text = T("leave_levels_confirm");
        }
    }
}
