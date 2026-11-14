using System;

namespace Facet
{
    /// The two ends of a dual-range slider (Controls.Range's model), BCL only. The low end never passes the high one.
    internal static class RangeMath
    {
        /// v clamped to [min, max]; a step > 0 first rounds it to min + k·step.
        internal static float Snap(float v, float min, float max, float step)
        {
            v = ValueScale.Snap(v, step, min);
            return Math.Max(min, Math.Min(max, v));
        }

        /// One end (high or low) to Snap(v); passing the other end pushes it to the same value.
        internal static void Move(ref float lo, ref float hi, bool high, float v, float min, float max, float step)
        {
            v = Snap(v, min, max, step);
            if (high) { hi = v; if (lo > v) lo = v; }
            else { lo = v; if (hi < v) hi = v; }
        }

        /// The ends as shown: a stored x > y shows swapped.
        internal static void Shown(float x, float y, out float lo, out float hi)
        {
            lo = Math.Min(x, y);
            hi = Math.Max(x, y);
        }
    }
}
