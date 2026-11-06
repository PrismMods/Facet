using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;

namespace Facet
{
    /* The panel's typefaces: which font files to try, building them as SDF assets, and handing them
       to a surface. A host that ships its own files (HostOptions.SansFonts / MonoFonts) is tried
       first and gets faces of its own; every other host shares one set. Built on demand, once, never
       per frame. */
    public static class Fonts
    {
        sealed class Faces { public FontAsset Sans, Mono; public bool CjkTried; }

        private static readonly Faces _shared = new Faces();
        private static readonly Dictionary<Host, Faces> _own = new Dictionary<Host, Faces>();

        /* The host whose font is being made, for the static code below that logs while it runs.
           ponytail: a line may go to the host that happened to build last when two build at once. */
        private static Host _ctx;
        private static void L(string m) { (_ctx ?? Host.Fallback).Log(m); }

        private static Faces For(Host h)
        {
            _ctx = h = h ?? Host.Fallback;
            if (h.SansFonts == null && h.MonoFonts == null) return _shared;
            Faces f;
            if (!_own.TryGetValue(h, out f)) _own[h] = f = new Faces();
            return f;
        }

        /// The host's proportional face; null only when the machine has no usable font at all.
        public static FontAsset Regular(Host h)
        {
            var f = For(h);
            return f.Sans ?? (f.Sans = BuildSans(h ?? Host.Fallback, f));
        }

        /* A monospace face for anything columnar. A log is the case that matters: timestamps and
           level tags only line up in a fixed pitch, and the game's own display font — which is what
           a mod's TMP asset is built from — is the worst possible choice for reading one. */
        public static FontAsset Mono(Host h)
        {
            var f = For(h);
            return MonoOnly(h ?? Host.Fallback, f) ?? Regular(h);
        }

        /// Makes `font` the face of everything under `root` (and, before Unity 6, hands it down).
        public static void Apply(VisualElement root, FontAsset font)
        {
            if (root == null || font == null) return;
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            KeepHanded(root);
        }

        private sealed class Handed { }

        /// Before Unity 6, re-hands the root's font down on a short schedule. Once per root.
        internal static void KeepHanded(VisualElement root)
        {
            if (FontsInherit || Data.Find<Handed>(root) != null) return;
            Data.Get<Handed>(root);
            root.schedule.Execute(() => HandDownFonts(root, default(StyleFontDefinition))).Every(200);
        }

        /* Hangul is not in the Latin face (macOS's Arial has none): a Korean host gets an OS CJK face
           appended to the regular one's fallbacks the first time its language is "ko". Lazy — the
           file is large — and checked once per face set, never per frame. */
        internal static void ForLanguage(Host h)
        {
            if (h == null || h.Language != "ko") return;
            var f = For(h);
            if (f.CjkTried) return;
            f.CjkTried = true;
            var sans = Regular(h);
            var bold = BoldFace(sans);   // titles and headers are bold; its table is its own
            if (sans == null || (HasHangul(sans) && (bold == null || HasHangul(bold)))) return;
            var cjk = FileFontAsset(_cjkFiles, SamplePx);
            if (cjk == null) { L("Fonts: no CJK font file found — Korean text will draw boxes"); return; }
            try { cjk.isMultiAtlasTexturesEnabled = true; } catch { }   // thousands of glyphs outgrow one 1024 atlas
            foreach (var fa in new[] { sans, bold })
            {
                if (fa == null) continue;
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<FontAsset>();
                if (!fa.fallbackFontAssetTable.Contains(cjk)) fa.fallbackFontAssetTable.Add(cjk);
            }
            L("Fonts: Hangul " + (HasHangul(sans) ? "now" : "STILL NOT") + " drawable through " + cjk.name);
        }

        /// The real weight-700 face AttachBold put in the regular face's table, or null.
        public static FontAsset BoldFace(FontAsset regular)
        {
            var t = regular == null ? null : regular.fontWeightTable;
            return t != null && t.Length > 7 ? t[7].regularTypeface : null;
        }

        /// Whether the face, with its fallbacks, has the glyphs for 크 and 기.
        public static bool HasHangul(FontAsset fa)
        {
            return fa != null && fa.HasCharacter('\uD06C', true, true) && fa.HasCharacter('\uAE30', true, true);
        }

        // Windows Malgun Gothic, macOS Apple SD Gothic Neo, then Noto Sans CJK where the distro puts it
        // (and under /run/host inside the Steam runtime container).
        private static readonly string[][] _cjkFiles =
        {
            new[] { "/System/Library/Fonts/AppleSDGothicNeo.ttc" },
            new[] { @"C:\Windows\Fonts\malgun.ttf", @"C:\Windows\Fonts\malgunbd.ttf" },
            new[] { "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc" },
            new[] { "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc" },
            new[] { "/run/host/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc" },
            new[] { "/run/host/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc" },
        };

