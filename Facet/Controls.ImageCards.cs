using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// What ImageCards shows and what it calls. Not a Control: there is no row, label or bind.
    public sealed class ImageCardsOptions<T> where T : class
    {
        public Func<IReadOnlyList<T>> Items { get; set; }
        /// Identity across refreshes: a kept key keeps its card element and its Body.
        public Func<T, string> Key { get; set; }
        /// The card's image; null = not ready. Asked only for attached, displayed cards near the viewport.
        public Func<T, Texture> Image { get; set; }
        /// The card's content, built once per key, only while attached.
        public Action<VisualElement, T> Body { get; set; }
        /// Null: cards are not buttons.
        public Action<T> Click { get; set; }
        /// Full-width content after the row that holds the item; null = none.
        public Func<T, Action<VisualElement>> Below { get; set; }
        /// Null or false: one card per row.
        public Func<bool> Grid { get; set; }
        public float MinWidth { get; set; } = 268f;
        public int MaxColumns { get; set; } = 6;
        public float Gap { get; set; } = 8f;
        /// The darkening over an image, 0..1.
        public float Scrim { get; set; } = 0.5f;
        /// The window around the viewport in which cards get their image; 0 = one viewport height.
        public float ImageMargin { get; set; }
    }

    /* Responsive cards over an image the consumer hands in when it is ready. Cards follow Items by Key: new keys
       build, gone keys go, kept cards move into place without a rebuild. Bodies are built only while the root is
       attached. Images are asked for only for cards inside the image window (the nearest ScrollView's viewport grown
       by ImageMargin above and below, or every card with no ScrollView); a card leaving the window drops its image. */
    public sealed class ImageCards<T> where T : class
    {
        public VisualElement Root { get; }
        public int Columns { get; private set; } = 1;

        private sealed class Card
        {
            public string Key;
            public T Item;
            public VisualElement El, Layer, Body, Below;
            public Image Img;
            public Texture Shown;
            public bool Built, InWindow, HasBelow;
        }

        private readonly ImageCardsOptions<T> _o;
        private readonly Host _host;
        private readonly Sizes _sz;
        private readonly Action _pass;
        private readonly Action<float> _scrolled;
        private readonly EventCallback<GeometryChangedEvent> _viewGeo;
        private readonly List<Card> _cards = new List<Card>();
        private readonly HashSet<string> _logged = new HashSet<string>();
        private Refresher _joined;
        private ScrollView _sv;
        private float _width = -1f;
        private bool _passing;

        internal ImageCards(VisualElement parent, ImageCardsOptions<T> o)
        {
            _o = o;
            _pass = Pass;
            _scrolled = _ => Window(false);
            _viewGeo = _ => Window(false);
            Root = new VisualElement { name = "image-cards" };
            Root.style.flexDirection = FlexDirection.Row;
            Root.style.flexWrap = Wrap.Wrap;
            Root.style.alignItems = Align.FlexStart;
            Root.style.flexShrink = 0f;
            if (parent != null) parent.Add(Root);
            _host = Host.For(Root);
            _sz = Theme.For(Root).Sizes;
            Events.On<AttachToPanelEvent>(Root, _ => Attached());
            Events.On<DetachFromPanelEvent>(Root, _ => Detached());
            Events.On<GeometryChangedEvent>(Root, _ => Geometry());
            // Added to a panel above, before these handlers: find the ScrollView first, or the first pass sees no window.
            if (Root.panel != null) Attached();
            Refresh();
        }

        /// Re-reads the items (keys), the column count and each item's Below; asks Image for new cards in the window.
        public void Refresh() { Safe.Run(_host, "ImageCards", _pass); }

        /// Rebuilds one card's Body (and its Below), while attached.
        public void Redraw(T item)
        {
            var c = Find(item);
            if (c == null) return;
            c.Body.Clear();
            c.Built = false;
            if (Root.panel != null) BuildBody(c);
            if (c.HasBelow) { c.Below.Clear(); if (Root.panel != null) BuildBelow(c); }
        }

        /// Asks Image again for every card in the window.
        public void ImagesChanged() { Window(true); }

        private Card Find(T item)
        {
            foreach (var c in _cards) if (ReferenceEquals(c.Item, item)) return c;
            string k = item == null ? null : KeyOf(item);
            foreach (var c in _cards) if (k != null && c.Key == k) return c;
            return null;
        }

        // With no Key every card shares one key: cards are then kept by position.
        private string KeyOf(T it) => _o.Key != null ? _o.Key(it) ?? "" : "";

        private void Attached()
        {
            if (_joined == null) { _joined = Refresher.Of(Root); if (_joined != null) _joined.Add(_pass, "ImageCards"); }
            _sv = Root.GetFirstAncestorOfType<ScrollView>();
            if (_sv != null)
            {
                _sv.verticalScroller.valueChanged += _scrolled;
                Events.On(_sv.contentViewport, _viewGeo);
            }
            foreach (var c in _cards) { BuildBody(c); if (c.HasBelow && c.Below.childCount == 0) BuildBelow(c); }
            Window(false);
        }

        private void Detached()
        {
            if (_joined != null) _joined.Remove(_pass);
            _joined = null;
            if (_sv != null)
            {
                _sv.verticalScroller.valueChanged -= _scrolled;
                Events.Off(_sv.contentViewport, _viewGeo);
            }
            _sv = null;
            foreach (var c in _cards) if (c.InWindow) Drop(c);
        }

        private void Pass()
        {
            if (_passing) return;
            _passing = true;
            try
            {
                var items = _o.Items != null ? _o.Items() : null;
                int n = items == null ? 0 : items.Count;
                bool same = n == _cards.Count, moved = false;
                for (int i = 0; same && i < n; i++) same = string.Equals(KeyOf(items[i]), _cards[i].Key);
                if (!same)
                {
                    Rekey(items, n);
                    moved = true;
                    if (Root.panel != null) foreach (var c in _cards) BuildBody(c);   // the new keys' bodies; built ones return at once
                }
                for (int i = 0; i < n; i++)
                {
                    var c = _cards[i];
                    c.Item = items[i];
                    bool has = false;
                    if (_o.Below != null)
                        try { has = _o.Below(c.Item) != null; }
                        catch (Exception e) { Logged(c, "Below", e); }
                    if (has == c.HasBelow) continue;
                    c.HasBelow = has;
                    moved = true;
                    if (has)
                    {
                        c.Below = new VisualElement { name = "image-cards-below" };
                        c.Below.style.width = Length.Percent(100f);
                        c.Below.style.marginBottom = _o.Gap;
                        if (Root.panel != null) BuildBelow(c);
                    }
                    else { c.Below.RemoveFromHierarchy(); c.Below = null; }
                }
                int cols = Count();
                if (cols != Columns) { Columns = cols; moved = true; Widths(); }
                if (moved) { Arrange(); Window(false); }
            }
            finally { _passing = false; }
        }

        // Kept keys keep their card; a key twice gets two cards.
        private void Rekey(IReadOnlyList<T> items, int n)
        {
            var old = new Dictionary<string, List<Card>>();
            foreach (var c in _cards)
            {
                List<Card> l;
                if (!old.TryGetValue(c.Key, out l)) old[c.Key] = l = new List<Card>();
                l.Add(c);
            }
            var next = new List<Card>(n);
            for (int i = 0; i < n; i++)
            {
                string k = KeyOf(items[i]);
                List<Card> l;
                Card c;
                if (old.TryGetValue(k, out l) && l.Count > 0) { c = l[0]; l.RemoveAt(0); }
                else c = New(k, items[i]);
                next.Add(c);
            }
            foreach (var l in old.Values)
                foreach (var gone in l) { gone.El.RemoveFromHierarchy(); if (gone.Below != null) gone.Below.RemoveFromHierarchy(); }
            _cards.Clear();
            _cards.AddRange(next);
            Widths();
        }

        private Card New(string key, T item)
        {
            var c = new Card { Key = key, Item = item };
            var el = c.El = new VisualElement { name = "image-cards-card" };
            el.style.overflow = Overflow.Hidden;
            el.style.flexShrink = 0f;
            Ui.SetBorderWidth(el, _sz.Line);
            Ui.SetRadius(el, _sz.Radius);
            Paint.Look(el, Theme.For(Root).Looks.Tab);
            var layer = c.Layer = new VisualElement { name = "image-cards-layer", pickingMode = PickingMode.Ignore };
            Fill(layer);
            layer.style.display = DisplayStyle.None;
            var img = c.Img = new Image { name = "image-cards-image", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleAndCrop };
            Fill(img);
            layer.Add(img);
            var scrim = new VisualElement { name = "image-cards-scrim", pickingMode = PickingMode.Ignore };
            Fill(scrim);
            Paint.Bg(scrim, Role.Panel);
            scrim.style.opacity = Mathf.Clamp01(_o.Scrim);
            layer.Add(scrim);
            el.Add(layer);
            c.Body = new VisualElement { name = "image-cards-body" };
            Ui.SetPadding(c.Body, _sz.ControlPad);
            el.Add(c.Body);
            Events.On<GeometryChangedEvent>(el, _ => { if (Root.panel != null && Displayed()) Ask(c, View(), false); });
            if (_o.Click != null)
            {
                el.focusable = true;
                Events.On<ClickEvent>(el, e => { if (!Inner(e.target as VisualElement, el)) Clicked(c); });
                Events.On<NavigationSubmitEvent>(el, e => { if (e.target == el) Clicked(c); });
            }
            return c;
        }

        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0f; e.style.top = 0f; e.style.right = 0f; e.style.bottom = 0f;
        }

        // A click that lands on a control inside the Body is that control's.
        private static bool Inner(VisualElement t, VisualElement card)
        {
            for (var x = t; x != null && x != card; x = x.parent)
                if (x.focusable || x is Button || x is TextField || x is Toggle) return true;
            return false;
        }

        private void Clicked(Card c)
        {
            if (_o.Click == null || !_cards.Contains(c)) return;
            var it = c.Item;
            Safe.Run(_host, "ImageCards Click", () => _o.Click(it));
        }

        private void BuildBody(Card c)
        {
            if (c.Built || _o.Body == null) return;
            c.Built = true;
            var it = c.Item;
            if (!Guard(c, "Body", () => { _o.Body(c.Body, it); return true; }, false)) c.Body.Clear();
        }

        private void BuildBelow(Card c)
        {
            var it = c.Item;
            var build = Guard(c, "Below", () => _o.Below(it), null);
            if (build == null || !Guard(c, "Below", () => { build(c.Below); return true; }, false)) c.Below.Clear();
        }

        // A throwing delegate leaves its card blank and is logged once per key and site.
        private TR Guard<TR>(Card c, string site, Func<TR> fn, TR fallback)
        {
            try { return fn(); }
            catch (Exception e) { Logged(c, site, e); return fallback; }
        }

        private void Logged(Card c, string site, Exception e)
        {
            if (_logged.Add(site + "\n" + c.Key)) _host.Log("[" + _host.Name + "] ImageCards " + site + " '" + c.Key + "': " + e.Message);
        }

        private int Count()
        {
            bool grid = _o.Grid != null && Safe.Get(_host, "ImageCards Grid", _o.Grid, false);
            if (!grid) return 1;
            return CardMath.Columns(_width, _o.MinWidth, _o.Gap, _o.MaxColumns, Columns);
        }

        private void Geometry()
        {
            float w = Root.contentRect.width;
            if (!float.IsNaN(w) && Mathf.Abs(w - _width) > 0.5f)
            {
                _width = w;
                int cols = Count();
                bool regroup = cols != Columns;
                Columns = cols;
                Widths();
                if (regroup) Arrange();
            }
            Window(false);
        }

        // One column fills the row; a grid splits it, Gap between columns.
        private void Widths()
        {
            int cols = Columns;
            float w = cols <= 1 || _width < 1f ? -1f : (_width - _o.Gap * (cols - 1)) / cols - 0.5f;
            for (int i = 0; i < _cards.Count; i++)
            {
                var s = _cards[i].El.style;
                s.width = w < 0f ? Length.Percent(100f) : new Length(w);
                s.marginRight = cols > 1 && i % cols != cols - 1 ? _o.Gap : 0f;
                s.marginBottom = _o.Gap;
                if (_cards[i].Below != null) _cards[i].Below.style.marginBottom = _o.Gap;
            }
        }

        // Each row's cards, then the Below of each item in that row; only elements out of place move.
        private void Arrange()
        {
            int cols = Math.Max(1, Columns), at = 0, n = _cards.Count;
            for (int r = 0; r < n; r += cols)
            {
                int end = Math.Min(n, r + cols);
                for (int i = r; i < end; i++) Place(_cards[i].El, at++);
                for (int i = r; i < end; i++) if (_cards[i].Below != null) Place(_cards[i].Below, at++);
            }
        }

        private void Place(VisualElement e, int i)
        {
            if (e.parent == Root && i < Root.childCount && Root.ElementAt(i) == e) return;
            Root.Insert(i, e);
        }

        private bool Displayed()
        {
            for (var x = Root; x != null; x = x.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        // The image window in panel space (unused with no ScrollView: every card is inside).
        // A viewport with no height yet gives an empty window, and Ask waits for its geometry change.
        private Rect View()
        {
            if (_sv == null) return default(Rect);
            var vp = _sv.contentViewport.worldBound;
            if (!(vp.height > 0f)) return default(Rect);
            float m = _o.ImageMargin > 0f ? _o.ImageMargin : vp.height;
            return new Rect(vp.x, vp.y - m, vp.width, vp.height + 2f * m);
        }

        private void Window(bool again)
        {
            if (Root.panel == null || _o.Image == null || !Displayed()) return;
            var view = View();
            for (int i = 0; i < _cards.Count; i++) Ask(_cards[i], view, again);
        }

        // Asks for the card's image on entering the window (or again), drops it on leaving.
        private void Ask(Card c, Rect view, bool again)
        {
            if (_o.Image == null) return;
            bool inside;
            if (_sv == null) inside = true;
            else
            {
                if (!(view.height > 0f)) return;
                var b = c.El.worldBound;
                if (float.IsNaN(b.y) || b.height <= 0f) return;   // not laid out yet: its geometry change comes back here
                inside = b.yMax > view.yMin && b.yMin < view.yMax;
            }
            if (!inside) { if (c.InWindow) Drop(c); return; }
            if (c.InWindow && !again) return;
            c.InWindow = true;
            Texture t = null;
            try { t = _o.Image(c.Item); }
            catch (Exception e) { Logged(c, "Image", e); }   // inline: no closure per card entering the window
            Show(c, t);
        }

        private void Show(Card c, Texture t)
        {
            if (ReferenceEquals(t, c.Shown)) return;   // by reference: a destroyed texture equals null to Unity
            bool was = c.Shown != null;
            c.Shown = t;
            c.Img.image = t;
            if (t == null) { c.Layer.style.display = DisplayStyle.None; return; }
            if (was) return;
            c.Layer.style.display = DisplayStyle.Flex;
            c.Layer.style.opacity = 0f;
            Anim.Run(c.Layer, 0f, 1f, 280, (e, v) => e.style.opacity = v);   // from 0: a fresh layer's resolved opacity is stale
        }

        private void Drop(Card c)
        {
            c.InWindow = false;
            Show(c, null);
        }
    }

    public static partial class Controls
    {
        /// Responsive cards over late images, with full-width content after a card's row.
        public static ImageCards<T> ImageCards<T>(VisualElement parent, Action<ImageCardsOptions<T>> configure) where T : class
        {
            var o = new ImageCardsOptions<T>();
            if (configure != null) configure(o);
            return new ImageCards<T>(parent, o);
        }
    }
}
