using System;
using System.Globalization;

namespace Facet
{
    /// How a slider maps a value onto its track: t is 0 at Min and 1 at Max.
    public interface IValueScale
    {
        float ToT(float v, float min, float max);
        float FromT(float t, float min, float max);
    }

    public static class ValueScale
    {
        public static readonly IValueScale Linear = new LinearScale();

        /// Equal steps along the track multiply the value; needs Min > 0 (otherwise it behaves as Linear).
        public static readonly IValueScale Log = new LogScale();

        /// The nearest multiple of step counted from min; a step of 0 or less leaves v alone.
        public static float Snap(float v, float step, float min)
        {
            if (step <= 0f) return v;
            return min + (float)Math.Round((v - min) / step, MidpointRounding.AwayFromZero) * step;
        }

        /* A typed number box's text: a whole number, or the number before a unit a Format put after it ("500 BPM",
           "12.5 %"), so a reader who edits only the digits is not reverted. NaN and infinity are no number. */
        internal static bool TryParse(string s, IFormatProvider culture, out float v)
        {
            v = 0f;
            if (s == null) return false;
            const NumberStyles any = NumberStyles.Float;
            if (!float.TryParse(s, any, culture, out v))
            {
                int end = s.Length;
                while (end > 0 && !char.IsDigit(s[end - 1])) end--;
                if (end == 0 || !float.TryParse(s.Substring(0, end), any, culture, out v)) return false;
            }
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        /* A scrubbed number's value per 4 px of drag: Step, else 1/200 of the range, else 1. A range wider than a
           float holds (Min and Max near the float limits) gives 1 too, or the first pixel jumps to a bound. */
        internal static float ScrubUnit(float step, float min, float max)
        {
            float span = max - min;
            return step > 0f ? step : span > 0f && !float.IsInfinity(span) ? span / 200f : 1f;
        }

        private static float Clamp01(float t) { return t < 0f ? 0f : t > 1f ? 1f : t; }

        private sealed class LinearScale : IValueScale
        {
            public float ToT(float v, float min, float max) { return max > min ? Clamp01((v - min) / (max - min)) : 0f; }

            // Exact at the ends: min + 1 * (max - min) is not always max in float.
            public float FromT(float t, float min, float max) { return t <= 0f ? min : t >= 1f ? max : min + t * (max - min); }
        }

        private sealed class LogScale : IValueScale
        {
            public float ToT(float v, float min, float max)
            {
                if (min <= 0f) return Linear.ToT(v, min, max);
                if (!(max > min)) return 0f;
                return Clamp01((float)(Math.Log(Math.Max(v, min) / min) / Math.Log(max / min)));
            }

            public float FromT(float t, float min, float max)
            {
                if (min <= 0f) return Linear.FromT(t, min, max);
                return t <= 0f ? min : t >= 1f ? max : (float)(min * Math.Pow(max / min, t));
            }
        }
    }
}