        private static FontAsset BuildSans(Host h, Faces f)
        {
            var sans = FileFontAsset(h.SansFonts ?? _sansFiles, SamplePx)
                    ?? (_osSdf == null ? FileFontAsset(_macSansFiles, SamplePx) : null)
                    ?? OsFontAsset(new[] { "Helvetica Neue", "Helvetica", "Arial", "Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Cantarell", "FreeSans" }, SamplePx);
            /* The monospace face as the sans face's fallback: Arial (macOS on 2.9) lacks the panel
               icons (✕ ▾ ▸ ◂ ▴ ↶ ↷) and drew boxes; Menlo preloads every one of them. */
            var mono = MonoOnly(h, f);
            if (sans != null && mono != null && mono != sans)
            {
                if (sans.fallbackFontAssetTable == null) sans.fallbackFontAssetTable = new List<FontAsset>();
                if (!sans.fallbackFontAssetTable.Contains(mono)) sans.fallbackFontAssetTable.Add(mono);
            }
            return sans;
        }

        private static FontAsset MonoOnly(Host h, Faces f)
        {
            if (f.Mono != null) return f.Mono;
            f.Mono = FileFontAsset(h.MonoFonts ?? _monoFiles, SamplePx)
                  ?? (_osSdf == null ? FileFontAsset(_macMonoFiles, SamplePx) : null)
                  ?? OsFontAsset(new[] { "Menlo", "SF Mono", "Consolas", "DejaVu Sans Mono", "Noto Sans Mono", "Courier New", "monospace" }, SamplePx);
            return f.Mono;
        }

        /* Every panel's text settings, with OUR face as the default. Left null, UI Toolkit falls
           back to a built-in default that names an OS "DejaVu Sans" / "Regular" — which the Steam
           runtime cannot resolve — and retries it for every label on every frame: ~10,000 "Unable
           to load font face" lines a second, blank labels, and (once a real font was supplied)
           every label drawing the same string. One shared instance; the fallback list is emptied
           so nothing reaches for an OS font behind our back. */
        internal static PanelTextSettings TextSettings(Host h, FontAsset preferred)
        {
            if (_textSettings != null) return _textSettings;
            _textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            _textSettings.name = "FacetPanelTextSettings";
            _textSettings.defaultFontAsset = preferred ?? Regular(h);
            _textSettings.fallbackFontAssets = new List<FontAsset>();
            L("Surface: panel text settings, default font "
                   + (_textSettings.defaultFontAsset != null ? _textSettings.defaultFontAsset.name : "NONE"));
            return _textSettings;
        }

        /* Before Unity 6 (a Unity 2022 game) a font set on an element does not reach its
           descendants: in game every label under the root resolved no font and laid out 0px tall,
           so no text drew at all; set on the label itself, it draws. There each surface hands its
           font down by hand on a short schedule — list rows are recycled and popups add elements
           later — giving every text element the nearest font set above it, as inheritance would.
           Stamped elements are marked so a later font change above them still reaches them. */
        private static readonly bool FontsInherit = UnityMajor() >= 6000;
        private const string Stamped = "facet-font-handed-down";

        private static int UnityMajor()
        {
            int n = 0;
            foreach (char c in Application.unityVersion) { if (!char.IsDigit(c)) break; n = n * 10 + (c - '0'); }
            return n;
        }

        private static void HandDownFonts(VisualElement e, StyleFontDefinition inherited)
        {
            var own = e.style.unityFontDefinition;
            if (own.keyword == StyleKeyword.Undefined && !e.ClassListContains(Stamped)) inherited = own;
            else if (e is UnityEngine.UIElements.TextElement && inherited.keyword == StyleKeyword.Undefined)
            {
                e.style.unityFontDefinition = inherited;
                e.AddToClassList(Stamped);
            }
            for (int i = 0; i < e.hierarchy.childCount; i++) HandDownFonts(e.hierarchy[i], inherited);
        }

        /* Glyphs are sampled well above the size they are drawn at. Masters of 14–16px, magnified by the
           panel scale (1.4 on a 1440p screen) and again by a Retina backing scale, drew every edge soft —
           the "blurry text" in every window. Padding about a tenth of it, like TMP's 90/9: at 9px on
           a 16px master the distance field was spread over half the glyph. */
        private const int SamplePx = 64, PadPx = 6;
        private static PanelTextSettings _textSettings;

