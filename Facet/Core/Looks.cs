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
        /// The border while selected (Paint.Select); focus still wins over it.
        public Role? SelectedBorder;

        public Look Copy() { return (Look)MemberwiseClone(); }

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
        public static Look Field => new Look { Bg = Role.Row, Text = Role.Text, Border = Role.PanelBorder, HoverBg = Role.RowHover, FocusBorder = Role.Focus };
        /// Only the focus ring: for a control that is not a button (a switch, a slider).
        public static Look Ring => new Look { FocusBorder = Role.Focus };
        public static Look Card => new Look { Bg = Role.Panel, Border = Role.PanelBorder };
        /// Title bar and footer.
        public static Look Chrome => new Look { Bg = Role.TitleBar };
    }

    /* One Look per part element, owned by a theme (Theme.Looks). A part reads Theme.For(e).Looks when
       it builds, so swapping a Look object shows on the next build; colours still re-apply live.
       Every default is Facet 0.1's look. */
    public sealed class Looks
    {
        public Look Button = Look.Button, FlatButton = Look.FlatButton, AccentButton = Look.AccentButton,
            DangerButton = Look.DangerButton, Row = Look.Row, RailItem = Look.RailItem, Field = Look.Field,
            Ring = Look.Ring, Chrome = Look.Chrome;
        /// The window shell and popups.
        public Look Card = Look.Card;
        /// A card of rows on a page (Controls.Card), and a preview's stage.
        public Look Group = Look.Card, Stage = Look.Card;
        /// A switch's track (on is Paint.Select) and its knob.
        public Look Switch = new Look { Bg = Role.Off, SelectedBg = Role.Accent, FocusBorder = Role.Focus },
            Knob = new Look { Bg = Role.Text };
        /// A slider's track, the filled part of it (none by default) and its thumb.
        public Look Track = new Look { Bg = Role.Off }, Fill = new Look(), Thumb = new Look { Bg = Role.Accent };
        /// A tab (a button by default) and a selection bar: a tab's underline, a rail entry's edge (none by default).
        public Look Tab = Look.Button, Indicator = new Look();
        public Look Tile = new Look { Bg = Role.Row, Border = Role.Line, Text = Role.TextMuted, HoverBg = Role.RowHover,
            SelectedText = Role.Text, FocusBorder = Role.Focus };
        public Look Link = new Look { Text = Role.Accent, HoverText = Role.Text, FocusBorder = Role.Focus };
        /// A hairline rule, and an upper-case header (Ui.Caps).
        public Look Rule = new Look { Bg = Role.Line }, Caps = new Look { Text = Role.TextMuted };

        /// A deep copy: every Look is copied, so the two never share one.
        public Looks Copy()
        {
            var c = (Looks)MemberwiseClone();
            c.Button = Button?.Copy(); c.FlatButton = FlatButton?.Copy(); c.AccentButton = AccentButton?.Copy();
            c.DangerButton = DangerButton?.Copy(); c.Row = Row?.Copy(); c.RailItem = RailItem?.Copy();
            c.Field = Field?.Copy(); c.Ring = Ring?.Copy(); c.Chrome = Chrome?.Copy(); c.Card = Card?.Copy();
            c.Group = Group?.Copy(); c.Stage = Stage?.Copy(); c.Switch = Switch?.Copy(); c.Knob = Knob?.Copy();
            c.Track = Track?.Copy(); c.Fill = Fill?.Copy(); c.Thumb = Thumb?.Copy(); c.Tab = Tab?.Copy();
            c.Indicator = Indicator?.Copy(); c.Tile = Tile?.Copy(); c.Link = Link?.Copy(); c.Rule = Rule?.Copy();
            c.Caps = Caps?.Copy();
            return c;
        }
    }
}
