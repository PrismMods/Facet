using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The first window drawn in a session cost about 85 ms more than any after it: the first run of
       the window, widget and UI Toolkit code (JIT) and its first glyphs, all in the frame it
       opened — a visible hitch on the hotkey. Drawing one throwaway window off screen for a moment,
       half a second after the first surface exists (at load, in practice), moves that cost there;
       the next window opened in 18 ms (measured in game, 2026-11-06). */
    internal static class WarmUp
    {
        internal static bool Done;

        internal static void Run(Host owner)
        {
            // Its own host with no Write (its geometry is not worth a saved key), logging to the
            // consumer whose first surface scheduled it.
            var host = Host.Create("FacetWarmUp", o => o.Log = owner.Log);
            Surface s = null;
            Action done = () =>
            {
                try { if (s != null) s.Dispose(); } catch { }
                Done = true;
            };
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                s = new Surface(host, "FacetWarmUp", Layer.Window);
                if (s.Root == null) { done(); return; }
                s.BlocksGameInput = false;
                var w = new Window(s.Root, "FacetWarmUp", "Facet warm-up", new Rect(120f, 80f, 760f, 560f));
                new Nav(w.Body, new[] { new NavItem("Warm-up", Rows) });
                s.Root.style.translate = new Translate(-10000f, 0f);
                s.Visible = true;
                host.Log("[perf] warm-up window built in " + sw.ElapsedMilliseconds + " ms");
                s.Root.schedule.Execute(done).StartingIn(100);   // after it has been drawn
            }
            catch (Exception e) { host.Log("WarmUp: failed: " + e.Message); done(); }
        }

        // One of each row kind, with every printable ASCII character between them.
        private static void Rows(VisualElement page)
        {
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", lower = "abcdefghijklmnopqrstuvwxyz";
            const string rest = "0123456789 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
            var s = Widgets.Section(page, upper);
            Controls.Toggle(s, lower, Bind.Value(true));
            Controls.Slider(s, rest, Bind.Value(0.5f), o => o.Max = 2f);
            Controls.IntSlider(s, upper, Bind.Value(1), o => o.Max = 2);
            Controls.Choice(s, lower, Bind.Value(1), o => o.Items = new[] { "One", "Two" });
            Controls.Text(s, rest, Bind.Value(lower));
            Controls.Info(s, upper, () => lower);
            Controls.Action(s, lower, "Run", () => { });
            Controls.Colour(s, rest, Bind.Value(Theme.Dark[Role.Accent]));
        }
    }
}
