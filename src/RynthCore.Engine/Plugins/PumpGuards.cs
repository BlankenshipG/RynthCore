// ============================================================================
//  RynthCore.Engine - Plugins/PumpGuards.cs
//
//  Two small pieces for PluginManager.TickAll and the plugin pump thread.
//
//  StepFaultLog: TickAll's own steps (the main-thread freeze check, the delayed
//  self-identify, the Nav3D frame clear/commit, dropping failed plugins' script
//  windows, the plugin loop itself) are caught one by one, so nothing outside a
//  plugin's own Tick can take the pump down. A throwing step is logged with its
//  exception the first time, then only every RepeatEvery-th time with a count, so a
//  step that throws on every tick can't flood the log.
//
//  PumpDispatchInfo: what the plugin pump is calling right now (plugin, export, since
//  when), formatted for the hang watchdog. A plugin export that faults natively (an
//  access violation in a managed plugin's code) is fatal in .NET 10 and leaves only
//  "TickAll" in the event log; the process then sits ~12 s while createdump writes
//  the dump, the hang watchdog reports, and this line names the plugin
//  (2026-10-13 06:00: RynthAi's tick jumped to address 0).
//
//  Pure: no engine state. Compiled into tools\EngineRobustnessTests.
// ============================================================================

namespace RynthCore.Engine.Plugins;

/// <summary>TickAll's own steps (not the plugins' ticks).</summary>
internal enum TickStep
{
    FreezeCheck,
    SelfIdentify,
    Nav3DClear,
    PluginLoop,
    DropFailedOwners,
    Nav3DCommit,
    Count,
}

/// <summary>
/// Counts throws per step and says when one should be written: the first, then every
/// <see cref="RepeatEvery"/>-th. Single-threaded by contract (the one pump thread).
/// </summary>
internal sealed class StepFaultLog
{
    public const int RepeatEvery = 1000;

    private readonly int[] _counts;

    public StepFaultLog(int steps) => _counts = new int[steps];

    /// <summary>Records one throw; true when this one should be logged.</summary>
    public bool Record(int step, out int count)
    {
        count = ++_counts[step];
        return count == 1 || count % RepeatEvery == 0;
    }

    public int CountOf(int step) => _counts[step];

    public static string Describe(TickStep step, string exceptionType, string message, int count) =>
        count == 1
            ? $"PluginManager.TickAll: {step} threw {exceptionType}: {message} - caught, the pump carries on (logged once; repeats are counted)."
            : $"PluginManager.TickAll: {step} threw {exceptionType}: {message} - {count} times so far, still caught.";
}

/// <summary>
/// The plugin call the pump is inside, for the hang watchdog. The pump thread writes
/// the fields, the watchdog reads them, unsynchronized: a torn read only garbles a
/// diagnostic line.
/// </summary>
internal static class PumpDispatchInfo
{
    /// <summary>
    /// "RynthAi Tick for 12375 ms", or null when no plugin call is in progress (or the
    /// one in progress started too recently to matter).
    /// </summary>
    public static string? Describe(string? plugin, string? export, long sinceMs, long nowMs, long minMs = 1000)
    {
        if (plugin == null || sinceMs == 0)
            return null;
        long ms = nowMs - sinceMs;
        if (ms < minMs)
            return null;
        return $"inside {plugin} {export ?? "call"} for {ms} ms";
    }
}
