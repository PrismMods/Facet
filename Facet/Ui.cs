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
