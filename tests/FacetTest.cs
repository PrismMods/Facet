using System;
using System.Collections.Generic;
using Facet;

static class FacetTest {
    static int _fail;
    static void Check(bool ok, string what) {
        if (!ok) { _fail++; Console.WriteLine("FAIL: " + what); }
    }

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
        Console.WriteLine(_fail == 0 ? "ALL PASS" : _fail + " FAILED");
    }
}
