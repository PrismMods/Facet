using UnityEngine.UIElements;

namespace Facet
{
    /* A per-state recipe of roles: resting, hover, pressed, selected, disabled and focused. A
       runtime panel has no theme style sheet, so :hover and :disabled do nothing, and every widget
       used to hand-roll its own enter/leave colour lambdas; Paint tracks the states instead and a
       Look says what each draws. A null role leaves that channel unset: no background, inherited
       text, no border colour.

       Each preset is a fresh copy, so a mod that restyles the one it was handed (`var l =
       Look.Button; l.Bg = Role.Accent;`) restyles only its own elements. */
    public sealed class Look
    {
        public Role? Bg, Text, Border, HoverBg, PressedBg, SelectedBg, SelectedText, FocusBorder, HoverText;
        public float DisabledOpacity = 0.35f;

        public static Look Button => new Look { Bg = Role.RowAlt, Text = Role.Text, HoverBg = Role.RowHover,
            PressedBg = Role.Row, SelectedBg = Role.Accent, SelectedText = Role.AccentText, FocusBorder = Role.Focus };
        /// Part of the frame rather than a button sitting on it: no background, hover included.
        public static Look FlatButton => new Look { Text = Role.Text, FocusBorder = Role.Focus };
        public static Look AccentButton => new Look { Bg = Role.Accent, Text = Role.AccentText, FocusBorder = Role.Focus };
        /// Selected is armed: the click that asks "Sure?".
        public static Look DangerButton => new Look { Bg = Role.RowAlt, Text = Role.Danger, HoverBg = Role.RowHover,
            PressedBg = Role.Row, SelectedBg = Role.Danger, SelectedText = Role.AccentText, FocusBorder = Role.Focus };
        public static Look Row => new Look { HoverBg = Role.Row };
        /// Its labels paint no role of their own (Paint.Inherit), so muted, hover and selected text reach them.
        public static Look RailItem => new Look { Text = Role.TextMuted, HoverBg = Role.Row, HoverText = Role.Text,
            SelectedBg = Role.RowHover, SelectedText = Role.Text };
        public static Look Field => new Look { Bg = Role.Row, Text = Role.Text, Border = Role.PanelBorder, FocusBorder = Role.Focus };
        /// Only the focus ring: for a control that is not a button (a switch, a slider).
        public static Look Ring => new Look { FocusBorder = Role.Focus };
        public static Look Card => new Look { Bg = Role.Panel, Border = Role.PanelBorder };
        /// Title bar and footer.
        public static Look Chrome => new Look { Bg = Role.TitleBar };
    }

    /* Colours by role. Facet remembers which role each element's background, text and border use,
       and its look and states, so Apply can redraw a window that is already open in a new scheme.

       Precedence: background is selected, then pressed, then hover, then the element's own role,
       then the look's; text is selected, then hover, then own, then look; border is focus, then own, then look.
       Disabled ignores hover and pressed and fades to the look's DisabledOpacity. */
    public static class Paint
    {
        private sealed class State
        {
            public Role? Bg, Fg, Border, Tint;
            public Look Look;
            public bool Hover, Pressed, Focus, Selected, Disabled, Hooked, Waiting;
        }

        private static readonly Look None = new Look();

        public static void Bg(VisualElement e, Role r) { if (e != null) { var s = Data.Get<State>(e); s.Bg = r; Draw(e, s); } }

        public static void Fg(VisualElement e, Role r) { if (e != null) { var s = Data.Get<State>(e); s.Fg = r; Draw(e, s); } }

        public static void Border(VisualElement e, Role r) { if (e != null) { var s = Data.Get<State>(e); s.Border = r; Draw(e, s); } }

        public static void Look(VisualElement e, Look look)
        {
            if (e == null) return;
            var s = Data.Get<State>(e);
            s.Look = look;
            Hook(e, s);
            Draw(e, s);
        }

        /// The tint of the element's background image (a text field's caret).
        internal static void Tint(VisualElement e, Role r) { if (e != null) { var s = Data.Get<State>(e); s.Tint = r; Draw(e, s); } }

        /// Drop the element's own text role so it takes its parent's colour: a label inside a row
        /// whose look carries the hover and selected text.
        internal static void Inherit(VisualElement e)
        {
            var s = Data.Find<State>(e);
            if (s == null || s.Fg == null) return;
            s.Fg = null;
            e.style.color = StyleKeyword.Null;
        }

        /// Hover set from outside: a hairline that lights when its wider hit area is hovered.
        internal static void Hover(VisualElement e, bool on)
        {
            if (e == null) return;
            var s = Data.Get<State>(e);
            s.Hover = on;
            Draw(e, s);
        }

        /// Light `e` in role `r`, fading out over `ms`, then back to what it was. Reads the scheme
        /// every frame, so a re-theme mid-flash does not leave the old accent behind.
        internal static void Flash(VisualElement e, Role r, int ms)
        {
            if (e == null) return;
            var s = Data.Get<State>(e);
            System.Action rest = () => { e.style.backgroundColor = StyleKeyword.Null; Draw(e, s, Theme.For(e)); };
            ms = (int)(ms * Anim.Speed);
            if (ms <= 0) { rest(); return; }   // instant motion: straight to the resting paint
            var a = e.experimental.animation.Start(1f, 0f, ms, (el, t) =>
            {
                var c = Theme.For(el)[r];
                c.a = t * 0.5f;
                el.style.backgroundColor = c;
            });
            a.onAnimationCompleted = rest;
        }

