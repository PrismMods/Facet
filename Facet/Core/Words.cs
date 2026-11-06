using System;
using System.Collections.Generic;

namespace Facet
{
    /* Text in the player's language. English is the key, as in the mods' own Loc tables, so
       anything untranslated shows in English.

       A string is translated only by the table of whoever wrote it. English keys carry no context,
       so a lookup in someone else's table finds the same word in another sense: CamDisplay's
       "Undo" (remove the camera patch) came out as the editor's 실행 취소. So the rules here take
       ONE translator at a time, never a list:
       - T(english, translator): the translator of the host whose string it is.
       - Own(english, language): Facet's own table, for Facet's own words, whatever any host maps.
       The Unity-typed halves (a Host, a TextElement) are in the other part of this class. */
    public static partial class Words
    {
        public static string T(string english, Func<string, string> translator)
        {
            if (string.IsNullOrEmpty(english) || translator == null) return english;
            string s;
            try { s = translator(english); } catch { return english; }
            return string.IsNullOrEmpty(s) ? english : s;
        }

        /// Facet's own words, from its own table only: Korean for "ko", else English.
        public static string Own(string english, string language)
        {
            string ko;
            return !string.IsNullOrEmpty(english) && language == "ko" && Korean.TryGetValue(english, out ko) ? ko : english;
        }

        /// A sentence with a value in it: the translated pattern, then filled.
        public static string F(string englishFormat, Func<string, string> translator, params object[] args)
            => Fill(T(englishFormat, translator), englishFormat, args);

        public static string OwnF(string englishFormat, string language, params object[] args)
            => Fill(Own(englishFormat, language), englishFormat, args);

        private static string Fill(string pattern, string english, object[] args)
        {
            try { return string.Format(pattern, args); }
            catch (FormatException) { return string.Format(english, args); }
        }

        // Facet's own words, read through Own(). Terms: 스톱 for a gradient stop, 실행 취소, 사용 중.
        private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
        {
            { "Nothing matches.", "일치하는 항목이 없습니다." },
            { "This page failed to build — see the log.", "이 페이지를 만들지 못했습니다 — 로그를 확인하세요." },
            { "Run", "실행" },
            { "Undo", "실행 취소" },
            { "Redo", "다시 실행" },
            { "Undo {0}", "실행 취소: {0}" },
            { "Redo {0}", "다시 실행: {0}" },
            { "Sure?", "정말요?" },
            { "Active", "사용 중" },
            { "unbound", "없음" },
            { "press a key…", "키를 누르세요…" },
            { "Click the strip to add a stop.", "띠를 클릭하면 스톱이 추가됩니다." },
            { "Stop colour", "스톱 색" },
            { "Position", "위치" },
            { "Add stop", "스톱 추가" },
            { "Remove stop", "스톱 제거" },
        };
    }
}
