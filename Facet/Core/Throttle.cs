using System.Collections.Generic;

namespace Facet {
    // Keeps a failing callback from logging every frame: one line per key per window.
    public static class Throttle {
        static readonly Dictionary<string, double> _last = new Dictionary<string, double>();

        public static bool Allow(string key, double nowSeconds, double windowSeconds = 10) {
            lock (_last) {
                if (_last.TryGetValue(key, out var t) && nowSeconds - t < windowSeconds) return false;
                _last[key] = nowSeconds;
                return true;
            }
        }

        public static void Reset() { lock (_last) _last.Clear(); }
    }
}
