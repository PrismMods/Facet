using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

        void Pointer(EventType type, Vector2 screen)
        {
            float k = K();
            var ev = new Event { type = type, button = 0, clickCount = 1,
                mousePosition = new Vector2(screen.x * k, (Screen.height - screen.y) * k) };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(ev)
                        : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev)
                        : (EventBase)PointerMoveEvent.GetPooled(ev);
            using (e) Root.panel.visualTree.SendEvent(e);
        }

        IEnumerator Drag(Vector2 from, Vector2 to, int steps = 6)
        {
            Pointer(EventType.MouseDown, from); yield return null;
            for (int i = 1; i <= steps; i++) { Pointer(EventType.MouseDrag, Vector2.Lerp(from, to, i / (float)steps)); yield return null; }
            Pointer(EventType.MouseUp, to); yield return null; yield return null;
        }

        // Down and up in the same frame: a real pointer's own move event between them would turn
        // a still click into a drag.
        IEnumerator Click(Vector2 at)
        {
            Pointer(EventType.MouseDown, at);
            Pointer(EventType.MouseUp, at); yield return null; yield return null;
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
            new Nav(wb.Body, new[] { new NavItem("Fine", p => Widgets.Info(p, "Fine", "ok")) });
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
