using System;
using System.Collections.Generic;

namespace Facet
{
    /* The cross-version entry list, BCL only so it can be tested offline. An entry is an object[]:
       [0] Func<float> top visible order or -1, [1] Func<bool> take Escape, [2] Func<bool> open,
       [3] Func<bool> text focused, [4] Func<bool> blocks input, [5] Func<float,float,bool> pointer,
       [6] string key (assembly full name), [7] Func<int,float[]> orders of its surfaces in a layer,
       [8] Action<int,Func<float,float>> remap its surfaces in a layer.
       Every call into an entry is guarded: a throwing entry counts as false / -1 and is logged once. */
    internal static class Coord
    {
        private static readonly HashSet<string> _logged = new HashSet<string>();

        // A second registration under the same key (a reloaded copy) replaces the first.
        public static void Register(List<object[]> entries, object[] e)
        {
            lock (entries)
            {
                for (int i = 0; i < entries.Count; i++)
                    if ((string)entries[i][6] == (string)e[6]) { entries[i] = e; return; }
                entries.Add(e);
            }
        }

        public static object[][] Snapshot(List<object[]> entries) { lock (entries) return entries.ToArray(); }

        private static T Call<T>(object[] e, T dflt, Action<string> log, Func<T> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                string k = e[6] + "/" + ex.GetType().Name;
                if (log != null && _logged.Add(k)) try { log("Shared entry " + e[6] + " threw: " + ex.Message); } catch { }
                return dflt;
            }
        }

        public static bool Flag(object[] e, int i, Action<string> log) =>
            Call(e, false, log, () => ((Func<bool>)e[i])());

        public static float Top(object[] e, Action<string> log) =>
            Call(e, -1f, log, () => ((Func<float>)e[0])());

        public static bool Pointer(object[] e, float x, float y, Action<string> log) =>
            Call(e, false, log, () => ((Func<float, float, bool>)e[5])(x, y));

        public static bool Any(List<object[]> entries, int i, Action<string> log)
        {
            foreach (var e in Snapshot(entries)) if (Flag(e, i, log)) return true;
            return false;
        }

        // The entry with the highest top order (>= 0) takes it; null/false when none.
        public static bool TakeEscape(List<object[]> entries, Action<string> log)
        {
            object[] best = null; float top = -1f;
            foreach (var e in Snapshot(entries))
            {
                var o = Top(e, log);
                if (o > top) { top = o; best = e; }
            }
            return best != null && Flag(best, 1, log);
        }

        /* Renumber one layer's band across every version, keeping relative stacking order. Returns
           the highest assigned order, which the caller stores as the shared counter. */
        public static float Renumber(List<object[]> entries, int layer, float bandBase, Action<string> log)
        {
            var all = new List<float>();
            var snap = Snapshot(entries);
            foreach (var e in snap)
                Call<object>(e, null, log, () => { all.AddRange(((Func<int, float[]>)e[7])(layer)); return null; });
            all.Sort();
            var map = new Dictionary<float, float>();
            float n = bandBase;
            foreach (var o in all) if (!map.ContainsKey(o)) map[o] = ++n;
            Func<float, float> f = o => { float r; return map.TryGetValue(o, out r) ? r : o; };
            foreach (var e in snap)
                Call<object>(e, null, log, () => { ((Action<int, Func<float, float>>)e[8])(layer, f); return null; });
            return n;
        }
    }
}
