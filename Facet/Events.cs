using UnityEngine.UIElements;

namespace Facet {
    // Every event registration goes through here. IL2CPP seam: one place to change if a callback
    // type ever needs wrapping.
    public static class Events {
        public static void On<T>(VisualElement e, EventCallback<T> cb, TrickleDown td = TrickleDown.NoTrickleDown)
            where T : EventBase<T>, new() {
            if (e != null && cb != null) e.RegisterCallback(cb, td);
        }

        public static void Off<T>(VisualElement e, EventCallback<T> cb, TrickleDown td = TrickleDown.NoTrickleDown)
            where T : EventBase<T>, new() {
            if (e != null && cb != null) e.UnregisterCallback(cb, td);
        }
    }
}
