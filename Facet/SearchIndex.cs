using System;
using System.Collections.Generic;

namespace Facet
{
    /* What the search box searches: one index per Nav, built on the first query.

       Widgets register themselves as they are built, so the index is whatever the menu actually
       contains rather than a list somebody has to remember to update. Pages are built lazily, so
       the Nav builds each one once into a throwaway container while it makes the index, with
       `Building` set to that index — a row built any other time finds `Building` null and notes
       nothing. Being per Nav, building one never touches another window's index.

       A hit knows its page, which is what lets a result navigate rather than just report. It keeps
       the English key beside the shown label, and its tooltip in both forms, so it matches in both
       languages. A row on a tab page (Tabs.Pages) also knows its tab, which joins its context.

       A consumer can add entries for pages Facet did not build (Nav.AddSearchSource): each is a
       label, where it lives, and what opening it does. */
    public sealed class SearchIndex
    {
        public sealed class Hit
        {
            /// As the host showed it when the index was built (translated).
            public string Label;
            /// The English key the label came from.
            public string Key;
            public string Page;
            public object PageRef;      // the NavItem, held loosely so this file stays generic
            public string Section;
            /// The tab page it is on (Tabs.Pages), by title; null outside one.
            public string Tab;
            internal Search.Text[] Labels, Context;
            /// The row's tooltip, English and shown: a last-resort match (Search.Rate's detail).
            internal Search.Text Tip;
            /// An entry's action (Nav.AddSearchSource): choosing the hit runs it instead of navigating.
            internal Action Open;
        }

        /// A search result for something Facet did not build (Nav.AddSearchSource).
        public sealed class Entry
        {
            /// The English key, translated by the Nav's host like a row's label.
            public string Label;
            /// Where it lives: an English key, translated by the Nav's host like a page name (text its
            /// table does not know shows as given), shown beside it and searched in English and as shown.
            public string Where;
            /// What choosing it does.
            public Action Open;
            /// A detail matched like a row's tooltip: a last-resort match, English or shown.
            public string Tip;
        }

        private readonly List<Hit> _all = new List<Hit>();
        private readonly Func<string, string> _tr;
        private object _page;
        private string _pageName, _section, _tab;

        /// The index whose pages are being built right now; null the rest of the time.
        internal static SearchIndex Building;

        public SearchIndex(Func<string, string> translate = null) { _tr = translate; }

        public int Count => _all.Count;

        internal void Begin(object page, string name) { _page = page; _pageName = name; _section = null; _tab = null; }
        internal void End() { _page = null; _pageName = null; _section = null; _tab = null; }

        internal static void EnterSection(string title) { if (Building != null) Building._section = title; }

        /// The rows noted next are on this tab (null: off the tabs again) and in `section`: a tab's page may open sections of its own.
        internal static void EnterTab(string title, string section) { if (Building != null) { Building._tab = title; Building._section = section; } }

        /// The section being built, so Tabs.Pages can put it back around each tab.
        internal static string CurrentSection => Building?._section;

        internal void Add(Entry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Label)) return;
            Begin(null, e.Where);
            var h = Add(e.Label, Words.T(e.Label, _tr));
            h.Open = e.Open;
            NoteTip(h, e.Tip, Words.T(e.Tip, _tr));
            End();
        }

        /* Two characters minimum, copied from Topaz's rule and for its reason: a colour picker's
           R, G and B rows are labels too, and a search that offers "R" as a result is noise. */
        internal static Hit Note(string key, string shown)
        {
            var i = Building;
            if (i == null || i._page == null || string.IsNullOrEmpty(shown) || shown.Trim().Length < 2) return null;
            return i.Add(key ?? shown, shown);
        }

        internal static void NoteTip(Hit h, string key, string shown)
        {
            if (h != null) h.Tip = string.IsNullOrEmpty(key) ? null : new Search.Text(key == shown ? key : key + " " + shown);
        }

        private Hit Add(string key, string shown)
        {
            var h = new Hit { Label = shown, Key = key, Page = _pageName, PageRef = _page, Section = _section, Tab = _tab };
            h.Labels = key == shown ? new[] { new Search.Text(shown) } : new[] { new Search.Text(shown), new Search.Text(key) };
            var ctx = new List<Search.Text>();
            foreach (var c in new[] { _pageName, _section, _tab, _tr == null ? null : _tr(_pageName ?? ""), _tr == null || _section == null ? null : _tr(_section),
                                      _tr == null || _tab == null ? null : _tr(_tab) })
                if (!string.IsNullOrEmpty(c)) ctx.Add(new Search.Text(c));
            h.Context = ctx.ToArray();
            _all.Add(h);
            return h;
        }

        /// Ranked matches, best first. Typos are a second pass, run only when the exact one finds nothing.
        public List<Hit> Find(string query, int limit = 40)
        {
            var outp = new List<Hit>();
            var q = new Search.Query(query);
            if (q.IsEmpty) return outp;
            var scored = new List<KeyValuePair<int, int>>();   // score, index (the tie-break keeps build order)
            for (int pass = 0; pass < 2 && scored.Count == 0; pass++)
                for (int i = 0; i < _all.Count; i++)
                {
                    int r = Search.Rate(q, _all[i].Labels, _all[i].Context, _all[i].Tip, pass == 1);
                    if (r > 0) scored.Add(new KeyValuePair<int, int>(r, i));
                }
            scored.Sort((a, b) => a.Key != b.Key ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value));
            for (int i = 0; i < scored.Count && i < limit; i++) outp.Add(_all[scored[i].Value]);
            return outp;
        }
    }
}
