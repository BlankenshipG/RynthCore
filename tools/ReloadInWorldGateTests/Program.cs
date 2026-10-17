using RynthCore.Engine;

namespace ReloadInWorldGateTests;

/// <summary>
/// Offline checks for ReloadInWorldGate (automatic hot reload must not tear the engine
/// down mid-session). See the csproj comment for the cases.
/// </summary>
internal static class Program
{
    private static int _checks, _failed;
    private static string _case = "";

    private static int Main()
    {
        ShouldHoldRules();
        ClearWhenNotInWorld();
        WaitsUntilLeftWorld();
        ThrowingHoldNeverWedges();

        Console.WriteLine($"{_checks} check(s), {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"[FAIL] {_case}: {what}");
    }

    private static void ShouldHoldRules()
    {
        _case = "ShouldHold";
        Check(ReloadInWorldGate.ShouldHold(byHand: true, allowInWorldAuto: false, inWorld: true), "by hand + in world: still hold (ERl must not bypass)");
        Check(!ReloadInWorldGate.ShouldHold(byHand: false, allowInWorldAuto: true, inWorld: true), "allow in-world auto: never hold");
        Check(!ReloadInWorldGate.ShouldHold(byHand: false, allowInWorldAuto: false, inWorld: false), "out of world: never hold");
        Check(ReloadInWorldGate.ShouldHold(byHand: false, allowInWorldAuto: false, inWorld: true), "auto + in world: hold");
    }

    private static void ClearWhenNotInWorld()
    {
        _case = "clear";
        var logs = new List<string>();
        var outcome = ReloadInWorldGate.Run(
            stillHold: () => false,
            log: logs.Add,
            notify: null,
            waited: out TimeSpan waited,
            clock: () => new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc),
            sleep: _ => { });
        Check(outcome == ReloadInWorldGate.Outcome.Clear, $"Clear (got {outcome})");
        Check(waited == TimeSpan.Zero, "no wait");
        Check(logs.Count == 0, "nothing logged");
    }

    private static void WaitsUntilLeftWorld()
    {
        _case = "wait then clear";
        var now = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
        int polls = 0;
        var logs = new List<string>();
        var notes = new List<string>();
        var outcome = ReloadInWorldGate.Run(
            stillHold: () => ++polls <= 5, // hold for 5 polls, then clear
            log: logs.Add,
            notify: notes.Add,
            waited: out TimeSpan waited,
            clock: () => now,
            sleep: t => now += t,
            poll: TimeSpan.FromSeconds(2),
            logEvery: TimeSpan.FromSeconds(30));
        Check(outcome == ReloadInWorldGate.Outcome.ClearedAfterWait, $"ClearedAfterWait (got {outcome})");
        Check(Math.Abs(waited.TotalSeconds - 10) < 0.01, $"waited 10 s (got {waited.TotalSeconds})");
        Check(logs.Count >= 2 && logs[0].Contains("player is in the world", StringComparison.Ordinal),
            $"first log names the world: {logs.FirstOrDefault()}");
        Check(logs[^1].Contains("left the world", StringComparison.Ordinal), $"last log: {logs[^1]}");
        Check(notes.Count == 1 && notes[0].Contains("in the world", StringComparison.Ordinal), "chat notify once");
    }

    private static void ThrowingHoldNeverWedges()
    {
        _case = "throwing stillHold";
        var outcome = ReloadInWorldGate.Run(
            stillHold: () => throw new InvalidOperationException("boom"),
            log: _ => { },
            notify: null,
            waited: out TimeSpan waited,
            clock: () => DateTime.UtcNow,
            sleep: _ => { });
        Check(outcome == ReloadInWorldGate.Outcome.Clear, "throwing query fails open to Clear");
        Check(waited == TimeSpan.Zero, "no wait on throw");
    }
}