        /* Regular, in that order, because a name match alone is not enough — see OsFontAsset. */
        private static readonly string[] _styles = { "Regular", "Book", "Normal", "Medium", "" };

        /* Font FILES, tried before any family-name lookup. A family+style asset only works while
           TextCore can find that family again: it reloads the face by name whenever it adds glyphs,
           and on Steam Linux Runtime that reload asks for style "Regular" — which fontconfig's
           "Book"-tagged DejaVu does not answer to. The asset was created fine and then every
           frame logged "Unable to load font face" and drew nothing (232k lines in one session).
           A path-built asset reloads by path, which cannot miss.

           Pairs of regular + bold: the bold file becomes the asset's real 700 weight (see
           AttachBold). Inside the runtime container the game sees its own DejaVu under
           /usr/share/fonts and the host's fonts under /run/host. */
        private static readonly string[][] _sansFiles =
        {
            new[] { "/run/host/usr/share/fonts/noto/NotoSans-Regular.ttf", "/run/host/usr/share/fonts/noto/NotoSans-Bold.ttf" },
            new[] { "/usr/share/fonts/noto/NotoSans-Regular.ttf", "/usr/share/fonts/noto/NotoSans-Bold.ttf" },
            new[] { "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf", "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf" },
            new[] { "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" },
            new[] { "/usr/share/fonts/TTF/DejaVuSans.ttf", "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf" },
            new[] { "/run/host/usr/share/fonts/TTF/DejaVuSans.ttf", "/run/host/usr/share/fonts/TTF/DejaVuSans-Bold.ttf" },
            new[] { @"C:\Windows\Fonts\segoeui.ttf", @"C:\Windows\Fonts\segoeuib.ttf" },
            new[] { @"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf" },
        };

        private static readonly string[][] _monoFiles =
        {
            new[] { "/run/host/usr/share/fonts/noto/NotoSansMono-Regular.ttf", "/run/host/usr/share/fonts/noto/NotoSansMono-Bold.ttf" },
            new[] { "/usr/share/fonts/noto/NotoSansMono-Regular.ttf", "/usr/share/fonts/noto/NotoSansMono-Bold.ttf" },
            new[] { "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf" },
            new[] { "/usr/share/fonts/TTF/DejaVuSansMono.ttf", "/usr/share/fonts/TTF/DejaVuSansMono-Bold.ttf" },
            new[] { "/run/host/usr/share/fonts/TTF/DejaVuSansMono.ttf", "/run/host/usr/share/fonts/TTF/DejaVuSansMono-Bold.ttf" },
            new[] { @"C:\Windows\Fonts\consola.ttf", @"C:\Windows\Fonts\consolab.ttf" },
        };

        /* macOS's own files, used FIRST only where there is no SDF family lookup (Unity 2022): there the family path builds a raster atlas and every label drew nothing.
           On Unity 6 the family lookup keeps Helvetica Neue / Menlo, so 3.x looks unchanged.
           Menlo.ttc's face 0 is its regular weight. */
        private static readonly string[][] _macSansFiles =
        {
            new[] { "/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf" },
            new[] { "/Library/Fonts/Arial.ttf", "/Library/Fonts/Arial Bold.ttf" },
        };

        private static readonly string[][] _macMonoFiles =
        {
            new[] { "/System/Library/Fonts/Menlo.ttc" },
            new[] { "/System/Library/Fonts/Supplemental/Courier New.ttf", "/System/Library/Fonts/Supplemental/Courier New Bold.ttf" },
        };

        // Printable ASCII plus the symbols the panels draw (arrows, ×, ✕, ▾, ↶ ↷, …, •).
        private static readonly string _preload = BuildPreload();

        private static string BuildPreload()
        {
            var sb = new System.Text.StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            sb.Append("×✕▾▸◂▴←→↑↓↶↷…•—–·°");
            return sb.ToString();
        }

        private static FontAsset FileFontAsset(string[][] candidates, int pointSize)
        {
            foreach (var pair in candidates)
            {
                if (!System.IO.File.Exists(pair[0])) continue;
                var fa = FromFile(pair[0], pointSize);
                if (fa == null) continue;
                L("Fonts: font file " + pair[0]);
                if (pair.Length > 1 && System.IO.File.Exists(pair[1])) AttachBold(fa, FromFile(pair[1], pointSize), pair[1]);
                return fa;
            }
            return null;
        }

