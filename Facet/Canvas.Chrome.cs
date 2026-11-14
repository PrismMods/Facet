using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facet
{
    /* The canvas's chrome: outlines, handles, guides, the marquee and the grid, painted by ONE element over Content
       through generateVisualContent, whatever the selection's size, plus the zoom badge inside it. Redraw reads the
       rects into reused lists; Draw paints only those, in stage px, so the outline and handles keep their size at any
       zoom. Colours are the roles of the theme the chrome wears when it paints (Theme.For), and Paint repaints it on
       every re-theme, so a replaced or recoloured theme shows without a rebuild. */
    public sealed partial class Canvas<T>
    {
        private readonly VisualElement _chrome;
        private readonly Label _badge;
        private readonly float _handleSize, _edgeLong, _edgeShort, _hitSize, _outline, _line;
        private readonly List<Rect> _outlines = new List<Rect>(), _handles = new List<Rect>(8);
        private Vector2 _guides = new Vector2(float.NaN, float.NaN);
        private Rect _band;
        private bool _banding;
        private string _badgeText;
        private int _badgePct = -1;
        private Color _drawnAccent;

        // The selection's rects, and the eight handles of a lone resizable item (reading order: TL, T, TR, L, R, BL, B, BR).
        private void Redraw()
        {
            _outlines.Clear();
            _handles.Clear();
            for (int i = 0; i < _sel.Count; i++)
            {
                Rect r;
                if (TryRect(_sel[i], out r)) _outlines.Add(ToStage(r));
            }
            if (_sel.Count == 1 && _outlines.Count == 1 && Is(_resizable, _sel[0], _siteResizable))
            {
                var o = _outlines[0];
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        if (x == 1 && y == 1) continue;
                        bool corner = x != 1 && y != 1;
                        float w = corner ? _handleSize : x == 1 ? _edgeLong : _edgeShort;
                        float h = corner ? _handleSize : y == 1 ? _edgeLong : _edgeShort;
                        float cx = o.x + o.width * 0.5f * x, cy = o.y + o.height * 0.5f * y;
                        _handles.Add(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h));
                    }
            }
            _chrome.MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext mgc)
        {
            var t = Theme.For(_chrome);
            var p = mgc.painter2D;
            var area = _chrome.contentRect;
            Color accent = _drawnAccent = t[Role.Accent];

            if (_showGrid) Grid(p, area, t[Role.Line]);

            if (_outlines.Count > 0)
            {
                // Outside each rect: the stroke is centred on a rect grown by half its width.
                float g = _outline * 0.5f;
                p.lineWidth = _outline;
                p.strokeColor = accent;
                p.BeginPath();
                for (int i = 0; i < _outlines.Count; i++)
                {
                    var r = _outlines[i];
                    Path(p, new Rect(r.x - g, r.y - g, r.width + _outline, r.height + _outline));
                }
                p.Stroke();
            }

            if (_showGuides && (!float.IsNaN(_guides.x) || !float.IsNaN(_guides.y)))
            {
                p.lineWidth = _line;
                p.strokeColor = t[Role.Guide];
                p.BeginPath();
                if (!float.IsNaN(_guides.x))
                {
                    float x = _guides.x * _zoom + _pan.x;
                    p.MoveTo(new Vector2(x, area.yMin)); p.LineTo(new Vector2(x, area.yMax));
                }
                if (!float.IsNaN(_guides.y))
                {
                    float y = _guides.y * _zoom + _pan.y;
                    p.MoveTo(new Vector2(area.xMin, y)); p.LineTo(new Vector2(area.xMax, y));
                }
                p.Stroke();
            }

            if (_banding)
            {
                var fill = accent;
                fill.a *= 0.15f;
                p.fillColor = fill;
                p.BeginPath(); Path(p, _band); p.Fill();
                p.lineWidth = _line;
                p.strokeColor = accent;
                p.BeginPath(); Path(p, _band); p.Stroke();
            }

            if (_handles.Count > 0)
            {
                p.fillColor = t[Role.Panel];
                p.BeginPath();
                for (int i = 0; i < _handles.Count; i++) Path(p, _handles[i]);
                p.Fill();
                p.lineWidth = _line;
                p.strokeColor = accent;
                p.BeginPath();
                for (int i = 0; i < _handles.Count; i++) Path(p, _handles[i]);
                p.Stroke();
            }
        }

        // The grid over the logical canvas where the stage shows it; none while GridPitch says 0 (no grid).
        private void Grid(Painter2D p, Rect area, Color line)
        {
            float pitch = CanvasMath.GridPitch(_grid, _zoom) * _zoom;
            if (!(pitch > 0f)) return;
            float x0 = Mathf.Max(area.xMin, _pan.x), x1 = Mathf.Min(area.xMax, _pan.x + _size.x * _zoom);
            float y0 = Mathf.Max(area.yMin, _pan.y), y1 = Mathf.Min(area.yMax, _pan.y + _size.y * _zoom);
            if (!(x1 > x0) || !(y1 > y0)) return;
            p.lineWidth = _line;
            p.strokeColor = line;
            p.BeginPath();
            for (float x = _pan.x + Mathf.Ceil((x0 - _pan.x) / pitch) * pitch; x <= x1; x += pitch) { p.MoveTo(new Vector2(x, y0)); p.LineTo(new Vector2(x, y1)); }
            for (float y = _pan.y + Mathf.Ceil((y0 - _pan.y) / pitch) * pitch; y <= y1; y += pitch) { p.MoveTo(new Vector2(x0, y)); p.LineTo(new Vector2(x1, y)); }
            p.Stroke();
        }

        private static void Path(Painter2D p, Rect r)
        {
            p.MoveTo(new Vector2(r.xMin, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax));
            p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
        }

        // The zoom badge: a pill at the stage's bottom left, inside the chrome so the stage's children stay two.
        private static Label Badge(VisualElement chrome, Sizes sz)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.style.position = Position.Absolute;
            l.style.left = sz.Gap; l.style.bottom = sz.Gap;
            l.style.fontSize = sz.Small;
            l.style.paddingLeft = sz.ControlPad * 2f; l.style.paddingRight = sz.ControlPad * 2f;
            l.style.paddingTop = sz.FieldPad / 2f; l.style.paddingBottom = sz.FieldPad / 2f;
            Ui.SetRadius(l, sz.Small);
            Paint.Bg(l, Role.Panel);
            Paint.Fg(l, Role.TextMuted);
            chrome.Add(l);
            return l;
        }
    }
}
