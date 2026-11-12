using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    sealed partial class AutoTest
    {
        const float GlideMs = 200f;   // Motion.Scale(200) at speed 1

        static bool? SharedSmooth()
        {
            var d = AppDomain.CurrentDomain.GetData("Facet.Shared") as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue("Facet.SmoothScroll", out v) && v is bool[] a && a.Length > 0 ? a[0] : (bool?)null;
        }

        // The glide's scheduled item on a ScrollView, or null when it never glided.
        static IVisualElementScheduledItem GlideItem(VisualElement sv)
        {
            var state = typeof(Anim).Assembly.GetType("Facet.Glide+State");
            if (state == null) return null;
            var s = typeof(Data).GetMethod("Find").MakeGenericMethod(state).Invoke(null, new object[] { sv });
            return s == null ? null : state.GetField("Item").GetValue(s) as IVisualElementScheduledItem;
        }

        // The glide's curve as its state holds it (From, To, Start, Ms); null when it never glided.
        static float[] GlideCurve(VisualElement sv)
        {
            var state = typeof(Anim).Assembly.GetType("Facet.Glide+State");
            var s = state == null ? null : typeof(Data).GetMethod("Find").MakeGenericMethod(state).Invoke(null, new object[] { sv });
            if (s == null) return null;
            Func<string, float> f = n => Convert.ToSingle(state.GetField(n).GetValue(s));
            return new[] { f("From"), f("To"), f("Start"), f("Ms") };
        }

        // How far the offset is past where that curve is now. A step writes the curve at its own time, never later than now.
        static float Ahead(ScrollView sv, float[] c)
        {
            float t = c[3] > 0f ? (Time.realtimeSinceStartup - c[2]) * 1000f / c[3] : 1f;
            float y = Mathf.LerpUnclamped(c[0], c[1], UnityEngine.UIElements.Experimental.Easing.OutCirc(Mathf.Clamp01(t)));
            return c[1] >= c[0] ? sv.scrollOffset.y - y : y - sv.scrollOffset.y;
        }

        // In game every Prism bridge rewrites the switch and the speed each frame: a hold keeps the test's own.
        static IDisposable Smooth(bool on, float speed = 1f) => Motion.Hold(speed, on);

        static void SendWheel(VisualElement root, VisualElement over, float dy)
        {
            var ev = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, dy), mousePosition = over.worldBound.center };
            using (var e = WheelEvent.GetPooled(ev)) root.panel.visualTree.SendEvent(e);
        }

        sealed class GlideRun { public float Y0, Same, Mid, End; public bool Late, Active, Idle; }

        /* One step, then the offset in the step's own frame, a sample once a quarter of the glide has passed in
           real time (before its end: a frame that ran past the end re-runs the step once), and the settled
           offset after the glide plus two frames. */
        IEnumerator Glided(ScrollView sv, Action step, GlideRun r)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                r.Y0 = sv.scrollOffset.y;
                float t0 = Time.realtimeSinceStartup;
                step();
                r.Same = sv.scrollOffset.y;
                while (Time.realtimeSinceStartup - t0 < GlideMs / 4000f) yield return null;
                r.Mid = sv.scrollOffset.y;
                r.Late = Time.realtimeSinceStartup - t0 >= GlideMs / 1000f;
                var item = GlideItem(sv);
                r.Active = item != null && item.isActive;
                yield return Wait(t0);
                r.End = sv.scrollOffset.y;
                item = GlideItem(sv);
                r.Idle = item == null || !item.isActive;
                if (!r.Late) yield break;
                Main.Log("INFO glide sample ran past the glide's end; re-running the step once");
            }
        }

        // Until a glide that started at t0 has ended, plus two frames.
        static IEnumerator Wait(float t0)
        {
            while (Time.realtimeSinceStartup - t0 < GlideMs / 1000f) yield return null;
            yield return null; yield return null;
        }

        static bool Between(float v, float a, float b) => v > Mathf.Min(a, b) && v < Mathf.Max(a, b);

        /* With Anim.SmoothScroll on, a wheel step glides (OutCirc, Motion.Scale(200) ms) to where it would have
           jumped, on the polled path and the panel's own; a later step retargets the glide; Facet's own movers
           and outside writes end it; off, or at speed 0, it steps at once. */
        IEnumerator SectionSmoothScroll()
        {
            var s = new Surface(Host.Create("Smooth", o => o.Log = m => Main.Log("[smooth] " + m)), "FacetSmooth", Layer.Window);
            try
            {
                Anim.SmoothScroll = true; var on = SharedSmooth();
                Anim.SmoothScroll = false; var off = SharedSmooth();
                Check(on == true && off == false, "Anim.SmoothScroll reads back from the cross-version store", on + "/" + off);
                using var glides = Smooth(true);

                var w = new Window(s.Root, "smooth", "Smooth", new Rect(60, 60, 480, 360));
                var sv = new ScrollView(ScrollViewMode.Vertical);
                sv.style.height = 200f; sv.style.flexGrow = 0f; sv.style.flexShrink = 0f;
                sv.contentContainer.style.flexShrink = 0f;   // not a Box: left at 1 it squashes to the view and nothing scrolls
                w.Body.Add(sv);
                Skin.Scroll(sv);
                var box = Ui.Box(sv);
                box.style.height = 3000f;
                yield return null; yield return null;
                var c = Ctr(sv);
                Check(GlideItem(sv) == null, "a ScrollView that never glided has no glide item");

                var r = new GlideRun();
                yield return Glided(sv, () => Facet.Input.TickWheel(c, -1f), r);
                Check(Mathf.Approximately(r.Same, r.Y0), "polled path: a notch moves nothing in the frame it arrives", (r.Same - r.Y0).ToString());
                Check(Between(r.Mid, r.Y0, r.Y0 + 60f), "...a quarter into the glide the offset is between start and target", r.Y0 + " < " + r.Mid + " < " + (r.Y0 + 60f) + (r.Late ? " (late)" : ""));
                Check(r.Active, "...while its scheduled item runs");
                Check(Mathf.Abs(r.End - r.Y0 - 60f) <= 0.5f, "...and it settles one notch (60 px) further", (r.End - r.Y0).ToString());
                Check(r.Idle, "...after which its scheduled item is paused");

                float unity = 3f * sv.mouseWheelScrollSize;
                yield return Glided(sv, () => SendWheel(s.Root, sv, 3f), r);
                Check(Mathf.Approximately(r.Same, r.Y0), "the panel's own wheel event adds no instant step", (r.Same - r.Y0).ToString());
                Check(Between(r.Mid, r.Y0, r.Y0 + unity), "...glides: a quarter in the offset is between start and target", r.Y0 + " < " + r.Mid + " < " + (r.Y0 + unity));
                Check(Mathf.Abs(r.End - r.Y0 - unity) <= 0.5f, "...and settles Unity's own step further (3 x its scroll size)", (r.End - r.Y0) + " vs " + unity);

                float y0 = sv.scrollOffset.y, t0;
                Facet.Input.TickWheel(c, -1f); yield return null;
                Facet.Input.TickWheel(c, -1f); t0 = Time.realtimeSinceStartup;
                yield return Wait(t0);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 120f) <= 0.5f, "two quick notches glide two notches", (sv.scrollOffset.y - y0).ToString());

                // A host that sends WheelEvents also has the notch in Input: each path leaves a notch the other took.
                y0 = sv.scrollOffset.y; t0 = Time.realtimeSinceStartup;
                Facet.Input.TickWheel(c, -1f); SendWheel(s.Root, sv, 3f);
                yield return Wait(t0);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 60f) <= 0.5f, "a notch the polled path took is not taken again by the panel's own event in that frame", (sv.scrollOffset.y - y0).ToString());
                y0 = sv.scrollOffset.y; t0 = Time.realtimeSinceStartup;
                SendWheel(s.Root, sv, 3f); Facet.Input.TickWheel(c, -1f);
                yield return Wait(t0);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - unity) <= 0.5f, "...nor one the panel's own event took by the polled path", (sv.scrollOffset.y - y0) + " vs " + unity);

                y0 = sv.scrollOffset.y; t0 = Time.realtimeSinceStartup;
                Facet.Input.TickWheel(c, -1f);
                while (Time.realtimeSinceStartup - t0 < GlideMs / 4000f) yield return null;
                float turned = sv.scrollOffset.y;
                Facet.Input.TickWheel(c, 1f); t0 = Time.realtimeSinceStartup;
                yield return null; yield return null; yield return null;
                float back = sv.scrollOffset.y;
                yield return Wait(t0);
                Check(back < turned && Mathf.Abs(sv.scrollOffset.y - y0) <= 0.5f, "a reverse notch mid-glide heads back to where it began",
                    "turned " + (turned - y0) + " then " + (back - y0) + ", ends " + (sv.scrollOffset.y - y0));

                y0 = sv.scrollOffset.y; t0 = Time.realtimeSinceStartup;
                Facet.Input.TickWheel(c, -1f);
                while (Time.realtimeSinceStartup - t0 < GlideMs / 4000f) yield return null;
                sv.scrollOffset = new Vector2(0f, y0 + 500f);
                float wrote = sv.scrollOffset.y;
                yield return Wait(t0);
                Check(Mathf.Abs(sv.scrollOffset.y - wrote) <= 0.5f, "an outside write mid-glide wins and stays", wrote + " -> " + sv.scrollOffset.y);

                sv.scrollOffset = Vector2.zero; yield return null;
                t0 = Time.realtimeSinceStartup;
                Facet.Input.TickWheel(c, -1f);
                while (Time.realtimeSinceStartup - t0 < GlideMs / 4000f) yield return null;
                box.style.height = sv.contentViewport.layout.height + 20f;
                yield return Wait(t0);
                float max = sv.contentContainer.layout.height - sv.contentViewport.layout.height;
                Check(sv.scrollOffset.y >= 0f && sv.scrollOffset.y <= max + 0.5f, "content shrinking mid-glide ends inside the new range", sv.scrollOffset.y + " of " + max);
                box.style.height = 3000f; yield return null; yield return null;

                // A native notch past a glide that already heads to the end is the glide's: left to Unity it lands there at once.
                float end = sv.contentContainer.layout.height - sv.contentViewport.layout.height;
                sv.scrollOffset = new Vector2(0f, end - unity * 0.75f); yield return null; yield return null;
                t0 = Time.realtimeSinceStartup;
                SendWheel(s.Root, sv, 3f);
                var curve = GlideCurve(sv);
                while (Time.realtimeSinceStartup - t0 < GlideMs / 4000f) yield return null;
                SendWheel(s.Root, sv, 3f);
                float ahead = curve == null ? float.NaN : Ahead(sv, curve);
                while (curve != null && Time.realtimeSinceStartup - t0 < GlideMs / 1000f) { yield return null; ahead = Mathf.Max(ahead, Ahead(sv, curve)); }
                yield return null; yield return null;
                Check(ahead <= 0.5f, "a native notch while the glide already heads to the end never puts the offset ahead of the glide's curve", ahead.ToString());
                Check(Mathf.Abs(sv.scrollOffset.y - end) <= 0.5f, "...and the glide settles exactly at the end", sv.scrollOffset.y + " of " + end);

                // A delta every frame (a trackpad, a fast wheel) retargets every frame: each step must still cover its frame.
                sv.scrollOffset = Vector2.zero; yield return null; yield return null;
                float sum = 0f, trail = 0f, first = 0f, last = 0f; int fed = 0;
                for (int i = 0; i < 30; i++)
                {
                    if (i >= 20) { trail += sum - sv.scrollOffset.y; fed++; }   // what the last frame's step left to go
                    Facet.Input.TickWheel(c, -0.05f); sum += 20f;   // a trackpad's fraction: 0.05 x 400 px
                    last = Time.realtimeSinceStartup;
                    if (i == 0) first = last;
                    yield return null;
                }
                trail /= fed * 20f;
                string lag = trail.ToString("0.00") + " frames of " + ((last - first) * 1000f / 29f).ToString("0.0") + " ms";
                Main.Log("INFO glide trail under a delta every frame: " + lag);
                while (Time.realtimeSinceStartup - last < 0.25f) yield return null;
                // A first step that covers its frame trails by (1 - OutCirc(frame / 200 ms)) / OutCirc(...) frames: 1.9 at
                // 12.5 ms frames, 2.5 at 8.3 ms. One that starts from the feed's own instant trailed by 17-18.
                Check(trail <= 3f, "a delta every frame: the glide trails their sum by at most 3 frames' worth", lag);
                Check(Mathf.Abs(sv.scrollOffset.y - sum) <= 0.5f, "...and settles exactly at the sum within 250 ms of the last", sv.scrollOffset.y + " of " + sum);

                y0 = sv.scrollOffset.y;
                using (Smooth(false)) Facet.Input.TickWheel(c, -1f);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 60f) <= 0.5f, "switched off, a notch lands in the frame it arrives", (sv.scrollOffset.y - y0).ToString());
                yield return null;
                y0 = sv.scrollOffset.y;
                using (Smooth(true, 0f)) Facet.Input.TickWheel(c, -1f);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 60f) <= 0.5f, "at Anim.Speed 0, a notch lands in the frame it arrives", (sv.scrollOffset.y - y0).ToString());
                yield return null;
                y0 = sv.scrollOffset.y;
                using (Smooth(false)) { SendWheel(s.Root, sv, 3f); yield return null; }
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - unity) <= 0.5f, "switched off, the panel's own wheel event steps at once", (sv.scrollOffset.y - y0) + " vs " + unity);
                yield return null; yield return null;
                var idle = GlideItem(sv);
                Check(idle != null && !idle.isActive, "a ScrollView that glided and settled has no running scheduled item", idle == null ? "none" : "active=" + idle.isActive);
                s.Dispose(); s = null;

                yield return SmoothNav();
            }
            finally { s?.Dispose(); }
        }

        // Facet's own movers end a glide: a Nav rebuild keeps the whole notch, a search reveal's ScrollTo wins.
        IEnumerator SmoothNav()
        {
            var s = new Surface(Host.Create("SmoothNav", o => o.Log = m => Main.Log("[smoothnav] " + m)), "FacetSmoothNav", Layer.Window);
            try
            {
                var w = new Window(s.Root, "smoothnav", "Smooth nav", new Rect(60, 60, 600, 400));
                var page = new NavItem("Long", p => { for (int i = 0; i < 40; i++) Controls.Info(p, i == 4 ? "Reveal row" : "Row " + i, () => "v"); });
                var nav = new Nav(w.Body, new[] { page });
                yield return null; yield return null; yield return null;
                Func<string, Label> lab = t => w.Body.Query<Label>().ToList().Find(l => l.text == t);
                var sv = lab("Row 5")?.GetFirstAncestorOfType<ScrollView>();
                Check(sv != null, "the long page is in a ScrollView");
                if (sv == null) yield break;
                var c = Ctr(sv);

                float y0 = sv.scrollOffset.y, t0 = Time.realtimeSinceStartup;
                Facet.Input.TickWheel(c, -1f);
                while (Time.realtimeSinceStartup - t0 < GlideMs / 4000f) yield return null;
                float at = sv.scrollOffset.y;
                nav.Refresh();
                float restored = sv.scrollOffset.y;   // what the rebuild keeps and puts back until it holds
                yield return Wait(t0);
                yield return new WaitForSeconds(0.1f);
                Check(Mathf.Abs(restored - y0 - 60f) <= 0.5f && Mathf.Abs(sv.scrollOffset.y - y0 - 60f) <= 0.5f, "a Nav rebuild mid-glide keeps the whole notch: it restores the glide's target",
                    "rebuilt at " + (at - y0) + ", restored " + (restored - y0) + ", ends " + (sv.scrollOffset.y - y0));

                // The reveal's ScrollTo runs on the panel's next update, mid-glide, on a row that is still in view:
                // it writes nothing, so only Nav ending the glide keeps the row there.
                nav.Filter("reveal row"); yield return null;
                nav.Filter(""); yield return null; yield return null;
                var hits = nav.Index?.Find("reveal row");
                Check(hits != null && hits.Count > 0 && hits[0].Key == "Reveal row", "the search index has the row");
                if (hits == null || hits.Count == 0) yield break;
                sv.scrollOffset = Vector2.zero; yield return null; yield return null;
                var row = lab("Reveal row");
                float bottom = row == null ? float.NaN : row.worldBound.yMax - sv.contentContainer.worldBound.yMin;
                var reveal = typeof(Nav).GetMethod("Reveal", BindingFlags.NonPublic | BindingFlags.Instance);
                c = Ctr(sv);
                t0 = Time.realtimeSinceStartup;
                Facet.Input.TickWheel(c, -5f);   // 300 px: past the row, so a glide left running carries it out of view
                reveal.Invoke(nav, new object[] { hits[0] });
                yield return Wait(t0);
                float settled = sv.scrollOffset.y;
                yield return null; yield return null;
                var view = sv.contentViewport.worldBound;
                bool shown = row != null && row.worldBound.yMin >= view.yMin - 1f && row.worldBound.yMax <= view.yMax + 1f;
                Check(bottom < 300f && shown && settled < 299.5f && Mathf.Abs(sv.scrollOffset.y - settled) <= 0.5f, "a search reveal mid-glide wins and stays: the row is in view",
                    "row bottom " + bottom + ", offset " + settled + " -> " + sv.scrollOffset.y + ", row " + (row == null ? "none" : row.worldBound.yMin + ".." + row.worldBound.yMax) + " view " + view.yMin + ".." + view.yMax);
            }
            finally { s.Dispose(); }
        }
    }
}
