using RynthCore.Engine.UI.Data;

namespace ChatCoordTests;

/// <summary>
/// The chat window's coordinate finder (ChatCoords): the forms players and the game write
/// coordinates in, and ordinary chat that must not become coordinates.
/// </summary>
internal static class Program
{
    private static int _checks, _failed;

    private static int Main()
    {
        One("Meet me at 42.1N, 33.6E", 42.1, 33.6, "42.1N, 33.6E");
        One("42.1N 33.6E", 42.1, 33.6, "42.1N, 33.6E");
        One("42.1N33.6E", 42.1, 33.6, "42.1N, 33.6E");
        One("loc 21.5S, 1.8W please", -21.5, -1.8, "21.5S, 1.8W");
        One("33.6E, 42.1N", 42.1, 33.6, "42.1N, 33.6E");
        One("at 42.1 N, 33.6 E now", 42.1, 33.6, "42.1N, 33.6E");
        One("(12.5n, 0.4w)", 12.5, -0.4, "12.5N, 0.4W");
        One("Your location is: 0xA9B40019 [84.000 7.100 42.005] 0.0 0.0 0.0 1.0 = 42.1N, 33.6E", 42.1, 33.6, "42.1N, 33.6E");
        One("drudges at 42N 33E", 42, 33, "42.0N, 33.0E");
        One("0.0N, 101.9W", 0, -101.9, "0.0N, 101.9W");

        // Two in one line, in order.
        var two = ChatCoords.Find("from 42.1N, 33.6E to 21.5S, 1.8W");
        Check(two.Count == 2, "two pairs");
        Check(two.Count == 2 && two[0].Ns == 42.1 && two[1].Ns == -21.5, "in order");
        Check(two.Count == 2 && two[0].Start < two[1].Start, "start positions");
        var first = two.Count > 0 ? two[0] : default;
        Check(first.Start == 5 && "from 42.1N, 33.6E to".Substring(first.Start, first.Length) == "42.1N, 33.6E", "the span covers the pair");

        // Not coordinates.
        None("give me 5 s, 2 e");
        None("lvl 120N 33E");                // off the map
        None("Holtburg is nice");
        None("brb 5 min");
        None("42.1N");                       // half a pair
        None("42.1N 33.6N");                 // two N/S
        None("v1.2.3n 4.5e");                // part of a word / version
        None("");
        None(null);

        Console.WriteLine(_failed == 0 && _checks > 0 ? $"PASS ({_checks} checks)" : $"FAIL ({_failed} of {_checks})");
        return _failed == 0 && _checks > 0 ? 0 : 1;
    }

    private static void One(string text, double ns, double ew, string formatted)
    {
        var found = ChatCoords.Find(text);
        Check(found.Count == 1, $"one pair in \"{text}\" (found {found.Count})");
        if (found.Count != 1) return;
        Check(Math.Abs(found[0].Ns - ns) < 1e-9 && Math.Abs(found[0].Ew - ew) < 1e-9,
            $"\"{text}\" -> {ns}, {ew} (got {found[0].Ns}, {found[0].Ew})");
        Check(found[0].Text == formatted, $"\"{text}\" formats as {formatted} (got {found[0].Text})");
    }

    private static void None(string? text)
    {
        var found = ChatCoords.Find(text);
        Check(found.Count == 0, $"no pair in \"{text}\" (found {found.Count}: {string.Join("; ", found.Select(f => f.Text))})");
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine("  [FAIL] " + what);
    }
}
