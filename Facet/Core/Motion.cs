using System;

namespace Facet
{
    /* The animation speed every loaded Facet version agrees on, so one suite setting moves all of
       their windows. A float[1] in the shared store (BCL type, value type fixed for good); a 0.1
       copy keeps its own field and stays at speed 1. Cached once per assembly: the per-frame path
       reads an array slot and allocates nothing. */
    internal static class Motion
    {
        public const string Key = "Facet.AnimSpeed";

        private static string _problem;
        private static readonly float[] Slot = Open();

        // A bad shared value (another version's wrong type or an empty array) must never kill all
        // animation: fall back to a private slot and report once (TakeProblem).
        private static float[] Open()
        {
            try
            {
                var a = Shared.Get<object>(Key, () => new[] { 1f }) as float[];
                if (a != null && a.Length >= 1) return a;
                _problem = "Facet.AnimSpeed in the shared store is not a float[] of length >= 1; using a private speed";
            }
            catch (Exception e) { _problem = "Facet.AnimSpeed could not be read (" + e.Message + "); using a private speed"; }
            return new[] { 1f };
        }

        /// The message to log through the fallback host, once; null when the store was fine.
        public static string TakeProblem() { var p = _problem; _problem = null; return p; }

        public static float Speed { get { return Slot[0]; } set { Slot[0] = value; } }

        /// A duration at the current speed. NaN, 0 and negative speeds are instant.
        public static int Scale(int ms)
        {
            float s = Speed;
            return ms > 0 && s > 0 ? (int)Math.Min(ms * (double)s, 60000) : 0;
        }
    }
}
