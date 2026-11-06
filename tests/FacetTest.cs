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

        Console.WriteLine(_fail == 0 ? "ALL PASS" : _fail + " FAILED");
    }
}
