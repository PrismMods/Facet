using System;
using UnityEngine.UIElements;

namespace Facet
{
    public static partial class Controls
    {
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
            Action run = () => { if (build != null) Safe.Run(Host.For(box), "Live build", () => build(box)); };
            run();   // in the caller's context already
            Action rebuild = () =>
            {
                builds.N++;
                box.Clear();
                int s = Seq;
                Seq = seq;
                try { if (nav != null) nav.Within(tab, run); else run(); }
                finally { Seq = s; }
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
    }
}
