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
    }

    // The parts window: one Nav page per part this plan adds, on the Gallery's host.
    public static class GalleryParts
    {
        public static Surface Surface { get; private set; }
        public static Nav Nav { get; private set; }
        /// Replaced wholesale by the autotest, like Gallery.Model.
        public static PartsModel Model { get; set; } = new PartsModel();
        static Window _win;

        public static Window Open()
        {
            if (Surface != null) { Surface.Visible = true; return _win; }
            Surface = new Surface(Gallery.Host, "FacetGalleryParts", Layer.Window);
            _win = new Window(Surface.Root, "parts", "Facet Parts", new Rect(160, 60, 760, 600));
            _win.CloseRequested += _ => Close();
            Nav = new Nav(_win.Body, new[] { new NavItem("Cards", Cards) });
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

        public static void Close()
        {
            if (Surface == null) return;
            Surface.Dispose();
            Surface = null; Nav = null; _win = null;
        }
    }
}
