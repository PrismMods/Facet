using System;
using System.Diagnostics;

namespace Facet {
    // The one guard for every call into consumer code. A throw is logged once per owner and site
    // per 10 s (Throttle), never rethrown, and never reaches the caller.
    public static partial class Safe {
        static readonly Stopwatch _clock = Stopwatch.StartNew();

        public static bool Run(string owner, string site, Action fn, Action<string> log) {
            try { fn(); return true; }
            catch (Exception e) { Report(owner, site, e, log); return false; }
        }

        public static T Get<T>(string owner, string site, Func<T> fn, T fallback, Action<string> log) {
            try { return fn(); }
            catch (Exception e) { Report(owner, site, e, log); return fallback; }
        }

        // A rate-limited line that is not a throw, on the same window per owner and site.
        internal static void Say(string owner, string site, string message, Action<string> log) {
            if (log == null || !Throttle.Allow(owner + "/" + site, _clock.Elapsed.TotalSeconds)) return;
            try { log("[" + owner + "] " + site + ": " + message); } catch { }
        }

        static void Report(string owner, string site, Exception e, Action<string> log) {
            if (log == null || !Throttle.Allow(owner + "/" + site, _clock.Elapsed.TotalSeconds)) return;
            var first = (e.StackTrace ?? "").Split('\n')[0].Trim();
            try { log("[" + owner + "] " + site + ": " + e.Message + " @ " + first); } catch { }
        }
    }
}
