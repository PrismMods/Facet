using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Facet
{
    /* A row bound to a value, and the handle its builder returns. A plain class, not a
       VisualElement subclass (IL2CPP rule): the row is Root, the part the user edits is Input.

       The builder makes the row and the input and hands over `show`, which draws a value without
       raising anything; everything else is here. While the surface is visible its Refresher calls
       Refresh every host.RefreshMs: the value is read again and, when it differs from what is shown,
       shown without notifying. A row being edited is skipped, so a refresh never yanks text out
       from under the caret or a slider from under the pointer. Three failed reads in a row and the
       row is unavailable: dimmed, its tooltip pointing at the log, and no longer read.

       Every call into consumer code — the bind, the predicates, the menu actions, the tooltip func
       and the event handlers — goes through Safe. */
    public sealed class Control<T>
    {
        public VisualElement Root { get; }
        public VisualElement Input { get; }

        /// What the row shows: the last value read or set.
        public T Value { get; private set; }

        /// The getter failed three reads in a row. The row is dimmed and no longer read.
        public bool Unavailable { get; private set; }

        /// Every change made through the row or Set: each step of a drag.
        public event Action<T> Changed;

        /// The end of a change: a click, Enter, the end of a drag.
        public event Action<T> Committed;

        private const int FailLimit = 3;

        private readonly Bind<T> _bind;
        private readonly Action<T> _show;
        private readonly ControlOptions _o;
        private readonly Options<T> _d;     // _o, when it can carry a Default
        private readonly VisualElement _dot;
        private readonly string _name, _getSite;
        private readonly Action _read, _write, _refresh;   // allocated once: Refresh runs per tick
        private string _setSite, _enabledSite, _visibleSite;
        private Host _host;
        private Refresher _refresher;
        private T _got, _pending;
        private int _fails;
        private bool _enabled = true, _visible = true;

        internal Control(VisualElement root, VisualElement input, string label, Bind<T> bind, Action<T> show, ControlOptions options)
        {
            Root = root;
            Input = input;
            _bind = bind;
            _show = show;
            _o = options ?? new ControlOptions();
            _d = options as Options<T>;
            _name = _o.Id ?? label;
            _getSite = "Bind.Get '" + _name + "'";
            _read = () => _got = _bind.Get();
            _write = () => _bind.Set(_pending);
            _refresh = Refresh;
            _host = Host.For(root);

            if (_o.Id != null)
            {
                Root.name = _o.Id;
                if (Input != null) Input.name = _o.Id + "-input";
            }
            if (_o.Tooltip != null) Words.Tip(Root, _o.Tooltip);
            if (_o.TooltipText != null)
                Events.On<MouseEnterEvent>(Root, _ =>
                {
                    if (!Unavailable) Root.tooltip = Safe.Get(_host, "TooltipText '" + _name + "'", _o.TooltipText, Root.tooltip);
                });

            if (_d != null && _d.HasDefault)
            {
                // Before the label, in the flow: a changed row reads as changed from its left edge.
                _dot = new VisualElement { name = "changed", pickingMode = PickingMode.Ignore };
                _dot.style.width = 6f;
                _dot.style.height = 6f;
                _dot.style.marginRight = 4f;
                _dot.style.flexShrink = 0f;
                Ui.SetRadius(_dot, 3f);
                Paint.Bg(_dot, Role.Accent);
                Root.Insert(0, _dot);
            }

            Events.On<PointerDownEvent>(Root, OnPointerDown);
            Events.On<AttachToPanelEvent>(Root, _ => Attach());
            Events.On<DetachFromPanelEvent>(Root, _ => Detach());

            if (Safe.Run(_host, _getSite, _read)) Value = _got; else _fails = 1;
            Show(Value);
            Conditions();
            // Built straight into a live page: the attach event has already gone by.
            if (Root.panel != null) Attach();
        }

        /// Write v to the bind and show it. notify raises Changed and Committed.
        public void Set(T v, bool notify = true)
        {
            Write(v);
            if (!notify) return;
            Raise(Changed, "Changed");
            Raise(Committed, "Committed");
        }

        /// One step of a change still in progress (a drag frame): written, shown, Changed raised.
        internal void Edit(T v)
        {
            Write(v);
            Raise(Changed, "Changed");
        }

        /// The change in progress is done: Committed with the current value.
        internal void Commit() { Raise(Committed, "Committed"); }

        /// Re-ask EnabledWhen and VisibleWhen, then read the value and show it if it changed. Skipped
        /// while the row is being edited; does nothing once the row is unavailable.
        public void Refresh()
        {
            if (Unavailable) return;
            Conditions();
            if (Editing()) return;
            if (!Safe.Run(_host, _getSite, _read))
            {
                if (++_fails >= FailLimit) Fail();
                return;
            }
            _fails = 0;
            if (!EqualityComparer<T>.Default.Equals(_got, Value)) Show(_got);
        }

        private void Write(T v)
        {
            _pending = v;
            Safe.Run(_host, _setSite ?? (_setSite = "Bind.Set '" + _name + "'"), _write);
            Show(v);
        }

        private void Show(T v)
        {
            Value = v;
            if (_show != null) _show(v);
            if (_dot != null)
                _dot.style.display = EqualityComparer<T>.Default.Equals(v, _d.Default) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Raise(Action<T> handler, string what)
        {
            if (handler == null) return;
            var v = Value;
            Safe.Run(_host, what + " '" + _name + "'", () => handler(v));
        }

        private void Conditions()
        {
            if (_o.VisibleWhen != null)
            {
                bool v = Safe.Get(_host, _visibleSite ?? (_visibleSite = "VisibleWhen '" + _name + "'"), _o.VisibleWhen, true);
                if (v != _visible) { _visible = v; Root.style.display = v ? DisplayStyle.Flex : DisplayStyle.None; }
            }
            if (_o.EnabledWhen != null)
            {
                bool v = Safe.Get(_host, _enabledSite ?? (_enabledSite = "EnabledWhen '" + _name + "'"), _o.EnabledWhen, true);
                if (v != _enabled) { _enabled = v; Paint.Enable(Root, v); }
            }
        }

        /* Being edited: the pointer is captured inside the row (a slider drag), or a TEXT field in it
           has focus. Only a text field: a clicked Button keeps focus until something else takes it,
           and a segment row would otherwise never refresh again after its first click. The whole row,
           not just Input: a slider's typed-number box sits beside it. */
        private bool Editing()
        {
            var p = Root.panel;
            if (p == null) return false;
            var cap = p.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
            if (cap != null && (cap == Root || Root.Contains(cap))) return true;
            var f = p.focusController != null ? p.focusController.focusedElement as VisualElement : null;
            if (f == null || !Root.Contains(f)) return false;
            for (var e = f; e != null && e != Root; e = e.parent)
                if (Facet.Input.IsTextInput(e.GetType())) return true;
            return false;
        }

        private void Fail()
        {
            Unavailable = true;
            Paint.Enable(Root, false);
            Words.Tip(Root, "unavailable, see the log", true);
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            if (e.button == 2 && _d != null && _d.HasDefault)
            {
                // Already at Default: no write, so no empty undo step and no Changed.
                if (!EqualityComparer<T>.Default.Equals(Value, _d.Default)) Set(_d.Default);
                e.StopPropagation();
            }
            else if (e.button == 1 && _o.ContextMenu != null && _o.ContextMenu.Count > 0)
            {
                var items = _o.ContextMenu;
                var names = new string[items.Count];
                for (int i = 0; i < names.Length; i++) names[i] = items[i].Key;
                Popup.Menu(Input ?? Root, names, -1, i => Safe.Run(_host, "ContextMenu '" + names[i] + "'", items[i].Value));
                e.StopPropagation();
            }
        }

        // Join the refresher of the surface this row now sits in. Guarded: a row built straight into
        // a live page joins in the constructor and may still hear its (queued) attach event.
        private void Attach()
        {
            if (_refresher != null) return;
            _host = Host.For(Root);
            for (var e = Root; e != null && _refresher == null; e = e.hierarchy.parent) _refresher = Data.Find<Refresher>(e);
            if (_refresher != null) _refresher.Add(_refresh);
        }

        private void Detach()
        {
            if (_refresher != null) _refresher.Remove(_refresh);
            _refresher = null;
        }
    }
}
