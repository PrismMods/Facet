using System;
using System.Collections.Generic;
using Facet;

static class FacetTest {
    static int _fail;
    static void Check(bool ok, string what, string detail = "") {
        if (!ok) { _fail++; Console.WriteLine("FAIL: " + what + " " + detail); }
    }

    static bool Near(float[] c, float r, float g, float b, double eps = 1e-3) {
        return c != null && Math.Abs(c[0] - r) < eps && Math.Abs(c[1] - g) < eps && Math.Abs(c[2] - b) < eps;
    }
    static string Show(float[] c) { return c == null ? "null" : string.Join(",", c); }

    static void Main() {
        Throttle.Reset();
        Check(Throttle.Allow("h/site", 0), "the first message for a host+site is logged");
        Check(!Throttle.Allow("h/site", 9.9), "a repeat inside 10 s is dropped");
        Check(Throttle.Allow("h/other", 9.9), "another site is independent");
        Check(Throttle.Allow("h/site", 10.1), "after 10 s it is logged again");

        History.Clear();
        double t = 0; History.Clock = () => t;
        int v = 0;
        History.Record("x", "k", () => v = 0, () => v = 2);
        t = 0.5; v = 1;
        History.Record("x", "k", () => v = 1, () => v = 3);
        Check(History.CanUndo && History.NextUndo == "x", "two records of one key 0.5 s apart exist");
        Check(History.Undo() && !History.CanUndo && v == 0, "...and coalesce into one undo step");
        t = 5; History.Clear();
        History.Record("x", "k", () => { }, () => { });
        t = 6; History.Record("x", "k", () => { }, () => { });
        History.Undo();
        Check(History.CanUndo, "records 1 s apart (past CoalesceSeconds) stay separate");
        // Ruling R14: Facet's refresh is its own hook, run first; a consumer assigning AfterApply cannot drop it.
        string ran = "";
        History.RefreshAll = () => ran += "r";
        History.AfterApply = () => ran += "x";
        History.AfterApply = () => ran += "a";   // a consumer assigns rather than adds
        History.Undo();
        Check(ran == "ra", "an undo runs Facet's RefreshAll, then the consumer's AfterApply", ran);
        History.RefreshAll = null; History.AfterApply = null;
        // Ruling R20: one throwing Changed handler leaves the handlers after it running, logged once.
        var hl = new List<string>(); var oldLog = History.Log; History.Log = hl.Add;
        int after = 0;
        Action throwing = () => { throw new InvalidOperationException("changed"); }, count = () => after++;
        History.Changed += throwing; History.Changed += count;
        History.Record("y", "k2", () => { }, () => { });
        History.Record("y", "k3", () => { }, () => { });
        History.Changed -= throwing; History.Changed -= count; History.Log = oldLog;
        Check(after == 2 && hl.Count == 1 && hl[0].Contains("History.Changed"), "a throwing History.Changed handler leaves the next one running, logged once per 10 s",
            after + " runs, " + string.Join(" | ", hl));
        History.Clear();
        var l1 = Shared.Get("t", () => new List<int>());
        Check(ReferenceEquals(l1, Shared.Get("t", () => new List<int>())), "Shared.Get returns the same instance twice");
        var dd = AppDomain.CurrentDomain.GetData("Facet.Shared") as Dictionary<string, object>;
        Check(dd != null && ReferenceEquals(dd["t"], l1), "...and it is visible through AppDomain data");
        var ents = new List<object[]>();
        var logs = new List<string>();
        Action<string> lg = m => logs.Add(m);
        Func<string, bool, float, object[]> mk = (key, open, top) => new object[] {
            (Func<float>)(() => top), (Func<bool>)(() => true), (Func<bool>)(() => open), null, null,
            (Func<float, float, bool>)((x, y) => open), key, null, null };
        Coord.Register(ents, mk("A", true, 5f));
        Coord.Register(ents, mk("A", false, 1f));
        Check(ents.Count == 1 && !Coord.Any(ents, 2, lg), "a second registration under one key replaces the first");
        var bad = mk("Bad", true, 9f); bad[2] = (Func<bool>)(() => { throw new Exception("x"); });
        bad[0] = (Func<float>)(() => { throw new Exception("x"); });
        Coord.Register(ents, bad); Coord.Register(ents, mk("C", true, 3f));
        Check(Coord.Any(ents, 2, lg), "a throwing entry does not stop the others");
        Coord.Any(ents, 2, lg);
        Check(logs.Count == 1, "...and is logged once");
        Check(Coord.TakeEscape(ents, lg), "Escape goes to the highest healthy entry despite a throwing one");
        // renumber across two versions keeps relative order and ends at the highest order
        var ao = new List<float> { 100f, 300f }; var bo = new List<float> { 200f, 400f };
        Func<List<float>, object[]> mr = l => { var e = mk("R" + l[0], true, 0f);
            e[7] = (Func<int, float[]>)(ly => l.ToArray());
            e[8] = (Action<int, Func<float, float>>)((ly, f) => { for (int i = 0; i < l.Count; i++) l[i] = f(l[i]); }); return e; };
        var re = new List<object[]> { mr(ao), mr(bo) };
        float hi = Coord.Renumber(re, 0, 10f, lg);
        Check(hi == 14f && ao[0] == 11f && bo[0] == 12f && ao[1] == 13f && bo[1] == 14f, "Renumber ranks orders across versions");
        // An older version registered a shorter entry (no [7]/[8]): it is skipped, not an IndexOutOfRange.
        var olog = new List<string>();
        var older = mk("Older", true, 1f); Array.Resize(ref older, 7);
        var mixed = new List<object[]> { older, mr(new List<float> { 500f }) };
        Coord.Renumber(mixed, 0, 10f, olog.Add);
        Check(olog.Count == 0 && !Coord.Any(mixed, 8, olog.Add) && olog.Count == 0, "a shorter entry from an older version is skipped without a throw or a log line", string.Join(" | ", olog));
        // The queries sit on a host's per-key input path: they allocate nothing.
        var q = new List<object[]> { mk("Q1", false, 1f), mk("Q2", true, 2f) };
        q[0][4] = q[1][4] = (Func<bool>)(() => false);
        Coord.Any(q, 2, lg); Coord.Any(q, 4, lg); Coord.Pointer(q, 1f, 1f, lg); Coord.TakeEscape(q, lg);   // JIT first
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) { Coord.Any(q, 2, lg); Coord.Any(q, 4, lg); Coord.Pointer(q, 1f, 1f, lg); Coord.TakeEscape(q, lg); }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(bytes == 0, "100000 rounds of Coord queries allocate nothing", bytes + " bytes");

