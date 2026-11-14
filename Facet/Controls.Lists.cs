using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// What a Reorder shows and what it calls. Of the inherited options only Id and Tooltip apply.
    public sealed class ReorderOptions<T> : ControlOptions where T : class
    {
        /// The items in order, asked on every refresh pass; the items are rebuilt when their keys change.
        public Func<IReadOnlyList<T>> Items { get; set; }
        /// An item's stable key (null: the item itself, by Equals: a string item is its own key, any other item its
        /// reference unless it overrides Equals). Set it when Items hands back new objects for the same item.
        public Func<T, string> Key { get; set; }
        /// An item's text, shown as returned (a name, not an English key); re-read on every refresh pass.
        public Func<T, string> Text { get; set; }
        /// Shown selected; re-read on every refresh pass.
        public Func<T, bool> Selected { get; set; }
        /// Shows a ● before the text (the one in use); re-read on every refresh pass.
        public Func<T, bool> Active { get; set; }
        /// A click on an item.
        public Action<T> Select { get; set; }
        /// A drop: Move(from, to), `to` being the item's index in the new order (remove at from, insert at to). Null: no drag.
        public Action<int, int> Move { get; set; }
        /// The new name typed into an item (never empty, never unchanged). Null: no rename.
        public Action<T, string> Rename { get; set; }
        /// A right-click on an item opens these at the pointer.
        public Func<T, IList<MenuItem>> Menu { get; set; }
        /// A trailing ＋ opening this menu under it.
        public Func<IList<MenuItem>> Add { get; set; }
        /// A caption before the items, an English key.
        public string Title { get; set; }
        /// Makes the caption a button opening this menu under it.
        public Func<IList<MenuItem>> TitleMenu { get; set; }
        /// Rows stacked down instead of a strip.
        public bool Vertical { get; set; }
        /// A strip on one line that scrolls sideways (the wheel, and a drag held near its edge) instead of wrapping.
        public bool Scroll { get; set; }
        /// Extra content built into each item after its text.
        public Action<VisualElement, T> Build { get; set; }
    }

    /* A group of items the user picks, drags into a new order, renames in place and right-clicks: a tab strip, or a
       list of rows. One Reorder is one group; two groups side by side are two. Items are rebuilt only when their
       keys change; text, selection and the active mark are re-read on every refresh pass of its surface (and on
       Refresh()), with no allocation when nothing changed.

       A drag starts past 5 px with at least two items and a Move, shows a drop marker at the slot and calls Move
       once on release when the slot differs. It is Input.Gesture while it runs, so Escape cancels it and leaves the
       window open. A lost pointer capture cancels it too: unlike a canvas drag, which writes the model on every
       frame and so commits what it did, a reorder has changed nothing until it drops. */
    public sealed class Reorder<T> where T : class
    {
        public VisualElement Root { get; }

        private readonly ReorderOptions<T> _o;
        private readonly Host _host;
        private readonly Sizes _sz;
        private readonly string _name, _site;
        private readonly VisualElement _list;     // the items' parent: Root's row, a column, or the scroll's content
        private readonly ScrollView _scroll;
        private readonly VisualElement _drop;
        private readonly List<VisualElement> _faces = new List<VisualElement>();
        private readonly List<Label> _texts = new List<Label>(), _marks = new List<Label>();
        private readonly List<T> _items = new List<T>();
        private readonly List<object> _keys = new List<object>();
        private readonly List<bool> _sel = new List<bool>(), _act = new List<bool>();
        private readonly Action _pass, _cancel;
        private Refresher _joined;

        private VisualElement _held;    // the face holding the pointer, while pressed
        private int _press = -1, _pointer = -1, _slot;
        private Vector2 _down;
        private bool _dragging;
        private int _renaming = -1;
        private Action _cancelRename;   // closes the open rename field with no call
        private bool _passing;          // a pass is running: a refresh asked from inside it (a rename ending) waits
        private Vector2 _last;          // the drag's last pointer point, for the edge scroll
        private IVisualElementScheduledItem _edge;

        internal Reorder(VisualElement parent, ReorderOptions<T> o)
        {
            _o = o;
            _name = o.Id ?? o.Title ?? "Reorder";
            _site = "Reorder '" + _name + "'";
            _pass = Pass;
            _cancel = End;

            Root = o.Vertical ? Ui.Box(parent) : Ui.Row(parent);
            _host = Host.For(Root);
            var t = Theme.For(Root);
            _sz = t.Sizes;
            Root.name = o.Id ?? "reorder";
            if (o.Tooltip != null) Words.Tip(Root, o.Tooltip);
            Root.style.flexShrink = 1f;
            Root.style.minWidth = 0f;
            if (!o.Vertical) Root.style.alignItems = Align.FlexStart;

            if (o.Title != null)
            {
                VisualElement title;
                if (o.TitleMenu != null)
                {
                    Button b = null;
                    b = Ui.Btn(Root, o.Title, () => Open(b, o.TitleMenu, "TitleMenu"));
                    Paint.Look(b, Ui.Bare);
                    title = b;
                }
                else
                {
                    var l = Ui.Text(Root, o.Title, _sz.Small, Role.TextMuted);
                    l.style.unityTextAlign = TextAnchor.MiddleLeft;
                    l.style.minHeight = _sz.ListRowHeight;
                    title = l;
                }
                title.name = Root.name + "-title";
                title.style.flexShrink = 0f;
                title.style.marginRight = o.Vertical ? 0f : _sz.Gap;
                title.style.marginBottom = o.Vertical ? _sz.Line : 0f;
            }

            if (o.Scroll && !o.Vertical)
            {
                _scroll = new ScrollView(ScrollViewMode.Horizontal);
                _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;   // the wheel and an edge drag scroll it
                _scroll.style.flexShrink = 1f;
                _scroll.style.minWidth = 0f;
                Skin.When<ScrollView>(_scroll, Skin.Scroll);
                Root.Add(_scroll);
                _list = _scroll.contentContainer;
                // No theme sheet lays the content out sideways: in the viewport's default column it takes the viewport's
                // width and the items overflow it unscrollably. Along a row it takes the items' whole width.
                _scroll.contentViewport.style.flexDirection = FlexDirection.Row;
                _list.style.flexDirection = FlexDirection.Row;
                _list.style.flexShrink = 0f;
            }
            else
            {
                _list = o.Vertical ? Ui.Box(Root) : Ui.Row(Root);
                _list.style.flexShrink = 1f;
                _list.style.minWidth = 0f;
                if (!o.Vertical) { _list.style.flexWrap = Wrap.Wrap; _list.style.alignItems = Align.FlexStart; }
            }

            _drop = new VisualElement { name = Root.name + "-drop", pickingMode = PickingMode.Ignore };
            _drop.style.position = Position.Absolute;
            _drop.style.display = DisplayStyle.None;
            Paint.Bg(_drop, Role.Accent);

            if (o.Add != null)
            {
                Button plus = null;
                plus = Ui.Btn(Root, "＋", () => Open(plus, o.Add, "Add"));
                Paint.Look(plus, Ui.Bare);
                plus.name = Root.name + "-add";
                plus.style.flexShrink = 0f;
                plus.style.marginRight = 0f;
                if (o.Vertical) plus.style.alignSelf = Align.FlexStart;
            }

            Events.On<AttachToPanelEvent>(Root, _ => Join());
            Events.On<DetachFromPanelEvent>(Root, _ => { if (_press >= 0) End(); if (_joined != null) _joined.Remove(_pass); _joined = null; });
            Refresh();
            if (Root.panel != null) Join();
        }

        /// Re-reads the items now: rebuilt when their keys changed, else their text, selection and mark.
        public void Refresh() { Safe.Run(_host, _site, _pass); }

        /// Swaps the item's text for a field holding its name (no Rename: nothing happens).
        public void BeginRename(T item)
        {
            Refresh();
            int i = _items.IndexOf(item);
            if (i < 0 && item != null && _o.Key != null)
            {
                object k = Safe.Get(_host, _site + " Key", () => _o.Key(item), null);
                i = k == null ? -1 : _keys.IndexOf(k);
            }
            if (i >= 0) Rename(i);
        }

        private void Join()
        {
            if (_joined != null) return;
            _joined = Refresher.Of(Root);
            if (_joined != null) _joined.Add(_pass, _site);
        }

        // Runs under the refresher's Safe (or Refresh's): the consumer's delegates are called directly.
        private void Pass()
        {
            if (_passing) return;
            _passing = true;
            try { PassOnce(); } finally { _passing = false; }
        }

        private void PassOnce()
        {
            var items = _o.Items != null ? _o.Items() : null;
            int n = items == null ? 0 : items.Count;
            bool same = n == _keys.Count;
            for (int i = 0; same && i < n; i++) same = Equals(KeyOf(items[i]), _keys[i]);
            if (!same) Rebuild(items, n);
            for (int i = 0; i < n; i++)
            {
                var it = _items[i] = items[i];   // a new object under the same key
                if (i != _renaming && _o.Text != null)
                {
                    string s = _o.Text(it) ?? "";
                    if (!string.Equals(_texts[i].text, s)) _texts[i].text = s;
                }
                bool sel = _o.Selected != null && _o.Selected(it);
                if (sel != _sel[i]) { _sel[i] = sel; Paint.Select(_faces[i], sel); }
                bool act = _o.Active != null && _o.Active(it);
                if (act != _act[i]) { _act[i] = act; _marks[i].style.display = act ? DisplayStyle.Flex : DisplayStyle.None; }
            }
        }

        private object KeyOf(T it) => _o.Key != null ? _o.Key(it) : it;

        private void Rebuild(IReadOnlyList<T> items, int n)
        {
            if (_press >= 0) End();
            // An open rename belongs to an item that may be gone or moved: close it with no call, never commit it to
            // whatever item now sits at its index.
            var cancel = _cancelRename;
            _cancelRename = null;
            if (cancel != null) cancel();
            _renaming = -1;
            _list.Clear();
            _faces.Clear(); _texts.Clear(); _marks.Clear(); _items.Clear(); _keys.Clear(); _sel.Clear(); _act.Clear();
            var looks = Theme.For(_list).Looks;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var face = Ui.Row(_list);
                face.name = Root.name + "-item";
                face.pickingMode = PickingMode.Position;
                face.style.flexShrink = 0f;
                face.style.minHeight = _sz.ListRowHeight;
                face.style.paddingLeft = _sz.ControlPad + _sz.Gap / 2f; face.style.paddingRight = _sz.ControlPad + _sz.Gap / 2f;
                Ui.SetBorderWidth(face, _sz.Line);
                Ui.SetRadius(face, _sz.Radius);
                if (_o.Vertical) face.style.marginBottom = _sz.Line;
                else { face.style.marginRight = _sz.Gap / 2f; if (_scroll == null) face.style.marginBottom = _sz.Gap / 2f; }
                Paint.Look(face, looks.Tab);
                var mark = Face(face, "●", "-mark");
                mark.style.marginRight = _sz.Gap / 2f;
                mark.style.display = DisplayStyle.None;
                var text = Face(face, "", "-text");
                if (_o.Build != null) Safe.Run(_host, _site + " Build", () => _o.Build(face, it));
                Events.On<PointerDownEvent>(face, e => Down(face, e));
                Events.On<PointerMoveEvent>(face, Moved);
                Events.On<PointerUpEvent>(face, Up);
                Events.On<PointerCaptureOutEvent>(face, _ => { if (_press >= 0) End(); });
                _faces.Add(face); _texts.Add(text); _marks.Add(mark); _items.Add(it); _keys.Add(KeyOf(it)); _sel.Add(false); _act.Add(false);
            }
            _list.Add(_drop);
        }

        // Data, not a word: set by hand, so a relabel leaves it alone. It takes the face's text colour (selected included).
        private Label Face(VisualElement face, string s, string name)
        {
            var l = new Label(s) { name = Root.name + name, pickingMode = PickingMode.Ignore };
            l.style.fontSize = _sz.Small;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            face.Add(l);
            Paint.Inherit(l);
            return l;
        }

        private void Open(VisualElement under, Func<IList<MenuItem>> menu, string what)
        {
            var items = Safe.Get(_host, _site + " " + what, menu, null);
            if (items != null && items.Count > 0) Popup.Menu(under, items);
        }

        // ---- the pointer

        private void Down(VisualElement face, PointerDownEvent e)
        {
            int i = _faces.IndexOf(face);
            if (i < 0 || i == _renaming) return;   // a press in the rename field is the field's
            if (e.button == 1)
            {
                e.StopPropagation();
                if (_o.Menu == null) return;
                var it = _items[i];
                var items = Safe.Get(_host, _site + " Menu", () => _o.Menu(it), null);
                if (items != null && items.Count > 0) Popup.Context(face, e.localPosition, items);
                return;
            }
            if (e.button != 0 || _press >= 0) return;
            e.StopPropagation();
            if (e.clickCount >= 2 && _o.Rename != null) { Rename(i); return; }
            _press = i;
            _pointer = e.pointerId;
            _down = e.position;
            _dragging = false;
            _held = face;
            face.CapturePointer(e.pointerId);
        }

        private void Moved(PointerMoveEvent e)
        {
            if (_press < 0 || e.pointerId != _pointer) return;
            if (!_dragging)
            {
                if (((Vector2)e.position - _down).magnitude <= 5f || _o.Move == null || _faces.Count < 2) return;
                _dragging = true;
                Input.Gesture = _cancel;
                _drop.style.display = DisplayStyle.Flex;
            }
            _last = e.position;
            if (_scroll != null)
            {
                if (_edge == null) _edge = Root.schedule.Execute(EdgeTick).Every(15);
                else _edge.Resume();
            }
            _slot = Slot(_list.WorldToLocal(e.position));
            Mark(_slot);
        }

        private void Up(PointerUpEvent e)
        {
            if (_press < 0 || e.pointerId != _pointer) return;
            int from = _press, slot = _slot;
            bool drag = _dragging;
            var it = _items[from];
            End();
            if (drag)
            {
                int to = slot > from ? slot - 1 : slot;
                if (to != from && _o.Move != null) Safe.Run(_host, _site + " Move", () => _o.Move(from, to));
            }
            else if (_o.Select != null) Safe.Run(_host, _site + " Select", () => _o.Select(it));
            Refresh();
        }

        // Puts everything back with no call: Escape (Input.Gesture), a lost capture, a rebuild, the strip leaving its panel.
        // State first, then the gesture, the capture last: the capture-out that follows finds nothing pressed.
        private void End()
        {
            var held = _held;
            _press = -1;
            _held = null;
            _dragging = false;
            if (Input.Gesture == _cancel) Input.Gesture = null;
            _drop.style.display = DisplayStyle.None;
            if (_edge != null) _edge.Pause();
            if (held != null && held.panel != null && held.HasPointerCapture(_pointer)) held.ReleasePointer(_pointer);
        }

        /* The gap the pointer is in, 0..n: before item i when it is left of (above) i's centre on the nearest line. */
        private int Slot(Vector2 p)
        {
            int n = _faces.Count;
            if (_o.Vertical)
            {
                for (int i = 0; i < n; i++) if (p.y < _faces[i].layout.center.y) return i;
                return n;
            }
            float best = float.MaxValue, line = 0f;
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Abs(p.y - _faces[i].layout.center.y);
                if (d < best) { best = d; line = _faces[i].layout.center.y; }
            }
            int last = -1;
            for (int i = 0; i < n; i++)
            {
                var r = _faces[i].layout;
                if (Mathf.Abs(r.center.y - line) > 0.5f) continue;
                if (p.x < r.center.x) return i;
                last = i;
            }
            return last + 1;
        }

        // The marker sits in the gap before item `slot` (after the last item at n).
        private void Mark(int slot)
        {
            int n = _faces.Count;
            var r = _faces[Mathf.Min(slot, n - 1)].layout;
            float w = _sz.Indicator;
            if (_o.Vertical)
            {
                float y = slot < n ? r.yMin - _sz.Line / 2f : r.yMax + _sz.Line / 2f;
                _drop.style.left = r.xMin; _drop.style.width = r.width;
                _drop.style.top = y - w / 2f; _drop.style.height = w;
            }
            else
            {
                float x = slot < n ? r.xMin - _sz.Gap / 4f : r.xMax + _sz.Gap / 4f;   // the middle of the Gap / 2 margin
                _drop.style.left = x - w / 2f; _drop.style.width = w;
                _drop.style.top = r.yMin; _drop.style.height = r.height;
            }
        }

        /* Every frame of a drag in a scrolling strip: within RowHeight of an edge it scrolls that way, ten RowHeights a
           second, so a pointer held still there keeps going; the marker follows the items under the pointer. */
        private void EdgeTick(TimerState t)
        {
            if (!_dragging || _scroll == null) return;
            var vb = _scroll.contentViewport.worldBound;
            float edge = _sz.RowHeight, dx = 0f, step = edge * 10f * Mathf.Min(t.deltaTime, 100L) / 1000f;
            if (_last.x < vb.xMin + edge) dx = -step;
            else if (_last.x > vb.xMax - edge) dx = step;
            if (dx == 0f) return;
            Skin.ScrollX(_scroll, _scroll.scrollOffset.x + dx);
            _slot = Slot(_list.WorldToLocal(_last));
            Mark(_slot);
        }

        // ---- rename

        private void Rename(int i)
        {
            if (_o.Rename == null || _renaming >= 0) return;
            if (_press >= 0) End();
            var it = _items[i];
            _renaming = i;
            _cancelRename = Controls.Rename(_texts[i], Root.name + "-rename", _texts[i].text, _host, _site + " Rename",
                name => _o.Rename(it, name), () => { _renaming = -1; _cancelRename = null; Refresh(); });
        }
    }

    public static partial class Controls
    {
        /// A group of items to pick, reorder, rename and right-click: a tab strip, or a list with Vertical.
        public static Reorder<T> Reorder<T>(VisualElement parent, Action<ReorderOptions<T>> configure) where T : class
            => new Reorder<T>(parent, ControlOptions.Make(parent, "Reorder", configure));

        /* One box per item, keyed: build runs once per new key. Each refresh pass of the surface (and the build) re-reads
           the items: boxes of new keys are built in place, boxes of gone keys removed, and the rest put in the new order
           without being rebuilt. Only a box whose place changed moves, and a move takes it off its panel and back, which
           ends a capture or an edit running inside it. Nothing allocates when the keys did not change. A key twice gets
           two boxes. */
        public static VisualElement Repeat<T>(VisualElement parent, Func<IReadOnlyList<T>> items, Func<T, string> key, Action<VisualElement, T> build)
            => new Repeater<T>(parent, items, key, build).Root;

        /* Renaming in place: `shown` gives way to a focused field holding `current`, all selected. Enter, or focus
           leaving, commits a name that is not blank and not `current` through `commit` (under Safe at `site`); Escape
           (the key in the field, or the host's Escape, which blurs it) and anything else cancel. `ended` runs once
           either way, after `shown` is back. Returns a cancel: it closes the field with no commit (nothing once ended). */
        internal static Action Rename(Label shown, string name, string current, Host host, string site, Action<string> commit, Action ended)
        {
            var parent = shown.parent;
            var f = new TextField { value = current ?? "", name = name };
            f.style.flexGrow = 1f;
            f.style.minWidth = 0f;
            f.style.fontSize = shown.resolvedStyle.fontSize > 0f ? shown.resolvedStyle.fontSize : Theme.For(parent).Sizes.Small;
            Skin.When<TextField>(f, Skin.Field);
            Cursors.Set(f, Cursors.Kind.Text);
            parent.Insert(parent.IndexOf(shown) + 1, f);
            shown.style.display = DisplayStyle.None;
            bool done = false;
            Action<bool> end = ok =>
            {
                if (done) return;
                done = true;
                string v = (f.value ?? "").Trim();
                f.RemoveFromHierarchy();
                shown.style.display = DisplayStyle.Flex;
                if (ok && v.Length > 0 && v != current) Safe.Run(host, site, () => commit(v));
                if (ended != null) Safe.Run(host, site, ended);
            };
            Events.On<KeyDownEvent>(f, e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); end(true); }
                else if (e.keyCode == KeyCode.Escape) { e.StopPropagation(); end(false); }
            });
            // FocusOut, not Blur (focus sits on the inner input); focus moving inside the field is no leaving. The
            // host's Escape blurs it: that is a cancel.
            Events.On<FocusOutEvent>(f, e =>
            {
                var to = e.relatedTarget as VisualElement;
                if (to != null && (to == f || f.Contains(to))) return;
                end(!Input.Escaping);
            });
            // Focused on the next scheduler tick: the press that opened it (a double-click) still moves focus to
            // what it hit once its handlers are done, which would blur a field focused now.
            f.schedule.Execute(() => { if (!done) { f.Focus(); f.SelectAll(); } });
            return () => end(false);
        }
    }

    internal sealed class Repeater<T>
    {
        internal readonly VisualElement Root;
        private readonly Func<IReadOnlyList<T>> _items;
        private readonly Func<T, string> _key;
        private readonly Action<VisualElement, T> _build;
        private readonly Host _host;
        private readonly string _site;
        private readonly Action _pass;
        private readonly List<string> _keys = new List<string>();
        private readonly List<VisualElement> _boxes = new List<VisualElement>();
        private Refresher _joined;

        internal Repeater(VisualElement parent, Func<IReadOnlyList<T>> items, Func<T, string> key, Action<VisualElement, T> build)
        {
            _items = items; _key = key; _build = build;
            Root = new VisualElement { name = "repeat" };
            if (parent != null) parent.Add(Root);
            _host = Host.For(Root);
            _site = "Repeat";
            _pass = Pass;
            Events.On<AttachToPanelEvent>(Root, _ => Join());
            Events.On<DetachFromPanelEvent>(Root, _ => { if (_joined != null) _joined.Remove(_pass); _joined = null; });
            Safe.Run(_host, _site, _pass);
            if (Root.panel != null) Join();
        }

        private void Join()
        {
            if (_joined != null) return;
            _joined = Refresher.Of(Root);
            if (_joined != null) _joined.Add(_pass, _site);
        }

        // Under the refresher's Safe (or the build's): the items and keys are read directly; each build is guarded.
        private void Pass()
        {
            var items = _items != null ? _items() : null;
            int n = items == null ? 0 : items.Count;
            bool same = n == _keys.Count;
            for (int i = 0; same && i < n; i++) same = string.Equals(_key(items[i]), _keys[i]);
            if (same) return;
            var old = new Dictionary<string, List<VisualElement>>();
            for (int i = 0; i < _keys.Count; i++)
            {
                List<VisualElement> l;
                if (!old.TryGetValue(_keys[i] ?? "", out l)) old[_keys[i] ?? ""] = l = new List<VisualElement>();
                l.Add(_boxes[i]);
            }
            // Match first, then drop the gone boxes: what stays is then in place unless the order changed.
            var keys = new string[n];
            var kept = new VisualElement[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = _key(items[i]);
                List<VisualElement> l;
                if (old.TryGetValue(keys[i] ?? "", out l) && l.Count > 0) { kept[i] = l[0]; l.RemoveAt(0); }
            }
            foreach (var l in old.Values) foreach (var gone in l) gone.RemoveFromHierarchy();
            _keys.Clear(); _boxes.Clear();
            /* Boxes 0..i-1 are in place, so a new box goes straight to i and an old one moves only when it is not
               there: a move takes the box off its panel and back, which ends what runs inside it. */
            for (int i = 0; i < n; i++)
            {
                var box = kept[i];
                if (box == null)
                {
                    var it = items[i];
                    box = new VisualElement { name = "repeat-item" };
                    Root.Insert(i, box);
                    if (_build != null) Safe.Run(_host, _site + " build", () => _build(box, it));
                }
                else if (Root.IndexOf(box) != i) Root.Insert(i, box);
                _keys.Add(keys[i]); _boxes.Add(box);
            }
        }
    }
}
