using System;
using UnityEngine.UIElements;

namespace Facet
{
    /* One line of feedback under an editor: "Saved", an import's result, a count. Show(key) translates an English key
       through the host (and again on a relabel); Show(func) shows what the func returns, re-read on every refresh pass
       of its surface, for text already formatted or translated. seconds > 0 fades it out after that long and then
       falls back to the last Show with no seconds (the resting line, a count say), or empties if there is none; a
       later Show or Clear wins over a pending or running fade. Clear drops the resting line too. */
    public sealed class StatusLine
    {
        public VisualElement Root { get; }

        private readonly Label _text;
        private readonly Action _pass;
        private Func<string> _func;
        private Action _rest;   // re-shows the last Show with no seconds
        private Refresher _joined;
        private int _shown;   // which Show a pending fade belongs to

        internal StatusLine(VisualElement parent)
        {
            Root = Ui.Row(parent);
            Root.name = "status";
            _text = Ui.Muted("", Root);
            _text.style.whiteSpace = WhiteSpace.Normal;
            _text.style.flexShrink = 1f;
            _pass = () => { if (_func != null) { string s = _func() ?? ""; if (!string.Equals(_text.text, s)) _text.text = s; } };
            Events.On<AttachToPanelEvent>(Root, _ => Join());
            Events.On<DetachFromPanelEvent>(Root, _ => { if (_joined != null) _joined.Remove(_pass); _joined = null; });
            if (Root.panel != null) Join();
        }

        public void Show(string key, Role role = Role.TextMuted, float seconds = 0f)
        {
            _func = null;
            Words.Label(_text, key ?? "");
            if (seconds <= 0f) _rest = () => Show(key, role);
            Begin(role, seconds);
        }

        public void Show(Func<string> text, Role role = Role.TextMuted, float seconds = 0f)
        {
            Words.Label(_text, "");
            _func = text;
            Safe.Run(Host.For(Root), "Status text", _pass);
            if (seconds <= 0f) _rest = () => Show(text, role);
            Begin(role, seconds);
        }

        public void Clear()
        {
            _shown++;
            _func = null;
            _rest = null;
            Words.Label(_text, "");
            _text.style.opacity = 1f;
        }

        private void Begin(Role role, float seconds)
        {
            int shown = ++_shown;
            Paint.Fg(_text, role);
            _text.style.opacity = 1f;
            if (seconds <= 0f) return;
            Root.schedule.Execute(() =>
            {
                if (shown != _shown) return;
                // UI Toolkit keeps running a started animation, so it writes only while this Show is still the latest.
                Anim.Run(_text, 1f, 0f, Anim.Fast, (el, v) => { if (shown == _shown) el.style.opacity = v; }, () =>
                {
                    if (shown != _shown) return;
                    if (_rest != null) _rest(); else Clear();
                });
            }).StartingIn((long)(seconds * 1000f));
        }

        private void Join()
        {
            if (_joined != null) return;
            _joined = Refresher.Of(Root);
            if (_joined != null) _joined.Add(_pass, "Status '" + (Root.name ?? "status") + "'");
        }
    }

    public static partial class Controls
    {
        /// A status line (StatusLine) at the end of `parent`.
        public static StatusLine Status(VisualElement parent) => new StatusLine(parent);

        // A Live region's rebuild count, kept on its box.
        private sealed class LiveBuilds { public int N; }

        /* What a row at `at` adds to its history key: the rebuild count of every Live region around it, outermost
           first ("" outside any). Read from the tree, so a page built later inside the region (a tab clicked open)
           is tagged as well as one built during the region's own build, and a region inside another one's build
           keeps the outer count too. */
        internal static string LiveTag(VisualElement at)
        {
            string tag = "";
            for (var e = at; e != null; e = e.hierarchy.parent)
            {
                var b = Data.Find<LiveBuilds>(e);
                if (b != null) tag = "@" + b.N + tag;
            }
            return tag;
        }

