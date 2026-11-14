using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;
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
           narrows it. CompactButton puts a toggle in the window's title bar that flips Compact and says so. */
        IEnumerator SectionCompactRail()
        {
            var th = new Theme();
            th.Looks.Indicator = new Look { SelectedBg = Role.Accent };
            th.Looks.Rule = new Look { Bg = Role.Line };
            var s = new Surface(Host.Create("Compact", o => { o.Log = m => Main.Log("[compact] " + m); o.Theme = th; }), "FacetCompact", Layer.Window);
            try
            {
                var w = new Window(s.Root, "compact", "Compact rail", new Rect(60, 60, 720, 420));
                w.ShowSearch("Search", _ => { });   // the title must stay clear of it with the toggle in front
                var icon = Background.FromTexture2D(Texture2D.whiteTexture);
                Action<VisualElement> page = p => Controls.Info(p, "Body", () => "b", o => o.Id = "cr-body");
                var one = new NavItem("One", page) { Icon = icon };
                var nav = new Nav(w.Body, new[] { NavItem.Heading(null, one, new NavItem("Two", page) { Icon = icon }), NavItem.Heading(null, new NavItem("Three", page) { Icon = icon }) }, 180f);
                nav.InsetChanged += w.SetContentInset;   // as a host does: the title block follows the rail
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

                // The button: in the window's title bar, left of the title, not in the rail.
                bool? told = null;
                nav.CompactChanged += v => told = v;
                nav.CompactButton = true;
                yield return null;
                var btn = w.Root.Q("sidebar-toggle");
                var title = w.TitleBarLeft.Q<Label>();
                Check(btn != null && btn.parent == w.TitleBarLeft && title != null && btn.worldBound.xMax <= title.worldBound.xMin && rail.Q("rail-button") == null,
                    "CompactButton puts sidebar-toggle in the title bar left of the title, and no rail-button in the rail",
                    btn == null ? "none" : btn.worldBound + " / " + (title == null ? "no title" : title.worldBound.ToString()) + " / rail " + (rail.Q("rail-button") != null));
                Func<Texture2D> tex = () => btn == null || btn.childCount == 0 ? null : btn[0].resolvedStyle.backgroundImage.texture;
                var tex0 = tex();
                float aspect = btn == null || btn.childCount == 0 ? 0f : DrawnAspect(btn[0]);
                Check(tex0 != null && tex0.width * 3 == tex0.height * 4 && Mathf.Abs(aspect - 4f / 3f) < 0.02f, "...drawing a 4:3 sidebar glyph, unstretched", tex0 == null ? "none" : tex0.width + "x" + tex0.height + " drawn " + aspect.ToString("0.00"));
                var bar = w.TitleBarLeft.parent;
                var searchBox = bar[bar.IndexOf(w.TitleBarLeft) + 1];
                Check(title != null && searchBox.resolvedStyle.display == DisplayStyle.Flex && title.worldBound.xMax <= searchBox.worldBound.xMin,
                    "...and with the rail compact the title still ends before the search box", title == null ? "no title" : title.worldBound.xMax + " vs " + searchBox.worldBound.xMin);
                Check(btn != null && btn.tooltip == "Expand sidebar", "...its tip says what it does while compact", btn?.tooltip);
                if (btn != null) ClickEl(btn);
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - 180f) < 1f, ms, 1000);
                Check(!nav.Compact && told == false && ms[0] >= 0, "clicking it expands the rail and says so", nav.Compact + " / " + told);
                yield return Until(() => Mathf.Abs(body().worldBound.xMin - x0) < 0.5f, ms, 1000);   // the width eases out: wait for it to land
                Check(ms[0] >= 0, "...giving the page back its old place", body().worldBound.xMin + " vs " + x0);
                Check(btn != null && btn.tooltip == "Collapse sidebar", "...and its tip turns to collapsing", btn?.tooltip);
                if (btn != null) { MoveTo(s.Root, btn.worldBound.center); ClickEl(btn); }
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, ms, 1000);
                Check(nav.Compact && told == true && ms[0] >= 0, "clicking it again makes the rail compact and says so", nav.Compact + " / " + told);
                Check(tex() == tex0, "the glyph is the same texture after both toggles");
                MoveTo(s.Root, railScroll.worldBound.center);
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - 180f) < 1f, ms, 1500);
                Check(ms[0] >= 0, "the strip it made compact widens on the first hover after it", railScroll.worldBound.width.ToString());
                MoveTo(s.Root, body().worldBound.center + new Vector2(200f, 0f));
                yield return Until(() => Mathf.Abs(railScroll.worldBound.width - compactW) < 1f, ms, 1000);
                long flips, sets, junk;
                /* Warm-up four times the run, then a frame: UI Toolkit's per-frame change lists grow once to hold the
                   flips, with room left for whatever else changes in the measured frame. */
                using (Motion.Hold(0f, false)) for (int i = 0; i < 4000; i++) { nav.Flip(); nav.Compact = !nav.Compact; }
                yield return null;
                SidebarToggleProbe(nav, out flips, out sets, out junk);
                Main.Log("[perf] sidebar toggle: heap grew " + flips + " bytes over 1000 flips, " + sets + " over 1000 host Compact sets (2000 x 128-byte arrays grew it " + junk + ")");
                Check(junk > 0 && flips <= sets + 8192, "a flip by the toggle allocates nothing beyond the host setting Compact (heap probe, 8 KB slack)", flips + " vs " + sets + " B, junk " + junk);
                nav.Rebuild(); yield return null; yield return null;
                Check(nav.Compact && w.Root.Q("sidebar-toggle") == btn && rail.Q("rail-button") == null && titles().TrueForAll(l => l.resolvedStyle.opacity == 0f), "a rebuild keeps the strip and its toggle");

                // A second Nav in the same window keeps its button in its own rail; the toggle stays the first one's.
                var box = Ui.Box(w.Body);
                var nav2 = new Nav(box, new[] { NavItem.Heading(null, new NavItem("Four", page) { Icon = icon }) }, 120f);
                nav2.CompactButton = true;
                yield return null;
                Check(box.Q("rail-button") != null && w.Root.Query(name: "sidebar-toggle").ToList().Count == 1, "a second Nav in the window keeps its rail-button; the title bar holds one toggle");
                box.RemoveFromHierarchy();

                nav.CompactButton = false; nav.Compact = false;
                yield return Until(() => Mathf.Abs(body().worldBound.xMin - x0) < 0.5f, ms, 1000);
                Check(ms[0] >= 0 && btn != null && btn.resolvedStyle.display == DisplayStyle.None && Mathf.Abs(railScroll.worldBound.width - 180f) < 1f, "switching Compact off from the host applies at once, toggle hidden",
                    railScroll.worldBound.width + " / " + body().worldBound.xMin + " vs " + x0);
                yield return Until(() => titles().TrueForAll(l => l.resolvedStyle.opacity == 1f), ms, 1000);
                Check(ms[0] >= 0, "...and the titles are back");

                // A host that clears the body and builds its Nav again: the new Nav takes the window's one toggle.
                bool? told3 = null;
                bool oldCompact = nav.Compact;
                w.Body.Clear();
                var nav3 = new Nav(w.Body, new[] { NavItem.Heading(null, new NavItem("Five", page) { Icon = icon }) }, 180f);
                nav3.CompactChanged += v => told3 = v;
                yield return null;
                var toggles = w.Root.Query(name: "sidebar-toggle").ToList();
                Check(toggles.Count == 1 && toggles[0].resolvedStyle.display == DisplayStyle.None, "a Nav built again in the cleared body finds the window's one toggle, hidden until it asks for it",
                    toggles.Count + (toggles.Count > 0 ? " / " + toggles[0].resolvedStyle.display : ""));
                nav3.CompactButton = true;
                yield return null;
                var rail3 = w.Body.Q<ScrollView>().contentContainer;
                Check(rail3.Q("rail-button") == null && w.Root.Query(name: "sidebar-toggle").ToList().Count == 1 && toggles.Count == 1 && toggles[0].resolvedStyle.display == DisplayStyle.Flex,
                    "...shows it once it does, and draws no rail-button", "rail-button " + (rail3.Q("rail-button") != null));
                if (toggles.Count > 0) ClickEl(toggles[0]);
                yield return null;
                Check(nav3.Compact && told3 == true && nav.Compact == oldCompact, "clicking it flips the new Nav, not the one it replaced", nav3.Compact + " / " + told3 + " / old " + nav.Compact);
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

        /* Heap growth over 1000 flips by the toggle's path (Compact applied, CompactChanged and InsetChanged raised, glyph
           and tip redrawn), and over 1000 host sets of Compact, each the least of three runs with the collector paused:
           another thread's allocation can land in one run, a flip's lands in every one. Motion is held at 0, since a
           running animation allocates by design; UI Toolkit's own width writes allocate in both, so the flip is
           measured against the set. `junk` is 2000 128-byte arrays. Even counts leave the Nav as it was. */
        static void SidebarToggleProbe(Nav nav, out long flips, out long sets, out long junk)
        {
            object keep = null;
            var mode = GarbageCollector.GCMode;
            using (Motion.Hold(0f, false))
            {
                try
                {
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    long a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) keep = new byte[128];
                    junk = GC.GetTotalMemory(false) - a;
                    flips = sets = long.MaxValue;
                    for (int run = 0; run < 3; run++)
                    {
                        a = GC.GetTotalMemory(false);
                        for (int i = 0; i < 1000; i++) nav.Flip();
                        flips = Math.Min(flips, GC.GetTotalMemory(false) - a);
                        a = GC.GetTotalMemory(false);
                        for (int i = 0; i < 1000; i++) nav.Compact = !nav.Compact;
                        sets = Math.Min(sets, GC.GetTotalMemory(false) - a);
                    }
                }
                finally { GarbageCollector.GCMode = mode; }
            }
            GC.KeepAlive(keep);
        }

        // Width over height of an image's background as drawn: Contain or Cover keep the texture's shape, anything else fills the box.
        static float DrawnAspect(VisualElement im)
        {
            var t = im.resolvedStyle.backgroundImage.texture;
            var k = im.resolvedStyle.backgroundSize.sizeType;
            if (t != null && (k == BackgroundSizeType.Contain || k == BackgroundSizeType.Cover)) return (float)t.width / t.height;
            return im.resolvedStyle.height > 0f ? im.resolvedStyle.width / im.resolvedStyle.height : 0f;
        }

        /* The rail button's glyph reads as large as a rail icon, sits in the icons' column with the rail open
           or a strip, and is tinted as an entry's icon: muted at rest, lit while the pointer is on it. */
        IEnumerator SectionRailButton()
        {
            var s = new Surface(Host.Create("RailButton", o => o.Log = m => Main.Log("[railbutton] " + m)), "FacetRailButton", Layer.Window);
            try
            {
                // A Nav outside any Window: its button stays at the top of its rail.
                var w = Ui.Box(s.Root);
                w.style.position = Position.Absolute; w.style.left = 60f; w.style.top = 60f; w.style.width = 600f; w.style.height = 400f;
                var icon = Background.FromTexture2D(Texture2D.whiteTexture);
                Action<VisualElement> page = p => Controls.Info(p, "Body", () => "b", o => o.Id = "rb-body");
                var nav = new Nav(w, new[] { NavItem.Heading(null, new NavItem("Open", page) { Icon = icon }, new NavItem("Closed", page) { Icon = icon }) }, 180f);
                nav.CompactButton = true;
                yield return null; yield return null;
                var rs = w.Q<ScrollView>(); var rail = rs.contentContainer;
                Check(rail.Q("rail-button") != null && rail.IndexOf(rail.Q("rail-button")) == 0 && s.Root.Q("sidebar-toggle") == null, "a Nav outside a Window keeps its rail-button at the top of the rail");
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
                    MoveTo(s.Root, w.Q("rb-body").worldBound.center + new Vector2(200f, 0f));
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

                // Made compact by its button, the strip does not widen again under the pointer that pressed it.
                nav.Compact = false;
                yield return Until(() => Mathf.Abs(rs.worldBound.width - 180f) < 1f, ms, 1000);
                var rb = rail.Q("rail-button");
                // On the glyph, which stays inside the strip once the rail narrows.
                if (rb != null) { MoveTo(s.Root, glyph().worldBound.center); yield return null; ClickEl(rb); }
                yield return Until(() => Mathf.Abs(rs.worldBound.width - strip) < 1f, ms, 1000);
                Check(rb != null && nav.Compact && ms[0] >= 0, "clicking the rail-button makes the rail compact", rs.worldBound.width.ToString());
                var still = Stopwatch.StartNew();
                while (still.ElapsedMilliseconds < 400) yield return null;
                Check(Mathf.Abs(rs.worldBound.width - strip) < 1f, "...and it does not widen again under the pointer that collapsed it", rs.worldBound.width.ToString());
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
