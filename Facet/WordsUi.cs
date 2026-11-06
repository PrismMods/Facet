using System;
using UnityEngine.UIElements;

namespace Facet {
    /* The Unity-typed half of Words (the rules are in Core/Words.cs). Every string is stored as its
       English key on the element, through Data, and translated by the element's own Host, so a
       language switch relabels an open window in place (Surface.Relabel) and a host's table never
       answers another host's strings. */
    public static partial class Words {
        sealed class Key {
            public string Text, Tip, Shown;
            public bool OwnText, OwnTip;
            public Action Hook;
        }

        static int _ownScope;

        /// Everything built inside the scope is Facet's own words (the colour picker's, the gradient editor's).
        internal static IDisposable OwnScope() { _ownScope++; return new Scope(); }
        sealed class Scope : IDisposable { public void Dispose() { _ownScope--; } }

        public static string T(string english, Host host) => T(english, host.Translate);

        public static string Own(string english, Host host) => Own(english, host.Language);

        /// The word as `host` shows it: Facet's table for its own words, else the host's.
        internal static string Show(string english, Host host, bool own)
            => own || _ownScope > 0 ? Own(english, host) : T(english, host);

        /// Sets the element's text from its English key, now and at every Relabel.
        public static void Label(TextElement e, string english, bool own = false) {
            var k = Data.Get<Key>(e);
            k.Text = english ?? ""; k.OwnText = own || _ownScope > 0;
            e.text = k.Shown = Show(k.Text, Host.For(e), k.OwnText);
        }

        /// The element's tooltip from its English key, kept up with the language like Label.
        public static void Tip(VisualElement e, string english, bool own = false) {
            var k = Data.Get<Key>(e);
            k.Tip = string.IsNullOrEmpty(english) ? null : english; k.OwnTip = own || _ownScope > 0;
            e.tooltip = k.Tip == null ? null : Show(k.Tip, Host.For(e), k.OwnTip);
        }

        /// Runs `redo` on every Relabel: for text built from parts (Undo + a label).
        public static void OnRelabel(VisualElement e, Action redo) { Data.Get<Key>(e).Hook = redo; }

        internal static void Relabel(VisualElement root) {
            Fonts.ForLanguage(Host.For(root));
            Walk(root);
        }

        static void Walk(VisualElement e) {
            var k = Data.Find<Key>(e);
            if (k != null) {
                var h = Host.For(e);
                var t = e as TextElement;
                // Text someone set by hand since (a readout, a key's name) is no longer ours to translate.
                if (t != null && k.Text != null && t.text == k.Shown) t.text = k.Shown = Show(k.Text, h, k.OwnText);
                if (k.Tip != null) e.tooltip = Show(k.Tip, h, k.OwnTip);
                if (k.Hook != null) Safe.Run(h, "Words.Relabel", k.Hook);
            }
            for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i]);
        }
    }
}
