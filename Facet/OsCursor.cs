using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Facet
{
    /* The OS's own cursors, for a host that gives no HostOptions.Cursor: macOS through NSCursor, Windows through
       user32. Elsewhere (Linux), and for a kind or private macOS cursor the OS lacks, nothing is taken and Cursors
       draws its own. The selectors and ids are the ones Quartz's NativeCursor proved in game. */
    internal static class OsCursor
    {
        private static int _platform = -1;   // 0 none, 1 macOS, 2 Windows; read on first use, not at type load
        private static bool _off;            // a call that threw (a missing library): drawn cursors for the session

        /// Shows the OS cursor for kind; false when there is none here, so the caller draws it.
        internal static bool Set(Cursors.Kind kind)
        {
            if (_off) return false;
            try
            {
                if (_platform < 0) _platform = Platform();
                if (_platform == 1) return MacSet(MacName(kind));
                if (_platform == 2) return WinSet(WinId(kind));
                return false;
            }
            catch (Exception e)
            {
                _off = true;
                Host.Fallback.Log("OS cursors off, drawing them instead: " + e.Message);
                return false;
            }
        }

        private static int Platform()
        {
            var p = Application.platform;
            if (p == RuntimePlatform.OSXPlayer || p == RuntimePlatform.OSXEditor) return 1;
            if (p == RuntimePlatform.WindowsPlayer || p == RuntimePlatform.WindowsEditor) return 2;
            return 0;
        }

        // A kind added later maps to nothing, so it is drawn rather than shown as some other OS cursor.
        private static string MacName(Cursors.Kind kind)
        {
            switch (kind)
            {
                case Cursors.Kind.Default: return "arrowCursor";
                case Cursors.Kind.Text: return "IBeamCursor";
                case Cursors.Kind.ResizeHorizontal: return "resizeLeftRightCursor";
                case Cursors.Kind.ResizeVertical: return "resizeUpDownCursor";
                case Cursors.Kind.ResizeCorner: return "_windowResizeNorthWestSouthEastCursor";   // private: absent means drawn
                case Cursors.Kind.ResizeCornerNESW: return "_windowResizeNorthEastSouthWestCursor";
                case Cursors.Kind.Move: return "openHandCursor";
                default: return null;
            }
        }

        private static int WinId(Cursors.Kind kind)
        {
            switch (kind)
            {
                case Cursors.Kind.Default: return 32512;            // IDC_ARROW
                case Cursors.Kind.Text: return 32513;               // IDC_IBEAM
                case Cursors.Kind.ResizeCorner: return 32642;       // IDC_SIZENWSE
                case Cursors.Kind.ResizeCornerNESW: return 32643;   // IDC_SIZENESW
                case Cursors.Kind.ResizeHorizontal: return 32644;   // IDC_SIZEWE
                case Cursors.Kind.ResizeVertical: return 32645;     // IDC_SIZENS
                case Cursors.Kind.Move: return 32646;               // IDC_SIZEALL
                default: return 0;
            }
        }

        // ── macOS ──
        private const string Objc = "/usr/lib/libobjc.A.dylib";
        [DllImport(Objc, EntryPoint = "objc_getClass")] private static extern IntPtr objc_getClass(string name);
        [DllImport(Objc, EntryPoint = "sel_registerName")] private static extern IntPtr sel_registerName(string name);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern byte objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg);
        private static IntPtr _nsCursor, _responds, _set;
        private static readonly Dictionary<string, IntPtr> MacCursors = new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        private static bool MacSet(string name)
        {
            if (name == null) return false;
            IntPtr c;
            if (!MacCursors.TryGetValue(name, out c)) MacCursors[name] = c = MacResolve(name);
            if (c == IntPtr.Zero) return false;
            if (_set == IntPtr.Zero) _set = sel_registerName("set");
            objc_msgSend(c, _set);
            return true;
        }

        private static IntPtr MacResolve(string name)
        {
            if (_nsCursor == IntPtr.Zero) _nsCursor = objc_getClass("NSCursor");
            if (_nsCursor == IntPtr.Zero) return IntPtr.Zero;
            if (_responds == IntPtr.Zero) _responds = sel_registerName("respondsToSelector:");
            IntPtr sel = sel_registerName(name);
            return objc_msgSend_bool(_nsCursor, _responds, sel) == 0 ? IntPtr.Zero : objc_msgSend(_nsCursor, sel);
        }

        // ── Windows ──
        [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);
        [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
        private static readonly Dictionary<int, IntPtr> WinCursors = new Dictionary<int, IntPtr>();

        private static bool WinSet(int id)
        {
            if (id == 0) return false;
            IntPtr c;
            if (!WinCursors.TryGetValue(id, out c)) WinCursors[id] = c = LoadCursorW(IntPtr.Zero, (IntPtr)id);
            if (c == IntPtr.Zero) return false;
            SetCursor(c);
            return true;
        }
    }
}
