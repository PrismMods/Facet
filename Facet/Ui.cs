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

        /// A bordered, rounded container — the standard panel body.
        public static VisualElement Card(VisualElement parent = null)
        {
            var e = Box(parent);
            Paint.Look(e, Look.Card);
            SetBorderWidth(e, 1f);
            SetRadius(e, Tokens.Radius);
            SetPadding(e, Tokens.Pad);
            return e;
        }

        public static Label Text(string text, VisualElement parent = null, float? size = null, Role role = Role.Text, bool own = false)
        {
            var l = new Label();
            l.style.fontSize = size ?? Tokens.FontSize;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.overflow = Overflow.Hidden;
            parent?.Add(l);
            Words.Label(l, text, own);   // after Add: the parent says which host's table answers
            Paint.Fg(l, role);
            return l;
        }

        public static Label Muted(string text, VisualElement parent = null, bool own = false)
            => Text(text, parent, Tokens.FontSizeSmall, Role.TextMuted, own);

        /// Look.Button: hover, pressed and selected (Paint.Select) are Paint's, not the caller's.
        public static Button Btn(string text, Action onClick, VisualElement parent = null, bool own = false)
        {
            var b = new Button();
            if (onClick != null) b.clicked += () => Safe.Run(Host.For(b), "Button '" + b.text + "'", onClick);
            b.style.fontSize = Tokens.FontSizeSmall;
            SetBorderWidth(b, 1f);   // the focus ring (Look.FocusBorder) paints this; unfocused it stays unset/clear
            SetRadius(b, 4f);
            SetPadding(b, 5f);
            b.style.marginLeft = 0f; b.style.marginRight = Tokens.Gap;
            b.style.marginTop = 0f; b.style.marginBottom = 0f;
            parent?.Add(b);
            Words.Label(b, text, own);
            Paint.Look(b, Look.Button);
            return b;
        }

        /* A virtualized list. This is the reason for moving to UI Toolkit at all: ListView recycles
           rows, so a 50k-line log costs the same as a 50-line one. The uGUI framework had to be
           told how many rows to build. */
        public static ListView List(Func<IList> source, Action<VisualElement, int> bind, VisualElement parent = null)
        {
            var lv = new ListView
            {
                fixedItemHeight = Tokens.RowHeight,
                selectionType = SelectionType.None,
                showBorder = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            lv.makeItem = () =>
            {
                var l = Text("", null, Tokens.FontSizeSmall);
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
