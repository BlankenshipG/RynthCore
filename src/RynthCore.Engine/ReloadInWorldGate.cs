// ============================================================================
//  RynthCore.Engine - ReloadInWorldGate.cs
//  Holds an automatic hot reload while the player is in the world (2026-10-16).
//
//  Stacked FileWatcher reloads mid-session were killing clients: Shutdown returned, then
//  CoreCLR re-init AVed / never logged "Reload: complete" (e.g. gen 4 after 15.8→…→16.2).
//  Combat deferral (ReloadDeferral) only waits for plugin blockers; it does not stop a
//  quiet in-world unload. This gate waits with the old engine still mapped until the
//  player is out of the world (or asked for the reload by hand). No force-timeout: going
//  ahead while still in-world is what we are trying to avoid.
//
//  Pure: the "still hold?" query, clock, sleep and log are injected so tools\ReloadInWorldGateTests
//  can run offline. EntryPoint wires it from MayHotReload / DeferReloadWhilePluginsObject.
// ============================================================================

using System;
using System.Threading;

namespace RynthCore.Engine;

internal static class ReloadInWorldGate
{
    /// <summary>How often we ask again while holding for the world.</summary>
    public static readonly TimeSpan DefaultPoll = TimeSpan.FromSeconds(2);
    /// <summary>While waiting, the "still holding" line repeats this often (first line at once).</summary>
    public static readonly TimeSpan DefaultLogEvery = TimeSpan.FromSeconds(30);

    public enum Outcome
    {
        /// <summary>Not in the world (or allowed to proceed): reload at once.</summary>
        Clear,
        /// <summary>Was in the world, then left; reload now.</summary>
        ClearedAfterWait,
    }

    /// <summary>
    /// True when a reload should wait: in-world auto reloads are not allowed, and the client
    /// is in the world. Manual ERl / /rc rl no longer bypass this — mid-session CoreCLR
    /// re-init still AVs (2026-10-16.5).
    /// </summary>
    public static bool ShouldHold(bool byHand, bool allowInWorldAuto, bool inWorld)
    {
        _ = byHand; // kept for call-site clarity; never clears an in-world hold
        return !allowInWorldAuto && inWorld;
    }

    /// <summary>
    /// Polls <paramref name="stillHold"/> until it is false. Never times out into a reload
    /// while still holding: that would re-create the crash path this gate exists to stop.
    /// </summary>
    public static Outcome Run(
        Func<bool> stillHold,
        Action<string> log,
        Action<string>? notify,
        out TimeSpan waited,
        Func<DateTime>? clock = null,
        Action<TimeSpan>? sleep = null,
        TimeSpan? poll = null,
        TimeSpan? logEvery = null)
    {
        clock ??= static () => DateTime.UtcNow;
        sleep ??= static t => Thread.Sleep(t);
        TimeSpan pollSpan = poll ?? DefaultPoll;
        TimeSpan logSpan = logEvery ?? DefaultLogEvery;

        waited = TimeSpan.Zero;
        if (!SafeHold(stillHold)) return Outcome.Clear;

        DateTime start = clock();
        log("engine reload waiting: player is in the world (automatic hot reload loads after logout / character select).");
        try
        {
            notify?.Invoke("player is in the world");
        }
        catch { /* chat must not abort the wait */ }
        DateTime lastLog = start;

        while (true)
        {
            sleep(pollSpan);
            DateTime now = clock();
            waited = now - start;
            if (!SafeHold(stillHold))
            {
                log($"engine reload: left the world after {waited.TotalSeconds:0.0} s; reloading now.");
                return Outcome.ClearedAfterWait;
            }
            if (now - lastLog >= logSpan)
            {
                lastLog = now;
                log($"engine reload waiting: still in the world ({waited.TotalSeconds:0} s so far; loads after logout / character select).");
            }
        }
    }

    private static bool SafeHold(Func<bool> stillHold)
    {
        try { return stillHold(); }
        catch { return false; } // fail open: a broken query must not wedge the client forever
    }
}
