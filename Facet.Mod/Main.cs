using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityModManagerNet;

namespace Facet.Mod
{
    // The only place that names a loader. Facet types are touched in NoInlining methods only, so
    // Load itself can run before Facet.0.1.dll has resolved.
    public static class Main
    {
        static string _log, _dir;
        static bool _auto, _started;

        internal static void Log(string s)
        {
            try { File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n"); } catch { }
        }

        public static bool Load(UnityModManager.ModEntry e)
        {
            _dir = e.Path;
            _log = Path.Combine(_dir, "gallery.log");
            _auto = File.Exists(Path.Combine(_dir, "autotest"));
            AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
            {
                var n = new AssemblyName(a.Name).Name;
                if (!n.StartsWith("Facet.")) return null;
                Log("AssemblyResolve fallback for " + n);
                var p = Path.Combine(_dir, n + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
            e.OnUpdate = (m, dt) => Tick();
            Log("Facet.Mod loaded");
            return true;
        }

        static void Tick()
        {
            if (_auto)
            {
                if (_started || Time.frameCount < 60) return;
                _started = true;
                Start();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.F8)) Toggle();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Start()
        {
            new GameObject("FacetAutoTest").AddComponent<AutoTest>();
            Log("autotest armed");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Toggle()
        {
            if (Gallery.Surface != null && Gallery.Surface.Visible) Gallery.Close(); else Gallery.Open();
        }
    }
}
