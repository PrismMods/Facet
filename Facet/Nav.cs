using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// A page in a rail. Children make it a nested rail; a leaf carries the content builder.
    public sealed class NavItem
    {
        public string Title;
        public string Tooltip;
        /// Drawn left of the title in the rail; the default (empty) draws none.
        public Background Icon;
        /// Fills the content area. Called on every visit — build fresh rather than cache, so a page
        /// cannot show a value that changed while it was hidden.
        public Action<VisualElement> Build;
        public List<NavItem> Children;

        /* For a page whose content arrives after it is built — a fetched list, a folder scan. The
           panel polls this while the page is open and rebuilds when the value changes, which is
           why the font list used to need a tab switch to appear: Refresh() returns long before the
           fetch does, and nothing asked again. */
        public Func<int> Revision;

        /// Who put this page here, for Nav.RemoveOwner: a mod that unloads takes its pages with it.
        public string Owner;

        /// Asked whenever the rail is built (Nav.Rebuild re-asks); false hides the page and its subtree
        /// from the rail and from search.
        public Func<bool> VisibleWhen;

        /* A GROUP is a heading whose children are always visible — no chevron, nothing to expand,
           the heading itself is not a page. It buys the organisation of a tree without the cost of
           one: nothing hides, so nothing has to be found twice. Prefer it. Reach for a real
           collapsible branch only when a group is long enough that showing it all is worse. */
        public bool Group;

        /// Runs when a search result lands on this page, or on this tab of a Tabs.Pages row, once the
        /// Nav has opened it and started revealing the row; the hit says which row. Only the item
        /// the row is on runs it: a tab's, not its page's.
        public Action<SearchIndex.Hit> OnHit;

        public NavItem(string title, Action<VisualElement> build = null) { Title = title; Build = build; }

        public static NavItem Heading(string title, params NavItem[] children)
            => new NavItem(title) { Group = true, Children = new List<NavItem>(children) };

        public NavItem With(params NavItem[] children)
        {
            Children = new List<NavItem>(children);
            return this;
        }

        public bool IsBranch => Children != null && Children.Count > 0;
    }

    /* Side rail + breadcrumb, with nesting.

       This is the shell the mods' settings menus get rebuilt in. Both already have a TabRail and a
       PageStack — 530 and 398 lines between them — doing a flatter version of the same job: a list
       of pages on the left, one of them showing on the right. What they do not have is depth, which
       is why Quartz's settings are hard to find: everything competes in one flat list.

       A branch expands IN PLACE in the rail rather than replacing it. Replacing the rail is how you
       lose people — the thing they clicked vanishes and the way back is a button somewhere else.
       The breadcrumb above the content says where they are and every crumb is clickable. */
    public sealed partial class Nav
    {
        private readonly VisualElement _rail, _crumbs, _content;
        private readonly VisualElement _host;
        private readonly List<NavItem> _roots;
        private readonly List<NavItem> _path = new List<NavItem>();
        private readonly HashSet<string> _expanded = new HashSet<string>();

        private string _filter = "";
        private VisualElement _railScroll;
        private ScrollView _contentScroll;
        private bool _railOpen = true;

        /* Search shows RESULTS, not a filtered rail. Filtering the rail answers "which page might
           hold this", which is the question the user already could not answer; a result list
           answers "here is the setting, and here is where it lives". Ported from Topaz. */
        public void Filter(string query)
        {
            _filter = (query ?? "").Trim();
            if (_filter.Length == 0)
            {
                // Back to the page, and drop the results heading with it.
                RebuildCrumbs();
                RebuildContent();
                return;
            }
            ShowResults();
        }

        private void ShowResults()
        {
            // Once per query, above the rows: Dark's Sizes and Looks are fresh copies on every read.
            var t = Theme.For(_content); var sz = t.Sizes; var rowLook = t.Looks.Row;
            _crumbs.Clear();
            _crumbs.style.paddingTop = sz.Gap;
            _crumbs.style.paddingBottom = sz.Gap;
            var title = Ui.Text(Words.OwnF("Results for \u201c{0}\u201d", Host.For(_crumbs), _filter), _crumbs, sz.Title, own: true);
            var shownFilter = _filter;
            Words.OnRelabel(title, () => title.text = Words.OwnF("Results for \u201c{0}\u201d", Host.For(title), shownFilter));   // follows a language switch
            title.style.unityFontStyleAndWeight = FontStyle.Bold;

            _content.Clear();
            var hits = EnsureIndex().Find(_filter);
            if (hits.Count == 0) { Ui.Muted("Nothing matches.", _content, true); return; }

            foreach (var hit in hits)
            {
                var h = hit;
                var row = Ui.Row(_content);
                row.style.minHeight = sz.RowHeight + 2f;
                row.style.paddingLeft = 8f;
                row.pickingMode = PickingMode.Position;
                Ui.SetRadius(row, sz.Radius);

                var label = Ui.Text(h.Label, row, sz.Body);
                label.style.flexGrow = 1f;
                label.pickingMode = PickingMode.Ignore;

                // Where it lives, so the result teaches the menu rather than bypassing it.
                // Each part through the host's table, then joined: the joined string is no key. An entry's
                // Where is a key too; text the table does not know comes back as given.
                var host = Host.For(_host);
                string at = host.Translate(h.Page);
                if (h.Open == null && !string.IsNullOrEmpty(h.Tab)) at += " · " + host.Translate(h.Tab);
                if (h.Open == null && !string.IsNullOrEmpty(h.Section)) at += " · " + host.Translate(h.Section);
                var where = Ui.Text(at, row, sz.Small, Role.TextMuted, true);
                where.pickingMode = PickingMode.Ignore;

                Paint.Look(row, rowLook);
                Events.On<ClickEvent>(row, _ => GoTo(h));
            }
        }

        /* Open the page a hit lives on, select its tab in every tab row there that has it, and bring
           its row into view; then the item it landed on hears of it. A consumer's entry
           (AddSearchSource) runs its own action instead. */
        private void GoTo(SearchIndex.Hit hit)
        {
            var host = Host.For(_host);
            if (hit.Open != null) { Safe.Run(host, "Nav search entry '" + hit.Key + "'", hit.Open); return; }
            var page = hit.PageRef as NavItem;
            if (page == null) return;
            _filter = "";
            Go(page);
            var landed = page;
            if (hit.Tab != null)
                foreach (var e in _content.Query<VisualElement>().ToList())
                {
                    var r = Data.Find<Tabs.Ref>(e);
                    var tab = r == null ? null : r.Tabs.Reveal(hit.Tab);
                    if (tab != null) landed = tab;
                }
            Reveal(hit);
            if (landed.OnHit != null) Safe.Run(host, "NavItem.OnHit '" + landed.Title + "'", () => landed.OnHit(hit));
        }

        private readonly List<Func<IEnumerable<SearchIndex.Entry>>> _sources = new List<Func<IEnumerable<SearchIndex.Entry>>>();

        /// Add once per Nav; entries refresh when the index rebuilds (a page's Revision or visibility change); to retire a source, yield nothing.
        /* Search results for pages Facet did not build (a consumer's own menu): `source` is asked for
           its entries each time the index is built (the first query, and again once a page's
           Revision or visibility has changed), and choosing one runs its Open. Through Safe: a
           throwing source or Open is logged, once per 10 s, and goes no further. */
        public void AddSearchSource(Func<IEnumerable<SearchIndex.Entry>> source)
        {
            if (source == null) return;
            _sources.Add(source);
            _built.Clear();
        }

        /* Unfold every Section on the way to the row (through the fold Section itself uses, so its
           state and the host's copy follow), then — once layout has seen the unfolded sections —
           scroll each enclosing ScrollView to it and flash it. Only rows Facet noted match (by the
           hit's English key); of several with the same key the one in the hit's Section wins.
           Layout is polled for (16 ms, bounded); giving up is logged once and the scroll is tried
           anyway. */
        private void Reveal(SearchIndex.Hit hit)
        {
            VisualElement row = null;
            foreach (var e in _content.Query<VisualElement>().ToList())
            {
                var n = Data.Find<Controls.Noting>(e);
                if (n == null || n.Key != hit.Key) continue;
                if (row == null) row = e;
                if (EnclosingTitle(e) == hit.Section) { row = e; break; }
            }
            if (row == null) return;
            for (var e = row.hierarchy.parent; e != null && e != _content; e = e.hierarchy.parent)
            {
                var f = Data.Find<Controls.Fold>(e);
                if (f != null && !f.IsOpen()) f.Set(true, false);
            }
            int tries = 0;
            bool done = false;
            row.schedule.Execute(() =>
            {
                if (done) return;
                if (Hidden(row)) { done = true; return; }   // the surface went away (an OnHit hid the window): nothing to wait for, so no log
                bool ready = row.layout.height > 0f;   // NaN or 0 until a layout pass has seen it displayed
                if (!ready && ++tries <= 30) return;
                done = true;
                if (!ready) Host.For(_host).Log("Nav: reveal of '" + hit.Label + "' gave up waiting for layout");
                // A glide would carry the row off again, and ScrollTo writes nothing for a row already in view.
                for (var sv = row.GetFirstAncestorOfType<ScrollView>(); sv != null; sv = sv.hierarchy.parent?.GetFirstAncestorOfType<ScrollView>())
                {
                    Glide.Stop(sv);
                    sv.ScrollTo(row);
                }
                /* Fade a highlight out rather than in: the row is on screen by now, so starting
                   lit and settling is what draws the eye to it. */
                Paint.Flash(row, Role.Accent, 900);
            }).Every(16).Until(() => done);
        }

        // Detached, or under an element set to display:none (a hidden window or surface).
        private static bool Hidden(VisualElement e)
        {
            if (e.panel == null) return true;
            for (; e != null; e = e.hierarchy.parent) if (e.style.display == DisplayStyle.None) return true;
            return false;
        }

        // Title of the nearest Fold or card body above e: a card is a search section but has no Fold.
        private static string EnclosingTitle(VisualElement e)
        {
            for (e = e.hierarchy.parent; e != null; e = e.hierarchy.parent)
            {
                var f = Data.Find<Controls.Fold>(e);
                if (f != null) return f.Title;
                var c = Data.Find<Controls.CardRows>(e);
                if (c != null) return c.Title;
            }
            return null;
        }

        /* Below CollapseAt the rail is worth less than the space it costs, so it snaps shut (unless the
           host made it not Collapsible: then it stops at MinRail) — and
           dragging back out from nothing reopens it at a usable width rather than at one pixel. */
        public const float MinRail = 120f, MaxRail = 380f, CollapseAt = 80f;

        private void MakeRailHandle(VisualElement handle, VisualElement rail)
        {
            float startWidth = 0f;
            float startX = 0f;

            Events.On<PointerDownEvent>(handle, e =>
            {
                if (e.button != 0) return;
                startX = e.position.x;
                startWidth = _railOpen ? rail.resolvedStyle.width : 0f;
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            Events.On<PointerMoveEvent>(handle, e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                float w = startWidth + (e.position.x - startX);
                if (w < CollapseAt && _collapsible)
                {
                    _railOpen = false;
                    rail.style.display = DisplayStyle.None;
                    PaintHandle();
                    ReportInset();
                    return;
                }
                _railOpen = true;
                rail.style.display = DisplayStyle.Flex;
                _railWidth = Mathf.Clamp(w, MinRail, MaxRail);
                rail.style.width = _railWidth;
                PaintHandle();
                ReportInset();
            });
            Events.On<PointerUpEvent>(handle, e =>
            {
                if (handle.HasPointerCapture(e.pointerId)) handle.ReleasePointer(e.pointerId);
                // A click with no drag reopens a collapsed rail: dragging out from zero width is
                // fiddly, and the knob looks like something you click.
                if (!_railOpen && Mathf.Abs(e.position.x - startX) < 3f)
                {
                    _railOpen = true;
                    rail.style.display = DisplayStyle.Flex;
                    rail.style.width = _railWidth;
                    PaintHandle();
                }
            });
        }

        private bool _collapsible = true;

        /// Whether the rail can be shut (default true). False is for a host whose own setting decides if the
        /// rail shows: dragging the handle stops at MinRail, ToggleRail never shuts it, and a shut rail opens.
        /// The width still drags.
        public bool Collapsible
        {
            get { return _collapsible; }
            set
            {
                _collapsible = value;
                if (value || _railOpen) return;
                ToggleRail();   // opens it
                PaintHandle();
            }
        }

        /// Collapse the rail to give the content the whole window. Does nothing while Compact, or (shutting)
        /// while not Collapsible.
        public void ToggleRail()
        {
            if (_compact || (_railOpen && !_collapsible)) return;
            _railOpen = !_railOpen;
            ReportInset();
            if (_railScroll == null) return;
            if (_railOpen)
            {
                _railScroll.style.display = DisplayStyle.Flex;
                Anim.Width(_railScroll, _railWidth, Anim.Fast);
            }
            else
            {
                // Hidden only once it has finished closing, or it vanishes and the content jumps.
                Anim.Width(_railScroll, 0f, Anim.Fast, () => _railScroll.style.display = DisplayStyle.None);
            }
        }

        private static readonly Look HandleLine = new Look { Bg = Role.PanelBorder, HoverBg = Role.Accent };
        private static readonly Look HandleKnob = new Look { Bg = Role.RowAlt, HoverBg = Role.RowHover };

        private float _railWidth = 200f;
        private VisualElement _railLine, _railKnob;

        /// Hairline while the rail is open, knob-with-arrow once it is collapsed.
        /// Where the content pane starts. The header lines its search box up with this, so the two
        /// stay aligned as the rail is resized or collapsed.
        public Action<float> OnInsetChanged;
        private float _lastInset = float.NaN;

        private void ReportInset()
        {
            // No captured locals here: the check runs on every drag move and must not allocate.
            if (OnInsetChanged == null || Inset == _lastInset) return;
            NotifyInset();
        }

        private void NotifyInset()
        {
            var f = OnInsetChanged;
            float inset = Inset;
            _lastInset = inset;
            Safe.Run(Host.For(_host), "Nav.OnInsetChanged", () => f(inset));
        }

        private void PaintHandle()
        {
            if (_railLine != null) _railLine.style.display = _railOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_railKnob != null) _railKnob.style.display = _railOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public bool RailOpen => _railOpen;

        public Nav(VisualElement parent, IEnumerable<NavItem> items, float railWidth = 200f)
        {
            _host = parent;
            _roots = new List<NavItem>(items ?? new NavItem[0]);

            var split = Ui.Row(parent);
            split.style.flexGrow = 1f;
            split.style.flexShrink = 1f;   // absorbs slack, like Window.Body — see its comment
            split.style.minHeight = 0f;
            split.style.alignItems = Align.Stretch;

            var railScroll = new ScrollView(ScrollViewMode.Vertical);
            railScroll.style.width = railWidth;
            _railWidth = railWidth;
            railScroll.style.flexShrink = 0f;
            railScroll.style.minHeight = 0f;
            railScroll.contentContainer.style.flexShrink = 0f;
            Skin.When<ScrollView>(railScroll, Skin.Scroll);
            split.Add(railScroll);
            _railScroll = railScroll;
            _rail = railScroll.contentContainer;
            Paint.Bg(railScroll, Role.Rail);

            /* A drag handle instead of a toggle button: the same gesture Sapphire's timeline uses,
               where dragging narrows the pane and collapses it once it is too small to be useful.

               Open, it is a hairline — a filled bar the height of the window reads as a scrollbar,
               which is what the first version looked like. Collapsed, it becomes a knob with an
               arrow, because a hairline says "boundary" and gives no hint that anything is hidden
               behind it. The hit area stays wider than the line either way. */
            var handle = Ui.Box(split);
            handle.style.width = 7f;
            handle.style.flexShrink = 0f;
            handle.style.alignItems = Align.Center;
            handle.style.justifyContent = Justify.Center;
            handle.pickingMode = PickingMode.Position;
            /* Resize cursor only while there is something to resize. Collapsed, the knob is a
               click-to-open affordance and a double arrow would promise the wrong gesture. */
            Cursors.Set(handle, () => _railOpen ? Cursors.Kind.ResizeHorizontal : Cursors.Kind.Default);

            var sz = Theme.For(handle).Sizes;
            var line = Ui.Box(handle);
            line.style.position = Position.Absolute;
            line.style.top = 0f; line.style.bottom = 0f;
            line.style.width = sz.Line;
            Paint.Look(line, HandleLine);
            line.pickingMode = PickingMode.Ignore;

            var knob = Ui.Box(handle);
            knob.style.width = 14f;
            knob.style.height = 34f;
            knob.style.alignItems = Align.Center;
            knob.style.justifyContent = Justify.Center;
            Paint.Look(knob, HandleKnob);
            Ui.SetRadius(knob, sz.Radius);
            knob.style.display = DisplayStyle.None;
            knob.pickingMode = PickingMode.Ignore;
            var knobArrow = Ui.Text("\u203A", knob, sz.Body, Role.TextMuted);
            knobArrow.pickingMode = PickingMode.Ignore;

            _railLine = line;
            _railKnob = knob;
            // The hit area is the handle, wider than either: they light when it is hovered.
            Events.On<MouseEnterEvent>(handle, _ => { Paint.Hover(line, true); Paint.Hover(knob, true); });
            Events.On<MouseLeaveEvent>(handle, _ => { Paint.Hover(line, false); Paint.Hover(knob, false); });
            MakeRailHandle(handle, railScroll);
            BuildCompact(split, handle, sz);

            var right = Ui.Box(split);
            right.style.flexGrow = 1f;
            right.style.flexShrink = 1f;
            right.style.minHeight = 0f;
            right.style.paddingLeft = sz.Pad;

            /* No page title. The rail already shows which page is open, and repeating it cost a
               line of height at the top of every page to say something the eye had just read.
               The element stays for search, which has nothing else to announce itself with. */
            _crumbs = Ui.Row(right);
            Paint.Bg(_crumbs, Role.Panel);
            _crumbs.style.flexShrink = 0f;

            var contentScroll = new ScrollView(ScrollViewMode.Vertical);
            contentScroll.style.flexGrow = 1f;
            contentScroll.style.minHeight = 0f;
            contentScroll.contentContainer.style.flexShrink = 0f;
            Skin.When<ScrollView>(contentScroll, Skin.Scroll);
            right.Add(contentScroll);
            _content = contentScroll.contentContainer;
            Data.Get<Tabs.Page>(_content);   // a Pages row's remembered tab is keyed by its index on the page
            _contentScroll = contentScroll;

            PaintHandle();
            ReportInset();

            // Open the first leaf, so the pane is never blank on arrival.
            var first = FirstLeaf(_roots);
            if (first != null) Go(first);
            else RebuildRail();
        }

        /* Pages are built on demand, so nothing would know what a page contains until it is
           opened — and a search that only finds pages you have already visited is not a search.
           The first query builds each visible page once into a container that is never added to
           the panel, with SearchIndex.Building pointing at this Nav's index; nothing is built at
           construction. One index per language (labels are indexed as the host shows them), kept
           until a page's Revision or visibility changes or an owner is removed. */
        private sealed class Built
        {
            public SearchIndex Index;
            public readonly Dictionary<NavItem, int> Revs = new Dictionary<NavItem, int>();
            public readonly Dictionary<NavItem, bool> Vis = new Dictionary<NavItem, bool>();
            public int Pages;
        }

        private readonly Dictionary<string, Built> _built = new Dictionary<string, Built>();

        /// The index for the host's current language: null until the first non-empty Filter builds it.
        public SearchIndex Index
        {
            get { Built b; return _built.TryGetValue(Host.For(_host).Language, out b) ? b.Index : null; }
        }

        private SearchIndex EnsureIndex()
        {
            var host = Host.For(_host);
            string lang = host.Language;
            Built b;
            if (_built.TryGetValue(lang, out b) && !Stale(b, host)) return b.Index;

            var sw = Stopwatch.StartNew();
            b = new Built { Index = new SearchIndex(host.Translate) };
            var was = SearchIndex.Building;
            SearchIndex.Building = b.Index;
            try { IndexInto(_roots, b, host); }
            finally { SearchIndex.Building = was; b.Index.End(); }
            foreach (var src in _sources)
                Safe.Run(host, "Nav search source", () => { foreach (var e in src()) b.Index.Add(e); });
            _built[lang] = b;
            host.Log("[perf] search index " + b.Pages + " pages " + sw.ElapsedMilliseconds + " ms");
            return b.Index;
        }

        private bool Stale(Built b, Host host)
        {
            foreach (var kv in b.Revs)
                if (Safe.Get(host, "Nav.Revision", kv.Key.Revision, kv.Value) != kv.Value) return true;
            foreach (var kv in b.Vis)
                if (Visible(kv.Key, host) != kv.Value) return true;
            return false;
        }

        private bool Visible(NavItem item, Host host)
            => Live(item) && (item.VisibleWhen == null || Safe.Get(host, "Nav.VisibleWhen", item.VisibleWhen, true));

        private bool Visible(NavItem item) => Visible(item, Host.For(_host));

        private void IndexInto(List<NavItem> items, Built b, Host host)
        {
            foreach (var item in items)
            {
                bool v = Visible(item, host);
                if (item.VisibleWhen != null) b.Vis[item] = v;
                if (!v) continue;
                if (item.IsBranch) { IndexInto(item.Children, b, host); continue; }
                if (item.Build == null) continue;
                if (item.Revision != null) b.Revs[item] = Safe.Get(host, "Nav.Revision", item.Revision, 0);
                b.Index.Begin(item, item.Title);
                using (Host.Use(host))   // the throwaway has no surface to name its host
                    Safe.Run(host, "Nav.build '" + item.Title + "'", () => item.Build(new VisualElement()));
                b.Index.End();
                b.Pages++;
            }
        }

        private NavItem FirstLeaf(List<NavItem> items)
        {
            foreach (var i in items)
            {
                if (!Visible(i)) continue;
                if (!i.IsBranch) return i;
                var deep = FirstLeaf(i.Children);
                if (deep != null) return deep;
            }
            return null;
        }

        /// Open a page, expanding whatever contains it.
        public void Go(NavItem target)
        {
            var path = new List<NavItem>();
            if (!FindPath(_roots, target, path)) return;
            _stack.Clear();
            _path.Clear();
            _path.AddRange(path);
            foreach (var step in _path) if (step.IsBranch) { _expanded.Add(step.Title); _last[step] = target; }
            RebuildRail();
            RebuildCrumbs();
            RebuildContent();
        }

        /// The last leaf opened under each branch, which clicking the branch reopens.
        /// Owners taken out by RemoveOwner. The caller's NavItems are never edited: they are filtered by this.
        private readonly HashSet<string> _removed = new HashSet<string>();

        private readonly Dictionary<NavItem, NavItem> _last = new Dictionary<NavItem, NavItem>();

        /// Remove every page `owner` put in (and any branch or group left empty by it), drop the
        /// search index, and move off the open page if it went.
        public void RemoveOwner(string owner)
        {
            if (owner == null || _removed.Contains(owner) || !HasOwner(_roots, owner)) return;
            _removed.Add(owner);
            _built.Clear();
            _last.Clear();
            var open = _path.Count > 0 ? _path[_path.Count - 1] : null;
            if (open != null && !FindPath(_roots, open, new List<NavItem>()))
            {
                _path.Clear();
                _stack.Clear();
                var first = FirstLeaf(_roots);
                if (first != null) { Go(first); return; }
                RebuildRail(); RebuildCrumbs(); RebuildContent();
            }
            else RebuildRail();
            if (_filter.Length > 0) ShowResults();
        }

        private static bool HasOwner(List<NavItem> items, string owner)
        {
            foreach (var it in items)
                if (it.Owner == owner || (it.IsBranch && HasOwner(it.Children, owner))) return true;
            return false;
        }

        /// Not removed with its owner, and — for a branch or group — something left under it.
        private bool Live(NavItem it)
        {
            if (it.Owner != null && _removed.Contains(it.Owner)) return false;
            if (!it.IsBranch) return true;
            foreach (var c in it.Children) if (Live(c)) return true;
            return false;
        }

        private bool FindPath(List<NavItem> items, NavItem target, List<NavItem> path)
        {
            foreach (var i in items)
            {
                if (!Visible(i)) continue;
                path.Add(i);
                if (i == target) return true;
                if (i.IsBranch && FindPath(i.Children, target, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        private void RebuildRail()
        {
            _rail.Clear();
            _pendingRule = false;
            var t = Theme.For(_rail);   // once per rebuild, not per entry
            if (_compactButton) AddRailButton(t.Sizes, t.Looks);
            foreach (var i in _roots) AddRailItem(i, 0, t.Sizes, t.Looks);
            if (_compact && !_widened) Titles(0f, false);
            DrawRailButton();
        }

        private bool _pendingRule;

        private void DrawPendingRule(Sizes sz, Looks k)
        {
            if (!_pendingRule) return;
            _pendingRule = false;
            var rule = Ui.Box(_rail);
            rule.name = "rule";
            rule.style.height = sz.Line;
            rule.style.marginTop = sz.Gap; rule.style.marginBottom = sz.Gap;
            rule.pickingMode = PickingMode.Ignore;
            Paint.Look(rule, k.Rule);
        }

        private void AddRailItem(NavItem item, int depth, Sizes sz, Looks k)
        {
            if (!Visible(item)) return;
            if (item.Group && string.IsNullOrEmpty(item.Title))
            {
                // An untitled group is a rule between groups, drawn only once a row follows it: a
                // group that hides entirely leaves no rule, and none sits above the first entry.
                if (_rail.childCount > (_compactButton ? 1 : 0)) _pendingRule = true;   // the button is not an entry
                foreach (var c in item.Children) AddRailItem(c, depth, sz, k);
                return;
            }
            if (item.Group)
            {
                DrawPendingRule(sz, k);
                /* A heading has to be obviously NOT clickable, or it reads as a page that does
                   nothing. Upper case, smaller than the items under it, and muted — the opposite
                   of the treatment a selected item gets. */
                var heading = Ui.Caps(item.Title, _rail);   // translated, then upper-cased
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                heading.style.marginTop = depth == 0 ? sz.Pad + 4f : sz.Gap;
                heading.style.marginBottom = 4f;
                heading.style.paddingLeft = 8f + depth * 12f;
                heading.pickingMode = PickingMode.Ignore;   // a heading is not a destination
                foreach (var c in item.Children) AddRailItem(c, depth, sz, k);
                return;
            }

            DrawPendingRule(sz, k);
            bool onPath = _path.Contains(item);
            bool open = item.IsBranch && _expanded.Contains(item.Title);

            var row = Ui.Row(_rail);
            row.style.minHeight = sz.NavRowHeight;
            row.style.paddingLeft = ItemInset + depth * 12f;
            row.pickingMode = PickingMode.Position;
            // The selected LEAF is the highlighted one. A branch on the path is merely open, and
            // colouring it as selected too makes it unclear which page is actually showing.
            bool selected = onPath && !item.IsBranch;
            Paint.Look(row, k.RailItem);
            Paint.Select(row, selected);
            Ui.SetRadius(row, sz.Radius);
            // The indicator bar on the row's left edge, selected with the row.
            var bar = Ui.Box(row);
            bar.name = "indicator";
            bar.style.position = Position.Absolute;
            bar.style.left = 0f; bar.style.top = 0f; bar.style.bottom = 0f;
            bar.style.width = sz.Indicator;
            bar.pickingMode = PickingMode.Ignore;
            Paint.Look(bar, k.Indicator);
            Paint.Select(bar, selected);
            if (!string.IsNullOrEmpty(item.Tooltip)) row.tooltip = item.Tooltip;

            if (item.IsBranch)
            {
                var chev = Ui.Text(open ? "▾" : "▸", row, sz.Small, Role.TextMuted);
                chev.style.width = 14f;
                chev.pickingMode = PickingMode.Ignore;
            }
            else
            {
                var pad = Ui.Box(row);
                pad.style.width = depth > 0 ? 14f : 0f;
            }

            // In the row, before the title: the row keeps its left edge and the title moves right.
            if (item.Icon != default(Background))
            {
                var icon = Ui.Box(row);
                icon.name = item.Title + "-icon";
                icon.style.width = sz.Icon; icon.style.height = sz.Icon;
                icon.style.marginRight = sz.Gap;
                icon.style.backgroundImage = item.Icon;
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);   // a wide sprite keeps its shape
                icon.pickingMode = PickingMode.Ignore;
                TintAsTitle(row, icon, k.RailItem, selected);
            }

            // The row's look drives the text (muted, full on hover or selected); without hover
            // feedback nothing in the rail looks clickable until you click it.
            var label = Ui.Text(item.Title, row, sz.Body);
            Paint.Inherit(label);
            label.style.flexGrow = 1f;
            label.pickingMode = PickingMode.Ignore;

            Events.On<ClickEvent>(row, _ =>
            {
                if (item.IsBranch)
                {
                    /* A branch you are not in reopens the leaf you last had open in it. One you are
                       in just folds or unfolds: it does not steal the content pane, the page you
                       were reading stays until you pick another one. */
                    NavItem back;
                    if (!_path.Contains(item) && _last.TryGetValue(item, out back))
                    {
                        // The remembered leaf, unless it (or a branch above it) has since gone or hidden.
                        if (!FindPath(_roots, back, new List<NavItem>())) back = FirstLeaf(item.Children);
                        if (back != null) { Go(back); return; }
                    }
                    if (!_expanded.Remove(item.Title)) _expanded.Add(item.Title);
                    RebuildRail();
                }
                else Go(item);
            });

            if (open) foreach (var c in item.Children) AddRailItem(c, depth + 1, sz, k);
        }

        // Tinted as the row's title: the rail look's text, lit while `row` is hovered unless it is the open entry.
        private static void TintAsTitle(VisualElement row, VisualElement image, Look rk, bool selected)
        {
            Role rest = (selected ? rk.SelectedText : null) ?? rk.Text ?? Role.TextMuted;
            Role lit = (selected ? rk.SelectedText : null) ?? rk.HoverText ?? rest;
            Paint.Tint(image, rest);
            if (lit == rest) return;
            Events.On<MouseEnterEvent>(row, _ => Paint.Tint(image, lit));
            Events.On<MouseLeaveEvent>(row, _ => Paint.Tint(image, rest));
        }

        // Titles live in the rail; _crumbs stays for search's results heading (see where it is built).
        private void RebuildCrumbs()
        {
            _crumbs.Clear();
            _crumbs.style.paddingTop = 0f;
            _crumbs.style.paddingBottom = 0f;
        }

        private void RebuildContent()
        {
            _content.Clear();

            Action<VisualElement> build;
            string what;
            var leaf = _path.Count > 0 ? _path[_path.Count - 1] : null;
            // What Tick compares against: the revision this build shows.
            if (leaf != null && leaf.Revision != null)
            {
                _revisionOf = leaf;
                _lastRevision = Safe.Get(Host.For(_host), "Nav.Revision", leaf.Revision, 0);
            }
            if (_stack.Count > 0)
            {
                var top = _stack[_stack.Count - 1];
                what = top.Key;
                build = top.Value;

                // Back row, naming where it goes: "Back" alone leaves you guessing after two pushes.
                string home = _stack.Count > 1 ? _stack[_stack.Count - 2].Key
                            : _path.Count > 0 ? _path[_path.Count - 1].Title : "back";
                var sz = Theme.For(_content).Sizes;
                var back = Ui.Row(_content);
                back.style.minHeight = sz.NavRowHeight;
                back.pickingMode = PickingMode.Position;
                var arrow = Ui.Text("\u2039  " + home, back, sz.Body, Role.Accent);
                arrow.pickingMode = PickingMode.Ignore;
                Events.On<ClickEvent>(back, _ => Pop());

                var heading = Ui.Text(what, _content, sz.Title);
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                heading.style.marginBottom = sz.Gap;
            }
            else
            {
                if (leaf == null) return;
                what = leaf.Title;
                build = leaf.Build;
            }
            if (build == null) return;

            var was = Active;
            Active = this;
            Controls.Seq = 0;   // so an unnamed control has the same history key after every rebuild
            var labels = Controls.Labels;
            Controls.Labels = null;   // the page counts its own labels; rows built after it (a popup's) count on as before it
            bool ok = Safe.Run(Host.For(_host), "Nav.build '" + what + "'", () => build(_content));
            Controls.Labels = labels;
            Active = was;
            if (!ok) FailedCard(_content, leaf != null && leaf.Owner != null ? leaf.Owner : Host.For(_host).Name);
        }

        // Names whose page it was (the page's Owner, else the host), so the reader knows which mod to blame.
        internal static void FailedCard(VisualElement parent, string owner)
        {
            var card = Ui.Muted("", parent);
            Action say = () => card.text = Words.OwnF("{0}: this page failed to build — see the log.", Host.For(card), owner);
            say();
            Words.OnRelabel(card, say);
        }

        /* Drill-down pages.

           The mods' menus push a subpage for anything with its own settings — a stat's colours, a
           key's row — rather than nesting it in the rail, and that is the right shape: a subpage
           belongs to the row that opened it, not to the top-level navigation. The rail keeps
           showing the page you came from, and a back row leads home. */
        private readonly List<KeyValuePair<string, Action<VisualElement>>> _stack =
            new List<KeyValuePair<string, Action<VisualElement>>>();

        /// The Nav currently building a page, so Controls.SubPage can reach it without plumbing.
        public static Nav Active { get; private set; }

        /// The title of the page being shown (a pushed subpage's, else the open leaf's), and the tab
        /// being built on it as "<page>/<tab>", for fold and history keys.
        internal string PageTitle
        {
            get
            {
                string p = _stack.Count > 0 ? _stack[_stack.Count - 1].Key : _path.Count > 0 ? _path[_path.Count - 1].Title : "";
                return _tab == null ? p : p + "/" + _tab;
            }
        }

        private string _tab;

        /// The tab being built, as InTab left it (null outside a tab page).
        internal string Tab => _tab;

        /// Build again inside the context a region was first built in: Active is this Nav and the tab is `tab`
        /// exactly, so a Controls.Live rebuild keys its rows and tabs as its first build did.
        internal void Within(string tab, Action build)
        {
            var was = Active; var wasTab = _tab;
            Active = this;
            _tab = tab;
            try { build(); }
            finally { Active = was; _tab = wasTab; }
        }

        /// Build a tab page of the open page (Tabs.Pages): Active is this Nav and PageTitle names the tab.
        internal void InTab(string tab, Action build)
        {
            var was = Active; var wasTab = _tab;
            Active = this;
            _tab = wasTab == null ? tab : wasTab + "/" + tab;
            try { build(); }
            finally { Active = was; _tab = wasTab; }
        }

        public void Push(string title, Action<VisualElement> build)
        {
            if (build == null) return;
            _stack.Add(new KeyValuePair<string, Action<VisualElement>>(title, build));
            RebuildContent();
        }

        public void Pop()
        {
            if (_stack.Count == 0) return;
            _stack.RemoveAt(_stack.Count - 1);
            RebuildContent();
        }

        /// Rebuild the open page in place, for a host whose values changed underneath it.
        public void Refresh() => Rebuild();

        /* Rebuild the open page and keep where it was scrolled to. The rail is rebuilt too, so a
           VisibleWhen that turned over shows up. Layout only knows the new height a frame later,
           so the offset is put back until it takes (bounded: this never polls for long). */
        public void Rebuild()
        {
            var sv = _contentScroll;
            Glide.Settle(sv);   // a rebuild mid-glide keeps the whole step, not the part already scrolled
            var off = sv.scrollOffset;
            RebuildRail();
            RebuildContent();
            if (off.y <= 0f && off.x <= 0f) return;
            sv.scrollOffset = off;
            int tries = 0;
            sv.schedule.Execute(() => sv.scrollOffset = off).Every(16)
              .Until(() => (Mathf.Abs(sv.scrollOffset.y - off.y) < 1f && Mathf.Abs(sv.scrollOffset.x - off.x) < 1f) || ++tries > 30);
        }

        private int _lastRevision;
        private NavItem _revisionOf;

        /// Called each frame by the panel. Rebuilds the open page in place (keeping scroll) when its
        /// Revision differs from the one its last build recorded.
        public void Tick()
        {
            if (_path.Count == 0) return;
            var page = _path[_path.Count - 1];
            if (page.Revision == null) return;
            int r = Safe.Get(Host.For(_host), "Nav.Revision", page.Revision, _lastRevision);
            if (page == _revisionOf && r == _lastRevision) return;
            Rebuild();
        }
    }
}
