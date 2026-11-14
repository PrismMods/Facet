using System;
using System.Collections.Generic;

namespace Facet
{
    /* The cross-version entry list, BCL only so it can be tested offline. An entry is an object[]:
       [0] Func<float> top visible order or -1, [1] Func<bool> take Escape, [2] Func<bool> open,
       [3] Func<bool> text focused, [4] Func<bool> blocks input, [5] Func<float,float,bool> pointer,
       [6] string key (assembly full name), [7] Func<int,float[]> orders of its surfaces in a layer,
       [8] Action<int,Func<float,float>> remap its surfaces in a layer,
       [9] Action<float,float,float> wheel(x, y, delta) for this version's topmost visible surface,
       [10] Func<bool> cancel this version's drag in progress (true when there was one),
       [11] Func<bool,bool> undo (false) or redo (true) for this version's topmost visible surface: true when a step
            applied, or when a drag in progress swallowed the chord.

       Cross-version contract: every loaded Facet version reads every other version's entry, so
       slots are only ever APPENDED, a reader checks e.Length before reading a slot (an older,
       shorter entry is skipped), and a slot's type never changes. Every call into an entry is
       guarded: a throwing entry counts as false / -1 and is logged once.

       The queries run on a host's per-key input path, so they allocate nothing: the shared list is
       walked by index under its lock, and the guard is an inline try, not a lambda. */
    internal static class Coord
    {
        private static readonly HashSet<string> _logged = new HashSet<string>();

        // A second registration under the same key (a reloaded copy) replaces the first.
        public static void Register(List<object[]> entries, object[] e)
        {
            lock (entries)
            {
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].Length > 6 && (string)entries[i][6] == (string)e[6]) { entries[i] = e; return; }
                entries.Add(e);
            }
        }

        private static void Threw(object[] e, Exception ex, Action<string> log)
        {
            string key = e.Length > 6 ? e[6] as string : null;
            if (log != null && _logged.Add(key + "/" + ex.GetType().Name))
                try { log("Shared entry " + key + " threw: " + ex.Message); } catch { }
        }

        // Slot i of e as T, or null when the entry is too short for it or the slot is empty.
        private static T Slot<T>(object[] e, int i) where T : class => e.Length > i ? e[i] as T : null;

        private static float Top(object[] e, Action<string> log)
        {
            var f = Slot<Func<float>>(e, 0);
            if (f == null) return -1f;
            try { return f(); } catch (Exception ex) { Threw(e, ex, log); return -1f; }
        }

        public static bool Any(List<object[]> entries, int i, Action<string> log)
        {
            lock (entries)
                for (int n = 0; n < entries.Count; n++)
                {
                    var e = entries[n];
                    var f = Slot<Func<bool>>(e, i);
                    if (f == null) continue;
                    try { if (f()) return true; } catch (Exception ex) { Threw(e, ex, log); }
                }
            return false;
        }

        public static bool Pointer(List<object[]> entries, float x, float y, Action<string> log)
        {
            lock (entries)
                for (int n = 0; n < entries.Count; n++)
                {
                    var e = entries[n];
                    var f = Slot<Func<float, float, bool>>(e, 5);
                    if (f == null) continue;
                    try { if (f(x, y)) return true; } catch (Exception ex) { Threw(e, ex, log); }
                }
            return false;
        }

        /* A drag in progress in any version takes it first (its window may not be the topmost); else the entry with the
           highest top order (>= 0) takes it; false when none. */
        public static bool TakeEscape(List<object[]> entries, Action<string> log)
        {
            lock (entries)
                for (int n = 0; n < entries.Count; n++)
                {
                    var e = entries[n];
                    var drag = Slot<Func<bool>>(e, 10);
                    if (drag != null)
                        try { if (drag()) return true; } catch (Exception ex) { Threw(e, ex, log); }
                }
            var best = Topmost(entries, log);
            var take = best == null ? null : Slot<Func<bool>>(best, 1);
            if (take == null) return false;
            try { return take(); } catch (Exception ex) { Threw(best, ex, log); return false; }
        }

        /* The wheel, counted once per frame across mods and versions. A zero delta (a mod whose read
           was gated) leaves the frame unspent; otherwise the first call in a frame stamps it and the
           entry with the highest top takes the wheel. A top entry without slot 9 (Facet 0.1) takes it
           by doing nothing: its own callers drive it. */
        public static bool Wheel(List<object[]> entries, int[] stamp, int frame, float x, float y, float delta, Action<string> log)
        {
            if (Math.Abs(delta) < 1e-6f || stamp[0] == frame) return false;
            stamp[0] = frame;
            var best = Topmost(entries, log);
            var wheel = best == null ? null : Slot<Action<float, float, float>>(best, 9);
            if (wheel == null) return false;
            try { wheel(x, y, delta); return true; } catch (Exception ex) { Threw(best, ex, log); return false; }
        }

        /* The undo chord, answered once per frame across mods and versions. The stamp is {frame, result}: a second call
           in the same frame (another mod polling the same keys) calls no slot and gets the first call's result.
           Otherwise the entry with the highest top runs slot 11; a top entry without it (an older copy) takes the
           chord by doing nothing, its own callers drive it. */
        public static bool Undo(List<object[]> entries, int[] stamp, int frame, bool redo, Action<string> log)
        {
            if (stamp[0] == frame) return stamp[1] != 0;
            stamp[0] = frame; stamp[1] = 0;
            var best = Topmost(entries, log);
            var undo = best == null ? null : Slot<Func<bool, bool>>(best, 11);
            if (undo == null) return false;
            try { if (undo(redo)) stamp[1] = 1; } catch (Exception ex) { Threw(best, ex, log); }
            return stamp[1] != 0;
        }

        // The entry with the highest top order (>= 0), null when none.
        private static object[] Topmost(List<object[]> entries, Action<string> log)
        {
            object[] best = null; float top = -1f;
            lock (entries)
                for (int n = 0; n < entries.Count; n++)
                {
                    var o = Top(entries[n], log);
                    if (o > top) { top = o; best = entries[n]; }
                }
            return best;
        }

        /* Renumber one layer's band across every version, keeping relative stacking order. Returns
           the highest assigned order, which the caller stores as the shared counter. Runs only when a
           band runs out, so it may allocate. */
        public static float Renumber(List<object[]> entries, int layer, float bandBase, Action<string> log)
        {
            var all = new List<float>();
            var map = new Dictionary<float, float>();
            float n = bandBase;
            Func<float, float> remap = o => { float r; return map.TryGetValue(o, out r) ? r : o; };
            lock (entries)
            {
                foreach (var e in entries)
                {
                    var orders = Slot<Func<int, float[]>>(e, 7);
                    if (orders == null) continue;
                    try { all.AddRange(orders(layer)); } catch (Exception ex) { Threw(e, ex, log); }
                }
                all.Sort();
                foreach (var o in all) if (!map.ContainsKey(o)) map[o] = ++n;
                foreach (var e in entries)
                {
                    var apply = Slot<Action<int, Func<float, float>>>(e, 8);
                    if (apply == null) continue;
                    try { apply(layer, remap); } catch (Exception ex) { Threw(e, ex, log); }
                }
            }
            return n;
        }
    }
}
