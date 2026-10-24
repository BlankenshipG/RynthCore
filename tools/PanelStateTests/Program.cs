using RynthCore.Engine.UI;

namespace PanelStateTests;

// Offline tests for the per-character window-layout file name. See the csproj.
internal static class Program
{
    private static int _fails, _asserts;
    private static void Check(bool cond, string msg)
    {
        _asserts++;
        if (!cond) { _fails++; Console.WriteLine($"  [FAIL] {msg}"); }
    }
    private static void Eq<T>(T actual, T expected, string msg) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{msg}: expected '{expected}', got '{actual}'");

    private static int Main()
    {
        FileNames();
        LaunchContext();
        Console.WriteLine();
        Console.WriteLine(_fails == 0 ? $"PASS ({_asserts} checks)" : $"FAIL ({_fails} of {_asserts} checks failed)");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>Silentkelpie 2026-10-24: each character keeps its own layout, so another client's moves don't reset it.</summary>
    private static void FileNames()
    {
        Console.WriteLine("FileNames");
        Eq(PanelStatePath.FileNameFor("Infinite Leaftide", "Silentkelpie"), "panel_state.infinite_leaftide.silentkelpie.txt", "server and character");
        Eq(PanelStatePath.FileNameFor("ILT", "Sir O'Brien-Smith"), "panel_state.ilt.sir_o_brien_smith.txt", "spaces, apostrophe and dash become one _");
        Eq(PanelStatePath.FileNameFor("", "Silentkelpie"), "panel_state.silentkelpie.txt", "no server: character only");
        Eq(PanelStatePath.FileNameFor("ILT", ""), PanelStatePath.SharedFileName, "no character: the shared file");
        Eq(PanelStatePath.FileNameFor(null, null), PanelStatePath.SharedFileName, "nothing known: the shared file");
        Eq(PanelStatePath.FileNameFor("ILT", "  ***  "), PanelStatePath.SharedFileName, "a name of only symbols: the shared file");
        Eq(PanelStatePath.FileNameFor("ILT", "Sílentkelpie"), "panel_state.ilt.s_lentkelpie.txt", "non-ASCII letters become _");
        string longName = new string('a', 80);
        Eq(PanelStatePath.FileNameFor("x", longName), $"panel_state.x.{new string('a', PanelStatePath.MaxPartLength)}.txt", "a long name is cut");
        Eq(PanelStatePath.FileNameFor("ILT", "Silentkelpie"), PanelStatePath.FileNameFor("ilt", "SILENTKELPIE"), "case doesn't make a second file");
    }

    private static void LaunchContext()
    {
        Console.WriteLine("LaunchContext");
        var parsed = PanelStatePath.ParseLaunchContext("{\"ProcessId\":5392,\"AccountName\":\"acct\",\"ServerName\":\" ILT \",\"TargetCharacter\":\"Silentkelpie\"}");
        Eq(parsed.Server, "ILT", "server, trimmed");
        Eq(parsed.Character, "Silentkelpie", "character");
        Eq(PanelStatePath.ParseLaunchContext("{\"ServerName\":\"ILT\"}").Character, "", "no character");
        Eq(PanelStatePath.ParseLaunchContext("{\"TargetCharacter\":42}").Character, "", "a non-string character");
        Eq(PanelStatePath.ParseLaunchContext("{ cut-off").Character, "", "a broken file");
        Eq(PanelStatePath.ParseLaunchContext("[1,2]").Character, "", "not an object");
        Eq(PanelStatePath.ParseLaunchContext(null).Character, "", "no file");
    }
}
