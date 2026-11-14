using System;
using System.Collections.Generic;

namespace Facet
{
    /* Where a control's value lives: a getter and a setter into the consumer's own settings.

       To reads live, so whatever the getter reaches is what the row shows at the next refresh — a
       settings object replaced wholesale (a profile switch) included, as long as the getter goes
       through the reference rather than capturing the old object. Value keeps the value itself, for
       code that has a snapshot and a change handler rather than a field to point at. Many binds one
       row to every item of a selection.

       Both run consumer code; Control calls them through Safe, so a throwing getter is logged and
       counted, never thrown at the panel. */
    public sealed class Bind<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;
        private readonly Many<T> _many;   // a Bind.Many's items; null for every other bind

        // A plain bind's newest recorded step, so a drag frame moves its end instead of recording again.
        private T[] _end;
        private Action _endRedo;

        internal Bind(Func<T> get, Action<T> set, Many<T> many = null) { _get = get; _set = set; _many = many; }

        public T Get() { return _get(); }

        /// A bind with no setter (a read-only row) ignores writes.
        public void Set(T v) { if (_set != null) _set(v); }

        /// A Bind.Many whose items do not all hold the first item's value. False for every other bind.
        public bool Mixed => _many != null && _many.Mixed();

        internal bool CanMix => _many != null;

        /// A Many bind reads and writes the items selected now until unpinned; other binds ignore it.
        internal void Pin(bool on) { if (_many != null) _many.Pin(on); }

        /// The same reads and Mixed, another setter: how a row records its writes.
        internal Bind<T> Wrap(Action<T> set) { return new Bind<T>(_get, set, _many); }

        /* Write v as one undo step under label and key (inside a History.Step bound to valid when valid
           is set). A plain bind records the value it had; a Many bind records each item's own (see Many).
           applied runs after every undo and redo, with History.Applying true: the step has already
           written the bind, so it only has to show the value. A drag frame on a plain bind allocates
           nothing: when History says the write only moves the end of the step this bind recorded last,
           the end moves instead of a new record (the closures are made in Record, never here, since a
           lambda capturing a parameter makes its closure at the top of the method). */
        internal void SetRecorded(T v, string label, string key, Func<string, string> translate, Action<T> applied = null, Func<bool> valid = null)
        {
            if (_many != null) { _many.SetRecorded(v, label, key, translate, applied, valid); return; }
            T prev;
            try { prev = _get(); }
            catch { Set(v); throw; }   // the write still lands; the caller's Safe logs the read
            if (!EqualityComparer<T>.Default.Equals(prev, v))
            {
                if (History.Extend(key, valid, _endRedo)) _end[0] = v;
                else Record(prev, v, label, key, translate, applied, valid);
            }
            Set(v);
        }

