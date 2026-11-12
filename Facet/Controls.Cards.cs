using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /// Actions, Note and NoteText live in the header, so a card with a null title has nowhere to put
    /// them: it drops them and logs that once per host and page.
    public sealed class CardOptions : ControlOptions
    {
        /// Header links ("All", "None"), English keys; a click runs the action.
        public List<KeyValuePair<string, Action>> Actions { get; set; }

        /// An English key: muted header text before the actions, translated like the title.
        public string Note { get; set; }

        /// Re-read on every refresh and shown as returned (untranslated), e.g. "8 / 8"; wins over Note.
        public Func<string> NoteText { get; set; }
    }

    public sealed class PageHeaderOptions : Options<bool>
    {
        /// An English key: the one-line description under the title.
        public string Description { get; set; }

        /// Re-read on every refresh and shown as returned (untranslated); wins over Description.
        public Func<string> DescriptionText { get; set; }
    }

    /* Cards: a titled group of rows, split by rules, with an optional Advanced fold at the bottom. */
    public static partial class Controls
    {
        private static readonly HashSet<string> _untitled = new HashSet<string>();   // host|page already told

        /* A card. The header is the title upper-cased (Ui.Caps), then the Note, then the Actions as
           links split by a muted "·", over a rule; a null title means no header. Returns the body:
           rows built into it take the card rules (Carded). The title is the search section of its
           rows. Id, VisibleWhen, EnabledWhen and ContextMenu work as on a button row. */
        public static VisualElement Card(VisualElement parent, string title, Action<CardOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, title, configure);
            SearchIndex.EnterSection(title);
            var root = Ui.Box(parent);
            var t = Theme.For(root); var sz = t.Sizes;
            Paint.Look(root, t.Looks.Group);   // its own fill (Ruling Q16): Looks.Card is the window shell
            Ui.SetBorderWidth(root, sz.Line);
            Ui.SetRadius(root, sz.Radius);
            root.style.marginBottom = sz.Gap;

            if (title != null)
            {
                var head = Ui.Row(root);
                head.style.minHeight = sz.RowHeight;
                head.style.paddingLeft = sz.Pad;
                head.style.paddingRight = sz.Pad;
                var caps = Ui.Caps(title, head);
                caps.style.flexGrow = 1f;
                caps.style.flexShrink = 1f;
                caps.style.minWidth = 0f;
                if (o.NoteText != null || !string.IsNullOrEmpty(o.Note))
                {
                    var note = Ui.Text(o.NoteText != null ? "" : o.Note, head, sz.Small, Role.TextMuted);
                    // A Gap before the actions; with none after it, the note ends on the row-control edge.
                    if (o.Actions != null && o.Actions.Count > 0) note.style.marginRight = sz.Gap;
                    // A Control of its own: the refresher re-reads it, and a throwing NoteText dims only the note.
                    if (o.NoteText != null)
                        new Control<string>(note, null, title + " note", Bind.To(o.NoteText, null), s => note.text = s ?? "", new ControlOptions());
                }
                var acts = o.Actions;
                var lk = t.Looks;
                for (int i = 0; acts != null && i < acts.Count; i++)
                {
                    if (i > 0)
                    {
                        var dot = Ui.Text("·", head, sz.Small, Role.TextMuted, true);
                        dot.style.marginLeft = sz.Gap / 2f;
                        dot.style.marginRight = sz.Gap / 2f;
                    }
                    var link = Ui.Btn(acts[i].Key, acts[i].Value, head);
                    link.style.paddingLeft = 0f;
                    link.style.paddingRight = 0f;
                    link.style.marginRight = 0f;
                    Paint.Look(link, lk.Link);
                }
                var rule = Ui.Box(root);
                rule.style.height = sz.Line;
                Paint.Look(rule, t.Looks.Rule);
            }
            else if ((o.Actions != null && o.Actions.Count > 0) || o.NoteText != null || !string.IsNullOrEmpty(o.Note))
            {
                var h = Host.For(root); string page = Nav.Active != null ? Nav.Active.PageTitle : "";
                if (_untitled.Add(h.Name + "|" + page)) h.Log("Card on '" + page + "': Actions and Note need a title; a card with none drops them");
            }

            var body = Ui.Box(root);
            Data.Get<CardRows>(body).Title = title;
            Hook(root, null, title, o);
            return body;
        }

        /* A page's head: the title (Sizes.Title, bold) with a one-line description under it, and on the
           right the feature's on/off switch, which is the Control's Input (a Toggle that records undo).
           With `on` null the Control is over a constant false and there is no switch. It replaces the
           old first row, "Use this feature". The title is noted for search. */
        public static Control<bool> PageHeader(VisualElement parent, string title, Bind<bool> on, Action<PageHeaderOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, title, configure);
            var root = Ui.Row(parent);
            var t = Theme.For(root); var sz = t.Sizes;
            root.style.minHeight = sz.RowHeight;
            root.style.marginBottom = sz.Pad;
            root.style.paddingRight = sz.Line + sz.Pad;   // the switch ends where a card row's control does: its border, then Pad
            Noted(root, title);
            var col = Ui.Box(root);
            col.style.flexGrow = 1f;
            col.style.flexShrink = 1f;
            col.style.minWidth = 0f;
            var head = Ui.Text(title, col, sz.Title);
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (o.DescriptionText != null)
            {
                var d = Ui.Text("", col, sz.Small, Role.TextMuted);
                new Control<string>(d, null, title + " description", Bind.To(o.DescriptionText, null), s => d.text = s ?? "", new ControlOptions());
            }
            else if (!string.IsNullOrEmpty(o.Description)) Ui.Muted(o.Description, col);

            Action<bool> show = v => { };
            VisualElement track = null;
            Control<bool> c = null;
            if (on != null) track = Switch(root, out show, () => c.Set(!c.Value));
            c = new Control<bool>(root, track, title, on != null ? Tracked(parent, title, o, on) : Bind.To(() => false, null), show, o);
            return c;
        }

        /* The fold at the bottom of a card, for the rows its writer marks advanced: call it after the
           card's other rows and build those rows into what it returns. The head reads "▸ label (n)",
           n the rows in it that VisibleWhen has not hidden. Folded by default, kept through the host
           under "fold.<page>/<card title>/Advanced"; a search hit inside unfolds it. */
        public static VisualElement Advanced(VisualElement card, string label = null)
        {
            var rows = Data.Find<CardRows>(card);
            string title = rows != null ? rows.Title : null;
            var head = Ui.Row(card);
            var t = Theme.For(head); var sz = t.Sizes;
            head.style.minHeight = sz.RowHeight;
            head.pickingMode = PickingMode.Position;
            Carded(head, sz);
            Paint.Look(head, t.Looks.Row);
            var text = Ui.Text("", head, sz.Body, Role.TextMuted);
            text.pickingMode = PickingMode.Ignore;

            var body = Ui.Box(card);
            if (rows != null) { var r = Data.Get<CardRows>(body); r.Rows = 1; r.Title = title; r.Card = rows.Card ?? card; }

            bool open = false; int n = 0;
            Action say = () => text.text = (open ? "▾ " : "▸ ") + Words.Show(label ?? "Advanced", Host.For(text), label == null) + " (" + n + ")";
            Words.OnRelabel(text, say);
            Folds(head, body, (title ?? "") + "/Advanced", title, false, o => { open = o; say(); });

            Func<int> shown = () =>
            {
                int k = 0;
                for (int i = 0; i < body.childCount; i++) if (body[i].style.display != DisplayStyle.None) k++;
                return k;
            };
            /* Joins the refresher after the rows built into the body, so each pass counts after their
               VisibleWhen has run: a hidden row shows in the count on the same tick, not the next. */
            head.schedule.Execute(() => new Control<int>(text, null, "Advanced", Bind.To(shown, null), v => { n = v; say(); }, new ControlOptions()));
            return body;
        }
    }
}
