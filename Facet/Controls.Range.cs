using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// Controls.Range's options: both ends run Min..Max, snapped to Step counted from Min (0: no snapping).
    public sealed class RangeOptions : Options<Vector2>
    {
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public float Step { get; set; }
        public string Format { get; set; } = "0.##";
        /// Names the high number field HighId + "-input". Defaults to the high label, as Id does to the low one.
        public string HighId { get; set; }
    }

    public static partial class Controls
    {
        private sealed class RangeDrag { public bool On, High, Moved; }

        /* Two ends over a Vector2 (x the low end, y the high one) in one row: the low label, its number, a track with
           two thumbs and an Accent span between them, the high number and the high label. x <= y always: an end moved
           past the other pushes it along. A stored x > y shows swapped and is written only once the reader changes
           an end. One History step per drag and per typed number. */
        public static Control<Vector2> Range(VisualElement parent, string lowLabel, string highLabel, Bind<Vector2> range, Action<RangeOptions> configure = null)
        {
            var o = Opts(parent, lowLabel, configure);
            if (o.Id == null) o.Id = lowLabel;
            if (o.HighId == null) o.HighId = highLabel;
            var right = Row(parent, lowLabel);
            var t = Theme.For(right); var sz = t.Sizes; var lk = t.Looks;
            float min = o.Min, max = o.Max, step = o.Step;

            var lowField = NumberField(right, null);
            var track = Ui.Box(right);
            track.name = o.Id + "-track";
            track.style.width = sz.SliderWidth;
            track.style.height = sz.ThumbHeight + 2f * sz.FieldPad;   // room above and below the thumb, so a press need not land on it exactly
            track.style.marginLeft = sz.Gap;
            track.pickingMode = PickingMode.Position;
            var line = Ui.Box(track);
            line.pickingMode = PickingMode.Ignore;
            line.style.position = Position.Absolute;
            line.style.left = 0f; line.style.right = 0f;
            line.style.top = Length.Percent(50f);
            line.style.height = sz.Track;
            line.style.marginTop = -sz.Track / 2f;
            Ui.SetRadius(line, sz.Track / 2f);
            Paint.Look(line, lk.Track);
            var span = Ui.Box(line);
            span.pickingMode = PickingMode.Ignore;
            span.style.position = Position.Absolute;
            span.style.top = 0f; span.style.bottom = 0f;
            Ui.SetRadius(span, sz.Track / 2f);
            Paint.Bg(span, Role.Accent);
            Func<VisualElement> thumb = () =>
            {
                var e = Ui.Box(track);
                e.pickingMode = PickingMode.Ignore;
                e.style.position = Position.Absolute;
                e.style.width = sz.ThumbWidth;
                e.style.height = sz.ThumbHeight;
                e.style.top = Length.Percent(50f);
                e.style.marginTop = -sz.ThumbHeight / 2f;
                e.style.marginLeft = -sz.ThumbWidth / 2f;
                Ui.SetRadius(e, Mathf.Min(sz.KnobRadius, Mathf.Min(sz.ThumbWidth, sz.ThumbHeight) / 2f));
                Paint.Look(e, lk.Thumb);
                return e;
            };
            var lowThumb = thumb();
            var highThumb = thumb();
            var highField = NumberField(right, null);
            var highText = Ui.Text(right, highLabel, sz.Body);
            highText.style.marginLeft = sz.Gap;
            highText.pickingMode = PickingMode.Ignore;
            Spaced(highText, sz);
            Noted(highText, highLabel);

            Func<float, float> at = v => max > min ? Mathf.Clamp01((v - min) / (max - min)) : 0f;
            Func<float, string> text = v => v.ToString(o.Format, CultureInfo.CurrentCulture);
            Action<Vector2, bool> show = (v, mixed) =>
            {
                float a, b;
                RangeMath.Shown(v.x, v.y, out a, out b);
                float ta = at(a), tb = at(b);
                lowThumb.style.left = Length.Percent(100f * ta);
                highThumb.style.left = Length.Percent(100f * tb);
                span.style.left = Length.Percent(100f * ta);
                span.style.width = Length.Percent(100f * (tb - ta));
                lowField.SetValueWithoutNotify(mixed ? MixedText(lowField) : text(a));
                highField.SetValueWithoutNotify(mixed ? MixedText(highField) : text(b));
            };
            var c = TrackedRow(right.parent, lowField, parent, lowLabel, o, range, null, show);
            highField.name = o.HighId + "-input";
            bool undoable = o.Undoable;

            // One end to v, measured from the ends as shown; false when that changes nothing.
            Func<bool, float, bool, bool> move = (high, v, dragging) =>
            {
                float a, b;
                RangeMath.Shown(c.Value.x, c.Value.y, out a, out b);
                float a0 = a, b0 = b;
                RangeMath.Move(ref a, ref b, high, v, min, max, step);
                if (a == a0 && b == b0 && !c.Mixed) return false;
                if (dragging) c.Edit(new Vector2(a, b)); else c.Set(new Vector2(a, b));
                return true;
            };
            Action<TextField, bool> typed = (f, high) => OnTyped(f, c, s =>
            {
                float v;
                if (!ValueScale.TryParse(s, CultureInfo.CurrentCulture, out v)) return false;
                if (move(high, v, false) && undoable) History.Seal();   // the next typed number is its own step
                return true;
            }, () => show(c.Value, c.Mixed));
            typed(lowField, false);
            typed(highField, true);

            // The track takes the pointer (thumbs and span ignore it): a press moves the nearer end there, a drag
            // carries it, and the release or a lost capture commits once.
            var d = new RangeDrag();
            Func<Vector2, float> value = p =>
            {
                float w = track.layout.width;
                float k = w > 0f ? Mathf.Clamp01(p.x / w) : 0f;
                return RangeMath.Snap(min + k * (max - min), min, max, step);
            };
            Ui.Pointer(track, (p, button) =>
            {
                if (button != 0) return;
                float v = value(p), a, b;
                RangeMath.Shown(c.Value.x, c.Value.y, out a, out b);
                d.High = a == b ? v >= b : Math.Abs(v - b) < Math.Abs(v - a);
                d.On = true;
                d.Moved = move(d.High, v, true);
            }, p =>
            {
                if (d.On && move(d.High, value(p), true)) d.Moved = true;
            }, (p, button) =>
            {
                if (!d.On) return;
                d.On = false;
                if (!d.Moved) return;
                if (undoable) History.Seal();   // a second drag soon after is its own step
                c.Commit();
            });
            return c;
        }
    }
}
