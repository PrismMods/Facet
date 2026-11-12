using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using EventType = UnityEngine.EventType;

namespace Facet.Mod
{
    sealed partial class AutoTest
    {
        /* A host's WheelSpeed scrolls linearly on both wheel paths: the polled one, where a notch and a
           trackpad's fraction move in proportion, and the panel's own WheelEvent, which Unity 6 sends at
           3 per wheel unit. A change to the speed shows on the next scroll. */
        IEnumerator SectionWheelSpeed()
        {
            float speed = 80f;
            var s = new Surface(Host.Create("WheelSpeed", o => { o.Log = m => Main.Log("[wheelspeed] " + m); o.WheelSpeed = () => speed; }), "FacetWheelSpeed", Layer.Window);
            try
            {
                var w = new Window(s.Root, "wheelspeed", "Wheel speed", new Rect(60, 60, 480, 360));
                var sv = new ScrollView(ScrollViewMode.Vertical);
                sv.style.height = 200f; sv.style.flexGrow = 0f; sv.style.flexShrink = 0f;
                sv.contentContainer.style.flexShrink = 0f;   // not a Box: left at 1 it squashes to the view and nothing scrolls
                w.Body.Add(sv);
                Skin.Scroll(sv);
                Ui.Box(sv).style.height = 3000f;
                yield return null; yield return null;
                var c = Ctr(sv);
                float y0 = sv.scrollOffset.y;
                Facet.Input.TickWheel(c, -1f);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 80f) <= 1f, "a host's WheelSpeed of 80 scrolls a notch 80 px", (sv.scrollOffset.y - y0).ToString());
                yield return null;
                y0 = sv.scrollOffset.y;
                Facet.Input.TickWheel(c, -0.1f);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 8f) <= 1f, "...and a trackpad's 0.1 a tenth of that, with no boost", (sv.scrollOffset.y - y0).ToString());
                yield return null;
                speed = 40f;
                y0 = sv.scrollOffset.y;
                Facet.Input.TickWheel(c, -1f);
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 40f) <= 1f, "changing the host's speed changes the next step", (sv.scrollOffset.y - y0).ToString());
                yield return null;
                // The panel's own path: a WheelEvent as Unity 6's input layer sends one notch down (delta.y 3).
                y0 = sv.scrollOffset.y;
                var ev = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, 3f), mousePosition = sv.worldBound.center };
                using (var e = WheelEvent.GetPooled(ev)) s.Root.panel.visualTree.SendEvent(e);
                yield return null;
                Check(Mathf.Abs(sv.scrollOffset.y - y0 - 40f) <= 1f, "the panel's own wheel event scrolls the same 40 px a notch", (sv.scrollOffset.y - y0).ToString());
            }
            finally { s.Dispose(); }
        }
    }
}
