using System;
using System.Collections.Generic;

namespace Facet
{
    /* Text in the player's language. English is the key, as in the mods' own Loc tables, so
       anything untranslated shows in English.

       A string is translated only by the table of whoever wrote it. English keys carry no context,
       so a lookup in someone else's table finds the same word in another sense: one mod's "Undo"
       (remove its patch) came out as another's 실행 취소 (undo an edit). So the rules here take
       ONE translator at a time, never a list:
       - T(english, translator): the translator of the host whose string it is.
       - Own(english, language): Facet's own table, for Facet's own words.
       - Own(english, host): the host's answer to "Facet:" + english first, else Facet's own table. The prefix keeps
         the rule: a host answers only Facet keys it chose to define, never by its own "Undo".
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

        /// Facet's own words, from its own table only: Korean for "ko" (or a tag such as "ko-KR"), else English.
        public static string Own(string english, string language)
        {
            string ko;
            return !string.IsNullOrEmpty(english) && Primary(language) == "ko" && Korean.TryGetValue(english, out ko) ? ko : english;
        }

        /// A BCP-47 tag's primary language subtag, lower-case: "ko-KR" and "KO_kr" give "ko". null and "" stay as they
        /// are. An already-reduced tag comes back as the same string, so the common case allocates nothing.
        internal static string Primary(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;
            int end = tag.IndexOfAny(Separators);
            if (end < 0) end = tag.Length;
            bool lower = end == tag.Length;
            for (int i = 0; lower && i < end; i++) lower = !char.IsUpper(tag[i]);
            return lower ? tag : tag.Substring(0, end).ToLowerInvariant();
        }

        private static readonly char[] Separators = { '-', '_' };

        /// A word Facet picks that the host may still word its own way: the host's, else Facet's own when the
        /// host leaves the key unchanged.
        internal static string TOrOwn(string english, Func<string, string> translator, string language)
        {
            var s = T(english, translator);
            return s == english ? Own(english, language) : s;
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
            { "Search", "검색" },
            { "Results for \u201c{0}\u201d", "\u201c{0}\u201d 검색 결과" },
            { "{0}: this page failed to build — see the log.", "{0}: 이 페이지를 만들지 못했습니다 — 로그를 확인하세요." },
            { "Run", "실행" },
            { "Undo", "실행 취소" },
            { "Redo", "다시 실행" },
            { "Undo {0}", "실행 취소: {0}" },
            { "Redo {0}", "다시 실행: {0}" },
            { "Sure?", "정말요?" },
            { "Active", "사용 중" },
            { "Unbound", "없음" },
            { "press a key…", "키를 누르세요…" },
            { "Click the strip to add a stop.", "띠를 클릭하면 스톱이 추가됩니다." },
            { "Stop colour", "스톱 색" },
            { "Position", "위치" },
            { "Add stop", "스톱 추가" },
            { "Remove stop", "스톱 제거" },
            { "unavailable, see the log", "사용할 수 없음 — 로그를 확인하세요" },
            { "Advanced", "고급" },
            { "Collapse sidebar", "사이드바 접기" },
            { "Expand sidebar", "사이드바 펼치기" },
            { "Move", "이동" },
            { "Resize", "크기 조절" },
            { "Nudge", "미세 이동" },
            { "Delete", "삭제" },
            { "Bring forward", "앞으로 가져오기" },
            { "Send backward", "뒤로 보내기" },
            { "Mixed", "혼합" },
            { "OK", "확인" },
            { "Cancel", "취소" },
            { "Press a key", "키를 누르세요" },
            { "Press a key or chord", "키 또는 조합 키를 누르세요" },
        };
    }
}
