using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* A draggable, resizable, scalable window inside a Surface.

       Every panel in these mods needs the same handful of behaviours — move it, resize it, keep it
       on screen when the resolution changes, remember where it was — and the uGUI framework grew a
       DragHandle and a ResizeHandle per mod to do it. One implementation here instead.

       Geometry is kept in POINTS, not style values read back: resolvedStyle is NaN until layout
       runs, and reading a position back after every drag frame accumulates rounding. The window
       owns its rect and writes it down. */
    public sealed class Window
    {
        public const float MinWidth = 320f, MinHeight = 200f;
        private const float GripSize = 16f;
        // The theme's TitleBarHeight, kept at build: Clamp runs on every drag and resize move.
        private readonly float _titleBar;

        /// The outer element. Style it as little as possible from outside; use Body.
        public VisualElement Root { get; private set; }

        /// Where content goes. Clips: anything inside is cut at the window edge rather than
        /// painting over the chrome, which is what a long log line did to the footer.
        public VisualElement Body { get; private set; }

        /// Right-hand side of the title bar. Put toolbar buttons here.
        public VisualElement TitleBarRight { get; private set; }

        /// The title block on the left of the title bar, holding the title. A logo goes in at index 0.
        public VisualElement TitleBarLeft => _headerGap;

        /// Search box in the header. Hidden until a panel calls ShowSearch.
        public TextField Search { get; private set; }

        /// Where the content pane starts, so the header can line up with it.
        public const float RailAlign = 200f;

        private VisualElement _headerGap;

        /* The Nav the title bar's sidebar toggle drives: the first one built in the body, or the next one built
           once that one has left it (a host that clears the body and builds its Nav again). The window keeps the
           toggle, so a rebuilt Nav takes the same button. Found from the body through Data. */
        internal Nav MainNav;
        internal Button SidebarToggle;
        internal VisualElement SidebarToggleTipOn;   // the tip last put on the toggle, by whichever Nav drew it
        internal string SidebarToggleTip;
        internal Label TitleLabel;
        internal sealed class Owner { public Window W; }

        internal static Window Of(VisualElement e)
        {
            for (; e != null; e = e.parent) { var o = Data.Find<Owner>(e); if (o != null) return o.W; }
            return null;
        }

        /// Keep the search box lined up with a content pane whose left edge moves.
        public void SetContentInset(float inset)
        {
            // Never narrower than a mod name, nor than what it holds (a logo, the sidebar toggle, the title):
            // with the rail collapsed the block would otherwise shrink and the title run into the search box.
            if (_headerGap != null) _headerGap.style.minWidth = Mathf.Max(inset, 110f);
        }

        public void ShowSearch(string placeholder, Action<string> onChange) { ShowSearch(placeholder, onChange, false); }

        // own: the placeholder is Facet's own word (SettingsWindow's), shown from Facet's table when the host has none.
        internal void ShowSearch(string placeholder, Action<string> onChange, bool own)
        {
            if (Search == null) return;
            _searchBox.style.display = DisplayStyle.Flex;
            Controls.WireSearch(Search, _searchClear, placeholder, onChange, _host, "Window.Search", own);
        }

        private VisualElement _searchBox;
        private Button _searchClear;

        /// Footer strip along the bottom, above the resize grip. Always on top of Body's content.
        public VisualElement Footer { get; private set; }

        /* Hint on the left, version on the right — the shape both mods' panels already use, so it
           belongs to the window rather than to each panel that wants one. */
        /// version on the left, hint on the right next to the resize grip.
        public void SetFooter(string hint, string version)
        {
            if (_footerHint != null) _footerHint.text = hint ?? "";
            if (_footerRight != null) _footerRight.text = version ?? "";
        }

        private Label _footerHint, _footerRight;

        /// Raised on any press in the window (title bar, grip and buttons included), for the panel that owns it to
        /// raise its surface: Window has no Surface of its own — it is just the element tree inside one.
        public event Action Raised
        {
            add { _raised += value; _raisedList = Safe.Handlers<Action>(_raised); }
            remove { _raised -= value; _raisedList = Safe.Handlers<Action>(_raised); }
        }
        private Action _raised;
        private Action[] _raisedList;

        /// Raised by the close button. With no subscriber the window's surface hides; with any,
        /// only they decide (animate, veto, dispose).
        public event Action<Window> CloseRequested;

        private readonly string _id;
        private readonly Host _host;
        private readonly string _key;
        private Rect _rect;
        private float _scale = 1f;
        private Vector2 _dragFrom;
        private Rect _rectFrom;

        /* Where each window was last left, by id.

           Kept in memory AND written through the host's Write when it has one. Facet cannot own
           a settings file — it has no idea where a given mod keeps its data — so the host supplies
           Read and Write and decides the format. Without them the geometry still survives a
           reopen, just not a restart. Keyed by host as well, so two mods' "main" do not collide. */
        private static readonly Dictionary<string, Rect> _saved = new Dictionary<string, Rect>();
        private static readonly Dictionary<string, float> _savedScale = new Dictionary<string, float>();

        /* "x,y,w,h,scale" — invariant culture, because a machine with a comma decimal separator
           would otherwise write a string it cannot read back. */
        private static bool TryParse(string s, out Rect r, out float scale)
        {
            r = default(Rect); scale = 1f;
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Split(',');
            if (p.Length < 5) return false;
            float x, y, w, h, sc;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, c, out x)) return false;
            if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, c, out y)) return false;
            if (!float.TryParse(p[2], System.Globalization.NumberStyles.Float, c, out w)) return false;
            if (!float.TryParse(p[3], System.Globalization.NumberStyles.Float, c, out h)) return false;
            if (!float.TryParse(p[4], System.Globalization.NumberStyles.Float, c, out sc)) return false;
            r = new Rect(x, y, w, h); scale = sc;
            return true;
        }

        public Window(VisualElement parent, string id, string title, Rect initial) : this(parent, id, title, initial, true) { }

        /* store false: the rect is kept nowhere (not in memory, not through the host) until Storing is set, so a
           window built ahead and never shown leaves no rect behind it. */
        internal Window(VisualElement parent, string id, string title, Rect initial, bool store)
        {
            Storing = store;
            _id = id ?? title ?? "window";
            _host = Host.For(parent);
            _key = _host.Name + "|" + _id;
            Rect stored; float storedScale;
            HasStoredGeometry = true;
            if (_saved.ContainsKey(_key))
            {
                _rect = _saved[_key];
                _scale = _savedScale.ContainsKey(_key) ? _savedScale[_key] : 1f;
            }
            else if (TryParse(_host.Read("window." + _id), out stored, out storedScale))
            {
                _rect = stored; _scale = storedScale;
            }
            else { _rect = initial; HasStoredGeometry = false; }

            Root = Ui.Card(parent);
            var t = Theme.For(Root); var sz = t.Sizes; var looks = t.Looks;
            _titleBar = sz.TitleBarHeight;
            Root.pickingMode = PickingMode.Position;
            Root.style.position = Position.Absolute;
            Root.style.overflow = Overflow.Hidden;      // chrome is never painted over
            Ui.SetPadding(Root, 0f);                    // the parts do their own padding
            // Scale about the top-left, so the stored rect still describes where it is.
            Root.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);

            var bar = Ui.Row(Root);
            bar.style.height = _titleBar;
            Paint.Look(bar, looks.Chrome);
            bar.style.paddingLeft = sz.Pad;
            bar.style.paddingRight = sz.Gap;
            // No rail toggle: the rail has a drag handle of its own, which resizes as well as
            // collapses. The title takes the space that button used to.
            /* The title sits in a block as wide as the rail, so the search box that follows starts
               exactly where the content pane does. A gap that merely GREW to that width could not
               do it: the title's own width pushed the box further right by however long the mod's
               name happened to be. */
            _headerGap = Ui.Row(bar);
            _headerGap.style.minWidth = RailAlign;   // its width is its content's, never less than this
            _headerGap.style.flexShrink = 0f;

            var label = TitleLabel = Ui.Text(_headerGap, title, sz.Body);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginLeft = 2f;
            // Keeps the name off the search box when the rail is collapsed and the block is narrow.
            label.style.marginRight = sz.Pad;
            label.style.flexShrink = 0f;
            label.pickingMode = PickingMode.Ignore;     // clicks on the text still drag the bar
            /* A search box in the header, where the mods already put theirs. Empty and hidden
               until a panel fills it — a window with nothing to search should not show one. */
            Search = Controls.SearchBox(bar, out _searchBox, out _searchClear);
            _searchBox.style.marginRight = sz.Gap;
            _searchBox.style.display = DisplayStyle.None;

            var spacer = Ui.Box(bar);
            spacer.style.flexGrow = 1f;   // pins the controls right whatever the search box does

            TitleBarRight = Ui.Row(bar);

            /* Undo and redo in the header, where they apply to whatever the window is showing. Bare
               like the ×, so the three read as one set of window controls, and at title size: at body
               size the arrows were specks beside it. */
            Controls.UndoRedo(TitleBarRight, _host, out _, out _);

            /* × rather than a "Close" button, matching the mods' own panels. Sized as a square so
               it reads as a window control instead of as another toolbar button. */
            var close = Ui.Btn(TitleBarRight, "\u2715", RequestClose);
            Paint.Look(close, CloseLook);
            close.style.width = 30f;
            close.style.height = 26f;
            close.style.marginRight = 0f;
            /* Centre the glyph by hand. A Button's text is laid out by the theme, so without
               one it sits wherever the default padding leaves it — which is what made the ✕
               look off-centre. */
            Ui.SetPadding(close, 0f);
            close.style.unityTextAlign = TextAnchor.MiddleCenter;
            close.style.fontSize = sz.Body;

            Body = Ui.Box(Root);
            Data.Get<Owner>(Body).W = this;
            Body.style.flexGrow = 1f;
            /* The one element that MUST absorb slack. Ui.Box defaults to flexShrink 0 so rows keep
               their height, but the window body is the opposite case: it has to shrink to whatever
               the window is, or it grows to its content and the window merely clips it. That is
               what made the debug log unscrollable — the list's viewport came out 2700px tall
               inside a 580px window, so the list reported that everything fitted. */
            Body.style.flexShrink = 1f;
            Body.style.minHeight = 0f;
            Body.style.overflow = Overflow.Hidden;
            Ui.SetPadding(Body, sz.Pad);

            Footer = Ui.Row(Root);
            Paint.Look(Footer, looks.Chrome);
            // No scale buttons on the chrome: panel size is a setting, and a control that lives on
            // every window competes with the content for a job done once.
            Footer.style.paddingLeft = sz.Pad;
            Footer.style.paddingRight = sz.Pad;
            Footer.style.paddingTop = 4f;
            Footer.style.paddingBottom = 4f;
            /* Version on the left, hint on the right beside the grip — the grip is a corner
               affordance and the hint explains the window, so they belong together. */
            _footerRight = Ui.Muted(Footer, "");
            _footerRight.style.flexGrow = 1f;
            _footerHint = Ui.Muted(Footer, "");
            _footerHint.style.marginRight = 14f;   // clear of the resize grip

            /* Click anywhere in the window to raise it above the other one. Trickle-down, because the title bar's drag,
               the resize grip and a Button's clickable stop the press before it would bubble here. */
            Events.On<PointerDownEvent>(Root, _ =>
            {
                Safe.Each(_host, "Window.Raised", _raisedList);
            }, TrickleDown.TrickleDown);

            MakeDraggable(bar);
            MakeResizable();
            Apply();
        }

        /* Each subscriber under its own Safe, so one that throws leaves the rest deciding. With none,
           the surface the window sits in hides (a surface Root is an ancestor, not always the parent). */
        private void RequestClose()
        {
            var h = CloseRequested;
            if (h != null)
            {
                foreach (Action<Window> a in h.GetInvocationList()) Safe.Run(_host, "Window.CloseRequested", () => a(this));
                return;
            }
            foreach (var s in Ui.Stack) if (s.Root != null && s.Root.Contains(Root)) { s.Visible = false; return; }
        }

        // A window control, not a toolbar button: bare until hovered, then Danger.
        private static readonly Look CloseLook = new Look { Text = Role.Text, HoverBg = Role.Danger, PressedBg = Role.Danger };

        /// Clamped to 0.6-2.5.
        public float Scale
        {
            get { return _scale; }
            set
            {
                _scale = Mathf.Clamp(value, 0.6f, 2.5f);
                _savedScale[_key] = _scale;
                Apply();
            }
        }

        /// True when the rect came from where this id's window was last left (this session, or the host's Read),
        /// false when the window opened at the rect it was built with.
        public bool HasStoredGeometry { get; private set; }

        // False while the rect is kept nowhere; set it to start keeping it (the next Apply stores the rect).
        internal bool Storing;

        // Rect was set while Storing was false: the owner's first placement keeps it.
        internal bool RectGiven;

        public Rect Rect
        {
            get { return _rect; }
            set { if (!Storing) RectGiven = true; _rect = value; Clamp(); Apply(); }
        }

        /* Scale magnifies the CONTENTS, not the window. The transform would otherwise blow the
           whole box up and push it off screen, when what scaling is for is reading the text — so
           the layout size is divided by the scale that multiplies it, leaving the rendered
           footprint exactly the rect the user dragged out. Scaling up therefore shows less, larger,
           in the same space. */
        private void Apply()
        {
            Root.style.left = _rect.x;
            Root.style.top = _rect.y;
            Root.style.width = _rect.width / _scale;
            Root.style.height = _rect.height / _scale;
            Root.style.scale = new Scale(new Vector2(_scale, _scale));
            if (!Storing) return;
            _saved[_key] = _rect;
            _savedScale[_key] = _scale;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            _host.Write("window." + _id, _rect.x.ToString(c) + "," + _rect.y.ToString(c) + ","
                                + _rect.width.ToString(c) + "," + _rect.height.ToString(c) + ","
                                + _scale.ToString(c));
        }

        /* Keep the window reachable. A resolution change or a scale bump can leave it mostly off
           screen, and a title bar you cannot reach is a window you cannot move back. Everything is
           in panel points, which is what left/top are in — Screen.width is PIXELS, so it has to be
           converted through the panel's own scale or the clamp is wrong at every resolution but
           the reference one. */
        private void Clamp()
        {
            var panel = Root.panel;
            if (panel == null) return;
            var size = Root.parent != null ? Root.parent.layout.size : Vector2.zero;
            if (size.x <= 0f || size.y <= 0f) return;

            _rect.width = Mathf.Clamp(_rect.width, MinWidth, Mathf.Max(MinWidth, size.x));
            _rect.height = Mathf.Clamp(_rect.height, MinHeight, Mathf.Max(MinHeight, size.y));
            // At least the title bar stays on screen, horizontally by a good grab's worth.
            float maxX = size.x - 80f;
            float maxY = size.y - _titleBar * _scale;
            _rect.x = Mathf.Clamp(_rect.x, -_rect.width + 80f, Mathf.Max(0f, maxX));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, maxY));
        }

        private void MakeDraggable(VisualElement bar)
        {
            Events.On<PointerDownEvent>(bar, e =>
            {
                if (e.button != 0) return;
                _dragFrom = e.position;
                _rectFrom = _rect;
                bar.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            Events.On<PointerMoveEvent>(bar, e =>
            {
                if (!bar.HasPointerCapture(e.pointerId)) return;
                var d = (Vector2)e.position - _dragFrom;
                _rect.x = _rectFrom.x + d.x;
                _rect.y = _rectFrom.y + d.y;
                Clamp();
                Apply();
            });
            Events.On<PointerUpEvent>(bar, e =>
            {
                if (!bar.HasPointerCapture(e.pointerId)) return;
                bar.ReleasePointer(e.pointerId);
            });
        }

        private void MakeResizable()
        {
            var grip = new VisualElement();
            grip.style.position = Position.Absolute;
            grip.style.right = 0f; grip.style.bottom = 0f;
            grip.style.width = GripSize; grip.style.height = GripSize;
            grip.pickingMode = PickingMode.Position;
            Cursors.Set(grip, Cursors.Kind.ResizeCorner);
            Root.Add(grip);

            /* A 3x3 dot grid with the top-left corner dropped, which is the grip Topaz's panels
               already use. Dots rather than rotated bars: a rotation about a percentage origin
               moves the box as well as turning it, which is why the earlier staircase never lined
               up. */
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (row + col < 2) continue;
                    var dot = new VisualElement();
                    dot.style.position = Position.Absolute;
                    dot.style.right = 3f + (2 - col) * 4f;
                    dot.style.bottom = 3f + (2 - row) * 4f;
                    dot.style.width = 2f;
                    dot.style.height = 2f;
                    dot.pickingMode = PickingMode.Ignore;
                    grip.Add(dot);
                    Paint.Bg(dot, Role.TextMuted);
                }

            Events.On<PointerDownEvent>(grip, e =>
            {
                if (e.button != 0) return;
                _dragFrom = e.position;
                _rectFrom = _rect;
                grip.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            Events.On<PointerMoveEvent>(grip, e =>
            {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                var d = (Vector2)e.position - _dragFrom;
                _rect.width = _rectFrom.width + d.x;
                _rect.height = _rectFrom.height + d.y;
                Clamp();
                Apply();
            });
            Events.On<PointerUpEvent>(grip, e =>
            {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                grip.ReleasePointer(e.pointerId);
            });
        }

        /// Re-clamp after the screen size changes. Cheap; call it when a panel becomes visible.
        public void Reclamp() { Clamp(); Apply(); }
    }
}
