// ============================================================================
//  RynthCore.Engine - EngineStatusMetrics.cs
//
//  A benign, generic "here are my own metrics" accessor. Each heartbeat tick
//  caches the engine's live metrics (UpdateMetrics); BuildEngineStatusJson
//  composes them — plus the main-thread-cached player stats and session
//  identity — into a small JSON object that a plugin can pull via the
//  GetEngineStatusJson host bridge. The engine NEVER writes a file, opens a
//  socket, or networks; it only hands a plugin its own numbers on request.
//
//  (The per-client status file and phone command drain now live entirely in the
//  private RynthCore.Plugin.RynthRemote plugin, so the public engine carries no
//  remote or networking code — just this benign metrics accessor.)
// ============================================================================

using System;
using System.IO;
using System.Text.Json;

namespace RynthCore.Engine.Compatibility;

internal static class EngineStatusMetrics
{
    // Latest heartbeat metrics, cached so the parameterless BuildEngineStatusJson
    // accessor (backing the GetEngineStatusJson host bridge) can compose without
    // the heartbeat thread's locals. Updated every heartbeat tick; ~1s staleness
    // is fine for a status pull.
    private static long _mUptimeSec, _mWorkingSetMb, _mQueueDropped, _mReconciles, _mForceClears;
    private static int _mFps, _mPluginTicksPerSec;
    private static bool _mInWorld;

    /// <summary>
    /// Cache this tick's engine metrics. Called once/second from the heartbeat
    /// thread with values it already computed — a few field writes, no I/O.
    /// </summary>
    internal static void UpdateMetrics(
        long uptimeSeconds, int fps, int pluginTicksPerSec, long workingSetMb,
        bool inWorld, long queueDropped, long reconciles, long forceClears)
    {
        _mUptimeSec = uptimeSeconds; _mFps = fps; _mPluginTicksPerSec = pluginTicksPerSec;
        _mWorkingSetMb = workingSetMb; _mInWorld = inWorld; _mQueueDropped = queueDropped;
        _mReconciles = reconciles; _mForceClears = forceClears;
    }

    // The session's XP / luminance totals and rates: RynthSuite's shared formula (SessionRates),
    // a new session per character. Locked: the status JSON and the Mini Remote's Session Stats
    // section read it from different threads, and chat lines arrive on a third.
    private static readonly object _ratesLock = new();
    private static readonly SessionRates _rates = new();
    private static int _deathsBaseline = -1;
    private static uint _deathsCharacter;
    private const uint Int64BankedLuminance = 9005;   // ACECustom bank
    private const uint Int64AelLuminanceBanked = 9102; // Aelrynth bank: luminance auto-banked
    private const uint Int64AelLuminanceDrawn = 9103;  // Aelrynth bank: luminance drawn

    /// <summary>A chat line (PluginManager.QueueChatWindowText): luminance moved into the bank isn't income.</summary>
    internal static void OnChatLine(string? text)
    {
        if (string.IsNullOrEmpty(text) || !SessionRates.TryMovedIntoBank(text, out _)) return;
        lock (_ratesLock) _rates.OnChat(text, DateTime.UtcNow);
    }

