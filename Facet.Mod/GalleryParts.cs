using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // Plain data the Parts window's pages write into.
    public sealed class PartsModel
    {
        public bool Glow;
        public float Size = 1f;
        public int Style;
        public bool Extra;
        public float Depth = 0.5f;
        /// The Look card's note reads On + " / 3"; All sets it to 3, None to 0.
        public int On;
        public int AllRuns;
        /// VisibleWhen of the Depth row and of the whole Look card.
        public bool HideDepth, HideCard;
        /// The Feature page's header switch and its second header's description.
        public bool Feature;
        public string Desc = "one";
        /// The Feature page's preview: how often each callback ran.
        public int Shows, Hides, Ticks, Links;
    }

    // The parts window: one Nav page per part this plan adds, on the Gallery's host.
    public static class GalleryParts
    {
        public static Surface Surface { get; private set; }
        public static Nav Nav { get; private set; }
        /// Replaced wholesale by the autotest, like Gallery.Model.
        public static PartsModel Model { get; set; } = new PartsModel();
        static Window _win;
        /// The Feature page, for the autotest to go to.
        public static NavItem FeatureItem { get; private set; }
        public static NavItem CardsItem { get; private set; }

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Gallery.Host, "FacetGalleryParts", Layer.Window);
            _win = new Window(Surface.Root, "parts", "Facet Parts", new Rect(100, 60, 960, 600));
            _win.CloseRequested += _ => Close();
            Nav = new Nav(_win.Body, new[] { CardsItem = new NavItem("Cards", Cards), FeatureItem = new NavItem("Feature", Feature) });
            return _win;
        }

        static void Cards(VisualElement p)
        {
            var card = Controls.Card(p, "Look", o =>
            {
                o.Id = "card-look";
                o.Actions = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("All", () => { Model.AllRuns++; Model.On = 3; }),
                    new KeyValuePair<string, Action>("None", () => Model.On = 0),
                };
                o.Note = () => Model.On + " / 3";
                o.VisibleWhen = () => !Model.HideCard;
            });
            Controls.Toggle(card, "Glow", Bind.To(() => Model.Glow, v => Model.Glow = v), o => { o.Id = "look-glow"; o.Help = "Adds a soft halo"; });
            Controls.Slider(card, "Size", Bind.To(() => Model.Size, v => Model.Size = v), o => { o.Id = "look-size"; o.Min = 0.5f; o.Max = 2f; });
            Controls.Dropdown(card, "Style", Bind.To(() => Model.Style, v => Model.Style = v), o => { o.Id = "look-style"; o.Items = new[] { "Flat", "Raised" }; });
            var adv = Controls.Advanced(card);
            Controls.Toggle(adv, "Extra", Bind.To(() => Model.Extra, v => Model.Extra = v), o => o.Id = "look-extra");
            Controls.Slider(adv, "Depth", Bind.To(() => Model.Depth, v => Model.Depth = v), o => { o.Id = "look-depth"; o.VisibleWhen = () => !Model.HideDepth; });
            // Review I2: a second card sharing the label "Size", far enough down that only a right reveal shows it.
            Ui.Box(p).style.height = 900f;
            var other = Controls.Card(p, "Other");
            Controls.Slider(other, "Size", Bind.To(() => Model.Size, v => Model.Size = v), o => { o.Id = "other-size"; o.Min = 0.5f; o.Max = 2f; });
        }

        // Task 9: a page header with the feature's switch, then two columns with a sticky right pane.
        static void Feature(VisualElement p)
        {
            Controls.PageHeader(p, "Judgement counter", Bind.To(() => Model.Feature, v => Model.Feature = v), o => { o.Id = "head"; o.Description = "Counts each judgement"; });
            Controls.PageHeader(p, "Other feature", null, o => { o.Id = "head2"; o.DescriptionText = () => Model.Desc; });
            var left = Controls.Columns(p, out var right);
            left.name = "feat-left"; right.name = "feat-right";
            var card = Controls.Card(left, "Rows");
            for (int i = 0; i < 30; i++) Controls.Toggle(card, "Row " + i, Bind.To(() => false, v => { }));
            var tick = Ui.Text("", null, null, Role.Text, true);
            Controls.Preview(right, "Preview", o =>
            {
                o.Id = "feat-preview";
                o.Note = "Live view";
                o.Link = "Edit layout →";
                o.OnLink = () => Model.Links++;
                o.Show = st => { Model.Shows++; st.Add(tick); };
                o.Tick = st => { Model.Ticks++; tick.text = Model.Ticks.ToString(); };
                o.Hide = () => Model.Hides++;
            });
            var box = Ui.Box(right);
            box.name = "feat-box";
            box.style.height = 200f;
        }

        public static void Close()
        {
            if (Surface == null) return;
            Surface.Dispose();
            Surface = null; Nav = null; _win = null;
        }
    }
}
