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
        // Before anything reads Motion: a smooth-scroll value of the wrong type is already in the shared store.
        Shared.Get<object>("Facet.SmoothScroll", () => new int[1]);
        Check(!Motion.Smooth, "a bad shared smooth-scroll value reads as off");
        Motion.Smooth = true;
        Check(Motion.Smooth, "...and falls back to a private switch that still works");
        var mp = Motion.TakeProblem();
        Check(mp != null && mp.Contains("Facet.SmoothScroll") && !mp.Contains("Facet.AnimSpeed"), "...reported once, naming its key", mp ?? "null");
        Check(Motion.TakeProblem() == null, "...and only once");
        Motion.Speed = 0.5f;
        Check(((float[])Shared.Get<object>("Facet.AnimSpeed", () => null))[0] == 0.5f, "the speed beside it stays shared");
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
        // Ruling R14: Facet's refresh is its own hook, run first; AfterApply is an event, so one subscriber
        // never drops another, and a throwing one neither stops the next nor the refresh.
        string ran = "";
        History.RefreshAll = () => ran += "r";
        var al = new List<string>(); var keepAl = History.Log; History.Log = al.Add;
        Action boomA = () => { throw new InvalidOperationException("applied boom"); };
        Action ev = () => ran += "e", ev2 = () => ran += "a";
        History.AfterApply += boomA; History.AfterApply += ev; History.AfterApply += ev2;
        History.Undo();
        Check(ran == "rea" && al.FindAll(m => m.Contains("applied boom")).Count == 1,
            "an undo runs RefreshAll, then AfterApply's handlers in order, each under its own guard", ran + " / " + string.Join(" | ", al));
        History.AfterApply -= boomA; History.AfterApply -= ev; History.AfterApply -= ev2; History.Log = keepAl;
        ran = ""; History.Record("x", null, () => { }, () => { }); History.Undo();
        Check(ran == "r", "a handler removed with -= no longer runs", ran);
        History.RefreshAll = null;
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
        // A drag running in a lower version takes Escape before the top window does.
        int topTook = 0, drags = 1;
        var low = mk("Low", true, 1f); Array.Resize(ref low, 11);
        low[10] = (Func<bool>)(() => drags-- > 0);
        var high = mk("High", true, 5f); high[1] = (Func<bool>)(() => { topTook++; return true; });
        var ge = new List<object[]> { low, high };
        bool g1 = Coord.TakeEscape(ge, lg), g2 = Coord.TakeEscape(ge, lg);
        Check(g1 && g2 && topTook == 1 && drags == -1, "a version's running drag takes Escape before the topmost window; once it ended the window does",
            g1 + " / " + g2 + " / top " + topTook);
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
        Check(Near(blue[(int)Role.Focus], .2f, .4f, 1f) && Near(blue[(int)Role.AccentText], .06f, .06f, .08f), "Focus is the accent; a mid blue gets near-black AccentText");
        Check(Near(Palette.FromAccent(.1f, .12f, .35f, 1f)[(int)Role.AccentText], .92f, .92f, .94f), "a navy accent gets light AccentText");
        Check(Near(Palette.FromAccent(1f, .75f, .2f, 1f)[(int)Role.AccentText], .06f, .06f, .08f), "a light amber accent gets dark AccentText");
        Check(Near(Palette.FromAccent(1f, .5995077f, .5995077f, 1f)[(int)Role.AccentText], .06f, .06f, .08f), "Quartz's default red accent gets dark AccentText");

        var sz = new Sizes(); var sc = sz.Copy(); sc.Radius = 2f;
        Check(sz.Radius == 3f && sz.Body == 15f && sz.RowHeight == 32f && sz.SwitchRadius == 9f, "Sizes default to Facet 0.1's look");
        Check(sc.Radius == 2f && sz.Radius == 3f, "a copy of Sizes is independent");
        Check(sz.ControlPad == 5f && sz.FieldPad == 4f && sz.ItemHeight == 38f, "ControlPad, FieldPad and ItemHeight keep 0.1's 5, 4 and 38");
        // every Sizes field, the new ones included, is copied and the copy is its own.
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
        Check(lk.Rule.Bg == null && lk.Rule.Border == null, "Looks.Rule is invisible by default, as on Dark (0.1 drew no rules)");
        Check(lk.Tab.Bg == Role.RowAlt && lk.Tile.Border == Role.Line && lk.Group.Bg == Role.Panel && lk.Group.Border == Role.PanelBorder
              && lk.Stage.Bg == Role.Panel, "Tab is the Button preset; Group and Stage start as Card");
        var lb = Look.Button; var lb2 = lb.Copy(); lb2.SelectedBorder = Role.Focus; lb2.DisabledOpacity = .5f;
        Check(lb.SelectedBorder == null && lb.DisabledOpacity == .35f && lb2.Bg == Role.RowAlt, "Look.Copy is independent of its source");
        var pb = Palette.FromAccent(.2f, .4f, 1f, 1f);
        // Off matches RowAlt in alpha too.
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
        Check(Words.Own("Search", "ko") == "\uAC80\uC0C9", "Facet's own search placeholder has a Korean word");
        Check(Words.TOrOwn("Search", s => s, "ko") == "\uAC80\uC0C9" && Words.TOrOwn("Search", s => s == "Search" ? "Find" : s, "ko") == "Find",
            "TOrOwn takes the host's word, and Facet's own only when the host leaves the key unchanged");
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
        MotionHold();
        HistorySteps();
        CanvasGeometry();
        GuideColour();
        Shortcuts();
        ManyBinds();
        BindThen();
        Conveniences();
        UndoChord();

        Console.WriteLine(_fail == 0 ? "ALL PASS" : _fail + " FAILED");
    }

    // The undo chord, answered once per frame across mods and Facet copies by the copy with the topmost window.
    static void UndoChord() {
        var logs = new List<string>(); Action<string> lg = logs.Add;
        Func<string, float, Func<bool, bool>, object[]> mk12 = (key, top, undo) =>
        { var e = new object[12]; e[0] = (Func<float>)(() => top); e[6] = key; e[11] = undo; return e; };
        int ua = 0, ub = 0; bool applies = true, lastRedo = false;
        var es = new List<object[]> {
            mk12("UndoA", 30001f, r => { ua++; return true; }),
            mk12("UndoB", 30005f, r => { ub++; lastRedo = r; return applies; }) };
        var st = new[] { -1, 0 };
        Check(Coord.Undo(es, st, 5, false, lg) && ub == 1 && ua == 0 && !lastRedo, "the topmost entry takes the undo chord; the lower one is not called", ua + " / " + ub);
        Check(Coord.Undo(es, st, 5, false, lg) && ub == 1 && ua == 0, "a second call in the same frame calls no slot and says a step applied", ua + " / " + ub);
        applies = false;
        Check(!Coord.Undo(es, st, 6, true, lg) && ub == 2 && lastRedo, "the next frame asks again (a redo), false when no step applied", ub + " / " + lastRedo);
        Check(!Coord.Undo(es, st, 6, true, lg) && ub == 2, "...and a second call in that frame returns false without calling a slot", ub.ToString());
        var older = new object[11]; older[0] = (Func<float>)(() => 30009f); older[6] = "UndoOlder";   // a copy from before the undo slot
        es.Add(older);
        Check(!Coord.Undo(es, st, 7, false, lg) && ub == 2 && ua == 0 && st[0] == 7, "under an older copy's window this copy undoes nothing; its own polls drive it", ua + " / " + ub);
        es[2] = mk12("UndoThrows", 30010f, r => { throw new InvalidOperationException("undo"); });
        int l0 = logs.Count;
        bool t1 = Coord.Undo(es, st, 8, false, lg), t2 = Coord.Undo(es, st, 9, false, lg);
        Check(!t1 && !t2 && logs.Count == l0 + 1 && ub == 2, "a throwing undo slot returns false and is logged once", t1 + " / " + t2 + " / " + (logs.Count - l0));
        var q = new List<object[]> { mk12("UndoQ1", 1f, r => false), mk12("UndoQ2", 2f, r => true) };
        var qs = new[] { -1, 0 };
        Coord.Undo(q, qs, 0, false, lg); Coord.Undo(q, qs, 0, false, lg);   // JIT first
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i <= 100000; i++) { Coord.Undo(q, qs, i, (i & 1) == 0, lg); Coord.Undo(q, qs, i, false, lg); }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(bytes == 0, "100000 frames of Coord.Undo, two calls each, allocate nothing", bytes + " bytes");
    }

    // Grouped steps, snapshots, and steps that go stale with the document they were recorded on.
    static void HistorySteps() {
        var clock = History.Clock;
        History.Clear(); double t = 0; History.Clock = () => t;
        int a = 0, b = 0;
        using (History.Step("Pair", "pair")) { a = 1; History.Record("A", "a", () => a = 0, () => a = 1);
                                               b = 1; History.Record("B", "b", () => b = 0, () => b = 1); }
        Check(History.NextUndo == "Pair", "a scope records one step under its own label");
        Check(History.TopKey == "pair", "TopKey is the newest entry's key", History.TopKey);
        History.Undo(); Check(a == 0 && b == 0 && !History.CanUndo, "one undo puts both back");
        History.Redo(); Check(a == 1 && b == 1, "one redo does both again");
        var log = new List<string>(); History.Clear();
        using (History.Step("Ord")) { History.Record("1", "k1", () => log.Add("u1"), () => log.Add("r1"));
                                      History.Record("2", "k2", () => log.Add("u2"), () => log.Add("r2")); }
        History.Undo(); History.Redo(); Check(string.Join(",", log) == "u2,u1,r1,r2", "undo last-first, redo first-last");
        History.Clear(); using (History.Step("Empty")) { } Check(!History.CanUndo, "an empty scope records nothing");
        bool okA = true, okB = true; History.Clear();
        using (History.Step("Outer", null, () => okA)) using (History.Step("Inner", null, () => okB)) History.Record("x", "x", () => { }, () => { });
        Check(History.NextUndo == "Outer", "a nested scope joins the outer step");
        okB = false; Check(!History.CanUndo, "the joined step is stale when either check fails");
        // stale: a live step under a step bound to a replaced document
        History.Clear(); int doc = 1, v = 0, w = 7;
        History.Record("W", "w", () => w = 0, () => w = 7); t += 2;
        int bound = doc; using (History.Step("Old", "old", () => doc == bound)) { v = 5; History.Record("V", "v", () => v = 0, () => v = 5); }
        doc = 2;
        Check(History.NextUndo == "W", "the header never names a stale step");
        Check(History.Undo() && w == 0 && v == 5, "undo skips the stale step and undoes the next live one");
        Check(!History.CanUndo && !History.Undo(), "nothing live is left");
        History.Clear(); using (History.Step("Throws", null, () => { throw new Exception("x"); })) History.Record("y", "y", () => { }, () => { });
        Check(!History.CanUndo, "a throwing validity check counts as stale");
        // bound at record time, by reference: a document that comes back brings its steps back
        History.Clear(); object docA = new object(), docB = new object(), cur = docA; int ea = 0, eb = 0;
        using (History.Step("EA", "ea", History.BoundTo(() => cur))) { ea = 1; History.Record("a", "a", () => ea = 0, () => ea = 1); }
        cur = docB; t += 2;
        using (History.Step("EB", "eb", History.BoundTo(() => cur))) { eb = 1; History.Record("b", "b", () => eb = 0, () => eb = 1); }
        Check(History.Undo() && eb == 0 && ea == 1 && !History.CanUndo, "only the step on the current document is live");
        cur = docA; Check(History.NextUndo == "EA" && History.Undo() && ea == 0, "a step whose document comes back is live again");
        // keyless steps never coalesce
        History.Clear(); t = 20; int k = 0;
        using (History.Step("D1")) { k = 1; History.Record("x", "x1", () => k = 0, () => k = 1); }
        t += 0.1; using (History.Step("D2")) { k = 2; History.Record("x", "x2", () => k = 1, () => k = 2); }
        History.Undo(); Check(k == 1 && History.CanUndo, "two keyless scopes 0.1 s apart are two steps");
        // snapshot
        History.Clear(); var s = new List<int> { 1, 2 }; Func<string> cap = () => string.Join(",", s);
        Action<string> put = x => { s.Clear(); foreach (var p in x.Split(',')) s.Add(int.Parse(p)); };
        History.Snapshot("Add", "doc", cap, put, () => { s.Add(3); History.Record("inner", "inner", () => { }, () => { }); });
        Check(cap() == "1,2,3" && History.NextUndo == "Add", "a snapshot runs the change and records one step");
        History.Undo(); Check(cap() == "1,2" && !History.CanUndo, "undo restores the before capture; the inner record was suppressed");
        History.Redo(); Check(cap() == "1,2,3", "redo restores the after capture");
        History.Clear(); History.Snapshot("None", "doc", cap, put, () => { }); Check(!History.CanUndo, "equal captures record nothing");
        History.Clear(); t = 10; History.Snapshot("N", "n", cap, put, () => s.Add(4)); t += 0.3; History.Snapshot("N", "n", cap, put, () => s.Add(5));
        History.Undo(); Check(cap() == "1,2,3" && !History.CanUndo, "coalesced snapshots keep the first before");
        // a throwing part is logged and the step's other parts still run
        var hl = new List<string>(); var oldLog = History.Log; History.Log = hl.Add;
        History.Clear(); int c1 = 1, c2 = 1;
        using (History.Step("Parts")) { History.Record("1", "p1", () => c1 = 0, () => c1 = 1);
                                        History.Record("2", "p2", () => { throw new Exception("part"); }, () => { });
                                        History.Record("3", "p3", () => c2 = 0, () => c2 = 1); }
        Check(History.Undo() && c1 == 0 && c2 == 0 && hl.Count == 1, "a throwing part is logged and the others still run", string.Join(" | ", hl));
        // a record never coalesces into a stale top entry
        History.Clear(); t = 30; bool live = true; int z = 0;
        using (History.Step("Z", "z", () => live)) { z = 1; History.Record("z", "z", () => z = 0, () => z = 1); }
        live = false; t += 0.1; z = 2; History.Record("Z", "z", () => z = 1, () => z = 2);
        Check(History.Undo() && z == 1 && !History.CanUndo, "a same-key record beside a stale top is a step of its own", "z=" + z);
        // a drag's steps on one document coalesce without chaining a check per frame
        History.Clear(); t = 40; object dd = new object(); int reads = 0; float drag = 0;
        Func<object> docf = () => { reads++; return dd; };
        for (int i = 1; i <= 50; i++) { float was = drag; drag = i; float now = drag; t += 0.01;
            using (History.Step("Drag", "drag", History.BoundTo(docf))) History.Record("Drag", "drag", () => drag = was, () => drag = now); }
        reads = 0;
        Check(History.CanUndo && reads == 1, "coalesced steps on one document keep one validity check", reads + " reads");
        Check(History.Undo() && drag == 0 && !History.CanUndo, "...and stay one step");
        // a step opened while recording is off (inside a snapshot's change) leaves the outer step's check alone
        History.Clear(); t = 60; object imp = new object(), was0 = imp; int bnd = 0;
        Func<object> impf = () => imp;
        using (History.Step("Import", "import"))
            History.Snapshot("Import", "import", impf, x => imp = x, () => { imp = new object();
                using (History.Step("Row", "row", History.BoundTo(impf))) { bnd = 1; History.Record("Row", "row", () => bnd = 0, () => bnd = 1); } });
        Check(History.Undo() && ReferenceEquals(imp, was0), "undoing an import puts the old document back");
        Check(History.CanRedo && History.Redo() && !ReferenceEquals(imp, was0), "...and the import can be redone although its change wrote a row bound to the new document");
        // steps on a document that cannot be read are dropped, so a long drag never pushes live steps out
        History.Clear(); t = 70; int lv = 0;
        History.Record("Live", "live", () => lv = 0, () => lv = 1); lv = 1; t += 2;
        var bl = new List<string>(); History.Log = bl.Add; Throttle.Reset();
        Func<object> gone = () => { throw new Exception("gone"); };
        for (int i = 0; i < History.Limit + 50; i++) { t += 0.01;
            using (History.Step("Lost", "lost", History.BoundTo(gone))) History.Record("Lost", "lost", () => { }, () => { }); }
        History.Log = hl.Add;
        Check(History.NextUndo == "Live" && History.Undo() && lv == 0, "steps on an unreadable document leave the live step below them in place and undoable", History.NextUndo ?? "null");
        Check(bl.Count == 1 && bl[0].Contains("History.BoundTo"), "...and the unreadable document is logged once", string.Join(" | ", bl));
        // a drag whose check is false while it runs is still one step: its frames share that check
        History.Clear(); t = 90; bool open = false; Func<bool> gate = () => open; float g = 0;
        for (int i = 1; i <= 50; i++) { float was = g; g = i; float now = g; t += 0.01;
            using (History.Step("Gate", "gate", gate)) History.Record("Gate", "gate", () => g = was, () => g = now); }
        open = true;
        Check(History.Undo() && g == 0 && !History.CanUndo, "a stale step takes the next frames that carry its own check", "g=" + g);
        // the throwing parts of one undo are one log line
        var tl = new List<string>(); History.Log = tl.Add; History.Clear();
        using (History.Step("Three")) for (int i = 0; i < 3; i++) { int n = i; History.Record("p", "p" + n, () => { throw new Exception("part " + n); }, () => { }); }
        History.Undo(); History.Log = hl.Add;
        Check(tl.Count == 1 && tl[0].Contains("undo Three: 3 parts threw: part 2"), "an undo whose 3 parts throw logs one line", string.Join(" | ", tl));
        // Clear abandons a scope left open: later records are steps of their own, and its Dispose does nothing
        History.Clear(); t = 100; int ab = 0;
        var held = History.Step("Held", "held");
        History.Clear();
        ab = 1; History.Record("After", "after", () => ab = 0, () => ab = 1);
        Check(History.NextUndo == "After", "after Clear, a record is a step of its own, not part of a scope left open", History.NextUndo ?? "null");
        held.Dispose();
        Check(History.NextUndo == "After" && History.Undo() && ab == 0 && !History.CanUndo, "...and disposing the abandoned scope later does nothing");
        // a scope still open 5 s after it opened is closed by the next record, logged once
        var ol = new List<string>(); History.Log = ol.Add; History.Clear(); t = 200; int lo = 0, nx = 0;
        var lost = History.Step("Lost scope", "lostscope");
        lo = 1; History.Record("in", "in", () => lo = 0, () => lo = 1);
        t += 6; nx = 1; History.Record("Next", "next", () => nx = 0, () => nx = 1);
        History.Log = hl.Add;
        Check(History.NextUndo == "Next" && ol.Count == 1 && ol[0].Contains("a history step stayed open: Lost scope"),
              "a record 5 s after a scope opened closes that scope first, logged once", (History.NextUndo ?? "null") + " / " + string.Join(" | ", ol));
        Check(History.Undo() && nx == 0 && History.NextUndo == "Lost scope" && History.Undo() && lo == 0, "...and what the scope recorded is still one step under its label");
        lost.Dispose(); Check(!History.CanUndo && History.CanRedo, "...and disposing it later records nothing and keeps the redo branch");
        // a drag's coalescing frames allocate nothing inside History, as a bound step or a plain record
        History.Clear(); t = 300; object ad = new object(); Func<object> adf = () => ad; Action nop = () => { };
        for (int i = 0; i < 3; i++) { t += 0.01; using (History.Step("A", "alloc", History.BoundTo(adf))) History.Record("A", "alloc", nop, nop); }
        long ab0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { t += 0.001; using (History.Step("A", "alloc", History.BoundTo(adf))) History.Record("A", "alloc", nop, nop); }
        ab0 = GC.GetAllocatedBytesForCurrentThread() - ab0;
        Check(ab0 == 0, "1000 coalescing frames of a document-bound step allocate nothing", ab0 + " bytes");
        History.Clear(); for (int i = 0; i < 3; i++) { t += 0.01; History.Record("R", "rec", nop, nop); }
        long ab1 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { t += 0.001; History.Record("R", "rec", nop, nop); }
        ab1 = GC.GetAllocatedBytesForCurrentThread() - ab1;
        Check(ab1 == 0 && History.Undo() && !History.CanUndo, "1000 coalescing records allocate nothing and stay one step", ab1 + " bytes");
        // a row's drag frame moves the end of the step it recorded last instead of recording again
        History.Clear(); t = 400; float rv = 0; var rend = new[] { 1f };
        Action rredo = () => rv = rend[0];
        History.Record("Row", "row", () => rv = 0, rredo); rv = 1;
        t += 0.1; Check(History.Extend("row", null, rredo), "a write that only moves the end of the row's own top step extends it");
        rend[0] = 2f; rv = 2;
        Check(!History.Extend("row", null, () => { }) && !History.Extend("other", null, rredo), "...only for that step's own redo and key");
        t += 2; Check(!History.Extend("row", null, rredo), "...and only within CoalesceSeconds");
        History.Undo(); Check(rv == 0 && History.Redo() && rv == 2f, "...so the step's undo puts back the first value and its redo the newest");
        // a step on a document that throws is not kept, and the throw is logged once
        var gl = new List<string>(); History.Log = gl.Add; Throttle.Reset(); History.Clear(); t = 500;
        for (int i = 0; i < 3; i++) { t += 2;
            using (History.Step("Gone", "gone" + i, History.BoundTo(() => { throw new Exception("no doc"); }))) History.Record("x", "x", () => { }, () => { }); }
        Check(!History.CanUndo && gl.Count == 1 && gl[0].Contains("History.BoundTo: no doc"), "steps on a document that throws are not kept, and the throw is logged once", string.Join(" | ", gl));
        // three reads of a throwing validity check log once
        gl.Clear(); History.Clear();
        using (History.Step("Bad", "bad", () => { throw new Exception("bad check"); })) History.Record("x", "x", () => { }, () => { });
        bool canBad = History.CanUndo; string nextBad = History.NextUndo; bool didBad = History.Undo();
        Check(!canBad && nextBad == null && !didBad && gl.Count == 1, "three reads of a throwing validity check log it once", string.Join(" | ", gl));
        // keyless records never coalesce
        History.Clear(); t = 600; int kr = 1;
        History.Record("K", null, () => kr = 0, () => kr = 1);
        t += 0.1; kr = 2; History.Record("K", null, () => kr = 1, () => kr = 2);
        Check(History.Undo() && kr == 1 && History.Undo() && kr == 0, "two keyless records 0.1 s apart are two steps", "kr=" + kr);
        // a Step 5 s after a scope leaked closes the leak first, so the leak's stale check is not ANDed into the new step
        ol.Clear(); History.Log = ol.Add; Throttle.Reset(); History.Clear(); t = 700; int lk = 0, fr = 0;
        var leak = History.Step("Leak", "leak", () => false);
        lk = 1; History.Record("in", "in", () => lk = 0, () => lk = 1);
        t += 6;
        using (History.Step("Fresh", "fresh")) { fr = 1; History.Record("f", "f", () => fr = 0, () => fr = 1); }
        Check(History.NextUndo == "Fresh" && History.Undo() && fr == 0 && ol.Count == 1 && ol[0].Contains("stayed open: Leak"),
            "a Step 5 s after a scope leaked closes the leak first and keeps its own validity", (History.NextUndo ?? "null") + " / " + string.Join(" | ", ol));
        leak.Dispose();
        // a write on a document that cannot be read is still a change: the redo branch ends
        History.Log = gl.Add; History.Clear(); t = 800; int dr = 1;
        History.Record("D", "d", () => dr = 0, () => dr = 1); History.Undo();
        using (History.Step("Gone", "gone", History.BoundTo(() => { throw new Exception("no doc"); }))) History.Record("x", "x", () => { }, () => { });
        Check(!History.CanRedo, "a write on a document that cannot be read still ends the redo branch");
        // undo and redo hand AfterApply's handlers out without allocating
        History.Clear(); t = 900; Action ap1 = () => { }, ap2 = () => { };
        History.AfterApply += ap1; History.AfterApply += ap2;
        History.Record("U", "u", nop, nop); History.Undo(); History.Redo();
        long aa = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { History.Undo(); History.Redo(); }
        aa = GC.GetAllocatedBytesForCurrentThread() - aa;
        History.AfterApply -= ap1; History.AfterApply -= ap2;
        Check(aa == 0, "1000 undo/redo pairs with two AfterApply handlers allocate nothing", aa + " bytes");
        History.Log = hl.Add;
        History.Log = oldLog; History.Clock = clock; History.Clear();
    }

    // The canvas's snapping, grid, resize, marquee, z-order, zoom and fit, in canvas units with y down.
    static void CanvasGeometry() {
        Check(CanvasMath.GridStep(5, 1) == 5 && CanvasMath.GridStep(5, 2) == 3 && CanvasMath.GridStep(5, 0.5f) == 10
           && CanvasMath.GridStep(5, 4) == 1 && CanvasMath.GridStep(5, 0.3f) == 17 && CanvasMath.GridStep(0, 1) == 1, "grid step by zoom");
        Check(CanvasMath.GridPitch(5, 1) == 5 && CanvasMath.GridPitch(5, 0.5f) == 10 && CanvasMath.GridPitch(5, 0.3f) == 20, "drawn grid pitch");
        float gp = CanvasMath.GridPitch(8, 1e-30f);
        Check(CanvasMath.GridPitch(0, 1) == 0 && CanvasMath.GridPitch(-5, 1) == 0 && CanvasMath.GridPitch(float.NaN, 1) == 0
           && CanvasMath.GridPitch(float.PositiveInfinity, 1) == 0 && CanvasMath.GridPitch(5, 0) == 0 && CanvasMath.GridPitch(5, -1) == 0
           && gp > 0 && !float.IsInfinity(gp) && CanvasMath.GridPitch(8, float.Epsilon) == 0, "no grid, or a pitch past a float, draws none (0)",
              CanvasMath.GridPitch(-5, 1) + " " + CanvasMath.GridPitch(5, 0) + " " + gp + " " + CanvasMath.GridPitch(8, float.Epsilon));
        Check(CanvasMath.LatchAxis(-10, 3) == 1 && CanvasMath.LatchAxis(2, -9) == 2, "the axis lock compares magnitudes");
        var tg = new Box[108]; tg[0] = new Box(200, 50, 60, 60);
        var ms = new MoveSnap { Targets = tg, Count = 1, CentreX = 450, CentreY = 195, Distance = 8, Step = 5, On = true };
        CanvasMath.Move(new Box(100, 300, 60, 60), 94, 0, ref ms, out var dx, out var dy, out var gx, out var gy);
        Check(dx == 100 && gx == 200 && float.IsNaN(gy), "left snaps to a target's left within 8, with a guide");
        CanvasMath.Move(new Box(100, 300, 60, 60), 316, 0, ref ms, out dx, out _, out gx, out _);
        Check(dx == 320 && gx == 450, "the centre line snaps to the canvas centre");
        CanvasMath.Move(new Box(100, 300, 60, 60), 0, -130, ref ms, out dx, out dy, out gx, out gy);
        Check(dy == -135 && gy == 195 && dx == 0 && float.IsNaN(gx), "the centre line snaps to the canvas centre on y too", dy + " " + gy);
        CanvasMath.Move(new Box(100, 300, 60, 60), 13, 0, ref ms, out dx, out _, out gx, out _);
        Check(dx == 15 && float.IsNaN(gx), "no line in range: the left rounds to the grid step");
        // A centre line of an odd size is a half unit: the snapped box lands on a whole unit, the guide on the line.
        CanvasMath.Move(new Box(100, 300, 45, 45), 330, 0, ref ms, out dx, out _, out gx, out _);
        Check(dx == 328 && gx == 450, "an odd box centred on the canvas centre lands on a whole unit", dx + " " + gx);
        tg[0] = new Box(200, 50, 61, 60);
        CanvasMath.Move(new Box(100, 300, 60, 60), 128.6f, 0, ref ms, out dx, out _, out gx, out _);
        Check(dx == 131 && gx == 230.5f, "a left snapped to an odd target's centre lands on a whole unit", dx + " " + gx);
        // The position rounds, not the delta: a box off a whole unit still lands on one (rounding the delta would give 230.3).
        CanvasMath.Move(new Box(100.3f, 300, 60, 60), 128.6f, 0, ref ms, out dx, out _, out gx, out _);
        Check(100.3f + dx == 231 && gx == 230.5f, "a box at a fractional x snaps onto a whole unit", (100.3f + dx) + " " + gx);
        // Shift latched a mostly horizontal drag: y stays put though a target's top is in reach, x still snaps.
        ms.Keep = CanvasMath.LatchAxis(40, 1);
        CanvasMath.Move(new Box(100, 45, 60, 60), 40, 1, ref ms, out dx, out dy, out gx, out gy);
        Check(dy == 0 && float.IsNaN(gy) && dx == 40 && gx == 200, "a Shift-latched move leaves the locked axis alone", dx + " " + dy + " " + gx + " " + gy);
        ms.On = false; CanvasMath.Move(new Box(100, 45, 60, 60), 40.4f, 3.4f, ref ms, out dx, out dy, out _, out _);
        Check(dx == 40 && dy == 0, "with snapping off the locked axis stays put too", dx + " " + dy);
        ms.Keep = 0;
        ms.On = false; CanvasMath.Move(new Box(100, 300, 60, 60), 94.4f, 0, ref ms, out dx, out _, out gx, out _);
        Check(dx == 94 && float.IsNaN(gx), "snap off or Alt: one-unit steps, no guide");
        var ss = new SizeSnap { Widths = new float[] { 83 }, Heights = new float[] { 0 }, Count = 1, Distance = 4, Min = 10, Step = 5, On = true };
        var r = CanvasMath.Resize(new Box(0, 0, 60, 40), 7, 22, 3, ref ss);
        Check(r.X == 0 && r.Y == 0 && r.W == 83 && r.H == 45, "bottom-right: size rounds to 5, then snaps to a sibling width within 4");
        ss.KeepAspect = true; ss.Count = 0; r = CanvasMath.Resize(new Box(0, 0, 60, 40), 7, 30, 2, ref ss);
        Check(r.W == 90 && r.H == 60, "Shift keeps the aspect, the larger change driving");
        r = CanvasMath.Resize(new Box(0, 0, 60, 40), 4, 30, 0, ref ss);
        Check(r.X == 0 && r.W == 90 && r.H == 60 && r.CY == 20, "Shift on an edge: the other side follows, centred on the start's centre line");
        r = CanvasMath.Resize(new Box(0, 0, 60, 40), 4, 35, 0, ref ss);
        Check(r.W == 95 && r.H == 63 && r.Y == -12, "an odd growth centres within half a unit, on a whole unit", r.Y + " " + r.W + "x" + r.H);
        r = CanvasMath.Resize(new Box(0, 0, 60, 40), 0, 80, 80, ref ss);
        Check(r.W == 15 && r.H == 10 && r.Right == 60 && r.Bottom == 40, "with Shift the minimum keeps the aspect");
        ss.KeepAspect = false; r = CanvasMath.Resize(new Box(0, 0, 60, 40), 0, 80, 80, ref ss);
        Check(r.W == 10 && r.H == 10 && r.Right == 60 && r.Bottom == 40, "top-left past the far edge stops at the minimum, far edges fixed");
        ss.On = false; ss.Count = 1; r = CanvasMath.Resize(new Box(0, 0, 60, 40), 7, 22.4f, 3.6f, ref ss);
        Check(r.W == 82 && r.H == 44, "snap off: sizes round to whole units and skip the sibling width", r.W + "x" + r.H);
        var sk = new SizeSnap { Min = 10, Step = 5, On = true, KeepAspect = true };
        var rh = CanvasMath.Resize(new Box(0, 0, 63, 41), 8, 10, 10, ref sk); var rl = CanvasMath.Resize(new Box(0, 0, 63, 41), -1, 10, 10, ref sk);
        Check(rh.X == 0 && rh.Y == 0 && rh.W == 63 && rh.H == 41 && rl.X == 0 && rl.Y == 0 && rl.W == 63 && rl.H == 41,
              "with Shift, a handle outside 0..7 leaves the box as it was", rh.X + "," + rh.Y + " " + rh.W + "x" + rh.H);
        Check(!CanvasMath.Hits(new Box(0, 0, 10, 10), new Box(10, 0, 5, 5)) && CanvasMath.Hits(new Box(0, 0, 10, 10), new Box(9, 9, 5, 5)), "marquee overlap is strict");
        var ord = new[] { 0, 1, 2, 3 }; Check(CanvasMath.StepZ(ord, new[] { false, true, false, false }, true) && string.Join("", ord) == "0213", "forward one step");
        ord = new[] { 0, 1, 2, 3 }; Check(!CanvasMath.StepZ(ord, new[] { false, false, true, true }, true), "blocked by a selected top: no change");
        ord = new[] { 0, 1, 2, 3 }; CanvasMath.StepZ(ord, new[] { true, false, true, false }, true); Check(string.Join("", ord) == "1032", "each selected one steps past its neighbour");
        ord = new[] { 0, 1, 2, 3 }; Check(CanvasMath.StepZ(ord, new[] { false, true, false, true }, false) && string.Join("", ord) == "1032", "backward mirrors forward");
        Check(CanvasMath.ClampZoom(0.1f) == 0.3f && CanvasMath.ClampZoom(9) == 4, "zoom clamps 0.3..4");
        CanvasMath.ZoomAt(1, 0, 0, 2, 100, 50, out var px, out var py); Check(px == -100 && py == -50, "zooming keeps the point under the pointer");
        CanvasMath.ZoomAt(8, 0, 0, 4, 100, 0, out px, out py); Check(px == 50 && py == 0, "the current zoom is taken as given, only the new one clamps", px + " " + py);
        CanvasMath.ZoomAt(0, 0, 0, 2, 100, 50, out px, out py); Check(px == -100 && py == -50, "a current zoom of 0 divides by 1", px + " " + py);
        CanvasMath.Fit(new Box(0, 0, 900, 390), 1000, 500, 40, out var z, out px, out py);
        Check(Math.Abs(z - 920f / 900f) < 1e-4 && Math.Abs(px - 40) < 0.01 && Math.Abs(py - (500 - 390 * z) / 2) < 0.01, "fit frames with 40 px padding");
        CanvasMath.Fit(new Box(0, 0, 0, 0), 1000, 500, 40, out z, out px, out py); Check(z == 1 && !float.IsNaN(px + py), "fitting nothing is finite");
        Check(CanvasMath.Clamp(9000, 8000) == 8000 && CanvasMath.Clamp(-9000, 8000) == -8000, "positions clamp at the limit");

        // A drag moves every frame against the whole layout: 108 targets. Even rounds start among them and snap on
        // both axes; odd rounds start out of reach of every line and land on the grid.
        var all = new Box[108];
        for (int i = 0; i < all.Length; i++) all[i] = new Box(i * 37 % 900, i * 53 % 390, 40 + i % 3 * 10, 40);
        var am = new MoveSnap { Targets = all, Count = 108, CentreX = 450, CentreY = 195, Distance = 8, Step = 5, On = true };
        CanvasMath.Move(new Box(10, 10, 60, 60), 3, 4, ref am, out var ax, out var ay, out var agx, out var agy);   // JIT first
        long mb = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) {
            float o = 10 + (i & 1) * 5000;
            CanvasMath.Move(new Box(o, o, 60, 60), i % 97, i % 89, ref am, out ax, out ay, out agx, out agy);
        }
        mb = GC.GetAllocatedBytesForCurrentThread() - mb;
        Check(mb == 0, "100000 moves over 108 snap targets allocate nothing", mb + " bytes");
    }

    // A derived scheme has an entry for every role, and the canvas's guide shows on it. Rail stays clear on purpose.
    static void GuideColour() {
        var c = Palette.FromAccent(0.9f, 0.2f, 0.2f, 1f);
        Check(c.Length == Enum.GetValues(typeof(Role)).Length && Array.TrueForAll(c, x => x != null && x.Length == 4),
              "every role has a colour in FromAccent", c.Length.ToString());
        var g = c[(int)Role.Guide];
        Check(g != null && g[3] > 0f, "Role.Guide has alpha > 0 in FromAccent", Show(g));
    }

    // A host's language tag reduced to the part Facet's tables know, and SetAccent's text colour.
    static void Conveniences() {
        Check(Words.Primary("ko-KR") == "ko" && Words.Primary("KO_kr") == "ko" && Words.Primary("zh-Hans-CN") == "zh"
           && Words.Primary("en") == "en" && Words.Primary(null) == null && Words.Primary("") == "", "BCP-47 tags reduce to the language");
        var acc = Palette.FromAccent(0.9f, 0.2f, 0.2f, 1f)[(int)Role.AccentText];
        Check(string.Join(",", Palette.Contrast(0.9f, 0.2f, 0.2f)) == string.Join(",", acc), "SetAccent derives the same accent text");
        Check(Words.Own("Search", "ko-KR") == "검색", "Facet's own table reads a full language tag");
        Check(Cjk.Script("kor") == null && Cjk.Script("kO_kr") == "ko", "the CJK face reads the same primary subtag as Facet's words");
    }

    // Menu shortcut text: modifiers and a key named as Unity's KeyCode, shown the platform's way.
    static void Shortcuts() {
        Check(Shortcut.TryParse("Ctrl+Shift+C", out var c, out var s, out var a, out var k) && c && s && !a && k == "C", "a chord parses");
        Check(Shortcut.TryParse("Cmd+Z", out c, out _, out _, out k) && c && k == "Z", "Cmd counts as Ctrl");
        Check(Shortcut.TryParse("]", out c, out _, out _, out k) && !c && k == "RightBracket", "brackets map to key names");
        Check(Shortcut.TryParse("Ctrl++", out c, out _, out _, out k) && c && k == "Equals", "plus is the equals key");
        Check(!Shortcut.TryParse("", out _, out _, out _, out _) && !Shortcut.TryParse("Ctrl+", out _, out _, out _, out _), "empty or dangling fails");
        Check(Shortcut.Display("Ctrl+Shift+C", true) == "\u2318\u21E7C" && Shortcut.Display("Ctrl+Shift+C", false) == "Ctrl+Shift+C", "platform display");
        Check(Shortcut.Display("Ctrl+Alpha1", true) == "\u23181" && Shortcut.Display("Ctrl+Shift+Space", true) == "\u2318\u21E7Space"
            && Shortcut.Display("Ctrl+Alpha1", false) == "Ctrl+1" && Shortcut.Display("Alt+Mouse3", false) == "Alt+Mouse3",
            "a chord on a key no menu names still shows its modifiers as a menu does", Shortcut.Display("Ctrl+Alpha1", true));
        Check(ValueScale.ScrubUnit(0f, float.MinValue, float.MaxValue) == 1f && ValueScale.ScrubUnit(0f, 0f, 100f) == 0.5f
            && ValueScale.ScrubUnit(2f, 0f, 100f) == 2f && ValueScale.ScrubUnit(0f, 5f, 5f) == 1f,
            "a scrub unit is finite even when the range overflows a float", ValueScale.ScrubUnit(0f, float.MinValue, float.MaxValue).ToString());
        Check(Shortcut.TryParse("Ctrl+0", out c, out _, out _, out k) && c && k == "Alpha0", "a digit is its Alpha key", k);
        Check(Shortcut.TryParse("[", out _, out _, out _, out k) && k == "LeftBracket" && Shortcut.TryParse("-", out _, out _, out _, out k) && k == "Minus"
              && Shortcut.TryParse("=", out _, out _, out _, out k) && k == "Equals", "[ - = map to LeftBracket, Minus, Equals", k);
        Check(Shortcut.TryParse("Del", out _, out _, out _, out k) && k == "Delete" && Shortcut.TryParse("Delete", out _, out _, out _, out k) && k == "Delete"
              && Shortcut.TryParse("Backspace", out _, out _, out _, out k) && k == "Backspace", "Del and Delete are Delete; Backspace stays", k);
        Check(Shortcut.TryParse("Up", out _, out _, out _, out k) && k == "UpArrow" && Shortcut.TryParse("Right", out _, out _, out _, out k) && k == "RightArrow", "arrows take KeyCode's names", k);
        Check(Shortcut.TryParse("F12", out _, out _, out _, out k) && k == "F12" && !Shortcut.TryParse("F13", out _, out _, out _, out _), "F1 to F12 only", k);
        Check(Shortcut.TryParse("alt+shift+x", out c, out s, out a, out k) && !c && s && a && k == "X", "modifiers in any order and case; a letter is upper-cased", k);
        Check(!Shortcut.TryParse("Ctrl+Foo", out _, out _, out _, out _) && !Shortcut.TryParse("C+X", out _, out _, out _, out _), "an unknown key or modifier fails");
        Check(Shortcut.Display("Alt+Shift+Cmd+]", true) == "\u2318\u21E7\u2325]" && Shortcut.Display("shift+alt+del", false) == "Shift+Alt+Del",
              "display orders the modifiers and keeps the key as written", Shortcut.Display("Alt+Shift+Cmd+]", true) + " / " + Shortcut.Display("shift+alt+del", false));
        Check(Shortcut.Display("nonsense+", false) == "nonsense+" && Shortcut.Display(null, true) == "", "text that does not parse shows as given");
    }

    // The in-game autotest holds the speed against other mods' per-frame writes.
    static void MotionHold() {
        Motion.Speed = 2f;
        using (Motion.Hold(1f, false)) {
            Motion.Speed = 3f;   // a bridge's per-frame write
            Check(Motion.Speed == 1f && !Motion.Smooth, "a held speed ignores other writers");
            using (Motion.Hold(0.25f, true)) Check(Motion.Speed == 0.25f && Motion.Smooth, "the newest hold wins");
            Check(Motion.Speed == 1f, "disposing a nested hold restores the outer one");
        }
        Check(Motion.Speed == 3f, "with no hold the store's last write shows");
        Motion.Speed = 1f;
    }

    // One row over many items: the first item's value, a mixed state, and per-item undo.
    static void ManyBinds() {
        var clock = History.Clock;
        double t = 0; History.Clock = () => t;
        var items = new List<int[]> { new[] { 1 }, new[] { 1 }, new[] { 3 } };
        var sel = new List<int[]>(items); var m = Bind.Many<int[], int>(() => sel, x => x[0], (x, v) => x[0] = v);
        Check(m.Get() == 1 && m.Mixed, "Many reads the first item and knows the selection is mixed");
        History.Clear(); m.Set(7); Check(items.TrueForAll(x => x[0] == 7) && !m.Mixed && !History.CanUndo, "Set writes every item and records nothing");
        items[0][0] = 1; items[1][0] = 1; items[2][0] = 3;
        int applied = 0; bool applying = true;
        m.SetRecorded(7, "V", "k", null, _ => { applied++; applying &= History.Applying; }); sel.RemoveAt(2);   // the selection changes before the undo
        History.Undo(); Check(items[0][0] == 1 && items[1][0] == 1 && items[2][0] == 3, "undo restores each captured item's own value");
        History.Redo(); Check(items.TrueForAll(x => x[0] == 7), "redo writes the captured items again");
        Check(applied == 2 && applying, "applied runs once per undo and redo, while Applying");
        int pv = 0, pa = 0; var p = Bind.To(() => pv, x => pv = x); History.Clear();
        p.SetRecorded(4, "P", "p", null, _ => pa++); History.Undo(); History.Redo(); Check(pv == 4 && pa == 2, "a plain bind runs applied too");
        Check(!Bind.To(() => 0, _ => { }).Mixed, "a plain bind is never mixed");
        // by key: the consumer rebuilt its items under the same keys before the undo
        var objs = new List<int[]> { new[] { 1, 0 }, new[] { 2, 1 } };   // [value, key]
        var keyed = Bind.Many<int[], int>(() => objs, x => x[0], (x, v) => x[0] = v, x => x[1].ToString());
        History.Clear(); keyed.SetRecorded(9, "K", "keyed", null); var old = objs;
        objs = new List<int[]> { new[] { 9, 0 }, new[] { 9, 1 } };
        History.Undo(); Check(objs[0][0] == 1 && objs[1][0] == 2 && old[0][0] == 9, "a keyed step restores the clones and leaves the dropped originals");
        objs.RemoveAt(0); History.Redo(); Check(objs.Count == 1 && objs[0][0] == 9, "...and a key that has gone is skipped, not thrown on");
        // two selections in one tick are two steps
        var sa = new List<int[]> { items[0] }; var sb = new List<int[]> { items[1] }; var cs = sa;
        var two = Bind.Many<int[], int>(() => cs, x => x[0], (x, v) => x[0] = v);
        History.Clear(); two.SetRecorded(5, "T", "two", null); cs = sb; two.SetRecorded(6, "T", "two", null);
        History.Undo(); Check(History.CanUndo && items[0][0] == 5, "a new selection under the same row key is a new step");
        // ...also inside a document-bound step, 0.1 s apart and with no rebuild between
        var doc = new object(); Func<bool> valid = History.BoundTo(() => doc);
        cs = sa; History.Clear(); two.SetRecorded(1, "T", "two", null, null, valid); t += 0.1; cs = sb; two.SetRecorded(2, "T", "two", null, null, valid);
        bool u1 = History.Undo(); Check(u1 && History.CanUndo && items[1][0] == 7 && items[0][0] == 1, "two selections in document-bound steps 0.1 s apart are two entries", u1 + " " + History.CanUndo + " " + items[0][0] + " " + items[1][0]);
        // one selection dragged: one step, undone to each item's own value
        items[0][0] = 1; items[1][0] = 3; var both = new List<int[]> { items[0], items[1] }; cs = both;
        History.Clear(); for (int i = 0; i < 5; i++) { t += 0.05; two.SetRecorded(10 + i, "T", "two", null); }
        Check(History.Undo() && !History.CanUndo && items[0][0] == 1 && items[1][0] == 3 && two.Mixed, "a drag over one selection is one step, and its undo brings the mix back");
        Check(History.Redo() && items[0][0] == 14 && items[1][0] == 14, "...and its redo writes the drag's last value");
        History.Clear(); two.SetRecorded(14, "T", "two", null); Check(!History.CanUndo, "a write that changes no item records nothing");
        cs = new List<int[]>(); two.Set(3); two.SetRecorded(3, "T", "two", null);
        Check(two.Get() == 0 && !two.Mixed && !History.CanUndo, "with no items Get is default, nothing is written and nothing recorded");
        // two bind instances under one row key (a rebuilt row) over different sets, 0.1 s apart
        items[0][0] = 1; items[1][0] = 2;
        var ra = Bind.Many<int[], int>(() => sa, x => x[0], (x, v) => x[0] = v);
        var rb = Bind.Many<int[], int>(() => sb, x => x[0], (x, v) => x[0] = v);
        History.Clear(); ra.SetRecorded(5, "R", "rebuilt", null); t += 0.1; rb.SetRecorded(6, "R", "rebuilt", null);
        bool r1 = History.Undo();
        Check(r1 && History.CanUndo && items[0][0] == 5 && items[1][0] == 2, "two rebuilt Many rows under one key over different sets are two entries", r1 + " " + History.CanUndo + " " + items[0][0] + " " + items[1][0]);
        // a drag frame over the same set allocates nothing once the step is recorded
        items[0][0] = 1; items[1][0] = 3; cs = both; History.Clear(); two.SetRecorded(20, "T", "two", null); two.SetRecorded(21, "T", "two", null);
        long mb = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { t += 0.01; two.SetRecorded(30 + i % 50, "T", "two", null); }
        mb = GC.GetAllocatedBytesForCurrentThread() - mb;
        Check(mb == 0, "a Many drag frame over one set allocates nothing", mb + " bytes");
        Check(History.Undo() && !History.CanUndo && items[0][0] == 1 && items[1][0] == 3, "...and it is still one step, undone to each item's value");
        Check(History.Redo() && items[0][0] == 79 && items[1][0] == 79, "...whose redo writes the drag's last value", items[0][0].ToString());
        // pinned: a commit after the selection moved on still writes the items the edit began on
        var pa1 = new[] { 1 }; var pb1 = new[] { 2 }; var pcur = new List<int[]> { pa1 };
        var pin = Bind.Many<int[], int>(() => pcur, x => x[0], (x, v) => x[0] = v);
        History.Clear(); pin.Pin(true); pcur = new List<int[]> { pb1 }; pin.SetRecorded(9, "N", "pin", null); pin.Pin(false);
        Check(pa1[0] == 9 && pb1[0] == 2 && pin.Get() == 2, "a pinned Many bind writes the items pinned, not the new selection", pa1[0] + " " + pb1[0]);
        Check(History.Undo() && pa1[0] == 1, "...and its step restores them");
        // a step keeps neither the items delegate nor what it captures (a canvas, its page)
        History.Clear();
        var weakItems = ManyStep(true); var weakPlain = ManyStep(false);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!weakItems.IsAlive && !weakPlain.IsAlive && History.CanUndo, "a Many step lets its items delegate's page be collected", weakItems.IsAlive + " " + weakPlain.IsAlive);
        History.Clock = clock; History.Clear();
    }

    // A write composed with a follow-up: after runs once per write, undo and redo, for a plain and a Many bind.
    static void BindThen() {
        int seen = 0;
        var b = Bind.Value(1).Then(v => seen = v);
        b.Set(5); Check(b.Get() == 5 && seen == 5, "Then runs the write, then the follow-up with the value");
        var log = History.Log; var lines = new List<string>(); History.Log = lines.Add;
        var bad = Bind.Value(1).Then(v => { throw new InvalidOperationException("after"); });
        bad.Set(3); History.Log = log;
        Check(bad.Get() == 3 && lines.Count == 1 && lines[0].Contains("Bind.Then"), "a throwing follow-up leaves the value set and logs once", string.Join("|", lines));
        int pv = 0, pa = 0; var runs = new List<int>();
        var plain = Bind.To(() => pv, x => pv = x).Then(runs.Add); History.Clear();
        plain.SetRecorded(4, "P", "then-plain", null, _ => pa++); History.Undo(); History.Redo();
        Check(pv == 4 && pa == 2 && string.Join(",", runs) == "4,0,4", "a plain Then bind runs the follow-up after its write, undo and redo", string.Join(",", runs));
        var items = new List<int[]> { new[] { 1 }, new[] { 2 } }; var mruns = new List<int>();
        var many = Bind.Many<int[], int>(() => items, x => x[0], (x, v) => x[0] = v).Then(mruns.Add);
        Check(many.Mixed && many.Get() == 1, "a Many Then bind still reports Mixed");
        History.Clear(); many.SetRecorded(7, "M", "then-many", null);
        Check(items[0][0] == 7 && items[1][0] == 7 && !many.Mixed && string.Join(",", mruns) == "7", "...writes every item and runs the follow-up once", string.Join(",", mruns));
        History.Undo(); Check(items[0][0] == 1 && items[1][0] == 2 && many.Mixed, "...undoes each item to its own value");
        History.Redo(); Check(string.Join(",", mruns) == "7,1,7", "...and runs the follow-up once per undo and redo", string.Join(",", mruns));
        var clock = History.Clock; double t = 0; History.Clock = () => t;
        int dn = 0; var drag = Bind.Many<int[], int>(() => items, x => x[0], (x, v) => x[0] = v).Then(_ => dn++);
        Action<int> ap = _ => { }; History.Clear(); drag.SetRecorded(20, "M", "then-drag", null, ap); drag.SetRecorded(21, "M", "then-drag", null, ap);
        long mb = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { t += 0.01; drag.SetRecorded(30 + i % 50, "M", "then-drag", null, ap); }
        mb = GC.GetAllocatedBytesForCurrentThread() - mb; History.Clock = clock;
        Check(mb == 0 && dn == 202, "a Many Then drag frame allocates nothing", mb + " bytes, " + dn + " follow-ups");
        // One order for both kinds on undo and redo, as on a write: the follow-up, then the row's applied.
        var order = new List<string>(); int ov = 0;
        var po = Bind.To(() => ov, x => ov = x).Then(_ => order.Add("then"));
        History.Clear(); po.SetRecorded(1, "O", "then-order-p", null, _ => order.Add("applied")); order.Clear();
        History.Undo(); History.Redo();
        Check(string.Join(",", order) == "then,applied,then,applied", "a plain Then bind runs the follow-up before the row's applied on undo and redo", string.Join(",", order));
        var oitems = new List<int[]> { new[] { 1 } };
        var mo = Bind.Many<int[], int>(() => oitems, x => x[0], (x, v) => x[0] = v).Then(_ => order.Add("then"));
        History.Clear(); mo.SetRecorded(2, "O", "then-order-m", null, _ => order.Add("applied")); order.Clear();
        History.Undo(); History.Redo();
        Check(string.Join(",", order) == "then,applied,then,applied", "a Many Then bind runs the follow-up before the row's applied on undo and redo", string.Join(",", order));
        int both = 0; var twice = Bind.Value(0).Then(_ => both++).Then(_ => both += 10); twice.Set(1);
        Check(both == 11, "two Thens run both follow-ups in order");
        History.Clear();
    }

    // A Many row whose items delegate captures a "page"; one recorded write, then only the weak reference is left.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference ManyStep(bool keyed) {
        var page = new object[] { new List<int[]> { new[] { 1, 0 } } };
        Func<IReadOnlyList<int[]>> items = () => (List<int[]>)page[0];
        var b = keyed ? Bind.Many<int[], int>(items, x => x[0], (x, v) => x[0] = v, x => x[1].ToString(), Fixed((List<int[]>)page[0]))
                      : Bind.Many<int[], int>(items, x => x[0], (x, v) => x[0] = v);
        b.SetRecorded(keyed ? 4 : 5, "W", keyed ? "weak-k" : "weak", null, _ => { });
        return new WeakReference(page);
    }

    static Func<IReadOnlyList<int[]>> Fixed(List<int[]> l) { return () => l; }

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
