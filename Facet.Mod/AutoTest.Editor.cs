using System;
using System.Collections;
using System.Collections.Generic;
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

                // A Many row's step keeps neither the row nor the page its items delegate reads through (a canvas's selection).
                History.Clear();
                var manyItems = new List<GalleryBox> { new GalleryBox("w1", "W1", new Rect(0, 0, 1, 1)), new GalleryBox("w2", "W2", new Rect(0, 0, 1, 1)) };
                var weakPage = RemovedManyRow(win.Body, manyItems);
                yield return null;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Check(!weakPage.IsAlive && History.NextUndo == "Held hue",
                    "a step on a removed Many row whose items delegate captures its page lets the page be collected, and stays undoable",
                    "alive " + weakPage.IsAlive + ", next " + History.NextUndo);
                Check(History.Undo() && manyItems.TrueForAll(b => b.Hue == 0f), "...and its undo restores every item");

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

        // A press and a release at a panel point on the panel `via` sits in, in one frame: a still click.
        static void PressAt(Vector2 panel, VisualElement via, int button = 0)
        {
            PointerAt(EventType.MouseDown, panel, via, button);
            PointerAt(EventType.MouseUp, panel, via, button);
        }

        // A release of the left button on `e` itself: how a menu row is picked.
        static void Release(VisualElement e)
        {
            using (var ev = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { ev.target = e; e.SendEvent(ev); }
        }

        // The Canvas page's toolbar parts: edges, a toggle, undo/redo, chips, a label, search and an icon button.
        IEnumerator SectionToolbar()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            VisualElement box = null;
            try
            {
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem); yield return null; yield return null;
                History.Clear();
                box = Ui.Box();
                GalleryEditor.Page.Insert(0, box);
                var tb = Controls.Toolbar(box);
                var card = Controls.Card(box, "Probe card");
                var crow = Controls.Toggle(card, "Probe row", Bind.Value(false));
                bool grid = true;
                var tog = tb.Toggle("Grid", Bind.To(() => grid, v => grid = v), "Show the grid");
                tb.UndoRedo();
                int pick = 1;
                var chips = tb.Chips(Bind.To(() => pick, v => pick = v), new[] { 1, 2, 3 }, v => "Chip " + v);
                tb.Spacer();
                string said = "first";
                var lab = tb.Label(() => said);
                string searched = null;
                var search = tb.Search(q => searched = q, "Find");
                int icons = 0;
                var icon = tb.Icon(Background.FromTexture2D(Texture2D.whiteTexture), "Probe icon", () => icons++);
                yield return null; yield return null;

                /* A card's rows sit its border (Line) and then their own padding (Pad) in; the toolbar pads in the
                   same two steps, so layout rounds both to the same whole pixels at any scale. */
                var rowText = crow.Root.Q<Label>();
                var shell = card.parent;   // Controls.Card returns its body; the border is on the body's parent
                float cardL = rowText.worldBound.xMin - shell.worldBound.xMin, cardR = shell.worldBound.xMax - crow.Input.worldBound.xMax;
                float barL = tog.worldBound.xMin - tb.Root.worldBound.xMin, barR = tb.Root.worldBound.xMax - icon.worldBound.xMax;
                Check(Mathf.Abs(barL - cardL) <= 0.5f && Mathf.Abs(barR - cardR) <= 0.5f,
                    "a toolbar's edges pad as a card pads its rows (±0.5)", barL + " / " + barR + " vs " + cardL + " / " + cardR);
                float dl = tog.worldBound.xMin - rowText.worldBound.xMin, dr = icon.worldBound.xMax - crow.Input.worldBound.xMax;
                Check(Mathf.Abs(dl) <= 0.5f && Mathf.Abs(dr) <= 0.5f, "...so its first item starts under a row's label and its last ends on the row's control edge (±0.5)", dl + " / " + dr);

                var accent = Theme.For(tog)[Role.Accent];
                Check(Near(tog.style.backgroundColor.value, accent), "a toolbar Toggle shows selected while its bind is true");
                Submit(tog); yield return null;
                Check(!grid && !Near(tog.style.backgroundColor.value, accent), "...and a click flips the bind and the look", grid.ToString());
                Check(!History.CanUndo, "...recording nothing in History");

                var undo = tb.Root.Query<Button>().ToList().Find(b => b.text == "↶");
                var head = win.TitleBarRight.Query<Button>().ToList().Find(b => b.text == "↶");
                Check(undo != null && head != null && !undo.enabledSelf && !History.CanUndo, "UndoRedo's undo button is disabled while there is nothing to undo");
                if (undo == null || head == null) yield break;
                History.Record("Probe step", null, () => { }, () => { });
                Check(undo.enabledSelf && History.CanUndo && undo.tooltip == head.tooltip && undo.tooltip == "Undo Probe step",
                    "...enabled once there is a step, with the header's tooltip", undo.tooltip + " / " + head.tooltip);
                History.Undo();
                Check(!undo.enabledSelf && !History.CanUndo, "...and disabled again once it is undone");
                Check(Px(undo.style.fontSize) == Px(head.style.fontSize) && Px(undo.style.width) == Px(head.style.width),
                    "...drawn as the header's glyph", Px(undo.style.fontSize) + " / " + Px(head.style.fontSize));

                var c2 = chips.Root.Query<Button>().ToList().Find(b => b.text == "Chip 2");
                Check(c2 != null && chips.Value == 1, "Chips shows one button per value, the bound one first");
                if (c2 != null) { Submit(c2); yield return null; }
                Check(pick == 2 && chips.Value == 2 && c2 != null && Near(c2.style.backgroundColor.value, accent), "Chips selects the clicked value", pick.ToString());

                said = "second";
                Check(lab.text == "first", "a toolbar Label shows its text");
                GalleryEditor.Surface.Refresh();
                Check(lab.text == "second", "...and re-reads it on Surface.Refresh()", lab.text);

                search.value = "abc"; yield return null;
                Check(searched == "abc", "a toolbar Search reports what is typed", searched ?? "null");
                Submit(icon); yield return null;
                Check(icons == 1 && icon.tooltip == "Probe icon", "an icon button runs its click and carries its tooltip", icons + " / " + icon.tooltip);
            }
            finally { box?.RemoveFromHierarchy(); History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        static int OpenPopups() => ((System.Collections.IList)typeof(Popup).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Count;

        // A menu row on screen by its text label.
        static VisualElement MenuRow(string text) => LiveLabel(text)?.parent;

        static int Count(string s, string part)
        {
            int n = 0;
            for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        // The Canvas page's menu bar: one popup per title, hover switching, a submenu, disabled and checked rows,
        // shortcut labels, Escape, a throwing item, and text re-read each time the menu opens.
        IEnumerator SectionMenus()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            VisualElement probe = null;
            try
            {
                var m = GalleryEditor.Model;
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem); yield return null; yield return null;
                History.Clear();
                var bar = GalleryEditor.Bar;
                var file = win.Body.Q<Button>("ed-file"); var edit = win.Body.Q<Button>("ed-edit");
                Check(bar != null && file != null && edit != null, "the Canvas page has a menu bar with File and Edit");
                if (bar == null || file == null || edit == null) yield break;
                int open0 = OpenPopups();

                PressAt(edit.worldBound.center, win.Root); yield return null; yield return null;
                Check(bar.IsOpen && OpenPopups() == open0 + 1 && LiveLabel("Copy Style") != null && LiveLabel("New") == null,
                    "clicking Edit opens one popup, Edit's menu", "open " + bar.IsOpen + ", popups +" + (OpenPopups() - open0));
                var paste = MenuRow("Paste");
                Check(paste != null, "Edit lists Paste");
                if (paste != null) { Release(paste); yield return null; }
                Check(m.Pastes == 0 && bar.IsOpen, "the disabled Paste row does not run on a click", m.Pastes.ToString());
                var mark = MenuRow("Snap")?.Q<Label>("check");
                Check(mark != null && mark.text == "✓", "the checked Snap row shows its mark", mark?.text);
                var keys = MenuRow("Copy")?.Q<Label>("shortcut");
                string want = Shortcut.Display("Ctrl+C", Application.platform == RuntimePlatform.OSXPlayer);
                Check(keys != null && keys.text == want, "the Copy row's shortcut label reads " + want, keys?.text);
                var undoText = MenuRow("Copy")?.parent?.Q("ed-undo")?.Q<Label>("text");
                Check(undoText != null && undoText.text == "Undo ", "with nothing to undo the Undo row reads \"Undo \"", undoText?.text);

                var card = MenuRow("Copy")?.parent;
                if (card != null) PointerAt(EventType.MouseMove, file.worldBound.center, card);
                yield return null; yield return null;
                Check(bar.IsOpen && LiveLabel("New") != null && LiveLabel("Copy Style") == null && OpenPopups() == open0 + 1,
                    "hovering File while Edit is open swaps the popup to File's", "popups +" + (OpenPopups() - open0));

                var recent = MenuRow("Recent");
                Check(recent != null && recent.Q<Label>("sub") != null, "File lists Recent with a submenu arrow");
                if (recent == null) yield break;
                PointerAt(EventType.MouseMove, recent.worldBound.center, recent); yield return null; yield return null;
                var r1 = MenuRow("Recent 1");
                Check(r1 != null && OpenPopups() == open0 + 2 && r1.parent.worldBound.xMin >= recent.worldBound.xMax - 8f,
                    "hovering Recent opens its submenu beside it", r1 == null ? "none" : r1.parent.worldBound + " / " + recent.worldBound);
                if (r1 != null) { Release(r1); yield return null; yield return null; }
                Check(m.Recents == 1 && !bar.IsOpen && OpenPopups() == open0, "picking the submenu's item runs it once and closes both menus",
                    m.Recents + " runs, popups +" + (OpenPopups() - open0));

                PressAt(edit.worldBound.center, win.Root); yield return null; yield return null;
                bool esc = Input.HandleEscape(); yield return null;
                Check(esc && !bar.IsOpen && OpenPopups() == open0 && GalleryEditor.Surface.Visible, "Escape closes the menu and the window stays open");

                m.Fail = true;
                long at = LogLength();
                PressAt(file.worldBound.center, win.Root); yield return null; yield return null;
                var add = MenuRow("New");
                if (add != null) { Release(add); yield return null; }
                int lines = Count(LogSince(at), "Menu 'New'");
                Check(add != null && lines == 1 && !bar.IsOpen && OpenPopups() == open0, "an item that throws logs one line and the menu closes", lines + " lines");

                bar.Open(1); yield return null;
                var u1 = MenuRow("Copy")?.parent?.Q("ed-undo")?.Q<Label>("text")?.text;
                bar.Close();
                History.Record("Probe step", null, () => { }, () => { });
                bar.Open(1); yield return null;
                var u2 = MenuRow("Copy")?.parent?.Q("ed-undo")?.Q<Label>("text")?.text;
                bar.Close();
                Check(u1 == "Undo " && u2 == "Undo Probe step", "the Undo row's text changes after a History step", u1 + " -> " + u2);
                Check(!bar.IsOpen && OpenPopups() == open0, "MenuBar.Open and Close drive the same popup");

                // A command title (no Items) runs from its own click alone, never from a hover while a menu is open.
                int resets = 0, locked = 0;
                probe = Ui.Box();
                GalleryEditor.Page.Insert(0, probe);
                var bar2 = Controls.MenuBar(probe, new List<MenuItem>
                {
                    new MenuItem { Text = "Probe A", Items = new List<MenuItem> { new MenuItem { Text = "Probe A item" } } },
                    new MenuItem { Text = "Probe B", Items = new List<MenuItem> { new MenuItem { Text = "Probe B item" } } },
                    new MenuItem { Text = "Probe Reset", Id = "probe-reset", Run = () => resets++ },
                    new MenuItem { Text = "Probe Locked", Enabled = () => false, Run = () => locked++ },
                });
                yield return null; yield return null;
                var reset = probe.Q<Button>("probe-reset");
                var titleA = probe.Query<Button>().ToList().Find(b => b.text == "Probe A");
                bar2.Open(1); yield return null; yield return null;
                var card2 = MenuRow("Probe B item")?.parent;
                if (card2 != null && reset != null) PointerAt(EventType.MouseMove, reset.worldBound.center, card2);
                yield return null; yield return null;
                Check(card2 != null && reset != null && resets == 0 && bar2.IsOpen && LiveLabel("Probe B item") != null,
                    "hovering a command title while a menu is open runs nothing and keeps the menu", resets + " runs, open " + bar2.IsOpen);
                if (card2 != null && card2.panel != null && titleA != null) PointerAt(EventType.MouseMove, titleA.worldBound.center, card2);
                yield return null; yield return null;
                Check(LiveLabel("Probe A item") != null && LiveLabel("Probe B item") == null, "...while hovering a title with a menu still switches to it");
                bar2.Close(); yield return null;
                if (reset != null) { PressAt(reset.worldBound.center, win.Root); yield return null; }
                bar2.Open(3); yield return null;
                Check(resets == 1 && locked == 0 && !bar2.IsOpen, "a click on a command title runs it once; a disabled one does not run", resets + " / " + locked);
            }
            finally { Popup.CloseAll(); probe?.RemoveFromHierarchy(); History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        // A row's ContextMenuItems: read at the right-click, opened at the pointer. A canvas's stage keeps its right presses.
        IEnumerator SectionContextMenu()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            VisualElement box = null;
            try
            {
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem); yield return null; yield return null;
                int runs = 0;
                var first = new List<MenuItem> { new MenuItem { Text = "Ctx first", Run = () => runs += 1 } };
                var second = new List<MenuItem> { new MenuItem { Text = "Ctx second", Run = () => runs += 10 } };
                var list = first;
                box = Ui.Box();
                GalleryEditor.Page.Insert(0, box);
                var row = Controls.Toggle(box, "Ctx probe", Bind.Value(false), o => { o.Id = "ed-ctx"; o.ContextMenuItems = () => list; });
                yield return null; yield return null;
                list = second;
                var at = row.Root.worldBound.center + new Vector2(-40f, 3f);
                PressAt(at, win.Root, 1); yield return null; yield return null;
                var lab = LiveLabel("Ctx second");
                var card = lab?.parent?.parent;
                Check(lab != null && LiveLabel("Ctx first") == null, "a right-click on a row opens the list its ContextMenuItems returns at the click");
                Check(card != null && Mathf.Abs(card.worldBound.xMin - at.x) <= 1f && Mathf.Abs(card.worldBound.yMin - at.y) <= 1f,
                    "...with the card's top-left within 1 px of the click point", card == null ? "none" : card.worldBound.position + " vs " + at);
                if (lab != null) { Release(lab.parent); yield return null; }
                Check(runs == 10 && LiveLabel("Ctx second") == null, "picking its item runs it once and closes the menu", runs.ToString());
                row.Root.RemoveFromHierarchy();

                var cv = Controls.Canvas<GalleryBox>(box, "Ctx canvas", o =>
                {
                    o.Items = () => new List<GalleryBox>();
                    o.GetRect = b => b.Rect;
                    o.SetRect = (b, r) => b.Rect = r;
                    o.Size = new Vector2(400, 100);
                    o.ContextMenuItems = () => first;
                    o.Menu = _ => second;
                });
                yield return null; yield return null;
                PointerAt(EventType.MouseDown, cv.Stage.worldBound.center, win.Root, 1); yield return null; yield return null;
                Check(LiveLabel("Ctx first") == null && LiveLabel("Ctx second") == null, "a right press on a canvas's stage opens nothing: the stage keeps it from the pane's ContextMenuItems");
                PointerAt(EventType.MouseUp, cv.Stage.worldBound.center, win.Root, 1); yield return null; yield return null;
                Check(LiveLabel("Ctx second") != null && LiveLabel("Ctx first") == null, "...and its release opens the canvas's Menu");
            }
            finally { Popup.CloseAll(); box?.RemoveFromHierarchy(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        // Menus.HandleKeys over the editor's menus: chords with their modifiers, never while typing, a capture or a
        // drag runs, never Ctrl+Z, only for the front window, and nothing allocated on a frame with no key down.
        IEnumerator SectionMenuKeys()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            bool galleryShown = Gallery.Surface != null && Gallery.Surface.Visible, galleryMade = Gallery.Surface == null;
            TextField tf = null;
            Surface modal = null;
            try
            {
                var m = GalleryEditor.Model;
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem); yield return null; yield return null;
                History.Clear();
                var items = GalleryEditor.MenuItems;
                Func<KeyCode, bool> c = k => k == KeyCode.C, z = k => k == KeyCode.Z, none = k => false;

                bool ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(ran && m.CopyStyles == 1 && m.Copies == 0, "C down with Ctrl and Shift runs Copy Style and not Copy", m.CopyStyles + " / " + m.Copies);
                ran = Menus.HandleKeys(win.Root, items, c, true, false, false);
                Check(ran && m.Copies == 1 && m.CopyStyles == 1, "C down with Ctrl alone runs Copy", m.CopyStyles + " / " + m.Copies);
                ran = Menus.HandleKeys(win.Root, items, c, false, false, false);
                Check(!ran && m.Copies == 1, "C down with no modifier runs nothing");
                ran = Menus.HandleKeys(win.Root, items, k => k == KeyCode.Alpha1, true, false, false);
                Check(ran && m.Recents == 1, "a shortcut inside a submenu runs too", m.Recents.ToString());

                tf = new TextField();
                GalleryEditor.Page.Insert(0, tf); yield return null;
                tf.Focus(); yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(Input.TextFocused && !ran && m.CopyStyles == 1, "with a text field focused it runs nothing", "focused " + Input.TextFocused);
                tf.Blur(); tf.RemoveFromHierarchy(); tf = null; yield return null;

                KeyCapture.Claim("menu-keys probe", () => { });
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                KeyCapture.Release("menu-keys probe");
                Check(!ran && m.CopyStyles == 1, "while a key capture runs it runs nothing");
                Input.Gesture = () => { };
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Input.Gesture = null;
                Check(!ran && m.CopyStyles == 1, "while a drag is in progress it runs nothing");

                History.Record("Probe step", null, () => { }, () => { });
                ran = Menus.HandleKeys(win.Root, items, z, true, false, false);
                Check(!ran && m.Undos == 0 && History.NextUndo == "Probe step", "Z down with Ctrl runs nothing and returns false, though Undo shows Ctrl+Z",
                    "ran " + ran + ", undos " + m.Undos + ", next " + History.NextUndo);
                int redoZ = 0, redoY = 0, shiftY = 0;
                var redos = new List<MenuItem>
                {
                    new MenuItem { Text = "Probe redo Z", Shortcut = "Ctrl+Shift+Z", Run = () => redoZ++ },
                    new MenuItem { Text = "Probe redo Y", Shortcut = "Ctrl+Y", Run = () => redoY++ },
                    new MenuItem { Text = "Probe shift Y", Shortcut = "Ctrl+Shift+Y", Run = () => shiftY++ },
                };
                bool rz = Menus.HandleKeys(win.Root, redos, z, true, true, false), ry = Menus.HandleKeys(win.Root, redos, k => k == KeyCode.Y, true, false, false);
                Check(!rz && !ry && redoZ == 0 && redoY == 0, "...and neither do Ctrl+Shift+Z or Ctrl+Y, though items show them",
                    "ran " + rz + "/" + ry + ", runs " + redoZ + "/" + redoY);
                ran = Menus.HandleKeys(win.Root, redos, k => k == KeyCode.Y, true, true, false);
                Check(ran && shiftY == 1, "...while Ctrl+Shift+Y, outside the undo chord, runs its item", "ran " + ran + ", runs " + shiftY);

                Gallery.Open(); Gallery.Surface.BringToFront(); yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(!ran && m.CopyStyles == 1, "with a second Gallery window raised above it, the editor's HandleKeys runs nothing");
                var foot = win.Footer.worldBound;
                PressAt(new Vector2(foot.xMax - 40f, foot.center.y), win.Root); yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(ran && m.CopyStyles == 2, "a press in the editor raises it, and its shortcuts answer again", m.CopyStyles.ToString());
                Gallery.Surface.BringToFront(); yield return null;
                var bar = win.TitleBarRight.parent.worldBound;
                PressAt(new Vector2(bar.xMin + 60f, bar.center.y), win.Root); yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(ran && m.CopyStyles == 3, "...and so does a press on its title bar, whose drag stops the press", m.CopyStyles.ToString());

                GalleryEditor.Bar.Open(1); yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                bool barOpen = GalleryEditor.Bar.IsOpen;
                GalleryEditor.Bar.Close(); yield return null;
                Check(barOpen && !ran && m.CopyStyles == 3, "with the editor's own menu open, its HandleKeys runs nothing", "open " + barOpen + ", " + m.CopyStyles);
                modal = new Surface(Gallery.Host, "FacetModalProbe", Layer.Modal); yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(modal.Visible && !ran && m.CopyStyles == 3, "with a modal surface above the editor, its HandleKeys runs nothing", m.CopyStyles.ToString());
                modal.Dispose(); modal = null; yield return null;
                ran = Menus.HandleKeys(win.Root, items, c, true, true, false);
                Check(ran && m.CopyStyles == 4, "...and its shortcuts answer again once the menu and the modal are gone", m.CopyStyles.ToString());

                // The game's runtime reads 0 from the per-thread counter, so this counts heap growth with the collector paused.
                Menus.HandleKeys(win.Root, items, none, true, false, false);
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
                    for (int i = 0; i < 10000; i++) Menus.HandleKeys(win.Root, items, none, true, false, false);
                    grew = GC.GetTotalMemory(false) - a;
                }
                finally { GarbageCollector.GCMode = mode; }
                GC.KeepAlive(keep);
                Main.Log("[perf] menu shortcuts: heap grew " + grew + " bytes over 10000 HandleKeys rounds with no key down (2000 x 128-byte arrays grew it " + junk + ")");
                Check(junk > 0, "the heap probe sees allocations", junk + " bytes");
                Check(grew == 0, "10 000 HandleKeys rounds with no key down allocate nothing", grew + " bytes");
            }
            finally
            {
                if (tf != null) tf.RemoveFromHierarchy();
                modal?.Dispose();
                KeyCapture.Release("menu-keys probe"); Input.Gesture = null;
                if (galleryMade) Gallery.Close(); else if (!galleryShown && Gallery.Surface != null) Gallery.Surface.Visible = false;
                History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel();
            }
        }

        // ---- the Canvas page's inspector: Bind.Many rows, the Mixed look, Live, tab memory, RightFill

        static T InspectorQ<T>(string name) where T : VisualElement => GalleryEditor.Inspector?.Q<T>(name);

        static List<GalleryBox> Boxes(params int[] n)
        {
            var l = new List<GalleryBox>();
            foreach (var i in n) l.Add(BoxOf("k" + i));
            return l;
        }

        // Select boxes on the canvas, then let one refresh pass rebuild the inspector.
        static IEnumerator Pick(params int[] n)
        {
            Cv.Select(Boxes(n));
            GalleryEditor.Surface.Refresh(); yield return null; yield return null;
        }

        static bool Selected(VisualElement e) => e != null && Near(e.style.backgroundColor.value, Theme.For(e)[Role.Accent]);

        static Button TabButton(string name) => GalleryEditor.Inspector?.Query<Button>().ToList().Find(b => b.text == name);

        IEnumerator SectionInspector()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            GalleryEditor.HostKeys = false;
            Surface probe = null, live = null;
            try
            {
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem);
                History.Clear();
                for (int i = 0; i < 3; i++) yield return null;
                var m = GalleryEditor.Model;
                float[] hue0 = new float[13];
                for (int i = 1; i <= 12; i++) hue0[i] = BoxOf("k" + i).Hue;
                var box = TabButton("Box");
                if (box != null) { Submit(box); yield return null; }

                // Mixed
                yield return Pick(1, 2, 3);
                var num = InspectorQ<TextField>("ed-i-hue-number");
                var sl = InspectorQ<Slider>("ed-i-hue-input");
                Check(num != null && num.value == "Mixed" && sl != null && Mathf.Abs(sl.value - hue0[1]) < 1e-3f,
                    "boxes with different hues show Mixed in the slider's value text, its thumb at the first box's hue", num?.value + " / " + sl?.value);
                var tint = InspectorQ<VisualElement>("ed-i-tint");
                var mixedSwatch = InspectorQ<VisualElement>("ed-i-tint-swatch")?.Q("mixed");
                Check(tint != null && tint.Query<Label>().ToList().Exists(l => l.text == "Mixed") && mixedSwatch != null && mixedSwatch.style.display == DisplayStyle.Flex,
                    "...the Colour row shows Mixed over an Off swatch");
                var name = InspectorQ<TextField>("ed-i-name-input");
                Check(name != null && name.value == "", "...the Text field is empty", name?.value);
                var shape = InspectorQ<VisualElement>("ed-i-shape-input");
                Check(shape != null && !shape.Query<Button>().ToList().Exists(Selected), "...Choice has no segment selected");
                var pat = InspectorQ<Button>("ed-i-pattern-input");
                Check(pat != null && pat.text == "Mixed", "...the Dropdown reads Mixed", pat?.text);
                var bold = InspectorQ<VisualElement>("ed-i-bold-input");
                var knob = bold?.childCount > 0 ? bold[0] : null;
                Check(bold != null && !Selected(bold) && knob != null && Mathf.Abs(knob.worldBound.center.x - bold.worldBound.center.x) <= 1f,
                    "...and the Toggle is indeterminate: off track, knob centred", knob == null ? "none" : knob.worldBound.center.x + " vs " + bold.worldBound.center.x);
                var key = InspectorQ<Button>("ed-i-hotkey-input");
                Check(key != null && key.text == "Mixed", "...and the Key row reads Mixed", key?.text);
                yield return Pick(2);
                num = InspectorQ<TextField>("ed-i-hue-number"); name = InspectorQ<TextField>("ed-i-name-input"); pat = InspectorQ<Button>("ed-i-pattern-input");
                bold = InspectorQ<VisualElement>("ed-i-bold-input"); shape = InspectorQ<VisualElement>("ed-i-shape-input");
                Check(num != null && num.value == hue0[2].ToString("0.##") && name != null && name.value == "Box 2" && pat != null && pat.text == "Stripes"
                      && !Selected(bold) && shape != null && Selected(shape.Query<Button>().ToList()[1]),
                    "one box shows its own values", num?.value + " / " + name?.value + " / " + pat?.text);
                BoxOf("k4").Hue = 0.4f; BoxOf("k5").Hue = 0.4f;
                yield return Pick(4, 5);
                num = InspectorQ<TextField>("ed-i-hue-number");
                Check(num != null && num.value == 0.4f.ToString("0.##"), "two boxes sharing a hue show it, not Mixed", num?.value);
                BoxOf("k4").Hue = hue0[4]; BoxOf("k5").Hue = hue0[5];

                // Writes: every selected box, one step, undone to each box's own hue.
                yield return Pick(1, 2, 3);
                History.Clear();
                var row = GalleryEditor.HueRow; sl = InspectorQ<Slider>("ed-i-hue-input");
                Check(row != null && sl != null, "the inspector's hue row is built");
                if (row == null || sl == null) yield break;
                int changed = 0, committed = 0;
                Action<float> onC = _ => changed++, onK = _ => committed++;
                sl.value = 0.5f; yield return null;
                Check(Boxes(1, 2, 3).TrueForAll(b => Mathf.Abs(b.Hue - 0.5f) < 1e-4f) && BoxOf("k4").Hue == hue0[4], "moving the slider writes every selected box, and no other");
                Check(History.NextUndo == "Hue", "...as one step under the row's label", History.NextUndo);
                GalleryEditor.Surface.Refresh(); yield return null;
                num = InspectorQ<TextField>("ed-i-hue-number");
                Check(num != null && num.value == 0.5f.ToString("0.##"), "...and the three boxes, now equal, show the value, not Mixed", num?.value);
                row.Changed += onC; row.Committed += onK;
                History.Undo(); yield return null;
                row.Changed -= onC; row.Committed -= onK;
                Check(BoxOf("k1").Hue == hue0[1] && BoxOf("k2").Hue == hue0[2] && BoxOf("k3").Hue == hue0[3], "one undo restores each box's own old hue");
                num = InspectorQ<TextField>("ed-i-hue-number");
                Check(num != null && num.value == "Mixed", "...so Mixed comes back", num?.value);
                Check(committed == 1 && changed == 0, "...and the slider's Committed fired once, Changed not at all", committed + " / " + changed);

                History.Clear();
                name = InspectorQ<TextField>("ed-i-name-input");
                bool tookFocus = false, gaveUp = false;
                if (name != null)
                {
                    name.Focus(); yield return null;
                    tookFocus = Within(name.focusController?.focusedElement as VisualElement, name);
                    sl.Focus(); yield return null;
                    gaveUp = !Within(name.focusController?.focusedElement as VisualElement, name);
                }
                Check(tookFocus && gaveUp, "the mixed Text field takes focus, then gives it up", tookFocus + " / " + gaveUp);
                Check(name != null && BoxOf("k1").Name == "Box 1" && BoxOf("k3").Name == "Box 3" && !History.CanUndo, "focusing the mixed Text field and leaving it without typing writes nothing");

                // Replace: an undo by key reaches the new objects.
                History.Clear();
                sl.value = 0.25f; yield return null;
                var olds = Boxes(1, 2, 3);
                var replace = win.Body.Q<Button>("ed-replace");
                Check(replace != null, "the Canvas page has its Replace button");
                if (replace != null) { Submit(replace); yield return null; }
                GalleryEditor.Surface.Refresh(); yield return null;
                var news = Boxes(1, 2, 3);
                Check(!ReferenceEquals(olds[0], news[0]) && news[0].Hue == 0.25f, "Replace makes new objects that keep the edited hue");
                History.Undo(); yield return null;
                Check(news[0].Hue == hue0[1] && news[1].Hue == hue0[2] && news[2].Hue == hue0[3] && olds[0].Hue == 0.25f,
                    "after Replace, an undo gives the new objects their old hues back and leaves the dropped ones", news[0].Hue + " / " + olds[0].Hue);

                // Two selections edited within the coalescing window are two steps.
                History.Clear();
                yield return Pick(1, 2, 3);
                var t0 = Time.realtimeSinceStartup;
                InspectorQ<Slider>("ed-i-hue-input").value = 0.6f; yield return null;
                yield return Pick(4, 5);
                InspectorQ<Slider>("ed-i-hue-input").value = 0.7f; yield return null;
                float took = Time.realtimeSinceStartup - t0;
                Check(took < History.CoalesceSeconds, "the two edits are inside the coalescing window", took + " s");
                History.Undo(); yield return null;
                Check(BoxOf("k4").Hue == hue0[4] && BoxOf("k5").Hue == hue0[5] && Boxes(1, 2, 3).TrueForAll(b => b.Hue == 0.6f) && History.CanUndo,
                    "editing boxes 1-3 then 4-5 quickly: the first undo restores only 4-5");
                History.Undo(); yield return null;
                Check(BoxOf("k1").Hue == hue0[1] && BoxOf("k3").Hue == hue0[3] && BoxOf("k4").Hue == hue0[4], "...and the second only 1-3");

                /* Rows a tab click builds inside a rebuilt inspector are a new step too: Style is built by a click on
                   both selections, so only the shared step number keeps the two Bold edits apart. */
                History.Clear();
                bool[] bold0 = new bool[6];
                for (int i = 1; i <= 5; i++) bold0[i] = BoxOf("k" + i).Bold;
                yield return Pick(1, 2, 3);
                var styleTab = TabButton("Style");
                if (styleTab != null) { Submit(styleTab); yield return null; }
                var sb1 = InspectorQ<VisualElement>("ed-s-bold-input");
                float tb = Time.realtimeSinceStartup;
                if (sb1 != null) ClickEl(sb1);
                yield return null;
                bool b13 = BoxOf("k1").Bold;
                yield return Pick(4, 5);
                box = TabButton("Box"); if (box != null) { Submit(box); yield return null; }
                styleTab = TabButton("Style"); if (styleTab != null) { Submit(styleTab); yield return null; }
                var sb2 = InspectorQ<VisualElement>("ed-s-bold-input");
                if (sb2 != null) ClickEl(sb2);
                yield return null;
                bool b45 = BoxOf("k4").Bold;
                float tookTabs = Time.realtimeSinceStartup - tb;
                Check(tookTabs < History.CoalesceSeconds, "the two Bold clicks are inside the coalescing window", tookTabs + " s");
                Check(sb1 != null && sb2 != null && (b13 != bold0[1] || b13 != bold0[2] || b13 != bold0[3]) && (b45 != bold0[4] || b45 != bold0[5]), "clicking Style's Bold on boxes 1-3, then on 4-5 after a tab click, writes both");
                History.Undo(); yield return null;
                Check(BoxOf("k4").Bold == bold0[4] && BoxOf("k5").Bold == bold0[5] && BoxOf("k1").Bold == b13 && History.CanUndo,
                    "rows built by a tab click after a rebuild record their own step: the first undo restores only 4-5",
                    BoxOf("k1").Bold + " / " + BoxOf("k4").Bold + " / " + History.CanUndo);
                History.Undo(); yield return null;
                Check(BoxOf("k1").Bold == bold0[1] && BoxOf("k2").Bold == bold0[2] && BoxOf("k3").Bold == bold0[3], "...and the second restores 1-3");
                box = TabButton("Box"); if (box != null) { Submit(box); yield return null; }

                // Typed text commits to the boxes the typing began on, though a press on another box moves the selection first.
                History.Clear();
                yield return Pick(1, 2, 3);
                name = InspectorQ<TextField>("ed-i-name-input");
                if (name != null) { name.Focus(); yield return null; name.value = "Lead"; }
                yield return Tap(Mid("k4")); yield return null;
                Check(name != null && Boxes(1, 2, 3).TrueForAll(b => b.Name == "Lead") && BoxOf("k4").Name == "Box 4" && Keys(Cv) == "k4",
                    "a name typed with boxes 1-3 selected, then a press on box 4, names boxes 1-3 and leaves box 4",
                    BoxOf("k1").Name + " / " + BoxOf("k4").Name + " / " + Keys(Cv));
                Check(History.Undo() && BoxOf("k1").Name == "Box 1" && BoxOf("k3").Name == "Box 3", "...and its undo restores their names", BoxOf("k1").Name);

                // Tabs keep their pick through a rebuild, and fall back when it is gone.
                yield return Pick(1);
                var style = TabButton("Style");
                if (style != null) { Submit(style); yield return null; }
                Check(InspectorQ<VisualElement>("ed-s-tint") != null, "choosing Style shows its rows");
                yield return Pick(3);
                Check(InspectorQ<VisualElement>("ed-s-tint") != null && InspectorQ<VisualElement>("ed-i-hue") == null, "selecting another box rebuilds the inspector still on Style");
                yield return Pick(2, 4);
                var extra = TabButton("Extra");
                Check(extra != null, "every selected box even-numbered: the Extra tab is there");
                if (extra != null) { Submit(extra); yield return null; }
                Check(InspectorQ<VisualElement>("ed-x-note") != null, "...and shows its rows");
                yield return Pick(1, 2);
                Check(TabButton("Extra") == null && InspectorQ<VisualElement>("ed-i-hue") != null, "a set without Extra while on Extra opens the first tab");
                box = TabButton("Box");

                // RightFill: the right column pinned to the viewport while the left scrolls.
                var right = GalleryEditor.Right;
                var sv = right?.GetFirstAncestorOfType<ScrollView>();
                if (sv != null)
                {
                    var v0 = sv.contentViewport.worldBound;
                    Check(Mathf.Abs(right.worldBound.yMax - v0.yMax) <= 1f, "unscrolled, the right column reaches from its place down to the viewport's bottom (±1)",
                        right.worldBound + " vs " + v0);
                }
                // The box list makes the left column at least 500 px taller than the viewport, so the page scrolls under the pinned column.
                var leftCol = GalleryEditor.Left;
                Check(sv != null && leftCol != null && leftCol.layout.height >= sv.contentViewport.layout.height + 500f && sv.verticalScroller.highValue >= 500f,
                    "the Canvas page's box list makes its left column 500 px taller than the viewport, so the page scrolls",
                    sv == null || leftCol == null ? "none" : leftCol.layout.height + " vs " + sv.contentViewport.layout.height + ", scrolls " + sv.verticalScroller.highValue);
                if (sv == null) yield break;
                sv.scrollOffset = new Vector2(0f, 500f); yield return null; yield return null;
                var vp = sv.contentViewport.worldBound;
                Check(Mathf.Abs(right.worldBound.yMin - vp.yMin) <= 1f, "with the left column scrolled 500 px the right column's top is the viewport's (±1)", right.worldBound.yMin + " vs " + vp.yMin);
                Check(Mathf.Abs(right.worldBound.height - vp.height) <= 1f, "...and its height the viewport's (±1)", right.worldBound.height + " vs " + vp.height);
                var ins = GalleryEditor.InspectorScroll;
                Check(ins != null && ins.verticalScroller.highValue > 0f && ins.worldBound.yMax <= vp.yMax + 1f, "the inspector scroll takes the rest of the column and can scroll");
                if (ins != null)
                {
                    float page0 = sv.scrollOffset.y, ins0 = ins.scrollOffset.y;
                    var ev = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, 3f), mousePosition = ins.worldBound.center };
                    using (var e = WheelEvent.GetPooled(ev)) GalleryEditor.Surface.Root.panel.visualTree.SendEvent(e);
                    yield return null; yield return null;
                    Check(ins.scrollOffset.y > ins0 && sv.scrollOffset.y == page0, "a wheel over the inspector scrolls it and not the page", (ins.scrollOffset.y - ins0) + " / " + (sv.scrollOffset.y - page0));
                }
                // Scrolled to the end: the pinned column stops at the columns' bottom and leaves the line after them uncovered.
                sv.scrollOffset = new Vector2(0f, sv.verticalScroller.highValue); yield return null; yield return null;
                var cols = right.parent.worldBound;
                var foot = GalleryEditor.Footer;
                Check(right.worldBound.yMax <= cols.yMax + 1f && foot != null && foot.worldBound.yMin >= right.worldBound.yMax - 1f,
                    "scrolled to the end, the right column ends at the columns' bottom, above the line after them (±1)",
                    right.worldBound.yMax + " vs columns " + cols.yMax + ", line " + (foot == null ? "none" : foot.worldBound.yMin.ToString()));
                var wr = win.Rect;
                win.Rect = new Rect(wr.x, wr.y, 520f, wr.height); yield return null; yield return null; yield return null;
                float kids = 0f;
                for (int i = 0; i < right.childCount; i++) kids = Mathf.Max(kids, right[i].layout.yMax + right[i].resolvedStyle.marginBottom);
                Check(right.style.translate.value.y == 0f && Mathf.Abs(right.layout.height - kids) <= 1f && right.parent.resolvedStyle.flexDirection == FlexDirection.Column,
                    "narrowed below StackBelow the columns stack, and the right column is its content's height, unpinned", right.layout.height + " vs " + kids);
                win.Rect = wr; sv.scrollOffset = Vector2.zero; yield return null; yield return null;

                // Perf: a rebuild of the 25-row Box tab, and a refresh pass over 30 Many rows on 108 boxes.
                yield return Pick(1, 2, 3);
                box = TabButton("Box");
                if (box != null) { Submit(box); yield return null; }
                Check(InspectorQ<VisualElement>("ed-i-flag6") != null, "the Box tab holds its 25 rows");
                var sw = new System.Diagnostics.Stopwatch();
                double sum = 0;
                for (int r = 0; r < 12; r++)
                {
                    Cv.Select(r % 2 == 0 ? Boxes(4, 5) : Boxes(6));
                    sw.Reset(); sw.Start();
                    GalleryEditor.Surface.Refresh();
                    sw.Stop();
                    if (r >= 2) sum += sw.Elapsed.TotalMilliseconds;   // two warm-ups
                    yield return null;
                }
                double rebuild = sum / 10;
                Main.Log("[perf] inspector rebuild " + rebuild.ToString("0.00") + " ms (mean of 10, 25 rows under Tabs.Pages)");
                Check(rebuild <= 16.0, "rebuilding the Live inspector with 25 rows takes at most 16 ms", rebuild.ToString("0.00"));

                probe = new Surface(Gallery.Host, "FacetManyPerf", Layer.Window);
                var pw = new Window(probe.Root, "manyperf", "Many", new Rect(60, 60, 600, 500));
                var many = GalleryEditor.ManyBoxes();
                Func<IReadOnlyList<GalleryBox>> all = () => many;
                for (int k = 0; k < 10; k++)
                {
                    int i = k;
                    Controls.Slider(pw.Body, "S" + i, Bind.Many(all, b => b.Values[i], (b, v) => b.Values[i] = v, b => b.Key), o => { if (i == 0) o.Id = "many-s0"; });
                    Controls.Number(pw.Body, "N" + i, Bind.Many(all, b => b.Values[i], (b, v) => b.Values[i] = v, b => b.Key));
                }
                for (int k = 0; k < 6; k++) { int i = k; Controls.Toggle(pw.Body, "F" + i, Bind.Many(all, b => b.Flags[i], (b, v) => b.Flags[i] = v, b => b.Key)); }
                Controls.Text(pw.Body, "Name", Bind.Many(all, b => b.Name, (b, v) => b.Name = v, b => b.Key));
                Controls.Choice(pw.Body, "Shape", Bind.Many(all, b => b.Shape, (b, v) => b.Shape = v, b => b.Key), o => o.Items = new[] { "A", "B", "C" });
                Controls.Colour(pw.Body, "Tint", Bind.Many(all, b => b.Tint, (b, v) => b.Tint = v, b => b.Key));
                Controls.Key(pw.Body, "Hotkey", Bind.Many(all, b => b.Hotkey, (b, v) => b.Hotkey = v, b => b.Key));
                yield return null; yield return null;
                for (int r = 0; r < 3; r++) probe.Refresh();
                foreach (var mb in many) mb.Values[0] = 0.77f;
                probe.Refresh();
                var s0 = pw.Body.Q<TextField>("many-s0-number");
                int joinedRows = Refresher.Of(pw.Body)?.Count ?? 0;
                Check(s0 != null && s0.value == 0.77f.ToString("0.##") && joinedRows >= 30, "the 30 Many rows are on the probe's refresher, and one pass shows a changed value",
                    s0?.value + " / " + joinedRows + " refreshed");
                sum = 0;
                for (int r = 0; r < 20; r++)
                {
                    sw.Reset(); sw.Start();
                    probe.Refresh();
                    sw.Stop();
                    sum += sw.Elapsed.TotalMilliseconds;
                }
                double pass = sum / 20;
                Main.Log("[perf] many refresh " + pass.ToString("0.000") + " ms (mean of 20 passes, 30 Bind.Many rows over 108 boxes)");
                Check(pass <= 1.0, "a refresh pass over 30 Many rows with 108 items averages at most 1 ms", pass.ToString("0.000"));

                /* A drag on a Many row over 108 boxes: past the step's first write, its frames allocate nothing (the row's
                   own bind, so the slider's drawing is left out; heap growth with the collector paused, as above). */
                History.Clear();
                var dragRow = Controls.Slider(pw.Body, "Drag", Bind.Many(all, b => b.Hue, (b, v) => b.Hue = v, b => b.Key));
                var dragBind = typeof(Control<float>).GetField("_bind", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dragRow) as Bind<float>;
                Check(dragBind != null, "the Many drag row's bind is reachable for the allocation probe");
                if (dragBind == null) yield break;
                dragBind.Set(0.1f); dragBind.Set(0.11f); dragBind.Set(0.12f);
                long mgrew, mjunk;
                object mkeep = null;
                var gmode = GarbageCollector.GCMode;
                try
                {
                    GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                    long a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) mkeep = new byte[128];
                    mjunk = GC.GetTotalMemory(false) - a;
                    a = GC.GetTotalMemory(false);
                    for (int i = 0; i < 2000; i++) dragBind.Set(0.2f + (i % 100) * 0.005f);
                    mgrew = GC.GetTotalMemory(false) - a;
                }
                finally { GarbageCollector.GCMode = gmode; }
                GC.KeepAlive(mkeep);
                Main.Log("[perf] many slider drag: heap grew " + mgrew + " bytes over 2000 coalescing writes to 108 boxes (2000 x 128-byte arrays grew it " + mjunk + ")");
                Check(mgrew == 0, "a Many slider drag's coalescing writes over 108 boxes allocate nothing", mgrew + " bytes");
                Check(History.Undo() && !History.CanUndo && many.TrueForAll(b => b.Hue == 0f), "...and the drag is one step, undone to every box's hue");

                /* A Live region inside another one's build: an outer rebuild makes a new inner region, whose own count
                   starts again at 0, so only the outer count in the key keeps edits either side of it apart. */
                live = new Surface(Gallery.Host, "FacetLiveProbe", Layer.Window);
                var lw = new Window(live.Root, "liveprobe", "Live", new Rect(60, 60, 500, 300));
                int[] outer = { 0 };
                float[] cell = { 0f };
                Controls.Live(lw.Body, () => outer[0], ob => Controls.Live(ob, () => 0, ib => Controls.Slider(ib, "Nested", Cell(cell), o => o.Id = "live-nested")));
                yield return null; yield return null;
                History.Clear();
                float tn = Time.realtimeSinceStartup;
                var nested = lw.Body.Q<Slider>("live-nested-input");
                if (nested != null) { nested.value = 0.3f; yield return null; }
                outer[0]++; live.Refresh(); yield return null;
                var nested2 = lw.Body.Q<Slider>("live-nested-input");
                if (nested2 != null) { nested2.value = 0.6f; yield return null; }
                float tookNested = Time.realtimeSinceStartup - tn;
                Check(nested != null && nested2 != null && !ReferenceEquals(nested, nested2) && tookNested < History.CoalesceSeconds,
                    "an outer Live rebuild makes the inner region's row anew, inside the coalescing window", tookNested + " s");
                Check(History.Undo() && Mathf.Abs(cell[0] - 0.3f) < 1e-4f && History.CanUndo,
                    "a row in a Live region nested in another: edits either side of an outer rebuild are two steps", cell[0] + " / " + History.CanUndo);

                /* A Live region keyed on an int compares it by value: its refresh check allocates nothing. The same key
                   through the object overload boxes it on every pass, measured beside it as the probe's own check. */
                lw.Body.Clear();
                int[] ver = { 0 }, built = { 0 };
                Controls.Live(lw.Body, () => ver[0] + 1000, vb => built[0]++);
                yield return null;
                ver[0]++; live.Refresh();
                Check(built[0] == 2, "the int-keyed Live region rebuilds on its probe's refresh pass when the number moves", built[0].ToString());
                live.Refresh();
                long lgrew = LiveHeap(live, out long ljunk);
                lw.Body.Clear();
                Controls.Live(lw.Body, (Func<object>)(() => ver[0] + 1000), vb => { });
                yield return null;
                live.Refresh();
                long ogrew = LiveHeap(live, out long _);
                Main.Log("[perf] live int key: heap grew " + lgrew + " bytes over 100000 refresh passes; the same key as an object grew it " + ogrew
                         + " (100000 boxed ints grew it " + ljunk + ")");
                Check(ljunk > 0 && ogrew > 0 && lgrew == 0, "a refresh pass over a Live region keyed on an int allocates nothing (an object key's boxing shows)", lgrew + " / " + ogrew + " bytes");
            }
            finally { probe?.Dispose(); live?.Dispose(); GalleryEditor.HostKeys = true; History.Clear(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        static Bind<float> Cell(float[] x) => Bind.To(() => x[0], v => x[0] = v);

        /* Heap growth over 100000 refresh passes of `s` with the collector paused; `junk` is 100000 boxed ints' growth,
           the size a boxed key would take. Many passes: the heap's partly free blocks take a few thousand small objects
           before any growth shows. */
        static long LiveHeap(Surface s, out long junk)
        {
            object keep = null;
            var mode = GarbageCollector.GCMode;
            try
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                long a = GC.GetTotalMemory(false);
                for (int i = 0; i < 100000; i++) keep = i;
                junk = GC.GetTotalMemory(false) - a;
                a = GC.GetTotalMemory(false);
                for (int i = 0; i < 100000; i++) s.Refresh();
                long grew = GC.GetTotalMemory(false) - a;
                GC.KeepAlive(keep);
                return grew;
            }
            finally { GarbageCollector.GCMode = mode; }
        }

        static bool Within(VisualElement focused, VisualElement e) => focused != null && (focused == e || e.Contains(focused));

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

        // The same for a Many row, keyed with an all list, whose items delegate reads through its page.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference RemovedManyRow(VisualElement body, List<GalleryBox> items)
        {
            var page = new VisualElement { userData = items };
            page.style.display = DisplayStyle.None;
            body.Add(page);
            var c = Controls.Slider(page, "Held hue", Bind.Many(() => (List<GalleryBox>)page.userData, b => b.Hue, (b, v) => b.Hue = v, b => b.Key, AllOf(items)),
                                    o => o.Id = "ed-held-many");
            c.Set(0.3f);
            page.RemoveFromHierarchy();
            return new WeakReference(page);
        }

        static Func<IReadOnlyList<GalleryBox>> AllOf(List<GalleryBox> l) => () => l;

        // A strip's items in order, by the names Reorder gives them under its Id.
        static List<VisualElement> Faces(Reorder<GalleryTab> r) => r.Root.Query<VisualElement>(r.Root.name + "-item").ToList();

        static string Texts(Reorder<GalleryTab> r)
        {
            var t = new List<string>();
            foreach (var f in Faces(r)) t.Add(f.Q<Label>(r.Root.name + "-text").text);
            return string.Join(",", t.ToArray());
        }

        static string Names(List<GalleryTab> l)
        {
            var t = new List<string>();
            foreach (var x in l) t.Add(x.Name);
            return string.Join(",", t.ToArray());
        }

        // A left press with a click count of two (the second press of a double-click), then its release.
        static void DoublePress(Vector2 panel, VisualElement root)
        {
            var ev = new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = panel };
            using (var e = PointerDownEvent.GetPooled(ev)) root.panel.visualTree.SendEvent(e);
            PointerAt(EventType.MouseUp, panel, root);
        }

        // A left drag in panel points: press, `steps` moves, release.
        IEnumerator DragAt(Vector2 from, Vector2 to, VisualElement root, int steps = 6)
        {
            PointerAt(EventType.MouseDown, from, root); yield return null;
            for (int i = 1; i <= steps; i++) { PointerAt(EventType.MouseMove, Vector2.Lerp(from, to, i / (float)steps), root); yield return null; }
            PointerAt(EventType.MouseUp, to, root); yield return null; yield return null;
        }

        static bool Shown(VisualElement e) => e != null && e.panel != null && e.resolvedStyle.display == DisplayStyle.Flex;

        // The tab strip over the canvas and the Lists page's vertical list: click, drag, Escape, lost capture,
        // rename, menus, and thirty tabs on one scrolling line.
        IEnumerator SectionStrip()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            VisualElement box = null;
            try
            {
                GalleryEditor.Nav.Go(GalleryEditor.CanvasItem); yield return null; yield return null;
                var m = GalleryEditor.Model;
                var hands = GalleryEditor.Hands; var feet = GalleryEditor.Feet;
                var root = win.Root;
                Check(hands != null && feet != null && Faces(hands).Count == 4 && Faces(feet).Count == 2, "the strip over the canvas has two groups: Hands with 4 items and Feet with 2");
                if (hands == null || feet == null || Faces(hands).Count != 4) yield break;
                Check(Mathf.Abs(hands.Root.worldBound.yMin - feet.Root.worldBound.yMin) <= 0.5f && feet.Root.worldBound.xMin > hands.Root.worldBound.xMax,
                    "...side by side", hands.Root.worldBound + " / " + feet.Root.worldBound);
                Check(GalleryEditor.Canvas != null && hands.Root.worldBound.yMax <= GalleryEditor.Canvas.Stage.worldBound.yMin, "...over the canvas");
                Check(Texts(hands) == "Hand 1,Hand 2,Hand 3,Hand 4" && Shown(Faces(hands)[0].Q<Label>("ed-hands-mark")) && !Shown(Faces(hands)[1].Q<Label>("ed-hands-mark")),
                    "each item shows its text, and the active one its mark", Texts(hands));

                var f = Faces(hands);
                PressAt(f[2].worldBound.center, root); yield return null; yield return null;
                Check(m.Picks.Count == 1 && m.Picks[0] == "Hand 3" && m.Current == m.Hands[2], "clicking item 2 calls Select with item 2", string.Join(",", m.Picks.ToArray()));
                GalleryEditor.Surface.Refresh();
                var accent = Theme.For(f[2])[Role.Accent];
                Check(Near(f[2].style.backgroundColor.value, accent) && !Near(f[1].style.backgroundColor.value, accent), "...and the selected look follows Selected on the next refresh");

                // Item 0 dropped between items 2 and 3.
                f = Faces(hands);
                var to = new Vector2((f[2].worldBound.center.x + f[3].worldBound.center.x) / 2f, f[2].worldBound.center.y);
                PointerAt(EventType.MouseDown, f[0].worldBound.center, root); yield return null;
                PointerAt(EventType.MouseMove, f[0].worldBound.center + new Vector2(8f, 0f), root); yield return null;
                PointerAt(EventType.MouseMove, to, root); yield return null;
                var drop = hands.Root.Q<VisualElement>("ed-hands-drop");
                Check(Shown(drop) && Mathf.Abs(drop.worldBound.center.x - (f[2].worldBound.xMax + f[3].worldBound.xMin) / 2f) <= 1.5f,
                    "a drag past 5 px shows the drop marker at the slot", drop == null ? "none" : drop.worldBound + "");
                PointerAt(EventType.MouseUp, to, root); yield return null; yield return null;
                Check(m.Moves.Count == 1 && m.Moves[0] == "ed-hands 0>2" && Names(m.Hands) == "Hand 2,Hand 3,Hand 1,Hand 4",
                    "dragging item 0 past item 2 calls Move(0, 2) once: order 1, 2, 0, 3", string.Join(";", m.Moves.ToArray()) + " / " + Names(m.Hands));
                Check(Texts(hands) == "Hand 2,Hand 3,Hand 1,Hand 4" && !Shown(drop) && m.Picks.Count == 1, "...the strip shows the new order, the marker goes, and nothing was picked", Texts(hands));

                f = Faces(hands);
                yield return DragAt(f[3].worldBound.center, new Vector2((f[0].worldBound.center.x + f[1].worldBound.center.x) / 2f, f[0].worldBound.center.y), root);
                Check(m.Moves.Count == 2 && m.Moves[1] == "ed-hands 3>1" && Names(m.Hands) == "Hand 2,Hand 4,Hand 3,Hand 1",
                    "dragging item 3 before item 1 calls Move(3, 1)", string.Join(";", m.Moves.ToArray()) + " / " + Names(m.Hands));

                f = Faces(hands);
                yield return DragAt(f[0].worldBound.center, f[0].worldBound.center + new Vector2(4f, 0f), root, 2);
                Check(m.Moves.Count == 2 && m.Picks.Count == 2, "a 4 px drag moves nothing: it is a click", m.Moves.Count + " / " + m.Picks.Count);

                // Escape mid-drag: no Move, and the window stays open.
                f = Faces(hands);
                PointerAt(EventType.MouseDown, f[0].worldBound.center, root); yield return null;
                PointerAt(EventType.MouseMove, f[2].worldBound.center, root); yield return null;
                Check(Shown(drop), "...(a drag is under way)");
                Input.HandleEscape(); yield return null;
                Check(!Shown(drop) && GalleryEditor.Surface.Visible, "Escape mid-drag cancels it and the window stays open");
                PointerAt(EventType.MouseUp, f[2].worldBound.center, root); yield return null; yield return null;
                Check(m.Moves.Count == 2 && Names(m.Hands) == "Hand 2,Hand 4,Hand 3,Hand 1", "...and calls no Move", string.Join(";", m.Moves.ToArray()));

                // A lost capture cancels too: the drag changed nothing yet.
                f = Faces(hands);
                PointerAt(EventType.MouseDown, f[0].worldBound.center, root); yield return null;
                PointerAt(EventType.MouseMove, f[2].worldBound.center, root); yield return null;
                ReleasePointer(root, f[2].worldBound.center); yield return null;
                PointerAt(EventType.MouseUp, f[2].worldBound.center, root); yield return null; yield return null;
                Check(m.Moves.Count == 2 && !Shown(drop), "a lost pointer capture cancels the drag with no Move", m.Moves.Count.ToString());

                // Rename: double-click, type, Enter.
                f = Faces(hands);
                DoublePress(f[1].worldBound.center, root); yield return null; yield return null;
                var field = hands.Root.Q<TextField>("ed-hands-rename");
                Check(field != null && field.value == "Hand 4" && !Shown(f[1].Q<Label>("ed-hands-text")) && Within(field.panel.focusController.focusedElement as VisualElement, field),
                    "a double-click swaps the item's text for a focused field holding its name", field == null ? "none" : field.value);
                if (field != null)
                {
                    field.value = "Renamed";
                    Key(field, KeyCode.Return); yield return null; yield return null;
                }
                Check(m.Renames == 1 && m.Hands[1].Name == "Renamed" && hands.Root.Q<TextField>("ed-hands-rename") == null && Texts(hands) == "Hand 2,Renamed,Hand 3,Hand 1",
                    "typing Renamed and pressing Enter calls Rename once and shows it", m.Renames + " / " + Texts(hands));

                hands.BeginRename(m.Hands[2]); yield return null;
                field = hands.Root.Q<TextField>("ed-hands-rename");
                if (field != null) field.value = "Typed";
                Input.HandleEscape(); yield return null; yield return null;
                Check(field != null && m.Renames == 1 && m.Hands[2].Name == "Hand 3" && hands.Root.Q<TextField>("ed-hands-rename") == null && GalleryEditor.Surface.Visible,
                    "BeginRename opens the field; Escape calls nothing and leaves the window open", m.Renames + " / " + m.Hands[2].Name);
                hands.BeginRename(m.Hands[2]); yield return null;
                field = hands.Root.Q<TextField>("ed-hands-rename");
                if (field != null) { field.value = "Typed"; Key(field, KeyCode.Escape); yield return null; }
                Check(field != null && m.Renames == 1 && hands.Root.Q<TextField>("ed-hands-rename") == null, "...an Escape key in the field too");
                hands.BeginRename(m.Hands[2]); yield return null;
                field = hands.Root.Q<TextField>("ed-hands-rename");
                if (field != null) { field.value = "  "; Key(field, KeyCode.Return); yield return null; }
                Check(field != null && m.Renames == 1 && m.Hands[2].Name == "Hand 3", "...and an empty name cancels");

                // Menus: the item's at the pointer, Add's under ＋, the title's under the title.
                f = Faces(hands);
                var at = f[0].worldBound.center;
                PressAt(at, root, 1); yield return null; yield return null;
                var ren = LiveLabel("Rename");
                Check(ren != null && Mathf.Abs(ren.parent.parent.worldBound.xMin - at.x) <= 1f, "a right-click opens the item's menu at the pointer");
                Popup.CloseAll(); yield return null;
                var add = hands.Root.Q<Button>("ed-hands-add");
                if (add != null) { Submit(add); yield return null; yield return null; }
                var nt = LiveLabel("New tab");
                Check(nt != null && nt.worldBound.yMin >= add.worldBound.yMax, "＋ opens Add() under it");
                if (nt != null) { Release(nt.parent); yield return null; yield return null; }
                Check(m.Adds == 1 && Faces(hands).Count == 5, "...and its item adds a tab", Faces(hands).Count.ToString());
                var title = feet.Root.Q<Button>("ed-feet-title");
                Check(title != null && hands.Root.Q<Button>("ed-hands-title") == null && hands.Root.Q<Label>("ed-hands-title") != null, "a TitleMenu makes the title a button; without one it is a caption");
                if (title != null) { Submit(title); yield return null; yield return null; }
                var gm = LiveLabel("Group menu");
                // The card (the row's parent's parent) sits the popup's 4 px gap below the title, its left within the title.
                var gcard = gm == null ? null : gm.parent.parent;
                Rect tb = title == null ? new Rect() : title.worldBound, gb = gcard == null ? new Rect() : gcard.worldBound;
                Check(gcard != null && Mathf.Abs(gb.yMin - (tb.yMax + 4f)) <= 1f && gb.xMin >= tb.xMin - 1f && gb.xMin <= tb.xMax,
                    "...that opens TitleMenu under it", gb + " under " + tb);
                if (gm != null) { Release(gm.parent); yield return null; }
                Check(gm != null && m.TitleMenus == 1, "...and runs its item", m.TitleMenus.ToString());

                // Rename from the item's menu: a right-click, a click on Rename, the name typed, Enter; one undo step.
                History.Clear();
                f = Faces(hands);
                var named = m.Hands[0];
                string was = named.Name;
                PressAt(f[0].worldBound.center, root, 1); yield return null; yield return null;
                var mren = LiveLabel("Rename");
                if (mren != null) { PressAt(mren.parent.worldBound.center, mren); yield return null; yield return null; yield return null; }
                field = hands.Root.Q<TextField>("ed-hands-rename");
                Check(mren != null && field != null && field.value == was && field.panel != null && Within(field.panel.focusController.focusedElement as VisualElement, field),
                    "Rename in an item's menu opens a focused field holding its name", mren == null ? "no menu" : field == null ? "no field" : field.value);
                if (field != null) { field.value = "From menu"; Key(field, KeyCode.Return); yield return null; yield return null; }
                Check(named.Name == "From menu" && hands.Root.Q<TextField>("ed-hands-rename") == null && Texts(hands).StartsWith("From menu,") && History.CanUndo,
                    "...typing a name and pressing Enter renames the item", named.Name + " / " + Texts(hands));
                Check(History.Undo() && named.Name == was && !History.CanUndo, "...in one undo step", named.Name);

                // Thirty tabs: one line that scrolls, its last item reachable.
                float h0 = hands.Root.worldBound.height;
                while (m.Hands.Count < 30) m.Hands.Add(new GalleryTab("more" + m.Hands.Count, "More " + m.Hands.Count));
                hands.Refresh(); yield return null; yield return null;
                f = Faces(hands);
                float y0 = f[0].worldBound.yMin, spread = 0f;
                foreach (var x in f) spread = Mathf.Max(spread, Mathf.Abs(x.worldBound.yMin - y0));
                Check(f.Count == 30 && spread <= 0.5f && Mathf.Abs(hands.Root.worldBound.height - h0) <= 0.5f, "thirty tabs stay on one line of the same height", f.Count + " / " + spread + " / " + hands.Root.worldBound.height + " vs " + h0);
                Check(feet.Root.worldBound.xMax <= GalleryEditor.Right.worldBound.xMax + 0.5f, "...within the column", feet.Root.worldBound.xMax + " vs " + GalleryEditor.Right.worldBound.xMax);
                var sv = hands.Root.Q<ScrollView>();
                float x0 = sv == null ? 0f : sv.scrollOffset.x;
                if (sv != null) Skin.Wheel(GalleryEditor.Surface.Root, Ctr(f[1]), -1f);
                Check(sv != null && sv.scrollOffset.x > x0, "...the wheel scrolls it", sv == null ? "no scroll" : sv.scrollOffset.x + "");
                var over = Ctr(f[1]);   // a fixed point over the strip: the item itself scrolls away
                for (int i = 0; i < 200 && sv != null; i++) { float b = sv.scrollOffset.x; Skin.Wheel(GalleryEditor.Surface.Root, over, -1f); if (sv.scrollOffset.x == b) break; }
                yield return null;
                var last = Faces(hands)[29];
                Check(sv != null && last.worldBound.xMax <= sv.contentViewport.worldBound.xMax + 0.5f && last.worldBound.xMin >= sv.contentViewport.worldBound.xMin,
                    "...and its last item can be scrolled into view", sv == null ? "" : last.worldBound + " in " + sv.contentViewport.worldBound + " offset " + sv.scrollOffset.x
                    + " content " + sv.contentContainer.layout.width);
                // A drag to the strip's edge scrolls it back toward the start.
                f = Faces(hands);
                float before = sv == null ? 0f : sv.scrollOffset.x;
                var vp = sv == null ? new Rect() : sv.contentViewport.worldBound;
                PointerAt(EventType.MouseDown, f[29].worldBound.center, root); yield return null;
                PointerAt(EventType.MouseMove, f[29].worldBound.center - new Vector2(10f, 0f), root); yield return null;
                PointerAt(EventType.MouseMove, new Vector2(vp.xMin + 2f, vp.center.y), root); yield return null;
                PointerAt(EventType.MouseMove, new Vector2(vp.xMin + 3f, vp.center.y), root); yield return null;
                Check(sv != null && sv.scrollOffset.x < before, "...and a drag held near its edge scrolls it", before + " -> " + (sv == null ? 0f : sv.scrollOffset.x));
                float held0 = sv == null ? 0f : sv.scrollOffset.x;
                yield return new WaitForSecondsRealtime(0.25f);
                float held1 = sv == null ? 0f : sv.scrollOffset.x;
                Check(sv != null && held1 < held0 - 10f, "...and keeps scrolling while the pointer is held still there", held0 + " -> " + held1);
                if (Input.Gesture != null) Input.HandleEscape();
                yield return null;
                float ended0 = sv == null ? 0f : sv.scrollOffset.x;
                yield return new WaitForSecondsRealtime(0.15f);
                Check(sv != null && sv.scrollOffset.x == ended0, "...and stops when the drag ends", ended0 + " -> " + (sv == null ? 0f : sv.scrollOffset.x));
                PointerAt(EventType.MouseUp, vp.center, root); yield return null;

                // Without Scroll a strip wraps inside its parent.
                box = Ui.Box();
                box.style.width = 300f;
                GalleryEditor.Page.Insert(0, box);
                var many = GalleryTab.Make("w", "Wrapped", 30);
                var wrap = Controls.Reorder<GalleryTab>(box, o => { o.Id = "wrap"; o.Items = () => many; o.Key = t => t.Key; o.Text = t => t.Name; });
                yield return null; yield return null;
                var wf = wrap.Root.Query<VisualElement>("wrap-item").ToList();
                float maxX = 0f, maxY = 0f;
                foreach (var x in wf) { maxX = Mathf.Max(maxX, x.worldBound.xMax); maxY = Mathf.Max(maxY, x.worldBound.yMin); }
                Check(wf.Count == 30 && maxY > wf[0].worldBound.yMin + 1f && maxX <= box.worldBound.xMax + 0.5f, "a strip without Scroll wraps onto new lines within its parent",
                    wf.Count + " / y " + wf[0].worldBound.yMin + ".." + maxY + " / x " + maxX + " vs " + box.worldBound + " / first " + (wf.Count > 0 ? wf[0].worldBound + "" : ""));

                // A rebuild while a rename is open closes it with no Rename, and builds each item once.
                var rl = GalleryTab.Make("r", "R", 3);
                int renamed = 0;
                var rr = Controls.Reorder<GalleryTab>(box, o => { o.Id = "rb"; o.Items = () => rl; o.Key = t => t.Key; o.Text = t => t.Name; o.Rename = (t, n) => { renamed++; t.Name = n; }; });
                yield return null;
                rr.BeginRename(rl[1]); yield return null; yield return null;
                var rbf = rr.Root.Q<TextField>("rb-rename");
                if (rbf != null) rbf.value = "Typed";
                rl.RemoveAt(0); rr.Refresh(); yield return null; yield return null;
                int rbn = rr.Root.Query<VisualElement>("rb-item").ToList().Count;
                Check(rbf != null && renamed == 0 && rl[0].Name == "R 2" && rl[1].Name == "R 3" && rr.Root.Q<TextField>("rb-rename") == null && rbn == 2,
                    "a rebuild during a rename closes the field with no Rename and builds each item once", renamed + " / " + Names(rl) + " / " + rbn);

                // No Key: the item itself is the key, so two items with one text stay two.
                var same = new List<GalleryTab> { new GalleryTab("x", "Same"), new GalleryTab("y", "Same") };
                var er = Controls.Reorder<GalleryTab>(box, o => { o.Id = "eq"; o.Items = () => same; o.Text = t => t.Name; o.Build = (e, t) => e.Add(new Label(t.Key) { name = "eq-tag" }); });
                yield return null;
                same.Reverse(); er.Refresh(); yield return null;
                var tags = new List<string>();
                foreach (var l in er.Root.Query<Label>("eq-tag").ToList()) tags.Add(l.text);
                Check(string.Join(",", tags.ToArray()) == "y,x", "with no Key two items with the same text stay distinct: a swap rebuilds them", string.Join(",", tags.ToArray()));
                var ids = new List<string> { "id1", "id2" };
                var shownAs = new Dictionary<string, string> { { "id1", "One" }, { "id2", "Two" } };
                var sr = Controls.Reorder<string>(box, o => { o.Id = "ids"; o.Items = () => ids; o.Text = id => shownAs[id]; });
                yield return null;
                var sf0 = sr.Root.Q<VisualElement>("ids-item");
                shownAs["id1"] = "Uno"; sr.Refresh(); yield return null;
                var sf1 = sr.Root.Q<VisualElement>("ids-item");
                Check(sf0 != null && sf0 == sf1 && sf1.Q<Label>("ids-text").text == "Uno", "...and a string item is its own key: a new text keeps its item", sf1 == null ? "none" : sf1.Q<Label>("ids-text").text);

                // The Lists page's vertical list reorders the same way.
                GalleryEditor.Nav.Go(GalleryEditor.ListsItem); yield return null; yield return null;
                var rows = GalleryEditor.RowList;
                var rf = rows == null ? new List<VisualElement>() : Faces(rows);
                Check(rf.Count == 5 && Mathf.Abs(rf[0].worldBound.xMin - rf[1].worldBound.xMin) <= 0.5f && rf[1].worldBound.yMin >= rf[0].worldBound.yMax, "the Lists page stacks its rows", rf.Count.ToString());
                if (rf.Count == 5)
                {
                    yield return DragAt(rf[0].worldBound.center, new Vector2(rf[2].worldBound.center.x, (rf[2].worldBound.center.y + rf[3].worldBound.center.y) / 2f), root);
                    Check(m.Moves.Count == 3 && m.Moves[2] == "ed-rows 0>2" && Names(m.Rows) == "Row 2,Row 3,Row 1,Row 4,Row 5", "...and a drag reorders them: Move(0, 2)",
                        string.Join(";", m.Moves.ToArray()) + " / " + Names(m.Rows));
                }
            }
            finally { Popup.CloseAll(); box?.RemoveFromHierarchy(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }

        // Popup.Confirm, the status line and an Info row's tone.
        IEnumerator SectionDialogStatus()
        {
            GalleryEditor.Model = new EditorModel();
            var win = GalleryEditor.Open(); yield return null; yield return null;
            Popup p = null;
            try
            {
                GalleryEditor.Nav.Go(GalleryEditor.ListsItem); yield return null; yield return null;
                var owner = GalleryEditor.ListsPage;
                int yes = 0, no = 0;
                Action<bool> done = r => { if (r) yes++; else no++; };
                p = Popup.Confirm(owner, "Probe confirm", "Probe body", done); yield return null; yield return null;
                var ok = p?.Content.Q<Button>("confirm-ok");
                var cancel = p?.Content.Q<Button>("confirm-cancel");
                var surface = Popup.OwnerOf(p?.Content);
                Check(ok != null && cancel != null && ok.text == "OK" && cancel.text == "Cancel" && LiveLabel("Probe confirm") != null && LiveLabel("Probe body") != null,
                    "Confirm shows its title, body, OK and Cancel");
                Check(surface != null && surface.Layer == Layer.Modal && surface.Visible && Ui.Stack[Ui.Stack.Count - 1] == surface, "...on the Modal layer, above every window");
                Check(ok != null && ok.panel.focusController.focusedElement == ok, "...with OK focused");
                Check(p != null && Mathf.Abs(p.Content.worldBound.center.x - win.Root.worldBound.center.x) <= 1f && Mathf.Abs(p.Content.worldBound.center.y - win.Root.worldBound.center.y) <= 1f,
                    "...centred over the owner's window", p == null ? "" : p.Content.worldBound.center + " vs " + win.Root.worldBound.center);
                if (ok != null) { Submit(ok); yield return null; yield return null; }
                Check(yes == 1 && no == 0 && p != null && !p.IsOpen && surface != null && !surface.Visible, "a submit on OK gives done(true) once and closes it", yes + "/" + no);

                p = Popup.Confirm(owner, "Probe confirm", "Probe body", done); yield return null; yield return null;
                Input.HandleEscape(); yield return null; yield return null;
                Check(yes == 1 && no == 1 && !p.IsOpen && GalleryEditor.Surface.Visible, "Escape gives done(false) once, and the window under it stays open", yes + "/" + no);

                p = Popup.Confirm(owner, "Probe confirm", "Probe body", done); yield return null; yield return null;
                PressAt(new Vector2(3f, 3f), Popup.OwnerOf(p.Content).Root); yield return null; yield return null;   // the dialogs' own panel
                Check(yes == 1 && no == 2 && !p.IsOpen, "an outside click gives done(false)", yes + "/" + no);

                p = Popup.Confirm(owner, "Probe confirm", "Probe body", done); yield return null; yield return null;
                cancel = p.Content.Q<Button>("confirm-cancel");
                if (cancel != null) { Submit(cancel); yield return null; yield return null; }
                Check(no == 3 && !p.IsOpen, "Cancel gives done(false)", no.ToString());

                p = Popup.Confirm(owner, "Probe confirm", "Probe body", done, o => { o.Danger = true; o.Ok = "Remove"; o.Cancel = null; }); yield return null; yield return null;
                ok = p.Content.Q<Button>("confirm-ok");
                Check(ok != null && ok.text == "Remove" && Near(ok.resolvedStyle.color, Theme.For(ok)[Role.Danger]) && p.Content.Q<Button>("confirm-cancel") == null,
                    "Danger paints OK in the Danger look; a null Cancel shows OK alone");
                Input.HandleEscape(); yield return null; yield return null;
                Check(no == 4, "...and Escape still gives done(false)", no.ToString());

                p = Popup.Confirm(null, "Probe confirm", "Probe body", done); yield return null; yield return null;
                var screen = p.Content.panel.visualTree.layout;
                Check(Mathf.Abs(p.Content.worldBound.center.x - screen.center.x) <= 1f && Mathf.Abs(p.Content.worldBound.center.y - screen.center.y) <= 1f,
                    "with no owner it is centred on the screen", p.Content.worldBound.center + " vs " + screen.center);
                p.Close(); yield return null;
                Check(no == 5 && yes == 1, "Close gives done(false)");
                GalleryEditor.Surface.Visible = false; yield return null;
                GalleryEditor.Surface.Visible = true; yield return null;
                Check(no == 5 && yes == 1, "...and done never runs twice");

                // The status line.
                var st = GalleryEditor.Status;
                var text = st.Root.Q<Label>();
                st.Show("Saved", Role.Accent, 0.2f); yield return null;
                Check(text.text == "Saved" && Near(text.resolvedStyle.color, Theme.For(text)[Role.Accent]), "Status.Show(key, role, seconds) shows the key in that role", text.text);
                yield return new WaitForSecondsRealtime(0.45f);
                Check(text.text == "", "...and is empty after it faded", text.text);
                int count = 3;
                st.Show(() => count + " boxes");
                Check(text.text == "3 boxes", "Status.Show(func) shows what it returns", text.text);
                count = 5;
                GalleryEditor.Surface.Refresh();
                Check(text.text == "5 boxes", "...and follows it on refresh", text.text);
                st.Show("Saved", Role.Accent, 0.2f); yield return null;
                yield return new WaitForSecondsRealtime(0.45f);
                Check(text.text == "5 boxes" && text.resolvedStyle.opacity == 1f, "a timed message falls back to the resting line", text.text + " " + text.resolvedStyle.opacity);
                st.Clear();
                st.Show(() => "formatted", Role.TextMuted, 0.2f); yield return null;
                yield return new WaitForSecondsRealtime(0.45f);
                Check(text.text == "", "a shown func fades too", text.text);
                st.Show("Saved"); st.Clear();
                Check(text.text == "", "Clear empties it");
                using (Motion.Hold(1f, false))
                {
                    st.Show("Saved", Role.Accent, 0.1f);
                    yield return new WaitForSecondsRealtime(0.16f);   // past the delay, inside the fade
                    st.Show("Failed", Role.Danger);
                    yield return new WaitForSecondsRealtime(0.3f);    // past where the old fade would end
                    Check(text.text == "Failed" && text.resolvedStyle.opacity == 1f, "a Show during a running fade stays visible", text.text + " " + text.resolvedStyle.opacity);
                    st.Show("Saved", Role.Accent, 0.1f);
                    yield return new WaitForSecondsRealtime(0.16f);
                    st.Clear(); st.Show("Again");
                    yield return new WaitForSecondsRealtime(0.3f);
                    Check(text.text == "Again" && text.resolvedStyle.opacity == 1f, "...and after a Clear during it", text.text + " " + text.resolvedStyle.opacity);
                    st.Clear();
                }

                // Tone on Info.
                var tone = Role.Danger;
                var info = Controls.Info(GalleryEditor.ListsPage, "Toned", () => "value", o => o.Tone = () => tone);
                yield return null;
                Check(Near(info.Input.resolvedStyle.color, Theme.For(info.Input)[Role.Danger]), "an Info row paints its value in Tone()'s role");
                tone = Role.Accent;
                GalleryEditor.Surface.Refresh(); yield return null;
                Check(Near(info.Input.resolvedStyle.color, Theme.For(info.Input)[Role.Accent]), "...re-read on each refresh");
                Check(typeof(Controls).GetMethod("Info", new[] { typeof(VisualElement), typeof(string), typeof(Func<string>), typeof(Action<ControlOptions>) }) != null,
                    "Info still has its Action<ControlOptions> signature, so a mod built against it loads");
            }
            finally { Popup.CloseAll(); GalleryEditor.Close(); GalleryEditor.Model = new EditorModel(); }
        }
    }
}
