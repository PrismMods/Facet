using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Facet
{
    public class SliderOptions : Options<float>
    {
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        /// 0 means no snapping; otherwise values are multiples of Step counted from Min.
        public float Step { get; set; } = 0f;
        public IValueScale Scale { get; set; } = ValueScale.Linear;
        public string Format { get; set; } = "0.##";
        /// Number only: dragging the row's label changes the value by (pixels / 4) x unit, unit being Step, else
        /// (Max - Min) / 200, else 1; Shift x10, Alt x0.1; clamped and snapped as typed. Each drag is one undo step.
        public bool Scrub { get; set; }
    }

    public class IntSliderOptions : Options<int>
    {
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 10;
    }

    public class ChoiceOptions : Options<int>
    {
        public IList<string> Items { get; set; }
    }

    public class DropdownOptions : Options<int>
    {
        public IList<string> Items { get; set; }
        /// A face for each item in the open menu (a font list shows every name in its own font).
        public Func<int, Font> ItemFont { get; set; }
    }

    /// A typed dropdown's options: the list and the item text are Controls.Dropdown<T>'s arguments.
    public sealed class DropdownOptions<T> : Options<T>
    {
        /// A face for each item in the open menu (a font list shows every name in its own face); null, or a throw,
        /// draws that item in the menu's face. A Font converts through FontAsset.CreateFontAsset: this runs for every item
        /// each time the menu opens, so create each face once and keep it (as Fonts.Mono does), never in here.
        public Func<T, FontAsset> ItemFace { get; set; }
    }

    /// A typed segment row's options: the list and the item text are Controls.Choice<T>'s arguments.
    public sealed class ChoiceOptions<T> : Options<T> { }

    public class TextOptions : Options<string>
    {
        public float Width { get; set; } = 200f;
        /// Called with the text on every keystroke, before any commit (a filter as you type). The bind is still
        /// written only on Enter or when the field loses focus.
        public Action<string> Live { get; set; }
        /// An English key shown greyed while the field is empty.
        public string Placeholder { get; set; }
        /// The most characters the field takes (0: no limit). Longer text is cut before Live or the bind sees it.
        public int MaxLength { get; set; }
    }

    public class ColourOptions : Options<Color>
    {
        /// Offer an alpha channel.
        public bool Alpha { get; set; }
    }

    public class KeyOptions : Options<string>
    {
        /// Starts a capture: the host calls back with the next key's name (null or empty for none).
        public Action<Action<string>> Capture { get; set; }
        /// Stops the host's key poll. Called (guarded) when this capture is cancelled: another control
        /// claimed the capture, the control left its panel, its surface hid, the prompt was cancelled, or the host
        /// handed back Input.ChordCancel. Never called when a key or mouse button was bound.
        public Action CancelCapture { get; set; }
        /// The value is a chord ("Ctrl+Shift+K", as Input.ReadChord gives it), shown the way a menu shows a shortcut.
        public bool Chord { get; set; }
        /// While the capture runs, a popup under the row asks for a key ("Press a key", or "Press a key or chord"
        /// with Chord) and offers Cancel. The row is scrolled into view first.
        public bool Prompt { get; set; }
        /// With Prompt: mouse buttons bind too. The prompt's catcher covers every window, so a press anywhere off
        /// the prompt binds that button ("Mouse0" for the left one) and nothing beneath sees it; the host's next
        /// ReadChord(…, mouse: true) then returns that button once, so its poll ends as on a read of its own.
        /// Needs Prompt: without it Mouse does nothing (logged once) and a click also reaches what lies beneath.
        public bool Mouse { get; set; }
    }

    /* The value rows. Each is a labelled row bound to a Bind<T> and handed back as a Control<T>;
       every change goes through History under "host/page/label#id", so Ctrl+Z undoes it and a
       drag is one step. */
    public static partial class Controls
    {
        /// Counter for controls built without an Id; Nav resets it before every page build.
        internal static int Seq;

        /// Under a host with LabelIds, how many rows took each label as their Id on the page being built: scoped
        /// like Seq (a page, each tab, a Live rebuild replays its first build's), null until the first.
        internal static System.Collections.Generic.Dictionary<string, int> Labels;

        /* A row's options; with the host's LabelIds an unnamed row takes its label as Id ("label#2" for the second),
           skipping any Id a row already took on the page. Taken Ids are kept in Labels under a "\u0001" prefix, so
           they are scoped and replayed with the counts. */
        private static O Opts<O>(VisualElement at, string label, Action<O> configure) where O : ControlOptions, new()
        {
            var o = ControlOptions.Make(at, label, configure);
            if (!Host.For(at).LabelIds) return o;
            if (Labels == null) Labels = new System.Collections.Generic.Dictionary<string, int>();
            if (o.Id == null && !string.IsNullOrEmpty(label))
            {
                int n;
                Labels.TryGetValue(label, out n);
                do { n++; o.Id = n == 1 ? label : label + "#" + n; } while (Labels.ContainsKey("\u0001" + o.Id));
                Labels[label] = n;
            }
            if (o.Id != null) Labels["\u0001" + o.Id] = 1;
            return o;
        }
        private static int KeySeq;
        // A Key row with Mouse but no Prompt has been logged (once per Facet copy).
        internal static bool MouseWarned;

        /// Reachable by Tab / NavigationMove, and draws Role.Focus around itself while focused.
        internal static void Ring(VisualElement e)
        {
            var t = Theme.For(e);
            e.focusable = true;
            Ui.SetBorderWidth(e, t.Sizes.Line);
            Paint.Look(e, t.Looks.Ring);
        }

        public static Control<bool> Toggle(VisualElement parent, string label, Bind<bool> bind, Action<Options<bool>> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            Action<bool, bool> show;
            Control<bool> c = null;
            var track = Switch(right, out show, () => c.Set(c.Mixed || !c.Value));   // from mixed, a click sets them all on
            c = TrackedRow(right.parent, track, parent, label, o, bind, null, show);
            return c;
        }

        public static Control<float> Slider(VisualElement parent, string label, Bind<float> bind, Action<SliderOptions> configure = null)
        {
            return FloatRow(parent, label, bind, Opts(parent, label, configure), true);
        }

        /// Just the typed number, no track. Unbounded unless you set a range (Min < Max), then clamped to it.
        public static Control<float> Number(VisualElement parent, string label, Bind<float> bind, Action<SliderOptions> configure = null)
        {
            // No range by default: a slider's 0..1 would clamp every typed number.
            return FloatRow(parent, label, bind, Opts<SliderOptions>(parent, label, o =>
            {
                o.Min = o.Max = 0f;
                if (configure != null) configure(o);
            }), false);
        }

        public static Control<int> IntSlider(VisualElement parent, string label, Bind<int> bind, Action<IntSliderOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            var s = new SliderInt(o.Min, o.Max);
            s.style.width = Theme.For(right).Sizes.SliderWidth;
            VisualElement fill = null;
            Skin.When<SliderInt>(s, x => { Skin.Slider(x); fill = Skin.AddFill(x, o.Id); });
            var field = NumberField(right, o.Id);
            right.Insert(0, s);
            Ring(s);   // once in the row, so it finds the row's theme

            Control<int> c = null;
            Action<int, bool> show = (v, mixed) =>
            {
                s.SetValueWithoutNotify(v);
                Skin.SetFill(fill, s);
                field.SetValueWithoutNotify(mixed ? MixedText(field) : v.ToString(CultureInfo.CurrentCulture));
            };
            c = TrackedRow(right.parent, s, parent, label, o, bind, null, show);

            var drag = Watch(s, c);
            Events.On<ChangeEvent<int>>(s, e =>
            {
                if (e.newValue == c.Value && !c.Mixed) return;
                if (drag.On) c.Edit(e.newValue); else c.Set(e.newValue);
            });
            OnTyped(field, c, text =>
            {
                int v;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return false;
                v = Math.Max(o.Min, Math.Min(o.Max, v));
                if (v != c.Value || c.Mixed) c.Set(v);
                return true;
            }, () => show(c.Value, c.Mixed));
            return c;
        }

        /* Segmented buttons rather than a popup list. Same reason as the toggle — DropdownField's
           menu is themed by the style sheet we do not have — and for the handful of options a
           setting usually has, seeing them all beats opening a list to find out. */
        public static Control<int> Choice(VisualElement parent, string label, Bind<int> bind, Action<ChoiceOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            Control<int> c = null;
            var select = Segments(right, o.Items ?? new string[0], i => { if (i != c.Value || c.Mixed) c.Set(i); });
            c = TrackedRow(right.parent, right, parent, label, o, bind, null, (v, mixed) => select(mixed ? -1 : v));   // mixed: none selected
            return c;
        }

        /// A button showing the current item that opens a Popup.Menu of the rest.
        public static Control<int> Dropdown(VisualElement parent, string label, Bind<int> bind, Action<DropdownOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var items = o.Items ?? new string[0];
            return DropdownRow(parent, label, bind, o, items, v => v >= 0 && v < items.Count ? v : -1, i => i, o.ItemFont, null);
        }

        /* The same over typed values: text names each value (an English key, translated like a label; null: ToString),
           and values map to items by EqualityComparer<T>.Default, so a value not in the list shows blank and selects
           nothing. Mixed reads "Mixed" with nothing marked. The values are copied at build: a changed list needs a
           rebuild. */
        public static Control<T> Dropdown<T>(VisualElement parent, string label, Bind<T> bind, IList<T> values, Func<T, string> text,
                                             Action<DropdownOptions<T>> configure = null)
        {
            var o = Opts(parent, label, configure);
            var vals = Snapshot(values);
            var face = o.ItemFace;
            return DropdownRow(parent, label, bind, o, Names(parent, vals, text, "Dropdown text"), v => IndexOf(vals, v), i => vals[i], null,
                               face == null ? null : (Func<int, FontAsset>)(i => face(vals[i])));
        }

        /// A typed dropdown of every value of TEnum, named by ToString (English keys).
        public static Control<TEnum> Enum<TEnum>(VisualElement parent, string label, Bind<TEnum> bind, Action<DropdownOptions<TEnum>> configure = null)
            where TEnum : struct, System.Enum
        {
            return Dropdown(parent, label, bind, (TEnum[])System.Enum.GetValues(typeof(TEnum)), null, configure);
        }

        /// Segmented buttons over typed values, mapped as Dropdown<T>'s; a value not in the list selects none.
        public static Control<T> Choice<T>(VisualElement parent, string label, Bind<T> bind, IList<T> values, Func<T, string> text,
                                           Action<ChoiceOptions<T>> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            var vals = Snapshot(values);
            Control<T> c = null;
            var select = Segments(right, Names(parent, vals, text, "Choice text"), i =>
            {
                if (c.Mixed || !EqualityComparer<T>.Default.Equals(vals[i], c.Value)) c.Set(vals[i]);
            });
            c = TrackedRow(right.parent, right, parent, label, o, bind, null, (v, mixed) => select(mixed ? -1 : IndexOf(vals, v)));
            return c;
        }

        /* The values as built: names are read once, so the values they pair with are copied too, and a caller's list
           changed later cannot misname a value or index past its end. A changed list needs a rebuild (a Live region). */
        private static T[] Snapshot<T>(IList<T> values)
        {
            var a = new T[values == null ? 0 : values.Count];
            if (values != null) values.CopyTo(a, 0);
            return a;
        }

        // Each value's name, read once at build under Safe (null text: ToString).
        private static string[] Names<T>(VisualElement parent, IList<T> vals, Func<T, string> text, string site)
        {
            var host = Host.For(parent);
            var names = new string[vals.Count];
            for (int i = 0; i < names.Length; i++)
            {
                var v = vals[i];
                names[i] = text == null ? (v == null ? "" : v.ToString()) : Safe.Get(host, site, () => text(v), "");
            }
            return names;
        }

        private static int IndexOf<T>(IList<T> vals, T v)
        {
            var eq = EqualityComparer<T>.Default;
            for (int i = 0; i < vals.Count; i++) if (eq.Equals(vals[i], v)) return i;
            return -1;
        }

        /* The dropdown both kinds build: index maps a value to its item (-1: none), value an item back. */
        private static Control<T> DropdownRow<T>(VisualElement parent, string label, Bind<T> bind, ControlOptions o, IList<string> items,
                                                 Func<T, int> index, Func<int, T> value, Func<int, Font> itemFont, Func<int, FontAsset> itemFace)
        {
            var right = Controls.Row(parent, label);
            Control<T> c = null;
            Button b = null;
            /* The arrow goes in a cell around the button, not in the button: a Button that gains a child
               loses its text measure (UI Toolkit drops it on Insert), so it shrank to its padding and a
               long item overflowed instead of widening it. */
            var cell = Ui.Box(right);
            b = Ui.Btn(cell, "", () => Popup.Menu(b, items, c.Mixed ? -1 : index(c.Value), i => c.Set(value(i)), itemFont, itemFace));
            var t = Theme.For(b); var sz = t.Sizes;
            Paint.Look(b, t.Looks.Field);
            b.style.marginRight = 0f;   // the box fills its cell, so the arrow is pinned inside the box's border
            b.style.minWidth = sz.DropdownWidth;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingRight = sz.ControlPad + sz.Small + sz.Gap;   // room for the arrow
            var arrow = Ui.Text(cell, "\u25be", sz.Small, Role.TextMuted, true);
            arrow.name = o.Id != null ? o.Id + "-arrow" : "arrow";
            arrow.pickingMode = PickingMode.Ignore;
            arrow.style.position = Position.Absolute;
            arrow.style.right = sz.ControlPad; arrow.style.top = 0f; arrow.style.bottom = 0f;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            // An item's English key, translated like any label; out of range shows blank.
            Action<T, bool> show = (v, mixed) =>
            {
                if (mixed) { Words.Label(b, "Mixed", true); return; }
                int i = index(v);
                Words.Label(b, i >= 0 && i < items.Count ? items[i] : "");
            };
            c = TrackedRow(right.parent, b, parent, label, o, bind, null, show);
            return c;
        }

        public static Control<string> Text(VisualElement parent, string label, Bind<string> bind, Action<TextOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            var f = new TextField();
            f.style.width = o.Width;
            Skin.When<TextField>(f, Skin.Field);
            f.style.fontSize = Theme.For(right).Sizes.Small;
            Cursors.Set(f, Cursors.Kind.Text);
            right.Add(f);
            int max = o.MaxLength;
            if (max > 0) f.maxLength = max;
            // What the row last showed, read back after the field's own maxLength had its say: text equal to it is no edit.
            string shown = null;
            var c = TrackedRow(right.parent, f, parent, label, o, bind, null, (v, mixed) => { f.SetValueWithoutNotify(mixed ? "" : v ?? ""); shown = f.value; });
            Placeholder(f, o.Placeholder, Host.For(right));
            var live = o.Live;
            if (live != null)
            {
                // The site and the call made once, the text passed through a local: a keystroke allocates nothing here.
                var host = Host.For(right);
                var site = "TextOptions.Live '" + label + "'";
                string typed = null;
                Action run = () => live(typed);
                Events.On<ChangeEvent<string>>(f, e => { typed = Cut(e.newValue, max); Safe.Run(host, site, run); });
            }
            /* On commit, not per keystroke: a setter that persists to disk should not run once per
               character typed. Mixed shows empty, and leaving it empty writes nothing: only typing does.
               Untouched text writes nothing either, so a focus and blur never cuts a longer stored value. */
            OnTyped(f, c, text =>
            {
                text = text ?? "";
                if (text == shown) return true;
                if (max > 0 && text.Length > max) { text = text.Substring(0, max); f.SetValueWithoutNotify(text); }
                if (c.Mixed ? text.Length > 0 : text != (c.Value ?? "")) c.Set(text);
                shown = text;
                return true;
            }, null);
            return c;
        }

        /* Colour. The picker itself is ColourPicker — Topaz's model, the best of the three the
           mods had — and this is the row that hosts it. The picker keeps its own name (the tests
           look for it), so Root and Input are two wrappers around it. */
        public static Control<Color> Colour(VisualElement parent, string label, Bind<Color> bind, Action<ColourOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var root = Ui.Row(parent);
            Controls.Noted(root, label);
            var sz = Theme.For(root).Sizes;
            // The swatch ends where the other rows' controls do: inside the row's padding and, beside a row that opens a page, its arrow slot.
            root.style.paddingRight = Carded(root, sz) ? sz.Pad : 4f;
            root.style.alignItems = Align.FlexStart;
            var inner = Ui.Box(root);
            inner.style.flexGrow = 1f;
            inner.style.minWidth = 0f;
            Control<Color> c = null;
            var picker = new ColourPicker(inner, label, Color.white, o.Alpha, col => { if (c != null) c.Set(col); }, col => { if (c != null) c.Edit(col); }, () => { if (c != null) c.Commit(); });
            Data.Get<Noting>(root).Label = picker.Title;   // a help line goes under the picker's name
            if (o.Id != null) picker.Swatch.name = o.Id + "-swatch";
            AddArrowSlot(root);
            c = TrackedRow(root, inner, parent, label, o, bind, null, (v, mixed) => { picker.SetColour(v); picker.SetMixed(mixed); });
            // The dot sits in the flow before the picker: level it with the header, not the whole picker.
            var dot = root.Q("changed");
            if (dot != null) dot.style.marginTop = (sz.RowHeight - 6f) / 2f;
            return c;
        }

        /* Keybind capture. Click, then press — the next key wins, Escape cancels, and the row says
           which it is waiting for. The capture has to be polled by the host rather than read from a
           UI Toolkit event, because the keys worth binding include ones the panel would otherwise
           treat as navigation (Tab, arrows, Escape). */
        public static Control<string> Key(VisualElement parent, string label, Bind<string> bind, Action<KeyOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            if (o.Mouse && !o.Prompt && !MouseWarned)
            {
                MouseWarned = true;
                Host.For(parent).Log("Key '" + label + "': Mouse needs Prompt; without the prompt's catcher a click is not bound and reaches what lies beneath");
            }
            var right = Controls.Row(parent, label);
            Control<string> c = null;
            Button b = null;
            // One owner name per control; a claim by another control (any mod, any Facet version) cancels this one.
            string me = "key#" + (++KeySeq) + "@" + Host.For(parent).Name + "/" + typeof(Controls).Assembly.GetName().Name;
            bool active = false, chord = o.Chord;
            int gen = 0;   // a callback from a cancelled capture arrives late and is ignored
            Surface owner = null;
            Popup prompt = null;
            Action cancel = null;
            // Ends the capture on the control's side; the host hears about it only when cancelled.
            Action<bool> end = tellHost =>
            {
                if (!active) return;
                active = false; gen++;
                KeyCapture.Release(me);
                if (owner != null) { owner.Hidden -= cancel; owner = null; }
                var p = prompt; prompt = null;
                if (p != null) p.Close();
                Paint.Select(b, false);
                Draw(b, c.Value, c.Mixed, chord);
                if (tellHost && o.CancelCapture != null) Safe.Run(Host.For(b), "Key CancelCapture", o.CancelCapture);
            };
            cancel = () => end(true);
            /* A captured name: ChordCancel is a cancel; anything else is written (empty is unbound). The cancel spends
               the frame's Escape, so a host whose HandleEscape runs after this poll closes no window with it. */
            Action<string> got = name =>
            {
                if (name == Input.ChordCancel) { Input.ConsumeEscape(); end(true); return; }
                end(false);
                name = name ?? "";
                if (name != (c.Value ?? "") || c.Mixed) c.Set(name); else Draw(b, name, false, chord);
            };
            Func<bool> start = () =>
            {
                if (o.Capture == null || b.panel == null) return false;
                var host = Host.For(b);
                end(true);   // a second click while waiting restarts cleanly
                KeyCapture.Claim(me, cancel, host.LogFn);
                Input.PromptBound(null);   // a button an earlier prompt bound is not this capture's
                active = true;
                int mine = ++gen;
                foreach (var st in Ui.Stack) if (st.Root != null && st.Root.Contains(b)) { owner = st; break; }
                if (owner != null) owner.Hidden += cancel;
                Words.Label(b, "press a key…", true);
                Paint.Select(b, true);
                if (o.Prompt) Ask(b, chord, o.Mouse, mine, () => gen, p => prompt = p, cancel, got);
                bool ok = Safe.Run(host, "Key capture", () => o.Capture(name => { if (mine == gen) got(name); }));
                if (!ok) end(false);
                return active || ok;
            };
            b = Ui.Btn(right, "", () => start());
            Data.Get<KeyStart>(b).Begin = start;
            Events.On<DetachFromPanelEvent>(b, _ => cancel());
            b.style.marginRight = 0f;
            b.style.minWidth = 110f;
            c = TrackedRow(right.parent, b, parent, label, o, bind, null, (v, mixed) => Draw(b, v, mixed, chord));
            return c;
        }

        internal sealed class KeyStart { public Func<bool> Begin; }

        /// Starts a Key row's capture from code, as a click on it does (a "Rebind key" menu item): false when the row
        /// is not a Key row, has no Capture, or is not on a panel.
        public static bool BeginCapture(this Control<string> keyRow)
        {
            var k = keyRow == null ? null : Data.Find<KeyStart>(keyRow.Input);
            return k != null && k.Begin != null && k.Begin();
        }

        /* The prompt under a capturing Key row: its row scrolled into view first (a capture begun from code may sit
           off screen), then "Press a key" and Cancel. Closing it any other way than through the row (Cancel, an
           outside click, Escape) cancels the capture. With mouse, a press on its catcher binds that button, and the
           host's poll leaves mouse buttons alone only while that catcher is up.
           A row a Live rebuilt this frame has no layout yet: the scroll and the prompt wait for its first one, or the
           scroll works from NaN and the card is placed (and scaled) from where the row is not. A row that gets none
           within a few frames (not displayed: a folded section, another tab) has its prompt opened over its window. */
        private static void Ask(Button b, bool chord, bool mouse, int mine, Func<int> gen, Action<Popup> opened, Action cancel, Action<string> got)
        {
            var row = b.parent != null && b.parent.parent != null ? b.parent.parent : b;
            bool shown = false;
            Action<bool> show = over =>
            {
                if (shown || mine != gen()) return;
                shown = true;
                Func<bool> mp = () => mine == gen();
                Action<VisualElement> build = box =>
                {
                    var sz = Theme.For(box).Sizes;
                    var l = Ui.Text(box, "", sz.Body);
                    Words.Label(l, chord ? "Press a key or chord" : "Press a key", true);
                    l.style.marginBottom = sz.Gap;
                    l.style.marginLeft = sz.ControlPad; l.style.marginRight = sz.ControlPad;
                    var x = Ui.Btn(box, "Cancel", () => { if (mine == gen()) cancel(); }, true);
                    x.name = "key-prompt-cancel";
                    x.style.alignSelf = Align.FlexEnd;
                    x.style.marginRight = 0f;
                };
                // Every close lets go of the poll's mouse buttons (the capture ending closes it too).
                Action closed = () => { if (Input.MousePrompt == mp) Input.MousePrompt = null; if (mine == gen()) cancel(); };
                Popup p;
                if (!over)
                {
                    for (var e = row.parent; e != null; e = e.parent)
                        if (e is ScrollView sv) { Reveal(sv, row); break; }
                    // Placed on the card's first layout, from the row's bounds after this scroll.
                    p = Popup.Open(b, build, closed);
                }
                else
                {
                    VisualElement win = row;
                    foreach (var st in Ui.Stack)
                        if (st.Root != null && st.Root.Contains(b)) { while (win.parent != null && win.parent != st.Root) win = win.parent; break; }
                    p = Popup.Open(b, build, closed, true, win.worldBound.center, false);
                }
                if (p == null) return;
                p.Content.name = "key-prompt";
                if (mouse)
                {
                    Input.MousePrompt = mp;
                    p.Pressed = button => { if (mine == gen()) { Input.PromptBound("Mouse" + button); got("Mouse" + button); } return true; };
                }
                opened(p);
            };
            if (row.layout.height > 0f && b.layout.height > 0f) { show(false); return; }   // NaN or 0 until laid out
            EventCallback<GeometryChangedEvent> laid = null;
            laid = _ =>
            {
                if (mine == gen() && !(row.layout.height > 0f && b.layout.height > 0f)) return;
                Events.Off(row, laid);
                show(false);
            };
            Events.On(row, laid);
            int ticks = 0;
            IVisualElementScheduledItem wait = null;
            wait = b.schedule.Execute(() =>
            {
                if (shown || mine != gen()) { wait.Pause(); Events.Off(row, laid); return; }
                if (++ticks < 4) return;
                wait.Pause();
                Events.Off(row, laid);
                show(true);
            }).Every(0);
        }

        /* Scrolls row into sv's viewport at once: ScrollView.ScrollTo may defer to a later frame, after the prompt was
           placed from the row's old spot. Distances are in world units, the offset in the scroll's own (a window's
           scale sits between them). */
        private static void Reveal(ScrollView sv, VisualElement row)
        {
            Glide.Stop(sv);   // a glide would carry the row off again
            var vp = sv.contentViewport.worldBound;
            var r = row.worldBound;
            float k = sv.contentViewport.layout.height > 0f ? vp.height / sv.contentViewport.layout.height : 1f;
            float dy = r.yMax > vp.yMax ? Mathf.Min(r.yMax - vp.yMax, r.yMin - vp.yMin) : r.yMin < vp.yMin ? r.yMin - vp.yMin : 0f;
            if (dy == 0f || float.IsNaN(dy) || float.IsInfinity(dy) || !(k > 0f)) return;
            sv.scrollOffset = new Vector2(sv.scrollOffset.x, sv.scrollOffset.y + dy / k);
        }

        // The key's name is data, not a word: set by hand so a relabel leaves it alone. Empty reads "unbound".
        private static void Draw(Button b, string name, bool mixed, bool chord = false)
        {
            if (mixed) Words.Label(b, "Mixed", true);
            else if (string.IsNullOrEmpty(name)) Words.Label(b, "unbound", true);
            else b.text = chord ? Shortcut.Display(name, Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor) : name;
        }

        // A number box's text while its row is bound to many items that disagree.
        private static string Cut(string s, int max) => max > 0 && s != null && s.Length > max ? s.Substring(0, max) : s;

        private static string MixedText(VisualElement e) { return Words.Own("Mixed", Host.For(e)); }

        /// Read-only text beside a label that wraps when long, instead of a TextField that cuts it off. With Tone the
        /// value takes that role, re-read with the text on every refresh pass.
        public static Control<string> Info(VisualElement parent, string label, Func<string> value, Action<InfoOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            right.style.flexShrink = 1f;
            right.style.maxWidth = Length.Percent(65f);
            var v = Ui.Muted(right, "");
            Spaced(v, Theme.For(right).Sizes);
            v.style.whiteSpace = WhiteSpace.Normal;
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            v.style.flexShrink = 1f;
            v.style.minWidth = 0f;
            var tone = o.Tone;
            Func<string> read = value;
            if (tone != null)
            {
                Role last = Role.TextMuted;
                read = () =>
                {
                    var r = tone();
                    if (r != last) { last = r; Paint.Fg(v, r); }
                    return value == null ? null : value();
                };
            }
            return new Control<string>(right.parent, v, label, Bind.To(read, null), s => v.text = s ?? "", o);
        }

        /// Info's signature before InfoOptions, kept so a mod built against it still loads (a lambda that sets no Tone
        /// binds here; the row is the same).
        public static Control<string> Info(VisualElement parent, string label, Func<string> value, Action<ControlOptions> configure)
            => Info(parent, label, value, configure == null ? null : (Action<InfoOptions>)(o => configure(o)));

        public static Button Action(VisualElement parent, string label, string button, Action onClick, Action<ControlOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            if (string.IsNullOrEmpty(label)) Noted(right.parent, button);   // no label: search finds the row by its button
            Button b = null;
            // One write around the click: the rows onClick writes and what it changed show in this frame, in one pass.
            b = Ui.Btn(right, button, () =>
            {
                var r = Refresher.Enter(null, b);
                try { if (onClick != null) onClick(); }
                finally { Refresher.Exit(r); }
            });
            b.style.marginRight = 0f;
            Hook(right.parent, b, label, o);
            return b;
        }

        /// Destructive action: reads as dangerous and asks once. The confirm is the point — every
        /// mod had grown its own armed-button dance for this.
        public static Button Danger(VisualElement parent, string label, string button, Action onConfirm, Action<ControlOptions> configure = null)
        {
            var o = Opts(parent, label, configure);
            var right = Controls.Row(parent, label);
            if (string.IsNullOrEmpty(label)) Noted(right.parent, button);
            var b = Armed(button, onConfirm, right);
            Hook(right.parent, b, label, o);
            return b;
        }

        /* A destructive button that is its own label. "All positions [Reset]" reads as a setting
           called "All positions"; "[Reset all positions]" reads as the action it is. */
        public static Button DangerButton(VisualElement parent, string text, Action onConfirm, Action<ControlOptions> configure = null)
        {
            var o = Opts(parent, text, configure);
            var row = Ui.Row(parent);
            var sz = Theme.For(row).Sizes;
            row.style.minHeight = sz.RowHeight;
            row.style.marginBottom = 2f;
            Carded(row, sz);
            Controls.Noted(row, text);
            var b = Armed(text, onConfirm, row);
            Hook(row, b, text, o);
            return b;
        }

        // ── shared ────────────────────────────────────────────────────────────

        private static Button Armed(string text, Action onConfirm, VisualElement parent)
        {
            Button b = null;
            bool armed = false;
            b = Ui.Btn(parent, text, () =>
            {
                if (!armed)
                {
                    armed = true;
                    Words.Label(b, "Sure?", true);
                    Paint.Select(b, true);
                    return;
                }
                armed = false;
                Words.Label(b, text);
                Paint.Select(b, false);
                Safe.Run(Host.For(b), "Danger handler", () => { if (onConfirm != null) onConfirm(); });
            });
            Paint.Look(b, Theme.For(b).Looks.DangerButton);
            b.style.marginRight = 0f;
            return b;
        }

        // A button row (or a card) has no value, but takes the same options: a Control over a constant gives
        // it the Id naming, tooltips, EnabledWhen, VisibleWhen and ContextMenu the others have.
        internal static Control<string> Hook(VisualElement row, VisualElement b, string label, ControlOptions o)
        {
            o.NoRead = true;
            return new Control<string>(row, b, label, Bind.To<string>(() => null, null), null, o);
        }

        /// A switch, not a checkbox: UI Toolkit's Toggle is a tick box whose styling lives in the
        /// theme style sheet we do not have, so it would come out unstyled. Drawn from two elements
        /// instead — a track and a knob — which also lets it animate. Focusable, with the focus ring
        /// in its look (Looks.Switch); on is Paint.Select. A click or Submit on the track runs `toggle`
        /// and goes no further, so every switch answers the keyboard as well as the pointer.
        internal static VisualElement Switch(VisualElement host, out Action<bool> show, Action toggle)
        {
            Action<bool, bool> look;
            var track = Switch(host, out look, toggle);
            show = v => look(v, false);
            return track;
        }

        /// The same switch with an indeterminate look for a row over many items that disagree: the off track, the knob centred.
        internal static VisualElement Switch(VisualElement host, out Action<bool, bool> show, Action toggle)
        {
            var t = Theme.For(host); var s = t.Sizes; var k = t.Looks;
            var track = new VisualElement();
            track.style.width = s.SwitchWidth;
            track.style.height = s.SwitchHeight;
            Ui.SetRadius(track, s.SwitchRadius);
            Ui.SetBorderWidth(track, s.Line);
            track.focusable = true;
            track.pickingMode = PickingMode.Position;

            // The knob sits 2 in from the track's outer edges (children start inside the border, so Line is taken off); positions are worked out here, not in show,
            // which the refresher calls.
            float size = s.SwitchHeight - 4f, off = 2f - s.Line, on = s.SwitchWidth - size - 2f - s.Line;
            var knob = new VisualElement();
            knob.style.position = Position.Absolute;
            knob.style.top = off;
            knob.style.width = size;
            knob.style.height = size;
            Ui.SetRadius(knob, s.KnobRadius);
            knob.pickingMode = PickingMode.Ignore;
            track.Add(knob);
            host.Add(track);
            Paint.Look(track, k.Switch);
            Paint.Look(knob, k.Knob);
            Events.On<ClickEvent>(track, e => { toggle(); e.StopPropagation(); });
            Events.On<NavigationSubmitEvent>(track, e => { toggle(); e.StopPropagation(); });

            /* A knob with no layout yet resolves `left` to 0, not NaN, so Anim.Move would slide every new
               on-switch in from off (a page rebuilt into a shown window builds on a live panel). Until the
               knob is laid out, place it; only a change after that slides. */
            float mid = (on + off) / 2f;
            show = (v, mixed) =>
            {
                Paint.Select(track, v && !mixed);
                float to = mixed ? mid : v ? on : off;
                if (knob.panel == null || !(knob.layout.width > 0f)) knob.style.left = to;
                else Anim.Move(knob, to, Anim.Fast);
            };
            return track;
        }

        /* A float row: the slider (when there is one) and the number beside it. The slider runs
           0..1 and the scale maps that onto Min..Max, which is how a log slider and a snapped one
           differ from a plain one in nothing but two functions. */
        private static Control<float> FloatRow(VisualElement parent, string label, Bind<float> bind, SliderOptions o, bool withTrack)
        {
            var right = Controls.Row(parent, label);
            var field = NumberField(right, o.Id);
            Slider s = null;
            VisualElement fill = null;
            if (withTrack)
            {
                s = new Slider(0f, 1f);
                s.style.width = Theme.For(right).Sizes.SliderWidth;
                Skin.When<Slider>(s, x => { Skin.Slider(x); fill = Skin.AddFill(x, o.Id); });
                right.Insert(0, s);
                Ring(s);   // once in the row, so it finds the row's theme
            }
            var scale = o.Scale ?? ValueScale.Linear;
            float min = o.Min, max = o.Max;
            // Min == Max is no range: snapped, never clamped.
            Func<float, float> fit = v => { v = ValueScale.Snap(v, o.Step, min); return max > min ? Math.Max(min, Math.Min(max, v)) : v; };
            Func<float, string> text = v => v.ToString(o.Format, CultureInfo.CurrentCulture);

            // Mixed: the thumb at the first item's value, "Mixed" in the number box.
            Action<float, bool> show = (v, mixed) =>
            {
                if (s != null) { s.SetValueWithoutNotify(scale.ToT(v, min, max)); Skin.SetFill(fill, s); }
                field.SetValueWithoutNotify(mixed ? MixedText(field) : text(v));
            };
            var c = TrackedRow(right.parent, s ?? (VisualElement)field, parent, label, o, bind, null, show);

            if (s != null)
            {
                var drag = Watch(s, c);
                Events.On<ChangeEvent<float>>(s, e =>
                {
                    float v = fit(scale.FromT(e.newValue, min, max));
                    if (v == c.Value && !c.Mixed) { show(v, false); return; }   // snapped back onto the value it had
                    if (drag.On) c.Edit(v); else c.Set(v);
                });
            }
            OnTyped(field, c, typed =>
            {
                float v;
                if (!ValueScale.TryParse(typed, CultureInfo.CurrentCulture, out v)) return false;
                v = fit(v);
                if (v != c.Value || c.Mixed) c.Set(v);
                return true;
            }, () => show(c.Value, c.Mixed));
            if (o.Scrub && !withTrack)
            {
                var noted = Data.Find<Noting>(right.parent);
                float unit = ValueScale.ScrubUnit(o.Step, min, max);
                if (noted != null && noted.Label != null) Scrub(noted.Label, c, fit, unit);
            }
            return c;
        }

        private sealed class Scrubbing { public bool On, Moved; public int Pointer; public float X, From; }

        /* The label as a handle: a press captures the pointer, each move writes start + (dx / 4) x unit (Shift x10,
           Alt x0.1) as a drag frame, and the release or a lost capture commits once. The step is sealed then, so a
           second drag soon after is its own undo, not part of this one. */
        private static void Scrub(Label l, Control<float> c, Func<float, float> fit, float unit)
        {
            var s = new Scrubbing();
            l.pickingMode = PickingMode.Position;
            Cursors.Set(l, Cursors.Kind.ResizeHorizontal);
            Events.On<PointerDownEvent>(l, e =>
            {
                if (e.button != 0 || s.On) return;
                s.On = true; s.Moved = false; s.Pointer = e.pointerId; s.X = e.position.x; s.From = c.Value;
                l.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            Events.On<PointerMoveEvent>(l, e =>
            {
                if (!s.On || e.pointerId != s.Pointer) return;
                float k = e.shiftKey ? 10f : e.altKey ? 0.1f : 1f;
                float v = fit(s.From + (e.position.x - s.X) / 4f * unit * k);
                if (v == c.Value && !c.Mixed) return;
                s.Moved = true;
                c.Edit(v);
            });
            Action end = () =>
            {
                if (!s.On) return;
                s.On = false;
                if (s.Moved) { History.Seal(); c.Commit(); }
            };
            Events.On<PointerUpEvent>(l, e => { if (s.On && e.pointerId == s.Pointer) { end(); if (l.HasPointerCapture(e.pointerId)) l.ReleasePointer(e.pointerId); } });
            Events.On<PointerCaptureOutEvent>(l, _ => end());
        }

        // The number beside a slider, named <Id>-number.
        private static TextField NumberField(VisualElement right, string id)
        {
            var sz = Theme.For(right).Sizes;
            var f = new TextField();
            f.style.width = sz.NumberWidth;
            f.style.marginLeft = sz.Gap;
            f.style.fontSize = sz.Small;
            Skin.When<TextField>(f, Skin.Field);
            Cursors.Set(f, Cursors.Kind.Text);
            if (id != null) f.name = id + "-number";
            right.Add(f);
            return f;
        }

        /* Commit typed text on Enter or when focus leaves; `commit` says whether the text was good
           (it only matters to callers; restore runs either way). Bad text puts the shown value back — silently keeping it is worse than refusing it.
           FocusOut, not Blur: focus sits on the field's inner input, and Blur neither fires on the
           TextField nor bubbles. A Many row is pinned to its items while focused: a press on another
           item moves the selection before this field's FocusOut runs, and the commit belongs to the
           items the typing began on. */
        private static void OnTyped<T>(TextField f, Control<T> c, Func<string, bool> commit, Action restore)
        {
            Action apply = () =>
            {
                commit(f.value);
                if (restore != null) restore();   // bad text goes back; good text shows the way the row formats it
            };
            Events.On<FocusInEvent>(f, _ => c.Pin(true));
            Events.On<FocusOutEvent>(f, _ => { try { apply(); } finally { c.Pin(false); } });
            Events.On<KeyDownEvent>(f, e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) apply();
            });
        }

        private sealed class Dragging { public bool On; }

        /* A slider raises a change per frame of a drag: those are Edit, and the release is the one
           Commit. A change with no pointer down (the arrow keys) is a whole change on its own. The
           trickle phase, because the slider's own manipulators stop the events they handle. */
        private static Dragging Watch<T>(VisualElement s, Control<T> c)
        {
            var d = new Dragging();
            Events.On<PointerDownEvent>(s, e => { if (e.button == 0) d.On = true; }, TrickleDown.TrickleDown);
            Action end = () => { if (d.On) { d.On = false; c.Commit(); } };
            Events.On<PointerUpEvent>(s, e => end(), TrickleDown.TrickleDown);
            // The dragger follows the pointer with mouse capture: the moves and the release never reach
            // the slider as pointer events, but the capture ending does (measured in game).
            Events.On<PointerCaptureOutEvent>(s, e => end(), TrickleDown.TrickleDown);
            return d;
        }

        /* A row whose writes record in History. The undo needs the row (to show and commit what it
           restored), and the row needs the wrapped bind first, so the wrapper reads it from a box
           filled once the row exists. The box is a relay, empty while the row is off every panel:
           History outlives pages, and an entry holding the row would keep its removed page alive. An
           undo meanwhile restores the value only; a row back on a panel is refilled and its refresher
           reads the value, and a rebuilt page has new rows. */
        private static Control<T> TrackedRow<T>(VisualElement root, VisualElement input, VisualElement at, string label, ControlOptions o, Bind<T> bind, Action<T> show,
                                                Action<T, bool> showMixed = null)
        {
            var row = new Control<T>[1];
            var c = new Control<T>(root, input, label, Tracked(at, label, o, bind, row), show, o, showMixed);
            if (!o.Undoable) return c;
            Events.On<AttachToPanelEvent>(c.Root, _ => row[0] = c);
            Events.On<DetachFromPanelEvent>(c.Root, _ => row[0] = null);
            if (c.Root.panel != null) row[0] = c;
            return c;
        }

        /* Undo. The bind handed to Control is wrapped so every write — a click, a drag frame, the
           middle-click reset — records through the bind (Bind.SetRecorded): a plain bind records the
           value it had, read inside the write, and a Many bind each item's own. An undo writes the raw
           bind once, then shows the value and raises Committed (Applying is true, Changed is not
           raised). Dragging is one step: History coalesces by key, and a plain bind's drag frame
           allocates nothing. With UndoDocument each write is a step bound to the document it was made
           on. No lambda here reads o: every step would then hold the options, whose delegates (a
           tooltip func, a menu action) often capture the row. */
        private static Bind<T> Tracked<T>(VisualElement at, string label, ControlOptions o, Bind<T> b, Control<T>[] row)
        {
            if (!o.Undoable) return b;
            var host = Host.For(at);
            var tr = Words.Translator(at);   // fixed at build: the recorder's host, or Facet's own table
            var nav = Nav.Active;
            // Per control, never the label alone: two "Enabled" rows on different pages are two steps.
            string key = host.Name + "/" + (nav != null ? nav.PageTitle : "") + "/" + label + "#" + (o.Id ?? (++Seq).ToString()) + LiveTag(at);
            var document = o.UndoDocument;
            var applied = Relay(row);   // made apart: a lambda here would share this method's closure, which holds b
            return b.Wrap(v => b.SetRecorded(v, label, key, tr, applied, document == null ? null : History.BoundTo(document)));
        }

        // Show and commit what an undo restored, on the row in the box.
        private static Action<T> Relay<T>(Control<T>[] row)
        {
            return x =>
            {
                var c = row[0];
                if (c != null) c.Restored(x);
            };
        }
    }
}
