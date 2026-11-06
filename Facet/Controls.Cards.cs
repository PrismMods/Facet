using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Facet
{
    public sealed class CardOptions : ControlOptions
    {
        /// Header links ("All", "None"), English keys; a click runs the action.
        public List<KeyValuePair<string, Action>> Actions { get; set; }

        /// Muted header text before the actions, re-read on every refresh (e.g. "8 / 8"); shown untranslated.
        public Func<string> Note { get; set; }
    }

    /* Cards: a titled group of rows, split by rules, with an optional Advanced fold at the bottom. */
    public static partial class Controls
    {
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
                if (o.Note != null)
                {
                    var note = Ui.Text("", head, sz.Small, Role.TextMuted);
                    note.style.marginRight = sz.Gap;
                    // A Control of its own: the refresher re-reads it, and a throwing Note dims only the note.
                    new Control<string>(note, null, title + " note", Bind.To(o.Note, null), s => note.text = s ?? "", new ControlOptions());
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

            var body = Ui.Box(root);
            Data.Get<CardRows>(body).Title = title;
            Hook(root, null, title, o);
            return body;
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
            if (rows != null) { var r = Data.Get<CardRows>(body); r.Rows = 1; r.Title = title; }

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
