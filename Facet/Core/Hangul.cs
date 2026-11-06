namespace Facet
{
    /// Korean initial consonants (초성), for searching a label by its leading consonants.
    public static class Hangul
    {
        const string Lead = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";

        /// Each precomposed syllable (U+AC00-U+D7A3) becomes its leading consonant; everything else passes through.
        public static string Initials(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
                if (c[i] >= 0xAC00 && c[i] <= 0xD7A3) c[i] = Lead[(c[i] - 0xAC00) / 588];
            return new string(c);
        }

        /// A non-empty run of compatibility jamo consonants only (U+3131-U+314E).
        internal static bool IsInitialsOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char ch in s) if (ch < 0x3131 || ch > 0x314E) return false;
            return true;
        }
    }
}
