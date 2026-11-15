using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet.Mod
{
    // The Lists page of the Parts window: image cards over 300 items whose images arrive late.
    public static class GalleryLists
    {
        public sealed class Item { public int N; public string Name; }

        public static readonly List<Item> Items = new List<Item>();
        /// Whether the cards wrap into a grid.
        public static bool Grid;
        /// Null until "Images" hands them in.
        public static Texture2D[] Images;
        /// The cards, as last built.
        public static ImageCards<Item> Cards { get; private set; }

        internal static void Build(VisualElement p)
        {
            if (Items.Count == 0) for (int i = 1; i <= 300; i++) Items.Add(new Item { N = i, Name = "Item " + i });
            Controls.Toggle(p, "Grid", Bind.To(() => Grid, v => { Grid = v; if (Cards != null) Cards.Refresh(); }), o => o.Id = "lists-grid");
            Controls.Action(p, "Images", "Hand in", () => { MakeImages(p); if (Cards != null) Cards.ImagesChanged(); }, o => o.Id = "lists-images");
            Cards = Controls.ImageCards<Item>(p, o =>
            {
                o.Items = () => Items;
                o.Key = it => it.Name;
                o.Grid = () => Grid;
                o.Image = it => Images == null ? null : Images[(it.N - 1) % Images.Length];
                o.Body = (e, it) =>
                {
                    Ui.Text(e, "").text = it.Name;   // data, not a word
                    e.style.height = 72f;
                };
            });
        }

        // Gradients between two of the theme's roles, made once.
        static void MakeImages(VisualElement p)
        {
            if (Images != null) return;
            var th = Host.For(p).Theme;
            Color a = th[Role.Accent], b = th[Role.Panel];
            Images = new Texture2D[12];
            for (int k = 0; k < Images.Length; k++)
            {
                var t = new Texture2D(32, 8) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color[32 * 8];
                for (int x = 0; x < 32; x++)
                {
                    var c = Color.Lerp(a, b, (x + k * 2f) / (32f + 24f));
                    for (int y = 0; y < 8; y++) px[y * 32 + x] = c;
                }
                t.SetPixels(px);
                t.Apply();
                Images[k] = t;
            }
        }
    }
}
