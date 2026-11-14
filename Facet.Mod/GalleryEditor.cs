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
        public int News, Recents, Removes, Undos, Copies, CopyStyles, Pastes, Fronts;
        public bool Fail;

        /// The canvas's Delete and ZOrder calls, and Edit > Delete's runs; the key of the item its context menu was last
        /// asked for ("" for empty stage); NoMenu makes that menu null.
        public int Deletes, ZSteps, MenuDeletes;
        public string MenuFor;
        public bool NoMenu;

        /// The tab strip over the canvas (Hands, Feet) and the Lists page's rows: the picked tab, the one marked
        /// active, and what each callback was handed. Moves reads "<id> from>to".
        public List<GalleryTab> Hands = GalleryTab.Make("hand", "Hand", 4), Feet = GalleryTab.Make("foot", "Foot", 2), Rows = GalleryTab.Make("row", "Row", 5);
        public GalleryTab Current;
        public string ActiveKey = "hand1";
        public List<string> Picks = new List<string>(), Moves = new List<string>();
        public int Renames, Adds, TitleMenus, Made;
    }

    /// A tab in the strip or a row in the Lists page's list.
    public sealed class GalleryTab
    {
        public string Key, Name;
        public GalleryTab(string key, string name) { Key = key; Name = name; }

        internal static List<GalleryTab> Make(string key, string name, int n)
        {
            var l = new List<GalleryTab>();
            for (int i = 1; i <= n; i++) l.Add(new GalleryTab(key + i, name + " " + i));
            return l;
        }
    }

    /// A plain box on the Canvas page. Not "Box": Core's internal Box struct is visible to this assembly.
    public sealed class GalleryBox
    {
        public string Key, Name;
        public Rect Rect;
        /// What the Canvas page's inspector edits: nothing the canvas itself reads.
        public float Hue, Size = 1f;
        public Color Tint = Color.white;
        public int Shape, Pattern, Weight;
        public bool Bold;
        public string Hotkey = "", Note = "";
        public float[] Values = new float[10];
        public bool[] Flags = new bool[6];
        public GalleryBox(string key, string name, Rect rect) { Key = key; Name = name; Rect = rect; }

        /// A new object with the same key and values, as an import or a rebuilt document makes.
        public GalleryBox Copy()
        {
            var b = (GalleryBox)MemberwiseClone();
            b.Values = (float[])Values.Clone(); b.Flags = (bool[])Flags.Clone();
            return b;
        }
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
        /// The Canvas page's right column, its inspector scroll and the Live region in it, as last built.
        public static VisualElement Left { get; private set; }
        /// The line after the Canvas page's columns.
        public static Label Footer { get; private set; }
        public static VisualElement Right { get; private set; }
        public static ScrollView InspectorScroll { get; private set; }
        public static VisualElement Inspector { get; private set; }
        /// The inspector's hue slider, as last built.
        public static Control<float> HueRow { get; private set; }
        public static IList<MenuItem> MenuItems { get; private set; }
        /// The strip's two groups over the canvas, and the Lists page's list, status line and page, as last built.
        public static Reorder<GalleryTab> Hands { get; private set; }
        public static Reorder<GalleryTab> Feet { get; private set; }
        public static Reorder<GalleryTab> RowList { get; private set; }
        public static StatusLine Status { get; private set; }
        public static NavItem ListsItem { get; private set; }
        public static VisualElement ListsPage { get; private set; }
        /// Snap targets beside the boxes, like another tab's items: drawn dim, never selected.
        public static readonly Rect[] Ghosts = { new Rect(400, 20, 60, 40), new Rect(400, 100, 60, 40) };

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Gallery.Host, "FacetGalleryEditor", Layer.Window);
            // Wide enough that the Canvas page's right column (64 of 100) gives the stage about the width a whole page did.
            _win = new Window(Surface.Root, "editor", "Facet Editor", new Rect(40, 40, 1300, 700));
            _win.CloseRequested += _ => Close();
            _win.OnRaise = () => { if (Surface != null) Surface.BringToFront(); };   // a press raises it, so its shortcuts answer
            Nav = new Nav(_win.Body, new[] { NavItem.Heading(null, UndoItem = new NavItem("Undo", Undo), CanvasItem = new NavItem("Canvas", CanvasPage),
                ListsItem = new NavItem("Lists", Lists)) });
            return _win;
        }

        static readonly Func<KeyCode, bool> KeyDown = UnityEngine.Input.GetKeyDown, KeyHeld = UnityEngine.Input.GetKey;

        /// False while the autotest drives the keys itself through PollKeys(down, held).
        public static bool HostKeys = true;

        /// The host's per-frame key poll.
        public static void PollKeys() { if (HostKeys) PollKeys(KeyDown, KeyHeld); }

        /// The canvas's keys first; the menus' shortcuts (while the menu bar is on screen) only when it did nothing, so a
        /// key that is both (Delete) runs once.
        /// True when the canvas took the keys.
        internal static bool PollKeys(Func<KeyCode, bool> down, Func<KeyCode, bool> held)
        {
            if (Surface == null || !Surface.Visible) return false;
            if (Canvas != null && Canvas.PollKeys(down, held)) return true;
            if (Bar == null || MenuItems == null) return false;
            bool ctrl = held(KeyCode.LeftControl) || held(KeyCode.RightControl) || held(KeyCode.LeftCommand) || held(KeyCode.RightCommand);
            bool shift = held(KeyCode.LeftShift) || held(KeyCode.RightShift);
            bool alt = held(KeyCode.LeftAlt) || held(KeyCode.RightAlt);
            Menus.HandleKeys(Bar.Root, MenuItems, down, ctrl, shift, alt);
            return false;
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
                    new MenuItem { Text = "Delete", Shortcut = "Delete", Run = () => { Model.MenuDeletes++; canvas()?.DeleteSelection(); } },
                    new MenuItem { Text = "Bring to front", Shortcut = "Ctrl+]", Run = () => Model.Fronts++ },
                    MenuItem.Separator,
                    new MenuItem { Text = "Snap", Checked = () => canvas() != null && canvas().Snap, Run = () => { var c = canvas(); if (c != null) c.Snap = !c.Snap; } },
                } },
            };
        }

        /// Four rows of 60 x 40 boxes, 90 apart across and 80 down; box 12 is 83 wide, a width off the grid. Each box's
        /// hue, tint, shape, pattern, weight, bold and hotkey differ from its neighbour's.
        internal static List<GalleryBox> NewBoxes()
        {
            var l = new List<GalleryBox>();
            for (int i = 0; i < 12; i++)
                l.Add(new GalleryBox("k" + (i + 1), "Box " + (i + 1), new Rect(20 + i % 4 * 90, 20 + i / 4 * 80, i == 11 ? 83 : 60, 40))
                {
                    Hue = i / 12f, Tint = Color.HSVToRGB(i / 12f, 0.6f, 0.9f), Shape = i % 3, Pattern = i % 4, Weight = i % 10,
                    Bold = i % 2 == 0, Hotkey = "F" + (i + 1),
                });
            return l;
        }

        /// The box's number: k7 is 7.
        static int Number(GalleryBox b) { int n; return int.TryParse(b.Key.Substring(1), out n) ? n : 0; }

        /// The 108-box page: a 12 x 9 grid of 60 x 60 boxes, 70 apart.
        internal static List<GalleryBox> ManyBoxes()
        {
            var l = new List<GalleryBox>();
            for (int i = 0; i < 108; i++) l.Add(new GalleryBox("k" + (i + 1), "Box " + (i + 1), new Rect(20 + i % 12 * 70, 20 + i / 12 * 70, 60, 60)));
            return l;
        }

        static List<GalleryBox> Shown => Model.Tab == 0 ? Model.Boxes : Model.Other;

        // The canvas's menu: the item's own commands, or the view's on empty stage.
        static IList<MenuItem> MenuOf(GalleryBox b)
        {
            Model.MenuFor = b == null ? "" : b.Key;
            if (Model.NoMenu) return null;
            if (b == null)
                return new List<MenuItem>
                {
                    new MenuItem { Text = "Fit", Run = () => Canvas?.Fit() },
                    new MenuItem { Text = "Reset view", Run = () => Canvas?.ResetView() },
                    new MenuItem { Text = "Select all", Run = () => Canvas?.SelectAll() },
                };
            return new List<MenuItem>
            {
                new MenuItem { Text = "Bring forward", Run = () => Canvas?.StepZ(true) },
                new MenuItem { Text = "Send backward", Run = () => Canvas?.StepZ(false) },
                MenuItem.Separator,
                new MenuItem { Text = "Delete", Danger = true, Run = () => Canvas?.DeleteSelection() },
            };
        }

        // The canvas opens the step; this records the list before and after it.
        static void Change(Action<List<GalleryBox>> change)
        {
            var boxes = Shown;
            History.Snapshot("Boxes", null, () => boxes.ToArray(), a => Restore(boxes, a), () => change(boxes));
        }

        // An undo or redo rewrites the list; the canvas re-reads it after the apply and its Changed redraws the page.
        static void Restore(List<GalleryBox> boxes, GalleryBox[] to)
        {
            boxes.Clear(); boxes.AddRange(to);
        }

        /* The editor's shape: on the left a note and every shown box with its values, and on the right, pinned to the
           page's viewport, the menu bar, the canvas and the inspector of what is selected, which scrolls on its own.
           The box list makes the left column far taller than the viewport, so the page scrolls under the pinned
           column; a line after the columns shows the pinned column stopping at their bottom. */
        static void CanvasPage(VisualElement p)
        {
            Page = p;
            Canvas<GalleryBox> c = null;
            VisualElement right;
            var left = Controls.Columns(p, out right, o => { o.Left = 36f; o.Right = 64f; o.RightFill = true; });
            Right = right;
            Ui.Muted("Select boxes on the canvas; the inspector edits every selected box at once.", left).style.whiteSpace = WhiteSpace.Normal;
            Bar = Controls.MenuBar(right, MenuItems = BuildMenus(() => c));
            var drawn = new Dictionary<string, VisualElement>();
            var order = new List<string>();
            Action draw = null;
            // Two groups side by side; picking a Foot tab shows the second set of boxes.
            var strip = Ui.Row(right);
            strip.style.alignItems = Align.FlexStart;
            Action<GalleryTab> show = t =>
            {
                int tab = Model.Hands.Contains(t) ? 0 : 1;
                if (tab == Model.Tab || c == null) return;
                Model.Tab = tab;
                c.Refresh();
                draw();
            };
            Hands = Group(strip, "Hands", () => Model.Hands, "ed-hands", show, false, false);
            Feet = Group(strip, "Feet", () => Model.Feet, "ed-feet", show, false, true);
            Feet.Root.style.marginLeft = Theme.For(strip).Sizes.Pad;
            c = Canvas = Controls.Canvas<GalleryBox>(right, "Canvas", o =>
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
                o.Menu = MenuOf;
                o.Delete = gone => { Model.Deletes++; Change(l => l.RemoveAll(b => Has(gone, b))); };
                o.ZOrder = to => { Model.ZSteps++; Change(l => { l.Clear(); l.AddRange(to); }); };
            });
            Bar.Tools.Label(() => c.Selection.Count + " selected");
            Bar.Tools.Toggle("Snap", Bind.To(() => c.Snap, v => c.Snap = v), "Snap to the canvas centre, the grid and other boxes");
            Bar.Tools.UndoRedo();
            foreach (var g in Ghosts) Place(Plain(c.Content, new Color(0.6f, 0.6f, 0.65f, 0.25f)), g);
            draw = () => Draw(c, drawn, order);
            c.Changed += draw;
            draw();
            Ui.Btn("Replace", () =>
            {
                // New objects with the same keys, as an import or an undo that rebuilds its document would make.
                var l = Model.Tab == 0 ? Model.Boxes : Model.Other;
                for (int i = 0; i < l.Count; i++) l[i] = l[i].Copy();
                c.Refresh();
                draw();
            }, left).name = "ed-replace";
            Left = left;
            Controls.Live(left, () => Shown.Count, BoxList);
            Footer = Ui.Muted("Edits here change the Gallery's own boxes; nothing is saved.", p);
            Footer.style.whiteSpace = WhiteSpace.Normal;
            InspectorScroll = Ui.Scroll(right);
            InspectorScroll.name = "ed-inspector";
            Inspector = Controls.Live(InspectorScroll, () => c.SelectionVersion, box => Inspect(box, c));
        }

        static readonly Func<object> BoxDoc = () => Model.BoxDoc;

        /// One group of tabs (or rows, `vertical`): click picks, drag reorders, double-click renames, right-click opens
        /// its menu, ＋ adds one; `titleMenu` gives its caption a menu.
        static Reorder<GalleryTab> Group(VisualElement parent, string title, Func<List<GalleryTab>> list, string id, Action<GalleryTab> picked,
                                         bool vertical, bool titleMenu)
        {
            Reorder<GalleryTab> r = null;
            r = Controls.Reorder<GalleryTab>(parent, o =>
            {
                o.Id = id;
                o.Title = title;
                o.Vertical = vertical;
                o.Scroll = !vertical;
                o.Items = () => list();
                o.Key = t => t.Key;
                o.Text = t => t.Name;
                o.Selected = t => Model.Current == t;
                o.Active = t => t.Key == Model.ActiveKey;
                o.Select = t => { Model.Picks.Add(t.Name); Model.Current = t; if (picked != null) picked(t); };
                o.Move = (from, to) =>
                {
                    Model.Moves.Add(id + " " + from + ">" + to);
                    var l = list(); var t = l[from]; l.RemoveAt(from); l.Insert(to, t);
                };
                o.Rename = (t, name) => { Model.Renames++; Renamed(t, name); };
                o.Menu = t => new List<MenuItem>
                {
                    new MenuItem { Text = "Rename", Run = () => r.BeginRename(t) },
                    new MenuItem { Text = "Use", Run = () => Model.ActiveKey = t.Key },
                    new MenuItem { Text = "Remove", Danger = true, Run = () => { list().Remove(t); r.Refresh(); } },
                };
                o.Add = () => new List<MenuItem>
                {
                    new MenuItem { Text = "New tab", Run = () => { Model.Adds++; Model.Made++; list().Add(new GalleryTab(id + "-new" + Model.Made, "New " + Model.Made)); r.Refresh(); } },
                };
                if (titleMenu) o.TitleMenu = () => new List<MenuItem> { new MenuItem { Text = "Group menu", Run = () => Model.TitleMenus++ } };
            });
            return r;
        }

        // A rename is one undo step. The step holds the tab and the two names only: the strip re-reads its text on
        // the refresh that follows an undo.
        static void Renamed(GalleryTab t, string name)
        {
            string was = t.Name;
            t.Name = name;
            History.Record("Rename", null, () => t.Name = was, () => t.Name = name);
        }

        /// A vertical list of rows, a status line that names what happened, a confirm before clearing, and a count
        /// that turns Danger at zero.
        static void Lists(VisualElement p)
        {
            ListsPage = p;
            Ui.Muted("Drag a row to reorder it; double-click it to rename it; right-click it for its menu.", p).style.whiteSpace = WhiteSpace.Normal;
            var status = Status = Controls.Status(p);
            RowList = Group(p, "Rows", () => Model.Rows, "ed-rows", t => status.Show(() => t.Name), true, false);
            Controls.Info(p, "Rows", () => Model.Rows.Count == 0 ? "none" : "some", o => { o.Id = "ed-rows-count"; o.Tone = () => Model.Rows.Count == 0 ? Role.Danger : Role.TextMuted; });
            Ui.Btn("Clear rows", () => Popup.Confirm(p, "Clear every row?", "The list empties; undo does not bring it back.", yes =>
            {
                if (!yes) return;
                Model.Rows.Clear();
                RowList.Refresh();
                status.Show("Cleared", Role.Accent, 2f);
            }, o => { o.Ok = "Clear"; o.Danger = true; }), p).name = "ed-clear";
        }

        // Four read-only rows per shown box, re-read on every refresh pass; rebuilt when the number of boxes changes.
        static void BoxList(VisualElement p)
        {
            for (int k = 0; k < Shown.Count; k++)
            {
                int i = k;
                Controls.Info(p, "Box " + (i + 1), () => Of(i, b => b.Name + " (" + b.Key + ")"));
                Controls.Info(p, "Place", () => Of(i, b => b.Rect.x.ToString("0") + ", " + b.Rect.y.ToString("0") + "  " + b.Rect.width.ToString("0") + " x " + b.Rect.height.ToString("0")));
                Controls.Info(p, "Look", () => Of(i, b => "hue " + b.Hue.ToString("0.##") + ", size " + b.Size.ToString("0.##") + ", #" + ColorUtility.ToHtmlStringRGB(b.Tint)
                                                            + ", " + Shapes[Mathf.Clamp(b.Shape, 0, Shapes.Length - 1)] + ", " + Patterns[Mathf.Clamp(b.Pattern, 0, Patterns.Length - 1)]
                                                            + ", weight " + b.Weight + (b.Bold ? ", bold" : "")));
                Controls.Info(p, "Values", () => Of(i, b =>
                {
                    var t = new System.Text.StringBuilder();
                    foreach (var v in b.Values) t.Append(v.ToString("0.##")).Append(' ');
                    foreach (var f in b.Flags) t.Append(f ? '1' : '0');
                    return t.ToString();
                }));
            }
        }

        static string Of(int i, Func<GalleryBox, string> text) { var l = Shown; return i < l.Count ? text(l[i]) : ""; }

        // Every selected box, found again by key in the shown set when an undo or redo comes after a Replace.
        static Bind<T> Sel<T>(Canvas<GalleryBox> c, Func<GalleryBox, T> get, Action<GalleryBox, T> set)
            => Bind.Many(() => c.Selection, get, set, b => b.Key, () => Shown);

        static void Inspect(VisualElement box, Canvas<GalleryBox> c)
        {
            HueRow = null;
            var sel = c.Selection;
            if (sel.Count == 0) { Ui.Muted("Nothing selected.", box); return; }
            bool even = true;
            for (int i = 0; i < sel.Count; i++) even &= Number(sel[i]) % 2 == 0;
            var pages = new List<NavItem> { new NavItem("Box", b => BoxRows(b, c)), new NavItem("Style", b => StyleRows(b, c)) };
            if (even) pages.Add(new NavItem("Extra", b => ExtraRows(b, c)));
            Tabs.Pages(box, pages);
        }

        static readonly string[] Shapes = { "Square", "Round", "Diamond" }, Patterns = { "None", "Stripes", "Dots", "Grid" };

        // Twenty-five rows: one of each kind, then repeats over each box's Values and Flags.
        static void BoxRows(VisualElement p, Canvas<GalleryBox> c)
        {
            HueRow = Controls.Slider(p, "Hue", Sel(c, b => b.Hue, (b, v) => b.Hue = v), o => { o.Id = "ed-i-hue"; o.UndoDocument = BoxDoc; });
            Controls.Number(p, "Size", Sel(c, b => b.Size, (b, v) => b.Size = v), o => { o.Id = "ed-i-size"; o.UndoDocument = BoxDoc; });
            Controls.Colour(p, "Tint", Sel(c, b => b.Tint, (b, v) => b.Tint = v), o => { o.Id = "ed-i-tint"; o.UndoDocument = BoxDoc; });
            Controls.Text(p, "Name", Sel(c, b => b.Name, (b, v) => b.Name = v), o => { o.Id = "ed-i-name"; o.UndoDocument = BoxDoc; });
            Controls.Choice(p, "Shape", Sel(c, b => b.Shape, (b, v) => b.Shape = v), o => { o.Id = "ed-i-shape"; o.Items = Shapes; o.UndoDocument = BoxDoc; });
            Controls.Dropdown(p, "Pattern", Sel(c, b => b.Pattern, (b, v) => b.Pattern = v), o => { o.Id = "ed-i-pattern"; o.Items = Patterns; o.UndoDocument = BoxDoc; });
            Controls.Toggle(p, "Bold", Sel(c, b => b.Bold, (b, v) => b.Bold = v), o => { o.Id = "ed-i-bold"; o.UndoDocument = BoxDoc; });
            Controls.Key(p, "Hotkey", Sel(c, b => b.Hotkey, (b, v) => b.Hotkey = v), o => { o.Id = "ed-i-hotkey"; o.UndoDocument = BoxDoc; });
            Controls.IntSlider(p, "Weight", Sel(c, b => b.Weight, (b, v) => b.Weight = v), o => { o.Id = "ed-i-weight"; o.UndoDocument = BoxDoc; });
            for (int k = 0; k < 10; k++)
            {
                int i = k;
                var bind = Sel(c, b => b.Values[i], (b, v) => b.Values[i] = v);
                if (i % 2 == 0) Controls.Slider(p, "Value " + (i + 1), bind, o => { o.Id = "ed-i-value" + (i + 1); o.UndoDocument = BoxDoc; });
                else Controls.Number(p, "Value " + (i + 1), bind, o => { o.Id = "ed-i-value" + (i + 1); o.UndoDocument = BoxDoc; });
            }
            for (int k = 0; k < 6; k++)
            {
                int i = k;
                Controls.Toggle(p, "Flag " + (i + 1), Sel(c, b => b.Flags[i], (b, v) => b.Flags[i] = v), o => { o.Id = "ed-i-flag" + (i + 1); o.UndoDocument = BoxDoc; });
            }
        }

        static void StyleRows(VisualElement p, Canvas<GalleryBox> c)
        {
            Controls.Colour(p, "Tint", Sel(c, b => b.Tint, (b, v) => b.Tint = v), o => { o.Id = "ed-s-tint"; o.UndoDocument = BoxDoc; });
            Controls.Choice(p, "Shape", Sel(c, b => b.Shape, (b, v) => b.Shape = v), o => { o.Id = "ed-s-shape"; o.Items = Shapes; o.UndoDocument = BoxDoc; });
            Controls.Toggle(p, "Bold", Sel(c, b => b.Bold, (b, v) => b.Bold = v), o => { o.Id = "ed-s-bold"; o.UndoDocument = BoxDoc; });
        }

        static void ExtraRows(VisualElement p, Canvas<GalleryBox> c)
        {
            Controls.Text(p, "Note", Sel(c, b => b.Note, (b, v) => b.Note = v), o => { o.Id = "ed-x-note"; o.UndoDocument = BoxDoc; });
            Controls.Key(p, "Hotkey", Sel(c, b => b.Hotkey, (b, v) => b.Hotkey = v), o => { o.Id = "ed-x-hotkey"; o.UndoDocument = BoxDoc; });
        }

        static bool Has(IReadOnlyList<GalleryBox> l, GalleryBox b)
        {
            for (int i = 0; i < l.Count; i++) if (ReferenceEquals(l[i], b)) return true;
            return false;
        }

        // Every box where its rect says, in the set Items shows; a different set of keys, or a new order, is drawn afresh.
        static void Draw(Canvas<GalleryBox> c, Dictionary<string, VisualElement> drawn, List<string> order)
        {
            var items = Shown;
            bool same = order.Count == items.Count;
            for (int i = 0; i < items.Count && same; i++) same = ReferenceEquals(order[i], items[i].Key) || order[i] == items[i].Key;
            if (!same)
            {
                foreach (var e in drawn.Values) e.RemoveFromHierarchy();
                drawn.Clear();
                order.Clear();
                foreach (var b in items)
                {
                    order.Add(b.Key);
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
            Left = null; Footer = null; Right = null; InspectorScroll = null; Inspector = null; HueRow = null;
            Hands = null; Feet = null; RowList = null; Status = null; ListsItem = null; ListsPage = null;
        }
    }
}
