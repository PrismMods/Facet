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
        /// Sets an OS cursor for a kind and says whether it did; false falls back to Facet's drawn one. Return false
        /// for a kind you do not know: Cursors.Kind is appended to within a minor.
        public Func<Cursors.Kind, bool> Cursor;
        /// Panel px one wheel unit scrolls (Input.mouseScrollDelta 1, a notch), linear on the polled and the
        /// panel's own wheel path alike; asked on every scroll, so a setting applies at once. null or <= 0
        /// keeps Facet's own (a notch 60, a trackpad fraction x400).
        public Func<float> WheelSpeed;
        public int RefreshMs = 100;
        /// null keeps Facet's own font lists.
        public string[][] SansFonts, MonoFonts;
        /// A row built with no Id takes its label as its Id (the history key and the element name), and the second
        /// row with that label on a page takes "label#2". Off: an unnamed row is numbered, as before.
        public bool LabelIds;
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
            var h = new Host(name ?? "Host", o);
            if (h.Name == "Facet") { if (!_makingFallback) h.Log("host name 'Facet' is reserved for Host.Fallback; not registered"); }
            else if (Named_.ContainsKey(h.Name)) h.Log("a host named '" + h.Name + "' already exists; Host.Named keeps the first");
            else Named_[h.Name] = h;
            return h;
        }

        static readonly System.Collections.Generic.Dictionary<string, Host> Named_ = new System.Collections.Generic.Dictionary<string, Host>();

        static bool _makingFallback;

        /// The first host created under `name` (later ones with the same name are not registered), else null.
        /// "Facet" is Host.Fallback.
        public static Host Named(string name) {
            if (name == "Facet") return Fallback;
            Host h;
            return name != null && Named_.TryGetValue(name, out h) ? h : null;
        }

        static Host _fallback;
        /// For code with no surface to ask: logs through UnityEngine.Debug.Log.
        public static Host Fallback {
            get {
                if (_fallback != null) return _fallback;
                _makingFallback = true;
                try { _fallback = Create("Facet", o => { o.Log = m => UnityEngine.Debug.Log("[Facet] " + m); o.Theme = Theme.Dark; }); }
                finally { _makingFallback = false; }
                History.Log = _fallback._log;
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

        // Every live surface made for this host, in creation order: a surface adds itself and leaves on Dispose.
        internal readonly System.Collections.Generic.List<Surface> Surfaces = new System.Collections.Generic.List<Surface>();

        /// Relabel every surface made for this host (its windows, a SettingsWindow's), after a language change.
        public void RelabelSurfaces()
        {
            foreach (var s in Surfaces.ToArray()) Safe.Run(this, "Surface '" + s.Name + "' relabel", s.Relabel);
        }

        /// Dispose every surface made for this host. The host itself stays: Host.Named still returns it.
        /// One surface that throws is logged and the rest are still disposed (the same holds for RelabelSurfaces).
        public void DisposeSurfaces()
        {
            foreach (var s in Surfaces.ToArray()) Safe.Run(this, "Surface '" + s.Name + "' dispose", s.Dispose);
        }

        public void Log(string m) { _log(m); }

        public string Translate(string s) {
            if (_o.Translate == null) return s;
            // No lambda: Words.Own and T ask on every refresh pass, and a closure here would allocate per word.
            string t;
            try { t = _o.Translate(s); }
            catch (Exception e) { Safe.Report(Name, "Host.Translate", e, _log); t = null; }
            return string.IsNullOrEmpty(t) ? s : t;
        }

        /// The host's language reduced to its primary subtag ("ko-KR" reads "ko"); Facet's table knows "ko", anything
        /// else is English. Never null or empty: Nav keys its indexes by it.
        public string Language {
            get {
                string l = _o.Language == null ? null : Safe.Get(Name, "Host.Language", _o.Language, "en", _log);
                if (string.IsNullOrEmpty(l)) return "en";
                // A host answering a full tag ("ko-KR") on every read pays the reduction once per tag, not per read.
                if (l != _rawLanguage) { _language = Words.Primary(l); _rawLanguage = l; }
                return _language;
            }
        }
        private string _rawLanguage, _language;

        public string Read(string key) {
            return _o.Read == null ? null : Safe.Get(Name, "Host.Read", () => _o.Read(key), null, _log);
        }

        public void Write(string key, string value) {
            if (_o.Write != null) Safe.Run(Name, "Host.Write", () => _o.Write(key, value), _log);
        }

        internal Action<string> LogFn { get { return _log; } }

        internal Func<Cursors.Kind, bool> CursorHook => _o.Cursor;

        internal bool LabelIds => _o.LabelIds;

        /// The host's wheel speed, or 0 for Facet's own; asked on every scroll.
        internal float WheelSpeed => _o.WheelSpeed == null ? 0f : Safe.Get(this, "WheelSpeed", _o.WheelSpeed, 0f);
    }

    public static partial class Safe {
        public static bool Run(Host h, string site, Action fn) { return Run(h.Name, site, fn, h.LogFn); }

        public static T Get<T>(Host h, string site, Func<T> fn, T fallback) { return Get(h.Name, site, fn, fallback, h.LogFn); }

        // Each handler under its own guard, so one that throws leaves the rest to run; no closure per raise.
        internal static void Each(Host h, string site, Action[] handlers) {
            if (handlers == null) return;
            for (int i = 0; i < handlers.Length; i++) Run(h, site, handlers[i]);
        }

        internal static void Each<T>(Host h, string site, Action<T>[] handlers, T value) {
            if (handlers == null) return;
            for (int i = 0; i < handlers.Length; i++) {
                try { handlers[i](value); }
                catch (Exception e) { Report(h.Name, site, e, h.LogFn); }
            }
        }
    }
}
