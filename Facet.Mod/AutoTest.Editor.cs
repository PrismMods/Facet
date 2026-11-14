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
