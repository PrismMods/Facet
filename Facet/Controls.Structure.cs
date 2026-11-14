using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The structure rows: the row every control sits in, headings, folding sections, a group that
       follows a switch, and rows with a page behind them. */
    /// Controls.Section's options: Open is how it starts before the reader folds it.
    public sealed class SectionOptions : ControlOptions
    {
        public bool Open { get; set; } = true;
    }

    public static partial class Controls
    {
        /// Facet's default setting-row height; a part reads its theme's Sizes.RowHeight.
        public const float RowHeight = 32f;
        private const float ArrowSlot = 22f;
        private const float ArrowSize = 20f;   // the chevron was a speck at body size

        /// What Noted records on a row, through Data (element userData is the consumer's): the English
        /// key a search hit reveals it by, the index entry its tooltip joins, and the label a help line goes under.
        internal sealed class Noting { public string Key; public SearchIndex.Hit Hit; public Label Label; }

        /* Makes `row` findable: the index notes the label as shown (translated, so a search in the
           player's language finds it) and its English key, which is also how a result finds the row
           again. Every row that is a setting by itself, not by way of Row, calls this. */
        internal static void Noted(VisualElement row, string label, string tooltip = null)
        {
            var n = Data.Get<Noting>(row);
            n.Key = label;
            n.Hit = SearchIndex.Note(label, Words.Show(label, Host.For(row), false));
            NotedTip(row, tooltip);
        }

        /// The row's tooltip and help line join its index entry, English and as shown.
        internal static void NotedTip(VisualElement row, string tooltip, string help = null)
        {
            var n = Data.Find<Noting>(row);
            if (n == null || n.Hit == null) return;
            var h = Host.For(row);
            string key = null, shown = null;
            foreach (var s in new[] { tooltip, help })
            {
                if (string.IsNullOrEmpty(s)) continue;
                var w = Words.Show(s, h, false);
                key = key == null ? s : key + " " + s;
                shown = shown == null ? w : shown + " " + w;
            }
            if (key != null) SearchIndex.NoteTip(n.Hit, key, shown);
        }

        /* The row's help (ControlOptions.Help): a muted Small line under its label, translated like it.
           The label moves into a column with the help under it, so the row grows to hold both. A row
           with no label of its own (a DangerButton) has nothing to put it under. */
        internal static void Helped(VisualElement row, ControlOptions o)
        {
            if (string.IsNullOrEmpty(o.Help)) return;
            NotedTip(row, o.Tooltip, o.Help);
            var n = Data.Find<Noting>(row);
            var l = n == null ? null : n.Label;
            if (l == null || l.parent == null) return;
            var col = Ui.Box();
            l.parent.Insert(l.parent.IndexOf(l), col);
            Spaced(col, Theme.For(row).Sizes);   // the column, label and help together, stands off the row's edges
            l.style.marginTop = 0f; l.style.marginBottom = 0f;
            col.style.flexGrow = 1f;
            col.style.flexShrink = 1f;
            col.style.minWidth = 0f;
            col.pickingMode = PickingMode.Ignore;
            col.Add(l);
            var help = Ui.Muted(col, o.Help);
            help.name = "help";
            help.style.whiteSpace = WhiteSpace.Normal;
            help.pickingMode = PickingMode.Ignore;
        }

        /// Sizes.RowPadY above and below a row's text: one line still fits RowHeight, and lines that wrap
        /// stand off the row's top and bottom instead of touching them.
        internal static void Spaced(VisualElement text, Sizes sz)
        {
            text.style.marginTop = sz.RowPadY;
            text.style.marginBottom = sz.RowPadY;
        }

        /// Marks a card's body (and an Advanced fold's): its rows take the card rules. Title is the card's;
        /// Card is the card's own body, for an Advanced fold's (null on the card's body itself).
        internal sealed class CardRows { public int Rows; public string Title; public VisualElement Card; }

        /* In a card, a row pads Sizes.Pad on both sides and every row after the first draws a
           Sizes.Line rule above it in Looks.Rule's colour (its Bg), as the card's header rule is; an
           invisible Rule (Dark) draws none. An Advanced body starts at one: its head comes first.
           True when the row is in a card. */
        internal static bool Carded(VisualElement row, Sizes sz)
        {
            CardRows c = null;   // the NEAREST card body above: a wrapper, a ToggleGroup body or a nested Section still counts
            for (var e = row.parent; e != null && c == null; e = e.parent) c = Data.Find<CardRows>(e);
            if (c == null) return false;
            row.style.paddingLeft = sz.Pad;
            row.style.paddingRight = sz.Pad;
            row.style.marginBottom = 0f;
            if (c.Rows++ == 0) return true;
            var rule = Theme.For(row).Looks.Rule;
            if (rule == null || rule.Bg == null) return true;
            row.style.borderTopWidth = sz.Line;
            Paint.Border(row, rule.Bg.Value);
            return true;
        }

        /// Label on the left, whatever the caller adds on the right.
        public static VisualElement Row(VisualElement parent, string label, string tooltip = null)
        {
            var row = Ui.Row(parent);
            Noted(row, label, tooltip);
            var t = Theme.For(row); var sz = t.Sizes;
            row.style.minHeight = sz.RowHeight;
            row.style.marginBottom = 2f;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 4f;
            Carded(row, sz);
            Words.Tip(row, tooltip);

            var l = Ui.Text(row, label, sz.Body);
            l.style.flexGrow = 1f;
            l.style.flexShrink = 1f;
            // A long label gives way (ellipsis, one line) before the control does: without minWidth 0
            // the text's own width is the floor and it pushes the switch out of the window.
            l.style.minWidth = 0f;
            Spaced(l, sz);
            l.pickingMode = PickingMode.Ignore;
            Data.Get<Noting>(row).Label = l;

            Paint.Look(row, t.Looks.Row);

            var right = Ui.Row(row);
            right.style.flexShrink = 0f;
            right.name = "control";
            AddArrowSlot(row);
            return right;
        }

        /// Marks a container one of whose rows opens a page (Arrow): every row in it reserves the arrow slot.
        internal sealed class Arrowed { }

        /* Where the rows that share an arrow slot live: a card's whole body, so a row in its ToggleGroup,
           Section or Advanced body lines up with the rows beside it; outside a card, the row's container. */
        private static VisualElement ArrowBox(VisualElement row)
        {
            for (var e = row.parent; e != null; e = e.parent)
            {
                var c = Data.Find<CardRows>(e);
                if (c != null) return c.Card ?? e;
            }
            return row.parent;
        }

        /* The arrow's place, the row's last child. Shown only in a container where some row opens a
           page, so the switches beside that row line up with its own instead of sitting one arrow to
           the right; anywhere else an empty slot would only widen the right gutter past the left. */
        internal static void AddArrowSlot(VisualElement row)
        {
            var arrow = Ui.Text(row, "", ArrowSize, Role.TextMuted);
            arrow.name = "arrow";
            arrow.style.width = ArrowSlot;
            arrow.style.unityTextAlign = TextAnchor.MiddleRight;
            arrow.style.flexShrink = 0f;
            arrow.pickingMode = PickingMode.Ignore;
            arrow.style.display = Data.Find<Arrowed>(ArrowBox(row)) != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /* A toggle and the rows it governs. Showing controls that the mod is currently ignoring is
           the single most common way these settings screens confuse people. The body is built once
           and follows the bound value on every refresh, not only on clicks. */
        public static VisualElement ToggleGroup(VisualElement parent, string label, Bind<bool> bind, Action<Options<bool>> configure = null)
        {
            Control<bool> toggle;
            return ToggleGroup(parent, label, bind, out toggle, configure);
        }

        /// The same, handing back the switch (its Changed, Committed and Value) beside the body it governs.
        public static VisualElement ToggleGroup(VisualElement parent, string label, Bind<bool> bind, out Control<bool> toggle, Action<Options<bool>> configure = null)
        {
            var c = toggle = Toggle(parent, label, bind, configure);
            var body = Ui.Box(parent);
            body.style.paddingLeft = Theme.For(body).Sizes.Pad;
            Action<bool> follow = v => body.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
            c.OnShown = follow;
            follow(c.Value);
            return body;
        }

        /* A section that folds away. The state is kept through the host under
           "fold.<page>/<title>" as "0"/"1", so a page does not refold on every rebuild or restart. */
        private static readonly System.Collections.Generic.Dictionary<string, bool> _folds = new System.Collections.Generic.Dictionary<string, bool>();

        /// How a Section body is folded from outside (Nav's reveal), set by Section.
        internal sealed class Fold { public string Title; public Action<bool, bool> Set; public Func<bool> IsOpen; }

        public static VisualElement Section(VisualElement parent, string title, bool defaultOpen = true)
        {
            return Section(parent, title, defaultOpen, null);
        }

        /* The same with options: Open is the state before the reader first folds it; Id names the header row and
           the fold's stored key in place of the title (two sections with one title fold apart); VisibleWhen hides
           header and body together; Tooltip, EnabledWhen and ContextMenu go on the header. */
        public static VisualElement Section(VisualElement parent, string title, Action<SectionOptions> configure)
        {
            var o = ControlOptions.Make(parent, title, configure);
            return Section(parent, title, o.Open, o);
        }

        private static VisualElement Section(VisualElement parent, string title, bool defaultOpen, SectionOptions o)
        {
            SearchIndex.EnterSection(title);
            var head = Ui.Row(parent);
            var sz = Theme.For(head).Sizes;
            head.style.marginTop = sz.Pad;
            head.pickingMode = PickingMode.Position;
            Carded(head, sz);
            var arrow = Ui.Text(head, "", sz.Body + 3f, Role.TextMuted);   // it was a speck at body size
            arrow.style.width = 18f;
            var l = Ui.Text(head, title, sz.Body);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            Paint.Fg(l, Role.TextMuted);
            l.pickingMode = PickingMode.Ignore;

            var body = Ui.Box(parent);
            body.style.marginBottom = sz.Gap;
            Folds(head, body, o != null && o.Id != null ? o.Id : title, title, defaultOpen, open => arrow.text = open ? "▾" : "▸");
            if (o == null) return body;
            var c = Hook(head, null, title, o);
            var fold = Data.Get<Fold>(body);
            Action<bool> follow = v => body.style.display = v && fold.IsOpen() ? DisplayStyle.Flex : DisplayStyle.None;
            c.OnVisible = follow;
            if (head.style.display == DisplayStyle.None) follow(false);   // hidden by the first VisibleWhen, before OnVisible was set
            return body;
        }

        /* `head` folds `body`. The state is kept through the host under "fold.<page>/<path>" as
           "0"/"1"; a host with no Read/Write still keeps it for the session. The Fold record is how a
           search hit unfolds it (its Title is the search section the hit names); drawn shows the state. */
        private static void Folds(VisualElement head, VisualElement body, string path, string title, bool defaultOpen, Action<bool> drawn)
        {
            var nav = Nav.Active;
            string key = "fold." + (nav != null ? nav.PageTitle : "") + "/" + path;
            var host = Host.For(head);
            string saved = host.Read(key);
            string mem = host.Name + "|" + key;
            bool open = saved == "1" ? true : saved == "0" ? false : _folds.TryGetValue(mem, out var m) ? m : defaultOpen;
            body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            drawn(open);

            // One path for a click and for a search hit revealing the row: state, host and look stay in step.
            Action<bool, bool> fold = (to, animate) =>
            {
                open = to;
                _folds[mem] = open;
                Host.For(head).Write(key, open ? "1" : "0");
                drawn(open);
                // A header hidden by VisibleWhen keeps its body hidden; showing the header shows the body as folded.
                if (!open || head.style.display == DisplayStyle.None) body.style.display = DisplayStyle.None;
                else if (animate) Anim.SlideIn(body, -6f, Anim.Fast);
                else body.style.display = DisplayStyle.Flex;
            };
            var f = Data.Get<Fold>(body);
            f.Title = title;
            f.Set = fold;
            f.IsOpen = () => open;
            Events.On<ClickEvent>(head, _ => fold(!open, true));
        }

        /* A list entry: a name, an optional note under it, and buttons on the right.

           Profiles, font packs and the key viewer's rows are all this shape, and each of them had
           grown its own layout. Not a settings ROW — those are label-plus-one-control — this is one
           THING with actions, which is why it gets a taller line and a second text style. */
        public static VisualElement Item(VisualElement parent, string title, string note = null)
        {
            var row = Ui.Row(parent);
            var sz = Theme.For(row).Sizes;
            row.style.minHeight = sz.ItemHeight;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 4f;
            row.style.marginBottom = 2f;
            Ui.SetRadius(row, sz.Radius);
            Paint.Bg(row, Role.Row);

            var text = Ui.Box(row);
            text.style.flexGrow = 1f;
            var t = Ui.Text(text, title);
            t.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(note))
            {
                var n = Ui.Muted(text, note);
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
            var sz = Theme.For(actions).Sizes;
            var tag = Ui.Text(actions, label ?? "Active", sz.Small, Role.Accent, label == null);
            tag.style.marginRight = sz.Gap;
            tag.SendToBack();
        }

        /* A row that opens a page of its own. It belongs to the row rather than to the rail, which is
           why it pushes instead of nesting. Id and Tooltip of the options apply. */
        public static VisualElement SubPage(VisualElement parent, string label,
                                            Action<VisualElement> build, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var host = Row(parent, label, o.Tooltip);
            var row = host.parent;
            if (o.Id != null) row.name = o.Id;
            Helped(row, o);
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

        /* A switch AND a page behind it, on one row. The arrow marks the row as having more behind
           it; the switch still switches, because a click that lands on it must not navigate away. */
        public static Control<bool> ToggleSubPage(VisualElement parent, string label, Bind<bool> bind,
                                                  Action<VisualElement> build, Action<Options<bool>> configure = null)
        {
            var c = Toggle(parent, label, bind, configure);
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
            return c;
        }

        /// Fill the row's arrow slot, and from now on reserve one beside every row in its card (or container).
        private static void Arrow(VisualElement row)
        {
            var box = ArrowBox(row);
            if (box != null && Data.Find<Arrowed>(box) == null)
            {
                Data.Get<Arrowed>(box);
                // Only the slots whose own box this is, by the rule AddArrowSlot reads: outside a card the box is
                // the row's container, and a card in it keeps its own decision.
                box.Query<Label>(name: "arrow").ForEach(l => { if (Slot(l.parent) == l && ArrowBox(l.parent) == box) l.style.display = DisplayStyle.Flex; });
            }
            var arrow = Slot(row);
            if (arrow != null) arrow.text = "\u203A";
        }

        // The last child, when it is the arrow slot. Not Q("arrow"): a Dropdown with no Id has an "arrow" of its own.
        private static Label Slot(VisualElement row)
        {
            int n = row.childCount;
            var l = n > 0 ? row[n - 1] as Label : null;
            return l != null && l.name == "arrow" ? l : null;
        }

        public static Label Header(VisualElement parent, string text)
        {
            var sz = Theme.For(parent).Sizes;
            var l = Ui.Text(parent, text, sz.Body);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            Paint.Fg(l, Role.TextMuted);
            l.style.marginTop = sz.Pad;
            l.style.marginBottom = sz.Gap;
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
