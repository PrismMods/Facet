using System;

namespace Facet
{
    /// Which OS font files can draw Korean, Chinese or Japanese, by script. Paths only; Fonts loads them.
    internal static class Cjk
    {
        /// "ko", "zh" or "ja" from the first two letters of a host language ("zh-CN", "ja_JP", any case); else null.
        public static string Script(string language)
        {
            if (language == null || language.Length < 2) return null;
            switch (language.Substring(0, 2).ToLowerInvariant())
            {
                case "ko": return "ko";
                case "zh": return "zh";
                case "ja": return "ja";
                default: return null;
            }
        }

        /// Text whose glyphs show a face can draw the script.
        public static string Sample(string script)
        {
            return script == "ko" ? "\uD06C\uAE30" : script == "zh" ? "中文设置" : script == "ja" ? "日本語あア" : null;
        }

        /// { regular, bold? } candidates in order, or null for any other script.
        public static string[][] Files(string script)
        {
            switch (script)
            {
                case "ko": return Ko;
                case "zh": return Zh;
                case "ja": return Ja;
                default: return null;
            }
        }

        /* Verified on macOS 15 only (Windows and Linux paths are unverified on this rig). macOS 14+ keeps
           PingFang under /System/Library/AssetsV2/<hash>/, so zh falls through to Hiragino Sans GB there. */
        private static readonly string[] NotoPaths =
        {
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/run/host/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/run/host/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        };

        // Windows Malgun Gothic, macOS Apple SD Gothic Neo, then Noto Sans CJK where the distro puts it
        // (and under /run/host inside the Steam runtime container).
        private static readonly string[][] Ko = Build(
            new[] { "/System/Library/Fonts/AppleSDGothicNeo.ttc" },
            new[] { @"C:\Windows\Fonts\malgun.ttf", @"C:\Windows\Fonts\malgunbd.ttf" });

        private static readonly string[][] Zh = Build(
            new[] { "/System/Library/Fonts/PingFang.ttc" },
            new[] { "/System/Library/Fonts/Hiragino Sans GB.ttc" },
            new[] { "/System/Library/Fonts/STHeiti Medium.ttc" },
            new[] { @"C:\Windows\Fonts\msyh.ttc", @"C:\Windows\Fonts\msyhbd.ttc" });

        private static readonly string[][] Ja = Build(
            new[] { "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc", "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc" },
            new[] { @"C:\Windows\Fonts\YuGothR.ttc", @"C:\Windows\Fonts\YuGothB.ttc" },
            new[] { @"C:\Windows\Fonts\meiryo.ttc" });

        // The given pairs, then the Noto paths.
        private static string[][] Build(params string[][] first)
        {
            var all = new string[first.Length + NotoPaths.Length][];
            Array.Copy(first, all, first.Length);
            for (int i = 0; i < NotoPaths.Length; i++) all[first.Length + i] = new[] { NotoPaths[i] };
            return all;
        }
    }
}
