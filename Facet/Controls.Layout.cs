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

    public sealed class PreviewOptions : ControlOptions
    {
        /// Stage width / height.
        public float Aspect { get; set; } = 16f / 9f;

        /// Tick period in ms; 0 = every frame.
        public int EveryMs { get; set; }

        /// Show runs when the stage becomes visible (page and surface both showing), Tick while it
        /// is, Hide when it stops. All run under Safe; three Tick throws in a row stop ticking until the next Show.
        public Action<VisualElement> Show { get; set; }
        public Action<VisualElement> Tick { get; set; }
        public Action Hide { get; set; }

        /// An English key: a muted line under the stage.
        public string Note { get; set; }

        /// An English key shown as a link under the note; a click runs OnLink.
        public string Link { get; set; }
        public Action OnLink { get; set; }
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

        /* A pane the consumer draws in: a caps label, a stage that keeps its aspect, a note and a link.
           Returns the stage. It costs nothing while hidden: Show/Tick/Hide follow "stage on a panel
           and its surface visible", re-evaluated on attach, detach, and the surface's Shown/Hidden.
           ponytail: an ancestor hidden by display:none inside a visible page still counts as shown;
           upgrade path is checking resolvedStyle.display up the tree in the tick. */
        public static VisualElement Preview(VisualElement parent, string label, Action<PreviewOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var host = Host.For(parent);
            var t = Theme.For(parent); var sz = t.Sizes;
            var root = Ui.Box(parent);
            root.style.marginBottom = sz.Gap;
            Ui.Caps(label, root).style.marginBottom = sz.Gap / 2f;
            var stage = Ui.Box(root);
            stage.name = o.Id;
            Paint.Look(stage, t.Looks.Stage);
            Ui.SetBorderWidth(stage, sz.Line);
            Ui.SetRadius(stage, sz.Radius);
            stage.style.overflow = Overflow.Hidden;
            stage.style.alignSelf = Align.Stretch;
            float aspect = o.Aspect > 0f ? o.Aspect : 16f / 9f;
            Events.On<GeometryChangedEvent>(stage, _ =>
            {
                float h = stage.layout.width / aspect;
                if (stage.layout.width > 0f && Mathf.Abs(h - stage.layout.height) > 0.5f) stage.style.height = h;
            });
            if (o.Note != null) { var n = Ui.Muted(o.Note, root); n.style.marginTop = sz.Gap / 2f; }
            if (o.Link != null)
            {
                var link = Ui.Btn(o.Link, () => { if (o.OnLink != null) o.OnLink(); }, root);
                link.name = o.Id == null ? null : o.Id + "-link";
                link.style.alignSelf = Align.FlexStart;
                link.style.paddingLeft = 0f;
                link.style.paddingRight = 0f;
                Paint.Look(link, t.Looks.Link);
            }

            bool shown = false;
            int fails = 0;
            Surface owner = null;
            IVisualElementScheduledItem item = null;
            string site = "Preview '" + label + "' ";
            // Built once: the tick runs every frame at EveryMs 0 and must not allocate.
            string tickSite = site + "Tick", showSite = site + "Show", hideSite = site + "Hide";
            Action callTick = () => { if (o.Tick != null) o.Tick(stage); };
            Action callShow = () => { if (o.Show != null) o.Show(stage); };
            Action callHide = () => { if (o.Hide != null) o.Hide(); };
            Action tick = () =>
            {
                if (!shown) return;
                if (Safe.Run(host, tickSite, callTick)) fails = 0;
                else if (++fails >= 3 && item != null) item.Pause();   // until the next Show
                stage.MarkDirtyRepaint();
            };
            Action eval = () =>
            {
                bool want = stage.panel != null && (owner == null || owner.Visible);
                if (want == shown) return;
                shown = want;
                if (want)
                {
                    fails = 0;
                    Safe.Run(host, showSite, callShow);
                    if (o.Tick == null) return;
                    if (item == null) item = stage.schedule.Execute(tick).Every(Math.Max(1, o.EveryMs));
                    else item.Resume();
                }
                else
                {
                    if (item != null) item.Pause();
                    Safe.Run(host, hideSite, callHide);
                }
            };
            Action changed = () => eval();
            Events.On<AttachToPanelEvent>(stage, _ =>
            {
                owner = Popup.OwnerOf(stage);
                if (owner != null) { owner.Shown += changed; owner.Hidden += changed; }
                eval();
            });
            Events.On<DetachFromPanelEvent>(stage, _ =>
            {
                if (owner != null) { owner.Shown -= changed; owner.Hidden -= changed; owner = null; }
                if (shown) { shown = false; if (item != null) item.Pause(); Safe.Run(host, hideSite, callHide); }
            });
            if (stage.panel != null) { owner = Popup.OwnerOf(stage); if (owner != null) { owner.Shown += changed; owner.Hidden += changed; } eval(); }
            return stage;
        }
    }
}
