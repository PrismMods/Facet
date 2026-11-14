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
    }
}
