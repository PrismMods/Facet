using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Facet
{
    /* A floating card anchored to the control that opened it — what a dropdown, context menu or
       picker needs, since a Window's Body clips everything inside it at the window edge.

       Every popup lives on one shared Surface in the Popup layer, so it draws above all windows
       whichever mod opened it. Each popup brings a transparent full-screen catcher under its card:
       a click outside closes the popup and goes no further, the way a desktop menu behaves, and
       Ui.PointerOverWindow sees the catcher, so the editor behind does not take that click either.
       Popups stack in open order; Escape and an outside click close the newest first.

       A popup also closes when the window it came from hides, or when its anchor leaves the panel
       (the page it sat on was rebuilt) — otherwise it would float over nothing. */
    /// Popup.Confirm's buttons: English keys, translated by the owner's host (the defaults are Facet's own words).
    /// A null Cancel shows OK alone, a notice. Danger paints OK in the theme's DangerButton look.
    public sealed class ConfirmOptions
    {
        public string Ok { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";
        public bool Danger { get; set; }
    }

    public sealed class Popup
    {
        public VisualElement Content { get; }

        public bool IsOpen => _open.Contains(this);

        private readonly VisualElement _catcher;   // null for a submenu: the menu it hangs from has one
        private readonly VisualElement _anchor;
        private readonly Vector2 _at;              // a context menu's top-left (NaN: placed by the anchor)
        private readonly bool _beside;             // a submenu: beside its row rather than under it
        private readonly bool _modal;              // a dialog: on the Modal layer, centred over its owner's window
        private VisualElement _focus;              // focused once placed (a dialog's OK)

        /// The pointer moving over the catcher, in panel points: a menu bar switches menus as it crosses a title.
        internal Action<Vector2> Moved;
        /// A press on the catcher, by button: true when it was taken (a key prompt binding a mouse button), so the
        /// popup stays for whoever took it; else the press closes the popup.
        internal Func<int, bool> Pressed;
        private readonly Action _onClose;
        private Surface _owner;
        private readonly Theme _theme;
        private readonly Action _retheme;
        private EventCallback<DetachFromPanelEvent> _onDetach;

        private static Surface _popups, _modals;
        private static readonly List<Popup> _open = new List<Popup>();

        internal static readonly Look MenuRow = new Look { HoverBg = Role.RowHover };

        private const float Gap = 4f;      // between anchor and card
        private const float Margin = 8f;   // kept clear of the screen edge

        /// Open a popup under anchor (above it when there is no room below). build fills Content.
        public static Popup Open(VisualElement anchor, Action<VisualElement> build, Action onClose = null)
            => Open(anchor, build, onClose, true, new Vector2(float.NaN, float.NaN), false);

        /// catcher false: no outside-click catcher (a submenu, under its menu's). at: the card's top-left in
        /// panel points, else NaN. beside: next to the anchor rather than under it.
        internal static Popup Open(VisualElement anchor, Action<VisualElement> build, Action onClose, bool catcher, Vector2 at, bool beside)
        {
            if (anchor == null || anchor.panel == null) { Facet.Host.For(anchor).Log("Popup: anchor is not on a panel"); return null; }
            var surface = PopupSurface();
            if (surface == null || surface.Root == null) return null;
            var p = new Popup(anchor, onClose, surface.Root, catcher, at, beside, false);
            if (build != null) Safe.Run(Facet.Host.For(anchor), "Popup.build", () => build(p.Content));
            return p;
        }

        /// A menu of MenuItems under anchor (a menu bar's title, a toolbar button).
        public static Popup Menu(VisualElement anchor, IList<MenuItem> items) => Menus.Show(anchor, items, null);

        /// A context menu with its top-left at `local` (a point in owner's own coordinates, as a pointer event's
        /// localPosition), moved inside the panel when it would overflow. Closes with owner's window, or when owner
        /// leaves the panel.
        public static Popup Context(VisualElement owner, Vector2 local, IList<MenuItem> items)
        {
            if (owner == null || owner.panel == null) { Facet.Host.For(owner).Log("Popup.Context: owner is not on a panel"); return null; }
            return Menus.Show(owner, items, null, owner.LocalToWorld(local));
        }

        /// A list of choices. The current one is marked; picking closes the menu, then reports.
        public static Popup Menu(VisualElement anchor, IList<string> items, int selected, Action<int> onPick)
            => Menu(anchor, items, selected, onPick, null);

        /// itemFont: a face for each item (a font list shows every name in its own font); null keeps the menu's.
        public static Popup Menu(VisualElement anchor, IList<string> items, int selected, Action<int> onPick, Func<int, Font> itemFont)
            => Menu(anchor, items, selected, onPick, itemFont, null);

        /// itemFace: an SDF face for each item, over itemFont; null, or a throw, keeps the menu's face for that item.
        internal static Popup Menu(VisualElement anchor, IList<string> items, int selected, Action<int> onPick, Func<int, Font> itemFont, Func<int, FontAsset> itemFace)
        {
            Popup menu = null;
            menu = Open(anchor, content =>
            {
                content.style.minWidth = anchor.layout.width;
                var sz = Theme.For(content).Sizes;   // once, above the rows: Dark's is a fresh copy per read
                for (int i = 0; i < items.Count; i++)
                {
                    int index = i;
                    var row = Ui.Row(content);
                    row.pickingMode = PickingMode.Position;
                    row.style.height = sz.ListRowHeight;
                    row.style.paddingLeft = sz.Gap; row.style.paddingRight = sz.Gap;
                    Ui.SetRadius(row, sz.Radius);
                    var label = Ui.Text(items[i], row, sz.Small, i == selected ? Role.Accent : Role.Text);
                    label.pickingMode = PickingMode.Ignore;
                    if (itemFont != null)
                    {
                        var f = Safe.Get(Facet.Host.For(anchor), "Popup.Menu ItemFont", () => itemFont(index), null);
                        if (f != null) label.style.unityFontDefinition = FontDefinition.FromFont(f);
                    }
                    if (itemFace != null)
                    {
                        var fa = Safe.Get(Facet.Host.For(anchor), "Popup.Menu ItemFace", () => itemFace(index), null);
                        if (fa != null) label.style.unityFontDefinition = FontDefinition.FromSDFFont(fa);
                    }
                    Paint.Look(row, MenuRow);
                    Events.On<PointerUpEvent>(row, e =>
                    {
                        if (e.button != 0) return;
                        menu?.Close();
                        if (onPick != null) Safe.Run(Facet.Host.For(anchor), "Popup.Menu handler", () => onPick(index));
                    });
                }
            });
            return menu;
        }

        /* A yes-or-no question on the Modal layer, centred over the window `owner` sits in (on the screen when owner is
           null), behind a catcher. `title` and `body` are English keys, translated by owner's host. OK is focused, so a
           submit runs it; Escape, Cancel, an outside click, the owner's window hiding or owner leaving its panel mean no.
           `done` runs exactly once, through Safe, after the dialog has closed. */
        public static Popup Confirm(VisualElement owner, string title, string body, Action<bool> done, Action<ConfirmOptions> configure = null)
        {
            var host = Facet.Host.For(owner);
            var o = new ConfirmOptions();
            if (configure != null) Safe.Run(host, "Confirm configure", () => configure(o));
            var surface = ModalSurface();
            if (surface == null || surface.Root == null)
            {
                host.Log("Popup.Confirm: no modal surface");
                if (done != null) Safe.Run(host, "Confirm done", () => done(false));
                return null;
            }
            bool yes = false;
            var anchor = owner != null && owner.panel != null ? owner : surface.Root;
            var p = new Popup(anchor, () => { if (done != null) Safe.Run(host, "Confirm done", () => done(yes)); },
                              surface.Root, true, new Vector2(float.NaN, float.NaN), false, true);
            var c = p.Content;
            var sz = Theme.For(c).Sizes;
            var looks = Theme.For(c).Looks;
            Ui.SetPadding(c, sz.Pad);
            c.style.minWidth = sz.DialogMinWidth;
            c.style.maxWidth = sz.DialogMaxWidth;
            var t = Ui.Text(title, c, sz.Body);
            t.style.whiteSpace = WhiteSpace.Normal;
            if (!string.IsNullOrEmpty(body))
            {
                var b = Ui.Text(body, c, sz.Small, Role.TextMuted);
                b.style.whiteSpace = WhiteSpace.Normal;
                b.style.marginTop = sz.Gap / 2f;
            }
            var row = Ui.Row(c);
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = sz.Pad;
            if (o.Cancel != null)
            {
                var no = Ui.Btn(o.Cancel, p.Close, row, o.Cancel == "Cancel");
                no.name = "confirm-cancel";
            }
            var ok = Ui.Btn(o.Ok ?? "OK", () => { yes = true; p.Close(); }, row, o.Ok == null || o.Ok == "OK");
            ok.name = "confirm-ok";
            ok.style.marginRight = 0f;
            Paint.Look(ok, o.Danger ? looks.DangerButton : looks.AccentButton);
            p._focus = ok;
            return p;
        }

        private Popup(VisualElement anchor, Action onClose, VisualElement root, bool catcher, Vector2 at, bool beside, bool modal)
        {
            _anchor = anchor;
            _modal = modal;
            _onClose = onClose;
            _at = at;
            _beside = beside;

            if (catcher)
            {
                _catcher = new VisualElement();
                _catcher.style.position = Position.Absolute;
                _catcher.style.left = 0f; _catcher.style.top = 0f;
                _catcher.style.right = 0f; _catcher.style.bottom = 0f;   // all four, or it sizes to nothing
                _catcher.pickingMode = PickingMode.Position;
                Events.On<PointerDownEvent>(_catcher, e =>
                {
                    e.StopPropagation();
                    if (Pressed == null || !Pressed(e.button)) Close();
                });
                Events.On<PointerMoveEvent>(_catcher, e => { if (Moved != null) Moved(e.position); });
                root.Add(_catcher);
            }

            // The anchor's scheme, not the shared popup host's: a menu opened from a Quartz window is Quartz's.
            var theme = Theme.For(anchor);
            using (Theme.Use(theme)) Content = Ui.Card();
            Theme.Set(Content, theme);
            // The card follows its anchor's scheme while open; Close lets go.
            _theme = theme;
            _retheme = () => Paint.Apply(Content);
            theme.Changed += _retheme;
            Host.For(anchor).Attach(Content);   // its handlers log to the consumer that opened it, not to Facet
            Content.style.position = Position.Absolute;
            Content.style.paddingLeft = 4f; Content.style.paddingRight = 4f;
            Content.style.paddingTop = 4f; Content.style.paddingBottom = 4f;
            Content.pickingMode = PickingMode.Position;
            // Placed once its size is known; until then it would flash at the wrong spot.
            Content.style.visibility = Visibility.Hidden;
            root.Add(Content);

            _owner = OwnerOf(anchor);
            if (_owner != null)
            {
                _owner.Hidden += Close;
                // Same face as the window it came from, which may be a mod font rather than ours.
                if (_owner.Root != null) Content.style.unityFontDefinition = _owner.Root.style.unityFontDefinition;
            }
            _onDetach = _ => Close();
            Events.On(anchor, _onDetach);

            /* Match the window's scale: Window magnifies its contents with a transform, which
               worldBound includes and layout does not, so their ratio is the scale in effect. */
            float s = anchor.layout.height > 0f ? anchor.worldBound.height / anchor.layout.height : 1f;
            if (s > 0f && Mathf.Abs(s - 1f) > 0.001f)
            {
                Content.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);
                Content.style.scale = new Scale(new Vector3(s, s, 1f));
            }

            EventCallback<GeometryChangedEvent> place = null;
            place = _ =>
            {
                Events.Off(Content, place);
                Place(s);
            };
            Events.On(Content, place);

            _open.Add(this);
            (modal ? _modals : _popups).Visible = true;
        }

        /* Each surface is its own panel, but every Facet surface shares one PanelSettings scale over the
           same screen, so the anchor's world coordinates are this surface's coordinates too — no screen
           round trip needed. */
        private void Place(float scale)
        {
            if (!IsOpen) return;
            var a = _anchor.worldBound;
            // The panel's own size: the host surface was only just attached and has no layout yet,
            // which clamped every first popup to the top margin.
            var screen = Content.panel != null ? Content.panel.visualTree.layout : _popups.Root.layout;
            float w = Content.layout.width * scale, h = Content.layout.height * scale;

            float top, left;
            if (_modal)
            {
                // Over the window owner sits in: the child of its surface's root that holds it.
                var area = screen;
                var owner = OwnerOf(_anchor);
                if (owner != null && owner != _modals && owner.Root != null)
                {
                    var e = _anchor;
                    while (e.parent != null && e.parent != owner.Root) e = e.parent;
                    area = e.worldBound;
                }
                left = Mathf.Clamp(area.center.x - w / 2f, Margin, Mathf.Max(Margin, screen.width - w - Margin));
                top = Mathf.Clamp(area.center.y - h / 2f, Margin, Mathf.Max(Margin, screen.height - h - Margin));
            }
            else if (!float.IsNaN(_at.x))
            {
                // A context menu: its top-left at the point, moved in only as far as the panel needs.
                left = Mathf.Clamp(_at.x, 0f, Mathf.Max(0f, screen.width - w));
                top = Mathf.Clamp(_at.y, 0f, Mathf.Max(0f, screen.height - h));
            }
            else if (_beside)
            {
                // A submenu: right of its row, its first row level with it; left of the menu when there is no room.
                left = a.xMax;
                if (left + w > screen.width - Margin && a.xMin - w >= Margin) left = a.xMin - w;
                left = Mathf.Clamp(left, Margin, Mathf.Max(Margin, screen.width - w - Margin));
                top = Mathf.Clamp(a.yMin - Content.resolvedStyle.paddingTop * scale, Margin, Mathf.Max(Margin, screen.height - h - Margin));
            }
            else
            {
                top = a.yMax + Gap;
                if (top + h > screen.height - Margin && a.yMin - Gap - h >= Margin) top = a.yMin - Gap - h;
                top = Mathf.Clamp(top, Margin, Mathf.Max(Margin, screen.height - h - Margin));
                left = Mathf.Clamp(a.xMin, Margin, Mathf.Max(Margin, screen.width - w - Margin));
            }

            Content.style.left = left;
            Content.style.top = top;
            Content.style.visibility = Visibility.Visible;
            // Next tick: it is still hidden until this style applies, and a hidden element takes no focus.
            if (_focus != null) Content.schedule.Execute(() => { if (IsOpen) _focus.Focus(); });
        }

        public void Close()
        {
            if (!_open.Remove(this)) return;
            if (_owner != null) _owner.Hidden -= Close;
            _theme.Changed -= _retheme;
            Events.Off(_anchor, _onDetach);
            // A field focused inside the card lets go first, so its FocusOut is heard before it leaves the tree.
            var fc = Content.panel != null ? Content.panel.focusController : null;
            var fe = fc != null ? fc.focusedElement as VisualElement : null;
            if (fe != null && Content.Contains(fe)) fe.Blur();
            if (_catcher != null) _catcher.RemoveFromHierarchy();
            Content.RemoveFromHierarchy();
            if (Newest(_modal) == null) { var s = _modal ? _modals : _popups; if (s != null) s.Visible = false; }
            if (_onClose != null) Safe.Run(Facet.Host.For(_anchor), "Popup.onClose", _onClose);
        }

        /// Close every open popup — for a host tearing its UI down.
        public static void CloseAll()
        {
            for (int i = _open.Count - 1; i >= 0; i--) _open[i].Close();
        }

        // The newest open dialog (modal) or popup, else null.
        private static Popup Newest(bool modal)
        {
            for (int i = _open.Count - 1; i >= 0; i--) if (_open[i]._modal == modal) return _open[i];
            return null;
        }

        // The surface whose root holds the anchor. Each surface is its own panel; ownership is read from the
        // anchor's ancestry, the same for a popup or a dialog over any of them.
        internal static Surface OwnerOf(VisualElement anchor)
        {
            for (var e = anchor; e != null; e = e.parent)
                foreach (var s in Ui.Stack)
                    if (s != _popups && s.Root == e) return s;
            return null;
        }

        private static Surface PopupSurface()
        {
            if (_popups != null && _popups.Root != null) return _popups;
            _popups = new Surface(Facet.Host.Fallback, "FacetPopups", Layer.Popup);
            if (_popups.Root == null) return _popups;
            _popups.EscapeHandler = () =>
            {
                var p = Newest(false);
                if (p == null) return false;
                p.Close();
                return true;
            };
            _popups.Visible = false;
            return _popups;
        }

        // The dialogs' shared surface, above every window and popup; Escape closes the newest dialog.
        private static Surface ModalSurface()
        {
            if (_modals != null && _modals.Root != null) return _modals;
            _modals = new Surface(Facet.Host.Fallback, "FacetModals", Layer.Modal);
            if (_modals.Root == null) return _modals;
            _modals.EscapeHandler = () =>
            {
                var p = Newest(true);
                if (p == null) return false;
                p.Close();
                return true;
            };
            _modals.Visible = false;
            return _modals;
        }
    }
}
