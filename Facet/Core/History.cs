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
        private sealed class Entry
        {
            public string Label;
            public Func<string, string> Translate;
            public string Key;
            public Action Undo;
            public Action Redo;
            public double At;
        }

        private static readonly List<Entry> _done = new List<Entry>();
        private static readonly List<Entry> _undone = new List<Entry>();

        /// How long two changes to one control stay a single step.
        public const float CoalesceSeconds = 0.9f;

        /// Entries kept. A session's worth of settings edits is small; this is only a guard against
        /// something recording in a loop.
        public const int Limit = 500;

        /// Where a throwing undo or redo is reported; Ui points it at Ui.Log. Core cannot see Ui.
        public static Action<string> Log = _ => { };

        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        /// Seconds, monotonic. A BCL stopwatch by default so the offline tests need no Unity; a test
        /// sets its own.
        public static Func<double> Clock = () => _clock.Elapsed.TotalSeconds;

        /// True while an undo or redo is being applied. Controls check it and stay quiet.
        public static bool Applying { get; private set; }

        /// Raised whenever the stacks change, so a toolbar can enable or disable its buttons.
        public static event Action Changed;

        /// Called after an undo or redo has been applied, for panels to re-read what they show.
        public static Action AfterApply;

        /* Facet's own re-read of every visible surface (the Refresher sets it; Core cannot see
           Surface). Internal and run before AfterApply, so a consumer assigning AfterApply with =,
           as PrismLib's settings panel does, cannot switch it off (Ruling R14). */
        internal static Action RefreshAll;

        public static bool CanUndo => _done.Count > 0;
        public static bool CanRedo => _undone.Count > 0;
        public static string NextUndo => CanUndo ? _done[_done.Count - 1].Label : null;
        public static string NextRedo => CanRedo ? _undone[_undone.Count - 1].Label : null;

        /* The label as its recorder's host shows it. History is session-wide, so each entry brings
           its own translator (that host's, or Facet's own table for Facet's words); a window never
           runs another host's label through its own. */
        public static string NextUndoText => CanUndo ? Text(_done[_done.Count - 1]) : null;
        public static string NextRedoText => CanRedo ? Text(_undone[_undone.Count - 1]) : null;
        private static string Text(Entry e) => Words.T(e.Label, e.Translate);

        /// key groups changes that should coalesce — a setting's id or label.
        public static void Record(string label, string key, Action undo, Action redo, Func<string, string> translate = null)
        {
            if (Applying || undo == null || redo == null) return;

            // A new change makes the redo branch unreachable, which is what every editor does.
            _undone.Clear();

            var now = Clock();
            if (_done.Count > 0)
            {
                var top = _done[_done.Count - 1];
                if (top.Key == key && now - top.At <= CoalesceSeconds)
                {
                    /* Keep the ORIGINAL undo and take the newest redo: the step then spans the
                       whole drag, from where it started to where it ended. */
                    top.Redo = redo;
                    top.Translate = translate;
                    top.At = now;
                    Fire();
                    return;
                }
            }

            _done.Add(new Entry { Label = label, Translate = translate, Key = key, Undo = undo, Redo = redo, At = now });
            if (_done.Count > Limit) _done.RemoveAt(0);
            Fire();
        }

        public static bool Undo()
        {
            if (_done.Count == 0) return false;
            var e = _done[_done.Count - 1];
            _done.RemoveAt(_done.Count - 1);
            Apply(e.Undo, "undo " + e.Label);
            _undone.Add(e);
            Fire();
            return true;
        }

        public static bool Redo()
        {
            if (_undone.Count == 0) return false;
            var e = _undone[_undone.Count - 1];
            _undone.RemoveAt(_undone.Count - 1);
            Apply(e.Redo, "redo " + e.Label);
            // Straight back onto the undo stack, and NOT through Record, which would clear the rest
            // of the redo branch — the thing the user is in the middle of walking forward through.
            _done.Add(e);
            Fire();
            return true;
        }

        public static void Clear()
        {
            _done.Clear();
            _undone.Clear();
            Fire();
        }

        private static void Apply(Action a, string what)
        {
            Applying = true;
            try { a(); }
            catch (Exception e) { Log("History: " + what + " threw: " + e.Message); }
            finally { Applying = false; }

            After(RefreshAll, what);
            After(AfterApply, what);
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
