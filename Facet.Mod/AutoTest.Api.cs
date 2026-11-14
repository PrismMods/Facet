using System;
using System.Collections;
using System.Collections.Generic;
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
                PointerAt(EventType.MouseDown, o + new Vector2(10f, 10f), root);
                pad.RemoveFromHierarchy(); yield return null;
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
    }
}
