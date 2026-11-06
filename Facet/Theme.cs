using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* A mod's colour scheme, as a colour per Role. Sizes stay global in Tokens; colours are per
       window, so a Quartz window can be red while another mod's beside it is blue.

       An element finds its scheme with For(): the nearest ancestor that was given one (a Surface,
       or a popup card that took its anchor's), else the scheme of the build in progress (Use), else
       its host's, else Dark.

       Elements remember the ROLE they draw with (Paint), not the colour, so Recolour re-themes what
       is already on screen: each surface wearing the scheme re-applies on Changed. */
    public sealed class Theme
    {
        /// Facet's own dark scheme. Frozen: every mod on this Facet shares it, so a host without a
        /// theme gets its own copy (Host.Theme) and only Host.Fallback wears Dark itself.
        public static readonly Theme Dark = new Theme(new[] {
            new Color(0.102f, 0.102f, 0.122f, 0.98f),  // Panel
            new Color(1f, 1f, 1f, 0.10f),              // PanelBorder
            new Color(0.078f, 0.078f, 0.094f, 1f),     // TitleBar
            new Color(1f, 1f, 1f, 0.025f),             // Row
            new Color(1f, 1f, 1f, 0.05f),              // RowAlt
            new Color(1f, 1f, 1f, 0.08f),              // RowHover
            new Color(0.92f, 0.92f, 0.94f, 1f),        // Text
            new Color(0.58f, 0.58f, 0.62f, 1f),        // TextMuted
            new Color(0.604f, 0.706f, 1f, 1f),         // Accent
            new Color(0.886f, 0.404f, 0.427f, 1f),     // Danger
            new Color(0.06f, 0.06f, 0.08f, 1f),        // AccentText
            new Color(0.604f, 0.706f, 1f, 1f),         // Focus
        });

        private readonly Color[] _c;

        // Sized by the Role count, not the list above: a Role appended without a Dark colour is clear here
        // rather than an IndexOutOfRange in Derive.
        private Theme(Color[] c)
        {
            _c = new Color[Palette.Count];
            Array.Copy(c, _c, Math.Min(c.Length, _c.Length));
        }

        /// A copy of Dark, to adjust.
        public Theme() { _c = (Color[])Dark._c.Clone(); }

        /// Setting a role does not raise Changed; Paint.Apply(root) redraws by hand.
        public Color this[Role r]
        {
            get { return _c[(int)r]; }
            set { Writable(); _c[(int)r] = value; }
        }

        /// Raised once by Recolour. Each surface wearing this scheme re-applies its roles.
        public event Action Changed;

        /// A whole scheme from one colour.
        public static Theme FromAccent(Color accent, float tint = 1f)
        {
            var t = new Theme();
            t.Derive(accent, tint);
            return t;
        }

        /// Re-derive every role from `accent` (Palette), in place, and redraw whatever wears it.
        public void Recolour(Color accent, float tint = 1f)
        {
            Writable();
            Derive(accent, tint);
            var h = Changed;
            if (h == null) return;
            // One subscriber's throw must not keep the rest from re-theming.
            foreach (Action a in h.GetInvocationList()) Safe.Run(Host.Fallback, "Theme.Changed", a);
        }

        private void Writable()
        {
            if (ReferenceEquals(this, Dark))
                throw new InvalidOperationException("Theme.Dark is shared by every mod; give your host its own: o.Theme = new Theme()");
        }

        private void Derive(Color accent, float tint)
        {
            var p = Palette.FromAccent(accent.r, accent.g, accent.b, tint);
            for (int i = 0; i < p.Length; i++) _c[i] = new Color(p[i][0], p[i][1], p[i][2], p[i][3]);
        }

        private sealed class Ref { public Theme Theme; }
        private static Theme _building;
        internal static Theme Building { get { return _building; } }

        /// Give `e` and everything under it this scheme. Null clears it.
        public static void Set(VisualElement e, Theme t)
        {
            if (e == null) return;
            if (t != null) Data.Get<Ref>(e).Theme = t; else Data.Clear<Ref>(e);
        }

        /// The scheme `e` should draw with. Null-safe; never returns null.
        public static Theme For(VisualElement e)
        {
            for (var x = e; x != null; x = x.hierarchy.parent)
            {
                var r = Data.Find<Ref>(x);
                if (r != null) return r.Theme;
            }
            return _building ?? Host.For(e).Theme;
        }

        /// The scheme given to `e` itself, or null. Paint.Apply hands schemes down its walk with it.
        internal static Theme Own(VisualElement e)
        {
            var r = Data.Find<Ref>(e);
            return r != null ? r.Theme : null;
        }

        /// For a build that happens before its elements join the tree: `using (Theme.Use(t)) { … }`.
        /// A Surface created inside the scope keeps the scheme — how a panel built in its constructor
        /// gets one: `using (Theme.Use(mine)) window = new MyWindow(…);`.
        public static IDisposable Use(Theme t) { return new Scope(t); }

        private sealed class Scope : IDisposable
        {
            private readonly Theme _prev;
            public Scope(Theme t) { _prev = _building; _building = t; }
            public void Dispose() { _building = _prev; }
        }
    }
}
