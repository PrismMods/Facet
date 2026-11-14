using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// What a CardGrid shows and what it calls. Of the inherited options only Id and Tooltip apply.
    public sealed class CardGridOptions<T> : ControlOptions where T : class
    {
        public Func<IReadOnlyList<T>> Items { get; set; }
        /// Identity across refreshes: a list returning new objects under the same keys keeps its cards. Null keys by the item.
        public Func<T, string> Key { get; set; }
        /// Shown as returned (data, not a word), re-read on every refresh pass.
        public Func<T, string> Title { get; set; }
        public Func<T, string> Note { get; set; }
        /// The ● in the accent colour.
        public Func<T, bool> Active { get; set; }
        /// Draws into the card's emptied thumbnail box, on build and on Redraw(item). A throw leaves it empty.
        public Action<VisualElement, T> Thumb { get; set; }
        /// The thumbnail box's width over its height.
        public float ThumbAspect { get; set; } = 16f / 9f;
        /// A click, at once; a second click on the same card within 0.3 s also calls DoubleClick.
        public Action<T> Click { get; set; }
        public Action<T> DoubleClick { get; set; }
        /// A right-click's menu, opened at the pointer.
        public Func<T, IList<MenuItem>> Menu { get; set; }
        /// With Add: a trailing "＋ AddLabel" card (AddLabel an English key) whose menu opens under it.
        public string AddLabel { get; set; }
        public Func<IList<MenuItem>> Add { get; set; }
        /// Null: no rename. BeginRename swaps the title for a field; Enter commits a changed, non-blank name here.
        public Action<T, string> Rename { get; set; }
    }

    /* Cards that wrap, Sizes.CardWidth wide: a thumbnail the consumer draws, a title with the active mark, and a
       note. Cards are rebuilt only when the keys change; titles, notes and marks are re-read on every refresh pass of
       the surface (and on Refresh()), allocating nothing when nothing changed. */
    public sealed class CardGrid<T> where T : class
    {
        public VisualElement Root { get; }

        private readonly CardGridOptions<T> _o;
        private readonly Host _host;
        private readonly Sizes _sz;
        private readonly string _site;
        private readonly Action _pass;
        private readonly VisualElement _add;
        private readonly List<VisualElement> _cards = new List<VisualElement>(), _thumbs = new List<VisualElement>();
        private readonly List<Label> _titles = new List<Label>(), _notes = new List<Label>(), _marks = new List<Label>();
        private readonly List<T> _items = new List<T>();
        private readonly List<object> _keys = new List<object>();
        private readonly List<bool> _act = new List<bool>();
        private Refresher _joined;
        private bool _passing;
        private int _renaming = -1, _pressed = -1, _lastClick = -1;
        private bool _addPressed;
        private float _lastAt;
        private Action _cancelRename;

        internal CardGrid(VisualElement parent, CardGridOptions<T> o)
        {
            _o = o;
            _site = "CardGrid '" + (o.Id ?? "CardGrid") + "'";
            _pass = Pass;
            Root = Ui.Row(parent);
            Root.name = o.Id ?? "cardgrid";
            Root.style.flexWrap = Wrap.Wrap;
            Root.style.alignItems = Align.FlexStart;
            Root.style.flexShrink = 1f;
            Root.style.minWidth = 0f;
            if (o.Tooltip != null) Words.Tip(Root, o.Tooltip);
            _host = Host.For(Root);
            _sz = Theme.For(Root).Sizes;

            if (o.Add != null)
            {
                _add = Card();
                _add.name = Root.name + "-add";
                _add.style.minHeight = ThumbHeight() + _sz.ListRowHeight;
                _add.style.justifyContent = Justify.Center;
                var line = Ui.Row(_add);
                line.style.justifyContent = Justify.Center;
                line.pickingMode = PickingMode.Ignore;
                Text(line, "＋", "-add-plus", _sz.Body, Role.TextMuted).style.marginRight = _sz.Gap / 2f;
                var text = Text(line, "", "-add-text", _sz.Body, Role.TextMuted);
                Words.Label(text, o.AddLabel ?? "");   // the consumer's word: its host translates and relabels it
                // A click: the press and its release both on the add card.
                Events.On<PointerDownEvent>(_add, e =>
                {
                    if (e.button != 0) return;
                    _addPressed = true;
                    _add.CapturePointer(e.pointerId);
                    e.StopPropagation();
                });
                Events.On<PointerUpEvent>(_add, e =>
                {
                    if (e.button != 0 || !_addPressed) return;
                    _addPressed = false;
                    if (_add.HasPointerCapture(e.pointerId)) _add.ReleasePointer(e.pointerId);
                    if (!_add.worldBound.Contains(e.position)) return;
                    var items = Safe.Get(_host, _site + " Add", o.Add, null);
                    if (items != null && items.Count > 0) Popup.Menu(_add, items);
                });
                Events.On<PointerCaptureOutEvent>(_add, _ => _addPressed = false);
            }

            Events.On<AttachToPanelEvent>(Root, _ => Join());
            Events.On<DetachFromPanelEvent>(Root, _ => { if (_joined != null) _joined.Remove(_pass); _joined = null; });
            Refresh();
            if (Root.panel != null) Join();
        }

        /// Re-reads the items now: rebuilt when their keys changed, else their titles, notes and marks.
        public void Refresh() { Safe.Run(_host, _site, _pass); }

        /// Empties the item's thumbnail box and calls Thumb for it again.
        public void Redraw(T item)
        {
            int i = IndexOf(item);
            if (i >= 0) DrawThumb(i);
        }

        /// Swaps the item's title for a field holding it (no Rename: nothing happens).
        public void BeginRename(T item)
        {
            if (_o.Rename == null || _renaming >= 0) return;
            Refresh();
            int i = IndexOf(item);
            if (i < 0) return;
            var it = _items[i];
            _renaming = i;
            _cancelRename = Controls.Rename(_titles[i], Root.name + "-rename", _titles[i].text, _host, _site + " Rename",
                name => _o.Rename(it, name), () => { _renaming = -1; _cancelRename = null; Refresh(); });
        }

        private int IndexOf(T item)
        {
            int i = _items.IndexOf(item);
            if (i < 0 && item != null && _o.Key != null)
            {
                object k = Safe.Get(_host, _site + " Key", () => _o.Key(item), null);
                i = k == null ? -1 : _keys.IndexOf(k);
            }
            return i;
        }

        private void Join()
        {
            if (_joined != null) return;
            _joined = Refresher.Of(Root);
            if (_joined != null) _joined.Add(_pass, _site);
        }

        // Under the refresher's Safe (or Refresh's): the consumer's delegates are called directly, Thumb guarded.
        private void Pass()
        {
            if (_passing) return;
            _passing = true;
            try
            {
                var items = _o.Items != null ? _o.Items() : null;
                int n = items == null ? 0 : items.Count;
                bool same = n == _keys.Count;
                for (int i = 0; same && i < n; i++) same = Equals(KeyOf(items[i]), _keys[i]);
                if (!same) Rebuild(items, n);
                for (int i = 0; i < n; i++)
                {
                    var it = _items[i] = items[i];   // a new object under the same key
                    if (i != _renaming) Set(_titles[i], _o.Title != null ? _o.Title(it) : null);
                    Set(_notes[i], _o.Note != null ? _o.Note(it) : null);
                    bool act = _o.Active != null && _o.Active(it);
                    if (act != _act[i]) { _act[i] = act; _marks[i].style.display = act ? DisplayStyle.Flex : DisplayStyle.None; }
                }
            }
            finally { _passing = false; }
        }

        private static void Set(Label l, string s)
        {
            s = s ?? "";
            if (!string.Equals(l.text, s)) l.text = s;
            var d = s.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (l.style.display != d) l.style.display = d;
        }

        private object KeyOf(T it) => _o.Key != null ? (object)_o.Key(it) : it;

        // The first guess, before layout: the card's width less the padding and the border on both sides.
        private float ThumbHeight() => (_sz.CardWidth - 2f * (_sz.ControlPad + _sz.Line)) / (_o.ThumbAspect > 0f ? _o.ThumbAspect : 16f / 9f);

        private VisualElement Card()
        {
            var card = Ui.Box(Root);
            card.pickingMode = PickingMode.Position;
            card.style.width = _sz.CardWidth;
            card.style.flexShrink = 0f;
            card.style.marginRight = _sz.Gap; card.style.marginBottom = _sz.Gap;
            Ui.SetPadding(card, _sz.ControlPad);
            Ui.SetBorderWidth(card, _sz.Line);
            Ui.SetRadius(card, _sz.Radius);
            Paint.Look(card, Theme.For(Root).Looks.Tab);
            return card;
        }

        private void Rebuild(IReadOnlyList<T> items, int n)
        {
            var cancel = _cancelRename;   // the open rename's item may be gone or moved: close it with no call
            _cancelRename = null;
            if (cancel != null) cancel();
            _renaming = -1; _pressed = -1; _lastClick = -1;
            foreach (var c in _cards) c.RemoveFromHierarchy();
            _cards.Clear(); _thumbs.Clear(); _titles.Clear(); _notes.Clear(); _marks.Clear(); _items.Clear(); _keys.Clear(); _act.Clear();
            var stage = Theme.For(Root).Looks.Stage;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var card = Card();
                card.name = Root.name + "-card";
                if (_add != null) _add.BringToFront();
                var thumb = new VisualElement { name = Root.name + "-thumb" };
                thumb.style.height = ThumbHeight();
                // Then from the width it was laid out at, which a window's scale and pixel snapping move off the sizes.
                Events.On<GeometryChangedEvent>(thumb, _ =>
                {
                    float h = thumb.layout.width / (_o.ThumbAspect > 0f ? _o.ThumbAspect : 16f / 9f);
                    if (h > 0f && Mathf.Abs(thumb.style.height.value.value - h) > 0.01f) thumb.style.height = h;
                });
                thumb.style.overflow = Overflow.Hidden;
                thumb.style.marginBottom = _sz.ControlPad;
                Ui.SetRadius(thumb, _sz.Radius);
                Paint.Look(thumb, stage);
                card.Add(thumb);
                var line = Ui.Row(card);
                line.style.alignItems = Align.Center;
                var mark = Text(line, "●", "-mark", _sz.Small, Role.Accent);
                mark.style.marginRight = _sz.Gap / 2f;
                mark.style.display = DisplayStyle.None;
                var title = Text(line, "", "-title", _sz.Body, Role.Text);
                title.style.flexShrink = 1f;
                title.style.minWidth = 0f;
                title.style.overflow = Overflow.Hidden;
                title.style.textOverflow = TextOverflow.Ellipsis;
                var note = Text(card, "", "-note", _sz.Small, Role.TextMuted);
                Events.On<PointerDownEvent>(card, e => Down(card, e));
                Events.On<PointerUpEvent>(card, e => Up(card, e));
                Events.On<PointerCaptureOutEvent>(card, _ => { if (_cards.IndexOf(card) == _pressed) _pressed = -1; });
                _cards.Add(card); _thumbs.Add(thumb); _titles.Add(title); _notes.Add(note); _marks.Add(mark);
                _items.Add(it); _keys.Add(KeyOf(it)); _act.Add(false);
                DrawThumb(i);
            }
        }

        // Data, not a word: set by hand, so a relabel leaves it alone.
        private Label Text(VisualElement parent, string s, string name, float size, Role role)
        {
            var l = Ui.Text(parent, "", size, role);
            l.text = s;
            l.name = Root.name + name;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private void DrawThumb(int i)
        {
            var box = _thumbs[i];
            box.Clear();
            if (_o.Thumb == null) return;
            var it = _items[i];
            if (!Safe.Run(_host, _site + " Thumb", () => _o.Thumb(box, it))) box.Clear();
        }

        /* ---- the pointer: a press and its release on the same card is a click; a right press opens the menu. The
           press captures the pointer, so its release comes back to the card wherever it lands and always ends it. */

        private void Down(VisualElement card, PointerDownEvent e)
        {
            int i = _cards.IndexOf(card);
            if (i < 0 || i == _renaming) return;   // a press in the rename field is the field's
            if (e.button == 1)
            {
                e.StopPropagation();
                if (_o.Menu == null) return;
                var it = _items[i];
                var items = Safe.Get(_host, _site + " Menu", () => _o.Menu(it), null);
                if (items != null && items.Count > 0) Popup.Context(card, card.WorldToLocal(e.position), items);
                return;
            }
            if (e.button == 0) { _pressed = i; card.CapturePointer(e.pointerId); e.StopPropagation(); }
        }

        private void Up(VisualElement card, PointerUpEvent e)
        {
            int i = _cards.IndexOf(card);
            if (e.button != 0 || i < 0 || i != _pressed) return;
            _pressed = -1;
            if (card.HasPointerCapture(e.pointerId)) card.ReleasePointer(e.pointerId);
            if (!card.worldBound.Contains(e.position)) return;   // released off the card: no click
            var it = _items[i];
            float now = Time.realtimeSinceStartup;
            bool twice = i == _lastClick && now - _lastAt <= 0.3f;
            _lastClick = twice ? -1 : i;
            _lastAt = now;
            if (_o.Click != null) Safe.Run(_host, _site + " Click", () => _o.Click(it));
            if (twice && _o.DoubleClick != null) Safe.Run(_host, _site + " DoubleClick", () => _o.DoubleClick(it));
            Refresh();
        }
    }

    public static partial class Controls
    {
        /// Cards that wrap, each a thumbnail, a title and a note: a switcher of layouts, presets or profiles.
        public static CardGrid<T> CardGrid<T>(VisualElement parent, Action<CardGridOptions<T>> configure) where T : class
            => new CardGrid<T>(parent, ControlOptions.Make(parent, "CardGrid", configure));
    }
}
