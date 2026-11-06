using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Plan 7 sections (the parts and skinning hooks), one iterator each; Start runs them.
    sealed partial class AutoTest
    {
        static float SharedSpeed()
        {
            var d = (Dictionary<string, object>)AppDomain.CurrentDomain.GetData("Facet.Shared");
            return ((float[])d["Facet.AnimSpeed"])[0];
        }

        static void Mouse<T>(VisualElement e) where T : MouseEventBase<T>, new()
        {
            using (var ev = MouseEventBase<T>.GetPooled()) { ev.target = e; e.SendEvent(ev); }
        }

        // Duration of the single transition value, in ms (-1 when the list is not one value).
        static float FadeMs(VisualElement e)
        {
            var l = e.style.transitionDuration.value;
            return l != null && l.Count == 1 ? l[0].value * (l[0].unit == TimeUnit.Second ? 1000f : 1f) : -1f;
        }

        // Review Focus 3, first half: a theme's sizes and looks are its own, and Dark's cannot be written.
        IEnumerator SectionThemeShape()
        {
            Theme.Dark.Sizes.Radius = 99f;
            Theme.Dark.Looks.Button.Bg = Role.Danger;
            Check(Theme.Dark.Sizes.Radius == 3f && Theme.Dark.Looks.Button.Bg == Role.RowAlt, "a write to Theme.Dark's Sizes or Looks goes nowhere",
                Theme.Dark.Sizes.Radius + " " + Theme.Dark.Looks.Button.Bg);
            var a = new Theme(); var b = new Theme();
            a.Sizes.Radius = 6f; a.Looks.Button.Bg = Role.Danger;
            Check(a.Sizes.Radius == 6f && b.Sizes.Radius == 3f && b.Looks.Button.Bg == Role.RowAlt, "one new Theme's Radius 6 leaves a second at 3",
                a.Sizes.Radius + " / " + b.Sizes.Radius);
            a.Recolour(Color.green);
            Check(a.Sizes.Radius == 6f && a.Looks.Button.Bg == Role.Danger && Theme.FromAccent(Color.green).Sizes.Radius == 3f,
                "Recolour keeps a theme's sizes and looks; FromAccent starts from Dark's");
            Check(Theme.Dark[Role.Line] == Theme.Dark[Role.PanelBorder] && Theme.Dark[Role.Off] == Theme.Dark[Role.RowAlt] && Theme.Dark[Role.Rail].a == 0f,
                "Dark's Line is PanelBorder, Off is RowAlt and Rail is clear");

            var t = new Theme();
            var h = Host.Create("ThemeShape", o => { o.Log = m => Main.Log("[shape] " + m); o.Theme = t; });
            var s = new Surface(h, "FacetThemeShape", Layer.Window);
            try
            {
                var e = Ui.Box(s.Root);
                e.style.width = 20f; e.style.height = 20f;
                Paint.Bg(e, Role.Accent);
                yield return null;
                t[Role.Accent] = Color.red;
                Check(!Near(e.style.backgroundColor.value, Color.red), "setting a role alone repaints nothing");
                t.Notify();
                yield return null;
                Check(Near(e.resolvedStyle.backgroundColor, Color.red), "after t.Notify(), an element painted Accent is red a frame later", e.resolvedStyle.backgroundColor.ToString());

                // Border precedence: focus, then selected, then own, then look.
                var x = Ui.Box(s.Root);
                Paint.Look(x, new Look { Border = Role.PanelBorder, SelectedBorder = Role.Accent, FocusBorder = Role.Focus });
                Paint.Border(x, Role.Danger);
                Check(Near(x.style.borderLeftColor.value, t[Role.Danger]), "an own border role beats the look's");
                Paint.Select(x, true);
                Check(Near(x.style.borderLeftColor.value, t[Role.Accent]), "selected, the look's SelectedBorder beats the own role", x.style.borderLeftColor.value.ToString());
                x.focusable = true; x.Focus();
                Check(Near(x.style.borderLeftColor.value, t[Role.Focus]), "focused, FocusBorder beats SelectedBorder", x.style.borderLeftColor.value.ToString());
                x.Blur();
            }
            finally { s.Dispose(); }

            bool threw = false;
            try { Theme.Dark.Notify(); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "Theme.Dark.Notify() throws InvalidOperationException");
        }


        // Task 7: a slider fills up to its value; a dropdown is a compact box with an arrow.
        IEnumerator SectionSliderBox()
        {
            Gallery.Model = new Model();
            Gallery.Open(); yield return null; yield return null;
            var fill = Root.Q("size-fill");
            Check(fill != null && Mathf.Abs(fill.style.width.value.value - 33.3f) < 1f, "size-fill is 33.3% of 0.5-2 at 1",
                fill == null ? "none" : fill.style.width.value.value.ToString());
            Check(fill != null && fill.style.backgroundColor.value.a == 0f, "the fill has no background under Facet's default");
            Root.Q<Slider>("size-input").value = 1f;   // the top of the slider: Set(2)
            yield return null;
            Check(fill != null && Mathf.Abs(fill.style.width.value.value - 100f) < 0.5f && Mathf.Abs(Gallery.Model.Size - 2f) < 1e-4f,
                "after the slider reaches its top, size-fill is 100%", fill?.style.width.value.value.ToString());
            var cnt = Root.Q("count-fill");
            Check(cnt != null && Mathf.Abs(cnt.style.width.value.value - 30f) < 0.5f, "count-fill is 30% at 3 of 0-10",
                cnt == null ? "none" : cnt.style.width.value.value.ToString());
            var pick = Root.Q<Button>("pick-input");
            var fieldBg = Gallery.Host.Theme[Role.Row];
            Check(pick != null && Near(pick.style.backgroundColor.value, fieldBg), "pick-input is painted the Field background",
                pick?.style.backgroundColor.value.ToString());
            Check(pick != null && pick.resolvedStyle.width >= 120f - 0.5f, "pick-input is at least DropdownWidth wide", pick?.resolvedStyle.width.ToString());
            var arrow = pick?.Q<Label>("pick-arrow");
            Check(arrow != null && arrow.text == "\u25be" && arrow.pickingMode == PickingMode.Ignore, "pick-input holds a pick-arrow label that ignores picking");

            if (pick != null)
            {
                Mouse<MouseEnterEvent>(pick); yield return null;
                Check(Near(pick.resolvedStyle.backgroundColor, Gallery.Host.Theme[Role.RowHover]), "a hovered dropdown shows RowHover",
                    pick.resolvedStyle.backgroundColor.ToString());
                Mouse<MouseLeaveEvent>(pick);
            }

            var t = new Theme();
            t.Looks.Fill = new Look { Bg = Role.Accent };
            var h = Host.Create("SliderBox", o => { o.Log = m => Main.Log("[sliderbox] " + m); o.Theme = t; });
            var s = new Surface(h, "FacetSliderBox", Layer.Window);
            try
            {
                var w = Gallery.Build(s.Root, "sliderbox");
                yield return null; yield return null;
                var f2 = w.Root.Q("size-fill");
                Check(f2 != null && Near(f2.style.backgroundColor.value, t[Role.Accent]), "a theme's Looks.Fill paints the fill", f2?.style.backgroundColor.value.ToString());
            }
            finally { s.Dispose(); }
        }

        static float Px(StyleLength l) => l.value.value;

        // Review Focus 3, second half: a skin restyles every part on its own surface, and only there.
        IEnumerator SectionSkin()
        {
            var b = new Theme();
            var z = b.Sizes;
            z.Radius = 6f; z.SwitchRadius = 1f; z.KnobRadius = 1f; z.Line = 2f; z.RowHeight = 40f; z.Body = 20f; z.Small = 16f;
            z.Title = 25f; z.Caps = 14f; z.CapsSpacing = 3f; z.SwitchWidth = 50f; z.SwitchHeight = 24f;
            b.Looks.Button.Bg = Role.Danger;
            Gallery.Model = new Model();
            Gallery.Open(); yield return null;
            var h = Host.Create("Skin", o => { o.Log = m => Main.Log("[skin] " + m); o.Theme = b; });
            var s = new Surface(h, "FacetSkin", Layer.Window);
            try
            {
                var w = Gallery.Build(s.Root, "skin");
                yield return null; yield return null;
                var flag = w.Root.Q("flag"); var track = w.Root.Q("flag-input");
                Check(Px(flag.style.minHeight) == 40f && flag.Q<Label>().style.fontSize.value.value == 20f, "B: the flag row is 40 tall and its label 20",
                    Px(flag.style.minHeight) + " / " + flag.Q<Label>().style.fontSize.value.value);
                Check(Px(track.style.width) == 50f && Px(track.style.borderTopLeftRadius) == 1f, "B: the switch track is 50 wide with radius 1",
                    Px(track.style.width) + " r" + Px(track.style.borderTopLeftRadius));
                Check(w.Root.Q("size-number").style.fontSize.value.value == 16f, "B: the size number field's font is 16");
                // Built before they join the row or the window: still B's, not the fallback theme's.
                Check(w.Root.Q("size-input").style.borderLeftWidth.value == 2f, "B: the slider's focus ring is B's Line wide",
                    w.Root.Q("size-input").style.borderLeftWidth.value.ToString());
                var thumb = w.Body.Q<ScrollView>().verticalScroller.slider.Q(className: "unity-base-slider__dragger");
                Check(thumb != null && Px(thumb.style.borderTopLeftRadius) == 6f, "B: the Gallery's scrollbar thumb, skinned before it was added, has B's radius",
                    thumb == null ? "none" : Px(thumb.style.borderTopLeftRadius).ToString());
                var seg = w.Root.Q("mode").Query<Button>().ToList()[1];
                Check(Near(seg.style.backgroundColor.value, b[Role.Danger]), "B: a Choice button's background is B's Danger", seg.style.backgroundColor.value.ToString());
                Check(Px(w.Root.style.borderTopLeftRadius) == 6f && w.Root.style.borderLeftWidth.value == 2f, "B: the window card has radius 6 and border width 2",
                    Px(w.Root.style.borderTopLeftRadius) + " / " + w.Root.style.borderLeftWidth.value);
                ClickEl(track);
                var knob = track.hierarchy[0];
                Check(Px(knob.style.left) == 50f - 20f - 2f - 2f && Px(knob.style.width) == 20f, "B: switched on, the knob (20 wide) sits at 50 - 20 - 2 - Line(2)",
                    Px(knob.style.width) + " at " + Px(knob.style.left));

                // The default Gallery beside it keeps Facet's own sizes.
                var df = Root.Q("flag"); var dt = Root.Q("flag-input");
                Check(Px(df.style.minHeight) == 32f && df.Q<Label>().style.fontSize.value.value == 15f && Px(dt.style.width) == 36f
                      && Px(dt.style.borderTopLeftRadius) == 9f, "the default Gallery keeps 32, 15, 36 and radius 9",
                    Px(df.style.minHeight) + " " + df.Q<Label>().style.fontSize.value.value + " " + Px(dt.style.width) + " r" + Px(dt.style.borderTopLeftRadius));

                var logo = new VisualElement();
                w.TitleBarLeft.Insert(0, logo);
                Check(w.TitleBarLeft.IndexOf(logo) == 0 && w.TitleBarLeft.Q<Label>()?.text == "Facet Gallery" && w.TitleBarLeft.parent == w.Root.hierarchy[0],
                    "Window.TitleBarLeft is the title bar's title block, and a logo goes in before the title");
                var bc = Ui.Caps("Tint", flag);
                Check(bc.style.fontSize.value.value == 14f && bc.style.letterSpacing.value.value == 3f, "B: Ui.Caps is 14 with letter spacing 3",
                    bc.style.fontSize.value.value + " / " + bc.style.letterSpacing.value.value);

                // Review Focus 4: translated first, upper-cased after, and still so through a relabel.
                var cap = Ui.Caps("Tint", df);
                try
                {
                    Check(cap.text == "TINT", "Ui.Caps(\"Tint\") reads TINT", cap.text);
                    Gallery.Lang = "ko"; Gallery.Surface.Relabel();
                    Check(cap.text == "틴트", "in Korean, after Relabel, it reads the translation unchanged", cap.text);
                    Gallery.Lang = "en"; Gallery.Surface.Relabel();
                    Check(cap.text == "TINT", "back in English it reads TINT again", cap.text);
                }
                finally { Gallery.Lang = "en"; Gallery.Surface.Relabel(); cap.RemoveFromHierarchy(); }

                // Motion is fades and slides only: picking a segment pops nothing.
                var pick = w.Root.Q("mode").Query<Button>().ToList()[2];
                Submit(pick); yield return null;
                Check(Gallery.Model.Mode == 2 && pick.style.scale.keyword == StyleKeyword.Null, "a Choice click leaves the button's scale unset (no pop)",
                    Gallery.Model.Mode + " " + pick.style.scale.keyword);
            }
            finally { s.Dispose(); Gallery.Model = new Model(); Gallery.Close(); }
        }

        // Review Focus 1: two mods tick the wheel in one frame, the first with a gated zero read.
        // Chinese, Japanese and Korean labels draw through an OS fallback face (glyph presence, not label width).
        IEnumerator SectionCjk()
        {
            try
            {
                Gallery.Lang = "en";
                Gallery.Open(); yield return null; yield return null;
                Gallery.Surface.Relabel();
                Label size = null;
                foreach (var l in Root.Query<Label>().ToList()) if (l.text == "Size") size = l;
                Check(size != null, "the Size row's label reads Size before the CJK languages");
                var drawer = new Dictionary<string, string>();
                foreach (var c in new[] { new[] { "zh", "大小" }, new[] { "ja", "サイズ" }, new[] { "ko", "\uD06C\uAE30" } })
                {
                    Gallery.Lang = c[0];
                    Gallery.Surface.Relabel();
                    yield return null; yield return null; yield return null;
                    Check(size != null && size.text == c[1], c[0] + ": the Size label reads " + c[1], size?.text);
                    var face = Fonts.Regular(Gallery.Host);
                    Check(Fonts.Has(face, c[1]), c[0] + ": the regular face draws " + c[1], face?.name);
                    var bold = Fonts.BoldFace(face);
                    Check(bold == null || Fonts.Has(bold, c[1]), c[0] + ": the bold face draws " + c[1], bold?.name);
                    drawer[c[0]] = Fonts.Drawer(face, c[1][0]).name;
                }
                /* Documented limit (Han unification, see Fonts.ForLanguage): the fallback table is shared,
                   so after ko (SectionWords) and zh the ja host's first Han character draws through the SAME face as zh's, not
                   a Japanese one. This asserts that known behaviour; per-script sets would change it. */
                Check(drawer["ja"] == drawer["zh"], "ja's Han character draws through the shared table's first CJK face ", "zh=" + drawer["zh"] + " ja=" + drawer["ja"]);
            }
            finally { Gallery.Lang = "en"; Gallery.Surface?.Relabel(); Gallery.Close(); }
        }

        IEnumerator SectionWheel()
        {
            Gallery.Open(); yield return null; yield return null;
            try
            {
            var scroll = Root.Query<ScrollView>().First();
            // The 640 px window shows all of it: pin the view short so the content is taller.
            scroll.style.flexGrow = 0f; scroll.style.height = 200f; scroll.contentContainer.style.flexShrink = 0f; yield return null; yield return null;
            float k = scroll.scrollOffset.y;
            bool tall = scroll.contentContainer.layout.height > scroll.contentViewport.layout.height + 200f;
            Check(tall, "the Gallery's content is taller than its view", scroll.contentContainer.layout.height + " vs " + scroll.contentViewport.layout.height + " svs=" + Root.Query<ScrollView>().ToList().Count);
            Facet.Input.TickWheel(Ctr(scroll), 0f);
            Facet.Input.TickWheel(Ctr(scroll), -1f);
            Facet.Input.TickWheel(Ctr(scroll), -1f);
            float d1 = scroll.scrollOffset.y - k;
            Check(Mathf.Approximately(d1, 60f), "a zero read then two ticks in one frame scroll exactly one notch", d1.ToString());
            yield return null;
            Facet.Input.TickWheel(Ctr(scroll), -1f);
            float d2 = scroll.scrollOffset.y - k - d1;
            Check(Mathf.Approximately(d2, 60f), "the next frame scrolls one more notch", d2.ToString());
            var c = Ctr(scroll);
            Gallery.Close(); yield return null; yield return null;
            // No open window (Ui.OpenWindows == 0): the tick throws nothing and the Gallery's scroll stays put.
            // (Coord.Wheel still spends the frame; the guarantee is only that nothing moves.)
            bool visible = Ui.OpenWindows > 0;
            bool threw = false;
            try { Facet.Input.TickWheel(c, -1f); } catch { threw = true; }
            Check(!visible && !threw && Mathf.Approximately(scroll.scrollOffset.y, k + 120f), "with no visible surface, a tick moves nothing and throws nothing", "visible=" + visible + " y=" + scroll.scrollOffset.y);
            }
            finally { Gallery.Close(); }
        }

        IEnumerator SectionSpeed()
        {
            Gallery.Open(); yield return null;
            Anim.Speed = 0.25f;
            try { Check(SharedSpeed() == 0.25f, "Anim.Speed reads back from the cross-version store", SharedSpeed().ToString()); }
            finally { Anim.Speed = 1f; }

            var h = Host.Create("Speed", o => o.Log = m => Main.Log("[speed] " + m));
            var surf = new Surface(h, "FacetSpeed", Layer.Window);
            var w = new Window(surf.Root, "speed", "Speed", new Rect(60, 60, 400, 300));
            new Nav(w.Body, new[] { new NavItem("Home", p => { var s = Controls.Section(p, "Fold speed", false); Controls.Info(s, "In", () => "v"); }) });
            yield return null; yield return null;
            var head = w.Body.Query<Label>().ToList().Find(l => l.text == "Fold speed")?.parent;
            Check(head != null, "the folded section has a head row");
            if (head != null)
            {
                VisualElement body = head.parent[head.parent.IndexOf(head) + 1];
                try
                {
                    Anim.Speed = 0f;
                    ClickEl(head);
                    Check(body.style.display == DisplayStyle.Flex && body.style.opacity.value == 1f, "at speed 0, opening a section ends displayed at opacity 1 in the same call", body.style.opacity.value.ToString());
                    ClickEl(head);   // fold again
                    Anim.Speed = 2f;
                    ClickEl(head); yield return null;
                    Check(body.style.opacity.value < 1f, "at speed 2, the body is still fading in a frame after opening", body.style.opacity.value.ToString());
                }
                finally { Anim.Speed = 1f; }
            }
            surf.Dispose();

            // Hover fades on a hooked Look.Button.
            var b = Ui.Box(Root);
            Paint.Look(b, Look.Button);
            yield return null;
            try
            {
                Mouse<MouseEnterEvent>(b);
                var props = b.style.transitionProperty.value;
                Check(FadeMs(b) == 60f, "a hovered Look.Button fades over one 60 ms value", FadeMs(b).ToString());
                Check(props != null && props.Contains(new StylePropertyName("background-color")), "...including background-color");
                Anim.Speed = 0.5f; Mouse<MouseLeaveEvent>(b); Mouse<MouseEnterEvent>(b);
                Check(FadeMs(b) == 30f, "at speed 0.5, the next hover fades over 30 ms", FadeMs(b).ToString());
                Anim.Speed = 0f; Mouse<MouseLeaveEvent>(b); Mouse<MouseEnterEvent>(b);
                Check(FadeMs(b) == 0f, "at speed 0, the next hover fades over 0 ms", FadeMs(b).ToString());
                Anim.Speed = 1f; Mouse<MouseLeaveEvent>(b); Mouse<MouseEnterEvent>(b);

                // Ruling Q6: the flash back to 60 ms runs on a hooked row (an unhooked one asserts nothing).
                var flash = typeof(Paint).GetMethod("Flash", BindingFlags.NonPublic | BindingFlags.Static);
                flash.Invoke(null, new object[] { b, Role.Accent, 100 });
                Check(FadeMs(b) == 0f, "a running Flash switches the row's fade off", FadeMs(b).ToString());
            }
            finally { Anim.Speed = 1f; }
            yield return new WaitForSeconds(0.4f);
            Check(FadeMs(b) == 60f, "...and back to 60 ms once it ends", FadeMs(b).ToString());

            // A hover mid-flash must not turn the fade back on and smear it.
            var flash2 = typeof(Paint).GetMethod("Flash", BindingFlags.NonPublic | BindingFlags.Static);
            flash2.Invoke(null, new object[] { b, Role.Accent, 300 });
            Mouse<MouseLeaveEvent>(b); Mouse<MouseEnterEvent>(b);
            Check(FadeMs(b) == 0f, "a hover during a Flash leaves the fade off", FadeMs(b).ToString());
            yield return new WaitForSeconds(0.5f);
            Check(FadeMs(b) == 60f, "...and the fade is back after it", FadeMs(b).ToString());

            // Focus follows the speed too: keyboard-only users never hover.
            try
            {
                Anim.Speed = 0f;
                b.focusable = true; b.Focus();
                Check(FadeMs(b) == 0f, "a focus-in at speed 0 sets a 0 ms fade", FadeMs(b).ToString());
            }
            finally { Anim.Speed = 1f; }
            b.RemoveFromHierarchy();
        }
    }
}
