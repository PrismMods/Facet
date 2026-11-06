using System;
using System.Collections.Generic;

namespace Facet
{
    /* Text in the player's language. English is the key, as in the mods' own Loc tables, so
       anything untranslated shows in English. Ui.Text and Ui.Btn look every string up, so page code
       never has to — the convention the mods' uGUI builders already follow.

       A string is translated only by the table of whoever wrote it. English keys carry no context,
       so a lookup in someone else's table finds the same word in another sense: CamDisplay's
       "Undo" (remove the camera patch) came out as the editor's 실행 취소. So:
       - Facet's own table (below) answers only Own(), which Facet's code calls for its own
         words, and the rows Facet registers itself (owner "Facet").
       - A settings row is built with Owner set to its mod's name; then only that mod's
         translators answer, and a mod with no table shows English.
       - Elsewhere (a mod's own menu) every registered translator answers, in order. */
    public static class Words
    {
        /// "ko" for Korean; anything else (or null) is English. The host points this at its own
        /// language setting.
        public static Func<string> Language;

        /// Where a throwing translator is reported; Host points it at the fallback host's log.
        public static Action<string> Log;

        private static readonly List<Func<string, string>> _translators = new List<Func<string, string>>();
        private static readonly List<string> _assemblies = new List<string>();

        /// Whose strings are being built: a settings row's mod (its name), or null.
        internal static string Owner;

        internal const string Self = "Facet";

        /// A mod's own table: return the translation, or null when it has none.
        public static void AddTranslator(Func<string, string> t)
        {
            if (t == null) return;
            _translators.Add(t);
            _assemblies.Add(AssemblyOf(t));
        }

        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            string owner = Owner;
            if (owner == Self) return Own(english);
            for (int i = 0; i < _translators.Count; i++)
            {
                if (owner != null && !Owns(_assemblies[i], owner)) continue;
                var tr = _translators[i];
                string s = Safe.Get(Self, "Words.translator", () => tr(english), null, Log);
                if (!string.IsNullOrEmpty(s) && s != english) return s;
            }
            return english;
        }

        /// T for a sentence with a value in it: translate the pattern, then fill it.
        public static string F(string englishFormat, params object[] args)
        {
            try { return string.Format(T(englishFormat), args); }
            catch (FormatException) { return string.Format(englishFormat, args); }
        }

        /// Facet's own words, from its own table only.
        internal static string Own(string english)
        {
            string ko;
            return !string.IsNullOrEmpty(english) && IsKorean() && Korean.TryGetValue(english, out ko) ? ko : english;
        }

        internal static string OwnF(string englishFormat, params object[] args)
        {
            try { return string.Format(Own(englishFormat), args); }
            catch (FormatException) { return string.Format(englishFormat, args); }
        }

        /* A translator belongs to the mod whose code it is: a mod registers a lambda over
           its own Loc table, compiled into the mod's assembly, and the mod's assembly is named
           after the mod. This works with mods already released, which register with no owner.
           ponytail: name match; an AddTranslator(owner, t) overload if a mod's assembly is ever
           named otherwise. */
        private static string AssemblyOf(Func<string, string> t)
        {
            try { return t.Method.DeclaringType.Assembly.GetName().Name; } catch { return null; }
        }

        private static bool Owns(string assembly, string owner)
            => assembly != null && assembly.StartsWith(owner, StringComparison.OrdinalIgnoreCase);

        private static bool IsKorean()
        {
            try { return Language != null && Language() == "ko"; } catch { return false; }
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
