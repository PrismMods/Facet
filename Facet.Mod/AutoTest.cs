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
    sealed class AutoTest : MonoBehaviour
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

        // via: an element on the panel to send to, when it is not the Gallery's.
        void Pointer(EventType type, Vector2 screen, int button = 0, VisualElement via = null)
        {
            float k = K();
            var ev = new Event { type = type, button = button, clickCount = 1,
                mousePosition = new Vector2(screen.x * k, (Screen.height - screen.y) * k) };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(ev)
                        : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev)
                        : (EventBase)PointerMoveEvent.GetPooled(ev);
            using (e) (via ?? Root).panel.visualTree.SendEvent(e);
        }

        IEnumerator Drag(Vector2 from, Vector2 to, int steps = 6, VisualElement via = null)
        {
            Pointer(EventType.MouseDown, from, 0, via); yield return null;
            for (int i = 1; i <= steps; i++) { Pointer(EventType.MouseDrag, Vector2.Lerp(from, to, i / (float)steps), 0, via); yield return null; }
            Pointer(EventType.MouseUp, to, 0, via); yield return null; yield return null;
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
            yield return Run("SectionOpen", SectionOpen());
            yield return Run("SharedEscape", SharedEscape());
            yield return Run("SectionHost", SectionHost());
            yield return Run("SectionTheme", SectionTheme());
            yield return Run("SectionNoStale", SectionNoStale());
            yield return Run("SectionScrollClear", SectionScrollClear());
            yield return Run("SectionWords", SectionWords());
            yield return Run("SectionBind", SectionBind());
            yield return Run("SectionRebuild", SectionRebuild());
            yield return Run("SectionRefreshIsolation", SectionRefreshIsolation());
            yield return Run("SectionValues", SectionValues());
            yield return Run("SectionStructure", SectionStructure());
            // Later tasks: yield return Run("Section...", Section...());

            Main.Log("AUTOTEST DONE pass=" + _pass + " fail=" + _fail);
            yield return null;
            Application.Quit();
        }

        // Drives a section by hand so a throw is logged to gallery.log and the run carries on.
        IEnumerator Run(string name, IEnumerator section)
        {
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
            new Nav(wa.Body, new[] { new NavItem("Boom", p => throw new InvalidOperationException("boom")) });
            new Nav(wb.Body, new[] { new NavItem("Fine", p => Controls.Info(p, "Fine", () => "ok")) });
            yield return null; yield return null;
            var ea = la.FindAll(m => m.StartsWith("[A] "));
            Check(ea.Count == 1 && ea[0].Contains("Boom"), "A's list has one guard entry naming the page", string.Join(" | ", ea));
            Check(!lb.Exists(m => m.StartsWith("[B] ") || m.Contains("Boom")), "B's list stays empty of A's failure");
            Check(wa.Body.Query<Label>().ToList().Exists(l => l.text.Contains("failed to build")), "A's content shows the failed-to-build text");
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
            // The warm-up window builds a Nav, whose constructor clears the shared search index:
            // landing mid-section it wiped this page's entries and the search found nothing.
            var warm = typeof(Surface).Assembly.GetType("Facet.WarmUp").GetField("Done", BindingFlags.NonPublic | BindingFlags.Static);
            for (var sw = Stopwatch.StartNew(); !(bool)warm.GetValue(null) && sw.ElapsedMilliseconds < 3000;) yield return null;
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
            var sw = Stopwatch.StartNew();
            s.Refresh();
            Main.Log("[perf] refresh pass, 100 bound controls " + sw.Elapsed.TotalMilliseconds.ToString("0.000") + " ms");

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
            yield return null; yield return null; yield return null;

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

        IEnumerator SectionOpen()
        {
            var sw = Stopwatch.StartNew();
            var w = Gallery.Open(); yield return null; yield return null;
            Check(w.Root.layout.width > 300f, "the Gallery window lays out", "w=" + w.Root.layout.width);
            Main.Log("[perf] gallery first open " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
