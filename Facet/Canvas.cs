using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// What Controls.Canvas is told. Rects are canvas units, y down; Facet reads and writes them only through GetRect and
    /// SetRect, and the consumer draws its items into Canvas.Content. Every step is bound to UndoDocument when it is set;
    /// Undoable false records no steps, and the events still fire. Every delegate runs under Safe. An undo raises
    /// Changed and Committed only on a canvas on a panel; off one it only writes through SetRect (History.Applying
    /// true), so work every undo needs belongs there. The pane is a Controls.Preview: Id names its stage (Id-pane the
    /// pane), and VisibleWhen, EnabledWhen, Tooltip/TooltipText and ContextMenu act on the pane (EnabledWhen false
    /// also stops the stage taking the pointer). Help is not shown.
    public sealed class CanvasOptions<T> : ControlOptions where T : class
    {
        /// Draw order, last on top.
        public Func<IReadOnlyList<T>> Items { get; set; }

        /// Identity across Items() rebuilds; null compares by reference.
        public Func<T, string> Key { get; set; }

        /// The item with this key anywhere in the document, for an undo of an item Items() no longer lists (another
        /// tab). Null looks in Items().
        public Func<string, T> Find { get; set; }

        public Func<T, Rect> GetRect { get; set; }
        public Action<T, Rect> SetRect { get; set; }

        /// The logical canvas; its centre lines are snap targets.
        public Vector2 Size { get; set; } = new Vector2(800, 450);

        /// Null means every item.
        public Func<T, bool> Selectable { get; set; }
        public Func<T, bool> Movable { get; set; }
        public Func<T, bool> Resizable { get; set; }

        /// Snap targets only (another tab's items): never hit, selected or moved. A hidden item is not a ghost: it
        /// stays in Items, drawn dim, so it can still be selected and shown again.
        public Func<IReadOnlyList<Rect>> Ghosts { get; set; }

        /// In canvas units: the move grid, a line snap's reach, a size snap's reach, the smallest size, and how far
        /// from the origin a position may go.
        public float Grid { get; set; } = 5f;
        public float SnapDistance { get; set; } = 8f;
        public float SizeSnap { get; set; } = 4f;
        public float MinSize { get; set; } = 10f;
        public float Limit { get; set; } = 8000f;

        public bool Snap { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowGuides { get; set; } = true;

        /// The pane around the stage, a Controls.Preview (Note, Link, Show, Hide, MinHeight...). Aspect starts at
        /// Size.x / Size.y and MinHeight at 200 before this runs.
        public Action<PreviewOptions> Pane { get; set; }
    }

    /* A stage the consumer draws its items on, with selection, marquee, snapping moves and resize handles; Facet draws
       only the chrome over them. Each press-to-release is one undo step that holds keys and rects, never the items
       or the canvas: an undo re-finds each item by key, the canvas of its name commits inside the apply, and every
       other canvas on a panel re-reads after it.

       A drag frame reads and writes through cached delegates and reused arrays, so it allocates nothing once its
       arrays have grown to the selection. */
    public sealed partial class Canvas<T> where T : class
    {
        public VisualElement Root { get; }

        /// Takes the pointer; its local coordinates are stage px.
        public VisualElement Stage { get; }

        /// The consumer's layer, in canvas units: scaled by the zoom and moved by the pan.
        public VisualElement Content { get; }

        public IReadOnlyList<T> Selection => _view;

        /// The item a drag snaps by: the last one pressed or selected.
        public T Primary
        {
            get
            {
                if (_sel.Count == 0) return null;
                return _primary != null && IndexOf(_sel, _primary) >= 0 ? _primary : _sel[_sel.Count - 1];
            }
        }

        /// Grows by one on every SelectionChanged.
        public int SelectionVersion { get; private set; }

        public bool Snap { get; set; }
        public bool ShowGrid { get { return _showGrid; } set { _showGrid = value; _chrome.MarkDirtyRepaint(); } }
        public bool ShowGuides { get { return _showGuides; } set { _showGuides = value; _chrome.MarkDirtyRepaint(); } }

        public event Action SelectionChanged;

        /// Every frame of a drag, and once after every undo or redo while on a panel: redraw the items. On the canvas the
        /// step was recorded on (matched by name: Id, else label) it comes inside the apply, just before Committed, with
        /// History.Applying true; on every other canvas after the apply, with Applying false.
        public event Action Changed;

        /// The end of a gesture that changed something: save. Also once per undo or redo of a step recorded under this
        /// canvas's name while it is on a panel, raised inside the apply with History.Applying true, as a row's undo
        /// commits its row; off a panel the undo only writes through SetRect.
        public event Action Committed;

        private enum Mode { None, PressItem, PressHandle, PressEmpty, Move, Resize, Band }

        private static readonly T[] Empty = new T[0];

        private readonly Host _host;
        private readonly string _name;
        private readonly Func<IReadOnlyList<T>> _itemsOf;
        private readonly Func<T, string> _keyOf;
        private readonly Func<string, T> _find;
        private readonly Func<T, Rect> _getRect;
        private readonly Action<T, Rect> _setRect;
        private readonly Func<T, bool> _selectable, _movable, _resizable;
        private readonly Func<IReadOnlyList<Rect>> _ghosts;
        private readonly Func<object> _document;
        private readonly Func<string, string> _translate;
        private readonly Vector2 _size;
        private readonly float _grid, _snapDistance, _sizeSnap, _minSize, _limit;
        private readonly bool _undoable;
        private readonly string _siteItems, _siteGet, _siteSet, _siteKey, _siteSelectable, _siteMovable, _siteResizable, _siteGhosts,
                                _siteChanged, _siteCommitted, _siteSelection;

        // Cached calls into the consumer: Safe with no closure per call. _cur, _rect and _flag carry the arguments.
        private readonly Action _read, _write, _ask, _readKey, _cancel, _lost;
        private readonly Action<string> _applied;
        private Func<T, bool> _asking;
        private T _cur;
        private Rect _rect;
        private bool _flag;
        private string _gotKey;

        private IReadOnlyList<T> _items = Empty;
        private string[] _itemKeys = new string[0];
        private readonly List<T> _sel = new List<T>();
        private readonly ReadOnlyCollection<T> _view;
        private readonly List<T> _scratch = new List<T>(), _bandBase = new List<T>();
        private T _primary;
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _showGrid, _showGuides, _attached;
        private Surface _owner;
        private readonly Action<Cursors.Kind> _cursor;

        // The gesture: from press to release.
        private Mode _mode;
        private int _pointer, _handle, _latch;
        private bool _additive, _pressWasSelected;
        private T _press;
        private Vector2 _downStage, _downCanvas;
        private T[] _movers = new T[8];
        private Rect[] _start = new Rect[8];
        private int _count;
        private Box _primaryStart;
        private Box[] _targets = new Box[16];
        private int _targetCount;
        private float[] _widths = new float[16], _heights = new float[16];
        private int _sizeCount;
        private MoveSnap _moveSnap;
        private SizeSnap _sizeSnapping;

        internal Canvas(VisualElement parent, string label, CanvasOptions<T> o)
        {
            _host = Host.For(parent);
            _name = o.Id ?? label ?? "";   // never null: CanvasAll passes null for the pass after an apply
            _itemsOf = o.Items; _keyOf = o.Key; _find = o.Find; _getRect = o.GetRect; _setRect = o.SetRect;
            _selectable = o.Selectable; _movable = o.Movable; _resizable = o.Resizable; _ghosts = o.Ghosts;
            _size = o.Size; _grid = o.Grid; _snapDistance = o.SnapDistance; _sizeSnap = o.SizeSnap; _minSize = o.MinSize; _limit = o.Limit;
            Snap = o.Snap; _showGrid = o.ShowGrid; _showGuides = o.ShowGuides;
            _undoable = o.Undoable;
            _document = o.UndoDocument;
            _translate = Own(_host);
            string site = "Canvas '" + _name + "' ";
            _siteItems = site + "Items"; _siteGet = site + "GetRect"; _siteSet = site + "SetRect"; _siteKey = site + "Key";
            _siteSelectable = site + "Selectable"; _siteMovable = site + "Movable"; _siteResizable = site + "Resizable";
            _siteGhosts = site + "Ghosts"; _siteChanged = site + "Changed"; _siteCommitted = site + "Committed";
            _siteSelection = site + "SelectionChanged";
            _read = () => _rect = _getRect(_cur);
            _write = () => _setRect(_cur, _rect);
            _ask = () => _flag = _asking(_cur);
            _readKey = () => _gotKey = _keyOf(_cur);
            _cancel = Cancel;
            _applied = Applied;
            _lost = Lost;
            _view = _sel.AsReadOnly();

            var sz = Theme.For(parent).Sizes;   // read once: a drag never asks the theme
            _handleSize = sz.CanvasHandle; _edgeLong = sz.CanvasEdgeLong; _edgeShort = sz.CanvasEdgeShort;
            _hitSize = sz.CanvasHit; _outline = sz.CanvasOutline; _line = sz.Line;

            var size = _size;
            var pane = Controls.Preview(parent, label, p =>
            {
                p.Id = o.Id;
                p.Tooltip = o.Tooltip; p.TooltipText = o.TooltipText;
                p.EnabledWhen = o.EnabledWhen; p.VisibleWhen = o.VisibleWhen;
                p.ContextMenu = o.ContextMenu;
                p.Aspect = size.y > 0f ? size.x / size.y : 16f / 9f;
                p.MinHeight = 200f;
                if (o.Pane != null) o.Pane(p);
            });
            Root = pane.parent;

            // The preview's stage has a border; this fills inside it, so a stage point is measured from the inner edge.
            Stage = new VisualElement();
            Fill(Stage);
            pane.Add(Stage);
            Content = new VisualElement { pickingMode = PickingMode.Ignore };
            Content.style.position = Position.Absolute;
            Content.style.left = 0f; Content.style.top = 0f;
            Content.style.width = _size.x; Content.style.height = _size.y;
            Content.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);
            Stage.Add(Content);
            _chrome = new VisualElement { pickingMode = PickingMode.Ignore };
            Fill(_chrome);
            _chrome.generateVisualContent += Draw;
            Paint.Repaints(_chrome);
            Stage.Add(_chrome);
            _badge = Badge(_chrome, sz);
            View();

            Events.On<PointerDownEvent>(Stage, Down);
            Events.On<PointerMoveEvent>(Stage, Moved);
            Events.On<PointerUpEvent>(Stage, Up);
            Events.On<PointerCaptureOutEvent>(Stage, _ => Lost());
            Events.On<AttachToPanelEvent>(Stage, _ => Attach());
            Events.On<DetachFromPanelEvent>(Stage, _ => Detach());
            _cursor = Cursors.Follow(Stage);

            ReadItems();
            Redraw();
            if (Stage.panel != null) Attach();
        }

        private static Func<string, string> Own(Host h) { return s => Words.Own(s, h); }

        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0f; e.style.top = 0f; e.style.right = 0f; e.style.bottom = 0f;
        }

        public void Select(IEnumerable<T> items, bool additive = false)
        {
            _scratch.Clear();
            if (additive) _scratch.AddRange(_sel);
            if (items != null)
                foreach (var it in items)
                    if (it != null && IndexOf(_scratch, it) < 0 && Is(_selectable, it, _siteSelectable)) { _scratch.Add(it); _primary = it; }
            SetSelection(_scratch);
        }

        public void SelectAll()
        {
            ReadItems();
            _scratch.Clear();
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null && Is(_selectable, _items[i], _siteSelectable)) _scratch.Add(_items[i]);
            SetSelection(_scratch);
        }

        public void Clear()
        {
            _scratch.Clear();
            SetSelection(_scratch);
        }

        /// Re-read Items and the rects, keep the selection by key (now as the new objects), drop vanished keys, and
        /// redraw the chrome. SelectionChanged fires once if the selection changed. Call it after changing the items
        /// or their rects outside the canvas; a drag in progress drops the items that went.
        public void Refresh()
        {
            if (Reread()) SelectionDidChange(); else Redraw();
        }

        // ---- for the autotest: what the chrome last drew

        internal IReadOnlyList<Rect> HandleRects => _handles;

        /// NaN on an axis with no guide.
        internal Vector2 Guides => _showGuides ? _guides : new Vector2(float.NaN, float.NaN);

        internal Rect? Marquee => _banding ? _band : (Rect?)null;

        internal string BadgeText => _badgeText;

        /// The accent the outlines and handles were last painted in.
        internal Color DrawnAccent => _drawnAccent;

        // ---- reading the consumer

        private void ReadItems()
        {
            _items = Safe.Get(_host, _siteItems, _itemsOf ?? NoItems, null) ?? Empty;
            if (_keyOf == null) return;
            if (_itemKeys.Length < _items.Count) _itemKeys = new string[Math.Max(_items.Count, _itemKeys.Length * 2)];
            for (int i = 0; i < _items.Count; i++) _itemKeys[i] = KeyOf(_items[i]);
        }

        private static readonly Func<IReadOnlyList<T>> NoItems = () => Empty;

        private bool TryRect(T item, out Rect r)
        {
            r = default(Rect);
            if (_getRect == null || item == null) return false;
            _cur = item;
            if (!Safe.Run(_host, _siteGet, _read)) return false;
            r = _rect;
            return true;
        }

        private void Put(T item, Rect r)
        {
            if (_setRect == null) return;
            _cur = item; _rect = r;
            Safe.Run(_host, _siteSet, _write);
        }

        // A null predicate is true; a throwing one is false.
        private bool Is(Func<T, bool> f, T item, string site)
        {
            if (f == null) return true;
            _asking = f; _cur = item;
            return Safe.Run(_host, site, _ask) && _flag;
        }

        private string KeyOf(T item)
        {
            _cur = item; _gotKey = null;
            return Safe.Run(_host, _siteKey, _readKey) ? _gotKey : null;
        }

        // The item Items() now holds for `old`: the same key, or the same reference without a Key. Null once it went.
        private T Locate(T old)
        {
            if (_keyOf == null) return IndexOf(_items, old) >= 0 ? old : null;
            string k = KeyOf(old);
            if (k == null) return null;
            for (int i = 0; i < _items.Count; i++) if (_itemKeys[i] == k) return _items[i];
            return null;
        }

        // By reference: a consumer's Equals must not merge two items.
        private static int IndexOf(IReadOnlyList<T> l, T x)
        {
            for (int i = 0; i < l.Count; i++) if (ReferenceEquals(l[i], x)) return i;
            return -1;
        }

        /* Items again; the selection and a drag's items follow by key. True when the selection changed: an item went,
           or came back as a new object. */
        private bool Reread()
        {
            ReadItems();
            bool changed = false;
            for (int i = _sel.Count - 1; i >= 0; i--)
            {
                var now = Locate(_sel[i]);
                if (now == null) { _sel.RemoveAt(i); changed = true; }
                else if (!ReferenceEquals(now, _sel[i])) { _sel[i] = now; changed = true; }
            }
            if (_primary != null) _primary = Locate(_primary);
            if (_press != null) _press = Locate(_press);
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                var now = Locate(_movers[i]);
                if (now == null) continue;
                _movers[n] = now; _start[n] = _start[i]; n++;
            }
            for (int i = n; i < _count; i++) _movers[i] = null;
            _count = n;
            return changed;
        }

        // ---- selection

        private void SetSelection(List<T> next)
        {
            bool same = next.Count == _sel.Count;
            for (int i = 0; i < next.Count && same; i++) same = ReferenceEquals(next[i], _sel[i]);
            if (same) { Redraw(); return; }
            _sel.Clear();
            _sel.AddRange(next);
            SelectionDidChange();
        }

        private void SelectionDidChange()
        {
            SelectionVersion++;
            Raise(SelectionChanged, _siteSelection);
            Redraw();
        }

        private void Raise(Action a, string site) { if (a != null) Safe.Run(_host, site, a); }

        // ---- the view (zoom 1 and pan 0 until the view is added)

        private Vector2 ToCanvas(Vector2 stage) { return (stage - _pan) / _zoom; }

        private Rect ToStage(Rect r) { return new Rect(r.x * _zoom + _pan.x, r.y * _zoom + _pan.y, r.width * _zoom, r.height * _zoom); }

        private void View()
        {
            Content.style.translate = new Translate(_pan.x, _pan.y, 0f);
            Content.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
            _badgeText = Mathf.RoundToInt(_zoom * 100f) + "%";
            _badge.text = _badgeText;
            _chrome.MarkDirtyRepaint();
        }

        // The topmost selectable item under a canvas point.
        private T ItemAt(Vector2 c)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                Rect r;
                if (it != null && TryRect(it, out r) && r.Contains(c) && Is(_selectable, it, _siteSelectable)) return it;
            }
            return null;
        }

        // ---- the pointer

        private void Down(PointerDownEvent e)
        {
            if (e.button != 0 || _mode != Mode.None || !Stage.enabledInHierarchy) return;
            Refresh();
            var s = (Vector2)Stage.WorldToLocal(e.position);
            _downStage = s; _downCanvas = ToCanvas(s);
            _additive = e.ctrlKey || e.commandKey || e.shiftKey;
            _press = null; _latch = 0; _count = 0;
            int h = HandleAt(s);
            if (h >= 0) { _mode = Mode.PressHandle; _handle = h; _press = _sel[0]; }
            else
            {
                var hit = ItemAt(_downCanvas);
                if (hit != null)
                {
                    _mode = Mode.PressItem;
                    _press = _primary = hit;
                    _pressWasSelected = IndexOf(_sel, hit) >= 0;
                    // Selected at once, so the drag that follows moves it.
                    if (!_pressWasSelected)
                    {
                        _scratch.Clear();
                        if (_additive) _scratch.AddRange(_sel);
                        _scratch.Add(hit);
                        SetSelection(_scratch);
                    }
                }
                else
                {
                    _mode = Mode.PressEmpty;
                    _bandBase.Clear();
                    if (_additive) _bandBase.AddRange(_sel);
                }
            }
            _pointer = e.pointerId;
            Stage.CapturePointer(e.pointerId);
            Input.Gesture = _cancel;
        }

        private void Moved(PointerMoveEvent e)
        {
            var s = (Vector2)Stage.WorldToLocal(e.position);
            if (_mode == Mode.None) { if (Stage.enabledInHierarchy) Hover(s); return; }
            if (e.pointerId != _pointer) return;
            float travel = (s - _downStage).magnitude;
            switch (_mode)
            {
                case Mode.PressItem:
                    if (travel < 3f) return;
                    BeginMove();
                    _mode = Mode.Move;
                    break;
                case Mode.PressHandle:
                    if (travel < 3f) return;
                    BeginResize();
                    _mode = Mode.Resize;
                    break;
                case Mode.PressEmpty:
                    if (travel < 5f) return;
                    _mode = Mode.Band;
                    break;
            }
            var c = ToCanvas(s);
            if (_mode == Mode.Move) DoMove(c, e.altKey, e.shiftKey);
            else if (_mode == Mode.Resize) DoResize(c, e.altKey, e.shiftKey);
            else if (_mode == Mode.Band) DoBand(s);
        }

        private void Up(PointerUpEvent e)
        {
            if (e.button != 0 || _mode == Mode.None || e.pointerId != _pointer) return;
            Finish();
        }

        // Capture lost, the surface hidden, the stage off its panel: the gesture ends as a release does.
        private void Lost()
        {
            if (_mode != Mode.None) Finish();
        }

        private void Hover(Vector2 s)
        {
            int h = HandleAt(s);
            Cursors.Kind k;
            if (h == 0 || h == 7) k = Cursors.Kind.ResizeCorner;
            else if (h == 2 || h == 5) k = Cursors.Kind.ResizeCornerNESW;
            else if (h == 3 || h == 4) k = Cursors.Kind.ResizeHorizontal;
            else if (h == 1 || h == 6) k = Cursors.Kind.ResizeVertical;
            else
            {
                var it = ItemAt(ToCanvas(s));
                k = it != null && Is(_movable, it, _siteMovable) ? Cursors.Kind.Move : Cursors.Kind.Default;
            }
            _cursor(k);
        }

        // The handle under a stage point, corners first (they overlap the edges on a small box); -1 for none.
        private int HandleAt(Vector2 s)
        {
            if (_handles.Count != 8) return -1;
            float half = _hitSize * 0.5f;
            for (int k = 0; k < 8; k++)
            {
                int i = Order[k];
                var c = _handles[i].center;
                if (Mathf.Abs(s.x - c.x) <= half && Mathf.Abs(s.y - c.y) <= half) return i;
            }
            return -1;
        }

        private static readonly int[] Order = { 0, 2, 5, 7, 1, 3, 4, 6 };

        // ---- move and resize

        private void BeginMove()
        {
            _count = 0;
            for (int i = 0; i < _sel.Count; i++)
            {
                var it = _sel[i];
                Rect r;
                if (Is(_movable, it, _siteMovable) && TryRect(it, out r)) AddMover(it, r);
            }
            Rect p;
            if (!TryRect(_press, out p)) p = _count > 0 ? _start[0] : default(Rect);
            _primaryStart = new Box(p.x, p.y, p.width, p.height);
            _targetCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                Rect r;
                if (it != null && IndexOf(_sel, it) < 0 && TryRect(it, out r)) AddTarget(r);
            }
            var g = _ghosts == null ? null : Safe.Get(_host, _siteGhosts, _ghosts, null);
            if (g != null) for (int i = 0; i < g.Count; i++) AddTarget(g[i]);
        }

        private void BeginResize()
        {
            _count = 0;
            Rect r;
            if (_press == null || !Is(_resizable, _press, _siteResizable) || !TryRect(_press, out r)) return;
            AddMover(_press, r);
            _primaryStart = new Box(r.x, r.y, r.width, r.height);
            _sizeCount = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                Rect o;
                if (it == null || ReferenceEquals(it, _press) || !TryRect(it, out o)) continue;
                if (_sizeCount == _widths.Length) { Array.Resize(ref _widths, _sizeCount * 2); Array.Resize(ref _heights, _sizeCount * 2); }
                _widths[_sizeCount] = o.width; _heights[_sizeCount] = o.height; _sizeCount++;
            }
        }

        private void AddMover(T it, Rect r)
        {
            if (_count == _movers.Length) { Array.Resize(ref _movers, _count * 2); Array.Resize(ref _start, _count * 2); }
            _movers[_count] = it; _start[_count] = r; _count++;
        }

        private void AddTarget(Rect r)
        {
            if (_targetCount == _targets.Length) Array.Resize(ref _targets, _targetCount * 2);
            _targets[_targetCount++] = new Box(r.x, r.y, r.width, r.height);
        }

        private void DoMove(Vector2 c, bool alt, bool shift)
        {
            float rawX = c.x - _downCanvas.x, rawY = c.y - _downCanvas.y;
            // Shift keeps the axis of the first move it is held for; let go, both move again.
            if (!shift) _latch = 0;
            else if (_latch == 0) _latch = CanvasMath.LatchAxis(rawX, rawY);
            _moveSnap.Targets = _targets; _moveSnap.Count = _targetCount;
            _moveSnap.CentreX = _size.x * 0.5f; _moveSnap.CentreY = _size.y * 0.5f;
            _moveSnap.Distance = _snapDistance; _moveSnap.Step = CanvasMath.GridStep(_grid, _zoom);
            _moveSnap.On = Snap && !alt; _moveSnap.Keep = _latch;
            float dx, dy, gx, gy;
            CanvasMath.Move(_primaryStart, rawX, rawY, ref _moveSnap, out dx, out dy, out gx, out gy);
            for (int i = 0; i < _count; i++)
            {
                var r = _start[i];
                r.x = CanvasMath.Clamp(r.x + dx, _limit);
                r.y = CanvasMath.Clamp(r.y + dy, _limit);
                Put(_movers[i], r);
            }
            _guides = new Vector2(gx, gy);
            Raise(Changed, _siteChanged);
            Redraw();
        }

        private void DoResize(Vector2 c, bool alt, bool shift)
        {
            if (_count == 0) return;
            _sizeSnapping.Widths = _widths; _sizeSnapping.Heights = _heights; _sizeSnapping.Count = _sizeCount;
            _sizeSnapping.Distance = _sizeSnap; _sizeSnapping.Min = _minSize; _sizeSnapping.Step = CanvasMath.GridStep(_grid, _zoom);
            _sizeSnapping.On = Snap && !alt; _sizeSnapping.KeepAspect = shift;
            var b = CanvasMath.Resize(_primaryStart, _handle, c.x - _downCanvas.x, c.y - _downCanvas.y, ref _sizeSnapping);
            Put(_movers[0], new Rect(CanvasMath.Clamp(b.X, _limit), CanvasMath.Clamp(b.Y, _limit), b.W, b.H));
            Raise(Changed, _siteChanged);
            Redraw();
        }

        // Every selectable item the band overlaps, after the selection held at press when a modifier was.
        private void DoBand(Vector2 s)
        {
            var a = _downStage;
            _band = Rect.MinMaxRect(Mathf.Min(a.x, s.x), Mathf.Min(a.y, s.y), Mathf.Max(a.x, s.x), Mathf.Max(a.y, s.y));
            _banding = true;
            Vector2 lo = ToCanvas(_band.min), hi = ToCanvas(_band.max);
            var band = new Box(lo.x, lo.y, hi.x - lo.x, hi.y - lo.y);
            _scratch.Clear();
            _scratch.AddRange(_bandBase);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                Rect r;
                if (it == null || IndexOf(_scratch, it) >= 0 || !TryRect(it, out r)) continue;
                if (CanvasMath.Hits(band, new Box(r.x, r.y, r.width, r.height)) && Is(_selectable, it, _siteSelectable)) _scratch.Add(it);
            }
            SetSelection(_scratch);
        }

        // ---- the end of a gesture

        // State first, then Input.Gesture, and the capture last: the capture-out that follows finds no gesture.
        private Mode Stop()
        {
            var mode = _mode;
            _mode = Mode.None;
            if (Input.Gesture == _cancel) Input.Gesture = null;
            _guides = new Vector2(float.NaN, float.NaN);
            _banding = false;
            return mode;
        }

        private void Release()
        {
            if (Stage.panel != null && Stage.HasPointerCapture(_pointer)) Stage.ReleasePointer(_pointer);
        }

        private void Finish()
        {
            var mode = Stop();
            if (mode == Mode.PressItem && _pressWasSelected && _press != null)
            {
                // A click on a selected item: alone it keeps just that item, with a modifier it leaves the selection.
                _scratch.Clear();
                if (_additive) { _scratch.AddRange(_sel); int i = IndexOf(_scratch, _press); if (i >= 0) _scratch.RemoveAt(i); }
                else _scratch.Add(_press);
                SetSelection(_scratch);
            }
            else if (mode == Mode.PressEmpty && !_additive) Clear();
            else if (mode == Mode.Move || mode == Mode.Resize) Commit(mode == Mode.Move ? "Move" : "Resize");
            _count = 0;
            _press = null;
            Redraw();
            Release();
        }

        // Input.Gesture: the items go back where they started and nothing is recorded.
        private void Cancel()
        {
            var mode = Stop();
            if (mode == Mode.None) return;
            if (mode == Mode.Move || mode == Mode.Resize)
            {
                for (int i = 0; i < _count; i++) Put(_movers[i], _start[i]);
                Raise(Changed, _siteChanged);
            }
            _count = 0;
            _press = null;
            Redraw();
            Release();
        }

        /* One step over the items that are still there and moved. A gesture that changed nothing records nothing and
           does not commit. The step holds keys (or, with no Key, the items) and rects: never this canvas. */
        private void Commit(string label)
        {
            if (Reread()) SelectionDidChange();
            int n = 0;
            var after = new Rect[_count];
            for (int i = 0; i < _count; i++)
            {
                Rect r;
                if (TryRect(_movers[i], out r) && r != _start[i]) { after[i] = r; n++; }
                else after[i] = _start[i];
            }
            if (n == 0) return;
            if (_undoable)
            {
                var p = new Part { Items = _itemsOf, Key = _keyOf, Find = _find, Set = _setRect, Host = _host, Name = _name,
                                   Site = "Canvas '" + _name + "' " + label, Before = new Rect[n], After = new Rect[n] };
                if (_keyOf != null) p.Keys = new string[n]; else p.Refs = new T[n];
                for (int i = 0, k = 0; i < _count; i++)
                {
                    if (after[i] == _start[i]) continue;
                    if (p.Keys != null) p.Keys[k] = KeyOf(_movers[i]); else p.Refs[k] = _movers[i];
                    p.Before[k] = _start[i]; p.After[k] = after[i]; k++;
                }
                string key = "canvas/" + _name + "#" + (++CanvasAll.Gestures);
                var valid = _document == null ? null : History.BoundTo(_document);
                Action undo, redo;
                Steps(p, out undo, out redo);
                using (History.Step(label, key, valid, _translate)) History.Record(label, key, undo, redo, _translate);
            }
            Raise(Committed, _siteCommitted);
        }

        // The closures live here, over the part alone: a step must not keep the canvas or its page alive.
        private static void Steps(Part p, out Action undo, out Action redo)
        {
            undo = () => p.Apply(p.Before);
            redo = () => p.Apply(p.After);
        }

        /* A step's items and rects. Apply finds each item again by key (through Find, else in Items()), or by
           reference with no Key, skips one that went, then has the canvases of its name commit (CanvasAll.Applied). */
        private sealed class Part
        {
            public Func<IReadOnlyList<T>> Items;
            public Func<T, string> Key;
            public Func<string, T> Find;
            public Action<T, Rect> Set;
            public Host Host;
            public string Name, Site;
            public string[] Keys;
            public T[] Refs;
            public Rect[] Before, After;

            public void Apply(Rect[] to)
            {
                var items = Items == null ? null : Safe.Get(Host, Site, Items, null);
                Dictionary<string, T> byKey = null;
                for (int i = 0; i < to.Length; i++)
                {
                    T it = null;
                    if (Keys == null) it = items != null && IndexOf(items, Refs[i]) >= 0 ? Refs[i] : null;
                    else if (Find != null) { string k = Keys[i]; it = Safe.Get(Host, Site, () => Find(k), null); }
                    else
                    {
                        if (byKey == null) byKey = Index(items);
                        if (Keys[i] != null) byKey.TryGetValue(Keys[i], out it);
                    }
                    if (it == null || Set == null) continue;
                    var r = to[i];
                    Safe.Run(Host, Site, () => Set(it, r));
                }
                CanvasAll.Applied(Name);
            }

            private Dictionary<string, T> Index(IReadOnlyList<T> items)
            {
                var d = new Dictionary<string, T>();
                if (items == null) return d;
                foreach (var it in items)
                {
                    if (it == null) continue;
                    var k = Safe.Get(Host, Site, () => Key(it), null);
                    if (k != null && !d.ContainsKey(k)) d[k] = it;
                }
                return d;
            }
        }

        // ---- on a panel

        private void Attach()
        {
            if (_attached) return;
            _attached = true;
            CanvasAll.Live.Add(_applied);
            _owner = Popup.OwnerOf(Stage);
            if (_owner != null) _owner.Hidden += _lost;
            Refresh();
        }

        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            Lost();
            CanvasAll.Live.Remove(_applied);
            if (_owner != null) _owner.Hidden -= _lost;
            _owner = null;
        }

        /* After an undo or redo. With a name, inside the apply (History.Applying true), as an undo commits its row: the
           canvas the step was recorded on re-reads, raises Changed, then Committed. With null, after the apply: every
           other canvas re-reads and raises Changed. */
        private void Applied(string name)
        {
            if (name == null ? CanvasAll.Touched.Contains(_name) : name != _name) return;
            Refresh();
            Raise(Changed, _siteChanged);
            if (name != null) Raise(Committed, _siteCommitted);
        }
    }

    /* What every canvas shares: the gesture count that keeps two gestures' steps apart (a rebuilt page's canvas
       counts on from the last), and the re-read after each undo or redo, which reaches every canvas on a panel,
       not only the one that recorded the step (it may be gone with its page, or show another tab). */
    internal static class CanvasAll
    {
        internal static int Gestures;
        internal static readonly List<Action<string>> Live = new List<Action<string>>();
        internal static readonly HashSet<string> Touched = new HashSet<string>();

        static CanvasAll() { History.RefreshAll += After; }

        // A step's part has written its rects: the canvases of its name commit now, while History.Applying is true.
        internal static void Applied(string name)
        {
            Touched.Add(name);
            Each(name);
        }

        private static void After()
        {
            try { Each(null); }
            finally { Touched.Clear(); }
        }

        // A copy: a Changed handler may rebuild a page, which takes its canvas off the list.
        private static void Each(string name) { foreach (var a in Live.ToArray()) a(name); }
    }

    public static partial class Controls
    {
        /// A stage of the consumer's items with selection, moves and resize handles; see CanvasOptions and the README.
        public static Canvas<T> Canvas<T>(VisualElement parent, string label, Action<CanvasOptions<T>> configure) where T : class
        {
            return new Canvas<T>(parent, label, ControlOptions.Make(parent, label, configure));
        }
    }
}
