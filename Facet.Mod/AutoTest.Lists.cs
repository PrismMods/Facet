using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // List parts a browser page needs: image cards.
    sealed partial class AutoTest
    {
        sealed class Pic { public int N; public string Name; }

        static List<Pic> Pics(int from, int count)
        {
            var l = new List<Pic>(count);
            for (int i = from; i < from + count; i++) l.Add(new Pic { N = i, Name = "Item " + i });
            return l;
        }

        IEnumerator SectionImageCards()
        {
            var logs = new List<string>();
            var h = Host.Create("Cards", o => o.Log = m => { logs.Add(m); Main.Log("[cards] " + m); });
            var s = new Surface(h, "FacetCards", Layer.Window);
            s.Visible = true;
            var w = new Window(s.Root, "cards", "Cards", new Rect(60, 40, 1000, 760));
            var stage = new VisualElement { name = "cards-stage" };
            stage.style.width = 860f; stage.style.flexShrink = 0f;
            w.Body.Add(stage);
            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            var tex2 = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                yield return null; yield return null;

                // Building 300 attached, then refreshing with nothing changed.
                var items = Pics(1, 300);
                var bodyOf = new Dictionary<Pic, VisualElement>();
                int bodies = 0;
                bool grid = false;
                Func<Pic, Action<VisualElement>> below = null;
                var sw = Stopwatch.StartNew();
                var cards = Controls.ImageCards<Pic>(stage, o =>
                {
                    o.Items = () => items; o.Key = p => p.Name; o.Grid = () => grid;
                    o.Body = (e, p) => { bodies++; bodyOf[p] = e; Ui.Text(e, "").text = p.Name; };
                    o.Below = p => below?.Invoke(p);
                });
                double build = sw.Elapsed.TotalMilliseconds;
                Main.Log("[perf] image cards 300 first build: " + build.ToString("0.0") + " ms");
                Check(build <= 40.0 && bodies == 300, "ImageCards: 300 items build attached within 40 ms", build.ToString("0.0") + " ms, " + bodies + " bodies");
                yield return null; yield return null;
                sw.Restart();
                for (int i = 0; i < 20; i++) cards.Refresh();
                double idle = sw.Elapsed.TotalMilliseconds / 20.0;
                Main.Log("[perf] image cards Refresh unchanged: " + idle.ToString("0.000") + " ms");
                Check(idle <= 1.0 && bodies == 300, "ImageCards: Refresh with nothing changed takes 1 ms or less", idle.ToString("0.000") + " ms");

                Func<Pic, VisualElement> card = p => bodyOf.TryGetValue(p, out var b) ? b.parent : null;
                var first = items.Select(card).ToList();
                items.AddRange(Pics(301, 50));
                cards.Refresh();
                bool kept = items.Take(300).Select(card).SequenceEqual(first, ReferenceEqualityComparer.Instance);
                Check(kept && bodies == 350 && cards.Root.Query(name: "image-cards-card").ToList().Count == 350,
                    "ImageCards: appending 50 keeps the first 300 card elements and builds 50 bodies", kept + " " + bodies);

                var seven = card(items[6]);
                items.RemoveAt(6);
                cards.Refresh();
                bool gone = seven.parent == null && cards.Root.Query(name: "image-cards-card").ToList().Count == 349;
                var three = items[2];
                var el3 = card(three);
                items.RemoveAt(2); items.Add(three);
                cards.Refresh();
                var all = cards.Root.Query(name: "image-cards-card").ToList();
                Check(gone && all.Last() == el3 && bodies == 350, "ImageCards: removing an item removes its card; moving one moves its element without a Body call",
                    gone + " " + (all.Last() == el3) + " " + bodies);

                // Columns and rows, then a Below.
                items = Pics(1001, 12); bodyOf.Clear();   // new keys: twelve fresh cards
                cards.Refresh(); grid = true; cards.Refresh();
                yield return null; yield return null;
                int built = bodies;
                int wide = cards.Columns;
                bool row = Mathf.Abs(card(items[0]).worldBound.y - card(items[2]).worldBound.y) < 0.5f
                    && card(items[3]).worldBound.y > card(items[2]).worldBound.y + 1f;
                stage.style.width = 300f;
                yield return null; yield return null;
                int narrow = cards.Columns;
                bool stacked = card(items[1]).worldBound.y > card(items[0]).worldBound.y + 1f;
                Check(wide == 3 && narrow == 1 && row && stacked && bodies == built,
                    "ImageCards: 860 px gives 3 columns, 300 px gives 1; rows regroup without a Body call", wide + "/" + narrow + " " + row + "/" + stacked + " " + (bodies - built));

                var four = items[3];
                below = p => p == four ? (Action<VisualElement>)(b => Ui.Text(b, "").text = "Below") : null;
                cards.Refresh();
                var bel = cards.Root.Q("image-cards-below");
                var kids = cards.Root.Children().ToList();
                bool afterCard = bel != null && kids.IndexOf(bel) == kids.IndexOf(card(four)) + 1;
                stage.style.width = 860f;
                yield return null; yield return null;
                kids = cards.Root.Children().ToList();
                bool afterRow = bel != null && cards.Columns == 3 && kids.IndexOf(bel) == kids.IndexOf(card(items[5])) + 1
                    && bel.worldBound.y >= card(items[5]).worldBound.yMax - 0.5f;
                Check(afterCard && afterRow, "ImageCards: Below sits right after its card in one column and after its row in a grid", afterCard + "/" + afterRow);
                below = null; cards.Refresh();
                Check(cards.Root.Q("image-cards-below") == null, "ImageCards: a Below that turns null goes");
                cards.Root.RemoveFromHierarchy();

                // A detached build.
                int dImages = 0, dBodies = 0;
                var holder = new VisualElement();
                var five = Pics(1, 5);
                Controls.ImageCards<Pic>(holder, o =>
                {
                    o.Items = () => five; o.Key = p => p.Name;
                    o.Image = p => { dImages++; return null; };
                    o.Body = (e, p) => dBodies++;
                });
                Check(dImages == 0, "ImageCards asks for no image while detached", dImages.ToString());
                Check(dBodies == 0, "a detached build calls Body zero times", dBodies.ToString());
                stage.Add(holder);
                yield return null; yield return null;
                Check(dBodies == 5, "ImageCards: attaching builds every body once", dBodies.ToString());
                holder.RemoveFromHierarchy();

                // The image window: 300 cards of 50 px in a 600 px view.
                var asked = new List<int>();
                Texture handed = null;
                var sv = Ui.Scroll(stage);
                sv.style.height = 600f; sv.style.flexGrow = 0f;
                var tall = Pics(1, 300);
                var byN = new Dictionary<int, VisualElement>();
                var view = Controls.ImageCards<Pic>(sv, o =>
                {
                    o.Items = () => tall; o.Key = p => p.Name; o.Gap = 0f;
                    o.Image = p => { asked.Add(p.N); return handed; };
                    o.Body = (e, p) => { Ui.SetPadding(e, 0f); e.style.height = 50f; byN[p.N] = e.parent; };
                });
                yield return null; yield return null; yield return null;
                Check(Span(asked, 1, 24), "ImageCards asks only for cards near the viewport", Show(asked));
                asked.Clear();
                sv.scrollOffset = new Vector2(0f, 600f);
                yield return null; yield return null;
                Check(Span(asked, 25, 36), "ImageCards: scrolling one view down asks for the next view's cards only", Show(asked));

                Func<int, Image> img = n => byN[n].Q<Image>("image-cards-image");
                Func<int, VisualElement> layer = n => byN[n].Q("image-cards-layer");
                handed = tex;
                bool fading;
                using (Motion.Hold(1f, false))
                {
                    view.ImagesChanged();
                    fading = layer(20).resolvedStyle.opacity < 1f || layer(20).style.opacity.value < 1f;
                    yield return null;
                    bool set = img(20).image == tex && img(1).image == tex;
                    yield return new WaitForSeconds(0.4f);
                    Check(set && fading && layer(20).resolvedStyle.opacity > 0.99f, "ImageCards: a texture handed in shows within one frame and fades in",
                        set + "/" + fading + "/" + layer(20).resolvedStyle.opacity);
                }
                handed = tex2;
                sw.Restart();
                view.ImagesChanged();
                double changed = sw.Elapsed.TotalMilliseconds;
                Main.Log("[perf] image cards ImagesChanged 300: " + changed.ToString("0.000") + " ms");
                Check(changed <= 2.0 && img(20).image == tex2, "ImageCards: ImagesChanged over 300 cards takes 2 ms or less", changed.ToString("0.000") + " ms");
                handed = null;
                view.ImagesChanged();
                Check(img(20).image == null && layer(20).style.display == DisplayStyle.None,
                    "ImageCards: a null image afterwards clears the card");
                var doomed = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                handed = doomed;
                view.ImagesChanged();
                UnityEngine.Object.DestroyImmediate(doomed);
                handed = null;
                view.ImagesChanged();
                // Image's own setter ignores null over a destroyed texture (Unity equality), so the layer is what hides it.
                Check(img(20).image == null && layer(20).style.display == DisplayStyle.None,
                    "ImageCards: a null image after the shown texture was destroyed clears the card");
                handed = tex;
                sv.scrollOffset = Vector2.zero;
                yield return null; yield return null;
                view.ImagesChanged();
                bool had = img(1).image == tex;
                sv.scrollOffset = new Vector2(0f, 3000f);
                yield return null; yield return null;
                bool dropped = img(1).image == null;
                asked.Clear();
                sv.scrollOffset = Vector2.zero;
                yield return null; yield return null;
                Check(had && dropped && asked.Contains(1) && img(1).image == tex,
                    "ImageCards: a card scrolled out of the window drops its texture and asks again when scrolled back", had + "/" + dropped + "/" + asked.Contains(1));
                sv.RemoveFromHierarchy();

                // A throwing Body, and clicks.
                logs.Clear();
                var nine = Pics(1, 12);
                int clicks = 0; bool flag = false;
                Control<bool> toggle = null;
                var boxes = new Dictionary<int, VisualElement>();
                var bad = Controls.ImageCards<Pic>(stage, o =>
                {
                    o.Items = () => nine; o.Key = p => p.Name;
                    o.Click = p => clicks++;
                    o.Body = (e, p) =>
                    {
                        boxes[p.N] = e;
                        if (p.N == 9) { Ui.Text(e, "").text = "half"; throw new InvalidOperationException("probe body"); }
                        Ui.Text(e, "").text = p.Name;
                        if (p.N == 1) toggle = Controls.Toggle(e, "Flag", Bind.To(() => flag, v => flag = v), c => c.Id = "cards-flag");
                    };
                });
                yield return null;
                bad.Redraw(nine[8]);
                int lines = logs.FindAll(m => m.Contains("ImageCards Body 'Item 9'")).Count;
                Check(boxes[9].childCount == 0 && boxes[8].childCount == 1 && boxes[10].childCount == 1 && lines == 1,
                    "ImageCards: a throwing Body leaves its card blank and logs one line; the others draw", boxes[9].childCount + " " + lines);

                var c1 = boxes[1].parent;
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = c1; c1.SendEvent(e); }
                yield return null;
                int afterSubmit = clicks;
                var input = toggle?.Input;
                if (input != null) ClickEl(input);
                yield return null;
                Check(afterSubmit == 1 && flag && clicks == 1, "ImageCards: Click runs once per Submit; a toggle inside Body toggles without calling Click",
                    afterSubmit + " " + flag + " " + clicks + " " + (input != null));
                bad.Root.RemoveFromHierarchy();
            }
            finally
            {
                s.Dispose();
                UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(tex2);
            }
        }

        // asked holds lo..hi, give or take one card at either end, each once.
        static bool Span(List<int> asked, int lo, int hi)
        {
            if (asked.Count == 0 || asked.Distinct().Count() != asked.Count) return false;
            int min = asked.Min(), max = asked.Max();
            return Math.Abs(min - lo) <= 1 && Math.Abs(max - hi) <= 1 && asked.Count == max - min + 1;
        }

        static string Show(List<int> l) => l.Count == 0 ? "none" : l.Min() + ".." + l.Max() + " (" + l.Count + ")";

        sealed class ReferenceEqualityComparer : IEqualityComparer<VisualElement>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public bool Equals(VisualElement a, VisualElement b) => ReferenceEquals(a, b);
            public int GetHashCode(VisualElement e) => e == null ? 0 : e.GetHashCode();
        }
    }
}
