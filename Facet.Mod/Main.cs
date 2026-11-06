using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
#if ML
using MelonLoader;

[assembly: MelonInfo(typeof(Facet.Mod.FacetMelon), "Facet Gallery", "0.1.0", "PrismMods")]
// No game named: MelonLoader loads a mod with no MelonGame attribute into every game.
#else
using UnityModManagerNet;
#endif

namespace Facet.Mod
{
    // The only place that names a loader. Facet types are touched in NoInlining methods only, so
    // Load itself can run before Facet.0.1.dll has resolved.
    public static class Main
    {
        static string _log, _dir;
        static bool _auto, _started;

        internal static string LogPath => _log;

        internal static void Log(string s)
        {
            try { File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n"); } catch { }
        }

#if !ML
        public static bool Load(UnityModManager.ModEntry e)
        {
            Init(e.Path, e.Path, "gallery.log", "autotest");
            e.OnUpdate = (m, dt) => Tick();
            return true;
        }
#endif

        // dir holds the Facet.*.dll fallback; data holds the log and the autotest trigger.
        internal static void Init(string dir, string data, string logName, string triggerName)
        {
            _dir = dir;
            Directory.CreateDirectory(data);
            _log = Path.Combine(data, logName);
            _auto = File.Exists(Path.Combine(data, triggerName));
            AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
            {
                var n = new AssemblyName(a.Name).Name;
                if (!n.StartsWith("Facet.")) return null;
                Log("AssemblyResolve fallback for " + n);
                var p = Path.Combine(_dir, n + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
            Log("Facet.Mod loaded");
        }

        internal static void Tick()
        {
            if (_auto)
            {
                if (_started || Time.frameCount < 60) return;
                _started = true;
                Start();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.F8)) Toggle();
            Frame();
        }

        // The host's per-frame duties: Escape closes the topmost window, and the wheel is polled
        // because a runtime panel does not reliably receive WheelEvent.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Frame()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Facet.Input.HandleEscape();
            Facet.Input.TickWheel(UnityEngine.Input.mousePosition, UnityEngine.Input.mouseScrollDelta.y);
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
#if ML
    public class FacetMelon : MelonMod
    {
        public override void OnInitializeMelon()
        {
            // MelonLoader already loaded UserLibs/Facet.0.1.dll; the log and trigger live in UserData/Facet.
            Main.Init(MelonLoader.Utils.MelonEnvironment.UserLibsDirectory, Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "Facet"), "gallery.log", "autotest");
        }

        public override void OnUpdate() => Main.Tick();
    }
#endif
}
