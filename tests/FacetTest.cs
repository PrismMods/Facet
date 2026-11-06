using System;
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
        Console.WriteLine(_fail == 0 ? "ALL PASS" : _fail + " FAILED");
    }
}
