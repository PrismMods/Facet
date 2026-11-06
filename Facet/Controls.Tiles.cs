using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    public sealed class TilesOptions : ControlOptions
    {
        public int Columns { get; set; } = 5;
    }

    public sealed class TileOptions : Options<bool>
    {
        /// The consumer's data colour (a judgement colour): a coloured left edge, and a tint when on.
        public Color? Colour { get; set; }

        /// Run when the tile body is clicked; only the switch then toggles. Null: a click toggles.
        public Action Open { get; set; }

        /// A switch on the right. False: a click anywhere toggles.
        public bool Switch { get; set; } = true;
    }

    /* Toggle tiles in a wrapping grid: judgement choices, a Features page, a home page's quick switches. */
    public static partial class Controls
    {
        private sealed class GridWidth { public float W; }

        /* The grid. Each tile is (width - Columns * Gap) / Columns of the grid's own width, which is the
           parent's plus one Gap (a negative right margin) because each tile carries a Gap to its right. */
        public static VisualElement Tiles(VisualElement parent, Action<TilesOptions> configure = null)
        {
            var o = ControlOptions.Make(parent, "Tiles", configure);
            var grid = Ui.Box(parent);
            float gap = Theme.For(grid).Sizes.Gap;
            int cols = Math.Max(1, o.Columns);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginRight = -gap;
            Events.On<GeometryChangedEvent>(grid, e =>
            {
                float w = Mathf.Floor((grid.layout.width - cols * gap) / cols * 100f) / 100f;
                if (float.IsNaN(w) || w <= 0f) return;
                Data.Get<GridWidth>(grid).W = w;
                for (int i = 0; i < grid.childCount; i++) grid[i].style.width = w;
            });
            return grid;
        }

        public static Control<bool> Tile(VisualElement grid, string label, Bind<bool> on, Action<TileOptions> configure = null)
        {
            var o = ControlOptions.Make(grid, label, configure);
            var tile = Ui.Row(grid);
            var t = Theme.For(tile); var sz = t.Sizes; var lk = t.Looks;
            var gw = Data.Find<GridWidth>(grid);
            if (gw != null && gw.W > 0f) tile.style.width = gw.W;   // a tile added after the grid's last layout
            tile.style.height = sz.RowHeight;
            tile.style.marginRight = sz.Gap;
            tile.style.marginBottom = sz.Gap;
            tile.style.overflow = Overflow.Hidden;
            tile.focusable = true;
            tile.pickingMode = PickingMode.Position;
            Ui.SetBorderWidth(tile, sz.Line);
            Ui.SetRadius(tile, sz.Radius);
            Noted(tile, label);
            Paint.Look(tile, lk.Tile);

            // The tint and edge are the consumer's data colour: literal, so Paint.Forget keeps them off the role painter.
            VisualElement tint = null;
            if (o.Colour.HasValue)
            {
                tint = Ui.Box(tile);
                tint.style.position = Position.Absolute;
                tint.style.left = tint.style.right = tint.style.top = tint.style.bottom = 0f;
                tint.pickingMode = PickingMode.Ignore;
                Paint.Forget(tint);
                var c = o.Colour.Value; c.a = 0.18f;
                tint.style.backgroundColor = c;
            }
            var edge = Ui.Box(tile);
            edge.style.position = Position.Absolute;
            edge.style.left = edge.style.top = edge.style.bottom = 0f;
            edge.style.width = sz.Indicator;
            edge.pickingMode = PickingMode.Ignore;
            if (o.Colour.HasValue) { Paint.Forget(edge); edge.style.backgroundColor = o.Colour.Value; }
            else Paint.Look(edge, lk.Indicator);

            var l = Ui.Text(label, tile, sz.Body);
            l.style.flexGrow = 1f;
            l.style.flexShrink = 1f;
            l.style.minWidth = 0f;
            l.style.marginLeft = sz.Pad;
            l.pickingMode = PickingMode.Ignore;
            Paint.Inherit(l);   // the tile's look: muted off, Text on
            Data.Get<Noting>(tile).Label = l;

            Action<bool> sw = null;
            VisualElement track = null;
            if (o.Switch)
            {
                track = Switch(tile, out sw);
                track.focusable = false;   // one tab stop per tile: the tile answers Enter/Space
                track.style.flexShrink = 0f;
                track.style.marginLeft = sz.Gap;
                track.style.marginRight = sz.Pad;
            }
            Action<bool> show = v =>
            {
                Paint.Select(tile, v);
                if (!o.Colour.HasValue) Paint.Select(edge, v);
                if (tint != null) tint.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
                if (sw != null) sw(v);
            };
            var host = Host.For(tile);
            var ctl = new Control<bool>(tile, track, label, Tracked(grid, label, o, on), show, o);
            if (track != null) Events.On<ClickEvent>(track, e => { ctl.Set(!ctl.Value); e.StopPropagation(); });
            Action activate = () =>
            {
                if (o.Switch && o.Open != null) Safe.Run(host, "Open '" + label + "'", o.Open);
                else ctl.Set(!ctl.Value);
            };
            Events.On<ClickEvent>(tile, e => activate());
            Events.On<NavigationSubmitEvent>(tile, e => { activate(); e.StopPropagation(); });
            return ctl;
        }
    }
}
