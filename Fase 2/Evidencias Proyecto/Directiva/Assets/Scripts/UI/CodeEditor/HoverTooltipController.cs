using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    /// <summary>
    /// One delayed tooltip manager shared by the complete Directiva code UI.
    ///
    /// Hover detection is performed centrally against registered VisualElement
    /// bounds instead of relying on the button itself receiving pointer events.
    /// This lets disabled controls (Step, Pause, etc.) still explain themselves.
    /// </summary>
    public sealed class HoverTooltipController
    {
        private sealed class Registration
        {
            public VisualElement Target;
            public Func<string> TextProvider;
        }

        private readonly VisualElement _eventRoot;
        private readonly VisualElement _overlayRoot;
        private readonly VisualElement _tooltip;
        private readonly Label _label;
        private readonly List<Registration> _registrations = new List<Registration>();

        private Registration _hovered;
        private IVisualElementScheduledItem _pendingShow;
        private Vector2 _lastPointerPosition;
        private float _delaySeconds;

        private const float ScreenMargin = 8f;
        private const float CursorOffsetX = 14f;
        private const float CursorOffsetY = 18f;

        public HoverTooltipController(
            VisualElement eventRoot,
            VisualElement overlayRoot,
            float delaySeconds)
        {
            _eventRoot = eventRoot;
            _overlayRoot = overlayRoot;
            _delaySeconds = Mathf.Max(0f, delaySeconds);

            _tooltip = new VisualElement();
            _tooltip.AddToClassList("directiva-tooltip");
            _tooltip.pickingMode = PickingMode.Ignore;
            _tooltip.style.display = DisplayStyle.None;

            _label = new Label();
            _label.AddToClassList("directiva-tooltip-label");
            _label.pickingMode = PickingMode.Ignore;

            _tooltip.Add(_label);
            _overlayRoot.Add(_tooltip);

            _eventRoot.RegisterCallback<PointerMoveEvent>(
                OnPointerMove,
                TrickleDown.TrickleDown);

            _eventRoot.RegisterCallback<PointerDownEvent>(
                _ => Hide(),
                TrickleDown.TrickleDown);

            _eventRoot.RegisterCallback<PointerLeaveEvent>(
                _ => Hide(),
                TrickleDown.TrickleDown);
        }

        public void SetDelay(float delaySeconds)
        {
            _delaySeconds = Mathf.Max(0f, delaySeconds);
        }

        public void Register(
            VisualElement target,
            Func<string> textProvider)
        {
            if (target == null || textProvider == null)
                return;

            // Disable Unity's immediate native tooltip for these controls.
            // Directiva's delayed localized tooltip is the single source of truth.
            target.tooltip = string.Empty;

            _registrations.Add(new Registration
            {
                Target = target,
                TextProvider = textProvider
            });
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            _lastPointerPosition = evt.position;

            var candidate = FindRegistrationAt(evt.position);

            if (!ReferenceEquals(candidate, _hovered))
            {
                Hide();
                _hovered = candidate;

                if (_hovered != null)
                    ScheduleShow(_hovered);

                return;
            }

            if (_hovered != null &&
                _tooltip.style.display.value == DisplayStyle.Flex &&
                _tooltip.resolvedStyle.visibility == Visibility.Visible)
            {
                PositionTooltip(_lastPointerPosition);
            }
        }

        private Registration FindRegistrationAt(Vector2 panelPosition)
        {
            // Reverse order gives the most recently registered overlapping element
            // priority, which matches visual stacking in our current UI.
            for (var i = _registrations.Count - 1; i >= 0; i--)
            {
                var registration = _registrations[i];
                var target = registration.Target;

                if (target == null ||
                    target.panel == null ||
                    target.resolvedStyle.display == DisplayStyle.None ||
                    target.resolvedStyle.visibility == Visibility.Hidden)
                {
                    continue;
                }

                var rect = target.worldBound;
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                if (rect.Contains(panelPosition))
                    return registration;
            }

            return null;
        }

        private void ScheduleShow(Registration registration)
        {
            var delayMs = Mathf.RoundToInt(_delaySeconds * 1000f);

            _pendingShow = _eventRoot.schedule.Execute(() =>
            {
                if (!ReferenceEquals(_hovered, registration))
                    return;

                var text = registration.TextProvider?.Invoke();
                if (string.IsNullOrWhiteSpace(text))
                    return;

                _label.text = text;

                var availableWidth = Mathf.Max(
                    1f,
                    _overlayRoot.contentRect.width - (ScreenMargin * 2f));

                _tooltip.style.maxWidth = availableWidth;

                // Let UI Toolkit calculate the final wrapped size without briefly
                // flashing the tooltip at an unclamped position.
                _tooltip.style.visibility = Visibility.Hidden;
                _tooltip.style.display = DisplayStyle.Flex;

                _overlayRoot.schedule.Execute(() =>
                {
                    if (!ReferenceEquals(_hovered, registration))
                        return;

                    PositionTooltip(_lastPointerPosition);
                    _tooltip.style.visibility = Visibility.Visible;
                }).ExecuteLater(1);
            });

            _pendingShow.ExecuteLater(delayMs);
        }

        private void PositionTooltip(Vector2 pointerPanelPosition)
        {
            var rootWidth = _overlayRoot.contentRect.width;
            var rootHeight = _overlayRoot.contentRect.height;

            if (rootWidth <= 0f || rootHeight <= 0f)
                return;

            var tooltipWidth = Mathf.Max(1f, _tooltip.resolvedStyle.width);
            var tooltipHeight = Mathf.Max(1f, _tooltip.resolvedStyle.height);

            var pointerLocal = _overlayRoot.WorldToLocal(pointerPanelPosition);

            // Prefer below-right of the pointer.
            var x = pointerLocal.x + CursorOffsetX;
            var y = pointerLocal.y + CursorOffsetY;

            // If that would leave the screen, prefer the opposite side of the
            // pointer before applying a final hard clamp.
            if (x + tooltipWidth > rootWidth - ScreenMargin)
                x = pointerLocal.x - CursorOffsetX - tooltipWidth;

            if (y + tooltipHeight > rootHeight - ScreenMargin)
                y = pointerLocal.y - CursorOffsetY - tooltipHeight;

            var maxX = Mathf.Max(
                ScreenMargin,
                rootWidth - tooltipWidth - ScreenMargin);

            var maxY = Mathf.Max(
                ScreenMargin,
                rootHeight - tooltipHeight - ScreenMargin);

            x = Mathf.Clamp(x, ScreenMargin, maxX);
            y = Mathf.Clamp(y, ScreenMargin, maxY);

            _tooltip.style.left = x;
            _tooltip.style.top = y;
        }

        public void Hide()
        {
            _pendingShow?.Pause();
            _pendingShow = null;
            _hovered = null;

            if (_tooltip != null)
            {
                _tooltip.style.display = DisplayStyle.None;
                _tooltip.style.visibility = Visibility.Hidden;
            }
        }
    }
}
