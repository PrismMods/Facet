using System;
using System.Collections.Generic;
using UnityEngine;
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
    public sealed class Popup
    {
        public VisualElement Content { get; }

        public bool IsOpen => _open.Contains(this);

        private readonly VisualElement _catcher;
        private readonly VisualElement _anchor;
        private readonly Action _onClose;
        private Surface _owner;
        private readonly Theme _theme;
        private readonly Action _retheme;
        private EventCallback<DetachFromPanelEvent> _onDetach;

        private static Surface _popups;
        private static readonly List<Popup> _open = new List<Popup>();

        private static readonly Look MenuRow = new Look { HoverBg = Role.RowHover };

        private const float Gap = 4f;      // between anchor and card
        private const float Margin = 8f;   // kept clear of the screen edge

        /// Open a popup under anchor (above it when there is no room below). build fills Content.
        public static Popup Open(VisualElement anchor, Action<VisualElement> build, Action onClose = null)
        {
            if (anchor == null || anchor.panel == null) { Facet.Host.For(anchor).Log("Popup: anchor is not on a panel"); return null; }
            var surface = PopupSurface();
            if (surface == null || surface.Root == null) return null;
            var p = new Popup(anchor, onClose, surface.Root);
            if (build != null) Safe.Run(Facet.Host.For(anchor), "Popup.build", () => build(p.Content));
            return p;
        }

        /// A list of choices. The current one is marked; picking closes the menu, then reports.
        public static Popup Menu(VisualElement anchor, IList<string> items, int selected, Action<int> onPick)
        {
            Popup menu = null;
            menu = Open(anchor, content =>
            {
                content.style.minWidth = anchor.layout.width;
                for (int i = 0; i < items.Count; i++)
                {
                    int index = i;
                    var row = Ui.Row(content);
                    row.pickingMode = PickingMode.Position;
                    row.style.height = Tokens.RowHeight;
                    row.style.paddingLeft = 8f; row.style.paddingRight = 8f;
                    Ui.SetRadius(row, 4f);
                    var label = Ui.Text(items[i], row, Tokens.FontSizeSmall, i == selected ? Role.Accent : Role.Text);
                    label.pickingMode = PickingMode.Ignore;
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

        private Popup(VisualElement anchor, Action onClose, VisualElement root)
        {
            _anchor = anchor;
            _onClose = onClose;

            _catcher = new VisualElement();
            _catcher.style.position = Position.Absolute;
            _catcher.style.left = 0f; _catcher.style.top = 0f;
            _catcher.style.right = 0f; _catcher.style.bottom = 0f;   // all four, or it sizes to nothing
            _catcher.pickingMode = PickingMode.Position;
            Events.On<PointerDownEvent>(_catcher, e => { e.StopPropagation(); Close(); });
            root.Add(_catcher);

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
            _popups.Visible = true;
        }

        /* Every Facet surface shares one PanelSettings scale, so the anchor's panel coordinates are
           this surface's coordinates too — no screen round trip needed. */
        private void Place(float scale)
        {
            if (!IsOpen) return;
            var a = _anchor.worldBound;
            // The panel's own size: the host surface was only just attached and has no layout yet,
            // which clamped every first popup to the top margin.
            var screen = Content.panel != null ? Content.panel.visualTree.layout : _popups.Root.layout;
            float w = Content.layout.width * scale, h = Content.layout.height * scale;

            float top = a.yMax + Gap;
            if (top + h > screen.height - Margin && a.yMin - Gap - h >= Margin) top = a.yMin - Gap - h;
            top = Mathf.Clamp(top, Margin, Mathf.Max(Margin, screen.height - h - Margin));
            float left = Mathf.Clamp(a.xMin, Margin, Mathf.Max(Margin, screen.width - w - Margin));

            Content.style.left = left;
            Content.style.top = top;
            Content.style.visibility = Visibility.Visible;
        }

        public void Close()
        {
            if (!_open.Remove(this)) return;
            if (_owner != null) _owner.Hidden -= Close;
            _theme.Changed -= _retheme;
            Events.Off(_anchor, _onDetach);
            _catcher.RemoveFromHierarchy();
            Content.RemoveFromHierarchy();
            if (_open.Count == 0 && _popups != null) _popups.Visible = false;
            if (_onClose != null) Safe.Run(Facet.Host.For(_anchor), "Popup.onClose", _onClose);
        }

        /// Close every open popup — for a host tearing its UI down.
        public static void CloseAll()
        {
            for (int i = _open.Count - 1; i >= 0; i--) _open[i].Close();
        }

        // Every surface shares one panel, so ownership is ancestry, not which panel the anchor is in.
        private static Surface OwnerOf(VisualElement anchor)
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
                if (_open.Count == 0) return false;
                _open[_open.Count - 1].Close();
                return true;
            };
            _popups.Visible = false;
            return _popups;
        }
    }
}
