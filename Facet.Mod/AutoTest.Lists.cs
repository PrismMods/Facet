using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // List parts a browser page needs: image cards, the scroll-end hook, the search list.
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

        // A column of n rows of 60 px.
        static VisualElement Rows(VisualElement parent, int n)
        {
            var l = new VisualElement { name = "end-list" };
            l.style.flexShrink = 0f;
            for (int i = 0; i < n; i++) { var r = new VisualElement(); r.style.height = 60f; r.style.flexShrink = 0f; l.Add(r); }
            if (parent != null) parent.Add(l);
            return l;
        }

        static ScrollView View600(VisualElement stage)
        {
            var sv = Ui.Scroll(stage);
            sv.style.height = 600f; sv.style.flexGrow = 0f;
            return sv;
        }

        IEnumerator SectionScrollEnd()
        {
            var h = Host.Create("End", o => o.Log = m => Main.Log("[end] " + m));
            var s = new Surface(h, "FacetEnd", Layer.Window);
            s.Visible = true;
            var w = new Window(s.Root, "end", "End", new Rect(60, 40, 1000, 760));
            var stage = new VisualElement { name = "end-stage" };
            stage.style.width = 860f; stage.style.flexShrink = 0f;
            w.Body.Add(stage);
            try
            {
                // 50 rows (3000 px) in a 600 px view, margin 400.
                var sv = View600(stage);
                var list = Rows(sv, 50);
                int n = 0;
                var hook = Ui.ScrollEnd(list, 400f, () => n++);
                yield return null; yield return null;
                sv.scrollOffset = new Vector2(0f, 1000f); yield return null;
                sv.scrollOffset = new Vector2(0f, 1999f); yield return null; yield return null;
                Check(n == 0, "ScrollEnd: the top 1999 px of 3000 in a 600 px view call nothing", n.ToString());
                sv.scrollOffset = new Vector2(0f, 2000f); yield return null; yield return null; yield return null;
                Check(n == 1, "ScrollEnd: scrolling to 2000 of 3000 calls once", n + " vp " + sv.contentViewport.layout.height.ToString("0.###")
                    + " end " + (list.worldBound.yMax - sv.contentContainer.worldBound.y).ToString("0.###") + " at " + sv.scrollOffset.y.ToString("0.###"));
                sv.scrollOffset = Vector2.zero; yield return null;
                sv.scrollOffset = new Vector2(0f, 2000f); yield return null; yield return null;
                Check(n == 2, "ScrollEnd: scrolling up and back down calls again", n.ToString());
                yield return null;
                sv.scrollOffset = new Vector2(0f, 2100f);
                sv.scrollOffset = new Vector2(0f, 2200f);
                Check(n == 3, "ScrollEnd: two scrolls at the end in one frame call once", n.ToString());
                hook.Dispose();
                yield return null;
                sv.scrollOffset = Vector2.zero; yield return null;
                sv.scrollOffset = new Vector2(0f, 2000f); yield return null; yield return null;
                Check(n == 3, "ScrollEnd: disposed, it calls nothing", n.ToString());
                sv.RemoveFromHierarchy();

                // Short content, detached, hidden.
                var sv2 = View600(stage);
                int shortN = 0, detN = 0, hidN = 0;
                Ui.ScrollEnd(Rows(sv2, 3), 400f, () => shortN++);
                var loose = Rows(null, 3);
                Ui.ScrollEnd(loose, 400f, () => detN++);
                var hidden = Rows(null, 3);
                hidden.style.display = DisplayStyle.None;
                Ui.ScrollEnd(hidden, 400f, () => hidN++);
                sv2.Add(hidden);
                yield return null; yield return null; yield return null;
                Check(shortN == 1, "ScrollEnd: a list shorter than the view calls once on attach", shortN.ToString());
                Check(detN == 0, "ScrollEnd: detached, nothing is called", detN.ToString());
                Check(hidN == 0, "ScrollEnd: hidden (display none), nothing is called", hidN.ToString());
                hidden.style.display = DisplayStyle.Flex;
                sv2.Add(loose);
                yield return null; yield return null; yield return null;
                Check(hidN == 1 && detN == 1, "ScrollEnd: shown or attached, a short list calls once", hidN + "/" + detN);
                sv2.RemoveFromHierarchy();
            }
            finally { s.Dispose(); }
        }

        IEnumerator SectionSearchList()
        {
            var h = Host.Create("Search", o => o.Log = m => Main.Log("[search] " + m));
            var s = new Surface(h, "FacetSearch", Layer.Window);
            s.Visible = true;
            var w = new Window(s.Root, "search", "Search", new Rect(60, 40, 1000, 760));
            var stage = new VisualElement { name = "search-stage" };
            stage.style.width = 860f; stage.style.flexShrink = 0f;
            w.Body.Add(stage);
            try
            {
                string q = ""; int writes = 0, retries = 0, extras = 0, loads = 0;
                var state = ListState.Loading; var more = MoreState.None;
                var sv = View600(stage);
                var sl = Controls.SearchList(sv, o =>
                {
                    o.Query = Bind.To(() => q, v => { q = v; writes++; });
                    o.Placeholder = "Find";
                    o.State = () => state; o.Message = () => "Message " + state;
                    o.RetryLabel = "Again"; o.Retry = () => retries++;
                    o.ErrorExtra = e => { extras++; Ui.Text(e, "").text = "Extra"; };
                    o.More = () => more; o.EndText = () => "The end";
                    o.LoadMore = () => { loads++; more = MoreState.Loading; };
                });
                Rows(sl.Body, 50);
                yield return null; yield return null;

                var f = sl.Root.Q<TextField>("search-list-field");
                f.value = "b"; f.value = "be";
                Check(writes == 2 && q == "be", "SearchList: typing be writes the query twice", writes + " " + q);
                f.value = new string('x', 200);
                Check(q.Length == 128 && f.value.Length == 128 && f.maxLength == 128 && !f.multiline,
                    "SearchList: MaxLength 128 cuts a 200-character paste; the field is single line", q.Length + "/" + f.value.Length);

                Func<string, bool> on = n => { var e = sl.Root.Q(n); return e != null && e.style.display != DisplayStyle.None; };
                var status = sl.Root.Q("search-list-status");
                var spin = status.Q("search-list-spinner");
                Button retry = status.Q<Button>();
                Func<string> shown = () => status.Query<Label>().ToList().Select(l => l.text).FirstOrDefault(t => t.StartsWith("Message")) ?? "";
                Func<bool> spinOn = () => spin.style.display != DisplayStyle.None;
                Func<bool> retryOn = () => retry.style.display != DisplayStyle.None;
                sl.Refresh();
                bool loading = on("search-list-status") && !on("search-list-body") && spinOn() && !retryOn() && shown() == "Message Loading";
                state = ListState.Empty; sl.Refresh();
                bool empty = on("search-list-status") && !on("search-list-body") && !spinOn() && !retryOn() && shown() == "Message Empty";
                state = ListState.Idle; sl.Refresh();
                bool idle = !on("search-list-status") && !on("search-list-body") && !on("search-list-footer");
                state = ListState.Loading; sl.Refresh();
                state = ListState.Ready; sl.Refresh();
                bool ready = !on("search-list-status") && on("search-list-body") && !spinOn();
                Check(loading && empty && idle && ready, "SearchList: each state shows only its own content; Loading to Ready shows Body in one refresh",
                    loading + "/" + empty + "/" + idle + "/" + ready);

                state = ListState.Error; sl.Refresh(); sl.Refresh();
                bool err = on("search-list-status") && !on("search-list-body") && retryOn() && !spinOn() && shown() == "Message Error"
                    && status.Q("search-list-extra").childCount == 1;
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = retry; retry.SendEvent(e); }
                yield return null;
                Check(err && retries == 1, "SearchList: Error shows the retry button; Submit calls Retry once", err + " " + retries);
                int once = extras;
                state = ListState.Ready; sl.Refresh();
                state = ListState.Error; sl.Refresh();
                Check(once == 1 && extras == 2, "SearchList: ErrorExtra builds once per entry into Error", once + " " + extras);
                state = ListState.Ready; sl.Refresh();
                yield return null; yield return null;

                // Paging: 50 rows (3000 px) under the search row in a 600 px view.
                Check(loads == 0, "SearchList: the top of a long Ready list does not page", loads.ToString());
                sv.scrollOffset = new Vector2(0f, 2400f); yield return null; yield return null;
                Check(loads == 1 && more == MoreState.Loading, "SearchList: scrolling near the end calls LoadMore once", loads.ToString());
                sv.scrollOffset = new Vector2(0f, 2300f); yield return null;
                sv.scrollOffset = new Vector2(0f, 2400f); yield return null;
                Check(loads == 1, "SearchList: while More is Loading, the end calls nothing", loads.ToString());
                sl.Refresh();
                var foot = sl.Root.Q("search-list-footer");
                bool footSpin = on("search-list-footer") && foot.Q("search-list-spinner").style.display != DisplayStyle.None;
                more = MoreState.Error; sl.Refresh();
                var footRetry = foot.Q<Button>();
                bool footErr = footRetry.style.display != DisplayStyle.None;
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = footRetry; footRetry.SendEvent(e); }
                yield return null;
                Check(footSpin && footErr && loads == 2, "SearchList: More Loading shows a spinner; More Error's retry (Submit) calls LoadMore", footSpin + "/" + footErr + "/" + loads);
                more = MoreState.End; sl.Refresh();
                var endL = foot.Query<Label>().ToList().FirstOrDefault(l => l.style.display != DisplayStyle.None && l.text == "The end");
                Check(endL != null && footRetry.style.display == DisplayStyle.None, "SearchList: More End shows EndText");

                more = MoreState.Error; sl.Refresh(); yield return null;
                int before = loads;
                more = MoreState.None; yield return null;
                sl.Refresh(); sl.Refresh();
                Check(loads == before + 1, "SearchList: at the end, More going Error to None pages on the next Refresh, once", (loads - before).ToString());

                // A detached build reads its states and calls nothing.
                int dLoads = 0;
                var loose = Controls.SearchList(null, o => { o.State = () => ListState.Ready; o.More = () => MoreState.None; o.LoadMore = () => dLoads++; });
                loose.Refresh();
                Check(dLoads == 0 && loose.Body.style.display != DisplayStyle.None, "SearchList: detached, Refresh calls nothing", dLoads.ToString());

                // The refresh pass runs on the surface's timer: past the first, it allocates nothing (heap growth, collector paused).
                string pq = "find"; var pState = ListState.Empty; var pMore = MoreState.End;
                var probe = Controls.SearchList(null, o =>
                {
                    o.Query = Bind.To(() => pq, v => pq = v);
                    o.State = () => pState; o.Message = () => "Nothing found";
                    o.More = () => pMore; o.EndText = () => "The end";
                });
                probe.Refresh(); pState = ListState.Ready; probe.Refresh();
                long pgrew, pjunk; object pkeep = null;
                var pmode = UnityEngine.Scripting.GarbageCollector.GCMode;
                try
                {
                    UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
                    long a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) pkeep = new byte[128];
                    pjunk = GC.GetTotalMemory(false) - a;
                    // Steady Empty (Message read), then steady Ready with End (EndText read); each state's entry is left out.
                    pState = ListState.Empty; probe.Refresh();
                    a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 1000; i++) probe.Refresh();
                    pgrew = GC.GetTotalMemory(false) - a;
                    pState = ListState.Ready; probe.Refresh();
                    a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 1000; i++) probe.Refresh();
                    pgrew += GC.GetTotalMemory(false) - a;
                }
                finally { UnityEngine.Scripting.GarbageCollector.GCMode = pmode; }
                GC.KeepAlive(pkeep);
                Main.Log("[perf] search list refresh: heap grew " + pgrew + " bytes over 2000 refreshes (2000 x 128-byte arrays grew it " + pjunk + ")");
                Check(pjunk > 0 && pgrew == 0, "SearchList: a refresh (Empty and Ready with End, query read) allocates nothing", pgrew + " bytes over 2000 refreshes");
                sv.RemoveFromHierarchy();
            }
            finally { s.Dispose(); }
        }

        // Form fields that keep their own Id, hear their commits and bind their undo; a range over named steps and a
        // drawn track; a list whose drags start only from an item's text.
        IEnumerator SectionFormFields()
        {
            var h = Host.Create("Fields", o => o.Log = m => Main.Log("[fields] " + m));
            var s = new Surface(h, "FacetFields", Layer.Window);
            s.Visible = true;
            var w = new Window(s.Root, "fields", "Fields", new Rect(60, 40, 1000, 760));
            var stage = Ui.Box(w.Body);
            stage.style.width = 900f;
            var root = w.Root;
            try
            {
                // Id
                var withId = Ui.Box(stage); var noId = Ui.Box(stage);
                bool on = false;
                Form.Build(withId, new[] { new Field { Label = "Alpha", Id = "ALPHA_ID", Kind = FieldKind.Bool, Get = () => on, Set = v => on = (bool)v } });
                Form.Build(noId, new[] { new Field { Label = "Alpha", Kind = FieldKind.Bool, Get = () => on, Set = v => on = (bool)v } });
                yield return null; yield return null;
                var idRow = withId.Q("ALPHA_ID");
                Check(idRow != null && withId.Q("Alpha") == null && idRow.Query<Label>().ToList().Exists(l => l.text == "Alpha") && noId.Q("Alpha") != null,
                    "Form: a field with Id ALPHA_ID builds a control named ALPHA_ID showing Alpha; with no Id it is named Alpha",
                    (idRow != null) + "/" + (noId.Q("Alpha") != null));
                withId.RemoveFromHierarchy(); noId.RemoveFromHierarchy();

                // Committed: once per drag, once more per undo.
                History.Clear();
                float amount = 0.2f; int sets = 0, commits = 0;
                var fbox = Ui.Box(stage);
                Form.Build(fbox, new[] { new Field { Label = "Amount", Id = "fields-amount", Kind = FieldKind.Float, Min = 0f, Max = 1f,
                    Get = () => amount, Set = v => { sets++; amount = (float)v; }, Committed = () => commits++ } });
                yield return null; yield return null;
                var dragger = fbox.Q("fields-amount-input")?.Q(className: "unity-base-slider__dragger");
                Check(dragger != null, "Form: the float field's slider has a dragger");
                if (dragger != null)
                {
                    var from = dragger.worldBound.center;
                    PointerAt(EventType.MouseDown, from, root); yield return null;
                    for (int i = 1; i <= 10; i++) { PointerAt(EventType.MouseMove, from + new Vector2(8f * i, 0f), root); yield return null; }
                    PointerAt(EventType.MouseUp, from + new Vector2(80f, 0f), root); yield return null; yield return null;
                }
                int dragSets = sets, dragCommits = commits;
                History.Undo(); yield return null;
                int undoCommits = commits;
                History.Redo(); yield return null;
                Check(dragSets >= 5 && dragCommits == 1 && undoCommits == 2 && commits == 3,
                    "Form: a slider drag calls Set every frame and Committed once; an undo and a redo call Committed once more each",
                    "sets " + dragSets + " commits " + dragCommits + "/" + undoCommits + "/" + commits);
                fbox.RemoveFromHierarchy();

                // UndoDocument: a step recorded on a document that was replaced does nothing.
                History.Clear();
                object doc = new object();
                string name = "a"; int nsets = 0, ncommits = 0;
                var nbox = Ui.Box(stage);
                Form.Build(nbox, new[] { new Field { Label = "Name", Id = "fields-name", Kind = FieldKind.Text,
                    Get = () => name, Set = v => { nsets++; name = (string)v; }, Committed = () => ncommits++, UndoDocument = () => doc } });
                yield return null; yield return null;
                var nf = nbox.Q<TextField>("fields-name-input");
                if (nf != null) { Type(nf, "b"); yield return null; }
                bool typed = name == "b" && nsets == 1 && ncommits == 1;
                History.Undo(); yield return null;
                Check(nf != null && typed && nsets == 2 && ncommits == 2 && name == "a",
                    "Form: with the same UndoDocument, an undo of a typed edit calls Set and Committed once each", typed + " sets " + nsets + " commits " + ncommits + " " + name);
                if (nf != null) { Type(nf, "c"); yield return null; }
                typed = name == "c" && nsets == 3 && ncommits == 3;
                doc = new object();
                History.Undo(); yield return null;
                Check(nf != null && typed && nsets == 3 && ncommits == 3 && name == "c",
                    "Form: with UndoDocument replaced after an edit, an undo calls neither Set nor Committed", typed + " sets " + nsets + " commits " + ncommits + " " + name);
                nbox.RemoveFromHierarchy();
                History.Clear();

                // Named steps over a drawn track.
                var names = new List<string>();
                for (int i = 1; i <= 8; i++) names.Add("Name " + i);
                var span = new Vector2(2f, 2f); int rcommits = 0, tracks = 0;
                VisualElement drawn = null;
                var range = Controls.Range(stage, "Low", "High", Bind.To(() => span, v => span = v), o =>
                {
                    o.Id = "fields-range"; o.Names = names;
                    o.Track = t => { tracks++; drawn = Ui.Box(t); drawn.name = "fields-drawn"; };
                });
                range.Committed += _ => rcommits++;
                yield return null; yield return null;
                var track = stage.Q("fields-range-track");
                var value = stage.Q<Label>("fields-range-value");
                if (track != null)
                {
                    var b = track.worldBound;
                    Func<float, Vector2> at = v => new Vector2(b.xMin + v / 7f * b.width, b.center.y);
                    yield return DragAt(at(2f), at(5f), root);
                }
                Check(span == new Vector2(2f, 5f) && value != null && value.text == "Name 3  —  Name 6" && rcommits == 1,
                    "Range: with Names a drag from 2 to 5 shows Name 3  —  Name 6 and commits once", span + " '" + value?.text + "' " + rcommits);
                Check(range.Root.Query<TextField>().ToList().Count == 0 && stage.Q("fields-range-input") == null && stage.Q("High-input") == null,
                    "Range: with Names the ends have no -input field");
                var bar = drawn?.parent;
                Check(tracks == 1 && bar != null && bar.parent == track && track.IndexOf(bar) == 0 && bar.IndexOf(drawn) == 0 && track.childCount == 3,
                    "Range: Track runs once and what it draws sits under the span and the thumbs",
                    tracks + " " + (bar == null ? "none" : track?.IndexOf(bar) + "/" + bar.IndexOf(drawn)));
                range.Root.RemoveFromHierarchy();

                // A drag starts only from an item's text row.
                var outer = new List<string> { "Alpha", "Beta", "Gamma" };
                var inner = new Dictionary<string, List<string>>();
                int outerMoves = 0, innerMoves = 0;
                float level = 0.5f;
                Action<List<string>, int, int> move = (l, a, z) => { var x = l[a]; l.RemoveAt(a); l.Insert(z, x); };
                var list = Controls.Reorder<string>(stage, o =>
                {
                    o.Id = "fields-outer"; o.Vertical = true; o.DragFromText = true;
                    o.Items = () => outer; o.Text = x => x;
                    o.Move = (a, z) => { outerMoves++; move(outer, a, z); };
                    o.Build = (e, x) =>
                    {
                        Controls.Slider(e, "Level", Bind.To(() => level, v => level = v), so => so.Id = "fields-level-" + x);
                        var mine = inner[x] = new List<string> { x + " 1", x + " 2", x + " 3" };
                        Controls.Reorder<string>(e, io =>
                        {
                            io.Id = "fields-inner-" + x; io.Vertical = true;
                            io.Items = () => mine; io.Text = y => y;
                            io.Move = (a, z) => { innerMoves++; move(mine, a, z); };
                        });
                    };
                });
                yield return null; yield return null; yield return null;
                History.Clear();
                var sd = stage.Q("fields-level-Alpha-input")?.Q(className: "unity-base-slider__dragger");
                if (sd != null) yield return DragAt(sd.worldBound.center, sd.worldBound.center + new Vector2(40f, 0f), root);
                Check(sd != null && outerMoves == 0 && string.Join(",", outer) == "Alpha,Beta,Gamma",
                    "Reorder DragFromText: a 40 px drag on a slider in an item calls no outer Move", (sd != null) + " " + outerMoves);
                var nested = stage.Query<VisualElement>("fields-inner-Beta-item").ToList();
                if (nested.Count == 3)
                    yield return DragAt(nested[0].worldBound.center, new Vector2(nested[2].worldBound.center.x, nested[2].worldBound.yMax - 2f), root);
                Check(nested.Count == 3 && innerMoves == 1 && outerMoves == 0 && string.Join(",", inner["Beta"]) == "Beta 2,Beta 3,Beta 1" && string.Join(",", outer) == "Alpha,Beta,Gamma",
                    "Reorder DragFromText: a drag in a nested list moves only its own items",
                    nested.Count + " " + innerMoves + "/" + outerMoves + " " + string.Join(",", inner.ContainsKey("Beta") ? inner["Beta"] : new List<string>()));
                var heads = stage.Query<VisualElement>("fields-outer-head").ToList();
                var faces = stage.Query<VisualElement>("fields-outer-item").ToList();
                if (heads.Count == 3 && faces.Count == 3)
                    yield return DragAt(heads[0].worldBound.center, new Vector2(heads[0].worldBound.center.x, faces[2].worldBound.yMax - 2f), root);
                Check(heads.Count == 3 && outerMoves == 1 && string.Join(",", outer) == "Beta,Gamma,Alpha",
                    "Reorder DragFromText: a drag on an item's text row calls the outer Move once", heads.Count + " " + outerMoves + " " + string.Join(",", outer));
                list.Root.RemoveFromHierarchy();
                History.Clear();
            }
            finally { s.Dispose(); }
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
