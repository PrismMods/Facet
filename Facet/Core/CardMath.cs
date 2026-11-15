using System;

namespace Facet
{
    /// The column count of a card grid, BCL only.
    internal static class CardMath
    {
        /// As many minWidth columns as fit width with gap between them, 1..max; previous while there is no width yet.
        internal static int Columns(float width, float minWidth, float gap, int max, int previous)
        {
            if (width < 1f) return previous;
            int n = (int)Math.Floor((width + gap) / (minWidth + gap));
            return Math.Max(1, Math.Min(n, Math.Max(1, max)));
        }
    }
}
