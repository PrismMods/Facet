using System;
using UnityEngine.UIElements;

namespace Facet {
    public sealed class HostOptions {
        public Action<string> Log;
        public Func<string, string> Translate;
        public Func<string> Language;
        public Func<string, string> Read;
        public Action<string, string> Write;
        public Theme Theme;
        public int RefreshMs = 100;
        /// null keeps Facet's own font lists.
        public string[][] SansFonts, MonoFonts;
    }

    /* What a consumer (a mod) brings: its log, translator, language, persistence and theme. One per
       consumer, so one mod's settings never overwrite another's the way global statics did. A
       Surface is made for a Host and every element under it finds that Host with For(). Each
       member that calls consumer code goes through Safe, so a throwing delegate never escapes. */
    public sealed class Host {
        sealed class Ref { public Host Host; }

        readonly HostOptions _o;
        readonly Action<string> _log;
        readonly Theme _theme;

        public string Name { get; }
        public int RefreshMs => _o.RefreshMs;
        public string[][] SansFonts => _o.SansFonts;
        public string[][] MonoFonts => _o.MonoFonts;
        /// The consumer's scheme, else this host's own copy of Dark, so recolouring it never
        /// re-themes another mod's windows. Only Fallback wears the frozen Theme.Dark.
        public Theme Theme => _theme;

        Host(string name, HostOptions o) {
            Name = name; _o = o;
            _theme = o.Theme ?? new Theme();
            // Allocated once: Safe takes this on every guarded call, some of them per frame.
            _log = m => { try { if (_o.Log != null) _o.Log(m); } catch { } };
        }

        public static Host Create(string name, Action<HostOptions> configure = null) {
            var o = new HostOptions();
            if (configure != null) configure(o);
            return new Host(name ?? "Host", o);
        }

        static Host _fallback;
        /// For code with no surface to ask: logs through UnityEngine.Debug.Log.
        public static Host Fallback {
            get {
                if (_fallback != null) return _fallback;
                _fallback = Create("Facet", o => { o.Log = m => UnityEngine.Debug.Log("[Facet] " + m); o.Theme = Theme.Dark; });
                History.Log = Words.Log = _fallback._log;
                return _fallback;
            }
        }

        static Host _current;

        /// The host of the nearest surface above `e`, else the one in Use, else Fallback.
        public static Host For(VisualElement e) {
            for (var x = e; x != null; x = x.hierarchy.parent) {
                var r = Data.Find<Ref>(x);
                if (r != null && r.Host != null) return r.Host;
            }
            return _current ?? Fallback;
        }

        internal void Attach(VisualElement root) { Data.Get<Ref>(root).Host = this; }

        /// Elements built inside the scope, before they join a surface, resolve to `h`.
        public static IDisposable Use(Host h) { return new Scope(h); }

        sealed class Scope : IDisposable {
            readonly Host _prev;
            public Scope(Host h) { _prev = _current; _current = h; }
            public void Dispose() { _current = _prev; }
        }

        public void Log(string m) { _log(m); }

        public string Translate(string s) {
            if (_o.Translate == null) return s;
            var t = Safe.Get(Name, "Host.Translate", () => _o.Translate(s), null, _log);
            return string.IsNullOrEmpty(t) ? s : t;
        }

        /// "ko" for Korean; anything else is English.
        public string Language {
            get { return _o.Language == null ? "en" : Safe.Get(Name, "Host.Language", _o.Language, "en", _log); }
        }

        public string Read(string key) {
            return _o.Read == null ? null : Safe.Get(Name, "Host.Read", () => _o.Read(key), null, _log);
        }

        public void Write(string key, string value) {
            if (_o.Write != null) Safe.Run(Name, "Host.Write", () => _o.Write(key, value), _log);
        }

        internal Action<string> LogFn { get { return _log; } }
    }

    public static partial class Safe {
        public static bool Run(Host h, string site, Action fn) { return Run(h.Name, site, fn, h.LogFn); }

        public static T Get<T>(Host h, string site, Func<T> fn, T fallback) { return Get(h.Name, site, fn, fallback, h.LogFn); }
    }
}
