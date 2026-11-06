using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Facet
{
    /* What every control can be told, set through a configure lambda:
       Controls.X(parent, "Label", bind, o => { o.Id = "x"; o.Default = 1; }).
       New options are new settable properties, never new parameters: binary-safe within a minor. */
    public class ControlOptions
    {
        /// Names the row Id and its input Id + "-input", for tests and for finding a row again.
        public string Id { get; set; }

        /// An English key, translated by the row's host like its label.
        public string Tooltip { get; set; }

        /// Asked each time the pointer enters the row; shown as it comes back, untranslated.
        public Func<string> TooltipText { get; set; }

        /// Asked on every refresh tick: false dims the row (EnabledWhen) or hides it (VisibleWhen).
        public Func<bool> EnabledWhen { get; set; }
        public Func<bool> VisibleWhen { get; set; }

        /// A right-click on the row lists these names; picking one runs its action.
        public List<KeyValuePair<string, Action>> ContextMenu { get; set; }

        /// An English key: a muted line under the row's label, translated like it. Search finds the
        /// row by it, as by Tooltip.
        public string Help { get; set; }

        /// A button row: nothing to read, so the refresher polls it only for EnabledWhen/VisibleWhen.
        internal bool NoRead;

        /// Runs `configure` on a fresh options object, through Safe: it is the consumer's code.
        internal static O Make<O>(VisualElement at, string label, Action<O> configure) where O : ControlOptions, new()
        {
            var o = new O();
            if (configure != null) Safe.Run(Host.For(at), "configure '" + label + "'", () => configure(o));
            return o;
        }
    }

    /// Options for a control with a value: a Default shows the changed dot and a middle-click resets to it.
    public class Options<T> : ControlOptions
    {
        private T _default;

        public bool HasDefault { get; private set; }

        public T Default
        {
            get { return _default; }
            set { _default = value; HasDefault = true; }
        }
    }
}
