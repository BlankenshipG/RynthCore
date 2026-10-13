// ============================================================================
//  RynthCore.Engine - Compatibility/IltInfo.cs
//
//  Is this client on Infinite Leaftide (ILT, the ACECustom server)? IltInfo.IsIlt
//  is the one flag the ILT section checks (the bar's ILT button, /rc bounties,
//  /rc guardian, the ILT server menu, the Bounties window's reads through the
//  RynthILT plugin). Every ILT feature is RynthILT's (since 2026-10-13), so the
//  section also needs RynthILT loaded (PluginLoaded).
//
//  Signals (combined by IltDetect.Decide, IltDetect.cs):
//    - the launch command line's -h host (ServerInfo reads it once);
//    - the world name the server announced at login (AccountHooks);
//    - the engine.json lists "IltServerNames" / "IltHosts" replace the
//      defaults (InfiniteLeaftide / *Leaftide* and infiniteleaftide.online);
//    - RynthILT's own verdict (its server list, or the character's "Treat this
//      world as ILT"), read once a second by UI/Data/IltData.cs.
//  Unknown means not ILT. "/rc ilt on|off|auto" overrides it for this session
//  (testing), never saved.
//
//  Threads: any. Re-evaluated at most once a second; each read in between is a
//  volatile load and a compare. Never touches AC.
// ============================================================================

using System;
using System.Threading;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.Compatibility;

internal static class IltInfo
{
    public enum Mode { Auto, ForceIlt, ForceOther }

    private const int RefreshMs = 1000;

    private static volatile IltVerdict _verdict = IltVerdict.Unknown;
    private static long _nextRefreshMs;
    private static int _mode;                      // Mode
    private static volatile bool _lastIlt;
    private static volatile bool _pluginLoaded;
    private static volatile bool _pluginIlt;
    private static volatile string _pluginReason = "";

    /// <summary>RynthILT is loaded (its status export answered; IltStatusSource). Any thread.</summary>
    public static bool PluginLoaded => _pluginLoaded;

    /// <summary>
    /// IltStatusSource (pump): RynthILT loaded, and whether it says this is ILT (with why). Picked
    /// up by the next Refresh (at most a second later).
    /// </summary>
    public static void SetPluginStatus(bool loaded, bool ilt, string reason)
    {
        bool changed = _pluginIlt != (loaded && ilt);
        _pluginLoaded = loaded;
        _pluginIlt = loaded && ilt;
        _pluginReason = reason ?? "";
        if (changed) Volatile.Write(ref _nextRefreshMs, 0);
    }

    /// <summary>True when the client is on Infinite Leaftide - or the override says so. Any thread.</summary>
    public static bool IsIlt => Current.IsIlt;

    public static Mode Override
    {
        get => (Mode)Volatile.Read(ref _mode);
        set
        {
            Volatile.Write(ref _mode, (int)value);
            Volatile.Write(ref _nextRefreshMs, 0);
        }
    }

    /// <summary>The current verdict (re-evaluated at most once a second). Any thread.</summary>
    public static IltVerdict Current
    {
        get
        {
            long now = Environment.TickCount64;
            if (now >= Volatile.Read(ref _nextRefreshMs))
            {
                Volatile.Write(ref _nextRefreshMs, now + RefreshMs);
                Refresh();
            }
            return _verdict;
        }
    }

    /// <summary>The lists in force, for /rc ilt.</summary>
    public static string ListsDescription
    {
        get
        {
            var names = EngineSettings.IltServerNames;
            var hosts = EngineSettings.IltHosts;
            return $"world names {(names == null ? "default " + string.Join(", ", IltDetect.DefaultServerNames) : "engine.json " + Join(names))}; "
                 + $"hosts {(hosts == null ? "default " + string.Join(", ", IltDetect.DefaultHosts) : "engine.json " + Join(hosts))}";
        }
    }

    private static string Join(System.Collections.Generic.IReadOnlyList<string> list)
        => list.Count == 0 ? "(none)" : string.Join(", ", list);

    private static void Refresh()
    {
        try
        {
            IltVerdict v = Override switch
            {
                Mode.ForceIlt => new IltVerdict { IsIlt = true, Reason = "forced on with /rc ilt on (testing)" },
                Mode.ForceOther => new IltVerdict { Reason = "forced off with /rc ilt off (testing)" },
                _ => IltDetect.Decide(ServerInfo.LaunchHost, AccountHooks.AnnouncedWorldName,
                                      EngineSettings.IltServerNames, EngineSettings.IltHosts),
            };
            // RynthILT's own list / override counts too (its verdict, when the engine's says no).
            if (Override == Mode.Auto) v = IltDetect.Combine(v, _pluginIlt, _pluginReason);
            _verdict = v;
            if (v.IsIlt != _lastIlt)
            {
                _lastIlt = v.IsIlt;
                RynthLog.Compat($"IltInfo: {v.Reason}");
            }
        }
        catch (Exception ex)
        {
            RynthLog.Compat($"IltInfo: refresh failed - {ex.GetType().Name}: {ex.Message}");
        }
    }
}
