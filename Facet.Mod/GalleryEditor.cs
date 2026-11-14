using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Plain data the Editor window's pages write into.
    public sealed class EditorModel
    {
        /// The Undo page: a toggle and a slider over plain fields, a row that records nothing, and a
        /// row whose steps are bound to Doc. AmountWrites counts the slider's setter runs.
        public bool Flag;
        public float Amount = 0.2f;
        public int AmountWrites;
        public bool Quiet;
        public float Bound;
        public object Doc = new object();

        /// The Canvas page: twelve boxes, and a second set (Tab 1) that only Find reaches while Tab 0 shows. Their
        /// steps are bound to BoxDoc; Limit is read when the page builds.
        public List<GalleryBox> Boxes = GalleryEditor.NewBoxes();
        public List<GalleryBox> Other = new List<GalleryBox>
        {
            new GalleryBox("o1", "Other 1", new Rect(20, 20, 60, 40)),
            new GalleryBox("o2", "Other 2", new Rect(110, 20, 60, 40)),
            new GalleryBox("o3", "Other 3", new Rect(200, 20, 60, 40)),
        };
        public int Tab;
        public object BoxDoc = new object();
        public float Limit = 8000f;

        /// The Canvas page's menus count their runs; Fail makes File › New throw.
        public int News, Recents, Removes, Undos, Copies, CopyStyles, Pastes;
        public bool Fail;
    }

    /// A plain box on the Canvas page. Not "Box": Core's internal Box struct is visible to this assembly.
    public sealed class GalleryBox
    {
        public string Key, Name;
        public Rect Rect;
        public GalleryBox(string key, string name, Rect rect) { Key = key; Name = name; Rect = rect; }
    }

    // The Editor window: one Nav page per editor part, on the Gallery's host.
    public static class GalleryEditor
    {
        public static Surface Surface { get; private set; }
        public static Nav Nav { get; private set; }
        /// Replaced wholesale by the autotest, like Gallery.Model.
        public static EditorModel Model { get; set; } = new EditorModel();
        static Window _win;
        public static NavItem UndoItem { get; private set; }
        /// The Undo page's slider and document-bound row, as last built.
        public static Control<float> AmountRow { get; private set; }
        public static Control<float> BoundRow { get; private set; }
        public static NavItem CanvasItem { get; private set; }
        /// The Canvas page's canvas and the page itself, as last built.
        public static Canvas<GalleryBox> Canvas { get; private set; }
        public static VisualElement Page { get; private set; }
        /// The Canvas page's menu bar and the menus it shows, as last built.
        public static MenuBar Bar { get; private set; }
        public static IList<MenuItem> MenuItems { get; private set; }
        /// Snap targets beside the boxes, like another tab's items: drawn dim, never selected.
        public static readonly Rect[] Ghosts = { new Rect(400, 20, 60, 40), new Rect(400, 100, 60, 40) };

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Gallery.Host, "FacetGalleryEditor", Layer.Window);
            _win = new Window(Surface.Root, "editor", "Facet Editor", new Rect(140, 70, 900, 600));
            _win.CloseRequested += _ => Close();
            _win.OnRaise = () => { if (Surface != null) Surface.BringToFront(); };   // a press raises it, so its shortcuts answer
            Nav = new Nav(_win.Body, new[] { NavItem.Heading(null, UndoItem = new NavItem("Undo", Undo), CanvasItem = new NavItem("Canvas", CanvasPage)) });
            return _win;
        }

        static readonly Func<KeyCode, bool> KeyDown = UnityEngine.Input.GetKeyDown;

        /// The host's per-frame key poll: the menus' shortcuts, while the menu bar is on screen.
        public static void PollKeys()
        {
            if (Surface == null || !Surface.Visible || Bar == null || MenuItems == null) return;
            bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)
                     || UnityEngine.Input.GetKey(KeyCode.LeftCommand) || UnityEngine.Input.GetKey(KeyCode.RightCommand);
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            bool alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
            Menus.HandleKeys(Bar.Root, MenuItems, KeyDown, ctrl, shift, alt);
        }

        static void Undo(VisualElement p)
        {
            Controls.Toggle(p, "Flag", Bind.To(() => Model.Flag, v => Model.Flag = v), o => o.Id = "ed-flag");
            AmountRow = Controls.Slider(p, "Amount", Bind.To(() => Model.Amount, v => { Model.Amount = v; Model.AmountWrites++; }), o => o.Id = "ed-amount");
            Controls.Toggle(p, "Quiet", Bind.To(() => Model.Quiet, v => Model.Quiet = v), o => { o.Id = "ed-quiet"; o.Undoable = false; });
            BoundRow = Controls.Slider(p, "Bound", Bind.To(() => Model.Bound, v => Model.Bound = v), o => { o.Id = "ed-bound"; o.UndoDocument = () => Model.Doc; });
        }

        // File and Edit, with a separator, a submenu, a Danger row, a disabled row and a checked row.
        static List<MenuItem> BuildMenus(Func<Canvas<GalleryBox>> canvas)
        {
            return new List<MenuItem>
            {
                new MenuItem { Text = "File", Id = "ed-file", Items = new List<MenuItem>
                {
                    new MenuItem { Text = "New", Run = () => { if (Model.Fail) throw new InvalidOperationException("New failed on purpose"); Model.News++; } },
                    MenuItem.Separator,
                    new MenuItem { Text = "Recent", Items = new List<MenuItem> { new MenuItem { Text = "Recent 1", Shortcut = "Ctrl+1", Run = () => Model.Recents++ } } },
                    new MenuItem { Text = "Remove", Danger = true, Run = () => Model.Removes++ },
                } },
                new MenuItem { Text = "Edit", Id = "ed-edit", Items = new List<MenuItem>
                {
                    new MenuItem { Id = "ed-undo", TextOf = () => "Undo " + History.NextUndoText, Shortcut = "Ctrl+Z", Run = () => { Model.Undos++; History.Undo(); } },
                    MenuItem.Separator,
                    new MenuItem { Text = "Copy", Shortcut = "Ctrl+C", Run = () => Model.Copies++ },
                    new MenuItem { Text = "Copy Style", Shortcut = "Ctrl+Shift+C", Run = () => Model.CopyStyles++ },
                    new MenuItem { Text = "Paste", Shortcut = "Ctrl+V", Enabled = () => false, Run = () => Model.Pastes++ },
                    MenuItem.Separator,
                    new MenuItem { Text = "Snap", Checked = () => canvas() != null && canvas().Snap, Run = () => { var c = canvas(); if (c != null) c.Snap = !c.Snap; } },
                } },
            };
        }

        /// Four rows of 60 x 40 boxes, 90 apart across and 80 down; box 12 is 83 wide, a width off the grid.
        internal static List<GalleryBox> NewBoxes()
        {
            var l = new List<GalleryBox>();
            for (int i = 0; i < 12; i++)
                l.Add(new GalleryBox("k" + (i + 1), "Box " + (i + 1), new Rect(20 + i % 4 * 90, 20 + i / 4 * 80, i == 11 ? 83 : 60, 40)));
            return l;
        }

        static void CanvasPage(VisualElement p)
        {
            Page = p;
            Canvas<GalleryBox> c = null;
            Bar = Controls.MenuBar(p, MenuItems = BuildMenus(() => c));
            var drawn = new Dictionary<string, VisualElement>();
            c = Canvas = Controls.Canvas<GalleryBox>(p, "Canvas", o =>
            {
                o.Id = "ed-canvas";
                o.Items = () => Model.Tab == 0 ? Model.Boxes : Model.Other;
                o.Key = b => b.Key;
                o.Find = k => Model.Boxes.Find(b => b.Key == k) ?? Model.Other.Find(b => b.Key == k);
                o.GetRect = b => b.Rect;
                o.SetRect = (b, r) => b.Rect = r;
                o.Size = new Vector2(900, 390);
                o.Ghosts = () => Ghosts;
                o.UndoDocument = () => Model.BoxDoc;
                o.Limit = Model.Limit;
            });
            Bar.Tools.Label(() => c.Selection.Count + " selected");
            Bar.Tools.Toggle("Snap", Bind.To(() => c.Snap, v => c.Snap = v), "Snap to the canvas centre, the grid and other boxes");
            Bar.Tools.UndoRedo();
            foreach (var g in Ghosts) Place(Plain(c.Content, new Color(0.6f, 0.6f, 0.65f, 0.25f)), g);
            Action draw = () => Draw(c, drawn);
            c.Changed += draw;
            draw();
            Ui.Btn("Replace", () =>
            {
                // New objects with the same keys, as an import or an undo that rebuilds its document would make.
                var l = Model.Tab == 0 ? Model.Boxes : Model.Other;
                for (int i = 0; i < l.Count; i++) l[i] = new GalleryBox(l[i].Key, l[i].Name, l[i].Rect);
                c.Refresh();
                draw();
            }, p).name = "ed-replace";
        }

        // Every box where its rect says, in the set Items shows; a different set of keys is drawn afresh.
        static void Draw(Canvas<GalleryBox> c, Dictionary<string, VisualElement> drawn)
        {
            var items = Model.Tab == 0 ? Model.Boxes : Model.Other;
            bool same = drawn.Count == items.Count;
            for (int i = 0; i < items.Count && same; i++) same = drawn.ContainsKey(items[i].Key);
            if (!same)
            {
                foreach (var e in drawn.Values) e.RemoveFromHierarchy();
                drawn.Clear();
                foreach (var b in items)
                {
                    var e = drawn[b.Key] = Plain(c.Content, new Color(0.25f, 0.45f, 0.8f, 0.85f));
                    e.name = "ed-box-" + b.Key;
                    Ui.Text(b.Name, e).pickingMode = PickingMode.Ignore;
                }
            }
            for (int i = 0; i < items.Count; i++) Place(drawn[items[i].Key], items[i].Rect);
        }

        static VisualElement Plain(VisualElement content, Color colour)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.backgroundColor = colour;
            content.Add(e);
            return e;
        }

        static void Place(VisualElement e, Rect r)
        {
            e.style.left = r.x; e.style.top = r.y;
            e.style.width = r.width; e.style.height = r.height;
        }

        public static void Close()
        {
            if (Surface == null) return;
            Surface.Dispose();
            Surface = null; Nav = null; _win = null; AmountRow = null; BoundRow = null; Canvas = null; Page = null; Bar = null; MenuItems = null;
        }
    }
}
