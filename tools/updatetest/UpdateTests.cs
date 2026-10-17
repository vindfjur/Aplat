using System;
using System.Reflection;

// The update check's answer parsing and version comparison, through reflection (the class is internal).
// No network is used.   tools\paneltest\run.ps1 -Source tools\updatetest\UpdateTests.cs
class UpdateTests
{
    static int fails;
    static void Check(bool ok, string msg) { Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    static int Main()
    {
        var t = Assembly.Load("Aplat").GetType("Aplat.UpdateCheck");
        var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        Func<string, Version, object> eval = (json, cur) => t.GetMethod("Evaluate", flags).Invoke(null, new object[] { json, cur });
        Func<object, string, object> get = (r, n) => r.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(r);
        var cur = new Version(1, 0, 0, 0);

        var r1 = eval("{\"url\":\"x\",\"tag_name\":\"v1.2.0\",\"name\":\"Aplat 1.2\"}", cur);
        Check((bool)get(r1, "Ok") && (bool)get(r1, "Newer"), "v1.2.0 is newer than 1.0.0.0");
        var r2 = eval("{\"tag_name\":\"1.0\"}", cur);
        Check((bool)get(r2, "Ok") && !(bool)get(r2, "Newer"), "1.0 is the same as 1.0.0.0");
        var r3 = eval("{\"tag_name\":\"v0.9.9\"}", cur);
        Check((bool)get(r3, "Ok") && !(bool)get(r3, "Newer"), "an older tag is not an update");
        var r4 = eval("{\"tag_name\":\"v1.0.0.1\"}", cur);
        Check((bool)get(r4, "Newer"), "a fourth number counts");
        foreach (var bad in new[] { null, "", "{}", "<html>rate limited</html>", "{\"tag_name\":\"nightly\"}", "{\"tag_name\":\"v1.2.3-beta\"}", "{\"tag_name\":\"v99999999.1\"}", "{\"message\":\"Not Found\"}" })
            Check(!(bool)get(eval(bad, cur), "Ok"), "not a usable answer: " + (bad ?? "null"));
        Check(((Version)t.GetProperty("Current", flags).GetValue(null)) != null, "the running version is known");
        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
