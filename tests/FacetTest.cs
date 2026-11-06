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
        Console.WriteLine(_fail == 0 ? "ALL PASS" : _fail + " FAILED");
    }
}