        // The wheel is counted once per frame across mods and versions; a gated zero read keeps the frame.
        Func<string, bool, float, Action<float, float, float>, object[]> mk10 = (key, open, top, wheel) =>
        { var e = mk(key, open, top); Array.Resize(ref e, 10); e[9] = wheel; return e; };
        var we = new List<object[]>(); var wst = new[] { -1 }; int wa = 0, wb = 0;
        we.Add(mk10("WA", true, 30001f, (x, y, d) => wa++)); we.Add(mk10("WB", true, 30005f, (x, y, d) => wb++));
        Check(!Coord.Wheel(we, wst, 5, 0, 0, 0f, lg) && wst[0] == -1, "a zero delta does not use up the frame");
        Check(Coord.Wheel(we, wst, 5, 0, 0, -1f, lg) && wb == 1 && wa == 0, "the topmost entry takes the wheel");
        Check(!Coord.Wheel(we, wst, 5, 0, 0, -1f, lg) && wb == 1, "a second call in the same frame does nothing");
        Check(Coord.Wheel(we, wst, 6, 0, 0, -1f, lg) && wb == 2, "the next frame scrolls again");
        we.Add(mk("W01", true, 30009f));   // nine slots: a Facet 0.1 entry on top
        Check(!Coord.Wheel(we, wst, 7, 0, 0, -1f, lg) && wb == 2, "under an older version's window this version scrolls nothing");
        var wq = new List<object[]> { mk10("Q1", true, 1f, (x, y, d) => { }), mk10("Q2", true, 2f, (x, y, d) => { }) };
        var wqs = new[] { -1 }; Coord.Wheel(wq, wqs, 0, 0, 0, -1f, lg);
        long wbytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i <= 100000; i++) Coord.Wheel(wq, wqs, i, 0, 0, -1f, lg);
        wbytes = GC.GetAllocatedBytesForCurrentThread() - wbytes;
        Check(wbytes == 0, "100000 rounds of Coord.Wheel allocate nothing", wbytes + " bytes");

        Throttle.Reset();
        var slog = new List<string>();
        Action<string> sl = m => slog.Add(m);
        Action boom = () => { throw new InvalidOperationException("bad"); };
        Check(!Safe.Run("A", "site", boom, sl), "Safe.Run returns false when the action throws");
        Check(slog.Count == 1 && slog[0].StartsWith("[A] site: bad @ "), "...and logs owner, site, message and the first stack line", slog.Count > 0 ? slog[0] : "none");
        Safe.Run("A", "site", boom, sl);
        Check(slog.Count == 1, "a second throw from the same site inside 10 s logs nothing");
        Safe.Run("B", "site", boom, sl);
        Check(slog.Count == 2, "another owner at the same site is logged");
        Check(Safe.Run("A", "ok", () => { }, sl), "Safe.Run returns true when the action runs");
        Check(Safe.Get("A", "get", () => { if (slog != null) throw new Exception("x"); return 5; }, 7, sl) == 7, "Safe.Get returns the fallback on a throw");
        Check(Safe.Get("A", "get2", () => 5, 7, sl) == 5, "Safe.Get returns the value otherwise");
        Check(Safe.Run("A", "nolog", boom, null) == false, "a null log is tolerated");

        var red = Palette.FromAccent(1f, 0f, 0f, 1f);
        Check(Near(red[(int)Role.Danger], 1f, .478f, .271f), "a red accent turns Danger orange", Show(red[(int)Role.Danger]));
        var blue = Palette.FromAccent(.2f, .4f, 1f, 1f);
        Check(Near(blue[(int)Role.Danger], .886f, .404f, .427f), "a blue accent keeps Danger red", Show(blue[(int)Role.Danger]));
        var bp = blue[(int)Role.Panel];
        Check(Near(bp, .037f + (.2f - .037f) * .059f, .037f + (.4f - .037f) * .059f, .037f + (1f - .037f) * .059f, 1e-4) && Math.Abs(bp[3] - .98f) < 1e-4,
            "Panel leans .059 from grey .037 toward the accent, alpha .98", Show(bp));
        var grey = Palette.FromAccent(.2f, .4f, 1f, 0f)[(int)Role.Panel];
        Check(grey[0] == grey[1] && grey[1] == grey[2], "tint 0 gives a pure grey Panel", Show(grey));
        Check(Near(blue[(int)Role.Focus], .2f, .4f, 1f) && Near(blue[(int)Role.AccentText], .06f, .06f, .08f), "Focus is the accent; AccentText is near-black");

        var sz = new Sizes(); var sc = sz.Copy(); sc.Radius = 2f;
        Check(sz.Radius == 3f && sz.Body == 15f && sz.RowHeight == 32f && sz.SwitchRadius == 9f, "Sizes default to Facet 0.1's look");
        Check(sc.Radius == 2f && sz.Radius == 3f, "a copy of Sizes is independent");
        Check(sz.ControlPad == 5f && sz.FieldPad == 4f && sz.ItemHeight == 38f, "ControlPad, FieldPad and ItemHeight keep 0.1's 5, 4 and 38");
        // Ruling Q19: every Sizes field, the new ones included, is copied and the copy is its own.
        bool everySize = true;
        var sd = new Sizes(); var sdc = sd.Copy();
        foreach (var f in typeof(Sizes).GetFields())
        {
            everySize &= f.FieldType == typeof(float) && (float)f.GetValue(sdc) == (float)f.GetValue(sd);
            f.SetValue(sdc, -1f);
            everySize &= (float)f.GetValue(sd) != -1f;
        }
        Check(everySize, "Sizes.Copy copies every field, and writing the copy leaves the source");
        var lk = new Looks(); var lc = lk.Copy(); lc.Button.Bg = Role.Danger;
        Check(lk.Button.Bg == Role.RowAlt, "Looks.Copy copies each Look");
        bool everyLook = true;
        foreach (var f in typeof(Looks).GetFields())
            everyLook &= f.GetValue(lc) is Look && !ReferenceEquals(f.GetValue(lc), f.GetValue(lk));
        Check(everyLook, "Looks.Copy gives every Look field its own copy");
        Check(lk.Switch.Bg == Role.Off && lk.Switch.SelectedBg == Role.Accent && lk.Fill.Bg == null && lk.Indicator.SelectedBg == null, "the new looks keep 0.1's look");
        Check(lk.Tab.Bg == Role.RowAlt && lk.Tile.Border == Role.Line && lk.Group.Bg == Role.Panel && lk.Group.Border == Role.PanelBorder
              && lk.Stage.Bg == Role.Panel, "Tab is the Button preset; Group and Stage start as Card");
        var lb = Look.Button; var lb2 = lb.Copy(); lb2.SelectedBorder = Role.Focus; lb2.DisabledOpacity = .5f;
        Check(lb.SelectedBorder == null && lb.DisabledOpacity == .35f && lb2.Bg == Role.RowAlt, "Look.Copy is independent of its source");
        var pb = Palette.FromAccent(.2f, .4f, 1f, 1f);
        // Ruling Q7: Off matches RowAlt in alpha too.
        Check(Near(pb[(int)Role.Off], pb[(int)Role.RowAlt][0], pb[(int)Role.RowAlt][1], pb[(int)Role.RowAlt][2]) && pb[(int)Role.Off][3] == pb[(int)Role.RowAlt][3]
              && pb[(int)Role.Rail][3] == 0f && pb[(int)Role.Line][3] == pb[(int)Role.PanelBorder][3], "Off, Rail and Line derive from today's roles");
        var aCalls = new List<string>();
        Func<string, string> trA = s => { aCalls.Add(s); return s == "Undo" ? "A-undo" : null; };
        Func<string, string> trB = s => s == "Undo" ? "B-undo" : null;
        Check(Words.T("Undo", trA) == "A-undo" && aCalls.Count == 1, "a translator answers its own host's strings");
        aCalls.Clear();
        Check(Words.T("Undo", trB) == "B-undo" && aCalls.Count == 0, "host A's translator is never consulted when translating for B");
        Check(Words.T("Undo", (Func<string, string>)null) == "Undo" && Words.T("Nope", trB) == "Nope", "no translator or no entry shows English");
        Check(Words.T("Undo", s => { throw new Exception("x"); }) == "Undo", "a throwing translator falls back to English");
        Check(Words.Own("Undo", "ko") == "\uC2E4\uD589 \uCDE8\uC18C" && Words.Own("Undo", "en") == "Undo" && Words.Own("Undo", null) == "Undo", "Own answers Korean from Facet's table for ko only, whatever any consumer maps Undo to");
        Check(Words.Own("Not ours", "ko") == "Not ours", "Own falls back to English for a key not in Facet's table");
        History.Clear();
        History.Record("Size", "k1", () => { }, () => { }, trB);
        Check(History.NextUndoText == Words.T("Size", trB) && History.NextUndoText == "Size", "an entry recorded with translator B shows its label through B");
        History.Clear();
        History.Record("Undo", "k2", () => { }, () => { }, trB);
        aCalls.Clear();
        Check(History.NextUndoText == "B-undo" && aCalls.Count == 0, "...never through another host's translator", History.NextUndoText);
        History.Record("Plain", "k3", () => { }, () => { });
        Check(History.NextUndoText == "Plain", "an entry with no translator shows English");
        History.Clear();
        Check(Words.OwnF("Undo {0}", "ko", "x") == "\uC2E4\uD589 \uCDE8\uC18C: x", "OwnF fills the translated pattern");

        Check(Math.Abs(ValueScale.Log.FromT(ValueScale.Log.ToT(10, 1, 100), 1, 100) - 10) < 1e-3, "Log.FromT inverts Log.ToT");
        Check(Math.Abs(ValueScale.Linear.ToT(5, 0, 10) - .5f) < 1e-6, "Linear.ToT(5, 0, 10) is .5");
        Check(Math.Abs(ValueScale.Snap(0.37f, 0.25f, 0) - 0.25f) < 1e-6, "Snap(.37, .25) rounds down to .25");
        Check(Math.Abs(ValueScale.Snap(0.38f, 0.25f, 0) - 0.5f) < 1e-6, "Snap(.38, .25) rounds up to .5");
        Check(ValueScale.Snap(3, 0, 0) == 3, "a step of 0 means no snapping");
        Check(ValueScale.Linear.FromT(1f, 0.5f, 2f) == 2f && ValueScale.Linear.FromT(0f, 0.5f, 2f) == 0.5f, "the ends of the track are exactly Min and Max");

        var kc = new List<string>();
        KeyCapture.Claim("a", () => kc.Add("a"));
        Check(KeyCapture.Owner == "a" && kc.Count == 0, "the first claim cancels nobody");
        KeyCapture.Claim("b", () => kc.Add("b"));
        Check(kc.Count == 1 && kc[0] == "a" && KeyCapture.Owner == "b", "a second claim cancels the first once and takes over");
        KeyCapture.Release("a");
        Check(KeyCapture.Owner == "b" && kc.Count == 1, "releasing a non-owner changes nothing");
        KeyCapture.Release("b");
        Check(KeyCapture.Owner == null && kc.Count == 1, "releasing the owner clears it without cancelling");
        KeyCapture.Claim("c", () => { throw new Exception("x"); });
        KeyCapture.Claim("d", () => kc.Add("d"));
        Check(KeyCapture.Owner == "d", "a throwing cancel does not stop the next claim");
        KeyCapture.Release("d");

        Console.WriteLine("search");
        Check(Search.Score("Effective BPM", "Effective BPM") == 0, "an exact hit scores best");
        Check(Search.Score("Effective BPM", "eff") < Search.Score("Effective BPM", "bpm"),
              "a prefix beats a hit in the middle");
        Check(Search.Score("Grid snap", "snap") < Search.Score("Gridsnap", "snap"),
              "a word start beats mid-word");
        Check(Search.Score("Effective BPM", "efbpm") > Search.Score("Effective BPM", "bpm"),
              "a scattered subsequence ranks below a real substring");
        Check(Search.Matches("Effective BPM", "efbpm"), "subsequence matches out of contiguity");
        Check(!Search.Matches("Effective BPM", "zq"), "absent characters do not match");
        Check(Search.Score("anything", "") == 0 && Search.Matches("anything", null),
              "an empty query matches everything");
        Check(Search.Score("", "x") == Search.NoMatch, "an empty haystack matches nothing");
        Check(Search.ScoreAny("bpm", null, "Effective BPM", "x") != Search.NoMatch,
              "ScoreAny skips nulls and takes the best field");
        var ranked = Search.Rank(new[] { "Buffer Padding Mode", "Effective BPM", "bpm" }, "bpm", x => new[] { x });
        Check(ranked.Count == 3 && ranked[0] == "bpm", "Rank puts the exact hit first");
        Check(Search.Filter(new[] { "keep me", "drop", "me too" }, "me").Count == 2,
              "Filter keeps input order and drops non-matches");
        Check(Search.ScoreAny("key rain", "Note Rain", "Key Viewer") != Search.NoMatch,
              "query words may land in different fields");
        Check(Search.ScoreAny("rain", "Note Rain", "Key Viewer") < Search.ScoreAny("rain", "Scale", "Rain"),
              "a label hit outranks a context hit");
        Check(Search.Matches("Key Viewer", "keyviewer"), "spaces in the label do not count");
        Check(Search.Matches("Accent Color", "colour"), "a word answers to its alias");
        Check(Search.Matches("a -> b", "->"), "a punctuation-only query is a plain substring");
        var typo = Search.Rank(new[] { "Key Viewer", "Scale" }, "keyveiwer", x => new[] { x });
        Check(typo.Count == 1 && typo[0] == "Key Viewer", "Rank allows typos when nothing matches exactly");
        Check(!Search.Matches("Key Viewer", "keyveiwer"), "a per-item match never allows typos");
        var midWord = Search.Rank(new[] { "Brain", "Reverse Rain", "Rainbow" }, "rain", x => new[] { x });
        Check(midWord.Count == 3 && midWord[2] == "Brain", "a hit inside a word ranks below whole words and prefixes");

        // Quartz's settings-search cases, through the structured API it will call.
        Console.WriteLine("search (structured)");
        Check(First(Find("Key Rain")) == "Note Rain", "'Key Rain' finds Note Rain on the Key Viewer page");
        Check(First(Find("Keyviewer Rain")) == "Note Rain", "'Keyviewer Rain' ignores the missing space");
        Check(First(Find("kv scale")) == "Scale", "the kv alias reaches Key Viewer");
        Check(Find("key viewer size").Contains("Scale"), "size answers to scale");
        Check(First(Find("note rain")) == "Note Rain", "an exact label outranks rows that only share words");
        Check(!Find("Key Rain").Contains("Rain Offset Y"), "every word must land: 'key' is nowhere near Rain Offset Y");
        Check(First(Find("keyveiwer rain")) == "Note Rain", "a swapped pair is one typo");
        Check(First(Find("judgment font")) == "Judgement Popup Font", "the American spelling still finds Judgement");
        Check(First(Find("acent colr")) == "Accent Color", "one typo per short word");
        bool clean = true;
        foreach (var l in Find("rain")) if (!l.Contains("Rain") && l != "This Tabub Is Mine") clean = false;
        Check(clean, "an exact hit keeps typo matches out");
        Check(Find("xyzzy").Count == 0, "nonsense finds nothing");
        Check(Search.Rate(new Search.Query("cat"), new[] { new Search.Text("car") }, null, null, true) == 0,
              "three-letter words get no typo allowance");
        var tip = Find("pattern picture");
        Check(tip.Count == 1 && tip[0] == "This Tabub Is Mine", "tooltip text is searchable");
        var rain = Find("rain");
        Check(rain[rain.Count - 1] == "This Tabub Is Mine", "a tooltip-only hit ranks below every label hit");

        Check(Hangul.Initials("\uB178\uD2B8") == "\u3134\u314C", "Initials of a Korean word are its leading consonants");
        Check(Hangul.Initials("A\uAC00") == "A\u3131", "other characters pass through Initials unchanged");
        Check(Cjk.Script("zh-CN") == "zh" && Cjk.Script("ZH") == "zh" && Cjk.Script("ja_JP") == "ja" && Cjk.Script("ko-KR") == "ko", "a host language maps to its script");
        Check(Cjk.Script("en") == null && Cjk.Script("") == null && Cjk.Script(null) == null, "other languages need no CJK face");
        Check(Cjk.Files("zh")[0][0] == "/System/Library/Fonts/PingFang.ttc" && Array.Exists(Cjk.Files("zh"), p => p[0].EndsWith("Hiragino Sans GB.ttc")), "zh tries PingFang, then Hiragino Sans GB");
        Check(Array.Exists(Cjk.Files("ja"), p => p[0].EndsWith("ヒラギノ角ゴシック W3.ttc")) && Cjk.Files("en") == null, "ja has Hiragino Sans");
        Check(Search.Score("\uB178\uD2B8 \uD06C\uAE30", "\u3134\u314C") != Search.NoMatch, "initials find a Korean label");
        Check(Search.Score("\uB178\uD2B8", "\u3131\u3134") == Search.NoMatch, "wrong initials do not match");
        Check(Search.Matches("\uB178\uD2B8 \uD06C\uAE30", "\u314B\u3131"), "initials may start at a later word");
        var fps = Search.Rank(new[] { "Sound offset", "Show FPS" }, "fps", x => new[] { x });
        Check(fps.Count == 1 && fps[0] == "Show FPS", "Rank finds Show FPS for fps");
        var sohw = Search.Rank(new[] { "Sound offset", "Show FPS" }, "sohw", x => new[] { x });
        Check(sohw.Count == 1 && sohw[0] == "Show FPS", "a typo finds Show FPS through the second pass");
        Check(!Search.Matches("Show FPS", "sohw"), "...but never in a per-item match");
        var both = Search.Rank(new[] { "Sohw thing", "Show FPS" }, "sohw", x => new[] { x });
        Check(both.Count == 1 && both[0] == "Sohw thing", "...and not when an exact hit exists");

        Motion.Speed = 0.5f;
        var shared = (Dictionary<string, object>)AppDomain.CurrentDomain.GetData("Facet.Shared");
        Check(((float[])shared["Facet.AnimSpeed"])[0] == 0.5f, "the animation speed lives in the cross-version store");
        Check(Motion.Scale(120) == 60, "120 ms at speed 0.5 is 60 ms");
        Motion.Speed = float.NaN; Check(Motion.Scale(120) == 0, "a NaN speed is instant");
        Motion.Speed = -1f;       Check(Motion.Scale(120) == 0, "a negative speed is instant");
        Motion.Speed = float.PositiveInfinity; Check(Motion.Scale(0) == 0 && Motion.Scale(120) == 60000, "an infinite speed is capped, and 0 ms stays 0");
        Motion.Speed = 1f;

        Console.WriteLine(_fail == 0 ? "ALL PASS" : _fail + " FAILED");
    }

    static string First(List<string> l) { return l.Count > 0 ? l[0] : null; }

    // Label, section, page, tooltip — the shape of a Quartz settings row.
    static readonly string[][] Rows =
    {
        new[] { "Note Rain", "Key Viewer", "Key Viewer", null },
        new[] { "Reverse Rain", "Key Viewer", "Key Viewer", null },
        new[] { "Scale", "Key Viewer", "Key Viewer", null },
        new[] { "Key Viewer", null, "Key Viewer", null },
        new[] { "Judgement Popup Font", "Judgement", "Visuals", null },
        new[] { "Accent Color", "Theme", "Settings", null },
        new[] { "This Tabub Is Mine", "Key Viewer", "Key Viewer",
                "Past a point in the level, note rain stops and a picture drops over the key viewer, so nobody watching can read your tapping pattern." },
        new[] { "Rain Offset Y", "Rain", "Overlay", null },
    };

    // Quartz's two passes: exact first, typos only when that found nothing. Best first.
    static List<string> Find(string query)
    {
        var q = new Search.Query(query);
        var hits = new List<KeyValuePair<int, int>>();     // score, row
        foreach (bool typos in new[] { false, true })
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                var r = Rows[i];
                int s = Search.Rate(q, new[] { new Search.Text(r[0]) },
                                    new[] { new Search.Text(r[1]), new Search.Text(r[2]) },
                                    r[3] == null ? null : new Search.Text(r[3]), typos);
                if (s > 0) hits.Add(new KeyValuePair<int, int>(s, i));
            }
            if (hits.Count > 0) break;
        }
        hits.Sort((a, b) => a.Key != b.Key ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value));   // stable, as Quartz's
        var outp = new List<string>();
        foreach (var h in hits) outp.Add(Rows[h.Value][0]);
        return outp;
    }
}
