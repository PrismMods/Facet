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
       hidden window reads nothing. Each pass runs inside Safe, so one surface's failure leaves the
       others ticking. */
    internal sealed class Refresher
    {
        private static readonly List<Refresher> _all = new List<Refresher>();

        // Undo and redo re-read every visible surface at once, through History's own hook (Ruling R14).
        static Refresher() { History.RefreshAll = All; }

        private readonly List<Action> _controls = new List<Action>();
        private Surface _surface;
        private IVisualElementScheduledItem _timer;
        private Action _pass;

        internal int Count => _controls.Count;

        internal void Add(Action refresh) { _controls.Add(refresh); }

        internal void Remove(Action refresh) { _controls.Remove(refresh); }

        internal void Start(Surface s)
        {
            _surface = s;
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

        internal void Now()
        {
            var s = _surface;
            if (s != null && s.Visible && s.Root != null) Safe.Run(s.Host, "Refresher", _pass);
        }

        // By index: a control's refresh may rebuild its page, adding and removing rows mid-pass.
        private void Pass()
        {
            for (int i = 0; i < _controls.Count; i++) _controls[i]();
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
