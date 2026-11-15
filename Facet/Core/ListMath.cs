namespace Facet
{
    /// The scroll-end rule of a paged list, BCL only.
    internal static class ListMath
    {
        /// Content no taller than the viewport, or the viewport's bottom within margin of the content's end.
        internal static bool NearEnd(float offset, float content, float viewport, float margin)
        {
            if (content <= viewport) return true;
            return offset + viewport >= content - margin;
        }
    }
}
