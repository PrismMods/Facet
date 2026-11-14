using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// SettingsWindow.Open's options.
    public sealed class SettingsWindowOptions
    {
        /// The first rect (panel points) when the id has none stored; null centres InitialFraction of the panel.
        public Rect? Initial { get; set; }

        /// The share of the panel's width and height a first open takes, centred.
        public Vector2 InitialFraction { get; set; } = new Vector2(0.8f, 0.8f);

        /// A search box in the title bar, filtering the rail (Nav.Filter). Its placeholder is the key "Search": the
        /// host's word for it, else Facet's own.
        public bool Search { get; set; } = true;

        /// The rail starts as its icon strip (Nav.Compact).
        public bool CompactRail { get; set; }

        /// An icon at the start of the title bar.
        public Background? Icon { get; set; }

        /// The open rail's width, passed to the Nav.
        public float RailWidth { get; set; } = 200f;
    }

    /* A whole settings window in one call: a Window-layer surface, a window on it raised by a press, a Nav of
       `pages` in its body with the content inset kept in line with the header, search into the rail and a
       title-bar icon. The first open of an id with no stored rect is placed on the first layout of the surface
       while it is visible, keeping a Window.Rect set before then; until then (a surface hidden in the frame it opened) the window's rect is stored
       nowhere, neither in memory nor through the host's Write. */
    public sealed class SettingsWindow : IDisposable
    {
        public Surface Surface { get; }

        /// Null when the surface could not be created (its Root is null; the host's log says so).
        public Window Window { get; }

        /// Null when the surface could not be created, as Window.
        public Nav Nav { get; }

        private readonly Rect _first;
        private readonly Vector2 _fraction;
        private readonly bool _fixed;
        private bool _placed;

        private SettingsWindow(Host host, string id, string title, IList<NavItem> pages, SettingsWindowOptions o)
        {
            Surface = new Surface(host, id ?? title ?? "Settings", Layer.Window);
            if (Surface.Root == null)
            {
                host.Log("SettingsWindow '" + (id ?? title) + "': no surface root, so Window and Nav are null");
                return;
            }
            _fixed = o.Initial.HasValue;
            _fraction = o.InitialFraction;
            // A placeholder until the first visible layout knows the panel's size: the reference panel's.
            _first = o.Initial ?? Centred(new Vector2(1920f, 1080f), _fraction);
            Window = new Window(Surface.Root, id, title, _first, false);
            var s = Surface;
            Window.Raised = () => s.BringToFront();   // not OnRaise: that stays the consumer's
            if (o.Icon.HasValue)
            {
                var sz = Theme.For(Window.Root).Sizes;
                var icon = Ui.Box();
                icon.name = "window-icon";
                icon.style.width = sz.Icon;
                icon.style.height = sz.Icon;
                icon.style.marginRight = sz.Gap;
                icon.style.alignSelf = Align.Center;
                icon.style.flexShrink = 0f;
                icon.pickingMode = PickingMode.Ignore;
                icon.style.backgroundImage = new StyleBackground(o.Icon.Value);
                Window.TitleBarLeft.Insert(0, icon);
            }
            Nav = new Nav(Window.Body, pages, o.RailWidth);
            if (o.CompactRail) Nav.Compact = true;
            var w = Window;
            Nav.OnInsetChanged = w.SetContentInset;
            w.SetContentInset(Nav.Inset);   // the Nav reported its first inset before anyone listened
            var nav = Nav;
            if (o.Search) w.ShowSearch("Search", q => nav.Filter(q), true);

            EventCallback<GeometryChangedEvent> laid = null;
            laid = _ => { if (Place()) Events.Off(Surface.Root, laid); };
            Events.On(Surface.Root, laid);
        }

        private static Rect Centred(Vector2 panel, Vector2 f)
        {
            float w = panel.x * Mathf.Clamp01(f.x), h = panel.y * Mathf.Clamp01(f.y);
            return new Rect((panel.x - w) / 2f, (panel.y - h) / 2f, w, h);
        }

        // Once, on the first layout of the surface while it is visible: from then on the rect is stored.
        private bool Place()
        {
            if (_placed) return true;
            var root = Surface.Root;
            if (root == null || Window == null || !Surface.Visible) return false;
            var size = root.layout.size;
            if (!(size.x > 0f && size.y > 0f)) return false;
            _placed = true;
            Window.Storing = true;
            // A rect set before now (stored, or given through Window.Rect) is kept, clamped to the panel.
            Window.Rect = Window.HasStoredGeometry || Window.RectGiven ? Window.Rect : _fixed ? _first : Centred(size, _fraction);
            return true;
        }

        public static SettingsWindow Open(Host host, string id, string title, IList<NavItem> pages, Action<SettingsWindowOptions> configure = null)
        {
            host = host ?? Host.Fallback;
            var o = new SettingsWindowOptions();
            if (configure != null) Safe.Run(host, "SettingsWindow configure '" + (id ?? title) + "'", () => configure(o));
            return new SettingsWindow(host, id, title, pages, o);
        }

        /// Call every frame while it is shown: the open page rebuilds when its Revision moves.
        public void Tick()
        {
            if (!_placed) Place();
            if (Nav != null) Nav.Tick();
        }

        public void Dispose() { Surface.Dispose(); }
    }
}
