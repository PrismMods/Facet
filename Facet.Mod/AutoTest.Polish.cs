using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Small layout and motion details of rows and the rail, one section each.
    sealed partial class AutoTest
    {
        /* A page rebuilt into a shown window builds its switches on a live panel before any layout: the
           knob must start at its value, not slide in from off. A later change still slides. */
        IEnumerator SectionSwitchSnap()
        {
            Anim.Speed = 1f;   // the slide only exists with motion on; Run puts the speed back
            var s = new Surface(Host.Create("Snap", o => o.Log = m => Main.Log("[snap] " + m)), "FacetSnap", Layer.Window);
            try
            {
                var w = new Window(s.Root, "snap", "Snap", new Rect(60, 60, 420, 200));
                yield return null; yield return null;
                bool v = true;
                var c = Controls.Toggle(w.Body, "Snap", Bind.To(() => v, x => v = x), o => o.Id = "snap");
                var knob = c.Input.hierarchy[0];
                var sz = Theme.For(c.Input).Sizes;
                float size = sz.SwitchHeight - 4f, off = 2f - sz.Line, on = sz.SwitchWidth - size - 2f - sz.Line;
                Check(knob.panel != null && Px(knob.style.left) == on, "a switch built on into a shown window has its knob at on at once",
                    "panel " + (knob.panel != null) + ", left " + Px(knob.style.left) + " vs " + on);
                yield return null;
                // Resolved positions snap to device pixels: within one panel pixel is "at".
                Check(Mathf.Abs(knob.resolvedStyle.left - on) < 1f, "...and after one layout pass it resolves at on, with nothing sliding",
                    knob.resolvedStyle.left + " vs " + on);
                c.Set(false);
                Check(Px(knob.style.left) != off, "switching it off once laid out slides the knob rather than jumping", Px(knob.style.left).ToString());
                var ms = new double[1];
                yield return Until(() => Mathf.Abs(knob.resolvedStyle.left - off) < 1f, ms, 600);
                Check(ms[0] >= 0, "...and the knob comes to rest at off", knob.resolvedStyle.left + " vs " + off);
            }
            finally { s.Dispose(); }
        }

        /* A rail entry may carry an icon left of its title: Sizes.Icon square, Sizes.Gap before the label,
           muted unless the entry is open. Entries with and without one start at the same left edge. */
        IEnumerator SectionRailIcon()
        {
            var s = new Surface(Host.Create("RailIcon", o => o.Log = m => Main.Log("[railicon] " + m)), "FacetRailIcon", Layer.Window);
            try
            {
                var w = new Window(s.Root, "railicon", "Rail icons", new Rect(60, 60, 600, 400));
                Action<VisualElement> nop = p => { };
                var icon = Background.FromTexture2D(Texture2D.whiteTexture);
                var open = new NavItem("Opened", nop) { Icon = icon };
                var nav = new Nav(w.Body, new[] { NavItem.Heading(null, open, new NavItem("Iconed", nop) { Icon = icon }, new NavItem("Plain", nop)) });
                nav.Go(open);
                yield return null; yield return null;
                var th = Theme.For(w.Body); var sz = th.Sizes;
                var rail = w.Body.Q<ScrollView>().contentContainer;
                Func<string, Label> lab = t => rail.Query<Label>().ToList().Find(l => l.text == t);
                var ic = rail.Q("Iconed-icon"); var oc = rail.Q("Opened-icon");
                var il = lab("Iconed"); var pl = lab("Plain");
                Check(ic != null && oc != null && rail.Q("Plain-icon") == null, "entries with an Icon draw <title>-icon; one without draws none");
                Check(ic != null && Mathf.Abs(ic.resolvedStyle.width - sz.Icon) < 0.5f && Mathf.Abs(ic.resolvedStyle.height - sz.Icon) < 0.5f,
                    "the icon is a Sizes.Icon square", ic == null ? "none" : ic.resolvedStyle.width + "x" + ic.resolvedStyle.height + " vs " + sz.Icon);
                Check(ic != null && il != null && ic.parent == il.parent && Mathf.Abs(il.worldBound.xMin - ic.worldBound.xMax - sz.Gap) < 1f,
                    "it sits in the entry's row, Sizes.Gap left of the title", ic == null || il == null ? "none" : ic.worldBound.xMax + " -> " + il.worldBound.xMin);
                Check(ic != null && pl != null && Mathf.Abs(ic.worldBound.xMin - pl.worldBound.xMin) < 1f,
                    "an icon starts where a plain entry's title starts: the rail keeps its left edge", ic == null || pl == null ? "none" : ic.worldBound.xMin + " vs " + pl.worldBound.xMin);
                Check(ic != null && Near(ic.resolvedStyle.unityBackgroundImageTintColor, th[Role.TextMuted]), "a closed entry's icon is tinted TextMuted",
                    ic?.resolvedStyle.unityBackgroundImageTintColor.ToString());
                Check(oc != null && Near(oc.resolvedStyle.unityBackgroundImageTintColor, th[Role.Text]), "the open entry's icon is tinted Text",
                    oc?.resolvedStyle.unityBackgroundImageTintColor.ToString());
                // Read in the same frame: the real pointer is elsewhere, so the panel sends a leave next frame.
                var irow = ic?.parent;
                if (irow != null) using (var e = MouseEnterEvent.GetPooled()) { e.target = irow; irow.SendEvent(e); }
                Check(ic != null && Near(ic.style.unityBackgroundImageTintColor.value, th[Role.Text]), "hovering a closed entry tints its icon Text, as its title",
                    ic?.style.unityBackgroundImageTintColor.value.ToString());
                if (irow != null) using (var e = MouseLeaveEvent.GetPooled()) { e.target = irow; irow.SendEvent(e); }
                Check(ic != null && Near(ic.style.unityBackgroundImageTintColor.value, th[Role.TextMuted]), "...and TextMuted again once the pointer leaves",
                    ic?.style.unityBackgroundImageTintColor.value.ToString());
            }
            finally { s.Dispose(); }
        }

        /* One card holding one row of each value control: every control ends on the same right edge (a
           Choice by its last segment), and the dropdown's arrow sits ControlPad inside its box. */
        IEnumerator SectionRowEdges()
        {
            var s = new Surface(Host.Create("Edges", o => o.Log = m => Main.Log("[edges] " + m)), "FacetEdges", Layer.Window);
            try
            {
                var w = new Window(s.Root, "edges", "Edges", new Rect(60, 60, 640, 600));
                var card = Controls.Card(w.Body, "Rows");
                bool b = true; float f = 0.5f; string t = "text", k = "F8"; int d = 0; Color col = Color.cyan;
                Controls.Toggle(card, "Toggle", Bind.To(() => b, x => b = x), o => o.Id = "e-toggle");
                Controls.Slider(card, "Slider", Bind.To(() => f, x => f = x), o => o.Id = "e-slider");
                Controls.Text(card, "Text", Bind.To(() => t, x => t = x), o => o.Id = "e-text");
                Controls.Choice(card, "Choice", Bind.To(() => d, x => d = x), o => { o.Id = "e-choice"; o.Items = new[] { "All", "On", "Off" }; });
                Controls.Dropdown(card, "Dropdown", Bind.To(() => d, x => d = x), o => { o.Id = "e-drop"; o.Items = new[] { "Option", "Other" }; });
                Controls.Colour(card, "Colour", Bind.To(() => col, x => col = x), o => o.Id = "e-colour");
                Controls.Key(card, "Key", Bind.To(() => k, x => k = x), o => { o.Id = "e-key"; o.Capture = done => done(k); });
                Controls.Action(card, "Action", "Run", () => { }, o => o.Id = "e-action");
                yield return null; yield return null;
                var segs = w.Body.Q("e-choice-input")?.Query<Button>().ToList();
                var parts = new List<KeyValuePair<string, VisualElement>>
                {
                    new KeyValuePair<string, VisualElement>("switch", w.Body.Q("e-toggle-input")),
                    new KeyValuePair<string, VisualElement>("number", w.Body.Q("e-slider-number")),
                    new KeyValuePair<string, VisualElement>("text", w.Body.Q("e-text-input")),
                    new KeyValuePair<string, VisualElement>("last segment", segs != null && segs.Count == 3 ? segs[2] : null),
                    new KeyValuePair<string, VisualElement>("dropdown", w.Body.Q("e-drop-input")),
                    new KeyValuePair<string, VisualElement>("swatch", w.Body.Q("e-colour-swatch")),
                    new KeyValuePair<string, VisualElement>("key", w.Body.Q("e-key-input")),
                    new KeyValuePair<string, VisualElement>("action", w.Body.Q("e-action-input")),
                };
                var edges = new List<string>();
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in parts)
                {
                    float x = p.Value == null ? float.NaN : p.Value.worldBound.xMax;
                    edges.Add(p.Key + " " + (p.Value == null ? "missing" : x.ToString("0.0")));
                    if (p.Value == null) { lo = float.MinValue; continue; }
                    lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
                }
                Check(hi - lo <= 1f, "every control in a card's rows ends on the same right edge (±1)", string.Join(", ", edges.ToArray()));
                var box = w.Body.Q("e-drop-input"); var arrow = w.Body.Q("e-drop-arrow");
                float pad = Theme.For(card).Sizes.ControlPad;
                Check(box != null && arrow != null && box.worldBound.xMax - arrow.worldBound.xMax >= pad - 1f,
                    "the dropdown's arrow ends at least ControlPad inside its box", box == null || arrow == null ? "none" : arrow.worldBound.xMax + " in " + box.worldBound.xMax);
            }
            finally { s.Dispose(); }
        }

        /* A row that opens a page reserves an arrow slot, and so does every row in its card, those in a
           ToggleGroup's body included; a card without one ends its controls Pad inside its edge, and a page
           header's switch ends there too, as does a card header's note with no actions after it. A page row outside any
           card reserves the slot in its own container only, never in the cards beside it. */
        IEnumerator SectionArrowSlot()
        {
            var s = new Surface(Host.Create("Slot", o => o.Log = m => Main.Log("[slot] " + m)), "FacetSlot", Layer.Window);
            try
            {
                var w = new Window(s.Root, "slot", "Slot", new Rect(60, 60, 640, 640));
                bool b = true; Color col = Color.cyan;
                Controls.PageHeader(w.Body, "Feature", Bind.To(() => b, x => b = x), o => o.Id = "s-head");
                var plain = Controls.Card(w.Body, "Plain");
                Controls.Toggle(plain, "Toggle", Bind.To(() => b, x => b = x), o => o.Id = "s-toggle");
                Controls.Colour(plain, "Colour", Bind.To(() => col, x => col = x), o => o.Id = "s-colour");
                var paged = Controls.Card(w.Body, "Paged");
                Controls.Toggle(paged, "Before", Bind.To(() => b, x => b = x), o => o.Id = "p-before");
                var group = Controls.ToggleGroup(paged, "Group", Bind.To(() => b, x => b = x));
                Controls.Toggle(group, "Nested", Bind.To(() => b, x => b = x), o => o.Id = "p-nested");
                Controls.SubPage(paged, "More", q => { }, o => o.Id = "p-sub");
                Controls.Toggle(paged, "After", Bind.To(() => b, x => b = x), o => o.Id = "p-after");
                Controls.Colour(paged, "Colour", Bind.To(() => col, x => col = x), o => o.Id = "p-colour");
                var later = Controls.ToggleGroup(paged, "Later", Bind.To(() => b, x => b = x));
                Controls.Toggle(later, "Nested after", Bind.To(() => b, x => b = x), o => o.Id = "p-nested-after");
                var noted = Controls.Card(w.Body, "Noted", o => o.Note = "Licence");
                Controls.Toggle(noted, "Toggle", Bind.To(() => b, x => b = x), o => o.Id = "n-toggle");
                var linked = Controls.Card(w.Body, "Linked", o =>
                {
                    o.Note = "Some";
                    o.Actions = new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>("All", () => { }) };
                });
                yield return null; yield return null;
                var sz = Theme.For(plain).Sizes;
                var card = plain.parent;
                float inner = card.worldBound.xMax - card.resolvedStyle.borderRightWidth;
                var sw = w.Body.Q("s-toggle-input"); var swatch = w.Body.Q("s-colour-swatch"); var track = w.Body.Q("s-head-input");
                Check(sw != null && Mathf.Abs(sw.worldBound.xMax - (inner - sz.Pad)) <= 1f, "in a card with no page row a control ends Pad inside the card's inner edge",
                    sw == null ? "none" : sw.worldBound.xMax + " vs " + (inner - sz.Pad));
                Check(sw != null && swatch != null && Mathf.Abs(swatch.worldBound.xMax - sw.worldBound.xMax) <= 1f, "...and so does a colour row's swatch",
                    swatch == null ? "none" : swatch.worldBound.xMax.ToString());
                Check(sw != null && track != null && Mathf.Abs(track.worldBound.xMax - sw.worldBound.xMax) <= 1f, "the page header's switch ends on the card rows' control edge",
                    track == null || sw == null ? "none" : track.worldBound.xMax + " vs " + sw.worldBound.xMax);
                var parts = new[] { w.Body.Q("p-before-input"), w.Body.Q("p-after-input"), w.Body.Q("p-colour-swatch") };
                var sub = w.Body.Q("p-sub");
                var arrow = sub == null ? null : sub[sub.childCount - 1];
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in parts) { float x = p == null ? float.NaN : p.worldBound.xMax; lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
                Check(Array.TrueForAll(parts, p => p != null) && hi - lo <= 1f, "in a card with a page row every control still ends on one edge", lo + ".." + hi);
                Check(arrow != null && arrow.name == "arrow" && arrow.resolvedStyle.display == DisplayStyle.Flex && parts[0] != null
                      && Mathf.Abs(arrow.worldBound.xMin - parts[0].worldBound.xMax) <= 1f, "...one arrow slot in from the page row's arrow",
                    arrow == null || parts[0] == null ? "none" : arrow.worldBound.xMin + " vs " + parts[0].worldBound.xMax);
                var nested = new[] { w.Body.Q("p-nested-input"), w.Body.Q("p-nested-after-input") };
                Check(parts[0] != null && Array.TrueForAll(nested, p => p != null && Mathf.Abs(p.worldBound.xMax - parts[0].worldBound.xMax) <= 1f),
                    "a row in a ToggleGroup body of that card ends on its siblings' control edge, built before the page row or after",
                    string.Join(", ", Array.ConvertAll(nested, p => p == null ? "none" : p.worldBound.xMax.ToString("0.0"))) + " vs " + (parts[0] == null ? "none" : parts[0].worldBound.xMax.ToString("0.0")));
                var plainRow = w.Body.Q("s-toggle");
                var slot = plainRow == null ? null : plainRow[plainRow.childCount - 1];
                Check(slot != null && slot.name == "arrow" && slot.resolvedStyle.display == DisplayStyle.None, "a card with no page row reserves no arrow slot");
                Func<VisualElement, string, Label> note = (body, text) => body.parent.Query<Label>().Where(l => l.text == text).First();
                var lone = note(noted, "Licence"); var nsw = w.Body.Q("n-toggle-input");
                Check(lone != null && nsw != null && Mathf.Abs(lone.worldBound.xMax - nsw.worldBound.xMax) <= 1f, "a header note with no actions after it ends on the rows' control edge",
                    lone == null || nsw == null ? "none" : lone.worldBound.xMax + " vs " + nsw.worldBound.xMax);
                var some = note(linked, "Some"); var all = linked.parent.Query<Button>().Where(x => x.text == "All").First();
                Check(some != null && all != null && Mathf.Abs(all.worldBound.xMin - some.worldBound.xMax - sz.Gap) <= 1f, "...and a note before actions keeps a Gap before them",
                    some == null || all == null ? "none" : (all.worldBound.xMin - some.worldBound.xMax).ToString());
            }
            finally { s.Dispose(); }

            // A page row outside any card marks only its own container: the cards beside it keep no slot.
            var s2 = new Surface(Host.Create("Slot2", o => o.Log = m => Main.Log("[slot2] " + m)), "FacetSlot2", Layer.Window);
            try
            {
                var w2 = new Window(s2.Root, "slot2", "Slot 2", new Rect(60, 60, 640, 480));
                bool b2 = true;
                Controls.Toggle(Controls.Card(w2.Body, "First"), "Toggle", Bind.To(() => b2, x => b2 = x), o => o.Id = "f-toggle");
                Controls.SubPage(w2.Body, "Bare", q => { }, o => o.Id = "bare-sub");
                Controls.Toggle(Controls.Card(w2.Body, "Second"), "Toggle", Bind.To(() => b2, x => b2 = x), o => o.Id = "g-toggle");
                yield return null; yield return null;
                var rows = new[] { w2.Body.Q("f-toggle"), w2.Body.Q("g-toggle") };
                Func<VisualElement, string> shown = r => r == null ? "none" : r[r.childCount - 1].name + ":" + r[r.childCount - 1].resolvedStyle.display;
                Check(Array.TrueForAll(rows, r => r != null && r[r.childCount - 1].name == "arrow" && r[r.childCount - 1].resolvedStyle.display == DisplayStyle.None),
                    "a bare page row on a window body leaves the slots of the cards before and after it hidden", shown(rows[0]) + ", " + shown(rows[1]));
                var ins = new[] { w2.Body.Q("f-toggle-input"), w2.Body.Q("g-toggle-input") };
                Check(ins[0] != null && ins[1] != null && Mathf.Abs(ins[0].worldBound.xMax - ins[1].worldBound.xMax) <= 1f, "...so both cards end their controls on one edge",
                    ins[0] == null || ins[1] == null ? "none" : ins[0].worldBound.xMax + " vs " + ins[1].worldBound.xMax);
            }
            finally { s2.Dispose(); }
        }

        /* A button row with no label of its own (Action or Danger with "") is found by its button's text. */
        IEnumerator SectionButtonRows()
        {
            var s = new Surface(Host.Create("BtnRows", o => { o.Log = m => Main.Log("[btnrows] " + m); o.Translate = k => k == "Reset counts" ? "카운트 초기화" : k; }),
                "FacetBtnRows", Layer.Window);
            try
            {
                var w = new Window(s.Root, "btnrows", "Button rows", new Rect(60, 60, 600, 400));
                var page = new NavItem("Keys", p =>
                {
                    var c = Controls.Card(p, "Counts");
                    Controls.Action(c, "", "Reset counts", () => { }, o => { o.Id = "br-reset"; o.Tooltip = "Zero every key"; });
                    Controls.Danger(c, "", "Wipe stats", () => { }, o => o.Id = "br-wipe");
                });
                var nav = new Nav(w.Body, new[] { page });
                yield return null; yield return null;
                nav.Filter("reset counts"); yield return null;
                var idx = nav.Index;
                Func<string, string, bool> finds = (q, key) => idx != null && idx.Find(q).Exists(h => h.Key == key && h.PageRef == page);
                Check(finds("reset counts", "Reset counts"), "an Action row with no label is found by its button's English text");
                Check(finds("카운트", "Reset counts"), "...and by the button as shown");
                Check(finds("zero every key", "Reset counts"), "...and its tooltip joins the hit");
                Check(finds("wipe stats", "Wipe stats"), "a Danger row with no label is found by its button's text");
                nav.Filter(""); yield return null;
                var row = w.Body.Q("br-reset");
                Check(row != null && row.Query<Label>().ToList().TrueForAll(l => l.text != "카운트 초기화"), "the button's text is not drawn again as a row label");
            }
            finally { s.Dispose(); }
        }

        /* A window scales up to 2.5, so a host's own UI scale above 2 still grows it. */
        IEnumerator SectionScaleClamp()
        {
            var s = new Surface(Host.Create("ScaleClamp"), "FacetScaleClamp", Layer.Window);
            try
            {
                var w = new Window(s.Root, "scaleclamp", "Scale", new Rect(60, 60, 400, 300));
                w.Scale = 2.3f;
                Check(Mathf.Abs(w.Scale - 2.3f) < 1e-4f, "a window takes a scale of 2.3", w.Scale.ToString());
                w.Scale = 3f;
                Check(Mathf.Abs(w.Scale - 2.5f) < 1e-4f, "...and stops at 2.5", w.Scale.ToString());
                w.Scale = 0.1f;
                Check(Mathf.Abs(w.Scale - 0.6f) < 1e-4f, "...and at 0.6 the other way", w.Scale.ToString());
                yield return null;
            }
            finally { s.Dispose(); }
        }

        /* A field clips its text inside its border: a value longer than the box is cut there, not drawn past it. */
        IEnumerator SectionFieldClip()
        {
            var s = new Surface(Host.Create("FieldClip", o => o.Log = m => Main.Log("[fieldclip] " + m)), "FacetFieldClip", Layer.Window);
            try
            {
                var w = new Window(s.Root, "fieldclip", "Field clip", new Rect(60, 60, 520, 300));
                float f = 0.5f; string t = "text";
                Controls.Slider(w.Body, "Slider", Bind.To(() => f, x => f = x), o => o.Id = "fc-slider");
                Controls.Text(w.Body, "Text", Bind.To(() => t, x => t = x), o => o.Id = "fc-text");
                yield return null; yield return null;
                var ti = w.Body.Q("fc-text-input")?.Q(className: "unity-base-text-field__input");
                var ni = w.Body.Q("fc-slider-number")?.Q(className: "unity-base-text-field__input");
                Check(ti != null && ni != null && ti.style.overflow.value == Overflow.Hidden && ni.style.overflow.value == Overflow.Hidden,
                    "a text field and a slider's number box clip their text inside the border", ti == null || ni == null ? "none" : ti.style.overflow.value + " / " + ni.style.overflow.value);
            }
            finally { s.Dispose(); }
        }

        // A surface of its own, wearing `th`, for a section that checks a size.
        static Surface Themed(string name, Theme th)
            => new Surface(Host.Create(name, o => { o.Log = m => Main.Log("[" + name + "] " + m); o.Theme = th; }), "Facet" + name, Layer.Window);

        /* A narrow slider thumb keeps a corner it can draw: half its narrow side, not the knob's radius. */
        IEnumerator SectionThumbRadius()
        {
            var th = new Theme();
            th.Sizes.KnobRadius = 6f; th.Sizes.ThumbWidth = 4f; th.Sizes.ThumbHeight = 14f;
            var s = Themed("ThumbRadius", th);
            try
            {
                var w = new Window(s.Root, "thumbradius", "Thumb radius", new Rect(60, 60, 520, 300));
                float f = 0.5f;
                Controls.Slider(w.Body, "Slider", Bind.To(() => f, x => f = x), o => o.Id = "tr-slider");
                yield return null; yield return null;
                var dragger = w.Body.Q("tr-slider-input")?.Q(className: "unity-base-slider__dragger");
                Check(dragger != null && Px(dragger.style.borderTopLeftRadius) == 2f, "a 4 px wide thumb's corner is 2, half its narrow side, not the knob's 6",
                    dragger == null ? "none" : Px(dragger.style.borderTopLeftRadius).ToString());
            }
            finally { s.Dispose(); }
        }

        /* The title bar's undo and redo are bare window controls at title size, like the close button. */
        IEnumerator SectionTitleGlyphs()
        {
            var th = new Theme();
            var s = Themed("TitleGlyphs", th);
            try
            {
                var w = new Window(s.Root, "titleglyphs", "Title glyphs", new Rect(60, 60, 520, 300));
                yield return null; yield return null;
                var bs = w.TitleBarRight.Query<Button>().ToList();
                var sz = th.Sizes;
                Check(bs.Count == 3 && bs[0].resolvedStyle.backgroundColor.a == 0f && bs[1].resolvedStyle.backgroundColor.a == 0f,
                    "undo and redo are bare until hovered, like the close button", bs.Count < 2 ? "none" : bs[0].resolvedStyle.backgroundColor + " / " + bs[1].resolvedStyle.backgroundColor);
                Check(bs.Count == 3 && Px(bs[0].style.fontSize) == sz.Title + 3f && Px(bs[1].style.fontSize) == sz.Title + 3f,
                    "...drawn at Sizes.Title + 3", bs.Count < 2 ? "none" : Px(bs[0].style.fontSize).ToString());
            }
            finally { s.Dispose(); }
        }

        /* Sizes.RowPadY stands a row's wrapped text off its top and bottom; one line stays RowHeight. Facet's own
           default is 0, the look it had. */
        IEnumerator SectionRowPad()
        {
            Check(new Sizes().RowPadY == 0f, "Facet's own RowPadY is 0: rows keep the look they had", new Sizes().RowPadY.ToString());
            var th = new Theme();
            th.Sizes.RowPadY = 6f; th.Sizes.RowHeight = 38f;
            var s = Themed("RowPad", th);
            try
            {
                var w = new Window(s.Root, "rowpad", "Row padding", new Rect(60, 60, 460, 640));
                var card = Controls.Card(w.Body, "Credits");
                string longText = "A credits line long enough to wrap over several lines in a narrow window, and then some more words after that.";
                bool b = true;
                Controls.Info(card, "Made by", () => longText, o => o.Id = "rp-info");
                Controls.Toggle(card, "Helped", Bind.To(() => b, x => b = x), o => { o.Id = "rp-help"; o.Help = longText; });
                Controls.Toggle(card, "Plain", Bind.To(() => b, x => b = x), o => o.Id = "rp-plain");
                yield return null; yield return null;
                float pad = th.Sizes.RowPadY;
                var info = w.Body.Q("rp-info"); var v = w.Body.Q("rp-info-input");
                Check(v != null && v.worldBound.height > 2f * th.Sizes.Small, "the Info value wraps over several lines", v == null ? "none" : v.worldBound.height.ToString());
                Check(info != null && v != null && v.worldBound.yMin - info.worldBound.yMin >= pad - 0.5f && info.worldBound.yMax - v.worldBound.yMax >= pad - 0.5f,
                    "a wrapped Info row's text sits RowPadY inside its top and bottom", info == null || v == null ? "none" : v.worldBound + " in " + info.worldBound);
                var help = w.Body.Q("rp-help"); var col = help?.Q<Label>("help")?.parent;
                Check(help != null && col != null && col.worldBound.yMin - help.worldBound.yMin >= pad - 0.5f && help.worldBound.yMax - col.worldBound.yMax >= pad - 0.5f,
                    "a row whose help wraps keeps its label and help RowPadY inside its top and bottom", help == null || col == null ? "none" : col.worldBound + " in " + help.worldBound);
                var plain = w.Body.Q("rp-plain");
                Check(plain != null && Mathf.Abs(plain.worldBound.height - th.Sizes.RowHeight) <= 1f, "a one-line row stays RowHeight tall",
                    plain == null ? "none" : plain.worldBound.height.ToString());
            }
            finally { s.Dispose(); }
        }

        /* Sizes.TabGap is the space under a tab row, before its page; Facet's own default is Gap's 8, the look it had. */
        IEnumerator SectionTabGap()
        {
            var d = new Sizes();
            Check(d.TabGap == 8f && d.TabGap == d.Gap, "Facet's own TabGap is Gap's 8: tab rows keep the look they had", d.TabGap.ToString());
            var th = new Theme();
            th.Sizes.TabGap = 20f;
            var s = Themed("TabGap", th);
            try
            {
                var w = new Window(s.Root, "tabgap", "Tab gap", new Rect(60, 60, 460, 400));
                var tabs = Tabs.Pages(w.Body, new[] { new NavItem("One", p => Controls.PageHeader(p, "Head", null, o => o.Id = "tg-head")), new NavItem("Two", p => { }) }, 0);
                yield return null; yield return null;
                var head = w.Body.Q("tg-head");
                Check(head != null && Mathf.Abs(head.worldBound.yMin - tabs.Element.worldBound.yMax - th.Sizes.TabGap) <= 1f, "a tab row stands Sizes.TabGap above its page",
                    head == null ? "none" : (head.worldBound.yMin - tabs.Element.worldBound.yMax).ToString());
            }
            finally { s.Dispose(); }
        }
    }
}
