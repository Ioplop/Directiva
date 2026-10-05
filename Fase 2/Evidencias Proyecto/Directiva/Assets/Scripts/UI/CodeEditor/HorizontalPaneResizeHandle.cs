using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Draggable vertical divider that changes the width of a target pane.
    /// </summary>
    public sealed class HorizontalPaneResizeHandle : VisualElement
    {
        private readonly VisualElement _target;
        private readonly float _minWidth;
        private readonly float _maxWidth;

        private bool _resizing;
        private float _startX;
        private float _startWidth;

        public HorizontalPaneResizeHandle(
            VisualElement target,
            float minWidth = 180f,
            float maxWidth = 520f)
        {
            _target = target;
            _minWidth = minWidth;
            _maxWidth = maxWidth;

            AddToClassList("horizontal-pane-resize-handle");

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => _resizing = false);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _target == null)
                return;

            _resizing = true;
            _startX = evt.position.x;
            _startWidth = _target.resolvedStyle.width;

            this.CapturePointer(evt.pointerId);
            AddToClassList("resizing");
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_resizing || !this.HasPointerCapture(evt.pointerId))
                return;

            var delta = evt.position.x - _startX;
            _target.style.width = Mathf.Clamp(_startWidth + delta, _minWidth, _maxWidth);

            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_resizing)
                return;

            _resizing = false;

            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);

            RemoveFromClassList("resizing");
            evt.StopPropagation();
        }
    }
}
