using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    public enum ListState { Idle, Loading, Error, Empty, Ready }
    public enum MoreState { None, Loading, Error, End }

    /// What SearchList shows and calls. Not a Control: the list is the consumer's, in Body.
    public sealed class SearchListOptions
    {
        /// False: no search field.
        public bool Search { get; set; } = true;
        /// Written on every change of the search field.
        public Bind<string> Query { get; set; }
        /// An English key.
        public string Placeholder { get; set; }
        public int MaxLength { get; set; } = 128;
        /// Controls after the search field.
        public Action<Toolbar> Tools { get; set; }
        public Func<ListState> State { get; set; }
        /// The Loading, Error and Empty text, shown as returned.
        public Func<string> Message { get; set; }
        /// An English key: the Error button and the footer's retry.
        public string RetryLabel { get; set; }
        /// Error shows a RetryLabel button calling this; null = none.
        public Action Retry { get; set; }
        /// More Error content, built each time Error begins.
        public Action<VisualElement> ErrorExtra { get; set; }
        /// The footer under a Ready list; None = more to fetch.
        public Func<MoreState> More { get; set; }
        /// End's text, shown as returned.
        public Func<string> EndText { get; set; }
        /// Called near the end of a Ready list while More is None, and by the footer's retry.
        public Action LoadMore { get; set; }
        public float EndMargin { get; set; } = 400f;
    }

    /* A search row bound to a query, then the list's state: a spinner and Message while Loading, Message wrapped with
       a retry and ErrorExtra on Error, Message muted when Empty, and Body (the consumer's list) only when Ready, with a
       footer for paging. Every Func is re-read on Refresh and on the window's refresh pass. */
    public sealed class SearchList
    {
        public VisualElement Root { get; }
        public VisualElement Body { get; }

        private readonly SearchListOptions _o;
        private readonly Host _host;
        private readonly Action _pass;
        private readonly Func<string> _query;
        private readonly TextField _field;
        private readonly VisualElement _status, _spin, _extra, _foot, _footSpin;
        private readonly Label _message, _end;
        private readonly Button _retry, _footRetry;
        private readonly Ui.ScrollEnding _scrollEnd;
        private IDisposable _turn, _footTurn;
        private Refresher _joined;
        private ListState _state = (ListState)(-1);
        private bool _passing;

        internal SearchList(VisualElement parent, SearchListOptions o)
        {
            _o = o;
            _pass = Pass;
            if (o.Query != null) _query = o.Query.Get;   // made once: the pass runs on the refresh timer
            Root = new VisualElement { name = "search-list" };
            Root.style.flexShrink = 0f;
            if (parent != null) parent.Add(Root);
            _host = Host.For(Root);
            var sz = Theme.For(Root).Sizes;

            if (o.Search || o.Tools != null)
            {
                var tb = Controls.Toolbar(Root);
                if (o.Search)
                {
                    _field = tb.Search(Typed, o.Placeholder);
                    _field.name = "search-list-field";
                    _field.multiline = false;
                    _field.maxLength = Math.Max(0, o.MaxLength);
                    string q = Query();
                    if (!string.IsNullOrEmpty(q)) { _field.SetValueWithoutNotify(Cut(q)); ClearShown(true); }
                }
                if (o.Tools != null) Safe.Run(_host, "SearchList Tools", () => o.Tools(tb));
            }

            _status = new VisualElement { name = "search-list-status" };
            Ui.SetPadding(_status, sz.Pad);
            Root.Add(_status);
            var line = Ui.Row(_status);
            _spin = Spinner(line);
            _message = Ui.Text(line, "");
            _message.style.flexShrink = 1f;
            _retry = Ui.Btn(_status, o.RetryLabel, () => { if (_o.Retry != null) _o.Retry(); });
            _retry.style.alignSelf = Align.FlexStart;
            _retry.style.marginTop = sz.Gap;
            _extra = new VisualElement { name = "search-list-extra" };
            _status.Add(_extra);

            Body = new VisualElement { name = "search-list-body" };
            Body.style.flexShrink = 0f;
            Root.Add(Body);

            _foot = Ui.Row(Root);
            _foot.name = "search-list-footer";
            _foot.style.justifyContent = Justify.Center;
            Ui.SetPadding(_foot, sz.Gap);
            _footSpin = Spinner(_foot);
            _footRetry = Ui.Btn(_foot, o.RetryLabel, () => { if (_o.LoadMore != null) _o.LoadMore(); });
            _footRetry.style.marginRight = 0f;
            _end = Ui.Muted(_foot, "");

            _scrollEnd = (Ui.ScrollEnding)Ui.ScrollEnd(Body, o.EndMargin, Reached);
            Events.On<AttachToPanelEvent>(Root, _ => Attached());
            Events.On<DetachFromPanelEvent>(Root, _ => Detached());
            if (Root.panel != null) Attached();
            Safe.Run(_host, "SearchList", _pass);
        }

        /// Re-reads every state, then runs the end test once more (a footer back to None with nothing moved still pages).
        public void Refresh()
        {
            Safe.Run(_host, "SearchList", _pass);
            _scrollEnd.Check();
        }

        private void Attached()
        {
            if (_joined == null) { _joined = Refresher.Of(Root); if (_joined != null) _joined.Add(_pass, "SearchList"); }
        }

        private void Detached()
        {
            if (_joined != null) _joined.Remove(_pass);
            _joined = null;
        }

        private string Query() => _query == null ? "" : Safe.Get(_host, "SearchList Query", _query, "") ?? "";

        private string Cut(string v) => v != null && _o.MaxLength >= 0 && v.Length > _o.MaxLength ? v.Substring(0, _o.MaxLength) : v ?? "";

        private void Typed(string v)
        {
            string cut = Cut(v);
            if (cut != v) { _field.SetValueWithoutNotify(cut); ClearShown(cut.Length > 0); }
            if (_o.Query != null) _o.Query.Set(cut);
        }

        // The field's ✕ shows while there is text: SetValueWithoutNotify skips the handler that sets it.
        private void ClearShown(bool on)
        {
            var clear = _field.parent == null ? null : _field.parent.Q<Button>();
            if (clear != null) clear.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Reached()
        {
            if (_o.LoadMore == null || Get(_o.State, "SearchList State", ListState.Idle) != ListState.Ready
                || Get(_o.More, "SearchList More", MoreState.End) != MoreState.None) return;
            _o.LoadMore();
        }

        private T Get<T>(Func<T> f, string site, T fallback) => f == null ? fallback : Safe.Get(_host, site, f, fallback);

        private void Pass()
        {
            if (_passing) return;
            _passing = true;
            try
            {
                if (_field != null && !Editing())
                {
                    string q = Cut(Query());
                    if (q != _field.value) { _field.SetValueWithoutNotify(q); ClearShown(q.Length > 0); }
                }
                var st = Get(_o.State, "SearchList State", ListState.Idle);
                if (st == ListState.Error && _state != ListState.Error)
                {
                    _extra.Clear();
                    if (_o.ErrorExtra != null) Safe.Run(_host, "SearchList ErrorExtra", () => _o.ErrorExtra(_extra));
                }
                else if (st != ListState.Error && _state == ListState.Error) _extra.Clear();
                _state = st;
                bool ready = st == ListState.Ready;
                Show(_status, !ready && st != ListState.Idle);
                Show(Body, ready);
                string msg = st == ListState.Loading || st == ListState.Error || st == ListState.Empty ? Get(_o.Message, "SearchList Message", "") : "";
                _message.text = msg ?? "";
                _message.style.whiteSpace = st == ListState.Error ? WhiteSpace.Normal : WhiteSpace.NoWrap;
                Paint.Fg(_message, st == ListState.Empty ? Role.TextMuted : Role.Text);
                Show(_retry, st == ListState.Error && _o.Retry != null);
                _turn = Turn(_spin, st == ListState.Loading, _turn);

                var more = ready ? Get(_o.More, "SearchList More", MoreState.None) : MoreState.None;
                Show(_foot, ready && more != MoreState.None);
                Show(_footRetry, more == MoreState.Error);
                Show(_end, more == MoreState.End);
                if (more == MoreState.End) _end.text = Get(_o.EndText, "SearchList EndText", "") ?? "";
                _footTurn = Turn(_footSpin, more == MoreState.Loading, _footTurn);
            }
            finally { _passing = false; }
        }

        // A focused search field is the user's: the pass leaves its text alone.
        private bool Editing()
        {
            var fe = _field.focusController == null ? null : _field.focusController.focusedElement as VisualElement;
            return fe != null && (fe == _field || _field.Contains(fe));
        }

        private static void Show(VisualElement e, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        // An arc at icon size that turns while shown.
        private static VisualElement Spinner(VisualElement parent)
        {
            var sz = Theme.For(parent).Sizes;
            var e = new VisualElement { name = "search-list-spinner", pickingMode = PickingMode.Ignore };
            e.style.width = sz.Icon; e.style.height = sz.Icon;
            e.style.flexShrink = 0f;
            e.style.marginRight = sz.Gap;
            e.style.alignSelf = Align.Center;
            Ui.SetRadius(e, sz.Icon / 2f);
            e.style.borderTopWidth = sz.Indicator;
            e.style.borderRightWidth = sz.Indicator;
            Paint.Border(e, Role.Accent);
            e.style.display = DisplayStyle.None;
            parent.Add(e);
            return e;
        }

        /* Shows the spinner and turns it once per frame through Anim's speed (still at speed 0), or hides it and stops
           the turning. Ui.Every also stops it while detached or while its surface is hidden. */
        private static IDisposable Turn(VisualElement spin, bool on, IDisposable turning)
        {
            Show(spin, on);
            if (!on) { if (turning != null) turning.Dispose(); return null; }
            return turning ?? Spin(spin);
        }

        // Its own method: the closure over spin is made only when a turn starts, not on every pass through Turn.
        private static IDisposable Spin(VisualElement spin)
        {
            return Ui.Every(spin, 0, () =>
            {
                int ms = Motion.Scale(900);
                if (ms <= 0 || !Ui.Shown(spin)) return;
                spin.style.rotate = new Rotate(Time.realtimeSinceStartup * 1000f % ms / ms * 360f);
            });
        }
    }

    public static partial class Controls
    {
        /// A search row over a list with loading, error, empty and paging states.
        public static SearchList SearchList(VisualElement parent, Action<SearchListOptions> configure)
        {
            var o = new SearchListOptions();
            if (configure != null) configure(o);
            return new SearchList(parent, o);
        }
    }
}
