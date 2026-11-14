using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Form parts a settings page needs, on the Parts window's Forms page.
    sealed partial class AutoTest
    {
        IEnumerator SectionMaskTiles()
        {
            const int seed = 1 << 20 | 1 << 5;
            const int listed = 1 | 1 << 1 | 1 << 2 | 1 << 5 | 1 << 9;
            GalleryForms.Mask = seed;
            var win = GalleryParts.Open(); yield return null; yield return null;
            VisualElement box = null;
            try
            {
                GalleryParts.Nav.Go(GalleryParts.FormsItem); yield return null; yield return null; yield return null;
                var g = GalleryForms.Tiles;
                string[] ids = { "mask-alpha", "mask-bravo", "mask-charlie", "mask-delta", "mask-echo" };
                var colours = new Color?[] { new Color(0.3f, 0.6f, 1f), new Color(0.3f, 0.9f, 0.4f), null, new Color(1f, 0.8f, 0.2f), new Color(1f, 0.3f, 0.3f) };
                bool named = g != null && g.Tiles.Count == 5 && g.Root.name == "mask" && g.Root.childCount == 5, edges = named;
                if (named)
                    for (int i = 0; i < 5; i++)
                    {
                        var t = g.Tiles[i].Root;
                        named &= t.name == ids[i] && t.parent == g.Root && win.Body.Q(ids[i] + "-input") != null;
                        var abs = t.Children().Where(c => c.pickingMode == PickingMode.Ignore && c.style.position == Position.Absolute).ToList();
                        var edge = abs.LastOrDefault();
                        edges &= colours[i].HasValue ? abs.Count == 2 && Near(edge.style.backgroundColor.value, colours[i].Value) : abs.Count == 1;
                    }
                Check(named && edges, "MaskTiles: one tile per entry, named by its Id, a coloured edge where a colour is given",
                    g == null ? "no grid" : g.Tiles.Count + " tiles, names " + named + ", edges " + edges);
                if (g == null || g.Tiles.Count != 5) yield break;

                History.Clear();
                ClickEl(g.Tiles[0].Root); yield return null;
                bool one = History.CanUndo;
                Check(GalleryForms.Mask == (seed | 1) && g.Tiles[0].Value && one, "MaskTiles: a click flips its bit alone and records one undo step", Convert.ToString(GalleryForms.Mask, 2));
                History.Undo(); yield return null;
                bool back = GalleryForms.Mask == seed && !g.Tiles[0].Value && !History.CanUndo;
                History.Redo(); yield return null;
                Check(back && GalleryForms.Mask == (seed | 1) && g.Tiles[0].Value, "MaskTiles: undo puts the mask back, redo again", Convert.ToString(GalleryForms.Mask, 2));

                GalleryForms.Mask = seed; g.Tiles[0].Refresh(); History.Clear();
                g.SetAll(true); yield return null;
                bool all = GalleryForms.Mask == (seed | listed) && g.Tiles.All(t => t.Value);
                History.Undo(); yield return null;
                Check(all && GalleryForms.Mask == seed && !History.CanUndo, "MaskTiles: SetAll(true) sets the listed bits only, bit 20 kept, as one step", Convert.ToString(GalleryForms.Mask, 2));

                GalleryForms.Mask = seed | listed; History.Clear();
                g.SetAll(true); yield return null;
                Check(GalleryForms.Mask == (seed | listed) && !History.CanUndo, "MaskTiles: SetAll on a grid already all on records nothing");

                GalleryForms.Mask = seed; foreach (var t in g.Tiles) t.Refresh(); History.Clear();
                g.SetAll(true);
                yield return new WaitForSeconds(0.1f);
                g.SetAll(false); yield return null;
                bool none = GalleryForms.Mask == 1 << 20 && g.Tiles.All(t => !t.Value);
                History.Undo(); yield return null;
                bool undo1 = GalleryForms.Mask == (seed | listed);
                History.Undo(); yield return null;
                Check(none && undo1 && GalleryForms.Mask == seed, "MaskTiles: All then None undo one at a time", none + "/" + undo1 + "/" + Convert.ToString(GalleryForms.Mask, 2));
                History.Clear();

                // A bit no tile names, changed after SetAll, survives its undo and redo.
                g.SetAll(true); yield return null;
                GalleryForms.Mask |= 1 << 21;
                History.Undo(); yield return null;
                bool kept = GalleryForms.Mask == (seed | 1 << 21);
                GalleryForms.Mask &= ~(1 << 20);
                History.Redo(); yield return null;
                Check(kept && GalleryForms.Mask == (1 << 21 | listed), "MaskTiles: SetAll's undo and redo touch the listed bits only", kept + "/" + Convert.ToString(GalleryForms.Mask, 2));
                GalleryForms.Mask = seed; foreach (var t in g.Tiles) t.Refresh(); History.Clear();

                // A store that throws records nothing; a grid with Undoable false records neither SetAll nor a click.
                int strict = 0; bool fail = true;
                box = Ui.Box(GalleryForms.Page);
                var two = new[] { new MaskTile("Foxtrot", 0, o => o.Id = "mask-foxtrot"), new MaskTile("Golf", 1) };
                var bad = Controls.MaskTiles(box, Bind.To(() => strict, v => { if (fail) throw new InvalidOperationException("probe store"); strict = v; }), two, o => o.Id = "mask-bad");
                var quiet = Controls.MaskTiles(box, Bind.To(() => strict, v => strict = v), two, o => { o.Id = "mask-quiet"; o.Undoable = false; });
                yield return null; yield return null;
                bad.SetAll(true); yield return null;
                bool noStep = strict == 0 && !History.CanUndo;
                fail = false;
                ClickEl(quiet.Tiles[0].Root); yield return null;
                quiet.SetAll(true); yield return null;
                Check(noStep && strict == 3 && !History.CanUndo, "MaskTiles: a failed write and an Undoable-false grid record no step",
                    noStep + "/" + strict + "/" + History.CanUndo);
                box.RemoveFromHierarchy(); box = null;
                History.Clear();

                bool found = true; string miss = null;
                foreach (var label in new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" })
                {
                    GalleryParts.Nav.Filter(label); yield return null; yield return null;
                    var labels = win.Body.Query<Label>().ToList();
                    if (!labels.Exists(l => l.text == label) || labels.Exists(l => l.text.StartsWith("Nothing matches"))) { found = false; miss = miss ?? label; }
                }
                GalleryParts.Nav.Filter(""); yield return null; yield return null;
                Check(found, "MaskTiles: every tile's label is found by search", miss);
                g = GalleryForms.Tiles;   // the filter rebuilt the page

                GalleryForms.Mask = 1 << 20 | listed;
                GalleryParts.Surface.Refresh(); yield return null;
                var th = Gallery.Host.Theme;
                bool shown = g.Tiles.All(t => t.Value);
                foreach (var id in ids) shown &= Near(win.Body.Q(id + "-input").style.backgroundColor.value, th[Role.Accent]);
                GalleryForms.Mask = 0;
                GalleryParts.Surface.Refresh(); yield return null;
                shown &= g.Tiles.All(t => !t.Value);
                Check(shown, "MaskTiles: a mask changed outside shows on every tile after Surface.Refresh()");

                // A bare element takes a row's tooltip func and context menu.
                int acts = 0;
                box = Ui.Box(GalleryForms.Page);
                box.name = "hook-box";
                box.style.height = 40f;
                Controls.Hook(box, o =>
                {
                    o.TooltipText = () => "Hook tip";
                    o.ContextMenu = new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>("Hook act", () => acts++) };
                });
                yield return null; yield return null;
                Mouse<MouseEnterEvent>(box); yield return null;
                string tip = box.tooltip;
                PressAt(box.worldBound.center, win.Root, 1); yield return null; yield return null;
                var item = LiveLabel("Hook act");
                if (item != null) { Release(item.parent); yield return null; }
                Check(tip == "Hook tip" && item != null && acts == 1, "Controls.Hook: TooltipText and ContextMenu work on a bare element", tip + " / " + (item != null) + " / " + acts);
            }
            finally
            {
                if (box != null) box.RemoveFromHierarchy();
                History.Clear();
                GalleryParts.Close(); GalleryForms.Mask = seed;
            }
        }

        // The dual-range slider on the Forms page: 100..1000 in steps of 1, seeded (400, 600).
        IEnumerator SectionRange()
        {
            var seed = new Vector2(400f, 600f);
            GalleryForms.Span = seed;
            var win = GalleryParts.Open(); yield return null; yield return null;
            try
            {
                GalleryParts.Nav.Go(GalleryParts.FormsItem); yield return null; yield return null; yield return null;
                var r = GalleryForms.Range;
                var lo = win.Body.Q<TextField>("range-low-input");
                var hi = win.Body.Q<TextField>("range-high-input");
                var track = win.Body.Q("range-low-track");
                bool laid = r != null && r.Root.name == "range-low" && lo != null && hi != null && track != null
                    && lo.parent == track.parent && hi.parent == track.parent
                    && lo.parent.IndexOf(lo) < lo.parent.IndexOf(track) && lo.parent.IndexOf(track) < lo.parent.IndexOf(hi)
                    && lo.value == "400" && hi.value == "600";
                Check(laid, "Range: two number fields named Id-input and HighId-input, and a track",
                    (lo != null) + "/" + (hi != null) + "/" + (track != null) + " " + lo?.value + "-" + hi?.value);
                if (r == null || lo == null || hi == null || track == null) yield break;

                History.Clear();
                Type(lo, "700"); yield return null;
                bool pushed = GalleryForms.Span == new Vector2(700f, 700f) && hi.value == "700";
                History.Undo(); yield return null;
                Check(pushed && GalleryForms.Span == seed && !History.CanUndo && lo.value == "400",
                    "Range: typing a low value above the high pushes the high to it, as one undo step", pushed + " " + GalleryForms.Span);

                // The high thumb from 600 to 300, then (both ends at 300) from there to 800: two drags, two steps.
                History.Clear();
                var b = track.worldBound;
                Func<float, Vector2> at = v => new Vector2(b.xMin + (v - 100f) / 900f * b.width, b.center.y);
                yield return DragAt(at(600f), at(300f), win.Root);
                bool first = GalleryForms.Span == new Vector2(300f, 300f);
                var afterFirst = GalleryForms.Span;
                yield return DragAt(at(300f), at(800f), win.Root);
                bool second = GalleryForms.Span == new Vector2(300f, 800f);
                History.Undo(); yield return null;
                bool undo1 = GalleryForms.Span == new Vector2(300f, 300f);
                History.Undo(); yield return null;
                Check(first && second && undo1 && GalleryForms.Span == seed && !History.CanUndo,
                    "Range: a drag of the high thumb below the low pushes the low, one step per drag",
                    afterFirst + " " + second + " " + undo1 + " " + GalleryForms.Span);

                History.Clear();
                GalleryForms.Span = new Vector2(80f, 20f);
                int writes = GalleryForms.SpanWrites;
                GalleryParts.Surface.Refresh(); yield return null; yield return null;
                Check(lo.value == "20" && hi.value == "80" && GalleryForms.Span == new Vector2(80f, 20f) && GalleryForms.SpanWrites == writes && !History.CanUndo,
                    "Range: a stored (80, 20) shows 20 and 80 and writes nothing", lo.value + "-" + hi.value + " " + GalleryForms.Span);

                GalleryForms.Span = seed; GalleryParts.Surface.Refresh(); yield return null;
                Type(lo, "250.6"); yield return null;
                bool snapped = GalleryForms.Span == new Vector2(251f, 600f);
                Type(hi, "5000"); yield return null;
                bool top = GalleryForms.Span == new Vector2(251f, 1000f);
                Type(lo, "50"); yield return null;
                Check(snapped && top && GalleryForms.Span == new Vector2(100f, 1000f) && lo.value == "100" && hi.value == "1000",
                    "Range: typed values snap to Step from Min and clamp", snapped + "/" + top + " " + GalleryForms.Span);
                History.Clear();

                Type(hi, "450 BPM"); yield return null;
                Check(GalleryForms.Span == new Vector2(100f, 450f) && hi.value == "450",
                    "Range: a number typed before a unit is read", hi.value + " " + GalleryForms.Span);
                History.Clear();

                bool found = true; string miss = null;
                foreach (var label in new[] { "Low end", "High end" })
                {
                    GalleryParts.Nav.Filter(label); yield return null; yield return null;
                    var labels = win.Body.Query<Label>().ToList();
                    if (!labels.Exists(l => l.text == label) || labels.Exists(l => l.text.StartsWith("Nothing matches"))) { found = false; miss = miss ?? label; }
                }
                GalleryParts.Nav.Filter(""); yield return null; yield return null;
                Check(found, "Range: both labels are found by search", miss);
            }
            finally
            {
                History.Clear();
                GalleryParts.Close(); GalleryForms.Span = seed;
            }
        }
    }
}
