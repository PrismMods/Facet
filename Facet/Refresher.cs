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

        internal int Count => _controls.Count;

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

        internal void Now()
        {
            var s = _surface;
            if (s != null && s.Visible && s.Root != null) Safe.Run(s.Host, "Refresher", _pass);
        }

        /* By index: a control's refresh may rebuild its page, adding and removing rows mid-pass.
           One Safe per control (it allocates nothing when nothing throws), logged under that
           control's own site, once per 10 s, so a throwing row never silences the rows after it. */
        private void Pass()
        {
            var h = _host;
            for (int i = 0; i < _controls.Count; i++)
            {
                var c = _controls[i];
                Safe.Run(h, c.Key, c.Value);
            }
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
