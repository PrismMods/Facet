using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* Element factories. Every style is set in C# — there is no runtime USS — so this is where the
       repetition goes instead of into every screen.

       Deliberately thin: these return plain VisualElements, not wrapper types, so anything the
       factories do not cover is just UI Toolkit and needs no escape hatch. */
    public static class Ui
    {
        /* How many Facet windows are on screen.

           The mods need this for the same reason each of them already guards its own panels: the
           editor zooms on the scroll wheel and the game reacts to clicks, so scrolling a list or
           dragging a window inside a Facet panel would also drive the world behind it. A mod checks
           Input.AnyWindowOpen in its own zoom/click path — it cannot be enforced from here, because the
           input belongs to the game and the patch belongs to the mod.

           A counter rather than a bool: two windows can be open, and closing one must not tell the
           editor it is free again. */
        public static int OpenWindows { get; internal set; }   // Surface keeps it

        /* Open surfaces, bottom first: by layer, then by raise order (Surface.BringToFront keeps it
           so). Per loaded Facet; Input coordinates across versions through Shared. */
        internal static readonly List<Surface> Stack = new List<Surface>();

        public static VisualElement Box(VisualElement parent = null)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Column;
            /* flexShrink 0 by default, against the flexbox norm of 1.

               The CSS default assumes a layout that would rather squash than overflow. A settings
               page wants the opposite: rows and sections have a real height, and something too tall
               for its container should SCROLL, not compress until its text prints through the row
               below. Three separate overlaps — the debug panel's header under its tab bar, a
               section's rows through the next section's heading, and labels through each other —
               were all this one default, so it is set once here. Anything that genuinely should
               absorb slack sets flexShrink back to 1 on itself. */
            e.style.flexShrink = 0f;
            parent?.Add(e);
            return e;
        }

        public static VisualElement Row(VisualElement parent = null)
        {
            var e = Box(parent);
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        /// A bordered, rounded container — the standard panel body (the theme's Looks.Card).
        public static VisualElement Card(VisualElement parent = null)
        {
            var e = Box(parent);
            var t = Theme.For(e); var s = t.Sizes;
            Paint.Look(e, t.Looks.Card);
            SetBorderWidth(e, s.Line);
            SetRadius(e, s.Radius);
            SetPadding(e, s.Pad);
            return e;
        }

        /// size: null is the theme's Body. The parent comes first, as in every Facet part; parent may be null.
        public static Label Text(VisualElement parent, string text, float? size = null, Role role = Role.Text, bool own = false)
        {
            var l = NewLabel(parent, size ?? Theme.For(parent).Sizes.Body);
            Words.Label(l, text, own);   // after Add: the parent says which host's table answers
            Paint.Fg(l, role);
            return l;
        }

        // One line, cut with an ellipsis; added before anything translates it.
        private static Label NewLabel(VisualElement parent, float size)
        {
            var l = new Label();
            l.style.fontSize = size;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.overflow = Overflow.Hidden;
            parent?.Add(l);
            return l;
        }

        public static Label Muted(VisualElement parent, string text, bool own = false)
            => Text(parent, text, Theme.For(parent).Sizes.Small, Role.TextMuted, own);

        /// An upper-case header (a card title, a rail group): translated first, then upper-cased, and
        /// still upper-case after a relabel. Hangul and Han have no case and pass through unchanged.
        public static Label Caps(VisualElement parent, string text, bool own = false)
        {
            var t = Theme.For(parent); var s = t.Sizes;
            var l = NewLabel(parent, s.Caps);
            l.style.letterSpacing = s.CapsSpacing;
            Words.Caps(l, text, own);
            Paint.Look(l, t.Looks.Caps);
            return l;
        }

        /// The theme's Looks.Button: hover, pressed and selected (Paint.Select) are Paint's, not the caller's.
        public static Button Btn(VisualElement parent, string text, Action onClick, bool own = false)
        {
            var b = new Button();
            if (onClick != null) b.clicked += () => Safe.Run(Host.For(b), "Button '" + b.text + "'", onClick);
            parent?.Add(b);
            var t = Theme.For(b); var s = t.Sizes;
            b.style.fontSize = s.Small;
            SetBorderWidth(b, s.Line);   // the focus ring (Look.FocusBorder) paints this; unfocused it stays unset/clear
            SetRadius(b, s.Radius);
            SetPadding(b, s.ControlPad);
            b.style.marginLeft = 0f; b.style.marginRight = s.Gap;
            b.style.marginTop = 0f; b.style.marginBottom = 0f;
            Words.Label(b, text, own);
            Paint.Look(b, t.Looks.Button);
            return b;
        }

        /// Bare until hovered, then lit: the window header's glyphs, icon buttons and menu titles. Selected stays lit.
        internal static readonly Look Bare = new Look { Text = Role.Text, HoverBg = Role.RowHover, PressedBg = Role.RowAlt, SelectedBg = Role.RowHover };

        /// A square button showing `icon` (Sizes.Icon, tinted as text) on a bare face lit on hover; `tooltip` is
        /// an English key, translated like a label.
        public static Button IconButton(VisualElement parent, Background icon, string tooltip, Action click)
        {
            var b = Btn(parent, "", click);
            var s = Theme.For(b).Sizes;
            Paint.Look(b, Bare);
            SetPadding(b, 0f);
            b.style.width = s.Icon + 2f * s.ControlPad;
            b.style.height = s.Icon + 2f * s.ControlPad;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            // A child is safe here: the button has no text whose measure it would drop.
            var image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.style.width = s.Icon;
            image.style.height = s.Icon;
            image.style.backgroundImage = icon;
            b.Add(image);
            Paint.Tint(image, Role.Text);
            Words.Tip(b, tooltip);
            return b;
        }

        /* A virtualized list. This is the reason for moving to UI Toolkit at all: ListView recycles
           rows, so a 50k-line log costs the same as a 50-line one. The uGUI framework had to be
           told how many rows to build. */
        public static ListView List(VisualElement parent, Func<IList> source, Action<VisualElement, int> bind)
        {
            // Read once here: makeItem and bindItem run as rows recycle, and Dark's Sizes is a fresh copy per read.
            var sz = Theme.For(parent).Sizes;
            float small = sz.Small;
            var lv = new ListView
            {
                fixedItemHeight = sz.ListRowHeight,
                selectionType = SelectionType.None,
                showBorder = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            lv.makeItem = () =>
            {
                var l = Text(null, "", small);
                l.style.paddingLeft = 6f;
                l.style.unityTextAlign = TextAnchor.MiddleLeft;
                return l;
            };
            lv.bindItem = (e, i) => Safe.Run(Host.For(lv), "Ui.List.bind", () => bind(e, i));
            lv.itemsSource = Safe.Get(Host.For(parent), "Ui.List.source", source, (IList)new List<object>());
            lv.style.flexGrow = 1f;
            lv.style.minHeight = 0f;   // or the list sizes to its rows and the window clips it
            parent?.Add(lv);
            return lv;
        }

        /// A vertical ScrollView with Facet's scrollbars that takes the height left in its parent (flexGrow 1,
        /// minHeight 0, or it sizes to its content and the parent clips it): what goes in a RightFill column.
        public static ScrollView Scroll(VisualElement parent)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.style.flexGrow = 1f;
            sv.style.minHeight = 0f;
            sv.contentContainer.style.flexShrink = 0f;
            Skin.When<ScrollView>(sv, Skin.Scroll);
            parent?.Add(sv);
            return sv;
        }

        // ── delegate-only hooks: for code that cannot name a UI Toolkit event type or a scheduled item ──

        /* Press, move and release on e, at positions local to e; buttons 0 (left), 1 (right) and 2 (middle). A press
           captures the pointer until its release, so the moves and the release still arrive outside e; a lost
           capture, or e leaving its panel, ends the press like a release at the last position. move gets every
           move e hears, a hover's too. Each delegate runs under Safe. Disposing unregisters (and ends a held press without calling up). */
        public static IDisposable Pointer(VisualElement e, Action<Vector2, int> down, Action<Vector2> move = null, Action<Vector2, int> up = null)
        {
            if (e == null) throw new ArgumentNullException("e");
            var p = new Pointing { E = e, Down = down, Move = move, Up = up };
            Events.On<PointerDownEvent>(e, p.OnDown);
            Events.On<PointerMoveEvent>(e, p.OnMove);
            Events.On<PointerUpEvent>(e, p.OnUp);
            Events.On<PointerCaptureOutEvent>(e, p.OnLost);
            Events.On<DetachFromPanelEvent>(e, p.OnDetach);
            return p;
        }

        // Positions and the button go through fields to delegates made once, so a drag's moves allocate nothing.
        private sealed class Pointing : IDisposable
        {
            internal VisualElement E;
            internal Action<Vector2, int> Down, Up;
            internal Action<Vector2> Move;
            private readonly Action _down, _move, _up;
            private int _held = -1, _id, _b;
            private Vector2 _at;

            internal Pointing() { _down = RunDown; _move = RunMove; _up = RunUp; }
            private void RunDown() { Down(_at, _b); }
            private void RunMove() { Move(_at); }
            private void RunUp() { Up(_at, _b); }

            internal void OnDown(PointerDownEvent ev)
            {
                if (ev.button < 0 || ev.button > 2 || _held >= 0) return;
                _held = _b = ev.button; _id = ev.pointerId; _at = ev.localPosition;
                E.CapturePointer(_id);
                if (Down != null) Safe.Run(Host.For(E), "Ui.Pointer down", _down);
            }

            internal void OnMove(PointerMoveEvent ev)
            {
                _at = ev.localPosition;
                if (Move != null) Safe.Run(Host.For(E), "Ui.Pointer move", _move);
            }

            internal void OnUp(PointerUpEvent ev)
            {
                if (_held < 0 || ev.button != _held) return;
                _at = ev.localPosition;
                End(true);
            }

            internal void OnLost(PointerCaptureOutEvent ev) { if (_held >= 0) End(true); }

            // Leaving the panel mid-press may send no capture-out; without this the next press would be ignored.
            internal void OnDetach(DetachFromPanelEvent ev) { if (_held >= 0) End(true); }

            private void End(bool report)
            {
                _b = _held;
                _held = -1;
                if (E.HasPointerCapture(_id)) E.ReleasePointer(_id);
                if (report && Up != null) Safe.Run(Host.For(E), "Ui.Pointer up", _up);
            }

            public void Dispose()
            {
                if (E == null) return;
                Events.Off<PointerDownEvent>(E, OnDown);
                Events.Off<PointerMoveEvent>(E, OnMove);
                Events.Off<PointerUpEvent>(E, OnUp);
                Events.Off<PointerCaptureOutEvent>(E, OnLost);
                Events.Off<DetachFromPanelEvent>(E, OnDetach);
                if (_held >= 0) End(false);
                E = null;
            }
        }

        /* tick every ms milliseconds (0: every frame) while e is on a panel and its surface is visible; it costs
           nothing else. Under Safe: three throws in a row stop it until e is attached again. Disposing stops it. */
        public static IDisposable Every(VisualElement e, int ms, Action tick)
        {
            if (e == null) throw new ArgumentNullException("e");
            var t = new Ticking { E = e, Tick = tick };
            t.Run = t.Step;   // made once: the tick runs every frame at 0 and must not allocate
            t.Item = e.schedule.Execute(t.Run).Every(Math.Max(1, ms));
            Events.On<AttachToPanelEvent>(e, t.OnAttach);
            if (e.panel != null) t.Owner = Popup.OwnerOf(e);
            return t;
        }

        private sealed class Ticking : IDisposable
        {
            internal VisualElement E;
            internal Action Tick, Run;
            internal IVisualElementScheduledItem Item;
            internal Surface Owner;
            internal Host Host;
            private int _fails;

            internal void OnAttach(AttachToPanelEvent ev)
            {
                Owner = Popup.OwnerOf(E);
                Host = Facet.Host.For(E);
                _fails = 0;
                if (Item != null) Item.Resume();
            }

            internal void Step()
            {
                if (E == null || E.panel == null || Tick == null || (Owner != null && !Owner.Visible)) return;
                if (Safe.Run(Host ?? (Host = Facet.Host.For(E)), "Ui.Every", Tick)) _fails = 0;
                else if (++_fails >= 3) Item.Pause();   // until the next attach
            }

            public void Dispose()
            {
                if (E == null) return;
                Item.Pause();
                Events.Off<AttachToPanelEvent>(E, OnAttach);
                E = null;
            }
        }

        /// attached runs whenever e joins a panel (at once if it is on one now), detached whenever it leaves; both under Safe.
        public static void WhileAttached(VisualElement e, Action attached, Action detached)
        {
            if (e == null) throw new ArgumentNullException("e");
            if (attached != null) Events.On<AttachToPanelEvent>(e, _ => Safe.Run(Host.For(e), "Ui.WhileAttached attached", attached));
            if (detached != null) Events.On<DetachFromPanelEvent>(e, _ => Safe.Run(Host.For(e), "Ui.WhileAttached detached", detached));
            if (attached != null && e.panel != null) Safe.Run(Host.For(e), "Ui.WhileAttached attached", attached);
        }

        /* reached when e's end comes within margin of the bottom of the nearest ScrollView's viewport (or e is no taller
           than it), checked after a scroll and after a geometry change of e or the viewport, while e is attached and
           displayed; at most once per frame, under Safe. With no ScrollView, e counts as at the end. Disposing stops it. */
        public static IDisposable ScrollEnd(VisualElement e, float margin, Action reached)
        {
            if (e == null) throw new ArgumentNullException("e");
            return new ScrollEnding(e, margin, reached);
        }

        internal sealed class ScrollEnding : IDisposable
        {
            private VisualElement _e;
            private readonly float _margin;
            private readonly Action _reached;
            private readonly Action<float> _scrolled;
            private readonly EventCallback<GeometryChangedEvent> _geo;
            private readonly EventCallback<AttachToPanelEvent> _attach;
            private readonly EventCallback<DetachFromPanelEvent> _detach;
            private ScrollView _sv;
            private int _frame = -1;

            internal ScrollEnding(VisualElement e, float margin, Action reached)
            {
                _e = e; _margin = margin; _reached = reached;
                _scrolled = _ => Check();
                _geo = _ => Check();
                _attach = _ => Attach();
                _detach = _ => Detach();
                Events.On(e, _attach);
                Events.On(e, _detach);
                Events.On(e, _geo);
                if (e.panel != null) Attach();
            }

            private void Attach()
            {
                Detach();
                _sv = _e.GetFirstAncestorOfType<ScrollView>();
                if (_sv != null)
                {
                    _sv.verticalScroller.valueChanged += _scrolled;
                    Events.On(_sv.contentViewport, _geo);
                }
                Check();
            }

            private void Detach()
            {
                if (_sv == null) return;
                _sv.verticalScroller.valueChanged -= _scrolled;
                Events.Off(_sv.contentViewport, _geo);
                _sv = null;
            }

            /// Calls reached when the end is near, unless it already ran this frame.
            internal void Check()
            {
                if (_e == null || _e.panel == null || _reached == null || _frame == Time.frameCount || !Shown(_e)) return;
                // Never laid out (shown before its first layout reads 0 x 0 and display Flex): its geometry change comes back here.
                var size = _e.layout;
                if (!(size.width > 0f) && !(size.height > 0f)) return;
                if (_sv != null)
                {
                    float vp = _sv.contentViewport.layout.height, end = _e.worldBound.yMax - _sv.contentContainer.worldBound.y;
                    if (!(vp > 0f) || float.IsNaN(end)) return;   // not laid out yet: its geometry change comes back here
                    // Whole units: a scaled panel snaps layout to device pixels (a 600 view reads 599.75).
                    if (!ListMath.NearEnd(Mathf.Round(_sv.scrollOffset.y), Mathf.Round(end), Mathf.Round(vp), _margin)) return;
                }
                _frame = Time.frameCount;
                Safe.Run(Host.For(_e), "Ui.ScrollEnd", _reached);
            }

            public void Dispose()
            {
                if (_e == null) return;
                Detach();
                Events.Off(_e, _attach);
                Events.Off(_e, _detach);
                Events.Off(_e, _geo);
                _e = null;
            }
        }

        // e and every ancestor displayed (resolved: a just-set display shows after the next layout).
        internal static bool Shown(VisualElement e)
        {
            for (var x = e; x != null; x = x.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        /// The toolbar's search field on its own: a ✕ that clears it, `placeholder` an English key, `changed` on every keystroke.
        public static TextField Search(VisualElement parent, Action<string> changed, string placeholder = null)
        {
            VisualElement box; Button clear;
            var f = Controls.SearchBox(parent, out box, out clear);
            Controls.WireSearch(f, clear, placeholder, changed, Host.For(parent), "Ui.Search");
            return f;
        }

        // ── style helpers: UI Toolkit splits these across four properties each ──

        public static void SetPadding(VisualElement e, float v)
        {
            e.style.paddingLeft = v; e.style.paddingRight = v;
            e.style.paddingTop = v; e.style.paddingBottom = v;
        }

        public static void SetMargin(VisualElement e, float v)
        {
            e.style.marginLeft = v; e.style.marginRight = v;
            e.style.marginTop = v; e.style.marginBottom = v;
        }

        public static void SetRadius(VisualElement e, float v)
        {
            e.style.borderTopLeftRadius = v; e.style.borderTopRightRadius = v;
            e.style.borderBottomLeftRadius = v; e.style.borderBottomRightRadius = v;
        }

        public static void SetBorderWidth(VisualElement e, float v)
        {
            e.style.borderLeftWidth = v; e.style.borderRightWidth = v;
            e.style.borderTopWidth = v; e.style.borderBottomWidth = v;
        }
    }
}
