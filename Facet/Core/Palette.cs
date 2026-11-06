using System;

namespace Facet {
    /// What a theme colour is for. Append only: a mod built against an earlier Facet indexes by number.
    /// Rail is the sidebar's background, Line the hairline rules and separators, Off a switch's off track.
    public enum Role { Panel, PanelBorder, TitleBar, Row, RowAlt, RowHover, Text, TextMuted, Accent, Danger, AccentText, Focus, Rail, Line, Off }

    /* A whole scheme from one colour, over plain floats so the offline test can pin it. Quartz's
       derivation (its UIColors.ApplyAccent): surfaces are near-black greys leaning toward the accent,
       controls a lighter grey leaning further, muted text the accent washed out. `tint` scales the
       lean: 0 is plain grey, 1 is Quartz's warm look. */
    static class Palette {
        internal static readonly int Count = Enum.GetValues(typeof(Role)).Length;

        /// RGBA per role, indexed by (int)Role.
        internal static float[][] FromAccent(float r, float g, float b, float tint) {
            var a = new[] { C01(r), C01(g), C01(b), 1f };
            var c = new float[Count][];
            c[(int)Role.Accent] = a;
            c[(int)Role.Focus] = a;
            var panel = Lean(.037f, a, .059f * tint);
            panel[3] = .98f;
            c[(int)Role.Panel] = panel;
            var title = c[(int)Role.TitleBar] = Lean(.061f, a, .102f * tint);
            var control = c[(int)Role.RowAlt] = Lean(.173f, a, .146f * tint);
            c[(int)Role.RowHover] = Lerp(control, new[] { 1f, 1f, 1f, 1f }, .08f);
            c[(int)Role.Row] = Lerp(title, control, .5f);
            c[(int)Role.PanelBorder] = new[] { 1f, 1f, 1f, .09f };
            c[(int)Role.Text] = Lean(.92f, a, .04f * tint);
            c[(int)Role.TextMuted] = Lean(.745f, a, .17f * tint);
            c[(int)Role.AccentText] = new[] { .06f, .06f, .08f, 1f };
            c[(int)Role.Rail] = new[] { 0f, 0f, 0f, 0f };
            c[(int)Role.Line] = c[(int)Role.PanelBorder];
            c[(int)Role.Off] = control;
            // A red accent cannot also mean "destructive", so near red the warning turns orange.
            float h, s;
            Hs(a, out h, out s);
            c[(int)Role.Danger] = s > .25f && (h < .06f || h > .94f)
                ? new[] { 1f, .478f, .271f, 1f } : new[] { .886f, .404f, .427f, 1f };
            return c;
        }

        static float C01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        // Unity's Color.Lerp: t clamped, all four channels.
        static float[] Lerp(float[] x, float[] y, float t) {
            t = C01(t);
            return new[] { x[0] + (y[0] - x[0]) * t, x[1] + (y[1] - x[1]) * t, x[2] + (y[2] - x[2]) * t, x[3] + (y[3] - x[3]) * t };
        }

        static float[] Lean(float grey, float[] a, float t) { return Lerp(new[] { grey, grey, grey, 1f }, a, t); }

        // Hue and saturation as Unity's Color.RGBToHSV computes them.
        static void Hs(float[] c, out float h, out float s) {
            float r = c[0], g = c[1], b = c[2], v, lo, off, one, two;
            if (b > g && b > r) { v = b; one = r; two = g; off = 4f; }
            else if (g > r) { v = g; one = b; two = r; off = 2f; }
            else { v = r; one = g; two = b; off = 0f; }
            h = s = 0f;
            if (v == 0f) return;
            lo = one > two ? two : one;
            float d = v - lo;
            s = d == 0f ? 0f : d / v;
            h = (off + (d == 0f ? one - two : (one - two) / d)) / 6f;
            if (h < 0f) h += 1f;
        }
    }
}
