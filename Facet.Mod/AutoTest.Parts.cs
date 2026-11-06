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
