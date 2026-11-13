using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Sections for the Editor window's parts, one iterator each; Start runs them.
    sealed partial class AutoTest
    {
        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        // The Undo page: rows record steps, an undo commits the restored value, a row can skip history,
        // and a row's steps go stale with the document they were recorded on.
        IEnumerator SectionHistorySteps()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            try
            {
                var m = GalleryEditor.Model;
                GalleryEditor.Nav.Go(GalleryEditor.UndoItem); yield return null; yield return null;
                History.Clear();

                var flag = win.Body.Q("ed-flag-input");
                Check(flag != null, "the Undo page has its toggle, ed-flag-input");
                if (flag != null) ClickEl(flag);
                yield return null;
                Check(m.Flag && History.NextUndo == "Flag", "clicking the toggle records one step under its label", History.NextUndo);
                History.Undo(); yield return null;
                Check(!m.Flag, "History.Undo puts the toggle back");

                var amount = GalleryEditor.AmountRow;
                var s = amount?.Input as Slider;
                Check(s != null && Near(m.Amount, 0.2f), "the Undo page's slider starts at 0.2", m.Amount.ToString());
                if (s == null) yield break;
                s.value = 0.7f; yield return null;
                Check(Near(m.Amount, 0.7f), "moving the slider to 0.7 writes its field", m.Amount.ToString());
                int changed = 0, committed = 0; float got = -1f; bool applying = false;
                Action<float> onChanged = _ => changed++;
                Action<float> onCommitted = v => { committed++; got = v; applying = History.Applying; };
                amount.Changed += onChanged; amount.Committed += onCommitted;
                m.AmountWrites = 0;
                History.Undo(); yield return null;
                Check(Near(m.Amount, 0.2f) && Near(amount.Value, 0.2f), "History.Undo restores 0.2 to the field and the row", m.Amount + " / " + amount.Value);
                Check(committed == 1 && Near(got, 0.2f), "...the slider's Committed fires once with 0.2", committed + " x " + got);
                Check(applying, "...with History.Applying true inside it");
                Check(changed == 0, "...and Changed does not fire", changed.ToString());
                Check(m.AmountWrites == 1, "...and the undo writes the bind once", m.AmountWrites.ToString());
                committed = 0; got = -1f; m.AmountWrites = 0;
                History.Redo(); yield return null;
                Check(Near(m.Amount, 0.7f) && committed == 1 && Near(got, 0.7f) && changed == 0 && m.AmountWrites == 1,
                    "a redo writes 0.7 once and commits it once, without Changed", m.Amount + " committed " + committed + " x " + got + " changed " + changed + " writes " + m.AmountWrites);
                amount.Changed -= onChanged; amount.Committed -= onCommitted;

                // A row whose page went away: the undo still restores its field, and leaves the removed row alone.
                History.Clear();
                s.value = 0.5f; yield return null;
                amount.Root.RemoveFromHierarchy(); yield return null;
                committed = 0; amount.Committed += onCommitted;
                History.Undo(); yield return null;
                amount.Committed -= onCommitted;
                Check(Near(m.Amount, 0.7f) && committed == 0 && Near(amount.Value, 0.5f),
                    "an undo of a removed row's edit restores its field, and neither shows nor commits on the removed row",
                    m.Amount + " committed " + committed + " shown " + amount.Value);

                // ...and its step keeps neither the removed row nor what the row's options capture.
                History.Clear();
                var held = new float[1];
                var weak = RemovedRow(win.Body, Cell(held));
                yield return null;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Check(!weak.IsAlive && History.NextUndo == "Held",
                    "a step on a removed row whose tooltip func captures the row lets the row be collected, and stays undoable",
                    "alive " + weak.IsAlive + ", next " + History.NextUndo);
                Check(History.Undo() && held[0] == 0f, "...and its undo restores the field", held[0].ToString());

                History.Clear();
                var quiet = win.Body.Q("ed-quiet-input");
                Check(quiet != null, "the Undo page has its Undoable = false row, ed-quiet-input");
                if (quiet != null) ClickEl(quiet);
                yield return null;
                Check(m.Quiet && !History.CanUndo, "clicking the Undoable = false row changes its field, and CanUndo is still false");

                History.Clear();
                var bs = GalleryEditor.BoundRow?.Input as Slider;
                var undo = win.TitleBarRight.Query<Button>().ToList().Find(b => b.text == "↶");
                Check(bs != null && undo != null, "the Undo page has its document-bound slider, and the window its undo button");
                if (bs == null || undo == null) yield break;
                bs.value = 0.4f; yield return null;
                Check(Near(m.Bound, 0.4f) && History.CanUndo && undo.enabledSelf, "a change on the document-bound row records a live step");
                m.Doc = new object(); History.Invalidate(); yield return null;
                bool did = History.Undo(); yield return null;
                Check(!did && Near(m.Bound, 0.4f), "after the document is replaced, History.Undo does not change the row", "undid " + did + ", " + m.Bound);
                Check(!undo.enabledSelf, "...and the window header's undo button is disabled");
                bs.value = 0.9f; yield return null;
                Check(History.Undo() && Near(m.Bound, 0.4f), "a second change, on the new document, is undone by History.Undo", m.Bound.ToString());

                // A step that swaps the document passes no validity: its undo brings the older document's steps back.
                History.Clear();
                var older = m.Doc;
                bs.value = 0.6f; yield return null;
                History.Snapshot("Swap", "swap", () => m.Doc, d => m.Doc = d, () => m.Doc = new object());
                Check(History.NextUndo == "Swap" && !ReferenceEquals(m.Doc, older), "a snapshot that swaps the document is the next undo", History.NextUndo);
                History.Undo(); yield return null;
                Check(ReferenceEquals(m.Doc, older) && History.NextUndo == "Bound", "undoing the swap brings the older document back, and its step is live again", History.NextUndo);
                Check(History.Undo() && Near(m.Bound, 0.4f), "...so the next undo restores the row", m.Bound.ToString());

                /* A drag on the document-bound row: past its first write, the row's write and its history allocate
                   nothing (the bind is the row's own, so the slider's drawing is left out). The game's runtime reads 0
                   from the per-thread allocation counter even for code that allocates, so this counts heap growth over
                   many writes with the collector paused, beside a loop that allocates on purpose. */
                History.Clear();
                var bind = typeof(Control<float>).GetField("_bind", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(GalleryEditor.BoundRow) as Bind<float>;
                Check(bind != null, "the document-bound row's bind is reachable for the allocation probe");
                if (bind == null) yield break;
                float start = m.Bound;
                bind.Set(0.1f); bind.Set(0.11f); bind.Set(0.12f);   // the step's first write, then the coalescing path once
                long grew, junk;
                object keep = null;
                var mode = GarbageCollector.GCMode;
                try
                {
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    long a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) keep = new byte[128];
                    junk = GC.GetTotalMemory(false) - a;
                    a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) bind.Set(0.2f + (i % 100) * 0.005f);
                    grew = GC.GetTotalMemory(false) - a;
                }
                finally { GarbageCollector.GCMode = mode; }
                GC.KeepAlive(keep);
                Main.Log("[perf] bound slider drag: heap grew " + grew + " bytes over 2000 coalescing writes (2000 x 128-byte arrays grew it " + junk + ")");
                Check(junk > 0, "the heap probe sees allocations", junk + " bytes");
                Check(grew == 0, "a document-bound slider drag's coalescing writes allocate nothing", grew + " bytes");
                Check(History.Undo() && Near(m.Bound, start) && !History.CanUndo, "...and the drag is one step", m.Bound.ToString());
            }
            finally { History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        static Bind<float> Cell(float[] x) => Bind.To(() => x[0], v => x[0] = v);

        /* A hidden page under body with one row whose tooltip func captures the row, as consumers' options
           often do; the row is set to 0.3 once, then the page is removed. Its own method, so the section
           holds the row only through the weak reference. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference RemovedRow(VisualElement body, Bind<float> bind)
        {
            var page = new VisualElement();
            page.style.display = DisplayStyle.None;   // never under the pointer
            body.Add(page);
            Control<float> c = null;
            c = Controls.Slider(page, "Held", bind, o => { o.Id = "ed-held"; o.TooltipText = () => c.Value.ToString(); });
            c.Set(0.3f);
            page.RemoveFromHierarchy();
            return new WeakReference(c);
        }
    }
}
