using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
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

    public class TextOptions : Options<string>
    {
        public float Width { get; set; } = 200f;
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
        /// claimed the capture, the control left its panel, or its surface hid. Not called when a key was captured.
        public Action CancelCapture { get; set; }
    }

    /* The value rows. Each is a labelled row bound to a Bind<T> and handed back as a Control<T>;
       every change goes through History under "host/page/label#id", so Ctrl+Z undoes it and a
       drag is one step. */
    public static partial class Controls
    {
        /// Counter for controls built without an Id; Nav resets it before every page build.
        internal static int Seq;
        private static int KeySeq;

        /// Reachable by Tab / NavigationMove, and draws Role.Focus around itself while focused.
        internal static void Ring(VisualElement e)
        {
            e.focusable = true;
            Ui.SetBorderWidth(e, 1f);
            Paint.Look(e, Look.Ring);
        }

        public static Control<bool> Toggle(VisualElement parent, string label, Bind<bool> bind, Action<Options<bool>> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            Action<bool> show;
            var track = Switch(right, out show);
            Ring(track);
            var c = new Control<bool>(right.parent, track, label, Tracked(parent, label, o, bind), show, o);
            Events.On<ClickEvent>(track, e => { c.Set(!c.Value); e.StopPropagation(); });
            return c;
        }

        public static Control<float> Slider(VisualElement parent, string label, Bind<float> bind, Action<SliderOptions> configure = null)
        {
            return FloatRow(parent, label, bind, ControlOptions.Make(parent, label, configure), true);
        }

        /// Just the typed number, no track. Clamped to Min..Max, so set them when 0..1 is not the range.
        public static Control<float> Number(VisualElement parent, string label, Bind<float> bind, Action<SliderOptions> configure = null)
        {
            return FloatRow(parent, label, bind, ControlOptions.Make(parent, label, configure), false);
        }

        public static Control<int> IntSlider(VisualElement parent, string label, Bind<int> bind, Action<IntSliderOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var s = new SliderInt(o.Min, o.Max);
            s.style.width = 180f;
            Skin.When<SliderInt>(s, Skin.Slider);
            Ring(s);
            var field = NumberField(right, o.Id);
            right.Insert(0, s);

            Control<int> c = null;
            Action<int> show = v =>
            {
                s.SetValueWithoutNotify(v);
                field.SetValueWithoutNotify(v.ToString(CultureInfo.CurrentCulture));
            };
            c = new Control<int>(right.parent, s, label, Tracked(parent, label, o, bind), show, o);

            var drag = Watch(s, c);
            Events.On<ChangeEvent<int>>(s, e =>
            {
                if (e.newValue == c.Value) return;
                if (drag.On) c.Edit(e.newValue); else c.Set(e.newValue);
            });
            OnTyped(field, text =>
            {
                int v;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return false;
                v = Math.Max(o.Min, Math.Min(o.Max, v));
                if (v != c.Value) c.Set(v);
                return true;
            }, () => field.SetValueWithoutNotify(c.Value.ToString(CultureInfo.CurrentCulture)));
            return c;
        }

        /* Segmented buttons rather than a popup list. Same reason as the toggle — DropdownField's
           menu is themed by the style sheet we do not have — and for the handful of options a
           setting usually has, seeing them all beats opening a list to find out. */
        public static Control<int> Choice(VisualElement parent, string label, Bind<int> bind, Action<ChoiceOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var buttons = new List<Button>();
            Control<int> c = null;
            var items = o.Items;
            for (int i = 0; i < (items != null ? items.Count : 0); i++)
            {
                int idx = i;
                buttons.Add(Ui.Btn(items[i], () => { if (idx != c.Value) { c.Set(idx); Anim.Pop(buttons[idx]); } }, right));
            }
            Action<int> show = v => { for (int j = 0; j < buttons.Count; j++) Paint.Select(buttons[j], j == v); };
            c = new Control<int>(right.parent, right, label, Tracked(parent, label, o, bind), show, o);
            return c;
        }

        /// A button showing the current item that opens a Popup.Menu of the rest.
        public static Control<int> Dropdown(VisualElement parent, string label, Bind<int> bind, Action<DropdownOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var items = o.Items ?? new string[0];
            Control<int> c = null;
            Button b = null;
            b = Ui.Btn("", () => Popup.Menu(b, items, c.Value, i => c.Set(i), o.ItemFont), right);
            b.style.minWidth = 120f;
            // An item's English key, translated like any label; out of range shows blank.
            Action<int> show = v => Words.Label(b, v >= 0 && v < items.Count ? items[v] : "");
            c = new Control<int>(right.parent, b, label, Tracked(parent, label, o, bind), show, o);
            return c;
        }

        public static Control<string> Text(VisualElement parent, string label, Bind<string> bind, Action<TextOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var f = new TextField();
            f.style.width = o.Width;
            Skin.When<TextField>(f, Skin.Field);
            f.style.fontSize = Tokens.FontSizeSmall;
            right.Add(f);
            var c = new Control<string>(right.parent, f, label, Tracked(parent, label, o, bind), v => f.SetValueWithoutNotify(v ?? ""), o);
            // On commit, not per keystroke: a setter that persists to disk should not run once per
            // character typed.
            OnTyped(f, text => { if (text != (c.Value ?? "")) c.Set(text); return true; }, null);
            return c;
        }

        /* Colour. The picker itself is ColourPicker — Topaz's model, the best of the three the
           mods had — and this is the row that hosts it. The picker keeps its own name (the tests
           look for it), so Root and Input are two wrappers around it. */
        public static Control<Color> Colour(VisualElement parent, string label, Bind<Color> bind, Action<ColourOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var root = Ui.Row(parent);
            Controls.Noted(root, label);
            root.style.alignItems = Align.FlexStart;
            var inner = Ui.Box(root);
            inner.style.flexGrow = 1f;
            inner.style.minWidth = 0f;
            Control<Color> c = null;
            var picker = new ColourPicker(inner, label, Color.white, o.Alpha, col => { if (c != null) c.Set(col); }, col => { if (c != null) c.Edit(col); }, () => { if (c != null) c.Commit(); });
            c = new Control<Color>(root, inner, label, Tracked(parent, label, o, bind), picker.SetColour, o);
            // The dot sits in the flow before the picker: level it with the header, not the whole picker.
            var dot = root.Q("changed");
            if (dot != null) dot.style.marginTop = (Controls.RowHeight - 6f) / 2f;
            return c;
        }

        /* Keybind capture. Click, then press — the next key wins, Escape cancels, and the row says
           which it is waiting for. The capture has to be polled by the host rather than read from a
           UI Toolkit event, because the keys worth binding include ones the panel would otherwise
           treat as navigation (Tab, arrows, Escape). */
        public static Control<string> Key(VisualElement parent, string label, Bind<string> bind, Action<KeyOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            Control<string> c = null;
            Button b = null;
            // One owner name per control; a claim by another control (any mod, any Facet version) cancels this one.
            string me = "key#" + (++KeySeq) + "@" + Host.For(parent).Name + "/" + typeof(Controls).Assembly.GetName().Name;
            bool active = false;
            int gen = 0;   // a callback from a cancelled capture arrives late and is ignored
            Surface owner = null;
            Action cancel = null;
            // Ends the capture on the control's side; the host hears about it only when cancelled.
            Action<bool> end = tellHost =>
            {
                if (!active) return;
                active = false; gen++;
                KeyCapture.Release(me);
                if (owner != null) { owner.Hidden -= cancel; owner = null; }
                Paint.Select(b, false);
                Draw(b, c.Value);
                if (tellHost && o.CancelCapture != null) Safe.Run(Host.For(b), "Key CancelCapture", o.CancelCapture);
            };
            cancel = () => end(true);
            b = Ui.Btn("", () =>
            {
                if (o.Capture == null) return;
                var host = Host.For(b);
                end(true);   // a second click while waiting restarts cleanly
                KeyCapture.Claim(me, cancel, host.LogFn);
                active = true;
                int mine = ++gen;
                foreach (var st in Ui.Stack) if (st.Root != null && st.Root.Contains(b)) { owner = st; break; }
                if (owner != null) owner.Hidden += cancel;
                Words.Label(b, "press a key…", true);
                Paint.Select(b, true);
                bool ok = Safe.Run(host, "Key capture", () => o.Capture(name =>
                {
                    if (mine != gen) return;
                    end(false);
                    Anim.Pop(b);
                    name = name ?? "";
                    if (name != (c.Value ?? "")) c.Set(name); else Draw(b, name);
                }));
                if (!ok) end(false);
            }, right);
            Events.On<DetachFromPanelEvent>(b, _ => cancel());
            b.style.minWidth = 110f;
            c = new Control<string>(right.parent, b, label, Tracked(parent, label, o, bind), v => Draw(b, v), o);
            return c;
        }

        // The key's name is data, not a word: set by hand so a relabel leaves it alone. Empty reads "unbound".
        private static void Draw(Button b, string name)
        {
            if (string.IsNullOrEmpty(name)) Words.Label(b, "unbound", true); else b.text = name;
        }

        /// Read-only text beside a label that wraps when long, instead of a TextField that cuts it off.
        public static Control<string> Info(VisualElement parent, string label, Func<string> value, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            right.style.flexShrink = 1f;
            right.style.maxWidth = Length.Percent(65f);
            var v = Ui.Muted("", right);
            v.style.whiteSpace = WhiteSpace.Normal;
            v.style.unityTextAlign = TextAnchor.MiddleRight;
            v.style.flexShrink = 1f;
            v.style.minWidth = 0f;
            return new Control<string>(right.parent, v, label, Bind.To(value, null), s => v.text = s ?? "", o);
        }

        public static Button Action(VisualElement parent, string label, string button, Action onClick, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var b = Ui.Btn(button, onClick, right);
            Hook(right.parent, b, label, o);
            return b;
        }

        /// Destructive action: reads as dangerous and asks once. The confirm is the point — every
        /// mod had grown its own armed-button dance for this.
        public static Button Danger(VisualElement parent, string label, string button, Action onConfirm, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var b = Armed(button, onConfirm, right);
            Hook(right.parent, b, label, o);
            return b;
        }

        /* A destructive button that is its own label. "All positions [Reset]" reads as a setting
           called "All positions"; "[Reset all positions]" reads as the action it is. */
        public static Button DangerButton(VisualElement parent, string text, Action onConfirm, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, text, configure);
            var row = Ui.Row(parent);
            row.style.minHeight = Controls.RowHeight;
            row.style.marginBottom = 2f;
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
            b = Ui.Btn(text, () =>
            {
                if (!armed)
                {
                    armed = true;
                    Words.Label(b, "Sure?", true);
                    Paint.Select(b, true);
                    Anim.Pop(b);
                    return;
                }
                armed = false;
                Words.Label(b, text);
                Paint.Select(b, false);
                Safe.Run(Host.For(b), "Danger handler", () => { if (onConfirm != null) onConfirm(); });
            }, parent);
            Paint.Look(b, Look.DangerButton);
            return b;
        }

        // A button row has no value, but takes the same options: a Control over a constant gives it the
        // Id naming, tooltips, EnabledWhen, VisibleWhen and ContextMenu the others have.
        private static void Hook(VisualElement row, Button b, string label, ControlOptions o)
        {
            o.NoRead = true;
            new Control<string>(row, b, label, Bind.To<string>(() => null, null), null, o);
        }

        /// A switch, not a checkbox: UI Toolkit's Toggle is a tick box whose styling lives in the
        /// theme style sheet we do not have, so it would come out unstyled. Drawn from two elements
        /// instead — a track and a knob — which also lets it animate.
        internal static VisualElement Switch(VisualElement host, out Action<bool> show)
        {
            var track = new VisualElement();
            track.style.width = 36f;
            track.style.height = 18f;
            Ui.SetRadius(track, 9f);
            track.pickingMode = PickingMode.Position;

            var knob = new VisualElement();
            knob.style.position = Position.Absolute;
            knob.style.top = 2f;
            knob.style.width = 14f;
            knob.style.height = 14f;
            Ui.SetRadius(knob, 7f);
            Paint.Bg(knob, Role.Text);
            knob.pickingMode = PickingMode.Ignore;
            track.Add(knob);
            host.Add(track);

            // Before the first layout `left` resolves to NaN and Anim.Move starts at the target: a new switch does not slide in.
            show = v =>
            {
                Paint.Bg(track, v ? Role.Accent : Role.RowAlt);
                Anim.Move(knob, v ? 20f : 2f, Anim.Fast);
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
            if (withTrack)
            {
                s = new Slider(0f, 1f);
                s.style.width = 180f;
                Skin.When<Slider>(s, Skin.Slider);
                Ring(s);
                right.Insert(0, s);
            }
            var scale = o.Scale ?? ValueScale.Linear;
            float min = o.Min, max = o.Max;
            Func<float, float> fit = v => Math.Max(min, Math.Min(max, ValueScale.Snap(v, o.Step, min)));
            Func<float, string> text = v => v.ToString(o.Format, CultureInfo.CurrentCulture);

            Action<float> show = v =>
            {
                if (s != null) s.SetValueWithoutNotify(scale.ToT(v, min, max));
                field.SetValueWithoutNotify(text(v));
            };
            var c = new Control<float>(right.parent, s ?? (VisualElement)field, label, Tracked(parent, label, o, bind), show, o);

            if (s != null)
            {
                var drag = Watch(s, c);
                Events.On<ChangeEvent<float>>(s, e =>
                {
                    float v = fit(scale.FromT(e.newValue, min, max));
                    if (v == c.Value) { show(v); return; }   // snapped back onto the value it had
                    if (drag.On) c.Edit(v); else c.Set(v);
                });
            }
            OnTyped(field, typed =>
            {
                float v;
                if (!float.TryParse(typed, NumberStyles.Float, CultureInfo.CurrentCulture, out v) || float.IsNaN(v) || float.IsInfinity(v)) return false;
                v = fit(v);
                if (v != c.Value) c.Set(v);
                return true;
            }, () => field.SetValueWithoutNotify(text(c.Value)));
            return c;
        }

        // The number beside a slider, named <Id>-number.
        private static TextField NumberField(VisualElement right, string id)
        {
            var f = new TextField();
            f.style.width = 62f;
            f.style.marginLeft = Tokens.Gap;
            f.style.fontSize = Tokens.FontSizeSmall;
            Skin.When<TextField>(f, Skin.Field);
            Cursors.Set(f, Cursors.Kind.Text);
            if (id != null) f.name = id + "-number";
            right.Add(f);
            return f;
        }

        /* Commit typed text on Enter or when focus leaves; `commit` says whether the text was good
           (it only matters to callers; restore runs either way). Bad text puts the shown value back — silently keeping it is worse than refusing it.
           FocusOut, not Blur: focus sits on the field's inner input, and Blur neither fires on the
           TextField nor bubbles. */
        private static void OnTyped(TextField f, Func<string, bool> commit, Action restore)
        {
            Action apply = () =>
            {
                commit(f.value);
                if (restore != null) restore();   // bad text goes back; good text shows the way the row formats it
            };
            Events.On<FocusOutEvent>(f, _ => apply());
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

        /* Undo. The bind handed to Control is wrapped so every write — a click, a drag frame, the
           middle-click reset — records, and an undo writes the raw bind. The previous value is read
           INSIDE the setter, not captured from an enclosing variable, or the undo closure would see
           whatever the value had become by the time it ran. Dragging is one step: History coalesces
           by key. */
        private static Bind<T> Tracked<T>(VisualElement at, string label, ControlOptions o, Bind<T> b)
        {
            var host = Host.For(at);
            var tr = Words.Translator(at);   // fixed at build: the recorder's host, or Facet's own table
            var nav = Nav.Active;
            // Per control, never the label alone: two "Enabled" rows on different pages are two steps.
            string key = host.Name + "/" + (nav != null ? nav.PageTitle : "") + "/" + label + "#" + (o.Id ?? (++Seq).ToString());
            string site = "History '" + label + "'";
            return Bind.To(b.Get, v =>
            {
                T prev = default(T);
                bool read = Safe.Run(host, site, () => { prev = b.Get(); });
                if (read && !EqualityComparer<T>.Default.Equals(prev, v))
                    History.Record(label, key, () => b.Set(prev), () => b.Set(v), tr);
                b.Set(v);
            });
        }
    }
}
