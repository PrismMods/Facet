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
           AnyWindowOpen in its own zoom/click path — it cannot be enforced from here, because the
           input belongs to the game and the patch belongs to the mod.

           A counter rather than a bool: two windows can be open, and closing one must not tell the
           editor it is free again. */
        public static int OpenWindows;

        public static bool AnyWindowOpen => Input.AnyWindowOpen;

        public static bool BlocksGameInput => Input.BlocksGameInput;

        public static bool PointerOverWindow(Vector2 pointerScreen) => Input.PointerOverWindow(pointerScreen);

        public static bool TextFocused => Input.TextFocused;

        public static void TickWheel(Vector2 pointerScreen, float delta) => Input.TickWheel(pointerScreen, delta);

        /* Open surfaces, bottom first: by layer, then by raise order (Surface.BringToFront keeps it
           so). Per loaded Facet; Input coordinates across versions through Shared. */
        internal static readonly List<Surface> Stack = new List<Surface>();

        public static bool HandleEscape() => Input.HandleEscape();

        public static void ConsumeEscape() => Input.ConsumeEscape();

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

        /// size: null is the theme's Body.
        public static Label Text(string text, VisualElement parent = null, float? size = null, Role role = Role.Text, bool own = false)
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

        public static Label Muted(string text, VisualElement parent = null, bool own = false)
            => Text(text, parent, Theme.For(parent).Sizes.Small, Role.TextMuted, own);

        /// An upper-case header (a card title, a rail group): translated first, then upper-cased, and
        /// still upper-case after a relabel. Hangul and Han have no case and pass through unchanged.
        public static Label Caps(string text, VisualElement parent = null, bool own = false)
        {
            var t = Theme.For(parent); var s = t.Sizes;
            var l = NewLabel(parent, s.Caps);
            l.style.letterSpacing = s.CapsSpacing;
            Words.Caps(l, text, own);
            Paint.Look(l, t.Looks.Caps);
            return l;
        }

        /// The theme's Looks.Button: hover, pressed and selected (Paint.Select) are Paint's, not the caller's.
        public static Button Btn(string text, Action onClick, VisualElement parent = null, bool own = false)
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
            var b = Btn("", click, parent);
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
        public static ListView List(Func<IList> source, Action<VisualElement, int> bind, VisualElement parent = null)
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
                var l = Text("", null, small);
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
