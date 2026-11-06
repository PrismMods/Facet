#!/bin/bash
# Two copies of Shared.cs under different assembly names must see one store.
set -e
cd "$(dirname "$0")/.."
d=$(mktemp -d)
mcs -target:library -out:$d/A.dll Facet/Core/Shared.cs
mcs -target:library -out:$d/B.dll Facet/Core/Shared.cs
cat > $d/drv.cs <<'X'
using System; using System.Collections.Generic; using System.Reflection;
class D {
    static object Get(Assembly a, string k, Func<List<int>> c) =>
        a.GetType("Facet.Shared").GetMethod("Get").MakeGenericMethod(typeof(List<int>)).Invoke(null, new object[] { k, c });
    static int Main(string[] args) {
        var a = Assembly.LoadFrom(args[0]); var b = Assembly.LoadFrom(args[1]);
        var la = (List<int>)Get(a, "x", () => new List<int>()); la.Add(7);
        var lb = (List<int>)Get(b, "x", () => new List<int>());
        bool ok = ReferenceEquals(la, lb) && lb[0] == 7;
        Console.WriteLine(ok ? "shared2 PASS" : "shared2 FAIL");
        return ok ? 0 : 1;
    }
}
X
mcs -out:$d/drv.exe $d/drv.cs
mono $d/drv.exe $d/A.dll $d/B.dll
rm -rf $d
