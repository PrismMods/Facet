using System;

namespace Facet
{
    /* The animation speed and the smooth-scroll switch every loaded Facet version agrees on, so one suite
       setting moves all of their windows. Each is a one-slot array in the shared store (BCL type, value type
       fixed for good); a 0.1 copy keeps its own speed field and has no switch. Cached once per assembly: the
       per-frame path reads an array slot and allocates nothing. A test may hold both (Hold), so its checks
       read its own values while other mods keep writing theirs every frame. */
    internal static class Motion
    {
        public const string Key = "Facet.AnimSpeed", SmoothKey = "Facet.SmoothScroll", HoldKey = "Facet.MotionHold";

        private static string _problem;
        private static readonly float[] Slot = Open(Key, 1f, "float[]");
        private static readonly bool[] SmoothSlot = Open(SmoothKey, false, "bool[]");
        private static readonly float[] HoldSlot = Open(HoldKey, 0f, "float[]", 3);   // {held, speed, smooth}

        // A bad shared value (another version's wrong type or an empty array) must never kill all
        // animation or scrolling: fall back to a private slot and report once (TakeProblem).
        private static T[] Open<T>(string key, T start, string type, int length = 1)
        {
            Func<T[]> make = () => { var n = new T[length]; n[0] = start; return n; };
            string problem;
            try
            {
                var a = Shared.Get<object>(key, make) as T[];
                if (a != null && a.Length >= length) return a;
                problem = key + " in the shared store is not a " + type + " of length >= " + length + "; using a private value";
            }
            catch (Exception e) { problem = key + " could not be read (" + e.Message + "); using a private value"; }
            _problem = _problem == null ? problem : _problem + "; " + problem;
            return make();
        }

        /// The message to log through the fallback host, once; null when the store was fine.
        public static string TakeProblem() { var p = _problem; _problem = null; return p; }

        // While a hold is on, both read the held values; writes still reach the store.
        public static float Speed { get { return HoldSlot[0] != 0f ? HoldSlot[1] : Slot[0]; } set { Slot[0] = value; } }

        /// Whether a wheel step glides; unset reads false, so a host that never sets it keeps the instant step.
        public static bool Smooth { get { return HoldSlot[0] != 0f ? HoldSlot[2] != 0f : SmoothSlot[0]; } set { SmoothSlot[0] = value; } }

        /// For tests: Speed and Smooth read these values until the scope ends, whoever writes meanwhile.
        /// Ending it puts back the hold it replaced, so holds nest.
        internal static IDisposable Hold(float speed, bool smooth)
        {
            var prior = (float[])HoldSlot.Clone();
            HoldSlot[0] = 1f; HoldSlot[1] = speed; HoldSlot[2] = smooth ? 1f : 0f;
            return new Restore(prior);
        }

        private sealed class Restore : IDisposable
        {
            private readonly float[] _prior;
            public Restore(float[] prior) { _prior = prior; }
            public void Dispose() { Array.Copy(_prior, HoldSlot, 3); }
        }

        /// A duration at the current speed. NaN, 0 and negative speeds are instant.
        public static int Scale(int ms)
        {
            float s = Speed;
            return ms > 0 && s > 0 ? (int)Math.Min(ms * (double)s, 60000) : 0;
        }
    }
}
