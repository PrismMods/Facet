using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* Facet's own input state; the host reads it. A surface that has just hidden calls
       HoldThisFrame so the key that closed it (still down for the rest of the frame) does not
       reach the game as well.

       Also where loaded Facet versions agree (see Shared): each registers one entry of delegates;
       the queries OR over all entries and Escape goes to the version with the topmost window. */
    public static class Input
    {
        // Shared: a window of one version closing on Escape holds the key from a host reading another version.
        private static readonly int[] _holdFrame = Shared.Get("Facet.HoldFrame", () => new[] { -1 });

        // Entry layout: see Coord.
        internal static readonly List<object[]> Entries = Shared.Get("Facet.Entries", () => new List<object[]>());
        // [frame, result] of the last Escape, whichever version handled it
        private static readonly float[] _stamp = Shared.Get("Facet.EscapeStamp", () => new float[] { -1f, 0f });
        internal static readonly float[] BandNext = Shared.Get("Facet.BandNext",
            () => new float[] { 29000f, 30000f, 31000f, 31500f, 32000f });

        static Input()
        {
            Coord.Register(Entries, new object[]
            {
                (Func<float>)LocalTop, (Func<bool>)TakeEscape, (Func<bool>)(() => Ui.OpenWindows > 0),
                (Func<bool>)LocalText, (Func<bool>)LocalBlocks, (Func<float, float, bool>)LocalPointer,
                typeof(Input).Assembly.FullName,
                (Func<int, float[]>)LocalOrders, (Action<int, Func<float, float>>)LocalRemap,
                (Action<float, float, float>)LocalWheel, (Func<bool>)LocalGesture,
            });
        }

        private static float[] LocalOrders(int layer)
        {
            var r = new List<float>();
            foreach (var s in Ui.Stack) if ((int)s.Layer == layer && s.Order >= 0f) r.Add(s.Order);
            return r.ToArray();
        }

        private static void LocalRemap(int layer, Func<float, float> f)
        {
            foreach (var s in Ui.Stack) if ((int)s.Layer == layer && s.Order >= 0f) s.Order = f(s.Order);
        }

        /// The cancel of the drag in progress (a canvas move, resize or pan), null when none: it puts the start back and
        /// records nothing. Set by whoever starts the drag and cleared when it ends. It takes the frame's Escape first.
        internal static Action Gesture;

        /// True while Escape blurs a focused field: a field that commits on focus loss (an in-place rename) cancels instead.
        internal static bool Escaping;

        public static void HoldThisFrame() => _holdFrame[0] = Time.frameCount;

        public static bool HeldThisFrame => Time.frameCount == _holdFrame[0];

        public static bool AnyWindowOpen { get { return Coord.Any(Entries, 2, Host.Fallback.LogFn); } }
        public static bool TextFocused { get { return Coord.Any(Entries, 3, Host.Fallback.LogFn); } }
        /// One question for a host's input gate: is the pointer or the keyboard ours right now?
        public static bool WantsInput(Vector2 pointerScreen) => BlocksGameInput || PointerOverWindow(pointerScreen) || TextFocused;

        public static bool BlocksGameInput { get { return Coord.Any(Entries, 4, Host.Fallback.LogFn); } }

        public static bool PointerOverWindow(Vector2 screen) => Coord.Pointer(Entries, screen.x, screen.y, Host.Fallback.LogFn);

        /* Escape, answered once per frame for every loaded version: a drag in progress in any version
           takes it, else the version whose topmost escapable window sits highest takes it. Returns false when nothing was open, so the caller
           lets the key through to the game. A mod that already spent this frame's Escape --
           cancelling a keybind capture -- says so with ConsumeEscape. */
        public static bool HandleEscape()
        {
            if (_stamp[0] == Time.frameCount) return _stamp[1] != 0f;
            _stamp[0] = Time.frameCount; _stamp[1] = 0f;
            if (Coord.TakeEscape(Entries, Host.Fallback.LogFn)) _stamp[1] = 1f;
            return _stamp[1] != 0f;
        }

        /// This frame's Escape is spent (a keybind capture took it) -- HandleEscape will not act on it.
        public static void ConsumeEscape() { _stamp[0] = Time.frameCount; _stamp[1] = 1f; }

        private static float LocalTop()
        {
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
                if (Ui.Stack[i].Visible) return Ui.Stack[i].Order;
            return -1f;
        }

        /* A drag in progress in this copy takes Escape first, whichever version's host asked: it puts the drag's start
           back and records nothing. */
        private static bool LocalGesture()
        {
            var gesture = Gesture;
            if (gesture == null) return false;
            try { gesture(); } catch (Exception e) { Safe.Report("Facet", "Escape: gesture cancel", e, Host.Fallback.LogFn); }
            Gesture = null;
            return true;
        }

        // A drag in progress takes it first (for a version whose shared walk does not ask every copy). Then the layers,
        // top-down: the first visible surface takes it. A focused text field gives up focus first, then a focused
        // canvas in it clears its selection, then the surface's own handler runs, else it hides.
        private static bool TakeEscape()
        {
            if (LocalGesture()) return true;
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                var s = Ui.Stack[i];
                if (!s.Visible) continue;
                var focused = s.Root != null && s.Root.panel != null && s.Root.panel.focusController != null
                    ? s.Root.panel.focusController.focusedElement as VisualElement : null;
                // The focus controller is the shared panel's, so only a field inside THIS surface counts.
                if (focused != null && s.Root.Contains(focused))
                    if (InTextInput(focused))
                    {
                        Escaping = true;
                        try { focused.Blur(); } finally { Escaping = false; }
                        s.Host.Log("Escape: blurred a field in " + s.Name);
                        return true;
                    }
                if (CanvasAll.Escape(s.Root)) { s.Host.Log("Escape: cleared a canvas selection in " + s.Name); return true; }
                if (s.EscapeHandler != null)
                {
                    bool r = Safe.Get(s.Host, "Escape handler of " + s.Name, s.EscapeHandler, true);
                    s.Host.Log("Escape: " + s.Name + " handler -> " + r);
                    return r;
                }
                s.Visible = false;
                s.Host.Log("Escape: hid " + s.Name);
                return true;
            }
            return false;
        }

        private static bool LocalBlocks()
        {
            for (int i = 0; i < Ui.Stack.Count; i++)
                if (Ui.Stack[i].Visible && Ui.Stack[i].BlocksGameInput) return true;
            return false;
        }

        /* The user is typing into one of our windows. The game's "editing a field" test asks the
           uGUI EventSystem, which has never heard of a UI Toolkit TextField. Each surface keeps a
           flag set by its FocusIn / FocusOut (Surface.TextIn), so this is a bool per surface, not a
           walk of focus controllers. The flag is re-checked here only when set, because focus can
           also leave without an event (the page holding the field was rebuilt away); hide and
           Dispose clear it outright. */
        private static bool LocalText()
        {
            bool any = false;
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                var s = Ui.Stack[i];
                if (!s.TextIn) continue;
                if (s.Visible && s.Root != null && s.Root.panel != null)
                {
                    var fc = s.Root.panel.focusController;
                    var f = fc != null ? fc.focusedElement as VisualElement : null;
                    if (f != null && s.Root.Contains(f) && InTextInput(f)) { any = true; continue; }
                }
                s.TextIn = false;
            }
            return any;
        }

        /// Is e, or an ancestor (focus sits on the field's inner input), a text field?
        internal static bool InTextInput(VisualElement e)
        {
            for (; e != null; e = e.parent)
                if (IsTextInput(e.GetType())) return true;
            return false;
        }

        internal static bool IsTextInput(Type t)
        {
            for (; t != null && t != typeof(VisualElement); t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(TextInputBaseField<>)) return true;
            return false;
        }

        /* Is the pointer over one of our windows? A question about position, not about whether
           anything is open, so the editor's scroll-zoom works with a panel elsewhere on screen. */
        private static bool LocalPointer(float x, float y)
        {
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                var surface = Ui.Stack[i];
                if (!surface.Visible || surface.Root == null || surface.Root.panel == null) continue;
                var point = RuntimePanelUtils.ScreenToPanel(surface.Root.panel, new Vector2(x, Screen.height - y));
                var h = surface.Root.hierarchy;   // by index: Children() allocates an enumerator per call
                for (int c = 0; c < h.childCount; c++)
                    if (h[c].worldBound.Contains(point)) return true;
            }
            return false;
        }

        private static readonly int[] _wheelStamp = Open();

        private static string _wheelProblem;

        // A bad shared value must never break input: fall back to a private stamp and report once
        // (TakeWheelProblem). Never touches Host here: a throw would escape the static initialiser.
        private static int[] Open()
        {
            try
            {
                var a = Shared.Get<object>("Facet.WheelStamp", () => new[] { -1 }) as int[];
                if (a != null && a.Length >= 1) return a;
                _wheelProblem = "Facet.WheelStamp in the shared store is not an int[] of length >= 1; using a private stamp";
            }
            catch (Exception e) { _wheelProblem = "Facet.WheelStamp could not be read (" + e.Message + "); using a private stamp"; }
            return new[] { -1 };
        }

        /// The message to log through the fallback host, once; null when the store was fine.
        internal static string TakeWheelProblem() { var p = _wheelProblem; _wheelProblem = null; return p; }

        /// Drive the wheel for the topmost open window of any loaded version, once per frame. The host
        /// calls this each frame with Input.mousePosition and Input.mouseScrollDelta.y -- see Skin.Wheel
        /// for why. A zero delta does not use up the frame.
        public static void TickWheel(Vector2 screen, float delta) =>
            Coord.Wheel(Entries, _wheelStamp, Time.frameCount, screen.x, screen.y, delta, Host.Fallback.LogFn);

        /* Deliberately NOT standing down when a WheelEvent arrives: that says only that one
           reached the panel, not that any ScrollView acted on it. Skin.Wheel skips a target that
           already moved this frame, which is the honest version of the same check. Over a canvas's
           stage the canvas takes it (it skips a frame its own wheel event fed). */
        private static void LocalWheel(float x, float y, float delta)
        {
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                if (!Ui.Stack[i].Visible) continue;
                if (CanvasAll.Wheel(Ui.Stack[i].Root, x, y, delta)) return;
                Skin.Wheel(Ui.Stack[i].Root, new Vector2(x, y), delta);
                return;
            }
        }
    }
}
