namespace Facet
{
    /* A menu item's shortcut text ("Ctrl+Shift+C", "Delete", "[", "Ctrl+0"): modifier flags and a key
       named as Unity's KeyCode spells it, which MenuItem turns into one when the text is set. Cmd counts as
       Ctrl, so one string serves every platform. Plain BCL, so the offline test pins it. */
    internal static class Shortcut
    {
        /// Modifiers (Ctrl or Cmd, Shift, Alt or Option, any order and case) joined by '+', then one key.
        /// Empty, a dangling '+', an unknown modifier or an unknown key fails.
        internal static bool TryParse(string s, out bool ctrl, out bool shift, out bool alt, out string key)
        {
            string written;
            key = Split(s, out ctrl, out shift, out alt, out written) ? Name(written) : null;
            return key != null;
        }

        /// As a menu shows it: ⌘⇧⌥ and the key on a Mac, else Ctrl+Shift+Alt+Key. A key no menu names (a key
        /// row's chord: "Ctrl+Alpha1", "Shift+Space") keeps its modifiers that way too, with AlphaN shown as N.
        /// Text whose modifiers do not parse shows as given.
        internal static string Display(string s, bool mac)
        {
            bool c, sh, a; string written;
            if (!Split(s, out c, out sh, out a, out written)) return s ?? "";
            if (written.Length == 6 && written.StartsWith("Alpha", System.StringComparison.Ordinal) && char.IsDigit(written[5])) written = written.Substring(5);
            written = char.ToUpperInvariant(written[0]) + written.Substring(1);
            return mac ? (c ? "⌘" : "") + (sh ? "⇧" : "") + (a ? "⌥" : "") + written
                       : (c ? "Ctrl+" : "") + (sh ? "Shift+" : "") + (a ? "Alt+" : "") + written;
        }

        // Peels "Mod+" prefixes off; what is left is the key as written ("+" alone is a key: "Ctrl++").
        private static bool Split(string s, out bool ctrl, out bool shift, out bool alt, out string key)
        {
            ctrl = shift = alt = false; key = null;
            if (string.IsNullOrEmpty(s)) return false;
            string rest = s.Trim();
            for (int plus = rest.IndexOf('+'); plus > 0; plus = rest.IndexOf('+'))
            {
                string mod = rest.Substring(0, plus).ToLowerInvariant();
                if (mod == "ctrl" || mod == "control" || mod == "cmd" || mod == "command") ctrl = true;
                else if (mod == "shift") shift = true;
                else if (mod == "alt" || mod == "option" || mod == "opt") alt = true;
                else return false;
                rest = rest.Substring(plus + 1);
            }
            if (rest.Length == 0) return false;
            key = rest;
            return true;
        }

        private static string Name(string k)
        {
            if (k.Length == 1)
            {
                char c = k[0];
                if (c >= 'a' && c <= 'z') return char.ToUpperInvariant(c).ToString();
                if (c >= 'A' && c <= 'Z') return k;
                if (c >= '0' && c <= '9') return "Alpha" + c;
                switch (c)
                {
                    case '[': return "LeftBracket";
                    case ']': return "RightBracket";
                    case '+': case '=': return "Equals";
                    case '-': return "Minus";
                }
                return null;
            }
            int n;
            if ((k[0] == 'F' || k[0] == 'f') && int.TryParse(k.Substring(1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 12)
                return "F" + n;
            switch (k.ToLowerInvariant())
            {
                case "del": case "delete": return "Delete";
                case "backspace": return "Backspace";
                case "up": return "UpArrow";
                case "down": return "DownArrow";
                case "left": return "LeftArrow";
                case "right": return "RightArrow";
            }
            return null;
        }
    }
}
