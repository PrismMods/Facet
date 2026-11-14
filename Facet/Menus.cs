using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* One command, listed once and used everywhere: a row in a menu bar's menu, in a context menu, and a
       shortcut the host's key poll runs. Every func and the Run go through Safe. */
    public sealed class MenuItem
    {
        /// An English key, translated by the host of the element the menu opens from.
        public string Text { get; set; }

        /// Read each time the menu opens and shown as returned, untranslated; wins over Text.
        public Func<string> TextOf { get; set; }

        /// "Ctrl+Shift+C", "Delete", "[", "Ctrl+0"; Cmd is Ctrl. Shown right-aligned on the row, ⌘⇧⌥ on a Mac.
        /// Parsed once, when set: text that does not parse shows on the row and runs nothing.
        public string Shortcut
        {
            get { return _shortcut; }
            set
            {
                _shortcut = value;
                bool c, s, a; string name; KeyCode code;
                Key = Facet.Shortcut.TryParse(value, out c, out s, out a, out name) && Enum.TryParse(name, out code) ? code : KeyCode.None;
                Ctrl = c; Shift = s; Alt = a;
            }
        }

        private string _shortcut;
        // The parsed Shortcut, so Menus.HandleKeys compares without allocating.
        internal KeyCode Key;
        internal bool Ctrl, Shift, Alt;

        /// Read when the menu opens: false fades the row, and it does not run. Null is enabled.
        public Func<bool> Enabled { get; set; }

        /// Read when the menu opens: true shows a check mark. A menu with any Checked item has a check column.
        public Func<bool> Checked { get; set; }

        /// Runs once every open menu has closed.
        public Action Run { get; set; }

        /// A submenu: the row shows › and opens it beside itself on hover or click.
        public IList<MenuItem> Items { get; set; }

        /// The row's text takes the Danger role.
        public bool Danger { get; set; }

        /// Names the row (a menu bar's title) for tests and for finding it again.
        public string Id { get; set; }

        /// A rule between groups; nothing else on it is read.
        public bool IsSeparator { get; set; }

        /// A new separator on each read.
        public static MenuItem Separator => new MenuItem { IsSeparator = true };
    }

    /* A row of menu titles over a page. A click on a title opens its menu under it; while one is open, moving
       onto another title opens that one instead. The open menu's outside-click catcher covers the bar, so the
       move arrives through it (Popup.Moved). Left holds leading controls before the titles; Tools is a Toolbar
       at the right end. The bar's edges pad as a toolbar's do. */
    public sealed class MenuBar
    {
        public VisualElement Root { get; }

        /// Leading controls, before the titles.
        public VisualElement Left { get; }

        /// Tools at the bar's right end.
        public Toolbar Tools { get; }

        public bool IsOpen => _popup != null && _popup.IsOpen;

        private readonly IList<MenuItem> _menus;
        private readonly List<Button> _titles = new List<Button>();
        private readonly Action<Vector2> _hover;
        private Popup _popup;
        private int _open = -1;

        internal MenuBar(VisualElement parent, IList<MenuItem> menus)
        {
            _menus = menus ?? new MenuItem[0];
            _hover = Hover;
            Root = Ui.Row(parent);
            var sz = Theme.For(Root).Sizes;
            Root.style.minHeight = sz.RowHeight;
            Root.style.paddingLeft = sz.Line + sz.Pad;
            Root.style.paddingRight = sz.Line + sz.Pad;
            Left = Ui.Row(Root);
            for (int i = 0; i < _menus.Count; i++)
            {
                int index = i;
                var m = _menus[i];
                var title = Ui.Btn(Root, m != null ? m.Text : "", () => Open(index));
                Paint.Look(title, Ui.Bare);
                title.style.marginRight = 0f;
                if (m != null && m.Id != null) title.name = m.Id;
                _titles.Add(title);
            }
            Ui.Box(Root).style.flexGrow = 1f;
            Tools = new Toolbar(Root);
            Ui.SetPadding(Tools.Root, 0f);   // the bar pads its own edges
            Ui.SetPadding(Tools.Row, 0f);
        }

        /// Open menu `menu` (an index into the bar's list) under its title, closing any other. A top item with no
        /// Items runs its Run instead, when Enabled allows.
        public void Open(int menu)
        {
            Close();
            if (menu < 0 || menu >= _menus.Count || _menus[menu] == null) return;
            var m = _menus[menu];
            var title = _titles[menu];
            if (m.Items == null)
            {
                var host = Host.For(Root);
                if (m.Run != null && (m.Enabled == null || Safe.Get(host, "MenuItem.Enabled", m.Enabled, true)))
                    Safe.Run(host, "Menu '" + m.Text + "'", m.Run);
                return;
            }
            Popup p = null;
            p = Menus.Show(title, m.Items, () =>
            {
                Paint.Select(title, false);
                if (_popup == p) { _popup = null; _open = -1; }
            });
            if (p == null) return;
            _popup = p;
            _open = menu;
            Paint.Select(title, true);
            p.Moved = _hover;
        }

        public void Close()
        {
            var p = _popup;
            _popup = null;
            _open = -1;
            if (p != null) p.Close();
        }

        // Only a title with a menu opens on a hover: a command title runs from its own click alone.
        private void Hover(Vector2 at)
        {
            for (int i = 0; i < _titles.Count; i++)
                if (i != _open && _menus[i] != null && _menus[i].Items != null && _titles[i].worldBound.Contains(at)) { Open(i); return; }
        }
    }

    public static partial class Controls
    {
        /// A menu bar over `menus`: each top item's Text is a title, and its Items the menu the title opens.
        public static MenuBar MenuBar(VisualElement parent, IList<MenuItem> menus) => new MenuBar(parent, menus);
    }

    /* The menu card, one builder for the bar's menus, Popup.Menu and Popup.Context. A row has a check column
       (only when some item in that menu has Checked), the text, and the shortcut right-aligned. A disabled row
       fades and does not run; a separator is a Role.Line rule; a Danger row's text is Danger. A submenu opens
       beside its row on hover or click and has no catcher of its own, so the pointer still reaches the menu it
       hangs from. Picking a row closes every menu of its chain, newest first, then runs the item. Escape closes
       the newest (the popup host's handler). */
    public static class Menus
    {
        /* Runs the first enabled item, submenus included, whose Shortcut is a key that went down this frame (`down`,
           the host's GetKeyDown) with exactly the held modifiers (Cmd counts as Ctrl), and returns true. The host calls
           it from its per-frame key poll. It acts only while owner's surface is the topmost visible surface of the
           Window, Popup and Modal layers: with two editors open only the front one answers (a window must raise its
           surface on a press: Window.Raised), and nothing runs under an open menu, dropdown or dialog. Never while a
           text field has focus, a key capture runs or a drag is in progress. Ctrl+Z, Ctrl+Shift+Z and Ctrl+Y never
           run here: the undo chord owns them. A frame with no key down allocates nothing. */
        public static bool HandleKeys(VisualElement owner, IList<MenuItem> items, Func<KeyCode, bool> down, bool ctrlOrCmd, bool shift, bool alt)
        {
            if (owner == null || items == null || down == null) return false;
            int gate = 0;   // asked once, at the first key that matches: 1 owner may act, -1 it may not
            var hit = Match(owner, items, down, ctrlOrCmd, shift, alt, ref gate);
            if (hit == null) return false;
            if (hit.Run != null) Safe.Run(Host.For(owner), "Menu shortcut '" + hit.Shortcut + "'", hit.Run);
            return true;
        }

        private static MenuItem Match(VisualElement owner, IList<MenuItem> items, Func<KeyCode, bool> down, bool ctrl, bool shift, bool alt, ref int gate)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (it.Items != null)
                {
                    var sub = Match(owner, it.Items, down, ctrl, shift, alt, ref gate);
                    if (sub != null || gate < 0) return sub;
                    continue;
                }
                if (it.Key == KeyCode.None || it.Ctrl != ctrl || it.Shift != shift || it.Alt != alt || UndoChord(it)) continue;
                bool pressed;
                try { pressed = down(it.Key); }
                catch (Exception e) { var h = Host.For(owner); Safe.Report(h.Name, "Menus.HandleKeys down", e, h.LogFn); return null; }
                if (!pressed) continue;
                if (gate == 0) gate = MayAct(owner) ? 1 : -1;
                if (gate < 0) return null;
                if (it.Enabled != null && !Safe.Get(Host.For(owner), "MenuItem.Enabled", it.Enabled, true)) continue;
                return it;
            }
            return null;
        }

        private static bool UndoChord(MenuItem it) => it.Ctrl && !it.Alt && (it.Key == KeyCode.Z || (it.Key == KeyCode.Y && !it.Shift));

        private static bool MayAct(VisualElement owner)
        {
            if (Input.TextFocused || KeyCapture.Owner != null || Input.Gesture != null) return false;
            var s = Popup.OwnerOf(owner);
            if (s == null) return false;
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                var top = Ui.Stack[i];
                if (top.Visible && (top.Layer == Layer.Window || top.Layer == Layer.Popup || top.Layer == Layer.Modal)) return top == s;
            }
            return false;
        }

        // The open menus of one chain, the first menu first, and the row each submenu hangs from.
        private sealed class Chain
        {
            public readonly List<Popup> Open = new List<Popup>();
            public readonly List<VisualElement> From = new List<VisualElement>();
        }

        private static readonly Vector2 Unplaced = new Vector2(float.NaN, float.NaN);

        /// A menu under anchor, or (at not NaN) with its top-left at that panel point.
        internal static Popup Show(VisualElement anchor, IList<MenuItem> items, Action onClose) => Show(anchor, items, onClose, Unplaced);

        internal static Popup Show(VisualElement anchor, IList<MenuItem> items, Action onClose, Vector2 at)
            => Level(new Chain(), 0, anchor, items, onClose, at);

        private static Popup Level(Chain chain, int depth, VisualElement anchor, IList<MenuItem> items, Action onClose, Vector2 at)
        {
            var list = items ?? new MenuItem[0];
            var p = Popup.Open(anchor, content => Fill(chain, depth, content, list), onClose, depth == 0, at, depth > 0);
            if (p != null) { chain.Open.Add(p); chain.From.Add(depth == 0 ? null : anchor); }
            return p;
        }

        // Close the chain's menus from `depth` on, the newest first.
        private static void Trim(Chain chain, int depth)
        {
            for (int i = chain.Open.Count - 1; i >= depth; i--)
            {
                var p = chain.Open[i];
                chain.Open.RemoveAt(i);
                chain.From.RemoveAt(i);
                p.Close();
            }
        }

        private static void Fill(Chain chain, int depth, VisualElement content, IList<MenuItem> items)
        {
            var host = Host.For(content);
            var sz = Theme.For(content).Sizes;   // once, above the rows: Dark's is a fresh copy per read
            content.style.minWidth = sz.DropdownWidth;
            bool checks = false;
            for (int i = 0; i < items.Count; i++) if (items[i] != null && items[i].Checked != null) checks = true;
            bool mac = Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (it.IsSeparator)
                {
                    var rule = Ui.Box(content);
                    rule.style.height = sz.Line;
                    rule.style.marginTop = sz.Gap / 2f;
                    rule.style.marginBottom = sz.Gap / 2f;
                    Paint.Bg(rule, Role.Line);
                    continue;
                }
                var row = Ui.Row(content);
                row.pickingMode = PickingMode.Position;
                row.style.height = sz.ListRowHeight;
                row.style.paddingLeft = sz.Gap;
                row.style.paddingRight = sz.Gap;
                Ui.SetRadius(row, sz.Radius);
                if (it.Id != null) row.name = it.Id;
                if (checks)
                {
                    bool on = it.Checked != null && Safe.Get(host, "MenuItem.Checked", it.Checked, false);
                    var mark = Cell(row, on ? "✓" : "", Role.Text, "check", sz);
                    mark.style.width = sz.Small;
                    mark.style.marginRight = sz.Gap / 2f;
                }
                string shown = it.TextOf != null ? Safe.Get(host, "MenuItem.TextOf", it.TextOf, "") ?? "" : null;
                var label = Ui.Text(row, shown != null ? "" : it.Text, sz.Small, it.Danger ? Role.Danger : Role.Text);
                if (shown != null) label.text = shown;
                label.name = "text";
                label.style.flexGrow = 1f;
                label.pickingMode = PickingMode.Ignore;
                if (!string.IsNullOrEmpty(it.Shortcut)) Cell(row, Shortcut.Display(it.Shortcut, mac), Role.TextMuted, "shortcut", sz).style.marginLeft = sz.Pad;
                if (it.Items != null) Cell(row, "›", Role.TextMuted, "sub", sz).style.marginLeft = sz.Pad;
                Paint.Look(row, Popup.MenuRow);
                if (it.Enabled != null && !Safe.Get(host, "MenuItem.Enabled", it.Enabled, true))
                {
                    Paint.Enable(row, false);
                    continue;
                }
                string name = shown ?? it.Text;
                Events.On<PointerMoveEvent>(row, _ => Hover(chain, depth, row, it));
                Events.On<PointerUpEvent>(row, e =>
                {
                    if (e.button != 0) return;
                    if (it.Items != null) Hover(chain, depth, row, it);
                    else Pick(chain, host, name, it.Run);
                });
            }
        }

        private static Label Cell(VisualElement row, string text, Role role, string name, Sizes sz)
        {
            var l = Ui.Text(row, text, sz.Small, role, true);
            l.name = name;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        // The pointer on a row: its submenu opens (once), and any deeper menu from another row closes.
        private static void Hover(Chain chain, int depth, VisualElement row, MenuItem it)
        {
            int sub = depth + 1;
            if (it.Items != null && chain.Open.Count > sub && chain.From[sub] == row && chain.Open[sub].IsOpen) return;
            Trim(chain, sub);
            if (it.Items != null) Level(chain, sub, row, it.Items, null, Unplaced);
        }

        private static void Pick(Chain chain, Host host, string name, Action run)
        {
            Trim(chain, 0);
            if (run != null) Safe.Run(host, "Menu '" + name + "'", run);
        }
    }
}
