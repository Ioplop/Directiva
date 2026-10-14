using System;
using UnityEngine.UIElements;

namespace Directiva.SimulationUI
{
    /// <summary>Drag handle that resizes the docked editor without recreating its VisualElements.</summary>
    public sealed class SimulationEditorResizeHandle : VisualElement
    {
        private readonly Action<float> _setWidth;
        private float _startX;
        private float _startWidth;
        public bool IsDragging { get; private set; }

        public SimulationEditorResizeHandle(Action<float> setWidth)
        {
            _setWidth = setWidth;
            AddToClassList("sim-editor-resize");
            pickingMode = PickingMode.Position;
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up);
            RegisterCallback<PointerCaptureOutEvent>(_ => Finish());
        }

        private void Down(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            IsDragging = true;
            _startX = evt.position.x;
            _startWidth = parent.resolvedStyle.width;
            this.CapturePointer(evt.pointerId);
            AddToClassList("sim-resizing");
            evt.StopPropagation();
        }
        private void Move(PointerMoveEvent evt)
        {
            if (!IsDragging || !this.HasPointerCapture(evt.pointerId)) return;
            _setWidth?.Invoke(_startWidth + (evt.position.x - _startX));
            evt.StopPropagation();
        }
        private void Up(PointerUpEvent evt)
        {
            if (!IsDragging) return;
            Finish();
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }
        private void Finish()
        {
            IsDragging = false;
            RemoveFromClassList("sim-resizing");
        }
    }
}
