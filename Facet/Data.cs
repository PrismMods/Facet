using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Facet {
    // Per-element engine data. IL2CPP seam: Facet cannot subclass VisualElement to carry fields,
    // so state hangs off the element here, one object per type.
    public static class Data {
        static readonly ConditionalWeakTable<VisualElement, Dictionary<Type, object>> _t =
            new ConditionalWeakTable<VisualElement, Dictionary<Type, object>>();

        public static T Get<T>(VisualElement e) where T : class, new() {
            var d = _t.GetOrCreateValue(e);
            object o;
            if (!d.TryGetValue(typeof(T), out o)) d[typeof(T)] = o = new T();
            return (T)o;
        }

        public static T Find<T>(VisualElement e) where T : class, new() {
            Dictionary<Type, object> d; object o;
            return e != null && _t.TryGetValue(e, out d) && d.TryGetValue(typeof(T), out o) ? (T)o : null;
        }

        public static void Clear<T>(VisualElement e) where T : class, new() {
            Dictionary<Type, object> d;
            if (e != null && _t.TryGetValue(e, out d)) d.Remove(typeof(T));
        }
    }
}
