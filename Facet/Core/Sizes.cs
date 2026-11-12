namespace Facet
{
    /* The sizes a theme's parts build with, in panel pixels; every default is Facet 0.1's. A part reads
       Theme.For(e).Sizes when it builds, so a change shows on the next build, not on what is open. */
    public sealed class Sizes
    {
        /// Corners; the switch track; the switch knob and slider thumb.
        public float Radius = 3f, SwitchRadius = 9f, KnobRadius = 7f;
        /// Borders and rules.
        public float Line = 1f;
        public float Pad = 12f, Gap = 8f;
        /// Inner padding: buttons, tabs and dropdowns; text fields and number boxes.
        public float ControlPad = 5f, FieldPad = 4f;
        /// Setting rows; list and menu rows; rail entries; the title bar.
        public float RowHeight = 32f, ListRowHeight = 24f, NavRowHeight = 30f, TitleBarHeight = 40f;
        /// A list entry with actions (Controls.Item).
        public float ItemHeight = 38f;
        /// Type: titles, body, small print, upper-case headers and their letter spacing.
        public float Title = 19f, Body = 15f, Small = 13f, Caps = 11f, CapsSpacing = 1.5f;
        public float SwitchWidth = 36f, SwitchHeight = 18f;
        /// The slider's track thickness and its thumb.
        public float Track = 4f, ThumbWidth = 12f, ThumbHeight = 12f;
        /// Control widths (DropdownWidth is the dropdown's minimum).
        public float SliderWidth = 180f, NumberWidth = 62f, DropdownWidth = 120f;
        /// A selection bar's thickness (a tab's underline, a rail entry's edge).
        public float Indicator = 2f;
        /// A rail entry's icon, square.
        public float Icon = 16f;
        /// Space above and below a row's text, so lines that wrap stand off the row's edges (0: none).
        public float RowPadY = 0f;
        /// Space under a tab row, before its page.
        public float TabGap = 8f;

        public Sizes Copy() { return (Sizes)MemberwiseClone(); }
    }
}
