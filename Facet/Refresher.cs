using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Facet
{
    /* Re-reads a surface's bound controls on a timer, so a value changed behind the UI (undo, a
       profile switch, an import) shows in place instead of after a page rebuild. Polling, because
       the values live in the mods' own settings classes, which notify nobody.

       One per surface, found by its controls through Data on the surface root. Controls join when
       they attach to the panel and leave when they detach, so a rebuilt page's old rows stop being
       read with no bookkeeping by the page. The timer is paused while the surface is hidden: a
       hidden window reads nothing. Each control refreshes under its own Safe, so one that throws
       (its drawing, its value type's Equals) leaves the rows after it and other surfaces ticking. */
    internal sealed class Refresher
    {
        private static readonly List<Refresher> _all = new List<Refresher>();

        // Undo and redo re-read every visible surface at once, through History's own hook (Ruling R14). Added to,
        // not assigned: every canvas on a panel re-reads through the same hook.
        static Refresher() { History.RefreshAll += All; }

        // Each control's refresh with the Safe site it logs under, built once by the control.
        private readonly List<KeyValuePair<string, Action>> _controls = new List<KeyValuePair<string, Action>>();
        private Surface _surface;
        private Host _host;
        private IVisualElementScheduledItem _timer;
        private Action _pass;
        private int _written = -1;   // the frame of the last pass a write asked for
        private int _depth;          // writes in progress (nested), see Enter
        private bool _inPass;
        internal int Passes;         // every pass run, for the autotest

        internal int Count => _controls.Count;

        /// The refresher of the surface `e` sits in, or null.
        internal static Refresher Of(VisualElement e)
        {
            Refresher r = null;
            for (; e != null && r == null; e = e.hierarchy.parent) r = Data.Find<Refresher>(e);
            return r;
        }

        internal void Add(Action refresh, string site) { _controls.Add(new KeyValuePair<string, Action>(site, refresh)); }

        internal void Remove(Action refresh)
        {
            for (int i = 0; i < _controls.Count; i++)
                if (_controls[i].Value == refresh) { _controls.RemoveAt(i); return; }
        }

        internal void Start(Surface s)
        {
            _surface = s;
            _host = s.Host;
            _pass = Pass;
            _timer = s.Root.schedule.Execute(Now).Every(Math.Max(10, s.Host.RefreshMs));
            _all.Add(this);
        }

        /// Hidden, the timer stops; shown again, catch up at once.
        internal void Show(bool on)
        {
            if (_timer == null) return;
            if (!on) { _timer.Pause(); return; }
            _timer.Resume();
            Now();
        }

        /* A control wrote (or an Action button ran): refresh now, so a row whose VisibleWhen or value depends on what
           was written follows in this frame rather than at the next tick. Writes nest (a Committed or AfterChange
           handler writing another row, an Action writing several): Enter and Exit bracket each one, and the pass runs
           once, when the outermost returns, so every write inside it and every follow-up of the outer row show. At
           most once per frame: a drag writes every frame, and a second outermost write in the same frame waits for the
           timer. A write made during a pass runs none: the pass is already reading the rows. Enter returns the
           refresher (r when the control has joined one, else the one above e) for the matching Exit. */
        internal static Refresher Enter(Refresher r, VisualElement e)
        {
            if (r == null) r = Of(e);
            if (r != null) r._depth++;
            return r;
        }

        internal static void Exit(Refresher r)
        {
            if (r == null || --r._depth > 0 || r._inPass) return;
            int f = UnityEngine.Time.frameCount;
            if (r._written == f) return;
            r._written = f;
            r.Now();
        }

        internal void Now()
        {
            var s = _surface;
            if (!_inPass && s != null && s.Visible && s.Root != null) Safe.Run(s.Host, "Refresher", _pass);
        }

        /* By index: a control's refresh may rebuild its page, adding and removing rows mid-pass.
           One Safe per control (it allocates nothing when nothing throws), logged under that
           control's own site, once per 10 s, so a throwing row never silences the rows after it. */
        private void Pass()
        {
            var h = _host;
            Passes++;
            _inPass = true;
            try
            {
                for (int i = 0; i < _controls.Count; i++)
                {
                    var c = _controls[i];
                    Safe.Run(h, c.Key, c.Value);
                }
            }
            finally { _inPass = false; }
        }

        internal void Stop()
        {
            if (_timer != null) _timer.Pause();
            _controls.Clear();
            _all.Remove(this);
            _surface = null;
        }

        private static void All()
        {
            for (int i = 0; i < _all.Count; i++) _all[i].Now();
        }
    }
}
