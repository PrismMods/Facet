using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Facet
{
    /* Undo and redo for settings, for the whole session.

       Topaz had one undo, inside the position editor, holding a stack of restore closures with no
       redo — so it could take back the last drag and nothing else. This is the general version: any
       widget that changes a value records how to put it back and how to do it again, and the two
       stacks last as long as the game does.

       Recording is by VALUE, not by snapshotting settings: a mod's settings object is its own, may
       be huge, and copying it per keystroke to diff later is both slower and less exact than
       remembering the one number that changed.

       Two rules keep the history honest:

       - Applying an undo must not record anything. The widget's own change handler fires when the
         value is put back, and without the Applying guard that would push a new entry and make undo
         impossible to escape.
       - Consecutive changes to the SAME control within a moment are one step. Dragging a slider
         raises a change per frame; without coalescing, undo would walk back through sixty of them. */
    public static class History
    {
        /* One undo step. Parts are kept in record order: an undo runs them last-first, a redo
           first-last, each on its own so one that throws leaves the rest running. */
        private sealed class Entry
        {
            public string Label;
            public Func<string, string> Translate;
            public string Key;
            public List<Action> Undo = new List<Action>();
            public List<Action> Redo = new List<Action>();
            public Func<bool> Valid;   // null: always live
            public bool Warned;        // a throwing Valid is logged once per entry
            public double At;
        }

        private static readonly List<Entry> _done = new List<Entry>();
        private static readonly List<Entry> _undone = new List<Entry>();

        // The open Step scope, if any: records join it instead of becoming steps of their own.
        private static Entry _open;
        private static int _depth, _suppress;
        private static int _gen;           // Clear moves it on: a scope opened before is abandoned
        private static double _openedAt;   // when the outermost open scope opened, by Clock

        /* A scope is closed in the call that opened it (using). One still open this long after it opened
           (held across frames, or opened without using) would take every later record from every mod into
           a step that is never pushed, so the next record closes it first. */
        private const double OpenSeconds = 5;

        /// How long two changes to one control stay a single step.
        public const float CoalesceSeconds = 0.9f;

        /// Entries kept. A session's worth of settings edits is small; this is only a guard against
        /// something recording in a loop.
        public const int Limit = 500;

        /// Where a throwing undo or redo is reported; Host.Fallback points it at its log. Core cannot see Host.
        public static Action<string> Log = _ => { };

        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        /// Seconds, monotonic. A BCL stopwatch by default so the offline tests need no Unity; a test
        /// sets its own.
        public static Func<double> Clock = () => _clock.Elapsed.TotalSeconds;

        /// True while an undo or redo is being applied. Controls check it and stay quiet.
        public static bool Applying { get; private set; }

        /// Raised whenever what the stacks show may change, so a toolbar can enable or disable its
        /// buttons. A change that only coalesces into the top step, leaving the header as it was, raises
        /// nothing: a drag would refresh every window once a frame.
        public static event Action Changed;

        /// Raised after an undo or redo has been applied (after Facet's own refresh), for panels to
        /// re-read what they show. Add with +=, so one subscriber never drops another; each handler
        /// runs under its own guard.
        public static event Action AfterApply;

        /* Facet's own re-read of every visible surface (the Refresher sets it; Core cannot see
           Surface). Internal and run before AfterApply's handlers, so no
           consumer can switch it off (Ruling R14). */
        internal static Action RefreshAll;

        /* Stale steps (their document was replaced) are skipped by all of these, so the header never
           names a step that would do nothing, and Undo moves on to the nearest live one. */
        public static bool CanUndo => TopLive(_done) >= 0;
        public static bool CanRedo => TopLive(_undone) >= 0;
        public static string NextUndo => Next(_done, false);
        public static string NextRedo => Next(_undone, false);

        /// The newest entry's key (null when there is none), for tests.
        internal static string TopKey => _done.Count > 0 ? _done[_done.Count - 1].Key : null;

        /* The label as its recorder's host shows it. History is session-wide, so each entry brings
           its own translator (that host's, or Facet's own table for Facet's words); a window never
           runs another host's label through its own. */
        public static string NextUndoText => Next(_done, true);
        public static string NextRedoText => Next(_undone, true);

        /// key groups changes that should coalesce — a setting's id or label. A null key never coalesces.
        /// Inside a Step scope the record joins the scope's step, under the scope's label and key.
        public static void Record(string label, string key, Action undo, Action redo, Func<string, string> translate = null)
        {
            if (Applying || _suppress > 0 || undo == null || redo == null) return;
            if (_depth > 0 && Clock() - _openedAt > OpenSeconds)
            {
                var lost = _open;
                Safe.Say("Facet", "History.Step", "a history step stayed open: " + lost.Label, Log);
                Abandon();
                if (lost.Undo.Count > 0) Push(lost);
            }
            if (_open != null) { _open.Undo.Add(undo); _open.Redo.Add(redo); return; }
            var e = Rent(label, key, null, translate);
            e.Undo.Add(undo); e.Redo.Add(redo);
            Push(e);
        }

        /* Everything recorded until the returned scope is disposed (Record, row writes, Snapshot) is
           one step under this label and key. A nested scope joins the outer one and ANDs its valid in;
           an empty scope records nothing. valid says whether the step can still apply: false (or a
           throw) makes Undo and Redo skip it, and it stays where it is until valid says true again.
           A scope opened while recording is off (inside a snapshot's change, or an undo) records
           nothing, so its valid has no say over the step around it: a row written inside an import's
           change would otherwise bind the import to the new document, and its undo could never be redone. */
        public static IDisposable Step(string label, string key = null, Func<bool> valid = null, Func<string, string> translate = null)
        {
            if (_depth == 0)
            {
                _open = Rent(label, key, valid, translate);
                _openedAt = Clock();
            }
            else if (!Applying && _suppress == 0) _open.Valid = And(_open.Valid, valid);
            return ScopeAt(++_depth);
        }

        /// A validity check for a step on one document: reads document() now and stays true while it
        /// returns that same reference. A throw now is logged (rate-limited) and the step is dropped.
        public static Func<bool> BoundTo(Func<object> document)
        {
            if (document == null) return null;
            // Straight through Safe.Get, with no closure: a drag asks once a frame.
            object doc = Safe.Get("Facet", "History.BoundTo", document, Unread, Log);
            if (ReferenceEquals(doc, Unread)) return Dead;
            /* The same check again for the same document, so the steps of a drag, which coalesce once
               a frame, AND nothing in: each new check would chain one more call (and stack frame). */
            if (_boundDoc != document || !ReferenceEquals(_boundRef, doc))
            {
                _boundDoc = document;
                _boundRef = doc;
                _bound = Same(document, doc);
            }
            return _bound;
        }

        /* The lambdas live in methods of their own: a captured parameter makes its closure at the top
           of the method that declares it, on every call, even when no lambda is made. */
        private static Func<bool> Same(Func<object> document, object doc) { return () => ReferenceEquals(document(), doc); }

        private static readonly object Unread = new object();

        /* The check of a step whose document could not be read. Such a step could never apply, so
           Push drops it rather than keep it stale: a drag on it would push a dead step a frame, and
           Limit would push every live step out within seconds. */
        private static readonly Func<bool> Dead = () => false;

        private static Func<object> _boundDoc;
        private static object _boundRef;
        private static Func<bool> _bound;

        /* A step by value for a change that is easier to copy than to describe: capture, run change
           with recording suppressed, capture again; equal captures record nothing. Undo restores the
           first capture and redo the second (restore runs with Applying true), and a same-key snapshot
           coalesces like Record: the first before and the last after. */
        public static void Snapshot<T>(string label, string key, Func<T> capture, Action<T> restore, Action change,
                                       Func<bool> valid = null, Func<string, string> translate = null)
        {
            if (capture == null || restore == null || change == null) return;
            T before = default(T), after = default(T);
            string site = "History.Snapshot '" + label + "'";
            bool ok = Safe.Run("Facet", site, () => before = capture(), Log);
            _suppress++;
            try { Safe.Run("Facet", site + " change", change, Log); }
            finally { _suppress--; }
            if (!ok || !Safe.Run("Facet", site, () => after = capture(), Log)) return;
            if (EqualityComparer<T>.Default.Equals(before, after)) return;
            using (Step(label, key, valid, translate)) Record(label, key, () => restore(before), () => restore(after));
        }

        /// Raises Changed. Call it after replacing a document, so headers stop naming its stale steps.
        public static void Invalidate() { Fire(); }

        public static bool Undo()
        {
            int i = TopLive(_done);
            if (i < 0) return false;
            var e = _done[i];
            _done.RemoveAt(i);
            Apply(e.Undo, true, "undo " + e.Label);
            _undone.Add(e);
            Fire();
            return true;
        }

        public static bool Redo()
        {
            int i = TopLive(_undone);
            if (i < 0) return false;
            var e = _undone[i];
            _undone.RemoveAt(i);
            Apply(e.Redo, false, "redo " + e.Label);
            // Straight back onto the undo stack, and NOT through Record, which would clear the rest
            // of the redo branch — the thing the user is in the middle of walking forward through.
            _done.Add(e);
            Fire();
            return true;
        }

        /// Empties both stacks. A scope still open is abandoned: later records are steps of their own,
        /// and its Dispose does nothing.
        public static void Clear()
        {
            _done.Clear();
            _undone.Clear();
            Abandon();
            _boundDoc = null; _boundRef = null; _bound = null;   // let the last document go
            Fire();
        }

        private static void Abandon()
        {
            _gen++;
            _depth = 0;
            _open = null;
        }

        /* One scope object per depth, reused, since a drag opens one a frame. Only the innermost open
           scope of the current generation closes: a second Dispose, or one of a scope that Clear
           abandoned, does nothing. A Dispose kept back until another scope opened at the same depth
           would close that one, so a scope is always a using. */
        private sealed class Scope : IDisposable
        {
            public int Depth, Gen;

            public void Dispose()
            {
                if (Gen != _gen || Depth != _depth) return;
                if (--_depth > 0) return;
                var e = _open;
                _open = null;
                if (e == null) return;
                if (e.Undo.Count > 0) Push(e); else Return(e);
            }
        }

        private static readonly List<Scope> _scopes = new List<Scope>();

        private static Scope ScopeAt(int depth)
        {
            while (_scopes.Count < depth) _scopes.Add(new Scope());
            var s = _scopes[depth - 1];
            s.Depth = depth;
            s.Gen = _gen;
            return s;
        }

        // What a coalesced step left behind, for the next one to fill: a drag frame allocates no entry.
        private static Entry _spare;

        private static Entry Rent(string label, string key, Func<bool> valid, Func<string, string> translate)
        {
            var e = _spare ?? new Entry();
            _spare = null;
            e.Label = label; e.Key = key; e.Valid = valid; e.Translate = translate; e.Warned = false;
            return e;
        }

        private static void Return(Entry e)
        {
            e.Undo.Clear(); e.Redo.Clear();
            e.Valid = null; e.Translate = null;
            _spare = e;
        }

        private static void Push(Entry e)
        {
            if (e.Valid == Dead) { Return(e); return; }

            var now = Clock();
            var top = Top(e.Key, e.Valid, now);
            if (top != null)
            {
                /* Keep the ORIGINAL undo and take the newest redo: the step then spans the whole drag,
                   from where it started to where it ended. The redo lists swap and e goes back for the
                   next step. */
                var redo = top.Redo; top.Redo = e.Redo; e.Redo = redo;
                bool shown = top.Translate != e.Translate;
                top.Translate = e.Translate;
                Touch(top, e.Valid, now, shown);
                Return(e);
                return;
            }

            // A new change makes the redo branch unreachable, which is what every editor does.
            _undone.Clear();
            e.At = now;
            _done.Add(e);
            if (_done.Count > Limit) _done.RemoveAt(0);
            Fire();
        }

        /* The top entry a step under key with this check coalesces into now, or null: the same non-null
           key within CoalesceSeconds, and never a stale top unless the step carries the top's very check
           (the same document), so a drag that is stale while it runs stays one step, not one a frame. */
        private static Entry Top(string key, Func<bool> valid, double now)
        {
            if (key == null || _done.Count == 0) return null;
            var top = _done[_done.Count - 1];
            return top.Key == key && now - top.At <= CoalesceSeconds && (top.Valid == valid || Live(top)) ? top : null;
        }

        // The top took a newer change: it ends now, and the redo branch is gone.
        private static void Touch(Entry top, Func<bool> valid, double now, bool shown)
        {
            // ponytail: checks that are new closures every time still chain; BoundTo hands back one per document.
            var v = And(top.Valid, valid);
            shown |= v != top.Valid || _undone.Count > 0;
            top.Valid = v;
            top.At = now;
            _undone.Clear();
            if (shown) Fire();
        }

        /// The top step takes no more coalescing writes: a drag that has ended is its own step even when the next
        /// one under the same key follows within CoalesceSeconds.
        internal static void Seal()
        {
            if (_done.Count > 0) _done[_done.Count - 1].At = double.NegativeInfinity;
        }

        /* A row's drag frame: true when a write under key with this check would only move the end of
           the step the row recorded last (the top, whose one redo is redo). The top is touched as a
           coalescing record touches it, and the caller moves what redo restores instead of recording
           again, so the frame allocates nothing. */
        internal static bool Extend(string key, Func<bool> valid, Action redo)
        {
            if (redo == null || Applying || _suppress > 0 || _depth > 0 || valid == Dead) return false;
            var now = Clock();
            var top = Top(key, valid, now);
            if (top == null || top.Redo.Count != 1 || top.Redo[0] != redo) return false;
            Touch(top, valid, now, false);
            return true;
        }

        private static Func<bool> And(Func<bool> a, Func<bool> b)
        {
            if (a == Dead || b == Dead) return Dead;
            if (a == null || a == b) return b;
            if (b == null) return a;
            return Both(a, b);
        }

        private static Func<bool> Both(Func<bool> a, Func<bool> b) { return () => a() && b(); }

        private static bool Live(Entry e)
        {
            if (e.Valid == null) return true;
            try { return e.Valid(); }
            catch (Exception ex)
            {
                if (!e.Warned) { e.Warned = true; Log("History: the validity check of " + e.Label + " threw: " + ex.Message); }
                return false;
            }
        }

        private static int TopLive(List<Entry> stack)
        {
            for (int i = stack.Count - 1; i >= 0; i--) if (Live(stack[i])) return i;
            return -1;
        }

        private static string Next(List<Entry> stack, bool shown)
        {
            int i = TopLive(stack);
            if (i < 0) return null;
            var e = stack[i];
            return shown ? Words.T(e.Label, e.Translate) : e.Label;
        }

        private static void Apply(List<Action> parts, bool backwards, string what)
        {
            // One line per apply, rate-limited: a step over a hundred items would log a hundred per keypress.
            int threw = 0;
            string first = null;
            Applying = true;
            try
            {
                for (int k = 0; k < parts.Count; k++)
                {
                    try { parts[backwards ? parts.Count - 1 - k : k](); }
                    catch (Exception e) { if (threw++ == 0) first = e.Message; }
                }
            }
            finally { Applying = false; }
            if (threw > 0) Safe.Say("Facet", "History " + what, threw + (threw == 1 ? " part threw: " : " parts threw: ") + first, Log);

            After(RefreshAll, what);
            var ap = AfterApply;
            if (ap != null)
                foreach (Action a in ap.GetInvocationList()) Safe.Run("Facet", "History.AfterApply", a, Log);
        }

        private static void After(Action a, string what)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Log("History: refresh after " + what + " threw: " + e.Message); }
        }

        private static void Fire()
        {
            var c = Changed;
            if (c == null) return;
            // One guard per handler: a throwing window's refresh must not leave the others stale.
            foreach (Action a in c.GetInvocationList()) Safe.Run("Facet", "History.Changed", a, Log);
        }
    }
}
