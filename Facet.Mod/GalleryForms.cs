using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // The Forms page of the Parts window: form parts a settings page needs.
    public static class GalleryForms
    {
        /// The mask the tiles flip; bit 20 belongs to no tile.
        public static int Mask = 1 << 20 | 1 << 5;
        /// The page and the grid, as last built.
        public static VisualElement Page { get; private set; }
        public static MaskTiles Tiles { get; private set; }

        internal static void Build(VisualElement p)
        {
            Page = p;
            MaskTiles grid = null;
            var card = Controls.Card(p, "Mask tiles", o =>
            {
                o.Id = "mask-card";
                o.Actions = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("All", () => grid.SetAll(true)),
                    new KeyValuePair<string, Action>("None", () => grid.SetAll(false)),
                };
            });
            var tiles = new List<MaskTile>
            {
                new MaskTile("Alpha", 0, o => { o.Id = "mask-alpha"; o.Colour = new Color(0.3f, 0.6f, 1f); }),
                new MaskTile("Bravo", 1, o => { o.Id = "mask-bravo"; o.Colour = new Color(0.3f, 0.9f, 0.4f); }),
                new MaskTile("Charlie", 2, o => o.Id = "mask-charlie"),
                new MaskTile("Delta", 5, o => { o.Id = "mask-delta"; o.Colour = new Color(1f, 0.8f, 0.2f); }),
                new MaskTile("Echo", 9, o => { o.Id = "mask-echo"; o.Colour = new Color(1f, 0.3f, 0.3f); }),
            };
            Tiles = grid = Controls.MaskTiles(card, Bind.To(() => Mask, v => Mask = v), tiles, o => { o.Id = "mask"; o.Columns = 5; });
        }
    }
}
