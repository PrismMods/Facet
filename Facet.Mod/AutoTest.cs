using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    // Scripted in-game test: synthetic UI Toolkit events against the real panel. Each task appends
    // one Section...() iterator to Start and logs through Check.
    sealed partial class AutoTest : MonoBehaviour
    {
        int _pass, _fail;

        internal void Check(bool ok, string what, string detail = "")
        {
            if (ok) _pass++; else _fail++;
            Main.Log((ok ? "PASS " : "FAIL ") + what + (ok || detail == "" ? "" : "  [" + detail + "]"));
        }

        VisualElement Root => Gallery.Surface?.Root;

        float K()
        {
            var r = Root;
            return r == null ? 0f : r.layout.width / Screen.width;
        }

        // via: an element on the panel to send to, when it is not the Gallery's. mods: the keys held (Ctrl, Shift, Alt).
        void Pointer(EventType type, Vector2 screen, int button = 0, VisualElement via = null, EventModifiers mods = EventModifiers.None)
        {
            float k = K();
            PointerAt(type, new Vector2(screen.x * k, (Screen.height - screen.y) * k), via ?? Root, button, mods);
        }

        // The same at a panel position on root's panel, for a panel whose scale is not the Gallery's.
        static void PointerAt(EventType type, Vector2 panel, VisualElement root, int button = 0, EventModifiers mods = EventModifiers.None)
        {
            var ev = new Event { type = type, button = button, clickCount = 1, mousePosition = panel, modifiers = mods };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(ev)
                        : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev)
                        : (EventBase)PointerMoveEvent.GetPooled(ev);
            using (e) root.panel.visualTree.SendEvent(e);
        }

        /* Ends the mouse's pointer capture on root's panel with no release event, as when the capture is lost. The
           panel sends the capture-out before it dispatches the next pointer event, so a move at `panel` follows. */
        static void ReleasePointer(VisualElement root, Vector2 panel)
        {
            var held = root.panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
            if (held != null) held.ReleasePointer(PointerId.mousePointerId);
            PointerAt(EventType.MouseMove, panel, root);
        }

        IEnumerator Drag(Vector2 from, Vector2 to, int steps = 6, VisualElement via = null, int button = 0)
        {
            Pointer(EventType.MouseDown, from, button, via); yield return null;
            for (int i = 1; i <= steps; i++) { Pointer(EventType.MouseDrag, Vector2.Lerp(from, to, i / (float)steps), button, via); yield return null; }
            Pointer(EventType.MouseUp, to, button, via); yield return null; yield return null;
        }

        // Down and up in the same frame: a real pointer's own move event between them would turn
        // a still click into a drag.
        IEnumerator Click(Vector2 at, int button = 0, VisualElement via = null)
        {
            Pointer(EventType.MouseDown, at, button, via);
            Pointer(EventType.MouseUp, at, button, via); yield return null; yield return null;
        }

        void Submit(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        }

        void Type(TextField f, string text)
        {
            f.SetValueWithoutNotify(text);
            Key(f, KeyCode.Return);
        }

        void Key(VisualElement target, KeyCode code)
        {
            char c = code == KeyCode.Return ? '\n' : '\0';
            using (var ke = KeyDownEvent.GetPooled(c, code, EventModifiers.None)) { ke.target = target; target.SendEvent(ke); }
        }

        // Centre of an element in screen pixels, the inverse of Pointer's mapping.
        Vector2 Ctr(VisualElement e)
        {
            float k = K();
            var c = e.worldBound.center;
            return new Vector2(c.x / k, Screen.height - c.y / k);
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 5; i++) yield return null;
            Main.Log("AUTOTEST start screen " + Screen.width + "x" + Screen.height);
            // Other mods write the shared speed and smooth-scroll switch every frame: the run reads its own.
            using (Motion.Hold(1f, false))
            {
                yield return Run("SectionWarmUp", SectionWarmUp());   // first: it needs the session's first surface
                yield return Run("SectionOpen", SectionOpen());
                yield return Run("SectionPartsPerf", SectionPartsPerf());   // before any section warms the parts
                yield return Run("SharedEscape", SharedEscape());
                yield return Run("SectionHost", SectionHost());
                yield return Run("SectionTheme", SectionTheme());
                yield return Run("SectionNoStale", SectionNoStale());
                yield return Run("SectionScrollClear", SectionScrollClear());
                yield return Run("SectionWords", SectionWords());
                yield return Run("SectionCjk", SectionCjk());
                yield return Run("SectionBind", SectionBind());
                yield return Run("SectionRebuild", SectionRebuild());
                yield return Run("SectionRefreshIsolation", SectionRefreshIsolation());
                yield return Run("SectionValues", SectionValues());
                yield return Run("SectionStructure", SectionStructure());
                yield return Run("SectionInput", SectionInput());
                yield return Run("SectionNavIndex", SectionNavIndex());
                yield return Run("SectionForm", SectionForm());
                yield return Run("SectionSpeed", SectionSpeed());
                yield return Run("SectionWheel", SectionWheel());
                yield return Run("SectionThemeShape", SectionThemeShape());
                yield return Run("SectionSkin", SectionSkin());
                yield return Run("SectionSliderBox", SectionSliderBox());
                yield return Run("SectionCards", SectionCards());
                yield return Run("SectionHeaderColumns", SectionHeaderColumns());
                yield return Run("SectionPreview", SectionPreview());
                yield return Run("SectionTiles", SectionTiles());
                yield return Run("SectionTabs", SectionTabs());
                yield return Run("SectionSidebar", SectionSidebar());
                yield return Run("SectionSwitchSnap", SectionSwitchSnap());
                yield return Run("SectionRailIcon", SectionRailIcon());
                yield return Run("SectionRowEdges", SectionRowEdges());
                yield return Run("SectionArrowSlot", SectionArrowSlot());
                yield return Run("SectionButtonRows", SectionButtonRows());
                yield return Run("SectionScaleClamp", SectionScaleClamp());
                yield return Run("SectionHistorySteps", SectionHistorySteps());
                yield return Run("SectionFieldClip", SectionFieldClip());
                yield return Run("SectionThumbRadius", SectionThumbRadius());
                yield return Run("SectionTitleGlyphs", SectionTitleGlyphs());
                yield return Run("SectionRowPad", SectionRowPad());
                yield return Run("SectionTabGap", SectionTabGap());
                yield return Run("SectionCompactRail", SectionCompactRail());
                yield return Run("SectionCursorHook", SectionCursorHook());
                yield return Run("SectionWheelSpeed", SectionWheelSpeed());
                yield return Run("SectionRailButton", SectionRailButton());
                yield return Run("SectionRailCollapsible", SectionRailCollapsible());
                yield return Run("SectionSmoothScroll", SectionSmoothScroll());
                yield return Run("SectionCanvasEdit", SectionCanvasEdit());
                yield return Run("SectionCanvasView", SectionCanvasView());
                yield return Run("SectionCanvasPerf", SectionCanvasPerf());
                yield return Run("SectionToolbar", SectionToolbar());
                yield return Run("SectionMenus", SectionMenus());
                yield return Run("SectionContextMenu", SectionContextMenu());
                yield return Run("SectionMenuKeys", SectionMenuKeys());
                yield return Run("SectionClose", SectionClose());
                // Later tasks: yield return Run("Section...", Section...());
            }

            Main.Log("AUTOTEST DONE pass=" + _pass + " fail=" + _fail);
            yield return null;
            Application.Quit();
        }

        // Drives a section by hand so a throw is logged to gallery.log and the run carries on.
        IEnumerator Run(string name, IEnumerator section)
        {
            // Hover and focus fades are real transitions now, so a section that reads a resolved colour
            // right after an event would catch it mid-fade: all but the motion-aware ones run at speed 0.
            // Wheel steps land at once; the glide's section holds its own switch.
            float speed = name == "SectionWarmUp" || name == "SectionOpen" || name == "SectionPartsPerf" || name == "SectionSpeed" || name == "SectionClose" || name == "SectionSmoothScroll" ? 1f : 0f;
            using (Motion.Hold(speed, false))
                while (true)
                {
                    try { if (!section.MoveNext()) yield break; }
                    catch (Exception ex)
                    {
                        var st = (ex.StackTrace ?? "").Split('\n')[0].Trim();
                        Check(false, name + " threw", ex.Message + " @ " + st);
                        yield break;
                    }
                    yield return section.Current;
                }
        }


        // Two consumers, one broken page: only the owner's log hears of it, and the drag of a window
        // reaches its host's Write.
        IEnumerator SectionHost()
        {
            var la = new List<string>(); var lb = new List<string>();
            var a = Host.Create("A", o => o.Log = la.Add);
            var b = Host.Create("B", o => o.Log = lb.Add);
            var sa = new Surface(a, "FacetHostA", Layer.Window);
            var sb = new Surface(b, "FacetHostB", Layer.Window);
            var wa = new Window(sa.Root, "hosta", "Host A", new Rect(60, 60, 400, 300));
            var wb = new Window(sb.Root, "hostb", "Host B", new Rect(500, 60, 400, 300));
            new Nav(wa.Body, new[] { new NavItem("Boom", p => throw new InvalidOperationException("boom")) { Owner = "BoomMod" } });
            new Nav(wb.Body, new[] { new NavItem("Fine", p => Controls.Info(p, "Fine", () => "ok")) });
            yield return null; yield return null;
            var ea = la.FindAll(m => m.StartsWith("[A] "));
            Check(ea.Count == 1 && ea[0].Contains("Boom"), "A's list has one guard entry naming the page", string.Join(" | ", ea));
            Check(!lb.Exists(m => m.StartsWith("[B] ") || m.Contains("Boom")), "B's list stays empty of A's failure");
            Check(wa.Body.Query<Label>().ToList().Exists(l => l.text.Contains("failed to build")), "A's content shows the failed-to-build text");
            Check(wa.Body.Query<Label>().ToList().Exists(l => l.text.Contains("failed to build") && l.text.Contains("BoomMod")), "...naming the page's owner");
            Check(!wb.Body.Query<Label>().ToList().Exists(l => l.text.Contains("failed to build")), "B's content builds");
            Check(ReferenceEquals(Host.For(wa.Body), a) && ReferenceEquals(Host.For(wb.Body), b), "Host.For finds each element's own host");
            var nb = new Nav(wb.Body, new[] { new NavItem("Fine2", p => { }) });
            nb.OnInsetChanged = _ => throw new InvalidOperationException("inset");
            bool threw = false;
            try { nb.ToggleRail(); } catch { threw = true; }
            Check(!threw && lb.FindAll(m => m.StartsWith("[B] ") && m.Contains("OnInsetChanged")).Count == 1, "a throwing OnInsetChanged is guarded and logged once to its host", string.Join(" | ", lb));
            sa.Dispose(); sb.Dispose();

            var w = Gallery.Open(); yield return null; yield return null;
            string before; Gallery.Store.TryGetValue("window.Gallery", out before);
            var bar = w.Root.hierarchy[0];
            var from = Ctr(bar);
            yield return Drag(from, from + new Vector2(60, -40));
            string after; Gallery.Store.TryGetValue("window.Gallery", out after);
            Check(after != null && after.Split(',').Length == 5 && after != before, "dragging the title bar writes x,y,w,h,scale to the host", before + " -> " + after);
        }

        static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 2e-3f && Mathf.Abs(a.g - b.g) < 2e-3f && Mathf.Abs(a.b - b.b) < 2e-3f && Mathf.Abs(a.a - b.a) < 2e-3f;
        }

        // Paint's private per-element hover flag.
        static bool PaintHover(VisualElement e)
        {
            var state = typeof(Paint).GetNestedType("State", BindingFlags.NonPublic);
            var s = typeof(Data).GetMethod("Find").MakeGenericMethod(state).Invoke(null, new object[] { e });
            return s != null && (bool)state.GetField("Hover").GetValue(s);
        }

        static int Subscribers(Theme t)
        {
            var d = typeof(Theme).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(t) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        // Recolouring a theme re-themes its open window in place: chrome, hover, disabled fade.
        IEnumerator SectionTheme()
        {
            var w = Gallery.Open(); yield return null; yield return null;
            var t = Gallery.Surface.Theme;   // Gallery's host sets no theme: this is the host's own copy of Dark
            var dark = new Color[Enum.GetValues(typeof(Role)).Length];
            for (int i = 0; i < dark.Length; i++) dark[i] = Theme.Dark[(Role)i];
            // Body paints no role (the window's background is Root's Panel), so it must stay as it was.
            var body = w.Body.resolvedStyle.backgroundColor;
            var panel = w.Root.resolvedStyle.backgroundColor;
            var sw = Stopwatch.StartNew();
            t.Recolour(new Color(.9f, .2f, .2f));
            double ms = sw.Elapsed.TotalMilliseconds;   // encloses the surface's own [perf] retheme timing
            yield return null;
            var bar = w.Root.hierarchy[0];
            Check(Near(bar.resolvedStyle.backgroundColor, t[Role.TitleBar]), "the title bar re-themes to the new TitleBar",
                bar.resolvedStyle.backgroundColor + " vs " + t[Role.TitleBar]);
            Check(Near(w.Root.resolvedStyle.backgroundColor, t[Role.Panel]) && !Near(panel, t[Role.Panel]), "the window background moves to the new Panel",
                panel + " -> " + w.Root.resolvedStyle.backgroundColor);
            Check(Near(w.Body.resolvedStyle.backgroundColor, body), "the body, which paints no role, is left alone");
            Check(ms <= 16.0, "re-theming the open Gallery takes <= 16 ms", ms.ToString("0.0") + " ms");
            bool same = !ReferenceEquals(t, Theme.Dark);
            for (int i = 0; i < dark.Length; i++) same &= Theme.Dark[(Role)i] == dark[i];
            Check(same, "recolouring a theme-less host's surface leaves Theme.Dark's roles unchanged");
            bool threw = false, threwSet = false;
            try { Theme.Dark.Recolour(Color.green); } catch (InvalidOperationException) { threw = true; }
            try { Theme.Dark[Role.Accent] = Color.green; } catch (InvalidOperationException) { threwSet = true; }
            Check(threw && threwSet && Theme.Dark[Role.Accent] == dark[(int)Role.Accent], "Theme.Dark refuses Recolour and role writes");

            var bs = w.TitleBarRight.Query<Button>().ToList();
            var close = bs[bs.Count - 1];
            using (var e = MouseEnterEvent.GetPooled()) { e.target = close; close.SendEvent(e); }
            yield return null;
            Check(Near(close.resolvedStyle.backgroundColor, t[Role.Danger]), "the hovered close button shows Danger",
                close.resolvedStyle.backgroundColor + " vs " + t[Role.Danger]);
            using (var e = MouseLeaveEvent.GetPooled()) { e.target = close; close.SendEvent(e); }
            yield return null;
            Check(close.resolvedStyle.backgroundColor.a == 0f, "...and is bare again after the pointer leaves");

            var b = Ui.Btn("Probe", null, w.Body);
            yield return null; yield return null;
            // Read in the same frame: the real pointer is elsewhere, so the panel sends a leave next frame.
            Pointer(EventType.MouseDown, Ctr(b));
            var down = b.style.backgroundColor.value;
            Pointer(EventType.MouseUp, Ctr(b)); yield return null;
            Check(Near(down, t[Role.Row]), "a pressed button shows Look.Button's PressedBg", down + " vs " + t[Role.Row]);
            Paint.Enable(b, false); yield return null;
            Check(Mathf.Abs(b.resolvedStyle.opacity - .35f) < 1e-3f, "a button after Paint.Enable(b, false) has opacity .35", b.resolvedStyle.opacity.ToString());
            Paint.Enable(b, true); yield return null;
            Check(b.resolvedStyle.opacity == 1f && b.enabledSelf, "...and is whole again after Paint.Enable(b, true)");
            b.RemoveFromHierarchy();

            // Look presets are copies: restyling the one a mod was handed changes no one else's.
            var pb = Ui.Btn("Preset", null, w.Body);
            yield return null;
            var mine = Look.Button; mine.Bg = Role.Accent;
            bool fresh = Look.Button.Bg == Role.RowAlt;
            // pb sits where the probe above was pressed, so the panel may have hovered it (HoverBg wins over Bg).
            // Read its resting background: leave, then redraw and read in this frame.
            bool hovered = PaintHover(pb);
            using (var e = MouseLeaveEvent.GetPooled()) { e.target = pb; pb.SendEvent(e); }
            Paint.Apply(w.Root);
            Check(fresh && Near(pb.style.backgroundColor.value, t[Role.RowAlt]), "mutating the Look.Button you were handed changes neither the next Look.Button nor an existing button",
                "next " + Look.Button.Bg + ", button " + pb.style.backgroundColor.value + ", hovered before leave " + hovered);
            mine.Bg = Role.RowAlt;   // were it shared, put it back for the sections after this one
            pb.RemoveFromHierarchy();

            // A look with only HoverText still lights on hover.
            var ht = new Label("Hover text"); w.Body.Add(ht);
            Paint.Look(ht, new Look { Text = Role.TextMuted, HoverText = Role.Text });
            yield return null;
            using (var e = MouseEnterEvent.GetPooled()) { e.target = ht; ht.SendEvent(e); }
            yield return null;
            Check(Near(ht.style.color.value, t[Role.Text]), "a Look with only HoverText shows it on hover", ht.style.color.value + " vs " + t[Role.Text]);
            ht.RemoveFromHierarchy();

            // Paint.Forget: a literal colour set afterwards survives a re-theme and a hover.
            var fl = Ui.Text("Forgotten", w.Body); var fb = Ui.Btn("Forgotten", null, w.Body);
            yield return null;
            Paint.Forget(fl); Paint.Forget(fb);
            fl.style.color = Color.red; fb.style.backgroundColor = Color.red;
            t.Recolour(new Color(.9f, .2f, .2f));
            using (var e = MouseEnterEvent.GetPooled()) { e.target = fb; fb.SendEvent(e); }
            yield return null;
            Check(fl.style.color.value == Color.red && fb.style.backgroundColor.value == Color.red, "after Paint.Forget, a literal colour survives a re-theme and a hover",
                fl.style.color.value + " / " + fb.style.backgroundColor.value);
            using (var e = MouseLeaveEvent.GetPooled()) { e.target = fb; fb.SendEvent(e); }
            fl.RemoveFromHierarchy(); fb.RemoveFromHierarchy();

            // A throwing subscriber does not stop the surfaces after it; replacing and disposing unsubscribe.
            var tt = new Theme();
            tt.Changed += () => { throw new InvalidOperationException("subscriber"); };
            var h = Host.Create("ThemeSubs", o => o.Theme = tt);
            var s = new Surface(h, "FacetThemeSubs", Layer.Window);
            var card = Ui.Card(s.Root);
            Check(Subscribers(tt) == 2, "a surface subscribes to its host's theme", Subscribers(tt).ToString());
            tt.Recolour(new Color(.2f, .8f, .3f));
            Check(Near(card.style.backgroundColor.value, tt[Role.Panel]), "a throwing Changed subscriber leaves the surface re-themed");
            var other = Theme.FromAccent(new Color(.3f, .3f, 1f));
            s.Theme = other;
            Check(Subscribers(tt) == 1 && Subscribers(other) == 1 && Near(card.style.backgroundColor.value, other[Role.Panel]),
                "replacing a surface's theme moves its subscription and redraws in the new one");
            s.Dispose();
            Check(Subscribers(other) == 0, "disposing a surface unsubscribes it");
        }

        static bool IsRole(Color c, Theme t)
        {
            if (c.a < 1e-3f) return true;   // transparent is no colour at all
            foreach (Role r in Enum.GetValues(typeof(Role))) if (Near(c, t[r])) return true;
            return false;
        }

        // Every inline background, text, border and tint colour under e must be one of t's roles.
        // A picker's swatches (under the element named facet-picker) may hold a literal background or tint.
        static void Stale(VisualElement e, Theme t, List<string> bad, bool picker)
        {
            picker |= e.name == "facet-picker";
            var s = e.style;
            string id = e.GetType().Name + "#" + e.name + (e is TextElement te ? " '" + te.text + "'" : "");
            Action<string, StyleColor> chk = (what, sc) =>
            {
                if (sc.keyword != StyleKeyword.Undefined || IsRole(sc.value, t)) return;
                bad.Add(id + " " + what + " " + sc.value);
            };
            if (!picker) chk("background", s.backgroundColor);
            chk("text", s.color);
            chk("borderL", s.borderLeftColor); chk("borderR", s.borderRightColor);
            chk("borderT", s.borderTopColor); chk("borderB", s.borderBottomColor);
            if (!picker) chk("tint", s.unityBackgroundImageTintColor);
            var h = e.hierarchy;
            for (int i = 0; i < h.childCount; i++) Stale(h[i], t, bad, picker);
        }

        // Open every page, search, flash a hit, open a popup, recolour to red, and walk the whole tree:
        // no widget may keep a colour from the old scheme.
        IEnumerator SectionNoStale()
        {
            // No wait for the warm-up window: its Nav has its own index, so it cannot wipe this one.
            Check(Enum.GetValues(typeof(Role)).Length == 16, "IsRole checks all 16 roles (Rail, Line, Off and Guide appended)");
            var t = new Theme();
            var h = Host.Create("NoStale", o => { o.Log = m => Main.Log("[nostale] " + m); o.Theme = t; });
            var s = new Surface(h, "FacetNoStale", Layer.Window);
            var w = new Window(s.Root, "nostale", "No stale", new Rect(40, 40, 900, 700));
            w.ShowSearch("Search", null);
            var stops = new List<GradientStop> { new GradientStop { Position = 0f, Colour = Color.red }, new GradientStop { Position = 1f, Colour = Color.blue } };
            Button anchor = null;
            var pages = new[] {
                new NavItem("Controls", p =>
                {
                    Controls.Toggle(p, "Toggle", Bind.Value(true));
                    Controls.Slider(p, "Slider", Bind.Value(1f), o => o.Max = 2f);
                    Controls.Choice(p, "Choice", Bind.Value(1), o => o.Items = new[] { "One", "Two" });
                    Controls.Text(p, "Text", Bind.Value("x"));
                    Controls.Danger(p, "Danger", "Wipe", () => { });
                    Controls.DangerButton(p, "Reset all", () => { });
                    Controls.Colour(p, "Colour", Bind.Value(Color.cyan), o => o.Alpha = true);
                    new GradientEditor(p, "Gradient", stops, () => { });
                    var item = Controls.Item(p, "Item", "a note"); Controls.MarkActive(item);
                    new Tabs(p, i => { }).Rebuild(new[] { "A", "B" });
                    Controls.ToggleSubPage(p, "Sub toggle", Bind.Value(false), q => Controls.Info(q, "Inner", () => "x"));
                    var sec = Controls.Section(p, "Section"); Controls.Info(sec, "Info", () => "v");
                    anchor = Ui.Btn("Anchor", null, p);
                }),
                NavItem.Heading("Group", new NavItem("Other", p => Controls.Action(p, "Other action", "Go", () => { }))),
            };
            var nav = new Nav(w.Body, pages);
            yield return null; yield return null;
            nav.Go(pages[1].Children[0]); yield return null; yield return null;
            nav.Go(pages[0]); yield return null; yield return null;

            nav.Filter("Toggle"); yield return null;
            var hit = w.Body.Query<Label>().ToList().Find(l => l.text == "Toggle");
            Check(hit != null, "searching finds the Toggle row");
            if (hit != null) using (var e = ClickEvent.GetPooled()) { e.target = hit; hit.SendEvent(e); }
            yield return new WaitForSeconds(1.2f);   // the hit's flash settles

            int before = Subscribers(t);
            var menu = Popup.Menu(anchor, new[] { "One", "Two" }, 1, i => { });
            yield return null; yield return null;
            Check(menu != null && Subscribers(t) == before + 1, "an open popup follows its anchor's theme", before + " -> " + Subscribers(t));

            t.Recolour(new Color(.9f, .1f, .1f));
            yield return null; yield return null;
            var bad = new List<string>();
            Stale(s.Root, t, bad, false);
            if (menu != null) Stale(menu.Content, t, bad, false);
            Check(bad.Count == 0, "after recolouring, no element keeps an old colour", bad.Count + ": " + string.Join(" | ", bad.GetRange(0, Math.Min(6, bad.Count))));
            if (menu != null) Check(Near(menu.Content.resolvedStyle.backgroundColor, t[Role.Panel]), "the open popup card is re-themed");

            // Rail: the selected leaf is lit, the others show muted text and full text on hover.
            Label Find(string text) { return w.Body.Query<Label>().ToList().Find(l => l.text == text); }
            var on = Find("Controls"); var off = Find("Other");
            Check(on != null && Near(on.parent.resolvedStyle.backgroundColor, t[Role.RowHover]) && Near(on.resolvedStyle.color, t[Role.Text]),
                "the selected rail leaf shows RowHover and full text");
            Check(off != null && Near(off.resolvedStyle.color, t[Role.TextMuted]), "an unselected rail leaf shows muted text");
            if (off != null)
            {
                var row = off.parent;
                using (var e = MouseEnterEvent.GetPooled()) { e.target = row; row.SendEvent(e); }
                yield return null;
                Check(Near(off.resolvedStyle.color, t[Role.Text]) && Near(row.resolvedStyle.backgroundColor, t[Role.Row]), "hovering a rail leaf shows full text on Row");
                using (var e = MouseLeaveEvent.GetPooled()) { e.target = row; row.SendEvent(e); }
                yield return null;
                Check(Near(off.resolvedStyle.color, t[Role.TextMuted]) && row.resolvedStyle.backgroundColor.a == 0f, "...and muted text again after it leaves");
            }

            if (menu != null) menu.Close();
            Check(Subscribers(t) == before, "closing a popup stops it listening", Subscribers(t) + " vs " + before);
            s.Dispose();
        }

        // Skin.Scroll no longer writes Color.clear: every part but the dragger must still resolve transparent.
        IEnumerator SectionScrollClear()
        {
            var t = new Theme();
            var h = Host.Create("ScrollClear", o => { o.Log = m => Main.Log("[scrollclear] " + m); o.Theme = t; });
            var s = new Surface(h, "FacetScrollClear", Layer.Window);
            var sv = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            sv.style.width = 300f; sv.style.height = 100f;
            Skin.Scroll(sv);
            s.Root.Add(sv);
            for (int i = 0; i < 20; i++) { var l = new Label("row " + i); l.style.width = 500f; sv.Add(l); }
            yield return null; yield return null; yield return null;
            var bad = new List<string>();
            Action<string, VisualElement> clear = (n, e) =>
            {
                if (e == null) bad.Add(n + " missing");
                else if (e.resolvedStyle.backgroundColor.a != 0f) bad.Add(n + " " + e.resolvedStyle.backgroundColor);
            };
            clear("view", sv); clear("viewport", sv.contentViewport);
            foreach (var sc in new[] { sv.verticalScroller, sv.horizontalScroller })
            {
                clear("scroller", sc);
                var sl = sc?.slider;
                clear("tracker", sl?.Q(className: "unity-base-slider__tracker"));
                var d = sl?.Q(className: "unity-base-slider__dragger");
                if (d == null || !Near(d.resolvedStyle.backgroundColor, t[Role.RowHover])) bad.Add("dragger " + d?.resolvedStyle.backgroundColor);
            }
            Check(bad.Count == 0, "a skinned ScrollView's parts resolve transparent, the dragger RowHover", string.Join(" | ", bad));
            s.Dispose();
        }

        IEnumerator SectionWords()
        {
            Gallery.Lang = "en";
            Gallery.Open(); yield return null; yield return null;
            Gallery.Surface.Relabel();
            Label size = null;
            foreach (var l in Root.Query<Label>().ToList()) if (l.text == "Size") size = l;
            Check(size != null, "the Size row's label reads Size in English");
            Gallery.Lang = "ko";
            Gallery.Surface.Relabel();
            yield return null; yield return null; yield return null;
            string korean = "\uD06C\uAE30";
            Check(size != null && size.text == korean, "after Relabel the same Label instance reads \uD06C\uAE30 with no rebuild", size?.text);
            Check(size != null && size.layout.width > 0f, "...and the Korean label laid out wide", size?.layout.width.ToString());
            var face = Fonts.Regular(Gallery.Host);
            Check(Fonts.HasHangul(face), "the face the label draws with (with its fallbacks) has the glyphs \uD06C and \uAE30", face?.name);
            var used = Root.style.unityFontDefinition.value.fontAsset;
            Check(used != null && Fonts.HasHangul(used), "the font set on the panel root has Hangul", used?.name);
            var boldFace = Fonts.BoldFace(face);
            Check(boldFace != null && Fonts.HasHangul(boldFace), "the weight-700 face (titles, headers) also has the glyphs", boldFace?.name ?? "no bold face");
            Label title = null;
            foreach (var l in Root.Query<Label>().ToList()) if (l.text == "Facet Gallery") title = l;
            Check(title != null && title.style.unityFontStyleAndWeight.value == FontStyle.Bold, "the window title is the bold label that face serves");
            var late = Ui.Text("Size");   // no parent, no Host.Use scope: the Fallback host (English) translates it
            Check(late.text == "Size", "a parentless label starts untranslated");
            Root.Add(late);
            yield return null; yield return null;
            Check(late.text == korean, "...and is re-translated by the Gallery host once it joins the panel", late.text);
            late.RemoveFromHierarchy();
            Check(Words.Own("Sure?", Gallery.Host) == "\uC815\uB9D0\uC694?", "Facet's own words follow the host language through Own");
            Gallery.Lang = "en";
            Gallery.Surface.Relabel();
            Check(size != null && size.text == "Size", "switching back relabels to English again", size?.text);
            Gallery.Close();
        }

        // Frames until `ok` holds, or `cap` ms pass; the ms it took, -1 if it never did.
        IEnumerator Until(Func<bool> ok, double[] ms, int cap = 1000)
        {
            var sw = Stopwatch.StartNew();
            ms[0] = -1;
            while (sw.ElapsedMilliseconds < cap)
            {
                yield return null;
                if (ok()) { ms[0] = sw.Elapsed.TotalMilliseconds; yield break; }
            }
        }

        static string Ms(double[] ms) { return ms[0].ToString("0") + " ms"; }

        // Bound controls follow their values in place, leave a field being typed in alone, and stop
        // reading while hidden or once their getter keeps failing.
        IEnumerator SectionBind()
        {
            Gallery.Model = new Model();
            Gallery.Open(); yield return null; yield return null;
            var t = Gallery.Surface.Theme;
            var row = Root.Q("flag"); var input = Root.Q("flag-input"); var dot = row?.Q("changed");
            Check(row != null && input != null && dot != null, "the bound toggle's row and input are named from its Id, with a changed dot");
            if (row == null || input == null || dot == null) yield break;
            Func<bool> on = () => Near(input.style.backgroundColor.value, t[Role.Accent]);
            Func<bool> dotShown = () => dot.style.display.value == DisplayStyle.Flex;
            Check(!on() && !dotShown(), "the toggle starts off, with no dot");

            var ms = new double[1];
            Gallery.Model.Flag = true;
            yield return Until(on, ms);
            Check(ms[0] >= 0 && ms[0] <= 150, "Model.Flag = true behind the UI shows as on within 150 ms", Ms(ms));
            Check(dotShown(), "...and the changed dot shows while it differs from Default");

            // Review Focus 1: a profile switch replaces the settings object wholesale.
            Gallery.Model = new Model { Flag = false };
            yield return Until(() => !on(), ms);
            Check(ms[0] >= 0 && ms[0] <= 150, "with Gallery.Model replaced by one whose Flag = false, the toggle shows off within 150 ms", Ms(ms));

            // Undo and redo re-read every visible surface at once, not at the next tick.
            History.Record("Toggle", "bind-test", () => Gallery.Model.Flag = false, () => Gallery.Model.Flag = true);
            History.Undo(); History.Redo();
            Check(on(), "a redo shows its value in the same frame (undo and redo refresh the visible surfaces)");
            var consumers = History.AfterApply;
            History.AfterApply = () => { };   // a consumer assigns rather than adds, as PrismLib's SettingsPanel does
            History.Undo();
            Check(!on(), "with History.AfterApply assigned by a consumer, an undo still shows the old value in the same frame");
            History.Redo();
            History.AfterApply = consumers;
            History.Clear();
            Check(dotShown(), "the dot shows again with Flag on");
            yield return Click(Ctr(row), 2);
            Check(!Gallery.Model.Flag && !dotShown() && !on(), "a middle-click on the row with Default = false sets Model.Flag false and hides the dot",
                "flag=" + Gallery.Model.Flag + " dot=" + dotShown());

            // Probes sit at the top of the page, so a click on them lands inside the window.
            var probes = Ui.Box(); row.parent.Insert(0, probes);

            var bad = Controls.Toggle(probes, "Throwing probe", Bind.To<bool>(() => throw new InvalidOperationException("probe getter"), v => { }));
            yield return new WaitForSeconds(0.45f);
            Check(bad.Unavailable, "a getter that throws makes Unavailable true after 3 ticks");
            int lines = 0;
            foreach (var l in File.ReadAllLines(Main.LogPath)) if (l.Contains("[Gallery] ") && l.Contains("Throwing probe")) lines++;
            Check(lines == 1, "gallery.log holds exactly one [Gallery] line for it", lines + " lines");
            Check(bad.Root.tooltip == "unavailable, see the log" && Mathf.Abs(bad.Root.style.opacity.value - .35f) < 1e-3f,
                "...and the row is dimmed, its tooltip saying why", bad.Root.tooltip + " / " + bad.Root.style.opacity.value);

            var name = Root.Q<TextField>("name-input");
            Check(name != null, "the bound Name field is named name-input");
            if (name != null)
            {
                name.Focus(); yield return null;
                var fe = name.focusController?.focusedElement as VisualElement;
                Check(fe != null && (fe == name || name.Contains(fe)), "the Name field takes focus", fe?.GetType().Name);
                name.SetValueWithoutNotify("typed");
                Gallery.Model.Name = "behind";
                yield return new WaitForSeconds(0.35f);
                Check(name.value == "typed", "while the Name field holds focus, a write to Model.Name behind the UI leaves the typed text", name.value);
                (name.focusController?.focusedElement as VisualElement)?.Blur();
                yield return null;
                Check(Gallery.Model.Name == "typed", "...and leaving the field commits what was typed", Gallery.Model.Name);
            }

            int reads = 0;
            Controls.Toggle(probes, "Counting probe", Bind.To(() => { reads++; return false; }, v => { }));
            yield return new WaitForSeconds(0.25f);
            Check(reads > 1, "a visible surface keeps reading its controls", reads + " reads");
            Gallery.Surface.Visible = false; reads = 0;
            for (int i = 0; i < 30; i++) yield return null;
            Check(reads == 0, "with the Gallery hidden for 30 frames the refresher reads nothing", reads + " reads");
            Gallery.Surface.Visible = true;
            Check(reads > 0, "...and showing it again reads at once", reads + " reads");
            yield return null; yield return null;

            int acts = 0;
            var menu = new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>("Reset all", () => acts++) };
            var withMenu = Controls.Toggle(probes, "Menu probe", Bind.Value(false), o => o.ContextMenu = menu);
            yield return null; yield return null;
            yield return Click(Ctr(withMenu.Root), 1);
            Label item = null;
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.rootVisualElement != null)
                    foreach (var l in d.rootVisualElement.Query<Label>().ToList()) if (l.text == "Reset all" && l.panel != null) item = l;
            Check(item != null, "a right-click on a row with ContextMenu = { (\"Reset all\", act) } opens a menu naming it");
            if (item != null)
            {
                var pick = item.parent;
                using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { e.target = pick; pick.SendEvent(e); }
                yield return null;
            }
            Check(acts == 1, "picking the item runs act once", acts + " runs");
            probes.RemoveFromHierarchy();
        }

        static object RefresherOf(Surface s)
        {
            return typeof(Surface).GetField("_refresher", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
        }

        static int Bound(object refresher)
        {
            return refresher == null ? -1 : (int)refresher.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(refresher);
        }

        // Review Focus 2: a button that rebuilds the page it sits on. Nothing throws, the old page's
        // controls leave the refresher and the new page's are the ones read.
        IEnumerator SectionRebuild()
        {
            Gallery.Open(); yield return null;   // Pointer maps screen to panel through the Gallery's root
            var logs = new List<string>();
            var h = Host.Create("BindNav", o => o.Log = m => { logs.Add(m); Main.Log("[bindnav] " + m); });
            var s = new Surface(h, "FacetBindNav", Layer.Window);
            var w = new Window(s.Root, "bindnav", "Bind nav", new Rect(100, 60, 600, 600));
            bool value = false;
            Nav nav = null;
            var page = new NavItem("Bound", p =>
            {
                Ui.Btn("Rebuild", () => nav.Refresh(), p);
                for (int i = 0; i < 100; i++) { int n = i; Controls.Toggle(p, "Bound " + n, Bind.To(() => value, v => value = v), o => o.Id = "bound" + n); }
            });
            nav = new Nav(w.Body, new[] { page });
            yield return null; yield return null;
            var r = RefresherOf(s);
            Check(Bound(r) == 100, "the open page's 100 bound controls register with their surface's refresher", Bound(r).ToString());
            // The budgets: one refresher tick (the mean of 20, a frame apart) and a re-theme of this page.
            double sum = 0;
            for (int i = 0; i < 20; i++)
            {
                var tick = Stopwatch.StartNew();
                s.Refresh();
                sum += tick.Elapsed.TotalMilliseconds;
                yield return null;
            }
            Main.Log("[perf] refresh " + (sum / 20).ToString("0.000") + " ms (mean of 20 ticks, 100 bound toggles)");
            Check(sum / 20 <= 0.2, "a refresh pass over 100 bound toggles takes <= 0.2 ms (mean of 20)", (sum / 20).ToString("0.000") + " ms");
            var sw = Stopwatch.StartNew();
            s.Theme.Recolour(new Color(.9f, .2f, .2f));
            double rt = sw.Elapsed.TotalMilliseconds;
            Main.Log("[perf] retheme " + rt.ToString("0.0") + " ms (100 bound toggles)");
            Check(rt <= 16.0, "re-theming the page of 100 bound toggles takes <= 16 ms", rt.ToString("0.0") + " ms");

            var b = w.Body.Query<Button>().ToList().Find(x => x.text == "Rebuild");
            Check(b != null, "the page has its Rebuild button");
            if (b == null) { s.Dispose(); yield break; }
            bool threw = false;
            try { Pointer(EventType.MouseDown, Ctr(b), 0, b); Pointer(EventType.MouseUp, Ctr(b), 0, b); }
            catch (Exception ex) { threw = true; Main.Log("rebuild click threw " + ex); }
            yield return null; yield return null;
            var errs = logs.FindAll(m => m.StartsWith("[BindNav] "));
            Check(!threw && errs.Count == 0 && b.panel == null, "a button that rebuilds its own page raises nothing, and its page is replaced",
                "threw=" + threw + " attached=" + (b.panel != null) + " " + string.Join(" | ", errs));
            Check(Bound(r) == 100, "afterwards the refresher holds only the new page's 100 controls", Bound(r).ToString());

            var first = w.Body.Q("bound0-input");
            value = true;
            var ms = new double[1];
            yield return Until(() => first != null && Near(first.style.backgroundColor.value, s.Theme[Role.Accent]), ms);
            Check(ms[0] >= 0, "the new page's controls are the ones read", Ms(ms));

            threw = false;
            try { s.Dispose(); } catch { threw = true; }
            yield return null; yield return null;
            Check(!threw && Bound(r) == 0, "disposing the surface stops its refresher", Bound(r).ToString());
        }

        // A value type whose Equals throws: the refresher's comparison is a call into consumer code.
        sealed class Unequal
        {
            public override bool Equals(object o) { throw new InvalidOperationException("Equals"); }
            public override int GetHashCode() { return 0; }
        }

        // Ruling R15: a control that throws while refreshing leaves the rows after it refreshing.
        IEnumerator SectionRefreshIsolation()
        {
            Gallery.Open(); yield return null;
            var logs = new List<string>();
            var h = Host.Create("Isolate", o => o.Log = m => { logs.Add(m); Main.Log("[isolate] " + m); });
            var s = new Surface(h, "FacetIsolate", Layer.Window);
            var w = new Window(s.Root, "isolate", "Isolate", new Rect(120, 120, 500, 300));
            var right = Controls.Row(w.Body, "Unequal");
            // Control's constructor is internal; a test reaches it as Task 10's Controls will.
            typeof(Control<Unequal>).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0]
                .Invoke(new object[] { right.parent, right, "Unequal", Bind.To(() => new Unequal(), v => { }), null, null });
            bool flag = false;
            var after = Controls.Toggle(w.Body, "After it", Bind.To(() => flag, v => flag = v));
            yield return null; yield return null;
            flag = true;
            var ms = new double[1];
            yield return Until(() => Near(after.Input.style.backgroundColor.value, s.Theme[Role.Accent]), ms);
            Check(ms[0] >= 0 && ms[0] <= 150, "with a control whose value's Equals throws ahead of it, a toggle still shows a change behind the UI within 150 ms", Ms(ms));
            var errs = logs.FindAll(m => m.StartsWith("[Isolate] Refresh 'Unequal'"));
            Check(errs.Count == 1, "...and the throw is logged once, under the throwing control's name", string.Join(" | ", logs.FindAll(m => m.StartsWith("[Isolate] "))));
            s.Dispose();
        }

        // Structure rows: a persisted fold, a group that follows the value, and pages behind rows.
        IEnumerator SectionStructure()
        {
            var store = new Dictionary<string, string>();
            var h = Host.Create("Structure", o => { o.Log = m => Main.Log("[structure] " + m); o.Read = k => store.TryGetValue(k, out var v) ? v : null; o.Write = (k, v) => store[k] = v; });
            var surf = new Surface(h, "FacetStructure", Layer.Window);
            var w = new Window(surf.Root, "structure", "Structure", new Rect(60, 60, 560, 520));
            bool group = false, sub = false;
            Nav nav = null;
            nav = new Nav(w.Body, new[] { new NavItem("Home", p =>
            {
                var sec = Controls.Section(p, "Fold me"); Controls.Info(sec, "Inside", () => "v");
                var body = Controls.ToggleGroup(p, "Group", Bind.To(() => group, v => group = v), o => o.Id = "grp");
                Controls.Info(body, "Under group", () => "v");
                Controls.SubPage(p, "More", q => Controls.Info(q, "Deeper", () => "x"));
                Controls.ToggleSubPage(p, "Sub toggle", Bind.To(() => sub, v => sub = v), q => Controls.Info(q, "Deeper2", () => "x"), o => o.Id = "tsp");
            }) });
            yield return null; yield return null;

            Func<string, Label> lab = t => w.Body.Query<Label>().ToList().Find(l => l.text == t);
            var head = lab("Fold me")?.parent;
            Check(head != null, "the section has a head row");
            if (head == null) { surf.Dispose(); yield break; }
            VisualElement SecBody() { var hd = lab("Fold me").parent; return hd.parent[hd.parent.IndexOf(hd) + 1]; }
            Check(SecBody().style.display != DisplayStyle.None, "the section starts open");
            ClickEl(head); yield return null; yield return null;
            string fold; store.TryGetValue("fold.Home/Fold me", out fold);
            Check(fold == "0", "folding a section writes fold.Home/Fold me=0 to the host", fold);
            nav.Refresh(); yield return null; yield return null;
            Check(SecBody().style.display == DisplayStyle.None, "a rebuilt page shows the section folded");

            var gb = w.Body.Q("grp").parent[w.Body.Q("grp").parent.IndexOf(w.Body.Q("grp")) + 1];
            Check(gb.style.display == DisplayStyle.None, "the group's body starts hidden while the value is off");
            group = true;
            var ms = new double[1];
            yield return Until(() => gb.style.display == DisplayStyle.Flex, ms);
            Check(ms[0] >= 0 && ms[0] <= 150, "a behind-the-UI change shows the group's body within 150 ms", Ms(ms));
            group = false;
            yield return Until(() => gb.style.display == DisplayStyle.None, ms);
            Check(ms[0] >= 0 && ms[0] <= 150, "...and hides it again within 150 ms", Ms(ms));

            ClickEl(lab("More").parent); yield return null; yield return null;
            Check(lab("\u2039  Home") != null && lab("Deeper") != null, "clicking a SubPage row pushes a page whose back row names Home");
            ClickEl(lab("\u2039  Home").parent); yield return null; yield return null;

            ClickEl(w.Body.Q("tsp-input")); yield return null; yield return null;
            Check(sub && lab("\u2039  Home") == null && lab("Sub toggle") != null, "clicking the switch in a ToggleSubPage toggles without navigating");
            ClickEl(lab("Sub toggle").parent); yield return null; yield return null;
            Check(lab("Deeper2") != null, "clicking the rest of the row opens its page");
            surf.Dispose();

            // A host with no Read/Write: the fold still survives a rebuild.
            var h2 = Host.Create("StructureBare");
            var s2 = new Surface(h2, "FacetStructureBare", Layer.Window);
            var w2 = new Window(s2.Root, "structurebare", "Bare", new Rect(60, 60, 400, 300));
            var nav2 = new Nav(w2.Body, new[] { new NavItem("Home", p => { var b = Controls.Section(p, "Bare fold"); Controls.Info(b, "In", () => "v"); }) });
            yield return null; yield return null;
            Func<VisualElement> bare = () => { var hd = w2.Body.Query<Label>().ToList().Find(l => l.text == "Bare fold").parent; return hd.parent[hd.parent.IndexOf(hd) + 1]; };
            ClickEl(w2.Body.Query<Label>().ToList().Find(l => l.text == "Bare fold").parent); yield return null;
            nav2.Refresh(); yield return null; yield return null;
            Check(bare().style.display == DisplayStyle.None, "with a host that has no Read/Write, a folded section stays folded after a rebuild");
            s2.Dispose();
        }

        // Task 15: Form builds bound controls from Field data; bad getters give unavailable rows.
        IEnumerator SectionForm()
        {
            var logs = new List<string>();
            var ha = Host.Create("FormA", o => { o.Log = m => logs.Add(m); o.Translate = s => s == "Greeting" ? "안녕" : null; });
            var hp = Host.Create("FormP", o => { o.Log = m => logs.Add(m); o.Translate = s => s == "Hello" ? "PARENT" : null; });
            var dup1 = Host.Create("FormDup");
            var dupLogs = new List<string>();
            var dup2 = Host.Create("FormDup", o => o.Log = dupLogs.Add);
            Check(Host.Named("FormDup") == dup1 && dup2 != dup1, "a second host named FormDup does not replace the first in Host.Named");
            Check(dupLogs.Count == 1, "...and logs one line through the new host", dupLogs.Count.ToString());
            var facetLogs = new List<string>();
            var hf = Host.Create("Facet", o => o.Log = facetLogs.Add);
            Check(Host.Named("Facet") == Host.Fallback && hf != Host.Fallback && facetLogs.Count == 1, "a consumer host named Facet is not registered; Named(\"Facet\") stays Fallback", facetLogs.Count.ToString());

            var surf = new Surface(hp, "FacetForm", Layer.Window);
            var w = new Window(surf.Root, "form", "Form", new Rect(60, 60, 600, 700));
            bool b = false; int i = 3; float f = 0.5f; string t = "hi", key = "F1", col = "#336699"; object colObj = null, col8 = null;
            var sc = new List<object>(); var sd = new List<object>(); var sn = new List<object>();
            bool acted = false; object bsv = null, b1v = null; float fu = 2f;
            var fields = new[] {
                new Field { Group = "G", Label = "fb", Kind = FieldKind.Bool, Get = () => b, Set = v => b = (bool)v },
                new Field { Group = "G", Label = "fi", Kind = FieldKind.Int, Min = 0, Max = 10, Get = () => i, Set = v => i = (int)v },
                new Field { Group = "G", Label = "ff", Kind = FieldKind.Float, Min = 0, Max = 1, Get = () => f, Set = v => f = (float)v },
                new Field { Group = "G", Label = "fc", Kind = FieldKind.Choice, Options = new[] { "a", "b" }, Get = () => 1, Set = v => sc.Add(v) },
                new Field { Group = "G", Label = "ft", Kind = FieldKind.Text, Get = () => t, Set = v => t = (string)v },
                new Field { Group = "G", Label = "fa", Kind = FieldKind.Action, ActionLabel = "Go", Set = v => acted = true },
                new Field { Group = "G", Label = "fcol", Kind = FieldKind.Colour, Get = () => col, Set = v => col = (string)v },
                new Field { Group = "G", Label = "fcolc", Kind = FieldKind.Colour, Get = () => new Color(0.2f, 0.4f, 0.6f, 1f), Set = v => colObj = v },
                new Field { Group = "G", Label = "fcol8", Kind = FieldKind.Colour, Get = () => "#33669980", Set = v => col8 = v },
                new Field { Group = "G", Label = "fk", Kind = FieldKind.Key, Get = () => key, Set = v => key = (string)v, Capture = cb => cb("F2") },
                new Field { Group = "G", Label = "fin", Kind = FieldKind.Info, Get = () => "info" },
                new Field { Group = "H", Label = "fd", Kind = FieldKind.Dropdown, Options = new[] { "x", "y" }, Get = () => 0, Set = v => sd.Add(v) },
                new Field { Group = "H", Label = "fn", Kind = FieldKind.Number, Min = 0, Max = 5, Get = () => 2f, Set = v => sn.Add(v) },
                new Field { Group = "H", Label = "fu", Kind = FieldKind.Float, Get = () => fu, Set = v => fu = (float)v },
                new Field { Group = "H", Label = "badf", Kind = FieldKind.Float, Get = () => null },
                new Field { Group = "H", Label = "badi", Kind = FieldKind.Int, Get = () => "x" },
                new Field { Group = "H", Label = "badc", Kind = FieldKind.Colour, Get = () => "nope" },
                new Field { Group = "H", Label = "after", Kind = FieldKind.Bool, Get = () => true, Set = v => { } },
                new Field { Group = "H", Label = "oorc", Kind = FieldKind.Choice, Options = new[] { "a", "b" }, Get = () => 9, Set = v => { } },
                new Field { Group = "H", Label = "oord", Kind = FieldKind.Dropdown, Options = new[] { "x", "y" }, Get = () => -1, Set = v => { } },
                new Field { Group = "H", Label = "fl", Kind = FieldKind.Int, Min = 0, Max = 10, Get = () => 2.7f, Set = v => { } },
                new Field { Group = "H", Label = "lg", Kind = FieldKind.Int, Min = 0, Max = 10, Get = () => 4L, Set = v => { } },
                new Field { Group = "H", Label = "bs", Kind = FieldKind.Bool, Get = () => "true", Set = v => bsv = v },
                new Field { Group = "H", Label = "b1", Kind = FieldKind.Bool, Get = () => 1, Set = v => b1v = v },
                new Field { Owner = "FormA", Group = "H", Label = "Greeting", Kind = FieldKind.Bool, Get = () => true, Set = v => { } },
                new Field { Owner = "NoSuchHost", Group = "H", Label = "Hello", Kind = FieldKind.Bool, Get = () => true, Set = v => { } },
            };
            var page = Ui.Box(w.Body);
            bool threw = false;
            try { Form.Build(page, fields); } catch (Exception e) { threw = true; Main.Log("form threw " + e); }
            Check(!threw, "Form.Build returns normally with bad getters");
            yield return null; yield return null; yield return null;
            foreach (var n in new[] { "fb", "fi", "ff", "fc", "ft", "fa", "fcol", "fcolc", "fcol8", "fk", "fin", "fd", "fn", "after", "oorc", "oord", "fl", "lg", "bs", "b1" })
                Check(page.Q(n) != null, "a " + n + " row is built, found by its label");
            var labels = page.Query<Label>().ToList();
            Check(labels.Exists(l => l.text == "G") && labels.Exists(l => l.text == "H"), "consecutive groups become two sections");
            Func<string, TextField> tfIn = n => page.Q(n)?.Q<TextField>();
            Check(page.Q<SliderInt>("fi-input")?.value == 3, "an Int field shows its value on an IntSlider", page.Q<SliderInt>("fi-input")?.value.ToString());
            Check(page.Q<SliderInt>("fl-input")?.value == 3, "an Int field converts a float getter (2.7 -> 3)");
            Check(page.Q<SliderInt>("lg-input")?.value == 4, "an Int field converts a long getter");
            Check(page.Q<TextField>("ft-input")?.value == "hi", "a Text field shows its value");
            Check(tfIn("fn")?.value == "2", "a Number field shows its value", tfIn("fn")?.value);
            Check(tfIn("ff")?.value == "0.5", "a Float field shows its value", tfIn("ff")?.value);
            Check(page.Q<Button>("fk-input")?.text == "F1", "a Key field shows its key");
            Check(page.Q<Button>("fd-input")?.text == "x", "a Dropdown shows its item");
            Check(page.Q<Button>("oord-input")?.text == "", "a Dropdown index out of range shows blank and still builds");
            Check(page.Q("oorc").enabledSelf && page.Q("oord").enabledSelf, "out-of-range Choice and Dropdown rows are enabled");
            Check(page.Q("fin").Query<Label>().ToList().Exists(l => l.text == "info"), "an Info field shows its text");
            Check(page.Q("fa").Q<Button>()?.text == "Go", "an Action field shows its ActionLabel");

            ClickEl(page.Q("fb-input")); yield return null; yield return null;
            Check(b, "toggling the Bool row writes through Field.Set");
            ClickEl(page.Q("bs-input")); yield return null;
            Check(bsv is bool && !(bool)bsv, "Bool from the string \"true\" is on, so a click writes false");
            ClickEl(page.Q("b1-input")); yield return null;
            Check(b1v is bool && !(bool)b1v, "Bool from 1 is on, so a click writes false");
            Submit(page.Q<Button>("fk-input")); yield return null; yield return null;
            Check(key == "F2", "a Key field captures through Field.Capture", key);
            Submit(page.Q("fa").Q<Button>()); yield return null;
            Check(acted, "an Action field runs Set when pressed");
            Submit(page.Q("fc").Query<Button>().ToList()[0]); yield return null;
            Check(sc.Count == 1 && sc[0] is int && (int)sc[0] == 0, "picking Choice item 0 writes the int 0", sc.Count == 1 ? sc[0].ToString() : sc.Count.ToString());
            Submit(page.Q<Button>("fd-input")); yield return null; yield return null;
            var yl = LiveLabel("y");
            if (yl != null) using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { e.target = yl.parent; yl.parent.SendEvent(e); }
            yield return null; yield return null;
            Check(sd.Count == 1 && sd[0] is int && (int)sd[0] == 1, "picking Dropdown item 1 writes the int 1", sd.Count == 1 ? sd[0].ToString() : sd.Count.ToString());
            if (tfIn("fn") != null) { Type(tfIn("fn"), "3"); yield return null; }
            Check(sn.Count == 1 && sn[0] is float && Mathf.Abs((float)sn[0] - 3f) < 1e-4f, "typing 3 in a Number row writes the float 3", sn.Count == 1 ? sn[0].ToString() : sn.Count.ToString());
            Check(page.Q("fu") != null && page.Q<Slider>("fu-input") == null, "a Float field with Min == Max is a Number row, no slider");
            if (tfIn("fu") != null) { Type(tfIn("fu"), "50"); yield return null; }
            Check(fu == 50f, "...which is not clamped: typing 50 writes 50", fu.ToString());

            // Colour writes back the form it read.
            Action<string, string> hex = (row, text) => { var tf = page.Q(row)?.Q<TextField>(); if (tf != null) Type(tf, text); };
            hex("fcol", "#112233"); hex("fcolc", "#112233"); hex("fcol8", "#11223344");
            yield return null; yield return null;
            Check(col == "#112233", "a string-typed Colour getter gets a string back after an edit", col);
            Check(colObj is Color, "a Color-typed Colour getter gets a Color back after an edit", colObj?.GetType().Name);
            Check(col8 is string && ((string)col8).Length == 9, "an 8-digit string Colour gets an 8-digit string back", col8?.ToString());

            // Unavailable rows.
            Check(page.Q("badf") != null && !page.Q("badf").enabledSelf, "a Float field whose Get returns null is an unavailable row");
            Check(page.Q("badi") != null && !page.Q("badi").enabledSelf, "an Int field whose Get returns \"x\" is an unavailable row");
            Check(page.Q("badc") != null && !page.Q("badc").enabledSelf, "a Colour field whose Get returns an unparseable string is an unavailable row");
            Check(page.Q("after") != null && page.Q("after").enabledSelf, "the rows after the bad ones still build");
            foreach (var bad in new[] { "badf", "badi", "badc" })
            {
                int n = logs.FindAll(m => m.Contains("'" + bad + "'")).Count;
                Check(n == 1, "exactly one log line for failing field " + bad, n + ": " + string.Join(" | ", logs.FindAll(m => m.Contains("'" + bad + "'"))));
            }
            Check(labels.Exists(l => l.text == "안녕"), "a field owned by host FormA translates with FormA's table, not the surface's");
            Check(!labels.Exists(l => l.text == "Greeting"), "...and not untranslated");
            Check(labels.Exists(l => l.text == "PARENT"), "an unregistered owner falls back to the parent's host (its label translated by the parent)");
            surf.Dispose();

            // A foreign-owner field in a Section of a short scrolling window keeps its row height.
            var hs = Host.Create("FormShort", o => o.Log = m => Main.Log("[formshort] " + m));
            var surf2 = new Surface(hs, "FacetFormShort", Layer.Window);
            var w2 = new Window(surf2.Root, "formshort", "Short", new Rect(60, 60, 400, 160));
            var many = new List<Field>();
            for (int k = 0; k < 12; k++) many.Add(new Field { Owner = "FormA", Label = "own" + k, Kind = FieldKind.Bool, Get = () => true, Set = v => { } });
            // A fixed-height parent is where a shrinkable wrapper would squash its row.
            var fixedBox = Ui.Box(w2.Body);
            fixedBox.style.height = 100f;
            Form.Build(fixedBox, many);
            yield return null; yield return null; yield return null;
            float minH = 999f, slack = 999f, overlap = 0f; int built = 0; Rect prev = default(Rect);
            for (int k = 0; k < 12; k++)
            {
                var r = w2.Body.Q("own" + k);
                if (r == null) continue;
                built++; minH = Mathf.Min(minH, r.layout.height);
                slack = Mathf.Min(slack, r.parent.layout.height - r.layout.height);   // a squashed wrapper is shorter than its row
                if (k > 0) overlap = Mathf.Max(overlap, prev.yMax - r.worldBound.yMin);
                prev = r.worldBound;
            }
            Check(built == 12 && minH >= Controls.RowHeight - 0.5f && slack >= -0.5f && overlap <= 0.5f, "foreign-owner rows in a fixed-height parent keep their row height and do not overlap",
                "built=" + built + " minRow=" + minH + " wrapperSlack=" + slack + " overlap=" + overlap);
            surf2.Dispose();
        }

        // Task 14: one lazy index per Nav, reveal through folded sections, owners, last leaf, VisibleWhen.
        IEnumerator SectionNavIndex()
        {
            var logs = new List<string>();
            var h = Host.Create("NavIdx", o => o.Log = m => { logs.Add(m); Main.Log("[navidx] " + m); });
            var s = new Surface(h, "FacetNavIdx", Layer.Window);
            var w = new Window(s.Root, "navidx", "Nav index", new Rect(40, 40, 700, 500));
            var built = new int[7];
            bool shown = false, hideBeta2 = false;
            Func<string, VisualElement> rowOf = id => w.Body.Q(id);
            Func<string, Label> lab = t => w.Body.Query<Label>().ToList().Find(l => l.text == t);
            var pages = new[] {
                new NavItem("Alpha", p =>
                {
                    built[0]++; Controls.Info(p, "Page Alpha", () => "a");
                    Controls.Toggle(p, "Quiet", Bind.Value(false), o => o.Tooltip = "Silences the metronome");
                    Controls.SubPage(p, "Drums", q => { }, o => o.Tooltip = "Cymbal settings");
                }) { Owner = "A" },
                new NavItem("Branch").With(
                    new NavItem("Beta1", p => { built[1]++; Controls.Info(p, "Page Beta1", () => "b"); }) { Owner = "A" },
                    new NavItem("Beta2", p => { built[2]++; Controls.Info(p, "Page Beta2", () => "b"); }) { Owner = "A", VisibleWhen = () => !hideBeta2 },
                    new NavItem("Beta3", p => { built[5]++; Controls.Info(p, "Page Beta3", () => "b"); }) { Owner = "B" }),
                new NavItem("Gamma", p =>
                {
                    built[3]++;
                    var f = Controls.Section(p, "Filler");
                    for (int i = 0; i < 40; i++) Controls.Info(f, "Filler " + i, () => "f");
                    var c = Controls.Section(p, "Colours", false);
                    // A consumer's element whose userData happens to equal the label: not a row, never revealed.
                    var decoy = Ui.Box(c); decoy.userData = "Tint"; decoy.style.height = 600f;
                    Controls.Colour(c, "Tint", Bind.Value(Color.cyan), o => o.Id = "tint");
                    for (int i = 0; i < 10; i++) Controls.Info(c, "After " + i, () => "f");
                }) { Owner = "B" },
                new NavItem("Hidden", p => { built[4]++; Controls.Info(p, "Page Hidden", () => "h"); }) { VisibleWhen = () => shown },
            };
            var nav = new Nav(w.Body, pages);
            yield return null; yield return null;
            Check(built[0] == 1 && built[1] + built[2] + built[3] + built[4] == 0, "constructing a Nav builds only the first page", string.Join(",", built));
            Check(nav.Index == null, "no search index exists before the first query");
            Check(lab("Hidden") == null, "a page whose VisibleWhen is false is not in the rail");

            nav.Filter("tint"); yield return null; yield return null;
            Check(nav.Index != null, "the first non-empty Filter builds the index");
            Check(built[0] >= 2 && built[1] == 1 && built[2] == 1 && built[3] == 1 && built[5] == 1 && built[4] == 0, "the index builds each visible page once and skips hidden ones", string.Join(",", built));
            Check(lab("Tint") != null, "Filter(tint) lists the Tint row");
            nav.Filter("ti"); nav.Filter("tin"); yield return null;
            int perf = logs.FindAll(m => m.StartsWith("[perf] search index ")).Count;
            Check(perf == 1, "the index is built and logged once for several queries", perf.ToString());
            var line = logs.Find(m => m.StartsWith("[perf] search index "));
            var parts = (line ?? "").Split(' ');   // [perf] search index N pages M ms
            Check(parts.Length >= 7 && parts[4] == "pages" && int.Parse(parts[3]) == 5 && int.Parse(parts[5]) <= 100, "index of 5 pages built within 100 ms", line);
            nav.Filter("page hidden"); yield return null;
            Check(lab("Nothing matches.") != null, "a hidden page is not searchable");
            nav.Filter("metronome"); yield return null;
            Check(lab("Quiet") != null, "a word only in a row's tooltip finds the row");
            nav.Filter("cymbal"); yield return null;
            Check(lab("Drums") != null, "...and so does one in a SubPage row's tooltip");
            nav.Go(pages[2]); yield return null; yield return null;
            var tint0 = rowOf("tint");
            Check(tint0 != null && tint0.userData == null, "Facet leaves a row's userData to the consumer", tint0?.userData?.ToString());
            Check(tint0 != null && tint0.parent.resolvedStyle.display == DisplayStyle.None, "the Colours section is folded before the hit is clicked");
            nav.Filter("tint"); yield return null;
            ClickEl(lab("Tint"));

            var ms = new double[1];
            Func<bool> revealed = () =>
            {
                var r = rowOf("tint"); if (r == null) return false;
                var sv = r.GetFirstAncestorOfType<ScrollView>(); if (sv == null) return false;
                var vp = sv.contentViewport.worldBound;
                return r.worldBound.yMin >= vp.yMin - 1f && r.worldBound.yMax <= vp.yMax + 1f && r.worldBound.height > 0f;
            };
            yield return Until(revealed, ms, 2000);
            var tint = rowOf("tint");
            Check(lab("Page Alpha") == null && tint != null, "clicking the hit opens the Gamma page");
            var body = tint == null ? null : tint.parent;
            Check(body != null && body.resolvedStyle.display != DisplayStyle.None, "the folded Colours section was unfolded");
            Check(ms[0] >= 0 && revealed(), "the Tint row ends inside the content viewport", tint == null ? "no row" : tint.worldBound + " vs " + tint.GetFirstAncestorOfType<ScrollView>().contentViewport.worldBound);
            var csv = tint?.GetFirstAncestorOfType<ScrollView>();
            float y = csv == null ? 0f : csv.scrollOffset.y;
            Check(y > 0f, "the page scrolled to the row", y.ToString());
            nav.Rebuild(); yield return null; yield return null; yield return null;
            Check(csv != null && Mathf.Abs(csv.scrollOffset.y - y) < 1f, "Rebuild keeps the scroll offset", y + " -> " + (csv == null ? -1f : csv.scrollOffset.y));

            // Last leaf per branch.
            nav.Go(pages[1].Children[1]); yield return null;
            nav.Go(pages[0]); yield return null; yield return null;
            Check(lab("Page Alpha") != null, "Alpha is open");
            ClickEl(lab("Branch").parent); yield return null; yield return null;
            Check(lab("Page Beta2") != null, "clicking a branch reopens the last leaf opened in it");

            // A remembered leaf that has since hidden is not reopened: the first visible leaf is.
            hideBeta2 = true;
            nav.Go(pages[0]); yield return null; yield return null;
            ClickEl(lab("Branch").parent); yield return null; yield return null;
            Check(lab("Page Beta2") == null && lab("Page Beta1") != null, "a hidden last leaf is skipped for the first visible one");
            hideBeta2 = false;

            // VisibleWhen is re-read by Rebuild.
            shown = true; nav.Rebuild(); yield return null; yield return null;
            Check(lab("Hidden") != null, "Rebuild shows a page whose VisibleWhen turned true");

            // Owners.
            Check(lab("Gamma") != null, "Gamma is in the rail");
            nav.RemoveOwner("B"); yield return null; yield return null;
            Check(lab("Gamma") == null && lab("Alpha") != null, "RemoveOwner(B) drops B's pages from the rail");
            Check(nav.Index == null, "RemoveOwner drops the index");
            Check(pages[1].Children.Count == 3 && pages[2].Owner == "B", "RemoveOwner leaves the caller's NavItems alone");
            var w2 = new Window(s.Root, "navidx2", "Nav index 2", new Rect(760, 40, 400, 400));
            new Nav(w2.Body, pages); yield return null; yield return null;
            Check(w2.Body.Query<Label>().ToList().Find(l => l.text == "Gamma") != null, "the same items in a second Nav still show B's pages");
            w2.Body.Clear();
            nav.Filter("tint"); yield return null;
            Check(lab("Nothing matches.") != null, "...and a rebuilt index no longer finds B's rows");
            s.Dispose();

            // Two rows with one label in two sections: the second hit reveals the second.
            var sd = new Surface(Gallery.Host, "FacetNavIdxDup", Layer.Window);
            var wd = new Window(sd.Root, "navidxdup", "Nav dup", new Rect(40, 40, 500, 400));
            var navd = new Nav(wd.Body, new[] { new NavItem("Dup", p =>
            {
                var a = Controls.Section(p, "First");
                for (int i = 0; i < 30; i++) Controls.Info(a, "Pad " + i, () => "f");
                Controls.Info(a, "Enabled", () => "1", o => o.Id = "en1");
                var b = Controls.Section(p, "Second", false);
                for (int i = 0; i < 30; i++) Controls.Info(b, "Fill " + i, () => "f");
                Controls.Info(b, "Enabled", () => "2", o => o.Id = "en2");
            }) });
            yield return null; yield return null;
            navd.Filter("enabled"); yield return null;
            var hits = wd.Body.Query<Label>().ToList().FindAll(l => l.text == "Enabled");
            Check(hits.Count == 2, "two same-labelled rows give two hits", hits.Count.ToString());
            if (hits.Count == 2)
            {
                ClickEl(hits[1]);
                var msd = new double[1];
                Func<VisualElement> second = () => wd.Body.Q("en2");
                yield return Until(() =>
                {
                    var r = second(); if (r == null || r.layout.height <= 0f) return false;
                    var vp = r.GetFirstAncestorOfType<ScrollView>().contentViewport.worldBound;
                    return r.worldBound.yMin >= vp.yMin - 1f && r.worldBound.yMax <= vp.yMax + 1f;
                }, msd, 2000);
                Check(msd[0] >= 0, "the hit for the second Enabled row reveals the second one");
            }
            sd.Dispose();

            // Korean: a hit matches in both languages, and a jamo query finds it.
            var sk = new Surface(Gallery.Host, "FacetNavIdxKo", Layer.Window);
            var wk = new Window(sk.Root, "navidxko", "Nav index ko", new Rect(40, 40, 500, 400));
            var navk = new Nav(wk.Body, new[] { new NavItem("Colours", p => Controls.Colour(p, "Tint", Bind.Value(Color.cyan))) });
            try
            {
                Gallery.Lang = "ko";
                navk.Filter("\u314C"); yield return null; yield return null;
                Check(wk.Body.Query<Label>().ToList().Find(l => l.text == "\uD2F4\uD2B8") != null, "Filter(jamo) with the host in Korean finds the translated Tint row");
                navk.Filter("tint"); yield return null; yield return null;
                Check(wk.Body.Query<Label>().ToList().Find(l => l.text == "\uD2F4\uD2B8") != null, "the English key still finds the translated row");
            }
            finally { Gallery.Lang = "en"; }
            sk.Dispose();

            // Ruling R22: a Revision page builds once on its first visit; a Revision change rebuilds it in place.
            var sr = new Surface(Gallery.Host, "FacetNavRev", Layer.Window);
            var wr = new Window(sr.Root, "navrev", "Nav revision", new Rect(40, 40, 500, 300));
            int rev = 7, revBuilds = 0;
            var navr = new Nav(wr.Body, new[] { new NavItem("Rev", p => { revBuilds++; for (int i = 0; i < 60; i++) Controls.Info(p, "Line " + i, () => "r"); }) { Revision = () => rev } });
            yield return null; navr.Tick(); yield return null; navr.Tick();
            Check(revBuilds == 1, "a page with a Revision builds once on its first visit", revBuilds + " builds");
            var rsv = wr.Body.Query<Label>().ToList().Find(l => l.text == "Line 0")?.GetFirstAncestorOfType<ScrollView>();
            if (rsv != null)
            {
                rsv.scrollOffset = new Vector2(0f, 300f);
                yield return null; yield return null;
                rev++; navr.Tick();
                yield return null; yield return null; yield return null;
                Check(revBuilds == 2 && Mathf.Abs(rsv.scrollOffset.y - 300f) < 1f, "a Revision change rebuilds the page and keeps its scroll", revBuilds + " builds, y " + rsv.scrollOffset.y);
            }
            else Check(false, "the Revision page has a content ScrollView");
            sr.Dispose();
        }

        static Label LiveLabel(string text)
        {
            Label hit = null;
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.rootVisualElement != null)
                    foreach (var l in d.rootVisualElement.Query<Label>().ToList()) if (l.text == text && l.panel != null) hit = l;
            return hit;
        }

        void ClickEl(VisualElement e)
        {
            using (var ev = ClickEvent.GetPooled()) { ev.target = e; e.SendEvent(ev); }
        }

        // The value rows against the real panel: click, drag, type, menu, undo, and long labels.
        IEnumerator SectionValues()
        {
            Gallery.Model = new Model();
            Gallery.Open(); yield return null; yield return null;
            History.Clear();

            var flag = Root.Q("flag-input");
            ClickEl(flag); yield return null;
            Check(Gallery.Model.Flag, "clicking flag-input turns Model.Flag on");
            History.Undo(); yield return null;
            Check(!Gallery.Model.Flag, "History.Undo() puts Model.Flag back");

            var size = Root.Q<Slider>("size-input");
            var dragger = size?.Q(className: "unity-base-slider__dragger");
            Check(size != null && dragger != null, "size-input is the slider, with a dragger");
            if (size == null || dragger == null) yield break;
            yield return Drag(Ctr(dragger), new Vector2(size.worldBound.xMax / K() + 25f, Ctr(dragger).y));
            Check(Mathf.Abs(Gallery.Model.Size - 2f) < 1e-4f, "dragging size-input to its right end sets Model.Size to Max", Gallery.Model.Size.ToString());
            History.Undo(); yield return null;
            Check(Mathf.Abs(Gallery.Model.Size - 1f) < 1e-4f, "...and one Undo undoes the whole drag", Gallery.Model.Size.ToString());

            var num = Root.Q<TextField>("size-number");
            Check(num != null, "the slider's number field is size-number");
            if (num != null)
            {
                Type(num, "1.5"); yield return null;
                Check(Mathf.Abs(Gallery.Model.Size - 1.5f) < 1e-4f, "typing 1.5 + Enter into the size number sets 1.5", Gallery.Model.Size.ToString());
                Type(num, "abc"); yield return null;
                Check(Mathf.Abs(Gallery.Model.Size - 1.5f) < 1e-4f && num.value == "1.5", "typing abc + Enter restores the text and leaves the value", Gallery.Model.Size + " / " + num.value);
            }

            // Dropdown
            var pick = Root.Q<Button>("pick-input");
            Check(pick != null && pick.text == "Alpha", "the dropdown shows its current item", pick?.text);
            if (pick != null)
            {
                Submit(pick); yield return null; yield return null;
                var gamma = LiveLabel("Gamma");
                Check(gamma != null, "clicking the dropdown opens a menu of its items");
                if (gamma != null)
                {
                    var row = gamma.parent;
                    using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { e.target = row; row.SendEvent(e); }
                    yield return null; yield return null;
                }
                Check(Gallery.Model.Pick == 2 && pick.text == "Gamma", "choosing item 2 sets Model.Pick = 2 and the button shows it", Gallery.Model.Pick + " / " + pick.text);
                Submit(pick); yield return null; yield return null;
                Check(LiveLabel("Alpha") != null, "the menu opens again");
                yield return null;
                bool esc = Input.HandleEscape();
                yield return null; yield return null;
                Check(esc && LiveLabel("Alpha") == null && Gallery.Surface.Visible && Input.AnyWindowOpen, "Escape on the open menu closes only the menu", "esc=" + esc);
            }

            // Snapping, undo keys, long labels: a window of their own
            var h = Host.Create("Values", o => o.Log = m => Main.Log("[values] " + m));
            var surf = new Surface(h, "FacetValues", Layer.Window);
            var w = new Window(surf.Root, "values", "Values", new Rect(60, 60, 560, 520));
            float stepped = 0f; bool a = false, b = false;
            var st = Controls.Slider(w.Body, "Stepped", Bind.To(() => stepped, v => stepped = v), o => { o.Id = "stepped"; o.Min = 0f; o.Max = 2f; o.Step = 0.25f; });
            Controls.Toggle(w.Body, "Enabled", Bind.To(() => a, v => a = v), o => o.Id = "ena");
            Controls.Toggle(w.Body, "Enabled", Bind.To(() => b, v => b = v), o => o.Id = "enb");
            var longEn = Controls.Toggle(w.Body, string.Concat(System.Linq.Enumerable.Repeat("long label ", 19)).Substring(0, 200), Bind.Value(false));
            var longKo = Controls.Toggle(w.Body, string.Concat(System.Linq.Enumerable.Repeat("\uC544\uC8FC \uAE34 \uD55C\uAD6D\uC5B4 \uB77C\uBCA8 ", 10)), Bind.Value(false));
            float bare = 0f;
            Controls.Number(w.Body, "Bare number", Bind.To(() => bare, v => bare = v), o => o.Id = "bare");
            yield return null; yield return null; yield return null;
            Type(w.Body.Q<TextField>("bare-input"), "50"); yield return null;
            Check(bare == 50f, "a Number with no range accepts 50", bare.ToString());

            int ch = 0, cm = 0;
            st.Changed += _ => ch++; st.Committed += _ => cm++;
            var tint = Controls.Colour(w.Body, "Swatch", Bind.Value(Color.red), o => o.Id = "swatch");
            tint.Changed += _ => ch++;
            var sd = st.Input.Q(className: "unity-base-slider__dragger");
            var sl = (Slider)st.Input;
            yield return Drag(Ctr(sd), new Vector2(sl.worldBound.x / K() + sl.worldBound.width / K() * 0.37f, Ctr(sd).y), 6, w.Body);
            float q = stepped / 0.25f;
            Check(stepped > 0f && Mathf.Abs(q - Mathf.Round(q)) < 1e-4f, "with Step = 0.25 the dragged value is a multiple of 0.25", stepped.ToString());

            Check(ch >= 2 && cm == 1, "a slider drag raises Changed per frame and Committed once, at release", "changed=" + ch + " committed=" + cm);

            var sn = w.Body.Q<TextField>("stepped-number");
            cm = 0; Type(sn, "1.5"); yield return null;
            Check(cm == 1 && Mathf.Abs(stepped - 1.5f) < 1e-4f, "a typed number + Enter commits once", "committed=" + cm);
            cm = 0; sn.Focus(); yield return null;
            sn.SetValueWithoutNotify("0.75");
            (sn.focusController?.focusedElement as VisualElement)?.Blur(); yield return null;
            Check(cm == 1 && Mathf.Abs(stepped - 0.75f) < 1e-4f, "leaving the number field commits once", "committed=" + cm + " v=" + stepped);

            // A colour swatch drag: Changed per frame, Committed once at release
            int tm = 0; tint.Committed += _ => tm++;
            ch = 0;
            var head = tint.Root.Q("facet-picker")[0];
            ClickEl(head); yield return null; yield return null; yield return null;
            VisualElement square = null;
            foreach (var e in tint.Root.Query<VisualElement>().ToList())
                if (e.pickingMode == PickingMode.Position && Mathf.Abs(e.layout.width - 132f) < 1f && Mathf.Abs(e.layout.height - 132f) < 1f) square = e;
            Check(square != null, "the open colour picker has its saturation square");
            if (square != null)
            {
                var c0 = Ctr(square);
                yield return Drag(c0, c0 + new Vector2(20f, 20f), 6, w.Body);
                Check(ch >= 2 && tm == 1, "a colour drag raises Changed per frame and Committed once, at release", "changed=" + ch + " committed=" + tm);
            }

            // A channel slider drag, then a typed hex: each commits once, and the drag flag is not stuck
            var rs = tint.Root.Query<Slider>().First();
            var rd = rs?.Q(className: "unity-base-slider__dragger");
            Check(rd != null, "the picker has a channel slider with a dragger");
            if (rd != null)
            {
                ch = 0; tm = 0;
                var r0 = Ctr(rd);
                yield return Drag(r0, r0 - new Vector2(40f, 0f), 6, w.Body);
                Check(ch >= 2 && tm == 1, "a channel slider drag raises Changed per frame and Committed once", "changed=" + ch + " committed=" + tm);
                var hex = tint.Root.Query<TextField>().ToList().Find(f => f.value.StartsWith("#"));
                tm = 0;
                if (hex != null) { Type(hex, "#00ff00"); yield return null; }
                Check(hex != null && tm == 1 && tint.Value.g > .99f && tint.Value.r < .01f, "a typed hex after the drag still commits once", "committed=" + tm + " v=" + tint.Value);
            }

            // Button rows have nothing to poll
            var s20 = new Surface(h, "FacetValues20", Layer.Window);
            var w20 = new Window(s20.Root, "values20", "Twenty", new Rect(60, 60, 400, 300));
            for (int i = 0; i < 20; i++) Controls.Action(w20.Body, "Act " + i, "Go", () => { });
            Controls.Action(w20.Body, "Gated", "Go", () => { }, o => o.EnabledWhen = () => true);
            yield return null; yield return null;
            Check(Bound(RefresherOf(s20)) == 1, "20 Action rows without predicates add 0 to the refresher; one with EnabledWhen adds 1", Bound(RefresherOf(s20)).ToString());
            s20.Dispose();

            History.Clear();
            ClickEl(w.Body.Q("ena-input")); yield return null;
            ClickEl(w.Body.Q("enb-input")); yield return null;
            History.Undo(); yield return null;
            Check(a && !b, "two rows with the same label are separate undo steps", "a=" + a + " b=" + b);

            foreach (var t in new[] { longEn, longKo })
            {
                Check(t.Root.layout.height <= 34f, "a very long label keeps the row height <= 34", t.Root.layout.height.ToString());
                Check(t.Input.worldBound.xMax <= w.Body.worldBound.xMax + 0.5f, "...and the switch stays inside the window body", t.Input.worldBound.xMax + " vs " + w.Body.worldBound.xMax);
            }
            surf.Dispose();
        }

        IEnumerator SharedEscape()
        {
            Gallery.Open(); yield return null;
            Check(Input.AnyWindowOpen, "AnyWindowOpen is true with the Gallery open");
            bool first = Input.HandleEscape(), second = Input.HandleEscape();
            Check(first && !Input.AnyWindowOpen, "the first Escape hides the Gallery", "first=" + first);
            Check(second, "a second Escape in the same frame returns the cached true");
            Check(!Input.AnyWindowOpen, "AnyWindowOpen is false afterwards");
        }

        // Focus, Escape order with a field in a popup over a window, one key capture at a time, the focus ring.
        IEnumerator SectionInput()
        {
            Gallery.Model = new Model();
            var win = Gallery.Open(); yield return null; yield return null;
            var anchor = Root.Q("name-input");
            TextField tf = null;
            var pop = Popup.Open(anchor, c => { tf = new TextField(); c.Add(tf); });
            yield return null; yield return null;
            Check(pop != null && tf != null, "a popup holding a TextField opens over the Gallery");
            if (pop == null || tf == null) yield break;
            tf.Focus(); yield return null;
            Check(Input.TextFocused, "TextFocused is true with the popup's field focused");
            Input.HandleEscape(); yield return null;
            Check(!Input.TextFocused && pop.IsOpen && Gallery.Surface.Visible, "Escape 1 only blurs: field blurred, popup and window stay", "text=" + Input.TextFocused + " pop=" + pop.IsOpen);
            Input.HandleEscape(); yield return null;
            Check(!pop.IsOpen && Gallery.Surface.Visible, "Escape 2 closes the popup, the window stays");
            Input.HandleEscape(); yield return null;
            Check(!Gallery.Surface.Visible && !Input.AnyWindowOpen, "Escape 3 hides the window");

            // A surface hidden with a field focused must not leave the count stuck.
            Gallery.Open(); yield return null; yield return null;
            var name = Root.Q<TextField>("name-input");
            name.Focus(); yield return null;
            Check(Input.TextFocused, "focusing the Text row's field sets TextFocused");
            Gallery.Surface.Visible = false; yield return null;
            Check(!Input.TextFocused, "hiding the surface with the field focused clears TextFocused");
            Gallery.Surface.Visible = true; yield return null;
            name = Root.Q<TextField>("name-input"); name.Focus(); yield return null;
            Gallery.Close(); yield return null;
            Check(!Input.TextFocused, "disposing the surface with the field focused clears TextFocused");
            Check(Input.WantsInput(new Vector2(-10f, -10f)) == (Input.BlocksGameInput || Input.TextFocused), "WantsInput is the OR of the three queries");

            // Key capture: the second claim cancels the first.
            var sf = new Surface(Gallery.Host, "FacetKeyTest", Layer.Window);
            var kw = new Window(sf.Root, "keytest", "Key test", new Rect(100, 100, 500, 300));
            string v1 = "F1", v2 = "F2"; int cc1 = 0, cc3 = 0;
            var k1 = Controls.Key(kw.Body, "One", Bind.To(() => v1, v => v1 = v), o => { o.Id = "k1"; o.Capture = done => { }; o.CancelCapture = () => cc1++; });
            var k2 = Controls.Key(kw.Body, "Two", Bind.To(() => v2, v => v2 = v), o => { o.Id = "k2"; o.Capture = done => { }; });
            yield return null; yield return null;
            var b1 = (Button)k1.Input; var b2 = (Button)k2.Input;
            Check(b1.text == "F1" && b2.text == "F2", "the Key buttons show their bound values", b1.text + "/" + b2.text);
            Submit(b1); yield return null;
            Check(b1.text != "F1" && KeyCapture.Owner != null, "clicking Key one starts its capture", b1.text);
            string owner1 = KeyCapture.Owner;
            Submit(b2); yield return null;
            Check(b1.text == "F1" && b2.text != "F2" && KeyCapture.Owner != owner1, "Key two's capture cancels Key one: its button is back to F1", b1.text + "/" + b2.text);
            Check(cc1 == 1, "...and called Key one's CancelCapture exactly once", cc1.ToString());
            // Teardown mid-capture: the surface hiding releases the capture and tells the host.
            var k3 = Controls.Key(kw.Body, "Three", Bind.To(() => v1, v => v1 = v), o => { o.Id = "k3"; o.Capture = done => { }; o.CancelCapture = () => cc3++; });
            yield return null;
            Submit((Button)k3.Input); yield return null;
            Check(KeyCapture.Owner != null, "Key three starts a capture");
            sf.Visible = false; yield return null;
            Check(KeyCapture.Owner == null && cc3 == 1, "hiding the surface mid-capture releases it and runs CancelCapture once", KeyCapture.Owner + " cc=" + cc3);
            sf.Visible = true; yield return null;
            Submit((Button)k3.Input); yield return null;
            Check(KeyCapture.Owner != null, "Key three captures again");
            k3.Root.RemoveFromHierarchy(); yield return null;
            Check(KeyCapture.Owner == null && cc3 == 2, "the control leaving its panel mid-capture releases it and runs CancelCapture", KeyCapture.Owner + " cc=" + cc3);
            sf.Dispose(); yield return null;

            // Focus ring on a button-based control: border present, clear until focused, Role.Focus after.
            Gallery.Open(); yield return null; yield return null;
            var pick = Root.Q<Button>("pick-input");
            Check(pick != null && pick.style.borderTopWidth.value >= 1f && pick.resolvedStyle.borderTopWidth > 0.3f, "a Dropdown button has a 1px border", pick == null ? "none" : pick.resolvedStyle.borderTopWidth.ToString());
            Check(pick != null && pick.resolvedStyle.borderTopColor == Gallery.Host.Theme[Role.PanelBorder], "...the field border while unfocused", pick?.resolvedStyle.borderTopColor.ToString());
            pick.Focus(); yield return null; yield return null;
            Check(pick.resolvedStyle.borderTopColor == Gallery.Host.Theme[Role.Focus], "...Role.Focus once focused", pick.resolvedStyle.borderTopColor.ToString());
            var mode = Root.Q("mode-input")?.Q<Button>();
            if (mode != null)
            {
                using (var e = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Previous)) { e.target = pick; pick.SendEvent(e); }
                yield return null; yield return null;
                var f2 = Root.panel.focusController.focusedElement as Button;
                Check(f2 != null && f2.resolvedStyle.borderTopColor == Gallery.Host.Theme[Role.Focus], "Navigation onto a Choice segment shows Role.Focus", f2 == null ? "none" : f2.resolvedStyle.borderTopColor.ToString());
            }

            // Focus ring: Next from flag-input moves to the next control, which draws Role.Focus.
            Gallery.Open(); yield return null; yield return null;
            var flag = Root.Q("flag-input"); var size = Root.Q("size-input");
            Check(flag != null && flag.focusable && size != null && size.focusable, "the switch and the slider are focusable");
            flag.Focus(); yield return null;
            using (var e = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next)) { e.target = flag; flag.SendEvent(e); }
            yield return null; yield return null;
            var foc = Root.panel.focusController.focusedElement as VisualElement;
            Check(foc != null && (foc == size || size.Contains(foc)), "NavigationMove Next from flag-input focuses size-input", foc == null ? "none" : foc.GetType().Name + " " + foc.name);
            var want = Gallery.Host.Theme[Role.Focus];
            Check(size.resolvedStyle.borderTopColor == want, "the focused slider's border resolves to Role.Focus", size.resolvedStyle.borderTopColor + " vs " + want);
        }

        /* The session's first surface (a mod's HUD, built at load) schedules the warm-up; a window
           opened after it must not hitch. Timed as PrismLib's LayoutProbe does: the constructor, the
           show, the rest of that frame (layout and draw), and the frame's own length. The check is the
           three parts' sum: the frame length is paced by vsync (17 ms and 8 ms on two runs whose parts
           were the same 10 ms), so it says more about the display than about the window. */
        IEnumerator SectionWarmUp()
        {
            // Another mod on this Facet may have drawn the session's first surface already, as one on
            // 0.1 did in Task 1's cold runs (done 33 ms after ours): then it scheduled the warm-up.
            bool earlier = (bool)typeof(Surface).GetField("WarmUpStarted", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var hud = new Surface(Gallery.Host, "FacetFirstHud", Layer.Hud);
            var ms = new double[1];
            yield return Until(() => WarmUp.Done, ms, 5000);
            if (earlier) Check(ms[0] >= 0, "the warm-up an earlier surface scheduled is done", Ms(ms));
            else Check(ms[0] >= 450, "the first surface's warm-up is done, half a second or more after it", Ms(ms));
            Check(WarmUp.Drawn, "a frame drew the warm-up window before it was thrown away");
            yield return Quiet();

            var sw = Stopwatch.StartNew();
            var s = new Surface(Gallery.Host, "FacetFirstOpen", Layer.Window);
            s.Visible = false;
            Gallery.Build(s.Root, "firstopen");
            long ctor = sw.ElapsedMilliseconds;
            s.Visible = true;
            long show = sw.ElapsedMilliseconds - ctor;
            yield return new WaitForEndOfFrame();
            long draw = sw.ElapsedMilliseconds - ctor - show;
            yield return null;
            long open = ctor + show + draw;
            Main.Log("[perf] first open " + open + " ms (ctor " + ctor + " ms, show " + show + " ms, layout+draw " + draw
                     + " ms; frame " + (Time.unscaledDeltaTime * 1000f).ToString("0") + " ms)");
            Check(open <= 50, "a Gallery-sized window opened after the warm-up takes <= 50 ms", open + " ms");
            s.Dispose(); hud.Dispose();
        }

        /* Waits (at most 3 s) for 5 frames in a row of <= 34 ms, as a player opens a window on a game
           that has finished loading. On a cold start the game and the other mods are still loading,
           and a timed open that shares a frame with their work measures theirs too (layout+draw runs
           to the end of the frame). */
        IEnumerator Quiet()
        {
            var sw = Stopwatch.StartNew();
            int calm = 0, frames = 0;
            while (calm < 5 && sw.ElapsedMilliseconds < 3000)
            {
                yield return null;
                frames++;
                calm = Time.unscaledDeltaTime <= 0.034f ? calm + 1 : 0;
            }
            Main.Log("[perf] settled in " + frames + " frames, " + sw.ElapsedMilliseconds + " ms" + (calm < 5 ? " (never quiet; timing anyway)" : ""));
        }

        static int HistorySubscribers()
        {
            var d = typeof(History).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        static Button CloseButton(Window w)
        {
            var bs = w.TitleBarRight.Query<Button>().ToList();
            return bs.Find(b => b.text == "\u2715");
        }

        // The close button is the owner's to answer, and Anim.Speed 0 is instant.
        IEnumerator SectionClose()
        {
            Gallery.Open(); yield return null;   // Pointer maps screen to panel through the Gallery's root
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            e.style.left = 0f; e.style.top = 0f; e.style.width = 10f; e.style.height = 10f;
            Root.Add(e);
            yield return null; yield return null;   // laid out, so the motion would start from 0
            using (Motion.Hold(0f, false))
            {
                Anim.Move(e, 40f);
                Check(e.style.left.value.value == 40f, "with Anim.Speed = 0, Anim.Move(e, 40) leaves left at 40 in the same call", e.style.left.ToString());
            }
            e.RemoveFromHierarchy();

            // Ruling R20: a search hit's flash follows Anim.Speed too.
            var g = Gallery.Surface.Theme;
            var fl = Ui.Box(Root);
            Paint.Bg(fl, Role.Row);
            yield return null;
            var flash = typeof(Paint).GetMethod("Flash", BindingFlags.NonPublic | BindingFlags.Static);
            using (Motion.Hold(0f, false))
            {
                flash.Invoke(null, new object[] { fl, Role.Accent, 900 });
                Check(Near(fl.style.backgroundColor.value, g[Role.Row]), "with Anim.Speed = 0, a Flash leaves the resting background in the same call", fl.style.backgroundColor.value.ToString());
            }
            yield return null; yield return null;
            Check(Near(fl.style.backgroundColor.value, g[Role.Row]), "...and plays no flash after it", fl.style.backgroundColor.value.ToString());
            fl.RemoveFromHierarchy();

            var logs = new List<string>();
            var h = Host.Create("Closer", o => o.Log = m => { logs.Add(m); Main.Log("[closer] " + m); });

            var s1 = new Surface(h, "FacetCloseHide", Layer.Window);
            var w1 = new Window(s1.Root, "closehide", "Close hide", new Rect(100, 60, 400, 300));
            yield return null; yield return null;
            var x1 = CloseButton(w1);
            Check(x1 != null, "a window with no CloseRequested subscriber still has a close button");
            if (x1 != null) yield return Click(Ctr(x1), 0, x1);
            Check(!s1.Visible && s1.Root != null, "...which hides its surface");
            s1.Dispose();

            // Review Focus 2: a close handler that disposes the surface it runs in.
            int subs = HistorySubscribers();
            var s = new Surface(h, "FacetCloseDispose", Layer.Window);
            var w = new Window(s.Root, "closedispose", "Close dispose", new Rect(100, 60, 400, 300));
            int reads = 0, raised = 0;
            Controls.Toggle(w.Body, "Counting", Bind.To(() => { reads++; return false; }, v => { }));
            w.CloseRequested += win => { raised++; s.Dispose(); };
            yield return null; yield return null;
            var r = RefresherOf(s);
            var x = CloseButton(w);
            Check(x != null && Bound(r) == 1, "the window has a close button and one bound control", "bound " + Bound(r));
            if (x == null) { s.Dispose(); yield break; }
            bool threw = false;
            try { Pointer(EventType.MouseDown, Ctr(x), 0, x); Pointer(EventType.MouseUp, Ctr(x), 0, x); }
            catch (Exception ex) { threw = true; Main.Log("close click threw " + ex); }
            yield return null; yield return null;
            reads = 0;
            yield return new WaitForSeconds(0.3f);
            var errs = logs.FindAll(m => m.StartsWith("[Closer] "));
            Check(!threw && raised == 1 && errs.Count == 0 && s.Root == null, "a CloseRequested subscriber that disposes its surface raises nothing",
                "threw=" + threw + " raised=" + raised + " " + string.Join(" | ", errs));
            Check(Bound(r) == 0 && reads == 0, "...and the dead surface's refresher stops", "bound " + Bound(r) + ", " + reads + " reads");
            Check(HistorySubscribers() == subs, "...and the disposed window leaves History.Changed", subs + " before, " + HistorySubscribers() + " after");
            var undo = Gallery.Surface.Root.Query<Button>().ToList().Find(b => b.text == "\u21B6");
            History.Clear();
            bool was = undo != null && undo.enabledSelf;
            History.Record("Probe", "close-probe", () => { }, () => { });
            Check(undo != null && !was && undo.enabledSelf, "a live window's undo button still follows History", "before " + was + ", after " + undo?.enabledSelf);
            History.Clear();

            // A disposed surface cannot be revived: no stack entry, no open count, no input block.
            Gallery.Close(); yield return null;
            int open0 = Ui.OpenWindows; bool any0 = Input.AnyWindowOpen, blocks0 = Input.BlocksGameInput;
            s.Visible = true; s.BringToFront();
            var stack = (List<Surface>)typeof(Ui).GetField("Stack", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(Ui.OpenWindows == open0 && Input.AnyWindowOpen == any0 && Input.BlocksGameInput == blocks0 && !stack.Contains(s),
                "a disposed surface shown and raised again stays out of the stack, the open count and the input block",
                "open " + open0 + "->" + Ui.OpenWindows + " any " + any0 + "->" + Input.AnyWindowOpen + " blocks " + blocks0 + "->" + Input.BlocksGameInput + " inStack " + stack.Contains(s));
        }

        IEnumerator SectionOpen()
        {
            var w = Gallery.Open(); yield return null; yield return null;
            Check(w.Root.layout.width > 300f, "the Gallery window lays out", "w=" + w.Root.layout.width);
            Check(typeof(Controls).Assembly.GetName().Name == "Facet.0.2", "the Gallery runs on the Facet it was built with");
        }
    }
}
