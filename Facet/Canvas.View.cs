using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The canvas's view and keys: zoom and pan (the wheel, the middle button, a right drag), the context menu on a
       right click, the focused canvas's keys (nudge, delete, z-order) and its share of Escape, and double-click. */
    public sealed partial class Canvas<T> : ICanvasView
    {
        /// 0.3 to 4. Setting it keeps the view centre still.
        public float Zoom
        {
            get { return _zoom; }
            set { ZoomAt(value, StageSize * 0.5f); }
        }

        /// The stage position of canvas (0, 0), in stage px.
        public Vector2 Pan
        {
            get { return _pan; }
            set { SetView(_zoom, value); }
        }

        /// Raised once for every change of the zoom or the pan.
        public event Action ViewChanged;

        /// A second press on the same item within 0.3 s.
        public event Action<T> Activated;

        /// The one canvas of this Facet copy that a press on its stage last focused; a press on anything else of any
        /// Facet surface, or its own surface hiding, unfocuses it.
        public bool Focused => ReferenceEquals(CanvasAll.Focused, this);

        /// True: the wheel and trackpad pan with the swipe instead of against it, on both axes (Ctrl/Cmd zoom is
        /// unchanged). Read at each wheel step on either path, so a host's setting applies at once; null is false.
        public Func<bool> InvertWheelPan { get; set; }

        /// The pointer is over the stage.
        public bool Hovered => _hovered;

        /// The canvas point at the middle of the stage.
        public Vector2 ViewCentre => ToCanvas(StageSize * 0.5f);

        public Vector2 StageToCanvas(Vector2 stage) => ToCanvas(stage);

        public Vector2 CanvasToStage(Vector2 canvas) => canvas * _zoom + _pan;

        /// The topmost selectable item under a stage point, or null.
        public T HitTest(Vector2 stage)
        {
            Refresh();
            return ItemAt(ToCanvas(stage));
        }

        public void ZoomBy(float delta) { Zoom = _zoom + delta; }

        /// Frames every item with 40 px padding; with no items it is ResetView.
        public void Fit()
        {
            Rect u;
            var s = StageSize;
            if (!ItemUnion(out u) || s.x <= 0f || s.y <= 0f) { ResetView(); return; }
            float z, x, y;
            CanvasMath.Fit(new Box(u.x, u.y, u.width, u.height), s.x, s.y, 40f, out z, out x, out y);
            SetView(z, new Vector2(x, y));
        }

        /// Centres every item (with none, the logical canvas) at the current zoom.
        public void Centre()
        {
            Rect u;
            if (!ItemUnion(out u)) u = new Rect(Vector2.zero, _size);
            var s = StageSize;
            SetView(_zoom, s * 0.5f - u.center * _zoom);
        }

        /// Zoom 1, centred.
        public void ResetView()
        {
            Rect u;
            if (!ItemUnion(out u)) u = new Rect(Vector2.zero, _size);
            SetView(1f, StageSize * 0.5f - u.center);
        }

        /// Moves every movable selected item by (dx, dy) canvas units as one "Nudge" step, keyed by the selection, so
        /// repeated nudges of one selection within History.CoalesceSeconds stay one step. Raises Changed, then Committed.
        public void Nudge(float dx, float dy)
        {
            if (!NudgeBy(dx, dy)) return;
            _nudging = false;   // committed here, so a key poll with no arrow held commits nothing more
            _repeatKey = KeyCode.None;
            Raise(Committed, _siteCommitted);
        }

        /// What Delete and Backspace run: Delete(selection) inside a "Delete" step, then Changed and Committed.
        public void DeleteSelection() { Delete(); }

        /// What ] and [ run: one z-step of the selection, ZOrder(the whole list in its new draw order) inside a
        /// "Bring forward" or "Send backward" step when something moved, then Changed and Committed.
        public void StepZ(bool forward) { ZStep(forward); }

        /* The host calls this every frame with its key reads (down: this frame; held: still down), then runs its menu
           shortcuts only when this returned false. Nothing while a text field has focus, a key capture or a drag runs,
           or Ctrl, Cmd or Alt is held (chords are the menus'). Focused: the arrows nudge by 1 (repeating after 0.4 s
           every 0.05 s), Delete/Backspace delete, ] and [ step the z-order. Focused or hovered: + = zoom in, - out, 0
           ResetView. True when it acted. */
        public bool PollKeys(Func<KeyCode, bool> down, Func<KeyCode, bool> held)
        {
            if (down == null || held == null) return false;
            try { return Keys(down, held); }
            catch (Exception e) { Safe.Report(_host.Name, _siteKeys, e, _host.LogFn); return false; }
        }

        // ---- the view

        private Vector2 StageSize => new Vector2(Stage.layout.width, Stage.layout.height);

        private bool _viewed, _hovered;
        private Vector2 _panStart;
        private int _button, _nativeWheel = -1, _polledWheel = -1;
        private Func<T, IList<MenuItem>> _menu;
        private Action<IReadOnlyList<T>> _delete, _zorder;
        private string _siteKeys, _siteMenu, _siteDelete, _siteZOrder, _siteView, _siteActivated, _siteInvert;
        private T _lastHit;
        private float _lastHitAt;
        private bool _nudging;
        private KeyCode _repeatKey;
        private float _repeatAt;
        private string _nudgeKey;
        private int _nudgeKeyFor = -1;
        private Part _nudgePart;
        private Action _nudgeRedo;

        private void ViewOptions(CanvasOptions<T> o, string site)
        {
            _menu = o.Menu; _delete = o.Delete; _zorder = o.ZOrder;
            _siteKeys = site + "PollKeys"; _siteMenu = site + "Menu"; _siteDelete = site + "Delete"; _siteZOrder = site + "ZOrder";
            _siteView = site + "ViewChanged"; _siteActivated = site + "Activated"; _siteInvert = site + "InvertWheelPan";
            Stage.focusable = true;
            Events.On<GeometryChangedEvent>(Stage, _ => FirstSize());
            Events.On<WheelEvent>(Stage, NativeWheel);
            Events.On<MouseEnterEvent>(Stage, _ => _hovered = true);
            Events.On<MouseLeaveEvent>(Stage, _ => _hovered = false);
            // The arrows are the canvas's while it has focus, never a focus move to the next control.
            Events.On<NavigationMoveEvent>(Stage, e => { if (Focused) e.StopPropagation(); });
        }

        // The first time the stage has a size it shows the items at zoom 1.
        private void FirstSize()
        {
            if (_viewed || Stage.layout.width <= 0f || Stage.layout.height <= 0f) return;
            _viewed = true;
            ResetView();
        }

        private bool ItemUnion(out Rect u)
        {
            ReadItems();
            u = default(Rect);
            bool any = false;
            for (int i = 0; i < _items.Count; i++)
            {
                Rect r;
                if (_items[i] == null || !TryRect(_items[i], out r)) continue;
                u = any ? Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin), Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax)) : r;
                any = true;
            }
            return any;
        }

        private void ZoomAt(float zoom, Vector2 at)
        {
            float x, y;
            CanvasMath.ZoomAt(_zoom, _pan.x, _pan.y, zoom, at.x, at.y, out x, out y);
            SetView(CanvasMath.ClampZoom(zoom), new Vector2(x, y));
        }

        private void SetView(float zoom, Vector2 pan)
        {
            zoom = CanvasMath.ClampZoom(zoom);
            if (float.IsNaN(pan.x) || float.IsInfinity(pan.x) || float.IsNaN(pan.y) || float.IsInfinity(pan.y)) return;
            if (zoom == _zoom && pan == _pan) return;
            _zoom = zoom; _pan = pan;
            View();
            Redraw();
            Raise(ViewChanged, _siteView);
        }

        // ---- the wheel: the panel's own event and the host's poll feed one handler, at most one of them a frame

        // Units of a notch, x right and y down; InputForUI sends 3 per notch.
        private void NativeWheel(WheelEvent e)
        {
            e.StopPropagation();   // the page around the stage must not scroll as well
            if (_polledWheel == Time.frameCount) return;
            _nativeWheel = Time.frameCount;
            Wheel(e.delta.x / 3f, e.delta.y / 3f, e.ctrlKey || e.commandKey, e.shiftKey, Stage.WorldToLocal(e.mousePosition));
        }

        /* Input.TickWheel's delta is Input.mouseScrollDelta.y (up is positive); x and the modifiers are read here, in
           the host's own call, which a host's input gate lets through. One notch can reach the panel as an event on one
           frame and Input.mouseScrollDelta on the next, so a frame after the event's is skipped too, as the scrollers do. */
        void ICanvasView.Polled(float delta, Vector2 stage)
        {
            if (Time.frameCount - _nativeWheel <= 1) return;
            _polledWheel = Time.frameCount;
            bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)
                     || UnityEngine.Input.GetKey(KeyCode.LeftCommand) || UnityEngine.Input.GetKey(KeyCode.RightCommand);
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            float x, y;
            CanvasMath.PolledNotch(UnityEngine.Input.mouseScrollDelta.x, delta, out x, out y);
            Wheel(x, y, ctrl, shift, stage);
        }

        // Pans by CanvasMath.WheelPan; Ctrl or Cmd zooms at the pointer.
        private void Wheel(float x, float y, bool zoom, bool shift, Vector2 at)
        {
            if (_mode != Mode.None) return;
            if (zoom)
            {
                float n = Mathf.Abs(y) >= Mathf.Abs(x) ? y : x;
                if (n == 0f) return;
                ZoomAt(_zoom - Mathf.Sign(n) * 0.1f * Mathf.Min(Mathf.Abs(n), 1f), at);
                return;
            }
            float px, py;
            var invert = InvertWheelPan;
            CanvasMath.WheelPan(x, y, shift, invert != null && Safe.Get(_host, _siteInvert, invert, false), out px, out py);
            SetView(_zoom, _pan + new Vector2(px, py));
        }

        // ---- the right and middle buttons

        // A press of the right or middle button: the middle pans at once, the right waits for 5 px.
        private void PressPan(PointerDownEvent e, Vector2 s)
        {
            Refresh();
            _downStage = s;
            _panStart = _pan;
            _mode = e.button == 2 ? Mode.Pan : Mode.PressRight;
            _button = e.button;
            _pointer = e.pointerId;
            Stage.CapturePointer(e.pointerId);
            Input.Gesture = _cancel;
        }

        private void DoPan(Vector2 s) { SetView(_zoom, _panStart + (s - _downStage)); }

        // A right click: select the item under the press (alone, unless it was selected), then its menu at the press.
        private void OpenMenu()
        {
            var hit = ItemAt(ToCanvas(_downStage));
            if (hit != null && IndexOf(_sel, hit) < 0)
            {
                _primary = hit;
                _scratch.Clear(); _scratch.Add(hit);
                SetSelection(_scratch);
            }
            if (_menu == null) return;
            var menu = _menu;
            var items = Safe.Get(_host, _siteMenu, () => menu(hit), null);
            if (items != null && items.Count > 0 && Stage.panel != null) Popup.Context(Stage, _downStage, items);
        }

        // ---- focus, hover and double-click

        private void TakeFocus()
        {
            CanvasAll.Focused = this;
            Stage.Focus();   // a text field elsewhere gives up focus
        }

        private void Unfocus()
        {
            if (Focused) CanvasAll.Focused = null;
        }

        // The surface hid or the stage left its panel: no gesture, focus or hover survives it.
        private void Gone()
        {
            Lost();
            Unfocus();
            _hovered = false;
            _lastHit = null;
            EndNudge();
            ForgetNudge();
        }

        private void Pressed(T hit)
        {
            float now = Time.realtimeSinceStartup;
            if (hit != null && ReferenceEquals(hit, _lastHit) && now - _lastHitAt <= 0.3f)
            {
                _lastHit = null;
                var a = Activated;
                if (a != null) Safe.Run(_host, _siteActivated, () => a(hit));
                return;
            }
            _lastHit = hit;
            _lastHitAt = now;
        }

        // The last pressed item is kept only for its 0.3 s and only while Items holds it, so it never keeps a removed item.
        private void ForgetHit()
        {
            if (_lastHit != null && (Time.realtimeSinceStartup - _lastHitAt > 0.3f || IndexOf(_items, _lastHit) < 0)) _lastHit = null;
        }

        bool ICanvasView.TakeEscape()
        {
            if (_sel.Count == 0) return false;
            Clear();
            return true;
        }

        // ---- keys

        private bool Keys(Func<KeyCode, bool> down, Func<KeyCode, bool> held)
        {
            ForgetHit();
            if (_nudging && !held(KeyCode.LeftArrow) && !held(KeyCode.RightArrow) && !held(KeyCode.UpArrow) && !held(KeyCode.DownArrow)) EndNudge();
            if (Stage.panel == null || Input.TextFocused || KeyCapture.Owner != null || Input.Gesture != null) return false;
            if (held(KeyCode.LeftControl) || held(KeyCode.RightControl) || held(KeyCode.LeftCommand) || held(KeyCode.RightCommand)
                || held(KeyCode.LeftAlt) || held(KeyCode.RightAlt)) return false;
            bool focused = Focused;
            if (focused)
            {
                if (Arrow(down, KeyCode.LeftArrow, -1f, 0f) || Arrow(down, KeyCode.RightArrow, 1f, 0f)
                    || Arrow(down, KeyCode.UpArrow, 0f, -1f) || Arrow(down, KeyCode.DownArrow, 0f, 1f)) return true;
                if (_repeatKey != KeyCode.None && held(_repeatKey) && Time.realtimeSinceStartup >= _repeatAt)
                {
                    _repeatAt += 0.05f;
                    return Step(_repeatKey);
                }
                if (down(KeyCode.Delete) || down(KeyCode.Backspace)) return Delete();
                if (down(KeyCode.RightBracket)) return ZStep(true);
                if (down(KeyCode.LeftBracket)) return ZStep(false);
            }
            if (focused || _hovered)
            {
                if (down(KeyCode.Plus) || down(KeyCode.Equals) || down(KeyCode.KeypadPlus)) { ZoomBy(0.1f); return true; }
                if (down(KeyCode.Minus) || down(KeyCode.KeypadMinus)) { ZoomBy(-0.1f); return true; }
                if (down(KeyCode.Alpha0) || down(KeyCode.Keypad0)) { ResetView(); return true; }
            }
            return false;
        }

        // True only when something moved: with nothing to nudge the key is the host's menus'.
        private bool Arrow(Func<KeyCode, bool> down, KeyCode k, float dx, float dy)
        {
            if (!down(k) || !NudgeBy(dx, dy)) return false;
            _repeatKey = k;
            _repeatAt = Time.realtimeSinceStartup + 0.4f;
            return true;
        }

        private bool Step(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.LeftArrow: return NudgeBy(-1f, 0f);
                case KeyCode.RightArrow: return NudgeBy(1f, 0f);
                case KeyCode.UpArrow: return NudgeBy(0f, -1f);
                default: return NudgeBy(0f, 1f);
            }
        }

        // The key is up: the nudges it made are committed once.
        private void EndNudge()
        {
            _repeatKey = KeyCode.None;
            if (!_nudging) return;
            _nudging = false;
            Raise(Committed, _siteCommitted);
        }

        // A step of the selection's movable items, by whole canvas units whatever the snap. Committed is left to the caller.
        private bool NudgeBy(float dx, float dy)
        {
            if (_mode != Mode.None) return false;
            if (Reread()) SelectionDidChange();
            _count = 0;
            for (int i = 0; i < _sel.Count; i++)
            {
                Rect r;
                if (Is(_movable, _sel[i], _siteMovable) && TryRect(_sel[i], out r)) AddMover(_sel[i], r);
            }
            if (_count == 0) return false;
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                var r = Nudged(_start[i], dx, dy, _limit);
                if (r != _start[i]) { Put(_movers[i], r); n++; }
            }
            if (n > 0 && _undoable)
            {
                if (_nudgeKeyFor != SelectionVersion) { _nudgeKeyFor = SelectionVersion; _nudgeKey = "canvas/" + _name + "/nudge/" + SelectionVersion; }
                if (!ExtendNudge(dx, dy)) RecordNudge(n, dx, dy);
            }
            for (int i = 0; i < _count; i++) _movers[i] = null;
            _count = 0;
            if (n == 0) return false;
            _nudging = true;
            Raise(Changed, _siteChanged);
            Redraw();
            return true;
        }

        private void RecordNudge(int n, float dx, float dy)
        {
            var p = NewPart("Nudge", n);
            for (int i = 0, k = 0; i < _count; i++)
            {
                var r = Nudged(_start[i], dx, dy, _limit);
                if (r == _start[i]) continue;
                if (p.Keys != null) p.Keys[k] = KeyOf(_movers[i]); else p.Refs[k] = _movers[i];
                p.Before[k] = _start[i]; p.After[k] = r; k++;
            }
            _nudgeRedo = Record(p, "Nudge", _nudgeKey);
            _nudgePart = p;
        }

        /* A nudge that History would only coalesce into the step on top (the last one this canvas recorded, same key,
           within the window, not undone) moves that step's end in place instead of recording a new part, so a held
           arrow allocates nothing per repeat. Needs every item that moves now to be in that step, in mover order. */
        private bool ExtendNudge(float dx, float dy)
        {
            var p = _nudgePart;
            if (p == null) return false;
            int len = p.After.Length, k = 0;
            for (int i = 0; i < _count; i++)
            {
                if (Nudged(_start[i], dx, dy, _limit) == _start[i]) continue;
                while (k < len && !InPart(p, k, _movers[i])) k++;
                if (k == len) return false;
                k++;
            }
            if (!History.Extend(_nudgeKey, _document == null ? null : History.BoundTo(_document), _nudgeRedo)) return false;
            k = 0;
            for (int i = 0; i < _count; i++)
            {
                var r = Nudged(_start[i], dx, dy, _limit);
                if (r == _start[i]) continue;
                while (!InPart(p, k, _movers[i])) k++;
                p.After[k++] = r;
            }
            return true;
        }

        private bool InPart(Part p, int k, T item) => p.Keys != null ? p.Keys[k] == KeyOf(item) : ReferenceEquals(p.Refs[k], item);

        // The run's step may hold items (no Key): the canvas lets it go once the selection changes or the stage goes.
        private void ForgetNudge() { _nudgePart = null; _nudgeRedo = null; }

        private static Rect Nudged(Rect r, float dx, float dy, float limit)
        {
            r.x = CanvasMath.Clamp(r.x + dx, limit);
            r.y = CanvasMath.Clamp(r.y + dy, limit);
            return r;
        }

        // Delete(selection) inside one step of its own; the consumer records what it removed.
        private bool Delete()
        {
            if (_delete == null || _mode != Mode.None) return false;
            if (Reread()) SelectionDidChange();
            if (_sel.Count == 0) return false;
            var gone = _sel.ToArray();
            var del = _delete;
            Consumer("Delete", "/delete#", _siteDelete, () => del(gone));
            return true;
        }

        private bool ZStep(bool forward)
        {
            if (_zorder == null || _mode != Mode.None) return false;
            if (Reread()) SelectionDidChange();
            if (_sel.Count == 0) return false;
            int n = _items.Count;
            var order = new int[n];
            var picked = new bool[n];
            for (int i = 0; i < n; i++) { order[i] = i; picked[i] = IndexOf(_sel, _items[i]) >= 0; }
            if (!CanvasMath.StepZ(order, picked, forward)) return false;
            var list = new T[n];
            for (int i = 0; i < n; i++) list[i] = _items[order[i]];
            var z = _zorder;
            Consumer(forward ? "Bring forward" : "Send backward", "/z#", _siteZOrder, () => z(list));
            return true;
        }

        // A consumer edit as one step that never coalesces with another, then the canvas re-reads and reports it.
        private void Consumer(string label, string kind, string site, Action run)
        {
            if (_undoable)
            {
                var valid = _document == null ? null : History.BoundTo(_document);
                using (History.Step(label, "canvas/" + _name + kind + (++CanvasAll.Gestures), valid, _translate)) Safe.Run(_host, site, run);
            }
            else Safe.Run(_host, site, run);
            Refresh();
            Raise(Changed, _siteChanged);
            Raise(Committed, _siteCommitted);
        }
    }

    // What every Facet surface and Input see of a canvas, whatever its item type.
    internal interface ICanvasView
    {
        VisualElement Stage { get; }
        bool TakeEscape();
        void Polled(float delta, Vector2 stage);
    }
}
