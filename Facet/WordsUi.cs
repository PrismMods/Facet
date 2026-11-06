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
            public Host Labelled;
            public bool Watching;
            public bool OwnText, OwnTip;
            public bool Upper;   // Ui.Caps: upper-cased after translating, at every relabel
            public Action Hook;
        }

        static int _ownScope;

        /// Everything built inside the scope is Facet's own words (the colour picker's, the gradient editor's).
        internal static IDisposable OwnScope() { _ownScope++; return new Scope(); }
        sealed class Scope : IDisposable { public void Dispose() { _ownScope--; } }

        /// How `e`'s host translates, or Facet's own table inside an OwnScope; fixed when asked.
        internal static Func<string, string> Translator(VisualElement e)
        {
            var h = Host.For(e);
            if (_ownScope > 0) return s => Own(s, h.Language);
            return h.Translate;
        }

        public static string T(string english, Host host) => T(english, host.Translate);

        public static string Own(string english, Host host) => Own(english, host.Language);

        /// The word as `host` shows it: Facet's table for its own words, else the host's.
        internal static string Show(string english, Host host, bool own)
            => own || _ownScope > 0 ? Own(english, host) : T(english, host);

        /// Sets the element's text from its English key, now and at every Relabel.
        public static void Label(TextElement e, string english, bool own = false) {
            var k = Data.Get<Key>(e);
            k.Text = english ?? ""; k.OwnText = own || _ownScope > 0;
            e.text = k.Shown = Shown(k, k.Labelled = Host.For(e));
            Watch(e, k);
        }

        /// Label, upper-cased after it is translated (Ui.Caps). Upper-casing the key first would miss
        /// the host's table: "TINT" is not "Tint".
        internal static void Caps(TextElement e, string english, bool own) {
            Data.Get<Key>(e).Upper = true;
            Label(e, english, own);
        }

        static string Shown(Key k, Host h) {
            var s = Show(k.Text, h, k.OwnText);
            return k.Upper ? s.ToUpperInvariant() : s;
        }

        /// The element's tooltip from its English key, kept up with the language like Label.
        public static void Tip(VisualElement e, string english, bool own = false) {
            var k = Data.Get<Key>(e);
            k.Tip = string.IsNullOrEmpty(english) ? null : english; k.OwnTip = own || _ownScope > 0;
            e.tooltip = k.Tip == null ? null : Show(k.Tip, k.Labelled = Host.For(e), k.OwnTip);
            Watch(e, k);
        }

        // An element built before it had a parent was translated by the wrong host: fix it on joining a panel.
        static void Watch(VisualElement e, Key k)
        {
            if (k.Watching) return;
            k.Watching = true;
            Events.On<AttachToPanelEvent>(e, _ => { if (!ReferenceEquals(Host.For(e), k.Labelled)) Retext(e, k); });
        }

        /// Runs `redo` on every Relabel: for text built from parts (Undo + a label).
        public static void OnRelabel(VisualElement e, Action redo) { Data.Get<Key>(e).Hook = redo; }

        internal static void Relabel(VisualElement root) {
            Fonts.ForLanguage(Host.For(root));
            Walk(root);
        }

        static void Retext(VisualElement e, Key k) {
            var h = k.Labelled = Host.For(e);
            var t = e as TextElement;
            // Text someone set by hand since (a readout, a key's name) is no longer ours to translate.
            if (t != null && k.Text != null && t.text == k.Shown) t.text = k.Shown = Shown(k, h);
            if (k.Tip != null) e.tooltip = Show(k.Tip, h, k.OwnTip);
            if (k.Hook != null) Safe.Run(h, "Words.Relabel", k.Hook);
        }

        static void Walk(VisualElement e) {
            var k = Data.Find<Key>(e);
            if (k != null) Retext(e, k);
            for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i]);
        }
    }
}