        private void Record(T was, T now, string label, string key, Func<string, string> translate, Action<T> applied, Func<bool> valid)
        {
            var to = _end = new[] { now };
            Action undo = () => { Set(was); if (applied != null) applied(was); };
            Action redo = _endRedo = () => { Set(to[0]); if (applied != null) applied(to[0]); };
            if (valid == null) History.Record(label, key, undo, redo, translate);
            else using (History.Step(label, key, valid, translate)) History.Record(label, key, undo, redo, translate);
        }
    }

    // A Many bind's items, with the item type out of Bind<T>'s signature.
    internal abstract class Many<T>
    {
        internal abstract T Get();
        internal abstract void Set(T v);
        internal abstract bool Mixed();
        internal abstract void Pin(bool on);
        internal abstract void SetRecorded(T v, string label, string key, Func<string, string> translate, Action<T> applied, Func<bool> valid);
    }

    /* One row over every item of a selection. Get is the first item's value; Set writes them all.

       A recorded write captures the items it wrote (or, with key, their keys) and each one's old value:
       its undo puts each item's own value back, so a mixed selection comes back mixed, and its redo
       writes the same items again, whatever is selected by then. By key, undo and redo find each key
       again in all (else items), skipping a key that has gone, so a consumer that rebuilt its items
       (an undone snapshot, an import) still gets its values back.

       Its step key is the row's key plus a number shared by every Many bind, taken anew whenever the
       captured set changes: two selections' edits are two steps however close together, even when the
       row was rebuilt in between (a new bind under the same row key), and a drag over one set is one.
       A drag frame over the set the step captured allocates nothing (with key, as long as key does not).

       A step holds only set, the captured items or keys and their old values, and all (items when all
       is null): never the bind, so a selection read through a canvas does not keep its page alive.

       Pinned (a text field being typed in), reads and writes go to the items captured at Pin, so a
       commit that lands after the selection moved on still writes what the edit began on. */
    internal sealed class ManyOf<TItem, T> : Many<T>
    {
        private static readonly EqualityComparer<T> Eq = EqualityComparer<T>.Default;
        private static readonly EqualityComparer<TItem> ItemEq = EqualityComparer<TItem>.Default;
        private static int Sets;   // shared, so a rebuilt row's new bind never takes an old bind's step key

        private readonly Func<IReadOnlyList<TItem>> _items, _all;
        private readonly Func<TItem, T> _get;
        private readonly Action<TItem, T> _set;
        private readonly Func<TItem, string> _key;
        private object[] _last;          // the set the newest step captured: keys, or the items themselves
        private string _rowKey, _stepKey;
        private T[] _end;                // the newest step's redo value, which a drag frame moves
        private Action _endRedo;
        private TItem[] _pinned;

        internal ManyOf(Func<IReadOnlyList<TItem>> items, Func<TItem, T> get, Action<TItem, T> set, Func<TItem, string> key, Func<IReadOnlyList<TItem>> all)
        {
            _items = items; _get = get; _set = set; _key = key; _all = all;
        }

        private IReadOnlyList<TItem> Items() { return _pinned ?? _items(); }

        internal override void Pin(bool on)
        {
            if (!on) { _pinned = null; return; }
            var l = _items();
            var p = new TItem[l == null ? 0 : l.Count];
            for (int i = 0; i < p.Length; i++) p[i] = l[i];
            _pinned = p;
        }

        internal override T Get()
        {
            var l = Items();
            return l == null || l.Count == 0 ? default(T) : _get(l[0]);
        }

        internal override bool Mixed()
        {
            var l = Items();
            if (l == null || l.Count < 2) return false;
            var first = _get(l[0]);
            for (int i = 1; i < l.Count; i++) if (!Eq.Equals(first, _get(l[i]))) return true;
            return false;
        }

        internal override void Set(T v)
        {
            var l = Items();
            if (l == null || _set == null) return;
            for (int i = 0; i < l.Count; i++) _set(l[i], v);
        }

        internal override void SetRecorded(T v, string label, string key, Func<string, string> translate, Action<T> applied, Func<bool> valid)
        {
            var l = Items();
            int n = l == null ? 0 : l.Count;
            if (n == 0 || _set == null) return;
            if (key != null && key == _rowKey && Captured(l) && History.Extend(_stepKey, valid, _endRedo))
                _end[0] = v;   // a drag frame: the step's redo now writes v
            else
            {
                var its = new TItem[n];
                var olds = new T[n];
                bool same = true;
                for (int i = 0; i < n; i++) { its[i] = l[i]; olds[i] = _get(its[i]); same &= Eq.Equals(olds[i], v); }
                if (!same) Record(its, olds, v, label, key, translate, applied, valid);
            }
            for (int i = 0; i < n; i++) _set(l[i], v);
        }

        // Whether l is the set the newest step captured, compared in place.
        private bool Captured(IReadOnlyList<TItem> l)
        {
            if (_last == null || _last.Length != l.Count) return false;
            for (int i = 0; i < l.Count; i++)
                if (_key != null ? !string.Equals((string)_last[i], _key(l[i])) : !ItemEq.Equals((TItem)_last[i], l[i])) return false;
            return true;
        }

        private void Record(TItem[] its, T[] olds, T v, string label, string key, Func<string, string> translate, Action<T> applied, Func<bool> valid)
        {
            int n = its.Length;
            var ids = new object[n];
            for (int i = 0; i < n; i++) ids[i] = _key != null ? (object)_key(its[i]) : its[i];
            if (!SameSet(ids)) { _last = ids; _stepKey = null; }
            if (_stepKey == null || _rowKey != key) { _rowKey = key; _stepKey = key == null ? null : key + "|set" + (++Sets); }

            var to = _end = new[] { v };
            Action undo, redo;
            if (_key == null) Steps(its, olds, to, _set, applied, out undo, out redo);
            else
            {
                var at = new Dictionary<string, int>(n);
                for (int i = 0; i < n; i++) at[(string)ids[i] ?? ""] = i;
                Steps(_all ?? _items, _key, at, olds, to, _set, applied, out undo, out redo);
            }
            _endRedo = redo;
            if (valid == null) History.Record(label, _stepKey, undo, redo, translate);
            else using (History.Step(label, _stepKey, valid, translate)) History.Record(label, _stepKey, undo, redo, translate);
        }

        /* The step's closures, made in static methods so they cannot capture the bind. applied gets the
           first item's restored value; a row re-reads its own value and Mixed anyway. */
        private static void Steps(TItem[] its, T[] olds, T[] to, Action<TItem, T> set, Action<T> applied, out Action undo, out Action redo)
        {
            undo = () => { for (int i = 0; i < its.Length; i++) set(its[i], olds[i]); if (applied != null) applied(olds[0]); };
            redo = () => { for (int i = 0; i < its.Length; i++) set(its[i], to[0]); if (applied != null) applied(to[0]); };
        }

        private static void Steps(Func<IReadOnlyList<TItem>> find, Func<TItem, string> keyOf, Dictionary<string, int> at, T[] olds, T[] to,
                                  Action<TItem, T> set, Action<T> applied, out Action undo, out Action redo)
        {
            undo = () => { Each(find, keyOf, at, set, olds, default(T), true); if (applied != null) applied(olds[0]); };
            redo = () => { Each(find, keyOf, at, set, null, to[0], false); if (applied != null) applied(to[0]); };
        }

        // Every live item whose key the step captured gets its old value (or v); a vanished key is skipped.
        private static void Each(Func<IReadOnlyList<TItem>> find, Func<TItem, string> keyOf, Dictionary<string, int> at, Action<TItem, T> set, T[] olds, T v, bool back)
        {
            var l = find();
            if (l == null) return;
            for (int k = 0; k < l.Count; k++)
            {
                int i;
                if (at.TryGetValue(keyOf(l[k]) ?? "", out i)) set(l[k], back ? olds[i] : v);
            }
        }

        private bool SameSet(object[] ids)
        {
            if (_last == null || _last.Length != ids.Length) return false;
            for (int i = 0; i < ids.Length; i++) if (!Equals(_last[i], ids[i])) return false;
            return true;
        }
    }

    public static class Bind
    {
        public static Bind<T> To<T>(Func<T> get, Action<T> set) { return new Bind<T>(get, set); }

        public static Bind<T> Value<T>(T initial, Action<T> onChange = null)
        {
            T v = initial;
            return new Bind<T>(() => v, x => { v = x; if (onChange != null) onChange(x); });
        }

        /// One row over many items (a selection): reads the first item, writes them all, and is Mixed
        /// while they disagree. A recorded write undoes to each item's own old value. With key, the
        /// step keeps keys, not the objects, and finds them again in all (else items) at undo and redo.
        public static Bind<T> Many<TItem, T>(Func<IReadOnlyList<TItem>> items, Func<TItem, T> get, Action<TItem, T> set,
                                             Func<TItem, string> key = null, Func<IReadOnlyList<TItem>> all = null)
        {
            if (items == null || get == null) throw new ArgumentNullException(items == null ? "items" : "get");
            var m = new ManyOf<TItem, T>(items, get, set, key, all);
            return new Bind<T>(m.Get, set == null ? null : (Action<T>)m.Set, m);
        }
    }
}
