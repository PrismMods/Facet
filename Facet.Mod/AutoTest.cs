using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    // Scripted in-game test: synthetic UI Toolkit events against the real panel. Each task appends
    // one Section...() iterator to Start and logs through Check.
    sealed class AutoTest : MonoBehaviour
    {
        int _pass, _fail;

        internal void Check(bool ok, string what, string detail = "")
        {
            if (ok) _pass++; else _fail++;
            Main.Log((ok ? "PASS " : "FAIL ") + what + (ok || detail == "" ? "" : "  [" + detail + "]"));
        }

        VisualElement Root => Gallery.Surface?.Root;

        float K()
        {
            var r = Root;
            return r == null ? 0f : r.layout.width / Screen.width;
        }

        void Pointer(EventType type, Vector2 screen)
        {
            float k = K();
            var ev = new Event { type = type, button = 0, clickCount = 1,
                mousePosition = new Vector2(screen.x * k, (Screen.height - screen.y) * k) };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(ev)
                        : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev)
                        : (EventBase)PointerMoveEvent.GetPooled(ev);
            using (e) Root.panel.visualTree.SendEvent(e);
        }

        IEnumerator Drag(Vector2 from, Vector2 to, int steps = 6)
        {
            Pointer(EventType.MouseDown, from); yield return null;
            for (int i = 1; i <= steps; i++) { Pointer(EventType.MouseDrag, Vector2.Lerp(from, to, i / (float)steps)); yield return null; }
            Pointer(EventType.MouseUp, to); yield return null; yield return null;
        }

        // Down and up in the same frame: a real pointer's own move event between them would turn
        // a still click into a drag.
        IEnumerator Click(Vector2 at)
        {
            Pointer(EventType.MouseDown, at);
            Pointer(EventType.MouseUp, at); yield return null; yield return null;
        }

        void Submit(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        }

        void Type(TextField f, string text)
        {
            f.SetValueWithoutNotify(text);
            Key(f, KeyCode.Return);
        }

        void Key(VisualElement target, KeyCode code)
        {
            char c = code == KeyCode.Return ? '\n' : '\0';
            using (var ke = KeyDownEvent.GetPooled(c, code, EventModifiers.None)) { ke.target = target; target.SendEvent(ke); }
        }

        // Centre of an element in screen pixels, the inverse of Pointer's mapping.
        Vector2 Ctr(VisualElement e)
        {
            float k = K();
            var c = e.worldBound.center;
            return new Vector2(c.x / k, Screen.height - c.y / k);
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 5; i++) yield return null;
            Main.Log("AUTOTEST start screen " + Screen.width + "x" + Screen.height);
            yield return Run("SectionOpen", SectionOpen());
            yield return Run("SharedEscape", SharedEscape());
            // Later tasks: yield return Run("Section...", Section...());

            Main.Log("AUTOTEST DONE pass=" + _pass + " fail=" + _fail);
            yield return null;
            Application.Quit();
        }

        // Drives a section by hand so a throw is logged to gallery.log and the run carries on.
        IEnumerator Run(string name, IEnumerator section)
        {
            while (true)
            {
                try { if (!section.MoveNext()) yield break; }
                catch (Exception ex)
                {
                    var st = (ex.StackTrace ?? "").Split('\n')[0].Trim();
                    Check(false, name + " threw", ex.Message + " @ " + st);
                    yield break;
                }
                yield return section.Current;
            }
        }


        IEnumerator SharedEscape()
        {
            Gallery.Open(); yield return null;
            Check(Input.AnyWindowOpen, "AnyWindowOpen is true with the Gallery open");
            bool first = Input.HandleEscape(), second = Input.HandleEscape();
            Check(first && !Input.AnyWindowOpen, "the first Escape hides the Gallery", "first=" + first);
            Check(second, "a second Escape in the same frame returns the cached true");
            Check(!Input.AnyWindowOpen, "AnyWindowOpen is false afterwards");
        }

        IEnumerator SectionOpen()
        {
            var sw = Stopwatch.StartNew();
            var w = Gallery.Open(); yield return null; yield return null;
            Check(w.Root.layout.width > 300f, "the Gallery window lays out", "w=" + w.Root.layout.width);
            Main.Log("[perf] gallery first open " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