        /* The 5-arg overload that asks for SDF is Unity 6 only; Unity 2022 has just the
           3-arg one (a raster atlas — see OsFontAsset for what that costs bold text). Found by
           reflection: naming it would bind the dll to Unity 6. */
        private static readonly System.Reflection.MethodInfo _osSdf = typeof(FontAsset).GetMethod("CreateFontAsset",
            new[] { typeof(string), typeof(string), typeof(int), typeof(int), typeof(UnityEngine.TextCore.LowLevel.GlyphRenderMode) });

        private static FontAsset OsFont(string family, string style, int pointSize)
        {
            if (_osSdf != null)
                return (FontAsset)_osSdf.Invoke(null, new object[] { family, style, pointSize, PadPx, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA });
            return FontAsset.CreateFontAsset(family, style, pointSize);
        }

        private static FontAsset FromFile(string path, int pointSize)
        {
            try
            {
                var fa = FontAsset.CreateFontAsset(path, 0, pointSize, PadPx,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                if (fa != null)
                {
                    fa.name = System.IO.Path.GetFileNameWithoutExtension(path);
                    string missing;
                    bool all = fa.TryAddCharacters(_preload, out missing);
                    L("Fonts: preloaded " + fa.name + (all ? "" : " (missing: " + missing + ")"));
                }
                return fa;
            }
            catch (Exception e) { L("Fonts: font file '" + path + "' failed: " + e.Message); return null; }
        }

        /* A real bold face for FontStyle.Bold. Without one UI Toolkit fakes bold by dilating the
           regular glyphs' SDF edge, which at panel sizes comes out smeared and blurry — every header
           and title. Weight 700 is slot 7 of the weight table; the text generator uses a typeface
           found there instead of faking it. */
        private static void AttachBold(FontAsset regular, FontAsset bold, string what)
        {
            if (regular == null || bold == null) return;
            var table = regular.fontWeightTable;
            if (table == null || table.Length < 8) { L("Fonts: no weight table — bold stays synthesised"); return; }
            table[7].regularTypeface = bold;
            L("Fonts: bold face " + what);
        }

        /* An OS font, built once. Cheap insurance: every mod has a different font pipeline and any
           of them can hand over null, but a debug window that renders nothing is worse than one in
           the wrong typeface. Not safe during the loader's static-ctor window (Time.frameCount 0),
           which is why it is built on demand rather than at load.

           Goes through FontAsset.CreateFontAsset(family, style, size) rather than
           Font.CreateDynamicFontFromOSFont: the legacy call matches a family NAME against the OS
           list and hands back a non-null Font even when nothing can actually be rendered with it,
           deferring the failure to glyph generation — which runs every frame a label is on screen.
           Measured on Steam Linux Runtime: only "DejaVu Sans" was visible to the sandboxed process
           at all, and fontconfig tags its regular weight style "Book", not "Regular" — a mismatch
           CreateDynamicFontFromOSFont never surfaces, so the panel opened with every frame logging
           "Unable to load font face" and not one visible character. CreateFontAsset checks the
           family+style pair up front and returns null on a miss, so trying several style strings
           per family here actually finds a working one instead of silently failing per frame. */
        private static FontAsset OsFontAsset(string[] families, int pointSize)
        {
            foreach (string family in families)
                foreach (string style in _styles)
                {
                    FontAsset fa;
                    try
                    {
                        /* The 3-arg CreateFontAsset(family, style, size) passes GlyphRenderMode
                           .DEFAULT (a raster atlas, not SDF) — confirmed by disassembling
                           UnityEngine.TextCoreTextEngineModule.dll. UI Toolkit's faux-bold
                           (FontStyle.Bold on a face with no real bold weight, which is all this
                           fallback ever has) works by dilating the SDF glyph edge; on a raster
                           atlas that dilation has nothing to act on and instead doubles and
                           offsets the glyph. Headers and titles (the only bold text) rendered as
                           a garbled double-strike while plain-weight labels were fine — request
                           SDF explicitly so faux-bold has an edge to dilate. */
                        fa = OsFont(family, style, pointSize);
                    }
                    catch (Exception e) { L("Fonts: OS font '" + family + "' (" + style + ") failed: " + e.Message); continue; }
                    if (fa != null)
                    {
                        L("Fonts: OS font asset '" + family + "' style '" + style + "'");
                        // The family's own bold, where the OS has one (macOS's Helvetica Neue does).
                        FontAsset bold = null;
                        try { bold = OsFont(family, "Bold", pointSize); }
                        catch { }
                        AttachBold(fa, bold, family + " Bold");
                        return fa;
                    }
                }
            L("Fonts: no OS font asset matched any candidate");
            return null;
        }
    }
}
