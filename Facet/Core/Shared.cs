using System;
using System.Collections.Generic;

namespace Facet
{
    /* State every loaded Facet version agrees on. One Dictionary in AppDomain data: the AppDomain is
       the only thing two Facet.<major>.<minor> assemblies share. Values must be BCL types or
       delegates -- a Facet type from another version cannot be cast. */
    internal static class Shared
    {
        private const string Slot = "Facet.Shared";

        public static T Get<T>(string key, Func<T> create) where T : class
        {
            lock (typeof(object))   // one lock object every version sees (the BCL type), not a per-assembly one
            {
                var d = AppDomain.CurrentDomain.GetData(Slot) as Dictionary<string, object>;
                if (d == null) AppDomain.CurrentDomain.SetData(Slot, d = new Dictionary<string, object>());
                object v;
                if (!d.TryGetValue(key, out v)) d[key] = v = create();
                return (T)v;
            }
        }
    }
}
