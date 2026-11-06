using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* Settings rows.

       This is the replacement for the widget half of UIBuilder, which both mods carry in copies
       that differ by 615 lines out of 4503 — the same toggle, slider and dropdown written twice and
       then drifted. Every widget here is a labelled ROW: label on the left, control on the right,
       one consistent height. That shape is the reason the two mods' settings screens looked alike
       in the first place, so it is the thing worth keeping.

       Each takes a current value and a setter rather than binding to a field, because the mods
       store settings in their own serialised classes and none of that belongs here. A widget reads
       once at build and writes on change; a host that mutates a value behind the UI's back calls
       Refresh on the page, exactly as the uGUI version did. */
    public static class Widgets
    {
        public const float RowHeight = 32f;
        private const float ArrowSlot = 22f;
        private const float ArrowSize = 20f;   // the chevron was a speck at body size

        /// Label on the left, whatever the caller adds on the right.
        public static VisualElement Row(VisualElement parent, string label, string tooltip = null)
        {
            var row = Ui.Row(parent);
            // Translated before Note, so a search in the player's language finds it.
            string shown = Words.Show(label, Host.For(row), false);
            row.style.minHeight = RowHeight;
            row.style.marginBottom = 2f;
            row.userData = shown;          // how a search result finds this row again
            SearchIndex.Note(shown);
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 4f;
            Words.Tip(row, tooltip);

            var l = Ui.Text(label, row);
            l.style.flexGrow = 1f;
            l.style.flexShrink = 1f;
            // A long label gives way (ellipsis, one line) before the control does: without minWidth 0
            // the text's own width is the floor and it pushes the switch out of the window.
            l.style.minWidth = 0f;
            l.pickingMode = PickingMode.Ignore;

            Paint.Look(row, Look.Row);

            var right = Ui.Row(row);
            right.style.flexShrink = 0f;
            right.name = "control";

            /* Every row reserves the arrow's width, filled or not. Without it the switches on rows
               that have a page behind them sit one arrow to the left of the ones that do not, and a
               column of toggles reads as ragged. */
            var arrow = Ui.Text("", row, ArrowSize, Role.TextMuted);
            arrow.name = "arrow";
            arrow.style.width = ArrowSlot;
            arrow.style.unityTextAlign = TextAnchor.MiddleRight;
            arrow.style.flexShrink = 0f;
            arrow.pickingMode = PickingMode.Ignore;
            return right;
        }

        /* A toggle and the rows it governs. Showing controls that the mod is currently ignoring is
           the single most common way these settings screens confuse people — the accent picker
           under a disabled "use a custom colour" was one, and Hide UI has several more. The body is
           only built once; the toggle shows and hides it. */
        public static VisualElement ToggleGroup(VisualElement parent, string label, bool value,
                                                Action<bool> onChange, string tooltip = null)
        {
            var body = Ui.Box(parent);
            Controls.Toggle(parent, label, Bind.Value(value, v =>
            {
                body.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
                if (onChange != null) onChange(v);
            }), o => o.Tooltip = tooltip);
            // Built before the toggle so the toggle reads above it, then moved back under it.
            parent.Remove(body);
            parent.Add(body);
            body.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            body.style.paddingLeft = Tokens.Pad;
            return body;
        }

        /* A section that folds away. Long settings pages are mostly headings you are not reading;
           this is the one piece of UIBuilder's ExpandSection/Collapsible pair worth keeping, and
           the state is remembered per title so a page does not refold on every rebuild. */
        private static readonly Dictionary<string, bool> _folded = new Dictionary<string, bool>();

        public static VisualElement Section(VisualElement parent, string title, bool defaultOpen = true)
        {
            SearchIndex.CurrentGroup = title;
            bool open = _folded.ContainsKey(title) ? _folded[title] : defaultOpen;

            var head = Ui.Row(parent);
            head.style.marginTop = Tokens.Pad;
            head.pickingMode = PickingMode.Position;
            var arrow = Ui.Muted(open ? "▾" : "▸", head);
            arrow.style.width = 18f;
            arrow.style.fontSize = Tokens.FontSize + 3f;   // it was a speck at body size
            var l = Ui.Text(title, head, Tokens.FontSize);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            Paint.Fg(l, Role.TextMuted);
            l.pickingMode = PickingMode.Ignore;

            var body = Ui.Box(parent);
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            body.style.marginBottom = Tokens.Gap;

            Events.On<ClickEvent>(head, _ =>
            {
                open = !open;
                _folded[title] = open;
                arrow.text = open ? "▾" : "▸";
                if (open) Anim.SlideIn(body, -6f, Anim.Fast);
                else body.style.display = DisplayStyle.None;
            });
            return body;
        }

        /* A list entry: a name, an optional note under it, and buttons on the right.

           Profiles, font packs and the key viewer's rows are all this shape, and each of them had
           grown its own layout. Not a settings ROW — those are label-plus-one-control — this is one
           THING with actions, which is why it gets a taller line and a second text style. */
        public static VisualElement Item(VisualElement parent, string title, string note = null)
        {
            var row = Ui.Row(parent);
            row.style.minHeight = 38f;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 4f;
            row.style.marginBottom = 2f;
            Ui.SetRadius(row, Tokens.Radius);
            Paint.Bg(row, Role.Row);

            var text = Ui.Box(row);
            text.style.flexGrow = 1f;
            var t = Ui.Text(title, text);
            t.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(note))
            {
                var n = Ui.Muted(note, text);
                n.pickingMode = PickingMode.Ignore;
            }

            var actions = Ui.Row(row);
            actions.style.flexShrink = 0f;
            actions.name = "actions";
            return actions;
        }

        /// Marks a list entry as the active one, the way a profile list shows which is loaded.
        public static void MarkActive(VisualElement actions, string label = null)
        {
            var tag = Ui.Text(label ?? "Active", actions, Tokens.FontSizeSmall, Role.Accent, label == null);
            tag.style.marginRight = Tokens.Gap;
            tag.SendToBack();
        }

        /* A row that opens a page of its own. This is what the mods' menus use for anything with
           more than one setting behind it — a stat's colours, a key's layout — and it belongs to
           the row rather than to the rail, which is why it pushes instead of nesting. */
        public static VisualElement SubPage(VisualElement parent, string label,
                                            Action<VisualElement> build, string tooltip = null)
        {
            var host = Row(parent, label, tooltip);
            var row = host.parent;
            Arrow(row);

            /* Captured HERE, not read inside the handler: Nav.Active is only set while a page is
               being built, so by the time anyone clicks it is null and nothing happened. That is
               why the subpages could not be opened. */
            var nav = Nav.Active;
            row.pickingMode = PickingMode.Position;
            Events.On<ClickEvent>(row, _ =>
            {
                if (nav != null) nav.Push(label, build);
                else Host.For(row).Log("SubPage '" + label + "': no Nav was building this page");
            });
            return host;
        }

        /* A switch AND a page behind it, on one row.

           The alternative — a toggle row followed by an "X options" row — says the thing's name
           twice and doubles the length of every list it appears in. The arrow marks the row as
           having more behind it; the switch still switches, because a click that lands on it must
           not navigate away. */
        public static VisualElement ToggleSubPage(VisualElement parent, string label, bool value,
                                                  Action<bool> onChange, Action<VisualElement> build,
                                                  string tooltip = null)
        {
            var c = Controls.Toggle(parent, label, Bind.Value(value, onChange), o => o.Tooltip = tooltip);
            var row = c.Root;
            c.Input.pickingMode = PickingMode.Position;
            Arrow(row);

            var nav = Nav.Active;   // see SubPage: Active is null by the time a click arrives
            row.pickingMode = PickingMode.Position;
            Events.On<ClickEvent>(row, _ =>
            {
                // The switch stops its own clicks; anything else on the row opens the page.
                if (nav != null && build != null) nav.Push(label, build);
            });
            return row;
        }

        /// Fill the arrow slot Row() reserved.
        private static void Arrow(VisualElement row)
        {
            var arrow = row.Q<Label>("arrow");
            if (arrow != null) arrow.text = "\u203A";
        }

        public static Label Header(VisualElement parent, string text)
        {
            var l = Ui.Text(text, parent, Tokens.FontSize);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            Paint.Fg(l, Role.TextMuted);
            l.style.marginTop = Tokens.Pad;
            l.style.marginBottom = Tokens.Gap;
            l.style.flexShrink = 0f;
            return l;
        }

        public static VisualElement Spacer(VisualElement parent, float height = 8f)
        {
            var e = Ui.Box(parent);
            e.style.height = height;
            e.style.flexShrink = 0f;
            return e;
        }
    }
}
