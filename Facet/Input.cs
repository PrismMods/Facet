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
        private static int _holdFrame = -1;

        // [0] top visible sortingOrder (or -1), [1] take Escape, [2] open, [3] text focused,
        // [4] blocks game input, [5] pointer over window (x, y)
        private static readonly List<object[]> _entries = Shared.Get("Facet.Entries", () => new List<object[]>());
        // [frame, result] of the last Escape, whichever version handled it
        private static readonly float[] _stamp = Shared.Get("Facet.EscapeStamp", () => new float[] { -1f, 0f });
        internal static readonly float[] BandNext = Shared.Get("Facet.BandNext",
            () => new float[] { 29000f, 30000f, 31000f, 31500f, 32000f });

        static Input()
        {
            lock (_entries)
                _entries.Add(new object[]
                {
                    (Func<float>)LocalTop, (Func<bool>)TakeEscape, (Func<bool>)(() => Ui.OpenWindows > 0),
                    (Func<bool>)LocalText, (Func<bool>)LocalBlocks, (Func<float, float, bool>)LocalPointer,
                });
        }

        public static void HoldThisFrame() => _holdFrame = Time.frameCount;

        public static bool HeldThisFrame => Time.frameCount == _holdFrame;

        public static bool AnyWindowOpen { get { return Any(2); } }
        public static bool TextFocused { get { return Any(3); } }
        public static bool BlocksGameInput { get { return Any(4); } }

        public static bool PointerOverWindow(Vector2 screen)
        {
            foreach (var e in Snapshot())
                if (((Func<float, float, bool>)e[5])(screen.x, screen.y)) return true;
            return false;
        }

        private static object[][] Snapshot() { lock (_entries) return _entries.ToArray(); }

        private static bool Any(int i)
        {
            foreach (var e in Snapshot()) if (((Func<bool>)e[i])()) return true;
            return false;
        }

        /* Escape, answered once per frame for every loaded version: the version whose topmost
           escapable window sits highest takes it. Returns false when nothing was open, so the caller
           lets the key through to the game. A mod that already spent this frame's Escape --
           cancelling a keybind capture -- says so with ConsumeEscape. */
        public static bool HandleEscape()
        {
            if (_stamp[0] == Time.frameCount) return _stamp[1] != 0f;
            _stamp[0] = Time.frameCount; _stamp[1] = 0f;
            object[] best = null; float top = -1f;
            foreach (var e in Snapshot())
            {
                var o = ((Func<float>)e[0])();
                if (o > top) { top = o; best = e; }
            }
            if (best != null && ((Func<bool>)best[1])()) _stamp[1] = 1f;
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

        // Walks the layers top-down: the first visible surface takes it. A focused text field gives
        // up focus first, then the surface's own handler runs, else it hides.
        private static bool TakeEscape()
        {
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                var s = Ui.Stack[i];
                if (!s.Visible) continue;
                var focused = s.Root != null && s.Root.panel != null && s.Root.panel.focusController != null
                    ? s.Root.panel.focusController.focusedElement as VisualElement : null;
                // The focus controller is the shared panel's, so only a field inside THIS surface counts.
                if (focused != null && s.Root.Contains(focused))
                    for (var e = focused; e != null; e = e.parent)
                        if (IsTextInput(e.GetType())) { focused.Blur(); Ui.Log("Escape: blurred a field in " + s.Name); return true; }
                if (s.EscapeHandler != null)
                {
                    bool r;
                    try { r = s.EscapeHandler(); Ui.Log("Escape: " + s.Name + " handler -> " + r); }
                    catch (Exception e) { Ui.Log("Escape handler threw: " + e.Message); r = true; }
                    return r;
                }
                s.Visible = false;
                Ui.Log("Escape: hid " + s.Name);
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
           uGUI EventSystem, which has never heard of a UI Toolkit TextField. Walks up from the
           focused element because focus usually sits on the field's inner text input. */
        private static bool LocalText()
        {
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                var surface = Ui.Stack[i];
                if (!surface.Visible || surface.Root == null || surface.Root.panel == null) continue;
                var fc = surface.Root.panel.focusController;
                for (var e = fc != null ? fc.focusedElement as VisualElement : null; e != null; e = e.parent)
                    if (IsTextInput(e.GetType())) return true;
            }
            return false;
        }

        private static bool IsTextInput(Type t)
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
                foreach (var e in surface.Root.Children())
                    if (e.worldBound.Contains(point)) return true;
            }
            return false;
        }

        /// Drive the wheel for the topmost open window of this version. The host calls this each
        /// frame with Input.mousePosition and Input.mouseScrollDelta.y -- see Skin.Wheel for why.
        public static void TickWheel(Vector2 screen, float delta)
        {
            if (Mathf.Approximately(delta, 0f)) return;
            /* Deliberately NOT standing down when a WheelEvent arrives: that says only that one
               reached the panel, not that any ScrollView acted on it. Skin.Wheel skips a target that
               already moved this frame, which is the honest version of the same check. */
            for (int i = Ui.Stack.Count - 1; i >= 0; i--)
            {
                if (!Ui.Stack[i].Visible) continue;
                Skin.Wheel(Ui.Stack[i].Root, screen, delta);
                return;
            }
        }
    }
}