    /// <summary>
    /// Samples the player now and returns the session's totals and rates (the Mini Remote's
    /// Session Stats section). Any thread; reads only the main-thread-cached player stats.
    /// </summary>
    internal static SessionRatesView SessionView()
    {
        try
        {
            ClientObjectHooks.TryGetPlayerStats(out long xp, out long lum, out _, out _, out _, out _, out _, out _, out _, out _);
            return SampleRates(xp, lum);
        }
        catch
        {
            return new SessionRatesView(DateTime.UtcNow, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>Feeds one read to the shared-formula session (bank properties read from the property cache).</summary>
    private static SessionRatesView SampleRates(long xp, long lum)
    {
        uint player = ClientHelperHooks.GetPlayerId();
        long? Quad(uint stype) => player != 0 && ClientObjectHooks.TryGetObjectQuadProperty(player, stype, out long v) ? v : null;
        var sample = new SessionRatesSample(xp > 0 ? xp : null, lum > 0 ? lum : null,
                                            Quad(Int64BankedLuminance), Quad(Int64AelLuminanceBanked), Quad(Int64AelLuminanceDrawn));
        DateTime now = DateTime.UtcNow;
        lock (_ratesLock)
        {
            _rates.Sample(player, sample, now);
            return _rates.View(now);
        }
    }

    /// <summary>
    /// Compose the engine-side status fields as a JSON object string, from the
    /// latest cached heartbeat metrics. Backs the GetEngineStatusJson host bridge.
    /// Never throws; returns "{}" on failure. Safe off the main thread — player
    /// stats come from a main-thread-cached source.
    /// </summary>
    internal static string BuildEngineStatusJson()
    {
        try
        {
            using var ms = new MemoryStream(1024);
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                WriteEngineFields(w, _mUptimeSec, _mFps, _mPluginTicksPerSec, _mWorkingSetMb,
                                  _mInWorld, _mQueueDropped, _mReconciles, _mForceClears);
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return "{}";
        }
    }

    // Writes the engine status fields into an already-open JSON object. The per-hour
    // baselines below have a single writer (the GetEngineStatusJson accessor path).
    private static void WriteEngineFields(
        Utf8JsonWriter w,
        long uptimeSeconds, int fps, int pluginTicksPerSec, long workingSetMb,
        bool inWorld, long queueDropped, long reconciles, long forceClears)
    {
        w.WriteString("schema", "rynthcore.client-status/1");
        w.WriteString("ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'"));
        w.WriteString("host", SafeMachineName());
        w.WriteNumber("pid", Environment.ProcessId);
        // The engine's build ("2026.9.26.3 (8ca322f)", "dev (...)") and plugin API version, so a
        // plugin can say which engine it runs on (RynthAi /ra version). Additive fields.
        w.WriteString("engineVersion", EntryPoint.BuildStamp);
        w.WriteNumber("apiVersion", Plugins.PluginContractVersion.Current);

        w.WriteString("account", SessionStateRegistry.LastAccountName ?? string.Empty);
        w.WriteString("character", SessionStateRegistry.LastCharacterName ?? string.Empty);
        w.WriteString("server", SessionStateRegistry.LastServerName ?? string.Empty);

        w.WriteNumber("uptimeSec", uptimeSeconds);
        w.WriteNumber("fps", fps);
        w.WriteNumber("pluginTicksPerSec", pluginTicksPerSec);
        w.WriteNumber("workingSetMB", workingSetMb);
        w.WriteBoolean("inWorld", inWorld);
        w.WriteNumber("queueDropped", queueDropped);
        w.WriteNumber("reconciles", reconciles);
        w.WriteNumber("forceClears", forceClears);
        // AC's main thread frozen (message box or stale beat) and plugin ticks paused for it
        // (MainThreadFreeze). pluginTicksPausedMs is a running total: a plugin's stall
        // detector subtracts its change over the window it measured.
        bool frozen = false; long stallMs = 0, pausedMs = 0;
        try { frozen = MainThreadFreeze.Paused; stallMs = MainThreadFreeze.MainThreadStallMs; pausedMs = MainThreadFreeze.PausedTotalMs; }
        catch { }
        w.WriteBoolean("mainThreadFrozen", frozen);
        w.WriteNumber("mainThreadStallMs", stallMs);
        w.WriteNumber("pluginTicksPausedMs", pausedMs);

        // Player stats, read on the main thread by PrefetchPlayerStats and served here off-thread
        // (the plugin pump can't read these live). deaths all-time; rates/session = whole-session.
        ClientObjectHooks.TryGetPlayerStats(out long xp, out long lum, out int deaths, out float vitae,
                                            out int encVal, out int encCap, out uint landcell,
                                            out float px, out float py, out float pz);
        double vitaePct = Math.Clamp((1.0 - vitae) * 100.0, 0.0, 100.0);
        // XP and luminance: the shared formula (sum of rises, ACECustom / Aelrynth bank aware),
        // one session per character, the same numbers RynthAi and RynthTracker show.
        SessionRatesView rates = SampleRates(xp, lum);
        double xpPerHour = rates.XpPerHour;
        double lumPerHour = rates.LumPerHour;
        long xpSession = rates.Xp;
        // Deaths this session: from the first read for this character.
        uint who = ClientHelperHooks.GetPlayerId();
        if (who != 0 && who != _deathsCharacter) { _deathsCharacter = who; _deathsBaseline = deaths; }
        int deathsSession = (_deathsBaseline >= 0 && deaths >= _deathsBaseline) ? deaths - _deathsBaseline : 0;
        double burdenPct = encCap > 0 ? Math.Round((double)encVal / encCap * 100.0) : 0;
        w.WriteNumber("deaths", deaths);
        w.WriteNumber("deathsSession", deathsSession);
        w.WriteNumber("vitaePct", Math.Round(vitaePct, 1));
        w.WriteNumber("xpPerHour", Math.Round(xpPerHour));
        w.WriteNumber("luminancePerHour", Math.Round(lumPerHour));
        w.WriteNumber("xpSession", xpSession);
        w.WriteNumber("luminanceSession", rates.Lum);   // additive (2026.10.5.54): the luminance total behind luminancePerHour
        w.WriteNumber("burdenPct", burdenPct);
        w.WriteString("area", landcell != 0 ? (landcell >> 16).ToString("X4") : "");
        // [status-export] live map-dot position. wx/wy are ABSOLUTE world coords (cell-local origin + the
        // landblock grid offset) because the baked dungeon map's bounds are absolute (DungeonLOS adds the
        // same gx/gy per vertex). The app maps these straight to map pixels.
        uint lb = landcell != 0 ? (landcell >> 16) : 0u;
        bool indoor = lb != 0 && (landcell & 0xFFFF) >= 0x0100;   // indoor EnvCells are 0x0100+; outdoor = 0
        w.WriteString("landblock", lb != 0 ? lb.ToString("X8") : "");   // matches the .bin filename {lb:X8}
        w.WriteBoolean("indoor", indoor);
        if (indoor)
        {
            double wx = px + ((lb >> 8) & 0xFF) * 192.0;
            double wy = py + (lb & 0xFF) * 192.0;
            w.WriteNumber("wx", Math.Round(wx, 2));
            w.WriteNumber("wy", Math.Round(wy, 2));
            w.WriteNumber("pz", Math.Round(pz, 2));   // raw local Z — floor-label / fallback hint
        }
        // Last engine warning/error (truncated) + age, for remote diagnosis of a stuck box.
        string? li = RynthLog.LastIssue;
        if (!string.IsNullOrEmpty(li))
        {
            w.WriteString("lastIssue", li.Length > 140 ? li.Substring(0, 140) : li);
            w.WriteNumber("lastIssueAgeSec", (long)Math.Max(0, (DateTime.UtcNow - RynthLog.LastIssueUtc).TotalSeconds));
        }
        else { w.WriteNull("lastIssue"); w.WriteNumber("lastIssueAgeSec", -1); }
    }

    private static string SafeMachineName()
    {
        try { return Environment.MachineName; }
        catch { return string.Empty; }
    }
}
