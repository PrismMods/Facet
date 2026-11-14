using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Sections for the delegate-only hooks, write-side composition and typed choices; Start runs them.
    sealed partial class AutoTest
    {
        // The newest open popup's content, or null.
        static VisualElement NewestPopup()
        {
            var open = (IList)typeof(Popup).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            return open.Count == 0 ? null : ((Popup)open[open.Count - 1]).Content;
        }

        static List<string> MenuTexts(VisualElement menu)
        {
            var l = new List<string>();
            if (menu != null) foreach (var x in menu.Query<Label>().ToList()) l.Add(x.text);
            return l;
        }

        static Label MenuLabel(VisualElement menu, string text) => menu?.Query<Label>().ToList().Find(x => x.text == text);

        // Waits at least `seconds` of real time and `frames` frames.
        static IEnumerator Wait(float seconds, int frames = 1)
        {
            float until = Time.realtimeSinceStartup + seconds;
            for (int i = 0; i < frames || Time.realtimeSinceStartup < until; i++) yield return null;
        }

        static void PointerMoveProbe(IDisposable pointer, out long grew, out long junk)
        {
            var mi = pointer.GetType().GetMethod("OnMove", BindingFlags.NonPublic | BindingFlags.Instance);
            var move = (Action<PointerMoveEvent>)Delegate.CreateDelegate(typeof(Action<PointerMoveEvent>), pointer, mi);
            object keep = null;
            var mode = GarbageCollector.GCMode;
            using (var ev = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = new Vector2(5f, 5f) }))
            {
                move(ev);   // the first move may build what later moves reuse
                try
                {
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    long a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) keep = new byte[128];
                    junk = GC.GetTotalMemory(false) - a;
                    a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) move(ev);
                    grew = GC.GetTotalMemory(false) - a;
                }
                finally { GarbageCollector.GCMode = mode; }
            }
            GC.KeepAlive(keep);
        }

        IEnumerator SectionHooks()
        {
            var s = new Surface(Gallery.Host, "FacetHooksProbe", Layer.Window);
            var w = new Window(s.Root, "hooksprobe", "Hooks", new Rect(60, 60, 700, 560));
            var root = s.Root;
            IDisposable pointer = null, every = null, bad = null;
            try
            {
                yield return null; yield return null;

                // Pointer: local positions, captured from press to release, nothing after dispose.
                var pad = Ui.Box(w.Body);
                pad.style.width = 200f; pad.style.height = 80f; pad.style.flexShrink = 0f;
                yield return null; yield return null;
                var log = new List<string>();
                pointer = Ui.Pointer(pad, (p, b) => log.Add("d" + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "/" + b),
                                          p => log.Add("m" + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y)),
                                          (p, b) => log.Add("u" + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "/" + b));
                var o = pad.worldBound.position;
                // The local position a panel point at o + d lands on (the window may be scaled).
                Func<float, float, string> at = (dx, dy) => { var l = pad.WorldToLocal(o + new Vector2(dx, dy)); return Mathf.RoundToInt(l.x) + "," + Mathf.RoundToInt(l.y); };
                PointerAt(EventType.MouseDown, o + new Vector2(10f, 20f), root);
                PointerAt(EventType.MouseMove, o + new Vector2(30f, 25f), root);
                PointerAt(EventType.MouseMove, o + new Vector2(300f, 40f), root);   // outside the pad
                PointerAt(EventType.MouseUp, o + new Vector2(310f, 45f), root);
                yield return null;
                string seq = string.Join(" ", log);
                Check(log.Count > 0 && log[0] == "d" + at(10f, 20f) + "/0", "Ui.Pointer reports a left press at its position local to the element", seq);
                Check(log.Contains("m" + at(30f, 25f)), "...a move over it", seq);
                Check(log.Contains("m" + at(300f, 40f)), "...a move outside it while pressed (captured)", seq);
                Check(log.Count > 0 && log[log.Count - 1] == "u" + at(310f, 45f) + "/0" && pad.panel.GetCapturingElement(PointerId.mousePointerId) == null,
                    "...and the release outside it, which ends the capture", seq);
                log.Clear();
                PointerAt(EventType.MouseDown, o + new Vector2(10f, 10f), root, 1);
                PointerAt(EventType.MouseUp, o + new Vector2(10f, 10f), root, 1);
                Check(string.Join(" ", log) == "d" + at(10f, 10f) + "/1 u" + at(10f, 10f) + "/1", "a right press and release report button 1", string.Join(" ", log));
                // A press still held when the element leaves its panel ends there: after re-attach a fresh press reports.
                log.Clear();
                string held = at(10f, 10f);
                PointerAt(EventType.MouseDown, o + new Vector2(10f, 10f), root);
                pad.RemoveFromHierarchy(); yield return null;
                Check(string.Join(" ", log) == "d" + held + "/0 u" + held + "/0", "Ui.Pointer: a press held as its element leaves the panel ends with one release at the press position", string.Join(" ", log));
                w.Body.Insert(0, pad); yield return null; yield return null;
                log.Clear();
                PointerAt(EventType.MouseDown, o + new Vector2(12f, 12f), root);
                PointerAt(EventType.MouseUp, o + new Vector2(12f, 12f), root);
                Check(log.Count == 2 && log[0].StartsWith("d") && log[1].StartsWith("u"), "Ui.Pointer: a press held when its element leaves the panel ends, so a press after re-attach reports",
                    string.Join(" ", log));
                log.Clear();
                pointer.Dispose(); pointer = null;

                // Bytes per move: the hook's own move handler with a counting consumer, heap growth with the collector paused.
                int moved = 0;
                pointer = Ui.Pointer(pad, (p, b) => { }, p => moved++, (p, b) => { });
                long grew, junk;
                PointerMoveProbe(pointer, out grew, out junk);
                pointer.Dispose(); pointer = null;
                Main.Log("[perf] Ui.Pointer move: heap grew " + grew + " bytes over 2000 moves (2000 x 128-byte arrays grew it " + junk + ")");
                Check(junk > 0 && moved == 2001 && grew == 0, "Ui.Pointer: a move allocates nothing (heap probe, 2000 moves)", grew + " B, " + moved + " moves, junk " + junk);
                PointerAt(EventType.MouseDown, o + new Vector2(10f, 10f), root);
                PointerAt(EventType.MouseMove, o + new Vector2(20f, 10f), root);
                PointerAt(EventType.MouseUp, o + new Vector2(20f, 10f), root);
                Check(log.Count == 0, "...and after Dispose nothing arrives", string.Join(" ", log));

                // Every: ticks while shown, never while the surface is hidden, again once shown.
                int ticks = 0;
                every = Ui.Every(pad, 50, () => ticks++);
                yield return Wait(0.4f, 10);
                Check(ticks >= 3, "Ui.Every(50) ticks while its element is shown", ticks.ToString());
                s.Visible = false;
                int hidden = ticks;
                yield return Wait(0.2f, 20);
                Check(ticks == hidden, "...0 times over 20 frames once its surface is hidden", (ticks - hidden).ToString());
                s.Visible = true;
                yield return Wait(0.3f, 5);
                Check(ticks > hidden, "...and again once shown", (ticks - hidden).ToString());
                every.Dispose(); every = null;
                int disposed = ticks;
                yield return Wait(0.2f, 5);
                Check(ticks == disposed, "...and not after Dispose", (ticks - disposed).ToString());
                int throws = 0;
                bad = Ui.Every(pad, 0, () => { throws++; throw new InvalidOperationException("probe"); });
                yield return Wait(0.1f, 10);
                Check(throws == 3, "an Every that throws three times in a row stops", throws.ToString());
                pad.RemoveFromHierarchy(); w.Body.Insert(0, pad);
                yield return Wait(0.1f, 10);
                Check(throws == 6, "...until its element is attached again", throws.ToString());
                bad.Dispose(); bad = null;

                // WhileAttached.
                int att = 0, det = 0;
                var probe = Ui.Box(w.Body);
                Ui.WhileAttached(probe, () => att++, () => det++);
                Check(att == 1 && det == 0, "Ui.WhileAttached runs attached at once for an attached element", att + "/" + det);
                probe.RemoveFromHierarchy(); yield return null;
                Check(att == 1 && det == 1, "...detached when it leaves the panel", att + "/" + det);
                w.Body.Add(probe); yield return null;
                Check(att == 2 && det == 1, "...and attached again when it joins one", att + "/" + det);
                probe.RemoveFromHierarchy();

                // Search and TextOptions.Live: every keystroke; the bind is still written on Enter.
                var searched = new List<string>();
                var sf = Ui.Search(w.Body, searched.Add, "Find a box");
                sf.value = "a"; sf.value = "ab"; sf.value = "abc";
                Check(string.Join(",", searched) == "a,ab,abc", "Ui.Search: typing abc calls changed 3 times", string.Join(",", searched));
                var ph = typeof(ITextEdition).GetProperty("placeholder");
                Check(ph == null || (string)ph.GetValue(sf.textEdition) == "Find a box", "...and shows its placeholder", ph == null ? "" : (string)ph.GetValue(sf.textEdition));
                var lived = new List<string>();
                string named = "";
                var text = Controls.Text(w.Body, "Name", Bind.To(() => named, v => named = v), x => { x.Id = "hk-text"; x.Live = lived.Add; x.Placeholder = "Type a name"; });
                var tf = text.Input as TextField;
                tf.value = "a"; tf.value = "ab"; tf.value = "abc";
                Check(string.Join(",", lived) == "a,ab,abc" && named == "", "TextOptions.Live: typing abc calls Live 3 times and writes nothing yet", string.Join(",", lived) + " / " + named);
                Key(tf, KeyCode.Return);
                Check(named == "abc", "...and Enter still commits", named);
                Check(ph == null || (string)ph.GetValue(tf.textEdition) == "Type a name", "...with its placeholder", ph == null ? "" : (string)ph.GetValue(tf.textEdition));

                // Typed choices.
                string pick = "a";
                var dd = Controls.Dropdown(w.Body, "Pick", Bind.To(() => pick, v => pick = v), new[] { "a", "b" }, null, x => x.Id = "hk-dd");
                var ddb = dd.Input as Button;
                Check(ddb != null && ddb.text == "a", "Dropdown<string> shows the bound value", ddb?.text);
                Submit(ddb); yield return null; yield return null;
                var menu = NewestPopup();
                var bRow = MenuLabel(menu, "b")?.parent;
                if (bRow != null) { Release(bRow); yield return null; yield return null; }
                Check(pick == "b" && ddb.text == "b", "...and writes \"b\" when b is picked", pick + " / " + ddb?.text);
                Check(History.NextUndo == "Pick", "...as one undo step under its label", History.NextUndo);
                Popup.CloseAll();

                var mono = Fonts.Mono(Gallery.Host);
                var faced = Controls.Dropdown(w.Body, "Face", Bind.Value("a"), new[] { "a", "b", "c" }, null,
                    x => x.ItemFace = v => v == "b" ? mono : v == "c" ? throw new InvalidOperationException("probe face") : null);
                Submit(faced.Input as Button); yield return null; yield return null;
                menu = NewestPopup();
                var la = MenuLabel(menu, "a"); var lb = MenuLabel(menu, "b"); var lc = MenuLabel(menu, "c");
                Check(mono != null && lb != null && lb.style.unityFontDefinition.value.fontAsset == mono, "ItemFace draws the b item in the mono face");
                Check(la != null && lc != null && la.style.unityFontDefinition.value.fontAsset == null && lc.style.unityFontDefinition.value.fontAsset == null,
                    "...and a null or throwing face draws the default");
                Popup.CloseAll();

                int seg = 20;
                var ch = Controls.Choice(w.Body, "Size", Bind.To(() => seg, v => seg = v), new[] { 10, 20 }, v => v + " px", x => x.Id = "hk-choice");
                var segs = ch.Root.Query<Button>().ToList();
                Check(segs.Count == 2 && !Selected(segs[0]) && Selected(segs[1]) && segs[1].text == "20 px", "Choice<int> over 10 and 20 selects 20 for a bind of 20",
                    segs.Count + " " + (segs.Count == 2 ? segs[1].text : ""));
                seg = 30; ch.Refresh();
                Check(segs.Count == 2 && !segs.Exists(Selected), "...and nothing for 30");
                if (segs.Count == 2) Submit(segs[0]);
                Check(seg == 10 && Selected(segs[0]), "...and a click writes 10", seg.ToString());

                var layer = Layer.Window;
                var en = Controls.Enum(w.Body, "Layer", Bind.To(() => layer, v => layer = v), x => x.Id = "hk-enum");
                Submit(en.Input as Button); yield return null; yield return null;
                var names = MenuTexts(NewestPopup());
                Check(string.Join(",", names) == string.Join(",", Enum.GetNames(typeof(Layer))), "Enum<Layer> lists every Layer", string.Join(",", names));
                var modal = MenuLabel(NewestPopup(), "Modal")?.parent;
                if (modal != null) { Release(modal); yield return null; yield return null; }
                Check(layer == Layer.Modal && (en.Input as Button).text == "Modal", "...and picking Modal writes it", layer.ToString());
                Popup.CloseAll();

                var items = new List<string[]> { new[] { "a" }, new[] { "b" } };
                var mixed = Controls.Dropdown(w.Body, "Mixed pick", Bind.Many(() => items, x => x[0], (x, v) => x[0] = v), new[] { "a", "b" }, null);
                Check((mixed.Input as Button).text == "Mixed" && mixed.Mixed, "a typed Dropdown over a mixed Many bind reads Mixed", (mixed.Input as Button).text);
                Submit(mixed.Input as Button); yield return null; yield return null;
                var marked = MenuTexts(NewestPopup()).FindAll(t => { var l = MenuLabel(NewestPopup(), t); return l != null && Near(l.resolvedStyle.color, Theme.For(l)[Role.Accent]); });
                Check(marked.Count == 0, "...and opens with no item marked", string.Join(",", marked));
                Popup.CloseAll();

                // AfterChange: once per click, once per finished drag, once per undo.
                History.Clear();
                var card = Controls.Card(w.Body, "After");
                int after = 0, changed = 0;
                Controls.AfterChange(card, () => after++);
                bool flip = false; float amount = 0.2f;
                var tog = Controls.Toggle(card, "Flip", Bind.To(() => flip, v => flip = v), x => x.Id = "hk-flip");
                var sl = Controls.Slider(card, "Amount", Bind.To(() => amount, v => amount = v), x => x.Id = "hk-amount");
                sl.Changed += _ => changed++;
                yield return null; yield return null;
                ClickEl(tog.Input); yield return null;
                Check(flip && after == 1, "AfterChange runs once for a click on a row in the card", after.ToString());
                var dragger = sl.Input.Q(className: "unity-base-slider__dragger");
                Check(dragger != null, "the card's slider has a dragger");
                if (dragger != null)
                {
                    var from = dragger.worldBound.center;
                    PointerAt(EventType.MouseDown, from, root); yield return null;
                    for (int i = 1; i <= 10; i++) { PointerAt(EventType.MouseMove, from + new Vector2(8f * i, 0f), root); yield return null; }
                    PointerAt(EventType.MouseUp, from + new Vector2(80f, 0f), root); yield return null; yield return null;
                }
                Check(changed >= 5 && after == 2 && amount > 0.2f, "...once for a 10-frame slider drag (not per frame)", "changed " + changed + " after " + after + " amount " + amount);
                History.Undo(); yield return null;
                Check(after == 3 && Mathf.Abs(amount - 0.2f) < 1e-4f, "...and once for an undo of that drag", after + " / " + amount);
                History.Redo(); yield return null;
                Check(after == 4, "...and once for its redo", after.ToString());

                // Immediate refresh: a dependent row follows in the frame its toggle writes; one pass per frame.
                bool show = false;
                var dep = Controls.Info(w.Body, "Dependent", () => "shown", x => { x.Id = "hk-dep"; x.VisibleWhen = () => show; });
                var shower = Controls.Toggle(w.Body, "Show dependent", Bind.To(() => show, v => show = v), x => x.Id = "hk-show");
                yield return null; yield return null;
                Check(dep.Root.style.display == DisplayStyle.None, "a row with VisibleWhen false starts hidden");
                int frame = Time.frameCount;
                ClickEl(shower.Input);
                Check(show && dep.Root.style.display == DisplayStyle.Flex && Time.frameCount == frame, "...and shows in the same frame its toggle writes the flag");
                yield return null;
                var refresher = Refresher.Of(shower.Root);
                int passes = refresher == null ? -1 : refresher.Passes;
                frame = Time.frameCount;
                shower.Set(false); shower.Set(true);
                Check(refresher != null && Time.frameCount == frame && refresher.Passes - passes == 1, "two writes in one frame give one refresh pass",
                    refresher == null ? "no refresher" : (refresher.Passes - passes).ToString());
                yield return null;
                var act = Controls.Action(w.Body, "Hide it", "Hide", () => show = false);
                yield return null;
                Submit(act);
                Check(!show && dep.Root.style.display == DisplayStyle.None, "an Action click refreshes its surface in the same frame");

                // One pass at the end of the outermost write: every write inside it shows in the same frame.
                bool fa = false, fb = false;
                var depA = Controls.Info(w.Body, "Dep A", () => "a", x => { x.Id = "hk-depa"; x.VisibleWhen = () => fa; });
                var depB = Controls.Info(w.Body, "Dep B", () => "b", x => { x.Id = "hk-depb"; x.VisibleWhen = () => fb; });
                var rowA = Controls.Toggle(w.Body, "Flag A", Bind.To(() => fa, v => fa = v));
                var rowB = Controls.Toggle(w.Body, "Flag B", Bind.To(() => fb, v => fb = v));
                var bothAct = Controls.Action(w.Body, "Both", "Show both", () => { rowA.Set(true); rowB.Set(true); });
                yield return null; yield return null;
                Submit(bothAct);
                Check(fa && fb && depA.Root.style.display == DisplayStyle.Flex && depB.Root.style.display == DisplayStyle.Flex,
                    "an Action that writes two rows shows both dependent rows in the same frame", depA.Root.style.display.value + "/" + depB.Root.style.display.value);
                bool fc = false, ob = false, other = false;
                var outerCard = Controls.Card(w.Body, "Outer");
                Controls.AfterChange(outerCard, () => fc = true);
                var depC = Controls.Info(w.Body, "Dep C", () => "c", x => { x.Id = "hk-depc"; x.VisibleWhen = () => fc; });
                var otherRow = Controls.Toggle(w.Body, "Other", Bind.To(() => other, v => other = v));
                var outerRow = Controls.Toggle(outerCard, "Outer row", Bind.To(() => ob, v => ob = v));
                outerRow.Committed += _ => otherRow.Set(true);
                yield return null; yield return null;
                outerRow.Set(true);
                Check(other && fc && depC.Root.style.display == DisplayStyle.Flex,
                    "a Committed handler writing another row still shows the outer row's AfterChange effect in the same frame", depC.Root.style.display.value.ToString());
                bool arm = false, inner = false;
                var innerRow = Controls.Toggle(w.Body, "Inner", Bind.To(() => inner, v => inner = v));
                Controls.Info(w.Body, "Writes in a pass", () => { if (arm) { arm = false; innerRow.Set(!inner); } return "x"; });
                yield return null; yield return null;
                var pr = Refresher.Of(innerRow.Root);
                int p0 = pr == null ? 0 : pr.Passes;
                arm = true;
                pr?.Now();
                Check(pr != null && !arm && pr.Passes - p0 == 1, "a row written from inside a refresh pass starts no second pass",
                    pr == null ? "no refresher" : (pr.Passes - p0) + " passes");

                // Typed rows keep the values they were built with: a later change to the list cannot misname one.
                var snapList = new List<string> { "a", "b", "c" };
                string snap = "b";
                var snapDd = Controls.Dropdown(w.Body, "Snapshot", Bind.To(() => snap, v => snap = v), snapList, null);
                snapList.RemoveAt(0); snap = "c"; snapDd.Refresh();
                Check((snapDd.Input as Button).text == "c", "a typed Dropdown still names its value after the caller's list changes", (snapDd.Input as Button).text);

                // The Gallery's API page builds.
                GalleryEditor.Model = new EditorModel();
                var ed = GalleryEditor.Open(); yield return null; yield return null;
                GalleryEditor.Nav.Go(GalleryEditor.ApiItem); yield return null; yield return null;
                Check(ed.Body.Q("api-search") != null && ed.Body.Q("api-layer") != null && ed.Body.Q("api-pad") != null, "the Editor's API page shows its search, enum row and pointer pad");
            }
            finally
            {
                pointer?.Dispose(); every?.Dispose(); bad?.Dispose();
                Popup.CloseAll();
                s.Dispose();
                GalleryEditor.Close(); GalleryEditor.Model = new EditorModel();
                History.Clear();
            }
        }
        // Labels with this text anywhere under e.
        static int LabelsWith(VisualElement e, string text) => e.Query<Label>().Where(l => l.text == text).ToList().Count;

        static bool Close(Rect a, Rect b, float px) =>
            Mathf.Abs(a.x - b.x) <= px && Mathf.Abs(a.y - b.y) <= px && Mathf.Abs(a.width - b.width) <= px && Mathf.Abs(a.height - b.height) <= px;

        static Rect CentredIn(Vector2 panel, float f) => new Rect(panel.x * (1f - f) / 2f, panel.y * (1f - f) / 2f, panel.x * f, panel.y * f);

        IEnumerator SectionRegions()
        {
            var s = new Surface(Gallery.Host, "FacetRegionsProbe", Layer.Window);
            var w = new Window(s.Root, "regionsprobe", "Regions", new Rect(60, 60, 700, 560));
            var store = new Dictionary<string, string>();
            var writes = new List<string>();
            var swHost = Host.Create("FacetSettingsWindowProbe", o =>
            {
                o.Log = m => Main.Log("[settingswindow] " + m);
                o.Read = k => { string v; return store.TryGetValue(k, out v) ? v : null; };
                o.Write = (k, v) => { writes.Add(k); if (v == null) store.Remove(k); else store[k] = v; };
            });
            Surface ls = null, other = null;
            SettingsWindow sw = null, hid = null;
            try
            {
                yield return null; yield return null;

                // When: its rows follow the flag on a refresh.
                bool shown = true;
                var when = Controls.When(w.Body, () => shown);
                Controls.Toggle(when, "Inside", Bind.Value(false));
                s.Refresh();
                bool wasShown = when.style.display != DisplayStyle.None;
                shown = false; s.Refresh();
                bool hidden = when.style.display == DisplayStyle.None;
                shown = true; s.Refresh();
                Check(wasShown && hidden && when.style.display != DisplayStyle.None, "When's rows hide on a refresh after the flag goes false, and show again after it goes true",
                    wasShown + " " + hidden);

                // ToggleGroup with its switch handed back.
                Control<bool> sw1;
                var gb = Controls.ToggleGroup(w.Body, "Group", Bind.Value(true), out sw1);
                bool open1 = gb.style.display != DisplayStyle.None;
                sw1.Set(false);
                Check(sw1 != null && sw1.Root.parent == w.Body && open1 && gb.style.display == DisplayStyle.None,
                    "ToggleGroup's out toggle is the group's switch, and the body hides when it is set off");

                // Section options.
                bool secShown = true;
                var folded = Controls.Section(w.Body, "Folded probe", o => { o.Id = "rg-folded"; o.Open = false; });
                Check(folded.style.display == DisplayStyle.None && s.Root.Q("rg-folded") != null, "a Section with Open = false starts folded, its header named by Id");
                var sec = Controls.Section(w.Body, "Shown probe", o => { o.Id = "rg-shown"; o.VisibleWhen = () => secShown; });
                var head = s.Root.Q("rg-shown");
                bool both = head != null && head.style.display != DisplayStyle.None && sec.style.display != DisplayStyle.None;
                secShown = false; s.Refresh();
                Check(both && head.style.display == DisplayStyle.None && sec.style.display == DisplayStyle.None,
                    "a Section's VisibleWhen false hides its header and its body");
                Data.Find<Controls.Fold>(sec).Set(true, false);   // what a search hit inside it does
                Check(sec.style.display == DisplayStyle.None, "a search reveal inside a hidden Section leaves its body hidden with its header");
                secShown = true; s.Refresh();
                Check(head.style.display != DisplayStyle.None && sec.style.display != DisplayStyle.None, "...and true shows both again");

                // Hook on a plain element.
                var plain = new Label("Plain");
                w.Body.Add(plain);
                Controls.Hook(plain, o => o.Tooltip = "Probe tip");
                Check(plain.tooltip == Words.T("Probe tip", Gallery.Host), "Hook puts a Tooltip on a plain Label", plain.tooltip);

                // Label ids: two "Speed" rows on a page under a host that opts in, then under one that does not.
                Control<float> a = null, b = null, num = null, gap = null;
                ls = new Surface(Host.Create("FacetLabelIdsProbe", o => { o.Log = m => Main.Log("[labelids] " + m); o.LabelIds = true; }), "FacetLabelIdsProbe", Layer.Window);
                var lw = new Window(ls.Root, "labelidsprobe", "Label ids", new Rect(80, 80, 600, 400));
                Controls.Labels = null;   // nothing counted before the page
                new Nav(lw.Body, new List<NavItem> { new NavItem("Speed", p => { a = Controls.Slider(p, "Speed", Bind.Value(0f)); b = Controls.Slider(p, "Speed", Bind.Value(0f)); num = Controls.Number(p, "Width", Bind.Value(0f));
                    Controls.Slider(p, "Spacing", Bind.Value(0f), o => o.Id = "Gap"); gap = Controls.Slider(p, "Gap", Bind.Value(0f)); }) });
                History.Clear();
                a.Set(0.5f);
                string k1 = History.TopKey;
                b.Set(0.5f);
                string k2 = History.TopKey;
                Check(k1 != null && k1.EndsWith("/Speed#Speed") && k2 != null && k2.EndsWith("/Speed#Speed#2"), "LabelIds: two Speed rows on a page record under .../Speed#Speed, then .../Speed#Speed#2",
                    k1 + " | " + k2);
                Check(a.Root.name == "Speed" && b.Root.name == "Speed#2", "...and the rows are named by them", a.Root.name + " " + b.Root.name);
                num.Set(5f);
                string kn = History.TopKey ?? "";
                Check(kn.EndsWith("/Width#Width") && num.Root.name == "Width", "LabelIds: a Number row records under .../Width#Width and is named Width", kn + " " + num.Root.name);
                Check(gap.Root.name == "Gap#2", "LabelIds: a Gap row after a row given Id Gap takes Gap#2", gap.Root.name);
                var popupRow = Controls.Slider(lw.Root, "Speed", Bind.Value(0f));   // outside the Nav, as a popup's row
                Check(popupRow.Root.name == "Speed", "LabelIds: a Speed row built after the page, outside any Nav, keeps its plain id", popupRow.Root.name);
                Control<float> c = null;
                var plainNav = new Nav(w.Body, new List<NavItem> { new NavItem("Speed", p => { Controls.Slider(p, "Speed", Bind.Value(0f)); c = Controls.Slider(p, "Speed", Bind.Value(0f)); }) });
                c.Set(0.5f);
                string k3 = History.TopKey ?? "";
                Check(k3.EndsWith("/Speed#2") && c.Root.name != "Speed#2", "without LabelIds an unnamed row keeps its numbered key", k3);
                History.Clear();

                // SettingsWindow: placed on the first visible layout, raised by a press, searched, inset by the Nav.
                Func<IList<NavItem>> pages = () => new List<NavItem> {
                    new NavItem("Alpha", p => Controls.Toggle(p, "Gizmo", Bind.Value(false))),
                    new NavItem("Beta", p => Controls.Toggle(p, "Widget", Bind.Value(false))),
                    new NavItem("Gamma", p => Controls.Toggle(p, "Sprocket", Bind.Value(false))) };
                sw = SettingsWindow.Open(swHost, "swprobe", "Probe", pages());
                Check(!sw.Window.HasStoredGeometry, "a first SettingsWindow.Open has no stored geometry");
                yield return Wait(0.1f, 3);
                var panel = sw.Surface.Root.layout.size;
                var placed = sw.Window.Rect;
                Check(Close(placed, CentredIn(panel, 0.8f), 2f), "SettingsWindow.Open shows its window at 80% of the panel, centred (2 px)", placed + " in " + panel);
                Check(store.ContainsKey("window.swprobe"), "...and stores that rect once shown");
                Check(sw.Nav.Inset == 207f && Mathf.Abs(sw.Window.TitleBarLeft.style.minWidth.value.value - sw.Nav.Inset) < 0.01f,
                    "the content inset is Nav.Inset (207 with the default rail): the header lines up with it", sw.Nav.Inset + " / " + sw.Window.TitleBarLeft.style.minWidth.value.value);

                other = new Surface(Gallery.Host, "FacetRegionsOther", Layer.Window);
                new Window(other.Root, "regionsother", "Other", new Rect(100, 100, 300, 200));
                yield return null;
                bool under = Ui.Stack.IndexOf(other) > Ui.Stack.IndexOf(sw.Surface);
                var at = sw.Window.Footer.worldBound.center;   // no control there: the press is the window's alone
                PointerAt(EventType.MouseDown, at, sw.Surface.Root);
                PointerAt(EventType.MouseUp, at, sw.Surface.Root);
                Check(under && Ui.Stack.IndexOf(sw.Surface) > Ui.Stack.IndexOf(other), "a press on the settings window raises it over a later window");
                other.BringToFront();
                bool mine = false;
                sw.Window.Raised += () => mine = true;   // a consumer's own raise behaviour
                PointerAt(EventType.MouseDown, at, sw.Surface.Root);
                PointerAt(EventType.MouseUp, at, sw.Surface.Root);
                Check(mine && Ui.Stack.IndexOf(sw.Surface) > Ui.Stack.IndexOf(other), "with a consumer's Raised handler added, a press runs it and still raises the settings window");

                bool before = LabelsWith(sw.Window.Body, "Gizmo") > 0;
                sw.Window.Search.value = "Widget";
                yield return null;
                Check(before && LabelsWith(sw.Window.Body, "Gizmo") == 0 && LabelsWith(sw.Window.Body, "Widget") > 0,
                    "typing in its search filters through the Nav: the open page gives way to the hit");
                sw.Window.Search.value = "";

                var stored = sw.Window.Rect;
                sw.Dispose(); sw = null;
                sw = SettingsWindow.Open(swHost, "swprobe", "Probe", pages());
                yield return Wait(0.1f, 3);
                Check(sw.Window.HasStoredGeometry && Close(sw.Window.Rect, stored, 0.5f), "a reopen restores the stored rect, and HasStoredGeometry is true", sw.Window.Rect + " vs " + stored);
                sw.Dispose(); sw = null;

                // A rect set right after Open, before the first layout, is where it opens.
                var given = new Rect(50f, 60f, 500f, 400f);
                sw = SettingsWindow.Open(swHost, "swrect", "Rect", pages());
                sw.Window.Rect = given;
                yield return Wait(0.1f, 3);
                Check(Close(sw.Window.Rect, given, 0.5f) && store.ContainsKey("window.swrect"), "a Rect set right after Open is kept by the first placement and stored", sw.Window.Rect.ToString());
                sw.Dispose(); sw = null;

                // Rail width.
                sw = SettingsWindow.Open(swHost, "swrail", "Rail", pages(), o => o.RailWidth = 160f);
                yield return Wait(0.1f, 3);
                var rail = sw.Window.Body.Q<ScrollView>();
                Check(sw.Nav.RailOpen && !sw.Nav.Compact && sw.Nav.Inset == 167f && rail != null && Mathf.Abs(rail.layout.width - 160f) <= 1f,
                    "SettingsWindow with RailWidth 160: Nav.Inset is 167 and the rail is 160 wide (1 px)", sw.Nav.Inset + " / " + (rail == null ? -1f : rail.layout.width));
                sw.Dispose(); sw = null;

                // Built hidden: nothing stored until it is shown; the first show centres it once.
                writes.Clear();
                hid = SettingsWindow.Open(swHost, "swhidden", "Hidden", pages());
                hid.Surface.Visible = false;
                yield return Wait(0.2f, 6);
                Check(!writes.Contains("window.swhidden") && !store.ContainsKey("window.swhidden") && !hid.Window.HasStoredGeometry,
                    "a SettingsWindow hidden in the frame it opened writes nothing to window.<id>", string.Join(",", writes.ToArray()));
                hid.Surface.Visible = true;
                yield return Wait(0.1f, 3);
                var first = hid.Window.Rect;
                hid.Surface.Visible = false; yield return null;
                hid.Surface.Visible = true; yield return Wait(0.1f, 3);
                int n = writes.FindAll(x => x == "window.swhidden").Count;
                Check(Close(first, CentredIn(hid.Surface.Root.layout.size, 0.8f), 2f) && n == 1 && hid.Window.Rect == first,
                    "...its first show centres it and stores the rect once; a second show leaves it", first + " writes " + n);
            }
            finally
            {
                if (sw != null) sw.Dispose();
                if (hid != null) hid.Dispose();
                if (ls != null) ls.Dispose();
                if (other != null) other.Dispose();
                s.Dispose();
                History.Clear();
            }
        }
        static bool HasText(VisualElement root, string text) => root != null && root.Query<TextElement>().ToList().Exists(t => t.text == text);

        // Theme.SetAccent, a host's surfaces relabelled and disposed together, BCP-47 languages, live preview notes,
        // a text length limit, and a host answering Facet's own words under Facet: keys.
        /* Heap growth over 100000 Language reads with the collector paused; `junk` is 100000 boxed ints' growth, about
           the size of one short string. Many reads: partly free blocks take a few thousand small objects unseen. */
        static void LanguageProbe(Host h, out long grew, out long junk)
        {
            object keep = null;
            string l = null;
            var mode = GarbageCollector.GCMode;
            l = h.Language;   // the first read may reduce the tag
            try
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                long a = GC.GetTotalMemory(false);
                for (int i = 0; i < 100000; i++) keep = i;
                junk = GC.GetTotalMemory(false) - a;
                a = GC.GetTotalMemory(false);
                for (int i = 0; i < 100000; i++) l = h.Language;
                grew = GC.GetTotalMemory(false) - a;
            }
            finally { GarbageCollector.GCMode = mode; }
            GC.KeepAlive(keep); GC.KeepAlive(l);
        }

        IEnumerator SectionConveniences()
        {
            Surface a = null, b = null, c = null;
            SettingsWindow sw = null;
            try
            {
                // SetAccent on a theme with its own radius and panel colour.
                var th = new Theme();
                th.Sizes.Radius = 11f;
                var panel = new Color(0.2f, 0.3f, 0.1f, 1f);
                th[Role.Panel] = panel;
                var accHost = Host.Create("FacetAccentProbe", o => { o.Theme = th; o.Log = m => Main.Log("[accent] " + m); });
                a = new Surface(accHost, "FacetAccentProbe", Layer.Window);
                var aw = new Window(a.Root, "accentprobe", "Accent", new Rect(60, 60, 400, 300));
                var dot = Ui.Box(aw.Body);
                Paint.Bg(dot, Role.Accent);
                yield return null;
                var red = new Color(0.9f, 0.2f, 0.2f, 1f);
                th.SetAccent(red);
                var want = Theme.FromAccent(red)[Role.AccentText];
                Check(th[Role.Accent] == red && th[Role.Focus] == red && th[Role.AccentText] == want,
                    "SetAccent sets Accent and Focus to the colour and AccentText to its contrast", th[Role.Accent] + " " + th[Role.Focus] + " " + th[Role.AccentText]);
                Check(th[Role.Panel] == panel && th.Sizes.Radius == 11f, "...keeps the other roles and the sizes", th[Role.Panel] + " r=" + th.Sizes.Radius);
                Check(Near(dot.style.backgroundColor.value, red), "...and re-paints an open window", dot.style.backgroundColor.value.ToString());
                a.Dispose(); a = null;

                // RelabelSurfaces and DisposeSurfaces: every surface of one host, its SettingsWindow's included.
                string lang = "en";
                var relHost = Host.Create("FacetRelabelProbe", o =>
                {
                    o.Language = () => lang;
                    o.Translate = k => lang == "ko" && k == "Alpha" ? "\uC54C\uD30C" : null;
                    o.Log = m => Main.Log("[relabel] " + m);
                });
                b = new Surface(relHost, "FacetRelabelA", Layer.Window);
                c = new Surface(relHost, "FacetRelabelB", Layer.Window);
                var lb = Ui.Text(b.Root, "Alpha");
                var lc = Ui.Text(c.Root, "Alpha");
                sw = SettingsWindow.Open(relHost, "cvrelabel", "Relabel", new List<NavItem> { new NavItem("Alpha", p => Controls.Toggle(p, "Gizmo", Bind.Value(false))) });
                yield return null; yield return null;
                bool before = lb.text == "Alpha" && lc.text == "Alpha" && HasText(sw.Surface.Root, "Alpha");
                lang = "ko";
                relHost.RelabelSurfaces();
                Check(before && lb.text == "\uC54C\uD30C" && lc.text == "\uC54C\uD30C", "RelabelSurfaces relabels two surfaces of one host after a language change", lb.text + " / " + lc.text);
                Check(HasText(sw.Surface.Root, "\uC54C\uD30C"), "...and its SettingsWindow's surface");
                var swSurface = sw.Surface;
                // A surface that throws while disposing (here a shell with no host) must not stop the rest.
                var bogus = (Surface)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Surface));
                var bogusSettings = ScriptableObject.CreateInstance<PanelSettings>();
                typeof(Surface).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(bogus, bogusSettings);
                relHost.Surfaces.Insert(0, bogus);
                string threw = null;
                try { relHost.DisposeSurfaces(); } catch (Exception e) { threw = e.GetType().Name; }
                relHost.Surfaces.Remove(bogus);
                UnityEngine.Object.Destroy(bogusSettings);
                Check(threw == null, "DisposeSurfaces carries on past a surface that throws", threw);
                Check(b.Root == null && c.Root == null && swSurface.Root == null, "DisposeSurfaces disposes all three");
                Check(Host.Named("FacetRelabelProbe") == relHost, "...and Host.Named still returns the host");
                b = c = null; sw = null;

                // A BCP-47 language tag.
                var koHost = Host.Create("FacetLanguageProbe", o => { o.Language = () => "ko-KR"; o.Log = m => Main.Log("[lang] " + m); });
                a = new Surface(koHost, "FacetLanguageProbe", Layer.Window);
                var kl = Ui.Muted(a.Root, "Search", own: true);
                yield return null;
                Check(koHost.Language == "ko" && kl.text == "\uAC80\uC0C9", "a host whose language is ko-KR reads as ko and shows Facet's Korean", koHost.Language + " / " + kl.text);
                var face = Fonts.Regular(koHost);
                var used = kl.resolvedStyle.unityFontDefinition.fontAsset;
                Check(used != null && used == face && Fonts.Has(used, kl.text), "...drawn in its CJK face", (used == null ? "null" : used.faceInfo.familyName) + " / " + (face == null ? "null" : face.faceInfo.familyName));
                long lgrew, ljunk;
                LanguageProbe(koHost, out lgrew, out ljunk);
                Main.Log("[perf] full-tag host Language: heap grew " + lgrew + " bytes over 100000 reads (100000 boxed ints grew it " + ljunk + ")");
                Check(ljunk > 0 && lgrew == 0, "reading Language from a host that answers ko-KR allocates nothing", lgrew + " bytes over 100000 reads");
                a.Dispose(); a = null;

                // NoteText and MaxLength.
                a = new Surface(Gallery.Host, "FacetConveniencesProbe", Layer.Window);
                var w = new Window(a.Root, "convprobe", "Conveniences", new Rect(60, 60, 600, 500));
                string note = "One";
                Controls.Preview(w.Body, "Stage", o => { o.Note = "Fixed note"; o.NoteText = () => note; });
                a.Refresh(); yield return null;
                bool first = HasText(w.Body, "One") && !HasText(w.Body, "Fixed note");
                note = "Two"; a.Refresh(); yield return null;
                Check(first && HasText(w.Body, "Two") && !HasText(w.Body, "One"), "PreviewOptions.NoteText follows its Func on refresh and wins over Note");
                string named = "", liveSeen = null;
                var text = Controls.Text(w.Body, "Short", Bind.To(() => named, v => named = v), o => { o.MaxLength = 5; o.Live = v => liveSeen = v; });
                var tf = text.Input as TextField;
                Type(tf, "abcdefg"); yield return null;
                Check(named == "abcde", "TextOptions.MaxLength 5: typing abcdefg writes abcde", named);
                using (var ce = ChangeEvent<string>.GetPooled("", "abcdefg")) { ce.target = tf; tf.SendEvent(ce); }
                yield return null;
                Check(tf.maxLength == 5 && liveSeen == "abcde", "...the field's own limit is 5 and Live sees a 7-character change cut to abcde", tf.maxLength + " / " + liveSeen);
                string stored = "abcdefgh";
                var longer = Controls.Text(w.Body, "Stored", Bind.To(() => stored, v => stored = v), o => o.MaxLength = 5);
                a.Refresh(); yield return null;
                string nextUndo = History.NextUndo;
                var lf = longer.Input as TextField;
                lf.Focus(); yield return null;
                (lf.focusController?.focusedElement as VisualElement)?.Blur(); yield return null;
                Check(stored == "abcdefgh" && History.NextUndo == nextUndo,
                    "...and a focus and blur with no typing leaves a longer stored value and History untouched", stored + " / " + History.NextUndo);
                a.Dispose(); a = null;

                // A host answering Facet's own words.
                History.Clear();
                var trHost = Host.Create("FacetOwnWordsProbe", o =>
                {
                    o.Translate = k => k == "Facet:Mixed" ? "X" : k == "Facet:Undo" ? "Y" : k == "Facet:Undo {0}" ? "Y {0}" : null;
                    o.Log = m => Main.Log("[ownwords] " + m);
                });
                a = new Surface(trHost, "FacetOwnWordsProbe", Layer.Window);
                var tw = new Window(a.Root, "ownwordsprobe", "Own words", new Rect(60, 60, 600, 400));
                var items = new List<float[]> { new[] { 1f }, new[] { 2f } };
                Controls.Slider(tw.Body, "Mixed probe", Bind.Many(() => items, x => x[0], (x, v) => x[0] = v), o => o.Id = "cv-mixed");
                a.Refresh(); yield return null;
                var num = tw.Body.Q<TextField>("cv-mixed-number");
                Check(num != null && num.value == "X", "a host mapping Facet:Mixed to X shows X in a mixed Many slider", num?.value);
                var head = tw.TitleBarRight.Query<Button>().ToList().Find(x => x.text == "\u21B6");
                History.Record(null, null, () => { }, () => { });
                Check(head != null && head.tooltip == "Y", "...and Facet:Undo to Y shows Y in its window header's undo tooltip", head?.tooltip);
                History.Record("Thing", null, () => { }, () => { });
                Check(head != null && head.tooltip == "Y Thing", "...and Facet:Undo {0} formats the named step", head?.tooltip);
                History.Clear();
                a.Dispose(); a = null;

                var echoHost = Host.Create("FacetEchoProbe", o => { o.Translate = k => k; o.Log = m => Main.Log("[echo] " + m); });
                a = new Surface(echoHost, "FacetEchoProbe", Layer.Window);
                var ew = new Window(a.Root, "echoprobe", "Echo", new Rect(60, 60, 600, 400));
                Controls.Slider(ew.Body, "Mixed probe", Bind.Many(() => items, x => x[0], (x, v) => x[0] = v), o => o.Id = "cv-echo");
                a.Refresh(); yield return null;
                var en = ew.Body.Q<TextField>("cv-echo-number");
                Check(en != null && en.value == "Mixed", "a host whose translator returns the key it was given shows Facet's own Mixed", en?.value);
            }
            finally
            {
                if (sw != null) sw.Dispose();
                if (a != null) a.Dispose();
                if (b != null) b.Dispose();
                if (c != null) c.Dispose();
                History.Clear();
            }
        }
            // The 0.2 conventions: notifications are events (an InsetChanged handler is told the inset as it is added), the
        // text helpers take the parent first, and Columns hands back both columns.
        IEnumerator SectionCleanup()
        {
            Surface s = null;
            Action throwing = () => throw new InvalidOperationException("cleanup apply"); var keepLog = History.Log;
            try
            {
                bool flag = true;
                var page = new List<VisualElement>();
                var h = Host.Create("FacetCleanupProbe");
                s = new Surface(h, "FacetCleanupProbe", Layer.Window);
                var w = new Window(s.Root, "cleanupprobe", "Cleanup", new Rect(80, 80, 700, 420));
                var nav = new Nav(w.Body, new[] { new NavItem("Probe", p =>
                {
                    page.Add(p);
                    Controls.Toggle(p, "Flag", Bind.To(() => flag, v => flag = v), o => o.Id = "cleanup-flag");
                }) });
                var heard = new List<float>();
                nav.InsetChanged += heard.Add;
                Check(heard.Count == 1 && heard[0] == nav.Inset, "an InsetChanged handler added before any layout is called at once with Nav.Inset",
                    heard.Count + " / " + nav.Inset);
                yield return null; yield return null;
                Check(heard.Count == 1, "...and not again when the first layout produces the same inset", heard.Count.ToString());
                var late = new List<float>();
                nav.InsetChanged += late.Add;
                Check(late.Count == 1 && late[0] == nav.Inset, "one added after layout is also called once at once", late.Count.ToString());
                nav.ToggleRail();
                Check(heard.Count == 2 && late.Count == 2 && heard[1] == nav.Inset, "a changed inset reaches every handler once", heard.Count + " / " + late.Count);
                nav.InsetChanged -= late.Add;
                nav.ToggleRail();
                Check(heard.Count == 3 && late.Count == 2, "a removed handler hears no more", heard.Count + " / " + late.Count);

                int raised = 0;
                w.Raised += () => raised++;
                var at = w.Footer.worldBound.center;
                PointerAt(EventType.MouseDown, at, s.Root);
                PointerAt(EventType.MouseUp, at, s.Root);
                Check(raised == 1, "a Window.Raised handler runs on a press", raised.ToString());

                bool? told = null;
                nav.CompactChanged += v => told = v;
                nav.CompactButton = true;
                yield return null;
                var btn = w.Root.Q("sidebar-toggle");
                if (btn != null) ClickEl(btn);
                Check(nav.Compact && told == true, "CompactChanged reports the sidebar toggle's flip", nav.Compact + " / " + told);

                // A throwing handler leaves the handlers after it running, on each of the three events.
                int after = 0;
                w.Raised += () => throw new InvalidOperationException("raised boom");
                w.Raised += () => after++;
                PointerAt(EventType.MouseDown, at, s.Root);
                PointerAt(EventType.MouseUp, at, s.Root);
                Check(raised == 2 && after == 1, "a throwing Window.Raised handler leaves the ones after it running", raised + " / " + after);
                bool? told2 = null;
                nav.CompactChanged += _ => throw new InvalidOperationException("compact boom");
                nav.CompactChanged += v => told2 = v;
                if (btn != null) ClickEl(btn);
                Check(!nav.Compact && told == false && told2 == false, "a throwing CompactChanged handler leaves the ones after it running", told + " / " + told2);
                var heard2 = new List<float>();
                nav.InsetChanged += _ => throw new InvalidOperationException("inset boom");
                nav.InsetChanged += heard2.Add;
                nav.ToggleRail();
                Check(heard2.Count == 2 && heard2[1] == nav.Inset, "a throwing InsetChanged handler leaves the ones after it running", heard2.Count.ToString());
                nav.ToggleRail();

                // A throwing AfterApply subscriber: logged once, and the undo still shows in the same frame.
                var input = s.Root.Q("cleanup-flag-input");
                Func<bool> on = () => input != null && Near(input.style.backgroundColor.value, s.Theme[Role.Accent]);
                Check(on(), "the probe toggle starts on");
                var said = new List<string>();
                History.Log = said.Add;
                History.AfterApply += throwing;
                History.Record("Flag", "cleanup-flag", () => flag = false, () => flag = true);
                History.Undo();
                Check(!on() && said.FindAll(m => m.Contains("cleanup apply")).Count == 1,
                    "a throwing History.AfterApply subscriber is logged once, and an undo still shows the old value in the same frame", string.Join(" | ", said));

                // Parent first, and the Columns handle.
                var host = page.Count > 0 ? page[page.Count - 1] : w.Body;
                var text = Ui.Text(host, "Probe text");
                var btn2 = Ui.Btn(host, "Probe button", null);
                var muted = Ui.Muted(host, "Probe muted");
                var caps = Ui.Caps(host, "Probe caps");
                Check(text.parent == host && btn2.parent == host && muted.parent == host && caps.parent == host && btn2.text == "Probe button",
                    "Ui.Text, Btn, Muted and Caps take the parent first");
                var cols = Controls.Columns(host, o => o.StackBelow = 0f);
                var stage = Controls.Preview(cols.Right, "Probe preview");
                Check(cols.Left.parent == cols.Root && cols.Right.parent == cols.Root && cols.Root.parent == host && stage != null && cols.Right.Contains(stage),
                    "Controls.Columns(...).Right holds the preview, beside Left in Root");
            }
            finally
            {
                History.AfterApply -= throwing; History.Log = keepLog;
                History.Clear();
                if (s != null) s.Dispose();
            }
        }
    }
}