        /* A box built by `build` once, then again whenever key() changes (object.Equals against the last key):
           checked on every refresh pass of its surface and on every Surface.Refresh(). An inspector keyed on a
           selection's version rebuilds when the selection changes and is left alone otherwise.

           A rebuild runs in the context the first build ran in (the Nav page and tab, the unnamed-row count), so a
           Tabs.Pages inside keeps its chosen tab when the new tabs still have that name, and falls back to the first
           otherwise. Rows' history keys gain the rebuild count: two builds' rows never coalesce into one step, even
           over the same object. The old box content is removed first, so its rows leave the refresher. */
        public static VisualElement Live(VisualElement parent, Func<object> key, Action<VisualElement> build)
        {
            object last = key == null ? null : Safe.Get(Host.For(parent), "Live key", key, null);
            return Region(parent, key == null ? null : (Func<bool>)(() =>
            {
                object k = key();
                if (Equals(k, last)) return false;
                last = k;
                return true;
            }), build);
        }

        /// The same, keyed on a version number compared by value: no allocation per refresh pass.
        public static VisualElement Live(VisualElement parent, Func<int> version, Action<VisualElement> build)
        {
            int last = version == null ? 0 : Safe.Get(Host.For(parent), "Live key", version, 0);
            return Region(parent, version == null ? null : (Func<bool>)(() =>
            {
                int v = version();
                if (v == last) return false;
                last = v;
                return true;
            }), build);
        }

        // `changed` says whether the key moved since it last asked; it runs inside the refresher's Safe, under its site.
        private static VisualElement Region(VisualElement parent, Func<bool> changed, Action<VisualElement> build)
        {
            var box = Ui.Box(parent);
            var builds = Data.Get<LiveBuilds>(box);
            var nav = Nav.Active;
            string tab = nav != null ? nav.Tab : null;
            int seq = Seq;
            var labels = Labels == null ? null : new System.Collections.Generic.Dictionary<string, int>(Labels);
            Action run = () => { if (build != null) Safe.Run(Host.For(box), "Live build", () => build(box)); };
            run();   // in the caller's context already
            Action rebuild = () =>
            {
                builds.N++;
                box.Clear();
                int s = Seq;
                var l = Labels;
                Seq = seq;
                Labels = labels == null ? null : new System.Collections.Generic.Dictionary<string, int>(labels);
                try { if (nav != null) nav.Within(tab, run); else run(); }
                finally { Seq = s; Labels = l; }
            };
            Action check = () => { if (changed != null && changed()) rebuild(); };
            Refresher joined = null;
            Action attach = () =>
            {
                if (joined != null) return;
                joined = Refresher.Of(box);
                if (joined != null) joined.Add(check, "Live '" + (box.name ?? "region") + "'");
            };
            Events.On<AttachToPanelEvent>(box, _ => attach());
            Events.On<DetachFromPanelEvent>(box, _ => { if (joined != null) joined.Remove(check); joined = null; });
            if (box.panel != null) attach();
            return box;
        }

        /// A box whose rows show only while shown() is true, asked on every refresh pass of its surface.
        public static VisualElement When(VisualElement parent, Func<bool> shown)
        {
            var box = Ui.Box(parent);
            if (shown != null) Hook(box, o => o.VisibleWhen = shown);
            return box;
        }

        /* A row's options on any element: Tooltip, TooltipText, EnabledWhen, VisibleWhen (asked on every refresh
           pass while it is on a panel), ContextMenu and ContextMenuItems, and Id naming it. */
        public static void Hook(VisualElement e, Action<ControlOptions> configure)
        {
            if (e == null) return;
            Hook(e, null, e.name, ControlOptions.Make(e, "Hook", configure));
        }

        private sealed class AfterChanges { public Action Run; }

        /* after runs once per Committed of every control under container (a row, a toggle in a card, a canvas
           excluded): once per click, typed commit or finished drag, never per drag frame, and once per undo or redo
           of a row's step while that row is on a panel (an undo commits only a row on a panel). Under Safe. Nested
           containers each run theirs, the innermost first. */
        public static void AfterChange(VisualElement container, Action after)
        {
            if (container == null || after == null) return;
            Data.Get<AfterChanges>(container).Run += after;
        }

        internal static void AfterChanged(VisualElement from, Host host)
        {
            for (var e = from; e != null; e = e.hierarchy.parent)
            {
                var a = Data.Find<AfterChanges>(e);
                if (a != null && a.Run != null) Safe.Run(host, "AfterChange", a.Run);
            }
        }
    }
}
