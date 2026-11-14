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
            var t = Theme.For(e);
            e.focusable = true;
            Ui.SetBorderWidth(e, t.Sizes.Line);
            Paint.Look(e, t.Looks.Ring);
        }

        public static Control<bool> Toggle(VisualElement parent, string label, Bind<bool> bind, Action<Options<bool>> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            Action<bool, bool> show;
            Control<bool> c = null;
            var track = Switch(right, out show, () => c.Set(c.Mixed || !c.Value));   // from mixed, a click sets them all on
            c = TrackedRow(right.parent, track, parent, label, o, bind, null, show);
            return c;
        }

        public static Control<float> Slider(VisualElement parent, string label, Bind<float> bind, Action<SliderOptions> configure = null)
        {
            return FloatRow(parent, label, bind, ControlOptions.Make(parent, label, configure), true);
        }

        /// Just the typed number, no track. Unbounded unless you set a range (Min < Max), then clamped to it.
        public static Control<float> Number(VisualElement parent, string label, Bind<float> bind, Action<SliderOptions> configure = null)
        {
            // No range by default: a slider's 0..1 would clamp every typed number.
            return FloatRow(parent, label, bind, ControlOptions.Make<SliderOptions>(parent, label, o =>
            {
                o.Min = o.Max = 0f;
                if (configure != null) configure(o);
            }), false);
        }

        public static Control<int> IntSlider(VisualElement parent, string label, Bind<int> bind, Action<IntSliderOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
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
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            Control<int> c = null;
            var select = Segments(right, o.Items ?? new string[0], i => { if (i != c.Value || c.Mixed) c.Set(i); });
            c = TrackedRow(right.parent, right, parent, label, o, bind, null, (v, mixed) => select(mixed ? -1 : v));   // mixed: none selected
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
            /* The arrow goes in a cell around the button, not in the button: a Button that gains a child
               loses its text measure (UI Toolkit drops it on Insert), so it shrank to its padding and a
               long item overflowed instead of widening it. */
            var cell = Ui.Box(right);
            b = Ui.Btn("", () => Popup.Menu(b, items, c.Mixed ? -1 : c.Value, i => c.Set(i), o.ItemFont), cell);
            var t = Theme.For(b); var sz = t.Sizes;
            Paint.Look(b, t.Looks.Field);
            b.style.marginRight = 0f;   // the box fills its cell, so the arrow is pinned inside the box's border
            b.style.minWidth = sz.DropdownWidth;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingRight = sz.ControlPad + sz.Small + sz.Gap;   // room for the arrow
            var arrow = Ui.Text("\u25be", cell, sz.Small, Role.TextMuted, true);
            arrow.name = o.Id != null ? o.Id + "-arrow" : "arrow";
            arrow.pickingMode = PickingMode.Ignore;
            arrow.style.position = Position.Absolute;
            arrow.style.right = sz.ControlPad; arrow.style.top = 0f; arrow.style.bottom = 0f;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            // An item's English key, translated like any label; out of range shows blank.
            Action<int, bool> show = (v, mixed) =>
            {
                if (mixed) Words.Label(b, "Mixed", true);
                else Words.Label(b, v >= 0 && v < items.Count ? items[v] : "");
            };
            c = TrackedRow(right.parent, b, parent, label, o, bind, null, show);
            return c;
        }

        public static Control<string> Text(VisualElement parent, string label, Bind<string> bind, Action<TextOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            var f = new TextField();
            f.style.width = o.Width;
            Skin.When<TextField>(f, Skin.Field);
            f.style.fontSize = Theme.For(right).Sizes.Small;
            Cursors.Set(f, Cursors.Kind.Text);
            right.Add(f);
            var c = TrackedRow(right.parent, f, parent, label, o, bind, null, (v, mixed) => f.SetValueWithoutNotify(mixed ? "" : v ?? ""));
            /* On commit, not per keystroke: a setter that persists to disk should not run once per
               character typed. Mixed shows empty, and leaving it empty writes nothing: only typing does. */
            OnTyped(f, c, text =>
            {
                text = text ?? "";
                if (c.Mixed ? text.Length > 0 : text != (c.Value ?? "")) c.Set(text);
                return true;
            }, null);
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
                Draw(b, c.Value, c.Mixed);
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
                    name = name ?? "";
                    if (name != (c.Value ?? "") || c.Mixed) c.Set(name); else Draw(b, name, false);
                }));
                if (!ok) end(false);
            }, right);
            Events.On<DetachFromPanelEvent>(b, _ => cancel());
            b.style.marginRight = 0f;
            b.style.minWidth = 110f;
            c = TrackedRow(right.parent, b, parent, label, o, bind, null, (v, mixed) => Draw(b, v, mixed));
            return c;
        }

        // The key's name is data, not a word: set by hand so a relabel leaves it alone. Empty reads "unbound".
        private static void Draw(Button b, string name, bool mixed)
        {
            if (mixed) Words.Label(b, "Mixed", true);
            else if (string.IsNullOrEmpty(name)) Words.Label(b, "unbound", true); else b.text = name;
        }

        // A number box's text while its row is bound to many items that disagree.
        private static string MixedText(VisualElement e) { return Words.Own("Mixed", Host.For(e)); }

        /// Read-only text beside a label that wraps when long, instead of a TextField that cuts it off. With Tone the
        /// value takes that role, re-read with the text on every refresh pass.
        public static Control<string> Info(VisualElement parent, string label, Func<string> value, Action<InfoOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            right.style.flexShrink = 1f;
            right.style.maxWidth = Length.Percent(65f);
            var v = Ui.Muted("", right);
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
            var o = ControlOptions.Make(parent, label, configure);
            var right = Controls.Row(parent, label);
            if (string.IsNullOrEmpty(label)) Noted(right.parent, button);   // no label: search finds the row by its button
            var b = Ui.Btn(button, onClick, right);
            b.style.marginRight = 0f;
            Hook(right.parent, b, label, o);
            return b;
        }

        /// Destructive action: reads as dangerous and asks once. The confirm is the point — every
        /// mod had grown its own armed-button dance for this.
        public static Button Danger(VisualElement parent, string label, string button, Action onConfirm, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
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
            var o = ControlOptions.Make(parent, text, configure);
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
            b = Ui.Btn(text, () =>
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
            }, parent);
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
                if (!float.TryParse(typed, NumberStyles.Float, CultureInfo.CurrentCulture, out v) || float.IsNaN(v) || float.IsInfinity(v)) return false;
                v = fit(v);
                if (v != c.Value || c.Mixed) c.Set(v);
                return true;
            }, () => show(c.Value, c.Mixed));
            return c;
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
