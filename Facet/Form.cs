using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// Append-only: the first nine are PrismLib.UI.SettingControl's order.
    public enum FieldKind { Bool, Int, Float, Choice, Text, Action, Colour, Key, Info, Dropdown, Number }

    /* One setting as data. Get and Set are the consumer's: they see the value as an object
       (bool, int, float, int index for Choice/Dropdown, string for Text/Key, a Color or "#rrggbb[aa]"
       string for Colour) and run through Safe. Action fields call Set(null) when pressed. */
    public sealed class Field
    {
        public string Owner, Page, Group, Label, Tooltip;
        public FieldKind Kind;
        public Func<object> Get;
        public Action<object> Set;
        public float Min, Max, Step;
        public string[] Options;
        public string ActionLabel;
        public object Default;
        public Action<Action<string>> Capture;
        /// ControlOptions.Help: a muted line under the label.
        public string Help;
        /// Built into its group's Advanced fold, after the group's other fields (Form.Build with Cards).
        public bool Advanced;
    }

    public sealed class FormOptions
    {
        /// Consecutive Groups become Cards (a field with no Group, a card with no header), not Sections.
        public bool Cards { get; set; }
    }

    public static class Form
    {
        /// Builds a bound control per field into `parent`, consecutive Groups as Sections. A field
        /// that cannot be built, or whose getter cannot be converted, is an unavailable row: never an exception.
        public static void Build(VisualElement parent, IEnumerable<Field> fields) { Build(parent, fields, null); }

        /// Build, with options: Cards makes each run of one Group a Card, its Advanced fields in the card's Advanced fold.
        public static void Build(VisualElement parent, IEnumerable<Field> fields, Action<FormOptions> configure)
        {
            var o = new FormOptions();
            if (configure != null) Safe.Run(Host.For(parent), "Form configure", () => configure(o));
            var run = new List<Field>();
            string group = null;
            foreach (var f in fields)
            {
                if (f == null) continue;
                string g = f.Group ?? "";
                if (g != group && run.Count > 0) { Group(parent, group, run, o.Cards); run.Clear(); }
                group = g;
                run.Add(f);
            }
            if (run.Count > 0) Group(parent, group, run, o.Cards);
        }

        // One run of fields sharing a Group: its Section or Card, the fields, then (cards) the Advanced fold.
        static void Group(VisualElement parent, string g, List<Field> run, bool cards)
        {
            VisualElement into = parent;
            try { into = cards ? Controls.Card(parent, g == "" ? null : g) : g == "" ? parent : Controls.Section(parent, g); }
            catch (Exception e) { Host.For(parent).Log("Form group '" + g + "': " + e.Message); }
            foreach (var f in run) if (!cards || !f.Advanced) One(into, f);
            VisualElement adv = null;
            if (cards) foreach (var f in run) if (f.Advanced)
            {
                if (adv == null)
                {
                    try { adv = Controls.Advanced(into); }
                    catch (Exception e) { Host.For(parent).Log("Form advanced '" + g + "': " + e.Message); adv = into; }
                }
                One(adv, f);
            }
        }

        static void One(VisualElement into, Field f)
        {
            try { BuildOne(into, f); }
            catch (Exception e)
            {
                Host.For(into).Log("Form '" + f.Label + "': " + e.Message);
                try { Ui.Muted(into, (f.Label ?? "?") + " — unavailable"); } catch { }
            }
        }

        static void BuildOne(VisualElement parent, Field f)
        {
            var owner = Host.Named(f.Owner);
            var at = parent;
            if (owner != null && owner != Host.For(parent))
            {
                // Host.For finds the nearest surface first, so the owner is attached to a wrapper the field builds into.
                at = Ui.Box(parent);
                owner.Attach(at);
            }
            using (Host.Use(owner ?? Host.For(parent))) Make(at, f);
        }

        static void Make(VisualElement p, Field f)
        {
            var bad = new bool[1];   // set by a failing getter; read after the build
            Action<ControlOptions> common = o => { o.Id = f.Label; o.Tooltip = f.Tooltip; o.Help = f.Help; };
            string label = f.Label;
            switch (f.Kind)
            {
                case FieldKind.Bool:
                    Doom(Controls.Toggle(p, label, B(f, bad, x => Convert.ToBoolean(x)), o => { common(o); Def(o, f, x => Convert.ToBoolean(x)); }), bad[0]);
                    break;
                case FieldKind.Int:
                    Doom(Controls.IntSlider(p, label, B(f, bad, x => Convert.ToInt32(x)), o =>
                    {
                        common(o); Def(o, f, x => Convert.ToInt32(x));
                        if (f.Max > f.Min) { o.Min = (int)f.Min; o.Max = (int)f.Max; }
                    }), bad[0]);
                    break;
                case FieldKind.Float:
                case FieldKind.Number:
                    Action<SliderOptions> so = o =>
                    {
                        common(o); Def(o, f, x => Convert.ToSingle(x, CultureInfo.InvariantCulture));
                        if (f.Max > f.Min) { o.Min = f.Min; o.Max = f.Max; }
                        o.Step = f.Step;
                    };
                    var fb = B(f, bad, x => Convert.ToSingle(x, CultureInfo.InvariantCulture));
                    // A Float with no range (Min == Max) is a Number: a slider needs both ends.
                    Doom(f.Kind == FieldKind.Float && f.Max > f.Min ? Controls.Slider(p, label, fb, so) : Controls.Number(p, label, fb, so), bad[0]);
                    break;
                case FieldKind.Choice:
                    Doom(Controls.Choice(p, label, B(f, bad, x => Convert.ToInt32(x)), o => { common(o); Def(o, f, x => Convert.ToInt32(x)); o.Items = f.Options; }), bad[0]);
                    break;
                case FieldKind.Dropdown:
                    Doom(Controls.Dropdown(p, label, B(f, bad, x => Convert.ToInt32(x)), o => { common(o); Def(o, f, x => Convert.ToInt32(x)); o.Items = f.Options; }), bad[0]);
                    break;
                case FieldKind.Text:
                    Doom(Controls.Text(p, label, B(f, bad, x => Convert.ToString(x, CultureInfo.InvariantCulture)), o => { common(o); Def(o, f, x => Convert.ToString(x, CultureInfo.InvariantCulture)); }), bad[0]);
                    break;
                case FieldKind.Key:
                    Doom(Controls.Key(p, label, B(f, bad, x => Convert.ToString(x, CultureInfo.InvariantCulture)), o =>
                    {
                        common(o); Def(o, f, x => Convert.ToString(x, CultureInfo.InvariantCulture)); o.Capture = f.Capture;
                    }), bad[0]);
                    break;
                case FieldKind.Info:
                    Doom(Controls.Info(p, label, () => { try { return Convert.ToString(Need(f), CultureInfo.InvariantCulture); } catch { bad[0] = true; throw; } }, common), bad[0]);
                    break;
                case FieldKind.Action:
                    Controls.Action(p, label, f.ActionLabel ?? label, () => { if (f.Set != null) f.Set(null); }, common);
                    break;
                case FieldKind.Colour:
                    BuildColour(p, f);
                    break;
                default:
                    throw new ArgumentException("unknown kind " + f.Kind);
            }
        }

        static object Need(Field f)
        {
            if (f.Get == null) throw new InvalidOperationException("no getter");
            var v = f.Get();
            if (v == null) throw new InvalidOperationException("getter returned null");
            return v;
        }

        // The control's own first read failed in the constructor (one strike): take it to three so the row is unavailable now.
        static void Doom<T>(Control<T> c, bool bad)
        {
            if (!bad) return;
            c.Refresh(); c.Refresh();
        }

        static Bind<T> B<T>(Field f, bool[] bad, Func<object, T> conv)
        {
            return Bind.To(() => { try { return conv(Need(f)); } catch { bad[0] = true; throw; } }, v => { if (f.Set != null) f.Set(v); });
        }

        static void Def<T>(Options<T> o, Field f, Func<object, T> conv)
        {
            if (f.Default == null) return;
            try { o.Default = conv(f.Default); } catch { }
        }

        static bool ToColour(object v, out Color c, out bool text, out bool alpha)
        {
            text = false; alpha = false; c = Color.white;
            if (v is Color) { c = (Color)v; alpha = true; return true; }
            var s = v as string;
            if (s == null || !ColorUtility.TryParseHtmlString(s, out c)) return false;
            text = true; alpha = s.Length == 9;
            return true;
        }

        static void BuildColour(VisualElement p, Field f)
        {
            bool text = false, alpha = true;
            Color tmp; bool t, a;
            // Probe once for the form (string or Color) to write back and whether alpha is offered.
            try { if (ToColour(Need(f), out tmp, out t, out a)) { text = t; alpha = a; } } catch { }
            var bind = Bind.To(() =>
            {
                Color c; bool tx, al;
                if (!ToColour(Need(f), out c, out tx, out al)) throw new FormatException("not a colour: " + f.Get());
                text = tx;
                return c;
            }, c =>
            {
                if (f.Set == null) return;
                f.Set(!text ? (object)c : "#" + (alpha ? ColorUtility.ToHtmlStringRGBA(c) : ColorUtility.ToHtmlStringRGB(c)));
            });
            var ctl = Controls.Colour(p, f.Label, bind, o =>
            {
                o.Id = f.Label; o.Tooltip = f.Tooltip; o.Help = f.Help; o.Alpha = alpha;
                Color d; bool tx, al;
                if (f.Default != null && ToColour(f.Default, out d, out tx, out al)) o.Default = d;
            });
            // Value stays at its default when the first read failed; a failed probe means the getter is bad.
            bool bad = false;
            try { Color c; bool tx, al; bad = !ToColour(Need(f), out c, out tx, out al); } catch { bad = true; }
            Doom(ctl, bad);
        }
    }
}
