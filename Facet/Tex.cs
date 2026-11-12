using UnityEngine;

namespace Facet
{
    /* Generated textures.

       A mod has no image assets at runtime, and UI Toolkit will not compile a USS gradient either,
       so anything that is not a flat rectangle has to be built in code. These are the handful a
       colour picker needs, and the rail button's chevrons. Each is made once and kept: they are tiny, they never change, and a
       picker that rebuilt them per open would allocate a texture every time.

       Built with hideFlags DontSave so a scene change does not destroy them under a live panel. */
    public static class Tex
    {
        private static Texture2D _hue, _sat, _val, _checker;

        /// Vertical hue ramp, red at the bottom round to red at the top.
        public static Texture2D Hue()
        {
            if (_hue != null) return _hue;
            _hue = Make(1, 256, (x, y) => Color.HSVToRGB(y / 255f, 1f, 1f));
            return _hue;
        }

        /// White to transparent, left to right — saturation over a hue-coloured background.
        public static Texture2D Sat()
        {
            if (_sat != null) return _sat;
            _sat = Make(256, 1, (x, y) => new Color(1f, 1f, 1f, 1f - x / 255f));
            return _sat;
        }

        /// Transparent to black, top to bottom — value, laid over the saturation ramp.
        public static Texture2D Val()
        {
            if (_val != null) return _val;
            _val = Make(1, 256, (x, y) => new Color(0f, 0f, 0f, 1f - y / 255f));
            return _val;
        }

        /// Grey checkerboard, so a low alpha reads as transparent rather than as dark.
        public static Texture2D Checker(int cell = 6)
        {
            if (_checker != null) return _checker;
            int size = cell * 2;
            _checker = Make(size, size, (x, y) =>
                ((x / cell) + (y / cell)) % 2 == 0 ? new Color(0.30f, 0.30f, 0.32f, 1f)
                                                  : new Color(0.22f, 0.22f, 0.24f, 1f));
            _checker.wrapMode = TextureWrapMode.Repeat;
            return _checker;
        }

        private static Texture2D _chevronsLeft, _chevronsRight;

        /// Two chevrons, white on clear for a tint: », or « when `left`. Their ink spans nearly the whole
        /// square, so drawn at Sizes.Icon they read as large as a rail icon (a » in text drew a third of that).
        internal static Texture2D Chevrons(bool left)
        {
            var t = left ? _chevronsLeft : _chevronsRight;
            if (t != null) return t;
            const int n = 64;
            const float half = 3.5f, top = 5f, tip = 26f, step = 18f, x0 = (n - tip - step) / 2f;
            t = Make(n, n, (x, y) =>
            {
                var p = new Vector2((left ? n - 1 - x : x) + 0.5f, y + 0.5f);
                float d = float.MaxValue;
                for (int i = 0; i < 2; i++)
                {
                    float x1 = x0 + i * step;
                    var apex = new Vector2(x1 + tip, n / 2f);
                    d = Mathf.Min(d, Mathf.Min(ToSegment(p, new Vector2(x1, top), apex), ToSegment(p, apex, new Vector2(x1, n - top))));
                }
                return new Color(1f, 1f, 1f, Mathf.Clamp01(half + 0.5f - d));   // a stroke `half` either side, one pixel of edge
            });
            if (left) _chevronsLeft = t; else _chevronsRight = t;
            return t;
        }

        private static float ToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            return Vector2.Distance(p, a + Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) * ab);
        }

        private static Texture2D Make(int w, int h, System.Func<int, int, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }
    }
}
