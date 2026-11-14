using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The first window drawn in a session cost about 85 ms more than any after it: the first run of
       the window, widget and UI Toolkit code (JIT) and its first glyphs, all in the frame it
       opened — a visible hitch on the hotkey. Drawing one throwaway window off screen for a moment,
       half a second after the first surface exists (at load, in practice), moves that cost there;
       the next window opened in 18 ms (measured in game, 2026-11-06). */
    public static class WarmUp
    {
        private static bool _started;

        /// Set once the warm-up window has been drawn and thrown away.
        public static bool Done { get; private set; }

        /// Whether a frame drew the warm-up window before it was thrown away (the autotest reads it).
        internal static bool Drawn { get; private set; }

        /// Draws the warm-up window, once per session: later calls do nothing. The first surface
        /// schedules it half a second after it exists; a host that knows its load better may call it.
        public static void Run()
        {
            if (_started) return;
            _started = true;   // before the surface: the warm-up window is a surface too
            // Fallback has no Write, so the throwaway window's geometry is never saved.
            var host = Host.Fallback;
            Surface s = null;
            bool painted = false;
            int shown = Time.frameCount;
            var lived = new System.Diagnostics.Stopwatch();
            Action done = () =>
            {
                Drawn = painted;
                host.Log("[perf] warm-up window " + (painted ? "drawn" : "thrown away before it was drawn") + ", "
                         + (Time.frameCount - shown) + " frames and " + lived.ElapsedMilliseconds + " ms after it showed");
                try { if (s != null) s.Dispose(); } catch { }
                Done = true;
            };
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                s = new Surface(host, "FacetWarmUp", Layer.Window);
                if (s.Root == null) { done(); return; }
                s.BlocksGameInput = false;
                var w = new Window(s.Root, "FacetWarmUp", "Facet warm-up", new Rect(120f, 80f, 960f, 600f));
                // Two null-titled groups, so the rule between groups is drawn here too.
                new Nav(w.Body, new[] { NavItem.Heading(null, new NavItem("Warm-up", Rows)), NavItem.Heading(null, new NavItem("Warm-up 2", _ => { })) });
                s.Root.style.translate = new Translate(-10000f, 0f);
                s.Root.generateVisualContent += _ => painted = true;   // runs when a frame draws the panel
                s.Visible = true;
                shown = Time.frameCount; lived.Start();
                host.Log("[perf] warm-up window built in " + sw.ElapsedMilliseconds + " ms");
                /* Thrown away 100 ms on, but only once a frame has drawn it. At load a single frame can
                   outlast the 100 ms (237 ms in game beside other mods), and the window went before it was
                   ever laid out or drawn, so the first real window paid the whole cost. A window no frame
                   draws in 2 s goes anyway: it counts as an open window while it lives. */
                s.Root.schedule.Execute(() => { if (painted || lived.ElapsedMilliseconds >= 2000) done(); })
                    .StartingIn(100).Every(16).Until(() => Done);
            }
            catch (Exception e) { host.Log("WarmUp: failed: " + e.Message); done(); }
        }

        /* One of each control kind, with every printable ASCII character between them, and one of each
           0.2 part as a suite page lays them out: a header, then a card (with Advanced) beside a
           preview. Measured in game (2026-11-07): without the parts, a suite page opened straight after
           the warm-up took 19-42 ms against 11-18 ms for a second one; with them, the same as a second. */
        private static void Rows(VisualElement page)
        {
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", lower = "abcdefghijklmnopqrstuvwxyz";
            const string rest = "0123456789 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
            Controls.PageHeader(page, upper, Bind.Value(true), o => o.Description = lower);
            var cols = Controls.Columns(page);
            VisualElement left = cols.Left, right = cols.Right;
            var card = Controls.Card(left, upper, o =>
            {
                o.NoteText = () => lower;
                o.Actions = new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>(rest, () => { }) };
            });
            Controls.Toggle(card, lower, Bind.Value(true), o => o.Help = rest);
            Controls.Toggle(Controls.Advanced(card), upper, Bind.Value(false));
            Controls.Preview(right, lower, o => { o.Show = _ => { }; o.Tick = _ => { }; o.Hide = () => { }; o.Note = upper; o.Link = rest; });
            Controls.Tile(Controls.Tiles(page), upper, Bind.Value(true));
            Tabs.Pages(page, new List<NavItem> { new NavItem(upper, p => Controls.Info(p, lower, () => rest)), new NavItem(lower) });
            var s = Controls.Section(page, upper);
            Controls.Toggle(s, lower, Bind.Value(true));
            Controls.Slider(s, rest, Bind.Value(0.5f), o => o.Max = 2f);
            Controls.Number(s, upper, Bind.Value(4f), o => o.Max = 100f);
            Controls.IntSlider(s, upper, Bind.Value(1), o => o.Max = 2);
            Controls.Choice(s, lower, Bind.Value(1), o => o.Items = new[] { "One", "Two" });
            Controls.Dropdown(s, rest, Bind.Value(0), o => o.Items = new[] { "One", "Two" });
            Controls.Text(s, rest, Bind.Value(lower));
            Controls.Key(s, upper, Bind.Value("F8"));
            Controls.Colour(s, rest, Bind.Value(Theme.Dark[Role.Accent]));
            Controls.Info(s, upper, () => lower);
            Controls.Action(s, lower, "Run", () => { });
            Controls.Danger(s, upper, "Wipe", () => { });
            Controls.ToggleGroup(s, lower, Bind.Value(true));
            Controls.Item(page, upper, lower);
            Form.Build(page, new[] { new Field { Group = rest, Label = lower, Kind = FieldKind.Bool, Get = () => true, Set = _ => { } } });
            Parts(page, upper, lower, rest);
        }

        private sealed class Spot { public string Name; public Rect R; }

        /* The editor's parts, as an editor page holds them: a menu bar with its tools, a tab strip, a canvas with two
           items, a card grid with its add card, a scrolled inspector with a scrubbed number and a chord key row. Without
           them an editor window opened straight after the warm-up paid their first run in its own frame. */
        private static void Parts(VisualElement page, string upper, string lower, string rest)
        {
            var words = new List<string> { upper, lower };
            var bar = Controls.MenuBar(page, new List<MenuItem>
            {
                new MenuItem { Text = upper, Items = new List<MenuItem> { new MenuItem { Text = lower, Shortcut = "Ctrl+Shift+K", Run = () => { } }, MenuItem.Separator } },
            });
            bar.Tools.Label(() => rest);
            bar.Tools.Toggle(lower, Bind.Value(true), upper);
            bar.Tools.UndoRedo();
            Controls.Reorder<string>(page, o => { o.Title = upper; o.Items = () => words; o.Text = x => x; o.Scroll = true; o.Add = () => null; });
            var spots = new List<Spot> { new Spot { Name = upper, R = new Rect(10f, 10f, 60f, 40f) }, new Spot { Name = lower, R = new Rect(90f, 10f, 60f, 40f) } };
            Controls.Canvas<Spot>(page, upper, o =>
            {
                o.Items = () => spots; o.Key = x => x.Name; o.Find = k => spots.Find(x => x.Name == k);
                o.GetRect = x => x.R; o.SetRect = (x, r) => x.R = r;
                o.Size = new Vector2(400f, 200f);
            });
            Controls.CardGrid<string>(page, o =>
            {
                o.Items = () => words; o.Title = x => x; o.Note = x => rest; o.Active = x => x == upper;
                o.Thumb = (e, x) => { }; o.AddLabel = lower; o.Add = () => null;
            });
            var scroll = Ui.Scroll(page);
            Controls.Live(scroll, () => 1, p =>
            {
                Controls.Number(p, lower, Bind.Value(1f), o => { o.Scrub = true; o.Step = 1f; });
                Controls.Key(p, upper, Bind.Value("Ctrl+K"), o => o.Chord = true);
                Controls.Repeat<string>(p, () => words, x => x, (box, x) => Controls.Info(box, x, () => rest));
            });
        }
    }
}
