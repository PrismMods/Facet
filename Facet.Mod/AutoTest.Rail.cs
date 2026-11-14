using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    // The compact rail and the host's cursor hook: both follow the real pointer, so they move it on their own panel.
    sealed partial class AutoTest
    {
        // A pointer move to a panel position on `root`'s panel: unlike a bare MouseEnterEvent it moves the
        // panel's pointer, so what is under it stays hovered until the real pointer moves.
        static void MoveTo(VisualElement root, Vector2 panelPos)
        {
            var ev = new Event { type = EventType.MouseMove, mousePosition = panelPos };
            using (var e = PointerMoveEvent.GetPooled(ev)) root.panel.visualTree.SendEvent(e);
        }

        /* Compact: an icon strip as wide as an entry's padding and its icon, titles hidden, rules and the
           selection kept. Hovered past a 0.1 s dwell it widens over the page, which does not move; leaving
           narrows it. CompactButton draws a button at the top that flips Compact and says so. */
        IEnumerator SectionCompactRail()
        {
            var th = new Theme();
            th.Looks.Indicator = new Look { SelectedBg = Role.Accent };
            th.Looks.Rule = new Look { Bg = Role.Line };
            var s = new Surface(Host.Create("Compact", o => { o.Log = m => Main.Log("[compact] " + m); o.Theme = th; }), "FacetCompact", Layer.Window);
            try
            {
                var w = new Window(s.Root, "compact", "Compact rail", new Rect(60, 60, 720, 420));
                var icon = Background.FromTexture2D(Texture2D.whiteTexture);
                Action<VisualElement> page = p => Controls.Info(p, "Body", () => "b", o => o.Id = "cr-body");
                var one = new NavItem("One", page) { Icon = icon };
                var nav = new Nav(w.Body, new[] { NavItem.Heading(null, one, new NavItem("Two", page) { Icon = icon }), NavItem.Heading(null, new NavItem("Three", page) { Icon = icon }) }, 180f);
                yield return null; yield return null;
                var railScroll = w.Body.Q<ScrollView>(); var rail = railScroll.contentContainer;
                var sz = th.Sizes;
                Func<VisualElement> body = () => w.Body.Q("cr-body");
                Func<List<Label>> titles = () => rail.Query<Label>().ToList().FindAll(l => l.name != "rail-glyph");
                var entry = rail.Q("One-icon")?.parent;
                float compactW = (entry == null ? 0f : Px(entry.style.paddingLeft)) * 2f + sz.Icon;
                var ms = new double[1];
                Check(Mathf.Abs(railScroll.worldBound.width - 180f) < 1f && !nav.Compact, "a new Nav's rail is its full width", railScroll.worldBound.width.ToString());
                Check(rail.Q("rail-button") == null, "it draws no collapse button unless asked");
                float x0 = body().worldBound.xMin;

                nav.Compact = true;
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, ms, 1000);
                Check(ms[0] >= 0 && compactW > sz.Icon, "Compact: the rail is an entry's padding, its icon and the padding again wide", railScroll.worldBound.width + " vs " + compactW);
                yield return Until(() => titles().TrueForAll(l => l.resolvedStyle.opacity == 0f), ms, 1000);
                Check(ms[0] >= 0 && titles().Count == 3, "...its titles are hidden", titles().Count.ToString());
                Check(rail.Query(name: "rule").ToList().Count == 1, "...the rule between the groups stays");
                var ind = entry?.Q("indicator");
                Check(ind != null && Near(ind.resolvedStyle.backgroundColor, th[Role.Accent]) && ind.worldBound.width > 0f, "...and the open entry keeps its marker");
                float x1 = body().worldBound.xMin;
                Check(x1 < x0 - 100f, "the page moves left into the space the rail gave up", x0 + " -> " + x1);

                // Hover: one frame is shorter than the dwell, then it widens over the page.
                var sw = Stopwatch.StartNew();
                MoveTo(s.Root, railScroll.worldBound.center);
                yield return null;
                Check(sw.ElapsedMilliseconds >= 90 || Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, "a pass over the rail shorter than the dwell leaves it narrow", railScroll.worldBound.width.ToString());
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - 180f) < 1f, ms, 1500);
                long widened = sw.ElapsedMilliseconds;
                Check(ms[0] >= 0 && widened >= 90, "hovered past the 0.1 s dwell, the rail widens to its full width", railScroll.worldBound.width + " after " + widened + " ms");
                Check(Mathf.Abs(body().worldBound.xMin - x1) < 0.5f, "...over the page, which does not move", x1 + " -> " + body().worldBound.xMin);
                Check(railScroll.parent.IndexOf(railScroll) == railScroll.parent.childCount - 1, "...drawn above it");
                yield return Until(() => titles().TrueForAll(l => l.resolvedStyle.opacity == 1f), ms, 1000);
                Check(ms[0] >= 0, "...and its titles fade in");
                MoveTo(s.Root, body().worldBound.center + new Vector2(200f, 0f));
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, ms, 1000);
                Check(ms[0] >= 0, "leaving the rail narrows it again", railScroll.worldBound.width.ToString());

                // The button.
                bool? told = null;
                nav.CompactChanged += v => told = v;
                nav.CompactButton = true;
                yield return null;
                var btn = rail.Q("rail-button");
                Check(btn != null && rail.IndexOf(btn) == 0, "CompactButton draws a button at the top of the rail");
                if (btn != null) ClickEl(btn);
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - 180f) < 1f, ms, 1000);
                Check(!nav.Compact && told == false && ms[0] >= 0, "clicking it expands the rail and says so", nav.Compact + " / " + told);
                yield return Until(() => Mathf.Abs(body().worldBound.xMin - x0) < 0.5f, ms, 1000);   // the width eases out: wait for it to land
                Check(ms[0] >= 0, "...giving the page back its old place", body().worldBound.xMin + " vs " + x0);
                btn = rail.Q("rail-button");
                // On the glyph, which stays inside the strip once the rail narrows: the pointer is still over it.
                if (btn != null) { MoveTo(s.Root, new Vector2(btn.worldBound.xMin + compactW / 2f, btn.worldBound.center.y)); ClickEl(btn); }
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, ms, 1000);
                Check(nav.Compact && told == true && ms[0] >= 0, "clicking it again makes the rail compact and says so", nav.Compact + " / " + told);
                var still = Stopwatch.StartNew();
                while (still.ElapsedMilliseconds < 400) yield return null;
                Check(Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, "...and it does not widen again under the pointer that collapsed it", railScroll.worldBound.width.ToString());
                nav.Rebuild(); yield return null; yield return null;
                Check(nav.Compact && rail.Q("rail-button") != null && titles().TrueForAll(l => l.resolvedStyle.opacity == 0f), "a rebuild keeps the strip and its button");

                nav.CompactButton = false; nav.Compact = false;
                yield return Until(() => Mathf.Abs(body().worldBound.xMin - x0) < 0.5f, ms, 1000);
                Check(ms[0] >= 0 && rail.Q("rail-button") == null && Mathf.Abs(railScroll.worldBound.width - 180f) < 1f, "switching Compact off from the host applies at once, button gone",
                    railScroll.worldBound.width + " / " + body().worldBound.xMin + " vs " + x0);
                yield return Until(() => titles().TrueForAll(l => l.resolvedStyle.opacity == 1f), ms, 1000);
                Check(ms[0] >= 0, "...and the titles are back");
            }
            finally { s.Dispose(); }
        }

        static object CursorField(string name) => typeof(Cursors).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

        /* A host's Cursor hook is asked first on every apply and clear, and again every frame while a kind is
           held; a kind it declines falls back to the drawn cursor. */
        IEnumerator SectionCursorHook()
        {
            var calls = new List<Cursors.Kind>();
            var h = Host.Create("CursorHook", o => { o.Log = m => Main.Log("[cursorhook] " + m); o.Cursor = k => { calls.Add(k); return k != Cursors.Kind.ResizeCorner; }; });
            var s = new Surface(h, "FacetCursorHook", Layer.Window);
            try
            {
                var w = new Window(s.Root, "cursorhook", "Cursor hook", new Rect(60, 60, 480, 320));
                float f = 0.5f;
                Controls.Slider(w.Body, "Slider", Bind.To(() => f, x => f = x), o => o.Id = "ch-slider");
                string t = "text";
                Controls.Text(w.Body, "Text", Bind.To(() => t, x => t = x), o => o.Id = "ch-text");
                yield return null; yield return null;
                var num = w.Body.Q("ch-slider-number");
                var grip = w.Root.hierarchy[w.Root.hierarchy.childCount - 1];
                Func<Cursors.Kind, int> count = k => calls.FindAll(c => c == k).Count;
                calls.Clear();
                MoveTo(s.Root, num.worldBound.center); yield return null;
                Check(calls.Count > 0 && calls[0] == Cursors.Kind.Text, "entering a text field asks the host's hook for Text first", string.Join(",", calls));
                int n = count(Cursors.Kind.Text);
                for (int i = 0; i < 4; i++) yield return null;
                Check(count(Cursors.Kind.Text) >= n + 3, "...and asks again every frame while it is held", n + " -> " + count(Cursors.Kind.Text));
                Check(Equals(CursorField("_drawn"), false), "the hook took it: no drawn cursor is set");
                MoveTo(s.Root, w.Footer.worldBound.center); yield return null;
                Check(calls.Count > 0 && calls[calls.Count - 1] == Cursors.Kind.Default, "leaving clears it through the hook", string.Join(",", calls));
                n = count(Cursors.Kind.Text);
                for (int i = 0; i < 4; i++) yield return null;
                Check(count(Cursors.Kind.Text) == n, "...and the re-apply stops");

                calls.Clear();
                var text = w.Body.Q("ch-text-input");
                if (text != null) { MoveTo(s.Root, text.worldBound.center); yield return null; }
                Check(calls.Count > 0 && calls[0] == Cursors.Kind.Text, "entering a Text row's field asks the hook for Text too", string.Join(",", calls));
                MoveTo(s.Root, w.Footer.worldBound.center); yield return null;

                MoveTo(s.Root, grip.worldBound.center); yield return null;
                Check(count(Cursors.Kind.ResizeCorner) >= 1 && Equals(CursorField("_drawn"), true), "a kind the hook declines falls back to the drawn cursor",
                    count(Cursors.Kind.ResizeCorner) + " / " + CursorField("_drawn"));
                MoveTo(s.Root, w.Footer.worldBound.center); yield return null;
                Check(Equals(CursorField("_drawn"), false), "...which leaving clears");
            }
            finally { s.Dispose(); Cursors.Apply(Cursors.Kind.Default); }
        }

        // How tall a glyph's ink draws, in panel px: an image's rows at least half opaque, scaled to the
        // element, or a text glyph's outline from the host's font.
        static float InkHeight(VisualElement g)
        {
            var tex = g == null ? null : g.style.backgroundImage.value.texture;
            if (tex != null)
            {
                var px = tex.GetPixels();
                int lo = -1, hi = -1;
                for (int y = 0; y < tex.height; y++)
                    for (int x = 0; x < tex.width; x++)
                        if (px[y * tex.width + x].a >= 0.5f) { if (lo < 0) lo = y; hi = y; break; }
                return lo < 0 ? 0f : (hi - lo + 1) * g.resolvedStyle.height / tex.height;
            }
            var l = g as TextElement;
            var fa = l == null || string.IsNullOrEmpty(l.text) ? null : Fonts.Regular(Host.For(l));
            UnityEngine.TextCore.Text.Character ch;
            if (fa == null || !fa.characterLookupTable.TryGetValue(l.text[0], out ch)) return 0f;
            // By reflection: pointSize is an int in the reference assemblies and a float in the game's Unity.
            float pt = Convert.ToSingle(typeof(UnityEngine.TextCore.FaceInfo).GetProperty("pointSize").GetValue(fa.faceInfo));
            return ch.glyph.metrics.height * l.resolvedStyle.fontSize / pt;
        }

        // An image's tint, else the text's colour: what a glyph draws in.
        static Color InkColour(VisualElement g) => g.style.backgroundImage.value.texture != null ? g.resolvedStyle.unityBackgroundImageTintColor : g.resolvedStyle.color;

        /* The rail button's glyph reads as large as a rail icon, sits in the icons' column with the rail open
           or a strip, and is tinted as an entry's icon: muted at rest, lit while the pointer is on it. */
        IEnumerator SectionRailButton()
        {
            var s = new Surface(Host.Create("RailButton", o => o.Log = m => Main.Log("[railbutton] " + m)), "FacetRailButton", Layer.Window);
            try
            {
                var w = new Window(s.Root, "railbutton", "Rail button", new Rect(60, 60, 600, 400));
                var icon = Background.FromTexture2D(Texture2D.whiteTexture);
                Action<VisualElement> page = p => Controls.Info(p, "Body", () => "b", o => o.Id = "rb-body");
                var nav = new Nav(w.Body, new[] { NavItem.Heading(null, new NavItem("Open", page) { Icon = icon }, new NavItem("Closed", page) { Icon = icon }) }, 180f);
                nav.CompactButton = true;
                yield return null; yield return null;
                var rs = w.Body.Q<ScrollView>(); var rail = rs.contentContainer;
                var ic = rail.Q("Closed-icon");
                float strip = (ic == null ? 0f : Px(ic.parent.style.paddingLeft)) * 2f + Theme.For(rail).Sizes.Icon;
                Func<VisualElement> glyph = () => rail.Q("rail-button")?.Q("rail-glyph");
                Color rest = ic == null ? Color.clear : InkColour(ic);
                if (ic != null) { MoveTo(s.Root, ic.parent.worldBound.center); yield return null; }
                Color lit = ic == null ? Color.clear : InkColour(ic);
                var ms = new double[1];
                foreach (bool compact in new[] { false, true })
                {
                    string state = compact ? "compact" : "open";
                    MoveTo(s.Root, w.Body.Q("rb-body").worldBound.center + new Vector2(200f, 0f));
                    nav.Compact = compact;
                    yield return Until(() => Mathf.Abs(rs.worldBound.width - (compact ? strip : 180f)) < 1f, ms, 1000);
                    var g = glyph();
                    Check(g != null && ic != null && Mathf.Abs(g.worldBound.xMin - ic.worldBound.xMin) <= 1f, "the rail button's glyph sits in the icons' column, " + state,
                        g == null || ic == null ? "none" : g.worldBound.xMin + " vs " + ic.worldBound.xMin);
                    float ink = InkHeight(g);
                    Check(ic != null && ink >= ic.resolvedStyle.height - 2f, "...draws at least a rail icon's height less 2 px, " + state,
                        ic == null ? "none" : ink.ToString("0.0") + " vs " + ic.resolvedStyle.height);
                    Check(g != null && lit != rest && Near(InkColour(g), rest), "...is muted at rest as a closed entry's icon, " + state, g == null ? "none" : InkColour(g) + " vs " + rest);
                    if (g != null) { MoveTo(s.Root, g.parent.worldBound.center); yield return null; }
                    Check(g != null && Near(InkColour(g), lit), "...and lit as a hovered entry's icon while the pointer is on it, " + state, g == null ? "none" : InkColour(g) + " vs " + lit);
                }
            }
            finally { s.Dispose(); }
        }

        // `type` at a panel position on `root`'s panel (MoveTo's down and up, for a drag).
        static void PanelPointer(VisualElement root, EventType type, Vector2 at)
        {
            var ev = new Event { type = type, button = 0, clickCount = 1, mousePosition = at };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(ev)
                        : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev)
                        : (EventBase)PointerMoveEvent.GetPooled(ev);
            using (e) root.panel.visualTree.SendEvent(e);
        }

        // Down at `from`, `dx` along in six moves a frame apart, then up.
        static IEnumerator DragBy(VisualElement root, Vector2 from, float dx)
        {
            PanelPointer(root, EventType.MouseDown, from); yield return null;
            for (int i = 1; i <= 6; i++) { PanelPointer(root, EventType.MouseDrag, from + new Vector2(dx * i / 6f, 0f)); yield return null; }
            PanelPointer(root, EventType.MouseUp, from + new Vector2(dx, 0f)); yield return null; yield return null;
        }

        /* Collapsible false is for a host whose own setting decides whether the rail shows: the handle's drag
           stops at the minimum width instead of shutting it, ToggleRail leaves it open, a shut rail opens, and
           the width still drags. */
        IEnumerator SectionRailCollapsible()
        {
            var s = new Surface(Host.Create("Collapsible", o => o.Log = m => Main.Log("[collapsible] " + m)), "FacetCollapsible", Layer.Window);
            try
            {
                var w = new Window(s.Root, "collapsible", "Collapsible", new Rect(60, 60, 720, 420));
                Action<VisualElement> page = p => Controls.Info(p, "Body", () => "b");
                var nav = new Nav(w.Body, new[] { new NavItem("One", page), new NavItem("Two", page) }, 200f);
                yield return null; yield return null;
                var rs = w.Body.Q<ScrollView>();
                Func<Vector2> handle = () => new Vector2(rs.worldBound.xMax + 3f, rs.worldBound.center.y);
                var ms = new double[1];
                Check(nav.Collapsible, "a Nav's rail is collapsible by default");
                yield return DragBy(s.Root, handle(), -400f);
                Check(!nav.RailOpen, "...so dragging its handle far left shuts it");

                nav.Collapsible = false;
                yield return Until(() => rs.resolvedStyle.display == DisplayStyle.Flex && rs.worldBound.width >= Nav.MinRail - 1f, ms, 1000);
                Check(nav.RailOpen && ms[0] >= 0, "Collapsible false opens a shut rail", nav.RailOpen + " / " + rs.worldBound.width);
                yield return DragBy(s.Root, handle(), -400f);
                Check(nav.RailOpen && Mathf.Abs(rs.worldBound.width - Nav.MinRail) < 1f, "...dragging its handle far left stops at the minimum width",
                    nav.RailOpen + " / " + rs.worldBound.width);
                yield return DragBy(s.Root, handle(), 60f);
                float wide = rs.worldBound.width;
                Check(nav.RailOpen && Mathf.Abs(wide - (Nav.MinRail + 60f)) < 1f, "...dragging it right still widens it", wide.ToString());
                nav.ToggleRail(); yield return null; yield return null;
                Check(nav.RailOpen && Mathf.Abs(rs.worldBound.width - wide) < 1f, "...and ToggleRail leaves it open", nav.RailOpen + " / " + rs.worldBound.width);

                nav.Collapsible = true; nav.ToggleRail();
                yield return Until(() => rs.resolvedStyle.display == DisplayStyle.None, ms, 1000);
                Check(!nav.RailOpen && ms[0] >= 0, "Collapsible true again: ToggleRail shuts it");
            }
            finally { s.Dispose(); }
        }
    }
}
