using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    public sealed class ColumnsOptions
    {
        /// Flex weights of the two columns (the mockup's 1.25fr / 1fr).
        public float Left { get; set; } = 1.25f;
        public float Right { get; set; } = 1f;

        /// The right column follows the viewport top while the page scrolls.
        public bool Sticky { get; set; } = true;

        /// Below this width the right column stacks under the left (no sticky then); it unstacks again
        /// at StackBelow + 40, so a parent that shrink-wraps the columns cannot flip them every frame.
        public float StackBelow { get; set; } = 560f;
    }

    public static partial class Controls
    {
        /* Settings on the left, a preview on the right. Returns the left column; `right` is the other.
           Sticky: the right column's translate.y keeps it at the enclosing ScrollView's viewport top,
           clamped to the columns. It is recomputed when the view scrolls or the geometry changes, so
           it reads layout only: no theme lookups (sizes are taken at build). */
        public static VisualElement Columns(VisualElement parent, out VisualElement right, Action<ColumnsOptions> configure = null)
        {
            var o = new ColumnsOptions();
            if (configure != null) Safe.Run(Host.For(parent), "configure Columns", () => configure(o));
            var root = Ui.Row(parent);
            float pad = Theme.For(root).Sizes.Pad;
            root.style.alignSelf = Align.Stretch;   // the parent's width, not the content's
            root.style.minWidth = 0f;
            float stackAt = o.StackBelow, unstackAt = o.StackBelow + 40f;
            var l = Ui.Box(root); var r = Ui.Box(root);
            foreach (var c in new[] { l, r }) { c.style.minWidth = 0f; c.style.flexShrink = 1f; }
            r.style.alignSelf = Align.FlexStart;   // its own height, so there is slack to slide in

            bool stacked = false;
            Action layout = () =>
            {
                root.style.flexDirection = stacked ? FlexDirection.Column : FlexDirection.Row;
                root.style.alignItems = stacked ? Align.Stretch : Align.FlexStart;
                l.style.marginRight = stacked ? 0f : pad;
                l.style.marginBottom = stacked ? pad : 0f;
                l.style.flexGrow = stacked ? 0f : o.Left;
                r.style.flexGrow = stacked ? 0f : o.Right;
                r.style.alignSelf = stacked ? Align.Stretch : Align.FlexStart;
                if (stacked) { l.style.flexBasis = StyleKeyword.Auto; r.style.flexBasis = StyleKeyword.Auto; }
                else { l.style.flexBasis = 0f; r.style.flexBasis = 0f; }
            };
            layout();

            ScrollView sv = null;
            float shown = 0f;
            Action place = () =>
            {
                float w = root.layout.width;
                if (w > 0f && (stacked ? w >= unstackAt : w < stackAt)) { stacked = !stacked; layout(); }
                float y = 0f;
                if (o.Sticky && !stacked && sv != null)
                {
                    float top = root.WorldToLocal(sv.contentViewport.worldBound.position).y;
                    float room = root.layout.height - r.layout.height;
                    // A pane taller than the viewport would be pinned with its bottom off screen: let it scroll.
                    bool tall = r.layout.height > sv.contentViewport.layout.height;
                    if (!tall && !float.IsNaN(top) && !float.IsNaN(room)) y = Mathf.Clamp(top, 0f, Mathf.Max(0f, room));
                }
                if (Mathf.Abs(y - shown) < 0.01f) return;
                shown = y;
                r.style.translate = new Translate(0f, y, 0f);
            };
            Action<float> scrolled = _ => place();
            EventCallback<GeometryChangedEvent> geo = _ => place();
            EventCallback<AttachToPanelEvent> attach = _ =>
            {
                sv = root.GetFirstAncestorOfType<ScrollView>();
                if (sv != null) sv.verticalScroller.valueChanged += scrolled;
                place();
            };
            EventCallback<DetachFromPanelEvent> detach = _ =>
            {
                if (sv != null) sv.verticalScroller.valueChanged -= scrolled;
                sv = null;
            };
            Events.On(root, geo); Events.On(r, geo);
            Events.On(root, attach); Events.On(root, detach);
            if (root.panel != null) attach(null);
            right = r;
            return l;
        }
    }
}
