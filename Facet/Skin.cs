using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* Styling for UI Toolkit's built-in controls.

       Everything a runtime mod can build is unstyled. UI Toolkit's controls get their appearance
       from the default theme style sheet, which is an ASSET — and USS cannot be imported at
       runtime, so a mod has none. Toggle and the dropdown were cheap enough to draw by hand;
       Slider, TextField and ScrollView are not, because their dragging, focus and scrolling logic
       is worth far more than their looks.

       So they are skinned instead: the parts are named children ("unity-tracker", "unity-dragger",
       "unity-text-input"), and setting inline styles on those is the same thing the theme sheet
       would have done. Names are Unity's, not ours — if a Unity upgrade renames one, that control
       goes back to looking like nothing, which is why each lookup is null-checked rather than
       assumed. */
    public static class Skin
    {
        /// The track (Looks.Track, Sizes.Track thick) and the thumb (Looks.Thumb, ThumbWidth x ThumbHeight).
        public static void Slider(VisualElement slider)
        {
            if (slider == null) return;
            var t = Theme.For(slider); var sz = t.Sizes; var k = t.Looks;
            var tracker = slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null)
            {
                Paint.Look(tracker, k.Track);
                tracker.style.position = Position.Absolute;
                tracker.style.left = 0f; tracker.style.right = 0f;
                tracker.style.top = Length.Percent(50f);
                tracker.style.height = sz.Track;
                tracker.style.marginTop = -sz.Track / 2f;
                Ui.SetRadius(tracker, sz.Track / 2f);
                Ui.SetBorderWidth(tracker, 0f);
            }
            var dragger = slider.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                Paint.Look(dragger, k.Thumb);
                dragger.style.width = sz.ThumbWidth;
                dragger.style.height = sz.ThumbHeight;
                // KnobRadius, at most half the thumb's narrow side (a round end): Dark gives 0.1's 6, a 4 px wide thumb 2.
                Ui.SetRadius(dragger, Mathf.Min(sz.KnobRadius, Mathf.Min(sz.ThumbWidth, sz.ThumbHeight) / 2f));
                Ui.SetBorderWidth(dragger, 0f);
                /* Centre it on the track. Both the tracker and the dragger are laid out by the
                   theme — which a runtime mod does not get — so each is placed here against the
                   drag container's midline. Positioning only the dragger just moved the mismatch:
                   the track was not centred either. */
                dragger.style.top = Length.Percent(50f);
                dragger.style.marginTop = -sz.ThumbHeight / 2f;
            }
            // The drag container is what gives the row its height; the default theme sizes it.
            var drag = slider.Q(className: "unity-base-slider__drag-container");
            if (drag != null) drag.style.height = 20f;
            var label = slider.Q<Label>(className: "unity-base-field__label");
            if (label != null) label.style.display = DisplayStyle.None;   // the row already has one
        }

        /* The filled part of a slider's track (Looks.Fill, none by default): inside the tracker, from the
           left, as wide as the value. Read once here; the change callback only sets a width. */
        internal static VisualElement AddFill(VisualElement slider, string id)
        {
            var tracker = slider.Q(className: "unity-base-slider__tracker");
            if (tracker == null) return null;
            var t = Theme.For(slider);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            if (id != null) fill.name = id + "-fill";
            Paint.Look(fill, t.Looks.Fill);
            fill.style.position = Position.Absolute;
            fill.style.left = 0f; fill.style.top = 0f; fill.style.bottom = 0f;
            Ui.SetRadius(fill, t.Sizes.Track / 2f);
            tracker.Add(fill);
            SetFill(fill, slider);
            Events.On<ChangeEvent<float>>(slider, _ => SetFill(fill, slider));
            Events.On<ChangeEvent<int>>(slider, _ => SetFill(fill, slider));
            return fill;
        }

        // Percent of the track up to the value: the float slider runs 0..1 already, the int one over its range.
        internal static void SetFill(VisualElement fill, VisualElement slider)
        {
            if (fill == null) return;
            float t = 0f;
            var f = slider as Slider; var i = slider as SliderInt;
            if (f != null) t = f.value;
            else if (i != null && i.highValue > i.lowValue) t = (float)(i.value - i.lowValue) / (i.highValue - i.lowValue);
            fill.style.width = Length.Percent(100f * Mathf.Clamp01(t));
        }

        public static void Field(VisualElement field)
        {
            if (field == null) return;
            var input = field.Q(className: "unity-base-text-field__input");
            if (input == null) input = field.Q("unity-text-input");
            if (input != null)
            {
                var t = Theme.For(input); var sz = t.Sizes;
                Paint.Look(input, t.Looks.Field);
                Ui.SetBorderWidth(input, sz.Line);
                Ui.SetRadius(input, sz.Radius);
                Ui.SetPadding(input, sz.FieldPad);
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                // Text longer than the box is cut at its border, not drawn past it: nothing clips on a runtime panel by default.
                input.style.overflow = Overflow.Hidden;
                // Without this the caret is drawn in the theme's colour, which is nothing.
                Paint.Tint(input, Role.Text);
            }
            var label = field.Q<Label>(className: "unity-base-field__label");
            if (label != null) label.style.display = DisplayStyle.None;
        }

        /* Scrollbars. A ScrollView with no theme still scrolls — the wheel works — but nothing
           shows how far down you are, which on a log is most of the information. */
        public static void Scroll(ScrollView view)
        {
            if (view == null) return;
            // The thumb's radius is the view's theme's: one not yet in a panel is skinned when it joins one.
            if (view.panel == null) { When<ScrollView>(view, Scroll); return; }
            float radius = Theme.For(view).Sizes.Radius;
            Scroller(view.verticalScroller, radius);
            Scroller(view.horizontalScroller, radius);
            /* The editor theme's stylesheet clips the viewport; a runtime panel has no theme, so
               without this, scrolled rows draw over whatever sits above the view (a window's title
               bar, its search box). */
            view.style.overflow = Overflow.Hidden;
            if (view.contentViewport != null) view.contentViewport.style.overflow = Overflow.Hidden;
        }

        private static void Scroller(Scroller s, float radius)
        {
            if (s == null) return;
            s.style.width = 10f;
            var slider = s.slider;
            if (slider != null)
            {
                var tracker = slider.Q(className: "unity-base-slider__tracker");
                if (tracker != null)
                {
                    Ui.SetBorderWidth(tracker, 0f);
                }
                var dragger = slider.Q(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    Paint.Bg(dragger, Role.RowHover);
                    dragger.style.width = 6f;
                    Ui.SetRadius(dragger, radius);
                    Ui.SetBorderWidth(dragger, 0f);
                }
            }
            // The arrow buttons are pure theme: with none, they are invisible dead space.
            if (s.lowButton != null) s.lowButton.style.display = DisplayStyle.None;
            if (s.highButton != null) s.highButton.style.display = DisplayStyle.None;
        }

        /* A wheel path that does not rely on the host forwarding scroll.

           Clicks reach these panels, so pointer events are delivered — but a runtime UI Toolkit
           panel only receives WheelEvent if the game's input module forwards it, and this game's
           does not appear to. Rather than leave a log you cannot scroll, the window polls the wheel
           itself and drives whichever ScrollView is under the pointer.

           Reports once whether the event ever arrived on its own, so the workaround can be dropped
           if a future game or Unity version starts delivering it. */
        // The offset the wheel last left a ScrollView at, per element through Data so a dropped view is not kept alive.
        private sealed class WheelSeen { public float Y; public bool Set; }
        private static bool _sawWheelEvent, _reported, _movedOnce, _reportedFit;
        private static int _lastWheelFrame = -99;

        /* Whether a real WheelEvent arrived THIS frame, rather than whether one has ever arrived.

           A single "the host sends wheel events" flag was wrong: it was set by the settings panel,
           which does get them, and that switched polling off for the debug list, which does not —
           so the log stopped scrolling. Per-frame is the honest question, because the answer
           differs between panels and between the elements inside one. */
        public static bool WheelHandledThisFrame => Time.frameCount - _lastWheelFrame <= 1;

        public static void WatchWheel(VisualElement root)
        {
            if (root == null) return;
            /* TrickleDown, so this is seen on the way IN — a ScrollView that handles the wheel
               stops it bubbling, and a bubbling handler would therefore never hear about exactly
               the case it needs to know about. */
            Events.On<WheelEvent>(root, e =>
            {
                _lastWheelFrame = Time.frameCount;
                NativeStep(e.target as VisualElement, Host.For(root));
                Glide.Native(e, Host.For(root));
                if (_sawWheelEvent) return;
                _sawWheelEvent = true;
                Host.For(root).Log("Skin: host delivers WheelEvent; polling stands down on frames it arrives");
            }, TrickleDown.TrickleDown);
        }

        /* The panel's own path moves a ScrollView delta.y x mouseWheelScrollSize, and Unity 6's input layer
           sends 3 per wheel unit (InputForUI: -3 x Input.mouseScrollDelta), so a third of the host's speed
           moves it as far as the polled path does. Set on each scroller under the pointer just before it
           handles the event, so a changed setting shows on the next scroll. */
        private const float NativeUnitsPerWheel = 3f;

        private static void NativeStep(VisualElement target, Host host)
        {
            float speed = host.WheelSpeed;
            if (speed <= 0f) return;
            for (var sv = target as ScrollView ?? target?.GetFirstAncestorOfType<ScrollView>(); sv != null; sv = sv.GetFirstAncestorOfType<ScrollView>())
                sv.mouseWheelScrollSize = speed / NativeUnitsPerWheel;
        }

        /// Scroll whatever is under the pointer. `delta` is Input.mouseScrollDelta.y.
        public static void Wheel(VisualElement root, Vector2 pointerScreen, float delta)
        {
            if (root == null || root.panel == null || Mathf.Approximately(delta, 0f)) return;

            var lists = root.Query<ScrollView>().ToList();
            /* ScreenToPanel divides by the panel's scale and does NOT flip y — measured, not
               assumed: screen (1545,856) came back as panel (1098,608), which is 856/1.405 for
               this panel's scale. Unity screen coordinates start at the bottom, panel coordinates
               at the top, so the flip belongs here. */
            var point = RuntimePanelUtils.ScreenToPanel(
                root.panel, new Vector2(pointerScreen.x, Screen.height - pointerScreen.y));

            ScrollView target = null, biggest = null;
            foreach (var sv in lists)
            {
                var b = sv.worldBound;
                if (biggest == null || b.width * b.height > biggest.worldBound.width * biggest.worldBound.height)
                    biggest = sv;
                if (!b.Contains(point)) continue;
                // Innermost wins: a list inside a scrolling page should take the wheel.
                if (target == null || b.width * b.height < target.worldBound.width * target.worldBound.height)
                    target = sv;
            }

            /* Falling back to the biggest scroller when the point misses everything is deliberate.
               Getting the pointer into panel space depends on the panel's scale mode and on which
               screen coordinates the host reports, and a window you cannot scroll is a worse
               outcome than one that scrolls its main list when the pointer is over its chrome. */
            bool missed = target == null;
            if (missed) target = biggest;

            if (!_reported)
            {
                _reported = true;
                Host.For(root).Log("Skin wheel: delta=" + delta.ToString("0.##")
                       + " screen=" + pointerScreen.x.ToString("0") + "," + pointerScreen.y.ToString("0")
                       + " panel=" + point.x.ToString("0") + "," + point.y.ToString("0")
                       + " scrollviews=" + lists.Count
                       + (target == null ? " NONE FOUND"
                          : " target=" + target.worldBound
                            + " content=" + target.contentContainer.layout.height.ToString("0")
                            + " view=" + target.contentViewport.layout.height.ToString("0")
                            + " list=" + (target.GetFirstAncestorOfType<ListView>() != null ? "yes" : "no")
                            + (missed ? " (fallback, point missed all)" : ""))
                       + (_sawWheelEvent ? "" : " [host sends no WheelEvent]"));
            }
            if (target == null) return;

            /* A virtualised ListView does not grow its content container — that is the whole point
               of virtualising — so measuring it says a 53-row log has nothing to scroll. Glide.Max asks
               the list how many rows it has instead. */
            float view = target.contentViewport.layout.height;
            float max = Glide.Max(target);

            if (max <= 0f)
            {
                if (!_reportedFit)
                {
                    _reportedFit = true;
                    Host.For(root).Log("Skin wheel: nothing to scroll here — content " + target.contentContainer.layout.height.ToString("0")
                           + " fits the " + view.ToString("0") + " view");
                }
                return;
            }

            /* A wheel notch reports ±1; a trackpad reports fractions — the measured value here was
               -0.05, which at the 40px-per-unit this used to apply moved the page TWO pixels and
               looked exactly like nothing happening. Scale the two separately: a notch is worth
               about three rows, and a trackpad's continuous stream is worth enough per event to
               keep up with the finger. A host that gives a WheelSpeed gets one linear step instead, so
               its windows scroll like its other menus at the player's setting. */
            float speed = Host.For(root).WheelSpeed;
            float px = speed > 0f ? delta * speed : Mathf.Abs(delta) < 0.6f ? delta * 400f : delta * 60f;
            if (Glide.On) { Glide.Polled(target, -px, max, Host.For(root)); return; }
            var o = target.scrollOffset;
            float before = o.y;

            /* If the native handler already moved this one this frame, leave it alone. That is the
               precise version of "the host handles the wheel" — asked of the element that matters
               rather than of the panel as a whole. (A glide does not move it in the handler, so
               Glide.Polled asks when the native path last fed it instead.) */
            var seen = Data.Get<WheelSeen>(target);
            if (WheelHandledThisFrame && seen.Set && !Mathf.Approximately(seen.Y, before))
            {
                seen.Y = before;
                return;
            }

            o.y = Mathf.Clamp(before - px, 0f, max);
            target.scrollOffset = o;
            seen.Y = o.y; seen.Set = true;

            if (!_movedOnce)
            {
                _movedOnce = true;
                Host.For(root).Log("Skin wheel: scrolled " + before.ToString("0") + " -> " + o.y.ToString("0")
                       + " of " + max.ToString("0"));
            }
        }

        /* Styling has to wait for the control to be attached: the parts are created when it joins a
           panel, so a Q() before that finds nothing. */
        public static void When<T>(VisualElement e, System.Action<T> apply) where T : VisualElement
        {
            if (e == null) return;
            var t = e as T;
            if (t == null) return;
            if (e.panel != null) { apply(t); return; }
            EventCallback<AttachToPanelEvent> cb = null;
            cb = _ => { apply(t); Events.Off(e, cb); };
            Events.On(e, cb);
        }
    }
}
