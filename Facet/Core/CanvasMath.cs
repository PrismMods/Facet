using System;

namespace Facet
{
    /// A rectangle in canvas units, y down.
    internal struct Box
    {
        public float X, Y, W, H;
        public Box(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right { get { return X + W; } }
        public float Bottom { get { return Y + H; } }
        public float CX { get { return X + W * 0.5f; } }
        public float CY { get { return Y + H * 0.5f; } }
    }

    /// What a move snaps to: the canvas centre lines, then the edges and centre lines of Targets[0..Count).
    /// On is false while snapping is off or Alt is held. Keep is the axis a Shift drag keeps (LatchAxis): 0 both,
    /// 1 x only (y stays put), 2 y only (x stays put).
    internal struct MoveSnap { public Box[] Targets; public int Count; public float CentreX, CentreY, Distance; public int Step; public bool On; public int Keep; }

    /// What a resize snaps to once rounded to Step: other items' sizes, Widths/Heights[0..Count), within Distance.
    internal struct SizeSnap { public float[] Widths, Heights; public int Count; public float Distance, Min; public int Step; public bool On, KeepAspect; }

    /// The canvas's geometry, BCL only. A move allocates nothing: it runs every frame of a drag over the whole layout.
    internal static class CanvasMath
    {
        /// The move grid in canvas units: coarser as the view zooms out, never under one unit.
        internal static int GridStep(float grid, float zoom)
        {
            if (!(grid > 0) || !(zoom > 0)) return 1;
            return Math.Max(1, (int)R(grid / zoom));
        }

        /// The drawn grid: the grid doubled until its lines are 5 px apart on screen. 0 means draw no grid: a grid that
        /// is not a positive finite number, a zoom that is not positive, or a pitch too large for a float.
        internal static float GridPitch(float grid, float zoom)
        {
            if (!(grid > 0) || float.IsInfinity(grid) || !(zoom > 0)) return 0;
            while (grid * zoom < 5) grid *= 2;
            return float.IsInfinity(grid) ? 0 : grid;
        }

        /// The axis a Shift-drag keeps: 1 for x when it moved at least as far as y, else 2. Pass it as MoveSnap.Keep.
        internal static int LatchAxis(float rawDx, float rawDy) { return Math.Abs(rawDx) >= Math.Abs(rawDy) ? 1 : 2; }

        /// The delta for a drag of rawDx/rawDy, and the line each axis snapped to (NaN for none). The axis a Shift
        /// drag does not keep (s.Keep 1: y, 2: x) does not move and has no guide, snapping on or off.
        internal static void Move(Box primary, float rawDx, float rawDy, ref MoveSnap s, out float dx, out float dy, out float guideX, out float guideY)
        {
            if (!s.On) { dx = R(rawDx); dy = R(rawDy); guideX = guideY = float.NaN; }
            else
            {
                int n = s.Targets == null ? 0 : Math.Min(s.Count, s.Targets.Length), step = s.Step > 0 ? s.Step : 1;
                dx = Axis(primary.X, primary.W, rawDx, s.CentreX, s.Targets, n, false, s.Distance, step, out guideX);
                dy = Axis(primary.Y, primary.H, rawDy, s.CentreY, s.Targets, n, true, s.Distance, step, out guideY);
            }
            if (s.Keep == 1) { dy = 0; guideY = float.NaN; }
            else if (s.Keep == 2) { dx = 0; guideX = float.NaN; }
        }

        // The nearest target line within reach of the moved box's near, centre or far line wins, ties to the earliest
        // (the canvas centre, then each target's three lines); with none, the near edge lands on the grid. A snapped
        // near edge rounds to a whole unit (an odd size's centre line is a half unit), the guide stays on the line.
        private static float Axis(float at, float size, float raw, float centre, Box[] t, int n, bool y, float reach, int step, out float guide)
        {
            float near = at + raw, best = float.PositiveInfinity, shift = 0;
            guide = float.NaN;
            Line(near, size, centre, reach, ref best, ref shift, ref guide);
            for (int i = 0; i < n; i++)
            {
                float a = y ? t[i].Y : t[i].X, l = y ? t[i].H : t[i].W;
                Line(near, size, a, reach, ref best, ref shift, ref guide);
                Line(near, size, a + l * 0.5f, reach, ref best, ref shift, ref guide);
                Line(near, size, a + l, reach, ref best, ref shift, ref guide);
            }
            return float.IsNaN(guide) ? R(near / step) * step - at : R(near + shift) - at;
        }

        private static void Line(float near, float size, float line, float reach, ref float best, ref float shift, ref float guide)
        {
            for (int k = 0; k < 3; k++)
            {
                float d = line - (near + size * 0.5f * k), a = Math.Abs(d);
                if (a <= reach && a < best) { best = a; shift = d; guide = line; }
            }
        }

