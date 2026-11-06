namespace Facet
{
    /* Sizes as code, because USS cannot be authored at runtime. Global on purpose: no mod changes
       them at runtime, and Window scale covers "bigger". Colours are per window, in Theme. */
    public static class Tokens
    {
        /// Page and section titles. Bigger than body text, and the one place weight is used.
        public static float FontSizeTitle = 19f;

        /* Sharp, not rounded: the mods' own panels are square-edged and the framework should look
           like them rather than like a third product. 3px takes the hard edge off a border without
           reading as a pill. */
        public static float Radius      = 3f;
        public static float Pad         = 12f;
        public static float Gap         = 8f;
        // Sized up across the board: the first build was legible on a 2560-wide screen and no more.
        public static float FontSize    = 15f;
        public static float FontSizeSmall = 13f;
        public static float RowHeight   = 24f;
    }
}
