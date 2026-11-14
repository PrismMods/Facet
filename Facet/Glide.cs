using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Facet
{
    /* The wheel's glide. While Anim.SmoothScroll is on, a wheel step eases a ScrollView to where it would have
       jumped (OutCirc over Motion.Scale(200) ms, the curve of the menus that always glided), and a step that
       arrives mid-glide retargets it from where it is with a fresh duration. The distance per step is the
       step's own, so a settled glide has moved exactly what the instant step moves.

       One scheduled item per ScrollView drives it, made on its first glide and paused whenever no glide runs, so
       an idle view costs nothing per frame. Not Anim.Run: UI Toolkit's animation pools its ValueAnimation, which
       cannot be stopped safely once it has finished, and a glide is stopped and retargeted all the time.

       Who else moves the offset wins. Facet's own movers end the glide first (Nav's rebuild settles it, its
       search reveal stops it); anything else (a scrollbar drag, the keyboard, a host's own write) is seen as the
       offset no longer being what the glide read back after its last write. */
    internal static class Glide
    {
        private const int Ms = 200;

        internal sealed class State
        {
            public float From, To, Start, Written;
            public int Ms, Fed = -99, Polled = -99;   // frames the panel's own path and the polled path last fed it
            public bool Running;
            public IVisualElementScheduledItem Item;
        }

        private static bool _loggedNative, _loggedPolled;

        /// Whether a step glides now: the shared switch is on and motion is not off.
        public static bool On => Motion.Smooth && Motion.Scale(Ms) > 0;

        /// How far `sv` scrolls down. A virtualised ListView does not grow its content container: ask the list.
        public static float Max(ScrollView sv)
        {
            float view = sv.contentViewport.layout.height;
            float max = Mathf.Max(0f, sv.contentContainer.layout.height - view);
            var list = sv.GetFirstAncestorOfType<ListView>();
            if (list != null && list.itemsSource != null && list.fixedItemHeight > 0f)
                max = Mathf.Max(max, list.itemsSource.Count * list.fixedItemHeight - view);
            return max;
        }

        /* The panel's own WheelEvent, in the root's trickle-down handler. Unity's ScrollView would step the
           innermost scroller that can move and pass the wheel outward from one at its end; this takes the same
           pick and the same step (delta.y x mouseWheelScrollSize), but against the running glide's target, glides
           there and stops the event before the ScrollView's own handler adds an instant step. */
        public static void Native(WheelEvent e, Host host)
        {
            if (!On || e.delta.y == 0f || e.actionKey) return;
            var target = e.target as VisualElement;
            if (CanvasAll.OnStage(target)) return;   // a canvas pans or zooms on it; the page around it stays
            for (var sv = target as ScrollView ?? target?.GetFirstAncestorOfType<ScrollView>(); sv != null; sv = sv.GetFirstAncestorOfType<ScrollView>())
            {
                if (sv.mode == ScrollViewMode.Horizontal) continue;
                var g = Data.Find<State>(sv);
                if (g != null && g.Polled == Time.frameCount) { e.StopPropagation(); return; }   // the polled path already took this notch
                if (!Feed(sv, e.delta.y * sv.mouseWheelScrollSize, Max(sv)))
                {
                    // Its glide already heads to that end: the notch is spent there. Unity would step this view's live
                    // offset at once (it can still move, so Unity would not pass the wheel outward either).
                    if (g != null && g.Running) { e.StopPropagation(); return; }
                    continue;
                }
                Data.Find<State>(sv).Fed = Time.frameCount;
                e.StopPropagation();
                if (!_loggedNative) { _loggedNative = true; host.Log("Skin wheel: glided on the panel's own wheel event"); }
                return;
            }
        }

        /// The polled path's step (`dy` px down). A target the panel's own event fed this frame or the last has
        /// had this notch already.
        public static void Polled(ScrollView sv, float dy, float max, Host host)
        {
            var g = Data.Find<State>(sv);
            if (g != null && Time.frameCount - g.Fed <= 1) return;
            if (!Feed(sv, dy, max)) return;
            Data.Find<State>(sv).Polled = Time.frameCount;
            if (!_loggedPolled) { _loggedPolled = true; host.Log("Skin wheel: glided on the polled wheel"); }
        }

        // Retarget by dy from the running glide's target, else from the offset. False when that moves nothing.
        private static bool Feed(ScrollView sv, float dy, float max)
        {
            var g = Data.Get<State>(sv);
            float now = sv.scrollOffset.y;
            if (g.Running && Mathf.Abs(now - g.Written) > 0.5f) Stop(g);   // something else moved it since
            float from = Mathf.Clamp(g.Running ? g.To : now, 0f, Mathf.Max(0f, max));
            float to = Mathf.Clamp(from + dy, 0f, Mathf.Max(0f, max));
            if (to == from) return false;
            g.From = now; g.To = to; g.Written = now;
            // The first step runs later this same frame: started a frame back, it covers that frame. From now it would
            // move a sliver, and a delta every frame (a trackpad) would leave the glide trailing by many frames.
            g.Start = Time.realtimeSinceStartup - Time.unscaledDeltaTime; g.Ms = Motion.Scale(Ms);
            g.Running = true;
            if (g.Item != null) { g.Item.Resume(); return true; }
            g.Item = sv.schedule.Execute(() => Step(sv, g)).Every(0);
            // A detached view's item would restart on re-attach: end the glide with the view.
            Events.On<DetachFromPanelEvent>(sv, _ => Stop(g));
            return true;
        }

        private static void Step(ScrollView sv, State g)
        {
            if (!g.Running) { Stop(g); return; }
            if (Mathf.Abs(sv.scrollOffset.y - g.Written) > 0.5f) { Stop(g); return; }   // someone else moved it: theirs stays
            float max = Max(sv);
            if (g.To > max) g.To = max;   // the content shrank under the glide
            float t = g.Ms > 0 ? (Time.realtimeSinceStartup - g.Start) * 1000f / g.Ms : 1f;
            Write(sv, g, t >= 1f ? g.To : Mathf.LerpUnclamped(g.From, g.To, Easing.OutCirc(t)));
            if (t >= 1f) Stop(g);
        }

        // The setter clamps to the scroller's range, so keep what it read back, not what was asked.
        private static void Write(ScrollView sv, State g, float y)
        {
            var o = sv.scrollOffset;
            o.y = y;
            sv.scrollOffset = o;
            g.Written = sv.scrollOffset.y;
        }

        private static void Stop(State g)
        {
            g.Running = false;
            g.Item?.Pause();
        }

        /// End a glide where it is, for a mover about to write the offset itself.
        public static void Stop(ScrollView sv)
        {
            var g = Data.Find<State>(sv);
            if (g != null && g.Running) Stop(g);
        }

        /// End a glide at its target, so a mover that keeps the offset keeps the whole step.
        public static void Settle(ScrollView sv)
        {
            var g = Data.Find<State>(sv);
            if (g == null || !g.Running) return;
            if (Mathf.Abs(sv.scrollOffset.y - g.Written) <= 0.5f) Write(sv, g, Mathf.Min(g.To, Max(sv)));
            Stop(g);
        }
    }
}
