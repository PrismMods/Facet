using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Facet
{
    /* A tab strip. Both mods carry a TabRail (530 lines between them, 94 of which differ) and the
       debug panel had hand-rolled a third; this is the one that replaces all three.

       Selection state lives here rather than in the caller's index variable, because the bug it
       keeps causing is a rebuild that leaves the highlight on a tab that has moved. Rebuild() takes
       the names and re-applies selection by NAME where it can, so a log source appearing or going
       away does not silently switch which tab you are reading.

       Tabs.Pages makes a row of tab pages: one NavItem per tab, the selected one built under the row. */
    public sealed class Tabs
    {
        private readonly VisualElement _bar;
        private readonly Action<int> _onSelect;
        private readonly List<string> _names = new List<string>();
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<VisualElement> _lines = new List<VisualElement>();   // each tab's underline

        public int Selected { get; private set; }
        public string SelectedName => Selected >= 0 && Selected < _names.Count ? _names[Selected] : null;

        public Tabs(VisualElement parent, Action<int> onSelect)
        {
            _onSelect = onSelect;
            _bar = Ui.Row(parent);
            _bar.style.flexWrap = Wrap.Wrap;
            _bar.style.marginBottom = Theme.For(_bar).Sizes.TabGap;
        }

        public VisualElement Element => _bar;

        /// The selected tab's page (Tabs.Pages); null for a bare row.
        public VisualElement Content { get; private set; }

        // The tab last picked, per host and page, for a host with no Read/Write (as Controls' _folds does for folds).
        private static readonly Dictionary<string, string> _picked = new Dictionary<string, string>();

        // ponytail: a tab's own VisibleWhen is asked at build only; the index's visibility list does not record it, so a flip stays stale until a page Revision or visibility change rebuilds the index.
        private List<NavItem> _pages;
        private bool[] _shown;
        private Nav _nav;
        private string _key;

        /// On the row's element, so Nav's search reveal can find the tab rows on a page.
        internal sealed class Ref { public Tabs Tabs; }

        /// Marks what a row's index counts within: a Nav's page and each Pages row's Content.
        internal sealed class Page { }

        /* The row's index among the Pages rows on its page (else the top of its tree), in tree order and
           not counting rows inside another row's tab pages: a row's remembered tab is its own, with two
           rows on one page or rows outside a Nav. */
        private static int IndexOnPage(VisualElement bar)
        {
            var page = bar.parent;
            while (page != null && page.parent != null && Data.Find<Page>(page) == null) page = page.parent;
            int n = 0;
            if (page != null) Before(page, bar, ref n);
            return n;
        }

        private static bool Before(VisualElement e, VisualElement bar, ref int n)
        {
            for (int i = 0; i < e.childCount; i++)
            {
                var c = e[i];
                if (c == bar) return true;
                if (Data.Find<Ref>(c) != null) n++;
                else if (Data.Find<Page>(c) == null && Before(c, bar, ref n)) return true;
            }
            return false;
        }

        /* One tab per NavItem: its Title, Tooltip and Build, and its VisibleWhen, asked here only.
           Only the selected page is built, into Content, fresh on every select like a Nav page; the
           page it replaces is removed from the tree, not hidden, so anything it runs (a preview)
           stops. `selected` and onSelect's index are into `pages`; -1 picks the tab last selected
           on this page, kept through the host under "tab.<page>#<n>" (n: the row's index among the
           Pages rows on the page), else the first visible one.
           A page builds inside the Nav that was building when the row was made (Nav.InTab), so
           SubPage rows work, fold and undo keys are per tab, and unnamed controls count from
           1000 * (index + 1). While a search index builds, every visible page is built for it. */
        public static Tabs Pages(VisualElement parent, IList<NavItem> pages, int selected = -1, Action<int> onSelect = null)
        {
            var t = new Tabs(parent, onSelect);
            t._pages = pages == null ? new List<NavItem>() : new List<NavItem>(pages);   // a copy: the caller's list may change under us
            t._nav = Nav.Active;
            t._key = "tab." + (t._nav != null ? t._nav.PageTitle : "") + "#" + IndexOnPage(t._bar);
            t.Content = Ui.Box(parent);
            Data.Get<Page>(t.Content);
            Data.Get<Ref>(t._bar).Tabs = t;
            var host = Host.For(t._bar);
            var names = new List<string>();
            var shown = t._shown = new bool[t._pages.Count];
            for (int i = 0; i < shown.Length; i++)
            {
                var p = t._pages[i];
                names.Add(p?.Title);
                shown[i] = p != null && (p.VisibleWhen == null || Safe.Get(host, "Tabs.VisibleWhen", p.VisibleWhen, true));
            }
            t.Layout(names);   // every page keeps its index: a hidden one's tab is there, not displayed
            for (int i = 0; i < shown.Length; i++)
            {
                if (!shown[i]) t._buttons[i].parent.style.display = DisplayStyle.None;   // the tab's cell
                else if (!string.IsNullOrEmpty(t._pages[i].Tooltip)) Words.Tip(t._buttons[i], t._pages[i].Tooltip);
            }

            if (SearchIndex.Building != null)
            {
                string sec = SearchIndex.CurrentSection;   // a tab's own Sections must not leak into the next tab or past the row
                for (int i = 0; i < shown.Length; i++)
                    if (shown[i]) { SearchIndex.EnterTab(t._pages[i].Title, sec); t.Show(i); }
                SearchIndex.EnterTab(null, sec);
                return t;
            }

            int sel = selected;
            if (sel < 0 || sel >= shown.Length || !shown[sel])
            {
                string saved = host.Read(t._key);
                if (saved == null) _picked.TryGetValue(host.Name + "|" + t._key, out saved);
                sel = saved == null ? -1 : names.IndexOf(saved);
                if (sel < 0 || !shown[sel]) sel = Array.IndexOf(shown, true);
            }
            t.Selected = sel;
            t.Paint();
            if (sel >= 0) t.Show(sel);
            return t;
        }

        // Build page i into Content in place of the last one.
        private void Show(int i)
        {
            var host = Host.For(_bar);
            Content.Clear();
            Action build = () =>
            {
                var page = _pages[i];
                int seq = Controls.Seq;
                var labels = Controls.Labels;
                Controls.Seq = 1000 * (i + 1);
                Controls.Labels = null;   // a tab counts its own labels: its history keys carry the tab's name
                bool ok;
                try { ok = page.Build == null || Safe.Run(host, "Tabs.build '" + page.Title + "'", () => page.Build(Content)); }
                finally { Controls.Seq = seq; Controls.Labels = labels; }   // controls after the row count on as if no tab were there
                if (!ok) Nav.FailedCard(Content, page.Owner ?? host.Name);
            };
            if (_nav != null) _nav.InTab(_names[i], build);
            else build();
        }

        /// Select the tab titled `title` for a search hit: its NavItem, or null when this row has none.
        internal NavItem Reveal(string title)
        {
            int i = _pages == null ? -1 : _names.IndexOf(title);
            if (i < 0 || !_shown[i]) return null;
            Select(i);
            return _pages[i];
        }

        /// Re-lay the row for a new set of names. A Tabs.Pages row takes its names from its pages, so this is a no-op there.
        public void Rebuild(IEnumerable<string> names)
        {
            if (_pages == null) Layout(names);
        }

        private void Layout(IEnumerable<string> names)
        {
            string wasSelected = SelectedName;
            _names.Clear();
            _names.AddRange(names ?? new string[0]);
            _bar.Clear();
            _buttons.Clear();
            _lines.Clear();

            // Keep reading the same tab across a rebuild when it is still there.
            int keep = wasSelected != null ? _names.IndexOf(wasSelected) : -1;
            Selected = keep >= 0 ? keep : (_names.Count > 0 ? 0 : -1);

            /* The row sits on a rule, and each tab's underline (selected with it) lands on that rule:
               both end at the row's bottom edge. The underline goes in a cell around the button, not
               in the button: a Button that gains a child loses its text measure (UI Toolkit drops it
               on Insert), so it shrank to its padding and drew its name one letter per line. */
            var t = Theme.For(_bar); var sz = t.Sizes; var k = t.Looks;   // once per rebuild
            var rule = Pinned(_bar, 0f, sz.Line);
            rule.name = "rule";
            Facet.Paint.Look(rule, k.Rule);
            for (int i = 0; i < _names.Count; i++)
            {
                int idx = i;
                var cell = Ui.Box(_bar);
                cell.style.marginRight = sz.Gap;
                var b = Ui.Btn(_names[i], () => Select(idx), cell);
                b.style.marginRight = 0f;
                Facet.Paint.Look(b, k.Tab);
                var line = Pinned(cell, 0f, sz.Indicator);   // the cell's edge is the button's outer border edge
                line.name = "indicator";
                Facet.Paint.Look(line, k.Indicator);
                _buttons.Add(b);
                _lines.Add(line);
            }
            Paint();
        }

        // A bar along the bottom of `parent`, `height` tall, `inset` from its padding edge.
        private static VisualElement Pinned(VisualElement parent, float inset, float height)
        {
            var e = Ui.Box(parent);
            e.style.position = Position.Absolute;
            e.style.left = inset; e.style.right = inset; e.style.bottom = inset;
            e.style.height = height;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _names.Count || index == Selected || (_shown != null && !_shown[index])) return;
            Selected = index;
            Paint();
            if (_pages != null)
            {
                var host = Host.For(_bar);
                host.Write(_key, _names[index]);
                _picked[host.Name + "|" + _key] = _names[index];
                Show(index);
                Anim.FadeIn(Content, Anim.Fast);
            }
            if (_onSelect != null) Safe.Run(Host.For(_bar), "Tabs.onSelect", () => _onSelect(index));
        }

        private void Paint()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                Facet.Paint.Select(_buttons[i], i == Selected);
                Facet.Paint.Select(_lines[i], i == Selected);
            }
        }
    }
}
