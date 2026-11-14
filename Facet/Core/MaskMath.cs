using System.Collections.Generic;

namespace Facet
{
    /// Bits of an int mask (a mask tile grid's model), BCL only. Bits not named never change.
    internal static class MaskMath
    {
        internal static int With(int mask, int bit, bool on)
        {
            return on ? mask | 1 << bit : mask & ~(1 << bit);
        }

        internal static int WithAll(int mask, IList<int> bits, bool on)
        {
            for (int i = 0; i < bits.Count; i++) mask = With(mask, bits[i], on);
            return mask;
        }

        /// mask with the named bits copied from snapshot: an undo restores those bits alone.
        internal static int Merge(int mask, int snapshot, IList<int> bits)
        {
            for (int i = 0; i < bits.Count; i++) mask = With(mask, bits[i], (snapshot >> bits[i] & 1) != 0);
            return mask;
        }

        internal static bool AllOn(int mask, IList<int> bits)
        {
            for (int i = 0; i < bits.Count; i++) if ((mask >> bits[i] & 1) == 0) return false;
            return true;
        }
    }
}
