using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The structure rows: the row every control sits in, headings, folding sections, a group that
       follows a switch, and rows with a page behind them. */
    public static partial class Controls
    {
        public const float RowHeight = 32f;
        private const float ArrowSlot = 22f;
        private const float ArrowSize = 20f;   // the chevron was a speck at body size

        /// What Noted records on a row, through Data (element userData is the consumer's): the English
        /// key a search hit reveals it by.
        internal sealed class Noting { public string Key; }

        /* Makes `row` findable: the index notes the label as shown (translated, so a search in the
           player's language finds it) and its English key, which is also how a result finds the row
           again. Every row that is a setting by itself, not by way of Row, calls this. */
        internal static void Noted(VisualElement row, string label)
        {
            Data.Get<Noting>(row).Key = label;
            SearchIndex.Note(label, Words.Show(label, Host.For(row), false));
        }

        /// Label on the left, whatever the caller adds on the right.
        public static VisualElement Row(VisualElement parent, string label, string tooltip = null)
        {
            var row = Ui.Row(parent);
            Noted(row, label);
            row.style.minHeight = RowHeight;
            row.style.marginBottom = 2f;
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
           the single most common way these settings screens confuse people. The body is built once
           and follows the bound value on every refresh, not only on clicks. */
        public static VisualElement ToggleGroup(VisualElement parent, string label, Bind<bool> bind, Action<Options<bool>> configure = null)
        {
            var c = Toggle(parent, label, bind, configure);
            var body = Ui.Box(parent);
            body.style.paddingLeft = Tokens.Pad;
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
            SearchIndex.EnterSection(title);
            var nav = Nav.Active;
            string key = "fold." + (nav != null ? nav.PageTitle : "") + "/" + title;
            var host = Host.For(parent);
            string saved = host.Read(key);
            // A host with no Read/Write still keeps folds for the session.
            string mem = host.Name + "|" + key;
            bool open = saved == "1" ? true : saved == "0" ? false : _folds.TryGetValue(mem, out var m) ? m : defaultOpen;

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

            // One path for a click and for a search hit revealing the row: state, host and look stay in step.
            Action<bool, bool> fold = (to, animate) =>
            {
                open = to;
                _folds[mem] = open;
                Host.For(head).Write(key, open ? "1" : "0");
                arrow.text = open ? "▾" : "▸";
                if (!open) body.style.display = DisplayStyle.None;
                else if (animate) Anim.SlideIn(body, -6f, Anim.Fast);
                else body.style.display = DisplayStyle.Flex;
            };
            Data.Get<Fold>(body).Title = title;
            Data.Get<Fold>(body).Set = fold;
            Data.Get<Fold>(body).IsOpen = () => open;
            Events.On<ClickEvent>(head, _ => fold(!open, true));
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

        /* A row that opens a page of its own. It belongs to the row rather than to the rail, which is
           why it pushes instead of nesting. Id and Tooltip of the options apply. */
        public static VisualElement SubPage(VisualElement parent, string label,
                                            Action<VisualElement> build, Action<ControlOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, label, configure);
            var host = Row(parent, label, o.Tooltip);
            var row = host.parent;
            if (o.Id != null) row.name = o.Id;
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
