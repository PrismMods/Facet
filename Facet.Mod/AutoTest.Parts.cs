using System.Linq;
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

        // Task 9: the page header's switch and the two-column page with its sticky right pane.
        IEnumerator SectionHeaderColumns()
        {
            GalleryParts.Model = new PartsModel();
            var win = GalleryParts.Open(); yield return null; yield return null;
            try
            {
                var m = GalleryParts.Model; var sz = Gallery.Host.Theme.Sizes;
                GalleryParts.Nav.Go(GalleryParts.FeatureItem); yield return null; yield return null; yield return null;
                var ms = new double[1];
                var head = win.Body.Q("head");
                var title = head?.Query<Label>().ToList().Find(l => l.text == "Judgement counter");
                Check(title != null && title.style.fontSize.value.value == sz.Title && title.style.unityFontStyleAndWeight.value == FontStyle.Bold,
                    "the header title is Sizes.Title and bold", title?.style.fontSize.value.value.ToString());
                var desc = head?.Query<Label>().ToList().Find(l => l.text == "Counts each judgement");
                Check(desc != null && desc.style.fontSize.value.value == sz.Small && Near(desc.style.color.value, Gallery.Host.Theme[Role.TextMuted]),
                    "the description is Sizes.Small and muted");
                History.Clear();
                var sw = win.Body.Q("head-input");
                Check(sw != null, "the header has its switch, head-input");
                if (sw != null) ClickEl(sw);
                yield return null;
                Check(m.Feature, "clicking head-input flips the model");
                History.Undo(); yield return null;
                Check(!m.Feature, "History.Undo puts the header switch back");
                var off = sw.style.backgroundColor.value;
                m.Feature = true;
                yield return Until(() => !Near(sw.style.backgroundColor.value, off), ms, 400);
                Check(ms[0] >= 0 && ms[0] <= 150, "a flip behind the UI shows within 150 ms", Ms(ms));
                m.Feature = false;

                var head2 = win.Body.Q("head2");
                Check(head2 != null && head2.Q("head2-input") == null, "a header with no binding has no switch");
                Func<Label> d2 = () => win.Body.Q("head2")?.Query<Label>().ToList().Find(l => l.text == "one" || l.text == "two");
                Check(d2()?.text == "one", "DescriptionText shows as returned", d2()?.text);
                m.Desc = "two";
                yield return Until(() => d2()?.text == "two", ms);
                Check(ms[0] >= 0 && ms[0] <= 150, "a changed DescriptionText shows within 150 ms", Ms(ms));
                GalleryParts.Nav.Filter("judgement counter"); yield return null; yield return null;
                Func<string, Label> find = t => win.Body.Query<Label>().ToList().Find(l => l.text == t);
                var hitL = find("Judgement counter");
                var whereL = hitL?.parent?.Query<Label>().ToList().Find(l => l.text.StartsWith("Feature"));
                Check(hitL != null && whereL != null, "Filter(judgement counter) lists a result row for the title with a Feature breadcrumb", whereL?.text);
                Check(win.Body.Query<Label>().ToList().Exists(l => l.text.StartsWith("Results for")), "...under the results heading");
                if (hitL != null) ClickEl(hitL.parent);
                yield return null; yield return null; yield return null;
                var hd = win.Body.Q("head");
                var hsv = hd?.GetFirstAncestorOfType<ScrollView>();
                Check(hd != null && hsv != null && hd.worldBound.yMin >= hsv.contentViewport.worldBound.yMin - 1f && hd.worldBound.yMax <= hsv.contentViewport.worldBound.yMax + 1f,
                    "clicking the hit brings the header back into view", hd?.worldBound.ToString());
                GalleryParts.Nav.Go(GalleryParts.FeatureItem); yield return null; yield return null;

                var left = win.Body.Q("feat-left"); var right = win.Body.Q("feat-right"); var box = win.Body.Q("feat-box");
                Check(left != null && right != null && left.contentRect.width > 0f && Mathf.Abs(left.contentRect.width / right.contentRect.width - 1.25f) < 0.0625f,
                    "left / right width = 1.25 +- 5%", left == null ? "none" : left.contentRect.width + " / " + right.contentRect.width);
                var sv = left?.GetFirstAncestorOfType<ScrollView>();
                if (sv != null && box != null)
                {
                    sv.scrollOffset = new Vector2(0f, 300f);
                    yield return Until(() => Mathf.Abs(right.worldBound.yMin - sv.contentViewport.worldBound.yMin) < 2f, ms);
                    Check(ms[0] >= 0, "scrolled down 300, the right pane sits at the viewport top", right.worldBound.yMin + " vs " + sv.contentViewport.worldBound.yMin);
                    sv.scrollOffset = new Vector2(0f, 100000f);
                    yield return null; yield return null; yield return null;
                    Check(box.worldBound.yMax <= left.parent.worldBound.yMax + 1f, "scrolled to the end, the box stays inside the columns", box.worldBound.yMax + " vs " + left.parent.worldBound.yMax);
                    sv.scrollOffset = Vector2.zero;
                    yield return Until(() => right.resolvedStyle.translate.y == 0f, ms);
                    Check(ms[0] >= 0, "scrolled back to 0, the translate is 0", right.resolvedStyle.translate.y.ToString());

                    // Narrow window: below the threshold the right column stacks under the left.
                    var cols = left.parent;
                    cols.style.width = 300f;
                    yield return Until(() => right.worldBound.yMin >= left.worldBound.yMax - 1f, ms);
                    Check(ms[0] >= 0 && Mathf.Abs(right.layout.width - left.layout.width) < 2f && right.layout.width > 250f,
                        "a narrow page stacks the right column under the left, each full width", right.layout.width + " / " + left.layout.width);
                    cols.style.width = StyleKeyword.Null;
                    yield return Until(() => right.worldBound.yMin < left.worldBound.yMax - 1f, ms);
                    Check(ms[0] >= 0, "widened again, the columns sit side by side");
                }
                else Check(false, "the Feature page has a ScrollView and a right box");

                // Review I1: a pane taller than the viewport scrolls with the page instead of pinning its bottom off screen.
                if (sv != null && box != null)
                {
                    box.style.height = 2000f;
                    yield return null; yield return null;
                    sv.scrollOffset = new Vector2(0f, 300f);
                    yield return null; yield return null; yield return null;
                    Check(right.resolvedStyle.translate.y == 0f, "a pane taller than the viewport is not translated", right.resolvedStyle.translate.y.ToString());
                    sv.scrollOffset = new Vector2(0f, 100000f);
                    yield return null; yield return null; yield return null;
                    Check(box.worldBound.yMax <= sv.contentViewport.worldBound.yMax + 1f, "...and its bottom reaches the viewport at the end", box.worldBound.yMax + " vs " + sv.contentViewport.worldBound.yMax);
                    box.style.height = 200f;
                }

                // Review I3: in a parent that shrink-wraps, the mode must not flip between frames.
                var wrap = Ui.Box(win.Body);
                wrap.style.alignItems = Align.FlexStart;
                var wl = Controls.Columns(wrap, out var wr);
                Controls.Info(wl, "In", () => "left"); Controls.Info(wr, "In", () => "right");
                var seen = new List<bool>();
                for (int i = 0; i < 5; i++) { yield return null; if (i >= 2) seen.Add(wr.worldBound.yMin >= wl.worldBound.yMax - 1f); }
                Check(seen.TrueForAll(x => x == seen[0]), "Columns in a shrink-wrapping parent holds one mode across frames", string.Join(",", seen));
                wrap.RemoveFromHierarchy();
            }
            finally { GalleryParts.Close(); GalleryParts.Model = new PartsModel(); }
        }

        // Task 11: the tile grid: five across, switch vs body clicks, a coloured judgement tile, refresh, undo, search.
        IEnumerator SectionTiles()
        {
            GalleryParts.Model = new PartsModel();
            var win = GalleryParts.Open(); yield return null; yield return null;
            try
            {
                var m = GalleryParts.Model; var th = Gallery.Host.Theme;
                GalleryParts.Nav.Go(GalleryParts.TilesItem); yield return null; yield return null; yield return null;
                Func<int, VisualElement> tile = k => win.Body.Q("tile" + k);
                Check(tile(0) != null && tile(6) != null, "the Tiles page has its seven tiles");
                if (tile(0) == null || tile(6) == null) yield break;
                float w0 = tile(0).worldBound.width; bool eq = true, line = true;
                for (int k = 1; k < 5; k++)
                {
                    eq &= Mathf.Abs(tile(k).worldBound.width - w0) <= 1f;
                    line &= Mathf.Abs(tile(k).worldBound.yMin - tile(0).worldBound.yMin) <= 1f;
                }
                Check(eq && line && w0 > 20f, "the first five tiles are equal width +-1 on one line", w0 + "");
                Check(tile(5).worldBound.yMin > tile(0).worldBound.yMax - 1f, "the 6th wraps", tile(5).worldBound.ToString());

                m.Opens = 0; History.Clear();
                var sw = win.Body.Q("tile0-input");
                Check(sw != null, "a feature tile has its switch, tile0-input");
                if (sw != null) ClickEl(sw);
                yield return null;
                Check(m.Tile[0] && m.Opens == 0, "a click on the switch flips the model and does not run Open", m.Opens.ToString());
                History.Undo(); yield return null;
                Check(!m.Tile[0], "History.Undo restores a click");
                var lab0 = tile(0).Query<Label>().ToList().Find(l => l.text == "Judgement counter");
                if (lab0 != null) ClickEl(lab0);
                yield return null;
                Check(!m.Tile[0] && m.Opens == 1, "a click on the label runs Open once and does not flip the model", m.Opens.ToString());
                var ms = new double[1];
                var sel = Gallery.Host.Theme.Looks.Tile.SelectedBg;
                var selBg = sel.HasValue ? th[sel.Value] : th[Role.Row];
                var lab3 = tile(3).Query<Label>().ToList()[0];
                m.Tile[3] = true;
                yield return Until(() => Near(win.Body.Q("tile3-input").style.backgroundColor.value, th[Role.Accent]), ms, 400);
                Check(ms[0] >= 0 && ms[0] <= 150, "a flip behind the UI shows within 150 ms", Ms(ms));
                Check(Near(tile(3).style.backgroundColor.value, selBg) && Near(lab3.resolvedStyle.color, th[Role.Text]), "...as the selected look and Text on the label");
                m.Tile[3] = false;
                yield return Until(() => Near(lab3.resolvedStyle.color, th[Role.TextMuted]), ms, 400);
                Check(ms[0] >= 0, "...and flipping back restores TextMuted");

                // Keyboard: one tab stop, Submit does what a click does.
                Check(!win.Body.Q("tile0-input").focusable && tile(0).focusable, "the tile is the one tab stop, its switch is not focusable");
                m.Opens = 0; using (var e0 = NavigationSubmitEvent.GetPooled()) { e0.target = tile(2); tile(2).SendEvent(e0); }
                yield return null;
                Check(!m.Tile[2] && m.Opens == 1, "Submit on a tile body runs Open, not the toggle", m.Opens.ToString());
                var jj = win.Body.Q("judge");
                using (var e1 = NavigationSubmitEvent.GetPooled()) { e1.target = jj; jj.SendEvent(e1); }
                yield return null;
                Check(m.Judge, "Submit on a Switch=false tile flips the model");
                using (var e1 = NavigationSubmitEvent.GetPooled()) { e1.target = jj; jj.SendEvent(e1); }
                yield return null;
                Check(!m.Judge, "...and again flips it back");

                var j = win.Body.Q("judge");
                var red = new Color(1f, 0f, 0f);
                Check(j != null && j.Q("judge-input") == null, "a Switch=false tile has no switch");
                if (j == null) yield break;
                var edge = j.Children().First(c => c.pickingMode == PickingMode.Ignore && c.style.position == Position.Absolute && c.style.width.value.value > 0f);
                var jl = j.Query<Label>().ToList()[0];
                Check(Near(edge.style.backgroundColor.value, red) && Mathf.Abs(edge.style.width.value.value - Gallery.Host.Theme.Sizes.Indicator) < 0.01f, "the edge is the colour, Sizes.Indicator wide");
                Check(Near(jl.resolvedStyle.color, th[Role.TextMuted]), "off: the label is TextMuted");
                VisualElement tint = j.Children().First(c => c.style.position == Position.Absolute && c != edge);
                Check(tint.resolvedStyle.display == DisplayStyle.None, "off: no tint");
                ClickEl(j); yield return null; yield return null;
                Check(m.Judge, "a click anywhere on a Switch=false tile flips the model");
                var tc = tint.style.backgroundColor.value;
                Check(tint.resolvedStyle.display == DisplayStyle.Flex && Near(new Color(tc.r, tc.g, tc.b, 1f), red) && Mathf.Abs(tc.a - 0.18f) < 0.01f, "on: red at alpha .18", tc.ToString());
                Check(Near(edge.style.backgroundColor.value, red), "on: the edge stays red");
                Check(Near(jl.resolvedStyle.color, th[Role.Text]), "on: the label is Text", jl.resolvedStyle.color.ToString());
                // Re-theme: the literal colour survives.
                Paint.Apply(win.Body); yield return null;
                Check(Near(edge.style.backgroundColor.value, red) && Mathf.Abs(tint.style.backgroundColor.value.a - 0.18f) < 0.01f, "...and survives a re-paint");
                ClickEl(j); yield return null; yield return null;
                Check(!m.Judge && tint.resolvedStyle.display == DisplayStyle.None && Near(j.style.backgroundColor.value, th[Role.Row]) && Near(jl.resolvedStyle.color, th[Role.TextMuted]),
                    "off again: the tile is back to the look's background");

                m.Judge = true;
                yield return Until(() => tint.resolvedStyle.display == DisplayStyle.Flex, ms, 400);
                Check(ms[0] >= 0 && ms[0] <= 150 && Near(edge.style.backgroundColor.value, red) && Near(jl.resolvedStyle.color, th[Role.Text]), "an external flip of the judgement tile shows the tint, red edge and Text label", Ms(ms));
                // M3: the tint never takes the pointer.
                var pc = j.worldBound.center;
                var picked = j.panel.Pick(pc);
                Check(picked != tint && picked != edge, "a pick at the tile's centre does not return the tint", picked?.name ?? picked?.GetType().Name);
                m.Judge = false;
                yield return Until(() => tint.resolvedStyle.display == DisplayStyle.None, ms, 400);

                // Long labels keep one height and the switch inside.
                for (int k = 1; k <= 2; k++)
                {
                    var t = tile(k); var s = win.Body.Q("tile" + k + "-input");
                    Check(Mathf.Abs(t.worldBound.height - tile(0).worldBound.height) <= 1f && s.worldBound.xMax <= t.worldBound.xMax + 0.5f && s.worldBound.xMin >= t.worldBound.xMin,
                        "a " + (k == 1 ? "200-character" : "80-Hangul") + " label keeps the tile's height and the switch inside", t.worldBound + " " + s.worldBound);
                }

                m.HideTiles = true;
                yield return Until(() => tile(0).resolvedStyle.display == DisplayStyle.None && tile(2).resolvedStyle.display == DisplayStyle.None, ms);
                yield return null; yield return null;
                float y1 = tile(3).worldBound.yMin; bool one = true;
                for (int k = 4; k < 6; k++) one &= Mathf.Abs(tile(k).worldBound.yMin - y1) <= 1f;
                Check(one && tile(5).worldBound.yMin <= tile(3).worldBound.yMin + 1f, "hiding the first row's tiles pulls the 6th tile up to the first line", tile(5).worldBound + " vs " + tile(3).worldBound);
                m.HideTiles = false;

                GalleryParts.Nav.Filter("Feature 3"); yield return null; yield return null;
                var l3 = win.Body.Query<Label>().ToList().Find(l => l.text == "Feature 3");
                Check(l3 != null && !win.Body.Query<Label>().ToList().Exists(l => l.text.StartsWith("Nothing matches")), "Filter finds a tile by its label", l3?.text);
                GalleryParts.Nav.Filter("counts each"); yield return null; yield return null;
                Check(win.Body.Query<Label>().ToList().Exists(l => l.text == "Judgement counter") && !win.Body.Query<Label>().ToList().Exists(l => l.text.StartsWith("Nothing matches")), "...and by its tooltip");
                GalleryParts.Nav.Filter(""); yield return null;
            }
            finally { GalleryParts.Close(); GalleryParts.Model = new PartsModel(); }
        }

        // Task 10: the preview pane shows only while its page and surface do; consumer throws stay inside.
        IEnumerator SectionPreview()
        {
            GalleryParts.Model = new PartsModel();
            var win = GalleryParts.Open(); yield return null; yield return null;
            try
            {
                var m = GalleryParts.Model;
                GalleryParts.Nav.Go(GalleryParts.FeatureItem); yield return null; yield return null; yield return null;
                Check(m.Shows == 1, "opening the Feature page calls Show once", m.Shows.ToString());
                int t0 = m.Ticks;
                for (int i = 0; i < 30; i++) yield return null;
                Check(m.Ticks - t0 >= 20, "Tick runs at least 20 times in 30 frames", (m.Ticks - t0).ToString());
                var stage = win.Body.Q("feat-preview");
                Check(stage != null && Mathf.Abs(stage.layout.height - stage.layout.width * 9f / 16f) <= 1f && stage.layout.width > 50f,
                    "the stage height is width / Aspect", stage == null ? "none" : stage.layout.width + "x" + stage.layout.height);
                var link = win.Body.Q("feat-preview-link");
                Check(link != null, "the preview has its Link button");
                if (link != null) Submit((Button)link);
                yield return null;
                Check(m.Links == 1, "clicking the Link runs OnLink once", m.Links.ToString());

                GalleryParts.Nav.Go(GalleryParts.CardsItem); yield return null; yield return null;
                Check(m.Hides == 1, "going to another page calls Hide once", m.Hides.ToString());
                t0 = m.Ticks;
                for (int i = 0; i < 30; i++) yield return null;
                Main.Log("[perf] preview hidden: " + (m.Ticks - t0) + " ticks over 30 frames");
                Check(m.Ticks == t0, "a hidden page's preview ticks 0 times over 30 frames", (m.Ticks - t0).ToString());
                GalleryParts.Nav.Go(GalleryParts.FeatureItem); yield return null; yield return null; yield return null;
                Check(m.Shows == 2, "coming back calls Show again", m.Shows.ToString());

                GalleryParts.Surface.Visible = false; yield return null;
                Check(m.Hides == 2, "hiding the surface calls Hide", m.Hides.ToString());
                t0 = m.Ticks;
                for (int i = 0; i < 10; i++) yield return null;
                Check(m.Ticks == t0, "a hidden surface's preview does not tick", (m.Ticks - t0).ToString());
                GalleryParts.Surface.Visible = true; yield return null; yield return null;
                Check(m.Shows == 3, "showing the surface calls Show", m.Shows.ToString());
            }
            finally { GalleryParts.Close(); GalleryParts.Model = new PartsModel(); }

            // Fix 1: Facet's tick path allocates nothing per run (no-op Tick; the bytes measured include the scheduler, so a small tolerance).
            var ah = Host.Create("PrevAlloc", o => o.Log = l => Main.Log("[prevalloc] " + l));
            var asf = new Surface(ah, "FacetPrevAlloc", Layer.Window);
            int aticks = 0;
            try
            {
                var aw = new Window(asf.Root, "prevalloc", "Alloc", new Rect(60, 60, 300, 300));
                Controls.Preview(aw.Body, "A", o => o.Tick = _ => aticks++);
                for (int i = 0; i < 20; i++) yield return null;
                int n0 = aticks;
                // Logged, not asserted: the frame loop, the scheduler and Unity itself allocate in the same window, so 0 is not a stable bound.
                var per = new List<long>();
                for (int i = 0; i < 30; i++) { long a = GC.GetAllocatedBytesForCurrentThread(); yield return null; per.Add(GC.GetAllocatedBytesForCurrentThread() - a); }
                long total = 0; foreach (var x in per) total += x;
                Main.Log("[perf] preview tick allocation: " + total + " bytes over 30 frames, " + (aticks - n0) + " ticks");
                Check(aticks - n0 >= 20, "the allocation probe's Tick ran", (aticks - n0).ToString());
            }
            finally { asf.Dispose(); }

            // Review Focus 5: a consumer that throws, and a surface disposed while showing.
            var lines = new List<string>();
            int shows = 0, hides = 0, throws = 0, linkThrows = 0;
            var host = Host.Create("PrevThrow", o => o.Log = l => lines.Add(l));
            var s = new Surface(host, "FacetPrevThrow", Layer.Window);
            VisualElement st2 = null;
            try
            {
                var w = new Window(s.Root, "prevthrow", "Throw", new Rect(60, 60, 400, 400));
                st2 = Controls.Preview(w.Body, "Throws", o =>
                {
                    o.Id = "pt"; o.Aspect = 4f; o.Link = "Go"; o.OnLink = () => { linkThrows++; throw new Exception("link"); };
                    o.Show = _ => { shows++; throw new Exception("show"); };
                    o.Tick = _ => { throws++; throw new Exception("tick"); };
                    o.Hide = () => { hides++; throw new Exception("hide"); };
                });
                for (int i = 0; i < 30; i++) yield return null;
                Check(shows == 1 && throws == 3, "a Tick that always throws stops after 3 throws", shows + " show, " + throws + " ticks");
                Check(lines.FindAll(l => l.Contains("Tick")).Count == 1, "...and logs one line for it", string.Join(" | ", lines));
                Submit(w.Body.Q<Button>("pt-link")); yield return null;
                Check(linkThrows == 1, "a throwing OnLink is contained");
                s.Visible = false; yield return null;
                Check(hides == 1, "leaving runs Hide once although Tick threw", hides.ToString());
                s.Visible = true; yield return null; for (int i = 0; i < 10; i++) yield return null;
                Check(shows == 2 && throws == 6, "reopening calls Show again and ticking resumes", shows + " show, " + throws + " ticks");
            }
            finally { }
            bool escaped = false;
            try { s.Dispose(); } catch { escaped = true; }
            yield return null; yield return null;
            Check(!escaped && hides == 2 && throws == 6, "disposing the surface while shown calls Hide once, raises nothing, ticks no more", hides + " hides, " + throws + " ticks");
        }

        // Task 8: a card of rows split by rules, a help line, the Advanced fold, and forms built into cards.
        IEnumerator SectionCards()
        {
            GalleryParts.Model = new PartsModel();
            var win = GalleryParts.Open(); yield return null; yield return null; yield return null;
            try
            {
                Check(ReferenceEquals(GalleryParts.Open(), win), "GalleryParts.Open is idempotent");
                var m = GalleryParts.Model;
                var th = Gallery.Host.Theme; var sz = th.Sizes;
                Func<string, Label> lab = t => win.Body.Query<Label>().ToList().Find(l => l.text == t);
                var card = win.Body.Q("card-look");
                Check(card != null && Near(card.style.backgroundColor.value, th[Role.Panel]) && Near(card.style.borderTopColor.value, th[Role.PanelBorder]),
                    "the Look card is painted Looks.Group (Card's look under Dark)", card?.style.backgroundColor.value.ToString());
                Check(card != null && Px(card.style.borderTopLeftRadius) == sz.Radius && card.style.borderLeftWidth.value == sz.Line,
                    "...with Sizes.Radius corners and a Sizes.Line border", card == null ? "none" : Px(card.style.borderTopLeftRadius) + " / " + card.style.borderLeftWidth.value);
                Check(lab("LOOK") != null, "the card header reads LOOK");

                var all = card?.Query<Button>().ToList().Find(x => x.text == "All");
                var none = card?.Query<Button>().ToList().Find(x => x.text == "None");
                Check(all != null && none != null && lab("·") != null, "the header holds All · None");
                Check(all != null && Near(all.style.color.value, th[Role.Accent]), "...painted Looks.Link (Accent text)", all?.style.color.value.ToString());
                Check(lab("0 / 3") != null, "the note reads 0 / 3");
                if (all != null) Submit(all);
                Check(m.AllRuns == 1 && m.On == 3, "clicking All runs its action once", m.AllRuns.ToString());
                var ms = new double[1];
                yield return Until(() => lab("3 / 3") != null, ms);
                Check(ms[0] >= 0 && ms[0] <= 150, "the note shows 3 / 3 within 150 ms", Ms(ms));

                var r1 = win.Body.Q("look-glow"); var r2 = win.Body.Q("look-size"); var r3 = win.Body.Q("look-style");
                Check(r1 != null && r1.resolvedStyle.borderTopWidth == 0f, "row 1 has no top border", r1?.resolvedStyle.borderTopWidth.ToString());
                Check(r2 != null && r3 != null && r2.style.borderTopWidth.value == sz.Line && r3.style.borderTopWidth.value == sz.Line
                      && Near(r2.style.borderTopColor.value, th[Role.Line]), "rows 2 and 3 have a Sizes.Line top border in Role.Line");
                Check(r1 != null && Px(r1.style.paddingLeft) == sz.Pad && Px(r1.style.paddingRight) == sz.Pad, "a card row pads Sizes.Pad on both sides");
                var help = r1?.Q<Label>("help");
                Check(help != null && help.text == "Adds a soft halo" && help.style.fontSize.value.value == sz.Small && Near(help.style.color.value, th[Role.TextMuted]),
                    "the help label is Sizes.Small in TextMuted", help == null ? "none" : help.text + " " + help.style.fontSize.value.value);
                var gl = lab("Glow");
                Check(help != null && gl != null && help.worldBound.yMin >= gl.worldBound.yMax - 0.5f && help.worldBound.yMax <= r1.worldBound.yMax + 0.5f,
                    "the help line sits under the label and the row grows to hold it", help == null || gl == null ? "" : gl.worldBound + " " + help.worldBound + " in " + r1.worldBound);

                Func<Label> head = () => win.Body.Query<Label>().ToList().Find(l => l.text.Contains("Advanced ("));
                var fh = head();
                var extra = win.Body.Q("look-extra");
                Check(fh != null && fh.text == "▸ Advanced (2)", "the fold head reads ▸ Advanced (2)", fh?.text);
                Check(extra != null && extra.parent.resolvedStyle.display == DisplayStyle.None, "the fold body is hidden");
                if (fh != null) ClickEl(fh);
                yield return null;
                string fold; Gallery.Store.TryGetValue("fold.Cards/Look/Advanced", out fold);
                Check(extra != null && extra.parent.resolvedStyle.display == DisplayStyle.Flex && fh?.text == "▾ Advanced (2)" && fold == "1",
                    "a click shows the body, the head reads ▾ and the host holds fold.Cards/Look/Advanced=1", fh?.text + " " + fold);
                m.HideDepth = true;
                yield return Until(() => fh?.text == "▾ Advanced (1)", ms);
                Check(ms[0] >= 0 && ms[0] <= 150, "hiding Depth through VisibleWhen reads (1) within 150 ms", Ms(ms) + " " + fh?.text);
                m.HideDepth = false;
                yield return Until(() => fh?.text == "▾ Advanced (2)", ms);
                try
                {
                    Gallery.Lang = "ko"; GalleryParts.Surface.Relabel();
                    Check(fh?.text == "▾ 고급 (2)", "in Korean the head reads Facet's own 고급", fh?.text);
                }
                finally { Gallery.Lang = "en"; GalleryParts.Surface.Relabel(); }
                if (fh != null) ClickEl(fh);   // fold again for the search reveal
                yield return null;

                var nav = GalleryParts.Nav;
                nav.Filter("depth"); yield return null; yield return null;
                var hit = lab("Depth");
                Check(hit != null, "Filter(depth) lists the advanced Depth row");
                if (hit != null) ClickEl(hit);
                Func<bool> revealed = () =>
                {
                    var r = win.Body.Q("look-depth"); if (r == null) return false;
                    var sv = r.GetFirstAncestorOfType<ScrollView>(); if (sv == null) return false;
                    var vp = sv.contentViewport.worldBound;
                    return r.worldBound.yMin >= vp.yMin - 1f && r.worldBound.yMax <= vp.yMax + 1f && r.worldBound.height > 0f;
                };
                yield return Until(revealed, ms, 2000);
                Check(ms[0] >= 0 && head()?.text == "▾ Advanced (2)", "clicking the hit unfolds the fold and puts Depth inside the viewport", head()?.text);
                nav.Filter("halo"); yield return null;
                Check(lab("Glow") != null, "a word only in a row's help finds the row");
                nav.Filter(""); yield return null; yield return null;

                // Review I2: two cards share the label Size; the second card's hit lands on the second card's row.
                nav.Filter("size"); yield return null; yield return null;
                var where2 = lab("Cards · Other");
                Check(where2 != null, "Filter(size) lists the second card's Size hit");
                if (where2 != null) ClickEl(where2.parent);
                Func<bool> revealed2 = () =>
                {
                    var r = win.Body.Q("other-size"); if (r == null) return false;
                    var sv = r.GetFirstAncestorOfType<ScrollView>(); if (sv == null) return false;
                    var vp = sv.contentViewport.worldBound;
                    return r.worldBound.yMin >= vp.yMin - 1f && r.worldBound.yMax <= vp.yMax + 1f && r.worldBound.height > 0f;
                };
                yield return Until(revealed2, ms, 2000);
                Check(ms[0] >= 0, "choosing the second card's hit scrolls to the second card's Size row");
                nav.Filter(""); yield return null; yield return null;

                m.HideCard = true;
                yield return Until(() => win.Body.Q("card-look")?.style.display == DisplayStyle.None, ms);
                Check(ms[0] >= 0 && ms[0] <= 150, "a card whose VisibleWhen turns false hides within 150 ms", Ms(ms));
                m.HideCard = false;
            }
            finally { Gallery.Lang = "en"; GalleryParts.Close(); GalleryParts.Model = new PartsModel(); }

            // Ruling Q16: a card body wears Looks.Group, its own fill, not the window shell's Looks.Card.
            var gt = new Theme();
            gt.Looks.Group = new Look { Bg = Role.RowAlt, Border = Role.Line };
            var gs = new Surface(Host.Create("CardGroup", o => { o.Log = l => Main.Log("[cardgroup] " + l); o.Theme = gt; }), "FacetCardGroup", Layer.Window);
            try
            {
                var gw = new Window(gs.Root, "cardgroup", "Group", new Rect(60, 60, 300, 200));
                var gb = Controls.Card(gw.Body, "Own fill", o => o.Id = "own-fill");
                yield return null;
                var gc = gw.Body.Q("own-fill");
                Check(gc != null && Near(gc.style.backgroundColor.value, gt[Role.RowAlt]) && !Near(gw.Root.style.backgroundColor.value, gt[Role.RowAlt]),
                    "a theme's Looks.Group fills the card and not the window", gc?.style.backgroundColor.value.ToString());
            }
            finally { gs.Dispose(); }

            // Form.Build with Cards: groups A, A, B; A has an Advanced field and a Help field.
            var fs = new Surface(Gallery.Host, "FacetFormCards", Layer.Window);
            try
            {
                var fw = new Window(fs.Root, "formcards", "Form cards", new Rect(60, 60, 500, 500));
                var fp = Ui.Box(fw.Body);
                var owner = Host.Create("CardOwner", o => o.Log = l => Main.Log("[cardowner] " + l));
                var fields = new[] {
                    new Field { Group = "A", Label = "fadv", Kind = FieldKind.Bool, Advanced = true, Get = () => true, Set = v => { } },
                    new Field { Group = "A", Label = "fhelp", Kind = FieldKind.Bool, Help = "Says hello", Get = () => false, Set = v => { } },
                    new Field { Group = "B", Label = "fb", Kind = FieldKind.Bool, Get = () => false, Set = v => { } },
                    new Field { Group = "B", Label = "fown", Kind = FieldKind.Bool, Owner = "CardOwner", Get = () => false, Set = v => { } },
                };
                bool threw = false;
                try { Form.Build(fp, fields, o => o.Cards = true); } catch (Exception e) { threw = true; Main.Log("form cards threw " + e); }
                yield return null; yield return null; yield return null;
                var labels = fp.Query<Label>().ToList();
                Check(!threw && fp.childCount == 2 && labels.Exists(l => l.text == "A") && labels.Exists(l => l.text == "B"),
                    "Form.Build with Cards over groups A, A, B builds two cards", "children=" + fp.childCount);
                var adv = fp.Q("fadv"); var hp = fp.Q("fhelp");
                var ah = adv == null ? null : adv.parent.parent[adv.parent.parent.IndexOf(adv.parent) - 1].Q<Label>();
                Check(adv != null && fp[0].Contains(adv) && ah != null && ah.text == "▸ Advanced (1)" && adv.parent.resolvedStyle.display == DisplayStyle.None,
                    "the Advanced field is inside A's folded Advanced fold", ah?.text);
                Check(hp != null && adv != null && hp.parent == adv.parent.parent && hp.parent.IndexOf(hp) < hp.parent.IndexOf(adv.parent),
                    "...which comes after A's other fields");
                Check(hp?.Q<Label>("help")?.text == "Says hello", "the Help field shows its help line", hp?.Q<Label>("help")?.text);

                // Review I1: the card rules reach rows wrapped for a foreign owner, in a ToggleGroup and under a nested Section.
                var fsz = Gallery.Host.Theme.Sizes;
                var fown = fp.Q("fown");
                Check(fown != null && fown.style.borderTopWidth.value == fsz.Line && Px(fown.style.paddingRight) == fsz.Pad,
                    "a foreign-owner Form field in a card has a Sizes.Line top border and card padding", fown?.style.borderTopWidth.value.ToString());
                var tc = Controls.Card(fw.Body, "Nest");
                Controls.Toggle(tc, "first", Bind.To(() => true, v => { }));
                var tg = Controls.ToggleGroup(tc, "grp", Bind.To(() => true, v => { }));
                Controls.Toggle(tg, "inner", Bind.To(() => false, v => { }), o => o.Id = "tg-inner");
                var ns = Controls.Section(tc, "Sub");
                Controls.Toggle(ns, "deep", Bind.To(() => false, v => { }), o => o.Id = "ns-deep");
                yield return null; yield return null;
                var inner = tc.Q("tg-inner");
                Check(inner != null && Px(inner.style.paddingRight) == fsz.Pad && tg.style.paddingLeft.value.value == fsz.Pad,
                    "a ToggleGroup row inside a card pads Sizes.Pad on the right and the group keeps its indent", inner == null ? "none" : Px(inner.style.paddingRight).ToString());
                var sl = tc.Query<Label>().ToList().Find(l => l.text == "Sub");
                var sh = sl?.parent;
                Check(sh != null && Px(sh.style.paddingLeft) == fsz.Pad && sl.worldBound.xMin >= tc.worldBound.xMin + fsz.Pad - 0.5f,
                    "a nested Section head inside a card is padded, not flush", sh == null ? "none" : Px(sh.style.paddingLeft).ToString());
            }
            finally { fs.Dispose(); }
        }

        // Task 12: the tab row's underline and rule, and tab pages that search reaches.
        IEnumerator SectionTabs()
        {
            var bt = new Theme();
            bt.Looks.Tab = new Look { Text = Role.TextMuted, SelectedText = Role.Text, FocusBorder = Role.Focus };
            bt.Looks.Indicator = new Look { SelectedBg = Role.Accent };
            bt.Looks.Rule = new Look { Bg = Role.Line };   // Dark draws no rule (0.1's look); a skin gives it a colour
            var bs = new Surface(Host.Create("TabRow", o => { o.Log = l => Main.Log("[tabrow] " + l); o.Theme = bt; }), "FacetTabRow", Layer.Window);
            try
            {
                var bw = new Window(bs.Root, "tabrow", "Tab row", new Rect(60, 60, 400, 200));
                var row = new Tabs(bw.Body, null);
                row.Rebuild(new[] { "A", "B", "C" });
                row.Select(1);
                yield return null; yield return null;
                var btns = row.Element.Query<Button>().ToList();
                Func<int, VisualElement> line = i => btns.Count > i ? btns[i].Q("indicator") : null;
                Check(btns.Count == 3 && line(0) != null && line(1) != null && line(2) != null, "a bare row A/B/C has an underline under each tab", btns.Count.ToString());
                if (line(0) != null && line(1) != null && line(2) != null)
                {
                    Check(Near(line(1).resolvedStyle.backgroundColor, bt[Role.Accent]) && line(0).resolvedStyle.backgroundColor.a == 0f && line(2).resolvedStyle.backgroundColor.a == 0f,
                        "after selecting B, B's underline is Accent and the others are clear", line(1).resolvedStyle.backgroundColor + " / " + line(0).resolvedStyle.backgroundColor);
                    Check(Px(line(1).style.height) == bt.Sizes.Indicator, "the underline is Sizes.Indicator tall", Px(line(1).style.height).ToString());
                    Check(Near(btns[1].resolvedStyle.color, bt[Role.Text]) && Near(btns[0].resolvedStyle.color, bt[Role.TextMuted]), "B's text is Text and A's TextMuted",
                        btns[1].resolvedStyle.color + " / " + btns[0].resolvedStyle.color);
                    var rule = row.Element.Q("rule");
                    Check(rule != null && Near(rule.resolvedStyle.backgroundColor, bt[Role.Line]) && Px(rule.style.height) == bt.Sizes.Line
                          && Mathf.Abs(rule.worldBound.yMax - row.Element.worldBound.yMax) < 0.5f && Mathf.Abs(line(1).worldBound.yMax - rule.worldBound.yMax) < 0.5f,
                        "the row sits on a Looks.Rule line, Sizes.Line tall, that the underline lands on", rule == null ? "none" : rule.worldBound + " / " + line(1).worldBound);
                }

                var ds = new Surface(Host.Create("TabRuleDark"), "FacetTabRuleDark", Layer.Window);
                try
                {
                    var dw = new Window(ds.Root, "tabruledark", "Dark row", new Rect(80, 80, 300, 120));
                    var dr = new Tabs(dw.Body, null);
                    dr.Rebuild(new[] { "A", "B" });
                    yield return null; yield return null;
                    var drule = dr.Element.Q("rule");
                    Check(drule != null && drule.resolvedStyle.backgroundColor.a == 0f, "on Dark a tab row's rule is invisible (0.1 drew none)", drule?.resolvedStyle.backgroundColor.ToString());
                }
                finally { ds.Dispose(); }

                // Outside any Nav, an explicit selection opens that page and only it.
                var built = new int[2];
                var pt = Tabs.Pages(bw.Body, new List<NavItem> { new NavItem("One", q => built[0]++), new NavItem("Two", q => built[1]++) }, 1);
                Check(pt.SelectedName == "Two" && built[0] == 0 && built[1] == 1 && pt.Content != null && row.Content == null,
                    "Tabs.Pages(..., 1) outside a Nav builds only Two; a bare row has no Content", built[0] + "," + built[1]);
            }
            finally { bs.Dispose(); }

            GalleryParts.Model = new PartsModel();
            Gallery.Store.Remove("tab.Tabs");
            var win = GalleryParts.Open(); yield return null; yield return null;
            var clock = History.Clock;
            try
            {
                var m = GalleryParts.Model; var nav = GalleryParts.Nav;
                Func<string, Label> lab = t => win.Body.Query<Label>().ToList().Find(l => l.text == t);
                Func<string, Button> tab = t => GalleryParts.TabsRow?.Element.Query<Button>().ToList().Find(b => b.text == t);
                Func<string> builds = () => string.Join(",", m.TabBuilds);
                nav.Go(GalleryParts.TabsItem); yield return null; yield return null;
                for (int i = 0; i < 5; i++) yield return null;
                var rowT = GalleryParts.TabsRow;
                Check(builds() == "1,0,0" && rowT.SelectedName == "General", "after construction the tab builds are (1, 0, 0)", builds());
                Check(m.TabShows == 1 && m.TabTicks > 0, "General's preview shows and ticks", m.TabShows + " shows, " + m.TabTicks + " ticks");
                Check(tab("Combo")?.tooltip == "Combo settings", "a tab carries its NavItem's tooltip", tab("Combo")?.tooltip);

                Submit(tab("Combo"));
                var content = rowT.Content;
                Check(content.style.opacity.value == 1f && content.style.display == DisplayStyle.Flex, "at speed 0, the content's opacity is 1 right after the select", content.style.opacity.value.ToString());
                Check(builds() == "1,1,0", "select Combo: (1, 1, 0)", builds());
                yield return null;
                Check(m.TabHides == 1, "General's preview runs Hide when Combo is selected", m.TabHides.ToString());
                int t0 = m.TabTicks;
                for (int i = 0; i < 10; i++) yield return null;
                Check(m.TabTicks == t0, "...and ticks no more", (m.TabTicks - t0).ToString());
                Submit(tab("General"));
                Check(builds() == "2,1,0", "select General: (2, 1, 0)", builds());
                yield return null;
                Check(m.TabShows == 2, "...and its preview shows again", m.TabShows.ToString());

                Submit(tab("Combo")); yield return null;
                string kept; Gallery.Store.TryGetValue("tab.Tabs", out kept);
                nav.Rebuild(); yield return null;
                Check(GalleryParts.TabsRow != rowT && GalleryParts.TabsRow.SelectedName == "Combo" && builds() == "2,3,0" && kept == "Combo",
                    "rebuilding the page with selected = -1 reopens Combo (the host holds tab.Tabs = Combo)", builds() + " " + kept);

                // Two flips with a reselect between them are one undo step only if the key stayed the same.
                History.Clock = () => 1000.0; History.Clear();
                ClickEl(GalleryParts.ComboToggle.Input); yield return null;
                Submit(tab("General")); Submit(tab("Combo")); yield return null;
                ClickEl(GalleryParts.ComboToggle.Input); yield return null;
                bool one = History.Undo() && !History.CanUndo;
                Check(one && !m.ComboFlag, "an unnamed Toggle in Combo keeps the same undo key after a reselect", History.CanUndo + " " + m.ComboFlag);
                History.Clock = clock; History.Clear();

                m.BreakGeneral = true;
                Submit(tab("General")); yield return null;
                var failed = GalleryParts.TabsRow.Content.Query<Label>().ToList().Find(l => l.text.Contains("failed to build"));
                Check(failed != null && GalleryParts.TabsRow.SelectedName == "General", "a tab whose Build throws shows the failed card", failed?.text);
                Submit(tab("Combo")); yield return null;
                Check(GalleryParts.TabsRow.SelectedName == "Combo" && lab("Combo flag") != null && (failed == null || failed.panel == null), "...and the other tabs still work");
                m.BreakGeneral = false;

                var sub = win.Body.Q("combo-sub");
                if (sub != null) ClickEl(sub);
                yield return null;
                Check(lab("Inside combo") != null && GalleryParts.TabsRow.Element.panel == null, "a SubPage row in a tab pushes its page");
                nav.Pop(); yield return null;
                Check(GalleryParts.TabsRow.SelectedName == "Combo" && lab("Combo flag") != null, "...and back returns to the Combo tab");

                Check(m.TabBuilds[2] == 0, "Judgement has never been built");
                nav.Filter("judgement size"); yield return null; yield return null;
                var hits = nav.Index.Find("judgement size");
                Check(hits.Count > 0 && hits[0].Tab == "Judgement" && hits[0].Key == "Size" && hits[0].Page == "Tabs",
                    "Filter(judgement size) finds Size in the never-visited Judgement tab: hit.Tab is Judgement", hits.Count > 0 ? hits[0].Page + "/" + hits[0].Tab + "/" + hits[0].Key : "none");
                var where = lab("Tabs · Judgement");
                Check(where != null, "the result says where it lives: Tabs · Judgement");
                if (where != null) ClickEl(where.parent);
                var ms = new double[1];
                Func<bool> revealed = () =>
                {
                    var r = win.Body.Q("judge-size"); if (r == null) return false;
                    var sv = r.GetFirstAncestorOfType<ScrollView>(); if (sv == null) return false;
                    var vp = sv.contentViewport.worldBound;
                    return r.worldBound.yMin >= vp.yMin - 1f && r.worldBound.yMax <= vp.yMax + 1f && r.worldBound.height > 0f;
                };
                yield return Until(revealed, ms, 2000);
                Check(ms[0] >= 0 && GalleryParts.TabsRow.SelectedName == "Judgement", "clicking it opens the page, selects Judgement and puts the row inside the viewport",
                    Ms(ms) + " " + GalleryParts.TabsRow.SelectedName);
                nav.Filter(""); yield return null;
            }
            finally { History.Clock = clock; GalleryParts.Close(); GalleryParts.Model = new PartsModel(); }

            // Ruling Q22: a consumer's own search entries, and a callback when a hit lands on an item.
            var logs = new List<string>();
            var qs = new Surface(Host.Create("TabHooks", o => o.Log = l => { logs.Add(l); Main.Log("[tabhooks] " + l); }), "FacetTabHooks", Layer.Window);
            try
            {
                var qw = new Window(qs.Root, "tabhooks", "Hooks", new Rect(60, 60, 600, 400));
                Func<string, Label> qlab = t => qw.Body.Query<Label>().ToList().Find(l => l.text == t);
                SearchIndex.Hit pageHit = null, tabHit = null;
                bool outerRan = false, rowThere = false; string seenTab = null;
                int runs = 0, throws = 0;
                Tabs qt = null;
                var home = new NavItem("Home", p => Controls.Info(p, "Home row", () => "h", o => o.Id = "home-row"));
                home.OnHit = h => { pageHit = h; rowThere = qw.Body.Q("home-row") != null; };
                var deep = new NavItem("Deep", p => Controls.Info(p, "Deep row", () => "d")) { OnHit = h => { tabHit = h; seenTab = qt?.SelectedName; } };
                var tabbed = new NavItem("Tabbed", p => qt = Tabs.Pages(p, new List<NavItem> { new NavItem("Top", q => Controls.Info(q, "Top row", () => "t")), deep }))
                    { OnHit = h => outerRan = true };
                var qn = new Nav(qw.Body, new[] { home, tabbed });
                qn.AddSearchSource(() => { throw new InvalidOperationException("source"); });
                qn.AddSearchSource(() => new[] {
                    new SearchIndex.Entry { Label = "Classic profiles", Where = "Classic menu", Open = () => runs++ },
                    new SearchIndex.Entry { Label = "Classic broken", Where = "Classic menu", Open = () => { throws++; throw new InvalidOperationException("entry"); } },
                });
                yield return null; yield return null;

                qn.Filter("classic profiles"); yield return null;
                var found = qn.Index.Find("classic profiles");
                Check(found.Count > 0 && found[0].Key == "Classic profiles" && found[0].Page == "Classic menu", "an external entry is in the index with its label and where",
                    found.Count > 0 ? found[0].Key + " @ " + found[0].Page : "none");
                Check(logs.Exists(l => l.Contains("source")), "a throwing source is logged and the other source's entries still index", string.Join(" | ", logs));
                var el = qlab("Classic profiles");
                Check(el != null && qlab("Classic menu") != null, "it shows among the results with its where text");
                if (el != null) ClickEl(el.parent);
                yield return null;
                Check(runs == 1, "choosing it runs its action once", runs.ToString());

                qn.Filter("classic broken"); yield return null;
                var bl = qlab("Classic broken");
                bool escaped = false;
                try { if (bl != null) { ClickEl(bl.parent); ClickEl(bl.parent); } } catch { escaped = true; }
                yield return null;
                Check(!escaped && throws == 2 && logs.FindAll(l => l.Contains("Classic broken")).Count == 1, "an action that throws twice is contained and logged once",
                    throws + " throws: " + string.Join(" | ", logs));

                qn.Filter("home row"); yield return null;
                var hl = qlab("Home row");
                if (hl != null) ClickEl(hl.parent);
                yield return null;
                Check(pageHit != null && pageHit.Key == "Home row" && rowThere, "a hit on an item fires its OnHit with the hit, once the page is open", pageHit?.Key + " " + rowThere);
                qn.Filter("deep row"); yield return null;
                var dl = qlab("Deep row");
                if (dl != null) ClickEl(dl.parent);
                yield return null;
                Check(tabHit != null && tabHit.Tab == "Deep" && seenTab == "Deep" && !outerRan, "a hit in a tab fires that tab's OnHit with its tab selected, not its page's",
                    tabHit?.Tab + " " + seenTab + " outer=" + outerRan);
                // Fix round 1: entry Tip, the hider, the translated where line, section kept, guards.
                var hid = new NavItem("Hider", p => Controls.Info(p, "Hide me row", () => "x")) { OnHit = h => qs.Visible = false };
                var cardy = new NavItem("Cardy", p =>
                {
                    var c = Controls.Card(p, "Cardy card");
                    Tabs.Pages(c, new List<NavItem> { new NavItem("T1", q => Controls.Info(q, "In tab row", () => "t")) });
                    Controls.Info(c, "After tab row", () => "a");
                });
                var n2 = new Nav(qw.Body, new[] { hid, cardy });
                n2.AddSearchSource(() => new[] { new SearchIndex.Entry { Label = "Plain entry", Where = "Elsewhere", Tip = "zebra crossing detail", Open = () => { } } });
                n2.Filter("zebra crossing"); yield return null;
                var byTip = n2.Index.Find("zebra crossing");
                Check(byTip.Count == 1 && byTip[0].Key == "Plain entry", "an entry found only by its Tip", byTip.Count.ToString());
                n2.Filter("after tab row");
                var sec = n2.Index.Find("after tab row");
                var sec2 = n2.Index.Find("in tab row");
                Check(sec.Count > 0 && sec[0].Section == "Cardy card" && sec[0].Tab == null && sec2.Count > 0 && sec2[0].Tab == "T1" && sec2[0].Section == "Cardy card",
                    "a row after a tab row in the same Card keeps the card as its section", sec.Count > 0 ? sec[0].Section : "none");

                logs.Clear();
                n2.Filter("hide me row"); yield return null;
                var hlab = qlab("Hide me row");
                if (hlab != null) ClickEl(hlab.parent);
                yield return new WaitForSeconds(0.8f);
                bool hidden = !qs.Visible;
                qs.Visible = true;
                Check(hidden && !logs.Exists(l => l.Contains("gave up")), "an OnHit that hides the surface ends the reveal without a 'gave up' log", hidden + " " + string.Join(" | ", logs));
                n2.Filter(""); yield return null;
            }
            finally { qs.Dispose(); }

            // The where line translates each part with the host's table, then joins.
            var tmap = new Dictionary<string, string> { { "Key page", "Seite" }, { "Key tab", "Reiter" }, { "Key section", "Bereich" } };
            var ts = new Surface(Host.Create("TabWhere", o => { o.Log = l => Main.Log("[tabwhere] " + l); o.Translate = k => tmap.TryGetValue(k, out var v) ? v : k; }), "FacetTabWhere", Layer.Window);
            try
            {
                var tw = new Window(ts.Root, "tabwhere", "Where", new Rect(60, 60, 600, 300));
                var kp = new NavItem("Key page", p => Tabs.Pages(p, new List<NavItem> { new NavItem("Key tab", q => { Controls.Section(q, "Key section"); Controls.Info(q, "Needle row", () => "n"); }) }));
                var tn = new Nav(tw.Body, new[] { kp });
                yield return null; yield return null;
                tn.Filter("needle row"); yield return null;
                Check(tw.Body.Query<Label>().ToList().Exists(l => l.text == "Seite · Reiter · Bereich"), "the where line shows the translated page, tab and section",
                    string.Join(" | ", tw.Body.Query<Label>().ToList().ConvertAll(l => l.text)));
            }
            finally { ts.Dispose(); }

            // Pages misuse guards and the session memory of the picked tab.
            var gs = new Surface(Host.Create("TabGuards"), "FacetTabGuards", Layer.Window);
            try
            {
                var gw = new Window(gs.Root, "tabguards", "Guards", new Rect(60, 60, 400, 200));
                var list = new List<NavItem> { new NavItem("G1", q => { }), new NavItem("G2", q => { }), new NavItem("G3", q => { }) { VisibleWhen = () => false } };
                var g = Tabs.Pages(gw.Body, list, 0);
                list.Clear();
                g.Rebuild(new[] { "x" });
                g.Select(2);
                Check(g.Element.Query<Button>().ToList().Count == 3 && g.Selected == 0, "Rebuild(names) on a Pages row is ignored, and Select ignores a tab VisibleWhen hid, and the list was copied", g.Selected.ToString());
                g.Select(1);
                var g2 = Tabs.Pages(gw.Body, new List<NavItem> { new NavItem("G1", q => { }), new NavItem("G2", q => { }) });
                Check(g2.SelectedName == "G2", "with no host Read/Write a rebuild keeps the tab the user picked", g2.SelectedName);
            }
            finally { gs.Dispose(); }
        }

        // Task 13: the labelled sidebar: indicator bars, rules between groups, caps headings.
        IEnumerator SectionSidebar()
        {
            var st = new Theme();
            st.Looks.Indicator = new Look { SelectedBg = Role.Accent };
            st.Looks.Rule = new Look { Bg = Role.Line };   // Dark draws no rule (Q25); a skin gives it a colour
            st[Role.Rail] = new Color(0.2f, 0.1f, 0.3f, 1f);
            bool ko = false;
            var ss = new Surface(Host.Create("Sidebar", o => { o.Log = l => Main.Log("[sidebar] " + l); o.Theme = st; o.Translate = k => ko && k == "Tint" ? "틴트" : k; }), "FacetSidebar", Layer.Window);
            try
            {
                var sw = new Window(ss.Root, "sidebar", "Sidebar", new Rect(60, 60, 600, 400));
                Action<VisualElement> nop = p => { };
                var open = new NavItem("Two", nop);
                var sn = new Nav(sw.Body, new[]
                {
                    NavItem.Heading(null, new NavItem("One", nop), open),
                    NavItem.Heading(null, new NavItem("Three", nop)),
                    NavItem.Heading(null, new NavItem("Four", nop), new NavItem("Five", nop)),
                });
                sn.Go(open);
                yield return null; yield return null;
                var railScroll = sw.Body.Q<ScrollView>();
                var rail = railScroll.contentContainer;
                var rules = rail.Query(name: "rule").ToList();
                Check(rules.Count == 2 && rules.TrueForAll(r => Px(r.style.height) == st.Sizes.Line && Near(r.resolvedStyle.backgroundColor, st[Role.Line])),
                    "3 null-titled groups draw 2 rules, Sizes.Line tall, in the theme's Line", rules.Count.ToString());
                Check(rules.Count > 0 && rules.TrueForAll(r => Px(r.style.marginTop) == st.Sizes.Gap && Px(r.style.marginBottom) == st.Sizes.Gap), "a rule's margins are Sizes.Gap");
                Check(rules.Count > 0 && rail.IndexOf(rules[0]) > 0, "no rule sits above the first entry");
                Check(rail.Query<Label>().ToList().Count == 5, "no caps heading is drawn for a null-titled group", rail.Query<Label>().ToList().Count.ToString());
                var inds = rail.Query(name: "indicator").ToList();
                Check(inds.Count == 5 && Near(inds[1].resolvedStyle.backgroundColor, st[Role.Accent]) && Px(inds[1].style.width) == st.Sizes.Indicator
                      && inds.FindAll(i => i.resolvedStyle.backgroundColor.a > 0f).Count == 1,
                    "only the open entry's indicator is Accent, Sizes.Indicator wide", inds.Count.ToString());
                Check(inds.TrueForAll(i => Px(i.parent.style.minHeight) == st.Sizes.NavRowHeight), "entries are Sizes.NavRowHeight tall");
                Check(Near(railScroll.resolvedStyle.backgroundColor, st[Role.Rail]), "the rail's background is the theme's Rail", railScroll.resolvedStyle.backgroundColor.ToString());
                st[Role.Rail] = new Color(0.4f, 0.3f, 0.1f, 1f);
                Paint.Apply(ss.Root); yield return null;
                Check(Near(railScroll.resolvedStyle.backgroundColor, st[Role.Rail]), "re-themed, the rail takes the new Rail colour", railScroll.resolvedStyle.backgroundColor.ToString());

                // Hidden groups leave no rule: a middle group that hides, and a trailing one.
                bool hide = true;
                Func<bool> shown = () => !hide;
                Action<VisualElement> nop2 = p => { };
                var hw = new Window(ss.Root, "sidebarhide", "Hidden groups", new Rect(80, 80, 600, 400));
                var hn = new Nav(hw.Body, new[]
                {
                    NavItem.Heading(null, new NavItem("A", nop2)),
                    NavItem.Heading(null, new NavItem("B", nop2) { VisibleWhen = shown }),
                    NavItem.Heading(null, new NavItem("C", nop2)),
                    NavItem.Heading(null, new NavItem("D", nop2) { VisibleWhen = shown }),
                });
                yield return null; yield return null;
                var hrail = hw.Body.Q<ScrollView>().contentContainer;
                Func<int> ruleCount = () => hrail.Query(name: "rule").ToList().Count;
                Func<bool> adjacent = () => { for (int i = 1; i < hrail.childCount; i++) if (hrail[i].name == "rule" && hrail[i - 1].name == "rule") return true; return false; };
                Check(ruleCount() == 1 && !adjacent() && hrail[hrail.childCount - 1].name != "rule", "a hidden middle group and a hidden trailing group leave 1 rule (groups that drew, less one), none trailing",
                    ruleCount().ToString());
                hide = false; hn.Refresh(); yield return null; yield return null;
                Check(ruleCount() == 3, "with all 4 groups shown there are 3 rules", ruleCount().ToString());

                // A titled group reads in caps; in Korean it reads the translation (the 0.1 bug).
                var tw2 = new Window(ss.Root, "sidebartint", "Tint", new Rect(100, 100, 600, 300));
                var tn = new Nav(tw2.Body, new[] { NavItem.Heading("Tint", new NavItem("Six", nop)) });
                yield return null; yield return null;
                Func<string> head = () => tw2.Body.Query<Label>().ToList().Find(l => l.text == "TINT" || l.text == "틴트")?.text;
                Check(head() == "TINT", "a titled group \"Tint\" reads TINT", head());
                ko = true; ss.Relabel(); yield return null;
                Check(head() == "틴트", "in Korean it reads 틴트", head());
            }
            finally { ss.Dispose(); }
        }
    }
}