        /// The selected tab, segment or rail leaf. Survives the pointer leaving it.
        public static void Select(VisualElement e, bool on)
        {
            if (e == null) return;
            var s = Data.Get<State>(e);
            s.Selected = on;
            Draw(e, s);
        }

        /// Disabled elements fade rather than vanish, so a toolbar does not reflow when one goes
        /// unavailable. SetEnabled alone leaves them looking live.
        public static void Enable(VisualElement e, bool on)
        {
            if (e == null) return;
            var s = Data.Get<State>(e);
            e.SetEnabled(on);
            s.Disabled = !on;
            if (!on) s.Hover = s.Pressed = false;   // it may never hear the leave
            e.style.opacity = on ? new StyleFloat(StyleKeyword.Null) : (s.Look ?? None).DisabledOpacity;
            Draw(e, s);
        }

        /// Redraw every recorded role and look under `root` in the scheme each element finds now.
        public static void Apply(VisualElement root)
        {
            if (root != null) Walk(root, Theme.For(root));
        }

        private static void Walk(VisualElement e, Theme t)
        {
            t = Theme.Own(e) ?? t;
            var s = Data.Find<State>(e);
            if (s != null) Draw(e, s, t);
            var h = e.hierarchy;
            for (int i = 0; i < h.childCount; i++) Walk(h[i], t);
        }

        private static void Draw(VisualElement e, State s)
        {
            Draw(e, s, Theme.For(e));
            // Painted before joining the tree: drawn in the build's scheme for now, and again in
            // its window's once attached (a list row, a toolbar button built before it is added).
            if (e.panel == null && !s.Waiting) { s.Waiting = true; Events.On(e, Attached); }
        }

        private static void Draw(VisualElement e, State s, Theme t)
        {
            var k = s.Look ?? None;
            bool live = !s.Disabled;
            if (s.Look != null || s.Bg != null)
                e.style.backgroundColor = C(t, (s.Selected ? k.SelectedBg : null) ?? (live && s.Pressed ? k.PressedBg : null)
                                               ?? (live && s.Hover ? k.HoverBg : null) ?? s.Bg ?? k.Bg);
            if (s.Look != null || s.Fg != null)
                e.style.color = C(t, (s.Selected ? k.SelectedText : null) ?? (live && s.Hover ? k.HoverText : null) ?? s.Fg ?? k.Text);
            if (s.Tint != null) e.style.unityBackgroundImageTintColor = C(t, s.Tint);
            if (s.Look != null || s.Border != null)
            {
                var c = C(t, (s.Focus ? k.FocusBorder : null) ?? s.Border ?? k.Border);
                e.style.borderLeftColor = c; e.style.borderRightColor = c;
                e.style.borderTopColor = c; e.style.borderBottomColor = c;
            }
        }

        private static StyleColor C(Theme t, Role? r)
        {
            return r.HasValue ? new StyleColor(t[r.Value]) : new StyleColor(StyleKeyword.Null);
        }

        // Once per element, and only for a look that has a state to show.
        private static void Hook(VisualElement e, State s)
        {
            var k = s.Look;
            if (s.Hooked || k == null || (k.HoverBg == null && k.PressedBg == null && k.FocusBorder == null)) return;
            s.Hooked = true;
            Events.On(e, OnEnter);
            Events.On(e, OnLeave);
            // TrickleDown: a Button's clickable stops the pointer-down before a bubbling callback hears it.
            Events.On(e, OnDown, TrickleDown.TrickleDown);
            Events.On(e, OnUp, TrickleDown.TrickleDown);
            Events.On(e, OnFocusIn);
            Events.On(e, OnFocusOut);
        }

        private enum Flag { Hover, Pressed, Focus }

        private static void Mark(IEventHandler h, Flag f, bool on)
        {
            var e = h as VisualElement;
            var s = Data.Find<State>(e);
            if (s == null) return;
            if (f == Flag.Hover) { s.Hover = on; if (!on) s.Pressed = false; }
            else if (f == Flag.Pressed) s.Pressed = on;
            else s.Focus = on;
            Draw(e, s, Theme.For(e));
        }

        // Static, so hooking an element allocates nothing; the element is the event's currentTarget.
        private static readonly EventCallback<MouseEnterEvent> OnEnter = ev => Mark(ev.currentTarget, Flag.Hover, true);
        private static readonly EventCallback<MouseLeaveEvent> OnLeave = ev => Mark(ev.currentTarget, Flag.Hover, false);
        private static readonly EventCallback<PointerDownEvent> OnDown = ev => { if (ev.button == 0) Mark(ev.currentTarget, Flag.Pressed, true); };
        private static readonly EventCallback<PointerUpEvent> OnUp = ev => { if (ev.button == 0) Mark(ev.currentTarget, Flag.Pressed, false); };
        private static readonly EventCallback<FocusInEvent> OnFocusIn = ev => Mark(ev.currentTarget, Flag.Focus, true);
        private static readonly EventCallback<FocusOutEvent> OnFocusOut = ev => Mark(ev.currentTarget, Flag.Focus, false);

        private static readonly EventCallback<AttachToPanelEvent> Attached = ev =>
        {
            var e = ev.currentTarget as VisualElement;
            Events.Off(e, Attached);
            var s = Data.Find<State>(e);
            if (s == null) return;
            s.Waiting = false;
            Draw(e, s, Theme.For(e));
        };
    }
}
