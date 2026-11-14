using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* A row of tools over a canvas or a list: a search field, chips, icon, glyph and text buttons, toggles,
       labels and undo/redo, left to right with Sizes.Gap between them; Spacer pushes what follows to the right
       end. Its edges pad as a card pads its rows, in the same two steps (Sizes.Line on Root, then Sizes.Pad on
       an inner row that holds the items): layout rounds each step to whole pixels on its own, so one Line + Pad
       step can land a pixel off a card row's text at a fractional scale. A button's configure takes Id, Tooltip/TooltipText, EnabledWhen, VisibleWhen and
       ContextMenu(Items), as a button row does. */
    public sealed class Toolbar
    {
        public VisualElement Root { get; }

        // The items' row inside Root: Root pads a card's border, this a card row's padding.
        internal VisualElement Row { get; }

        private readonly float _gap;

        internal Toolbar(VisualElement parent)
        {
            Root = Ui.Row(parent);
            var sz = Theme.For(Root).Sizes;
            _gap = sz.Gap;
            Root.style.minHeight = sz.RowHeight;
            Root.style.paddingLeft = sz.Line;
            Root.style.paddingRight = sz.Line;
            Row = Ui.Row(Root);
            Row.style.flexGrow = 1f;
            Row.style.paddingLeft = sz.Pad;
            Row.style.paddingRight = sz.Pad;
        }

        /// A search field with a clear button; `placeholder` is an English key. `changed` gets every edit.
        public TextField Search(Action<string> changed, string placeholder = null)
        {
            VisualElement box; Button clear;
            var f = Controls.SearchBox(Row, out box, out clear);
            Place(box);
            Controls.WireSearch(f, clear, placeholder, changed, Host.For(Row), "Toolbar.Search");
            return f;
        }

        /// Controls.Chips in the toolbar.
        public Control<T> Chips<T>(Bind<T> bind, IList<T> values, Func<T, string> text)
        {
            var c = Controls.Chips(Row, bind, values, text);
            Place(c.Root);
            return c;
        }

        /// Ui.IconButton in the toolbar; `tooltip` is an English key.
        public Button Icon(Background icon, string tooltip, Action click, Action<ControlOptions> configure = null)
            => Hooked(Ui.IconButton(Row, icon, tooltip, click), tooltip, configure);

        /// A bare text glyph ("+", "−") drawn as the window header's ↶ ↷; `tooltip` is an English key.
        public Button Glyph(string glyph, string tooltip, Action click, Action<ControlOptions> configure = null)
        {
            var b = Controls.Glyph(Row, glyph, click);
            Words.Tip(b, tooltip);
            return Hooked(b, tooltip ?? glyph, configure);
        }

        /// A button in the theme's Looks.Button; `text` is an English key.
        public Button Button(string text, Action click, Action<ControlOptions> configure = null)
            => Hooked(Ui.Btn(Row, text, click), text, configure);

        /// A button that shows selected (Paint.Select) while `on` is true and flips it on a click. Re-read on
        /// every refresh. A view switch (snap, grid): it records nothing in History.
        public Button Toggle(string text, Bind<bool> on, string tooltip = null)
        {
            Control<bool> c = null;
            var b = Ui.Btn(Row, text, () => c.Set(!c.Value));
            Words.Tip(b, tooltip);
            c = new Control<bool>(b, null, text, on, v => Paint.Select(b, v), new ControlOptions());
            return Place(b);
        }

        /// Text re-read on every refresh and shown as returned (a zoom readout, a count).
        public Label Label(Func<string> text)
        {
            var l = Ui.Text(Row, "", Theme.For(Row).Sizes.Small);
            new Control<string>(l, null, "Toolbar label", Bind.To(text, null), s => l.text = s ?? "", new ControlOptions());
            return Place(l);
        }

        /// The window header's ↶ ↷: the same look, tooltips (naming the next step) and enablement.
        public void UndoRedo()
        {
            Button undo, redo;
            Controls.UndoRedo(Row, Host.For(Row), out undo, out redo);
            Place(undo);
            Place(redo);
        }

        /// Takes the free width, so what follows sits at the right end.
        public void Spacer() { Ui.Box(Row).style.flexGrow = 1f; }

        private Button Hooked(Button b, string name, Action<ControlOptions> configure)
        {
            if (configure != null) Controls.Hook(b, null, name, ControlOptions.Make(Row, name, configure));
            return Place(b);
        }

        // The gap goes before an item, not after: the last ends on the toolbar's edge.
        private T Place<T>(T e) where T : VisualElement
        {
            e.style.marginRight = 0f;
            e.style.marginLeft = Row.IndexOf(e) > 0 ? _gap : 0f;
            return e;
        }
    }

    public static partial class Controls
    {
        public static Toolbar Toolbar(VisualElement parent) => new Toolbar(parent);

        /* One segment per value, the bound one selected; a click selects its value. `text` names a value (an
           English key, translated like a label; null: ToString). A view switch with no label to name a step, so it
           records nothing in History: an edit to the document is a labelled row. Root is the bar. */
        public static Control<T> Chips<T>(VisualElement parent, Bind<T> bind, IList<T> values, Func<T, string> text, Action<Options<T>> configure = null)
        {
            var o = ControlOptions.Make(parent, "Chips", configure);
            var bar = Ui.Row(parent);
            var vals = values ?? new T[0];
            var names = Names(parent, vals, text, "Chips text");
            Control<T> c = null;
            var select = Segments(bar, names, i => { if (!EqualityComparer<T>.Default.Equals(vals[i], c.Value)) c.Set(vals[i]); });
            c = new Control<T>(bar, null, o.Id ?? "Chips", bind, v => select(vals.IndexOf(v)), o);
            return c;
        }

        /* Segmented buttons, the gap before each but the first, so the last ends on its row's edge; `pick` gets a
           click's index. Returns the show: it selects segment i (none when i is out of range). */
        internal static Action<int> Segments(VisualElement row, IList<string> names, Action<int> pick)
        {
            var buttons = new Button[names.Count];
            float gap = Theme.For(row).Sizes.Gap;
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = buttons[i] = Ui.Btn(row, names[i], () => pick(idx));
                b.style.marginRight = 0f;
                if (i > 0) b.style.marginLeft = gap;
            }
            return v => { for (int j = 0; j < buttons.Length; j++) Paint.Select(buttons[j], j == v); };
        }

        /* A search field with a ✕ that clears it, shown once there is something to clear: a ✕ sitting there
           permanently is one more thing to read. The window header's search and a toolbar's. */
        internal static TextField SearchBox(VisualElement parent, out VisualElement box, out Button clear)
        {
            var looks = Theme.For(parent).Looks;
            box = Ui.Row(parent);
            box.style.flexGrow = 1f;
            box.style.maxWidth = 420f;
            var f = new TextField { value = "" };
            f.style.flexGrow = 1f;
            Skin.When<TextField>(f, Skin.Field);
            Cursors.Set(f, Cursors.Kind.Text);
            box.Add(f);
            clear = Ui.Btn(box, "✕", () => f.value = "");
            clear.style.width = 24f;
            clear.style.marginLeft = -28f;   // sits inside the field's right edge
            clear.style.marginRight = 4f;
            Ui.SetPadding(clear, 0f);
            clear.style.unityTextAlign = TextAnchor.MiddleCenter;
            Paint.Look(clear, looks.FlatButton);
            clear.style.display = DisplayStyle.None;
            return f;
        }

        /// The placeholder (an English key, translated by `host`) and the change handler, run under Safe at `site`.
        internal static void WireSearch(TextField f, Button clear, string placeholder, Action<string> changed, Host host, string site, bool own = false)
        {
            Placeholder(f, placeholder, host, own);
            Events.On<ChangeEvent<string>>(f, e =>
            {
                clear.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.None : DisplayStyle.Flex;
                if (changed != null) Safe.Run(host, site, () => changed(e.newValue));
            });
        }

        /// Grey text shown while f is empty: `key` is an English key translated by `host`, again on a relabel.
        /// own: Facet picked the key, so Facet's own word stands in when the host leaves it untranslated.
        internal static void Placeholder(TextField f, string key, Host host, bool own = false)
        {
            // Unity 6 only; Unity 2022 has no placeholder, so it is set by name.
            var ph = typeof(ITextEdition).GetProperty("placeholder");
            if (ph == null || key == null) return;
            Action set = () => ph.SetValue(f.textEdition, own ? Words.TOrOwn(key, host) : Words.T(key, host), null);
            set();
            Words.OnRelabel(f, set);
        }

        /* A bare glyph button at title size, lit on hover: the window header's ↶ ↷ and a toolbar's Glyph. These
           live in Arrows, which every OS UI font carries — unlike the pictographic blocks, where a miss is a blank
           button rather than a wrong-looking one. */
        internal static Button Glyph(VisualElement parent, string glyph, Action onClick)
        {
            var b = Ui.Btn(parent, glyph, onClick);
            Paint.Look(b, Ui.Bare);
            b.style.width = 30f;
            b.style.height = 26f;
            b.style.fontSize = Theme.For(b).Sizes.Title + 3f;
            Ui.SetPadding(b, 0f);
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            return b;
        }

        /* ↶ and ↷, where they apply to whatever the window is showing: the window header's and Toolbar.UndoRedo.
           The tooltips name the next step in Facet's own words. History.Changed is subscribed only while the
           buttons are on a panel: History is static, so a disposed window left subscribed stays reachable forever
           and keeps redrawing its detached buttons. */
        internal static void UndoRedo(VisualElement parent, Host host, out Button undo, out Button redo)
        {
            var u = undo = Glyph(parent, "↶", () => History.Undo());
            var r = redo = Glyph(parent, "↷", () => History.Redo());
            Action refresh = () =>
            {
                Enable(u, History.CanUndo, History.NextUndo == null ? Words.Own("Undo", host) : Words.OwnF("Undo {0}", host, History.NextUndoText));
                Enable(r, History.CanRedo, History.NextRedo == null ? Words.Own("Redo", host) : Words.OwnF("Redo {0}", host, History.NextRedoText));
            };
            Events.On<AttachToPanelEvent>(u, _ => { History.Changed -= refresh; History.Changed += refresh; refresh(); });
            Events.On<DetachFromPanelEvent>(u, _ => History.Changed -= refresh);
            if (u.panel != null) History.Changed += refresh;   // already attached: no event comes
            refresh();
            Words.OnRelabel(u, refresh);   // the tooltips are built from parts, so a language switch redoes them
        }

        // Fades rather than vanishes (Paint.Enable), so the row does not reflow on the first change.
        private static void Enable(Button b, bool on, string tip)
        {
            Paint.Enable(b, on);
            b.tooltip = on ? tip : null;
        }
    }
}
