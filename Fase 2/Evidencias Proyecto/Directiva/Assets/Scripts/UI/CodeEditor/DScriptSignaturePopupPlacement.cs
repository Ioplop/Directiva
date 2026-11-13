using UnityEngine;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Pure geometry helper for signature popups. Prefer the space immediately
    /// below the editing line and center the popup under the caret; fall back
    /// above only when necessary. The popup itself ignores pointer input.
    /// </summary>
    public static class DScriptSignaturePopupPlacement
    {
        public static Vector2 Resolve(Vector2 caret, float lineHeight, Vector2 popup,
            Vector2 panel)
        {
            const float edge = 4f;
            const float gap = 6f;
            float maxX = Mathf.Max(edge, panel.x - popup.x - edge);
            float left = Mathf.Clamp(caret.x - popup.x * 0.5f, edge, maxX);
            float below = caret.y + lineHeight + gap;
            float above = caret.y - popup.y - gap;
            float top = below;
            if (below + popup.y > panel.y - edge && above >= edge)
                top = above;
            return new Vector2(left,
                Mathf.Clamp(top, edge, Mathf.Max(edge, panel.y - popup.y - edge)));
        }
    }
}