        /// The box after dragging a handle by dx/dy. Handles in reading order: 0 TL, 1 T, 2 TR, 3 L, 4 R, 5 BL, 6 B, 7 BR;
        /// the opposite edges stay put, and any other handle leaves the box as it was. A side the handle drags rounds to
        /// Step and then to a sibling size while snapping, to whole units otherwise. With KeepAspect the larger relative
        /// change drives on a corner and the side derived from the aspect rounds to whole units, off the grid if need be;
        /// on an edge that side grows about the start's centre line, its position whole, so its centre is within half a unit of that line.
        internal static Box Resize(Box start, int handle, float dx, float dy, ref SizeSnap s)
        {
            if (handle < 0 || handle > 7) return start;
            bool left = handle == 0 || handle == 3 || handle == 5, right = handle == 2 || handle == 4 || handle == 7;
            bool top = handle >= 0 && handle <= 2, bottom = handle >= 5 && handle <= 7;
            float w = start.W + (left ? -dx : right ? dx : 0), h = start.H + (top ? -dy : bottom ? dy : 0);
            bool keep = s.KeepAspect && start.W > 0 && start.H > 0;
            if (keep)
            {
                // A corner follows the larger relative change, an edge its own; the other side is derived from the start.
                float aspect = start.W / start.H;
                if ((left || right) && (!(top || bottom) || Math.Abs(w / start.W - 1) >= Math.Abs(h / start.H - 1)))
                { w = Size(w, ref s, s.Widths); h = R(w / aspect); }
                else { h = Size(h, ref s, s.Heights); w = R(h * aspect); }
                if (w < s.Min || h < s.Min)
                {
                    if (aspect >= 1) { h = s.Min; w = R(s.Min * aspect); }
                    else { w = s.Min; h = R(s.Min / aspect); }
                }
            }
            else
            {
                if (left || right) w = Math.Max(s.Min, Size(w, ref s, s.Widths));
                if (top || bottom) h = Math.Max(s.Min, Size(h, ref s, s.Heights));
            }
            // A side the handle does not touch keeps its place, or grows about the start's centre line when it follows the aspect.
            float x = left ? start.Right - w : right ? start.X : keep ? R(start.CX - w * 0.5f) : start.X;
            float y = top ? start.Bottom - h : bottom ? start.Y : keep ? R(start.CY - h * 0.5f) : start.Y;
            return new Box(x, y, w, h);
        }

        // Snapping on: the grid step, then the nearest sibling size within reach (ties to the earliest). Off: whole units.
        private static float Size(float v, ref SizeSnap s, float[] sizes)
        {
            if (!s.On) return R(v);
            int step = s.Step > 0 ? s.Step : 1;
            v = R(v / step) * step;
            float best = float.PositiveInfinity, to = v;
            int n = sizes == null ? 0 : Math.Min(s.Count, sizes.Length);
            for (int i = 0; i < n; i++)
            {
                float a = Math.Abs(sizes[i] - v);
                if (a <= s.Distance && a < best) { best = a; to = sizes[i]; }
            }
            return to;
        }

        /// A marquee takes an item it overlaps; edges that only touch do not count.
        internal static bool Hits(Box band, Box item)
        {
            return band.X < item.Right && item.X < band.Right && band.Y < item.Bottom && item.Y < band.Bottom;
        }

        /// One z-step for the selection. order is draw order (last on top), selected is by item index. Forward walks
        /// down from the top, so each selected item passes the unselected one above it once and a selected run at the
        /// top stays; backward mirrors it. Returns whether anything moved.
        internal static bool StepZ(int[] order, bool[] selected, bool forward)
        {
            if (order == null || selected == null) return false;
            bool moved = false;
            int n = order.Length;
            for (int k = 1; k < n; k++)
            {
                int i = forward ? n - 1 - k : k, j = forward ? i + 1 : i - 1;
                if (!Selected(selected, order[i]) || Selected(selected, order[j])) continue;
                int t = order[i]; order[i] = order[j]; order[j] = t; moved = true;
            }
            return moved;
        }

        private static bool Selected(bool[] s, int i) { return i >= 0 && i < s.Length && s[i]; }

        internal static float ClampZoom(float z) { return float.IsNaN(z) ? 1 : z < 0.3f ? 0.3f : z > 4 ? 4 : z; }

        /// The pan that keeps the canvas point under stage point (px, py) still while the zoom changes to
        /// ClampZoom(newZoom). The current zoom is taken as given; one of 0 or less (or NaN) divides by 1.
        internal static void ZoomAt(float zoom, float panX, float panY, float newZoom, float px, float py, out float outPanX, out float outPanY)
        {
            float d = zoom > 0 ? zoom : 1, z = ClampZoom(newZoom);
            outPanX = px - (px - panX) / d * z;
            outPanY = py - (py - panY) / d * z;
        }

        /// The zoom and pan that frame content in the stage, padding on every side, centred. Content with no width or
        /// height is centred at zoom 1.
        internal static void Fit(Box content, float stageW, float stageH, float padding, out float zoom, out float panX, out float panY)
        {
            zoom = content.W > 0 && content.H > 0
                ? ClampZoom(Math.Min((stageW - 2 * padding) / content.W, (stageH - 2 * padding) / content.H)) : 1;
            panX = stageW * 0.5f - content.CX * zoom;
            panY = stageH * 0.5f - content.CY * zoom;
        }

        /// Notches x right and y down from Input.mouseScrollDelta, the polled wheel. The panel's WheelEvent carries
        /// -3 x Input.mouseScrollDelta on both axes (InputForUI), so its delta / 3 is the same swipe: both axes flip.
        internal static void PolledNotch(float scrollX, float scrollY, out float x, out float y) { x = -scrollX; y = -scrollY; }

        /// How far a wheel of (x, y) notches pans the view: 20 stage px a notch against the swipe; Shift turns a
        /// y-only wheel sideways; invert pans with the swipe instead.
        internal static void WheelPan(float x, float y, bool shift, bool invert, out float panX, out float panY)
        {
            if (shift && Math.Abs(x) < 1e-4f) { x = y; y = 0f; }
            float k = invert ? 20f : -20f;
            panX = x * k; panY = y * k;
        }

        /// A position kept within ±limit; NaN comes back as 0.
        internal static float Clamp(float v, float limit) { return float.IsNaN(v) ? 0 : v < -limit ? -limit : v > limit ? limit : v; }

        private static float R(float v) { return (float)Math.Round(v, MidpointRounding.AwayFromZero); }
    }
}
