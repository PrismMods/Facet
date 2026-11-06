using System;

namespace Facet
{
    /* Where a control's value lives: a getter and a setter into the consumer's own settings.

       To reads live, so whatever the getter reaches is what the row shows at the next refresh — a
       settings object replaced wholesale (a profile switch) included, as long as the getter goes
       through the reference rather than capturing the old object. Value keeps the value itself, for
       code that has a snapshot and a change handler rather than a field to point at.

       Both run consumer code; Control calls them through Safe, so a throwing getter is logged and
       counted, never thrown at the panel. */
    public sealed class Bind<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        internal Bind(Func<T> get, Action<T> set) { _get = get; _set = set; }

        public T Get() { return _get(); }

        /// A bind with no setter (a read-only row) ignores writes.
        public void Set(T v) { if (_set != null) _set(v); }
    }

    public static class Bind
    {
        public static Bind<T> To<T>(Func<T> get, Action<T> set) { return new Bind<T>(get, set); }

        public static Bind<T> Value<T>(T initial, Action<T> onChange = null)
        {
            T v = initial;
            return new Bind<T>(() => v, x => { v = x; if (onChange != null) onChange(x); });
        }
    }
}
