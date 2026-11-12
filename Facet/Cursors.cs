using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* Mouse cursors for the parts of a panel that are not obviously draggable.

       UI Toolkit's USS `cursor` keywords are editor-only, and a runtime mod has no cursor assets,
       so these are drawn in code and swapped through UnityEngine.Cursor.SetCursor on enter and
       exit. That is a GLOBAL swap — there is no per-element cursor at runtime — which is why every
       element that sets one must also clear it, and why leaving the window has to restore the
       system arrow rather than whatever was set last.

       A host can show the OS's own cursors instead (HostOptions.Cursor): its hook is asked first on every
       enter and leave, and every frame while it holds one; a kind it declines is drawn here.

       Deliberately only the ambiguous ones. A resize edge and a text field look like anything else
       until the cursor says otherwise; a button already looks like a button, and the pointing hand
       is a web convention rather than a desktop one. */
    public static class Cursors
    {
        public enum Kind { Default, ResizeHorizontal, ResizeCorner, Text }

        private static Texture2D _horizontal, _corner, _text;
        private static Kind _current = Kind.Default;
        private static bool _drawn;   // a drawn texture is set, so a clear has to unset it

        /// Swap while the pointer is over this element, and restore when it leaves.
        public static void Set(VisualElement e, Kind kind)
        {
            if (e == null) return;
            Set(e, () => kind);
        }

        /* `kind` is asked on each enter (a rail handle has one only while the rail is open). The element's
           host's Cursor hook is tried first; while it holds a kind it is asked again every frame, because
           an OS cursor set from outside the engine is reset by the engine on the next pointer move. */
        internal static void Set(VisualElement e, Func<Kind> kind)
        {
            IVisualElementScheduledItem held = null;
            Kind now = Kind.Default;
            Action reapply = () => { if (_current == now && !_drawn) Native(Host.For(e), now); };
            Events.On<MouseEnterEvent>(e, _ =>
            {
                now = kind();
                if (!Apply(now, Host.For(e)) || now == Kind.Default) return;
                if (held == null) held = e.schedule.Execute(reapply).Every(0);
                else held.Resume();
            });
            Action<EventBase> clear = _ =>
            {
                if (held != null) held.Pause();
                Apply(Kind.Default, Host.For(e));
            };
            Events.On<MouseLeaveEvent>(e, _ => clear(_));
            // A panel can be hidden with the pointer still over it, which would otherwise strand
            // the cursor in whatever shape it last took.
            Events.On<DetachFromPanelEvent>(e, _ => clear(_));
        }

        /// Swap to `kind` (Default: the system arrow) with Facet's drawn cursors; no host hook is asked.
        public static void Apply(Kind kind) { Apply(kind, null); }

        // True when the host's hook took it; else the drawn cursor is set (or cleared).
        private static bool Apply(Kind kind, Host host)
        {
            if (Native(host, kind))
            {
                if (_drawn) Draw(Kind.Default);   // the hook's cursor shows only with no texture set over it
                _current = kind;
                return true;
            }
            if (kind != _current || (kind != Kind.Default) != _drawn) Draw(kind);
            _current = kind;
            return false;
        }

        // The hook and kind being asked, read by one cached Func: asked every frame while held, so no closure per ask.
        private static Func<Kind, bool> _hook;
        private static Kind _asking;
        private static readonly Func<bool> AskHook = () => _hook(_asking);

        private static bool Native(Host host, Kind kind)
        {
            var hook = host == null ? null : host.CursorHook;
            if (hook == null) return false;
            _hook = hook; _asking = kind;
            return Safe.Get(host, "Cursors hook", AskHook, false);
        }

        private static void Draw(Kind kind)
        {
            try
            {
                switch (kind)
                {
                    case Kind.ResizeHorizontal:
                        UnityEngine.Cursor.SetCursor(Horizontal(), new Vector2(11f, 11f), CursorMode.Auto);
                        break;
                    case Kind.ResizeCorner:
                        UnityEngine.Cursor.SetCursor(Corner(), new Vector2(11f, 11f), CursorMode.Auto);
                        break;
                    case Kind.Text:
                        UnityEngine.Cursor.SetCursor(TextBeam(), new Vector2(11f, 11f), CursorMode.Auto);
                        break;
                    default:
                        UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                        break;
                }
                _drawn = kind != Kind.Default;
            }
            catch (Exception e) { Host.Fallback.Log("Cursors: " + e.Message); }
        }

        private const int S = 23;   // odd, so the shapes have a true centre to point from

        private static Texture2D Horizontal()
        {
            if (_horizontal != null) return _horizontal;
            _horizontal = Draw((x, y) =>
            {
                int cy = S / 2;
                if (y == cy && x >= 4 && x <= S - 5) return true;              // shaft
                int d = Mathf.Abs(y - cy);
                if (d <= 4 && (x == 4 + d || x == S - 5 - d)) return true;     // heads
                return false;
            });
            return _horizontal;
        }

        private static Texture2D Corner()
        {
            if (_corner != null) return _corner;
            _corner = Draw((x, y) =>
            {
                if (x == y && x >= 5 && x <= S - 6) return true;               // shaft, top-left to bottom-right
                for (int i = 0; i <= 4; i++)
                {
                    if (x == 5 && y == 5 + i) return true;                     // upper head
                    if (y == 5 && x == 5 + i) return true;
                    if (x == S - 6 && y == S - 6 - i) return true;             // lower head
                    if (y == S - 6 && x == S - 6 - i) return true;
                }
                return false;
            });
            return _corner;
        }

        private static Texture2D TextBeam()
        {
            if (_text != null) return _text;
            _text = Draw((x, y) =>
            {
                int cx = S / 2;
                if (x == cx && y >= 4 && y <= S - 5) return true;              // stem
                if ((y == 4 || y == S - 5) && Mathf.Abs(x - cx) <= 3) return true;   // serifs
                return false;
            });
            return _text;
        }

        /* White with a black halo, so the cursor stays visible over a dark panel and over the
           bright game behind it — a plain white shape disappears against pale tiles. */
        private static Texture2D Draw(Func<int, int, bool> ink)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool on = ink(x, y);
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < S && ny < S && ink(nx, ny)) near = true;
                        }
                    // Texture rows run bottom-up; the shapes here are symmetric, so no flip needed.
                    px[y * S + x] = on ? Color.white : near ? new Color(0f, 0f, 0f, 0.85f) : Color.clear;
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }
    }
}
