// ============================================================================
//  RynthCore.Engine - Compatibility/PlayerLifecycleLog.cs
//  One log line per player lifecycle edge: death (health reaches 0), revival,
//  portal space entered/exited, and landblock jumps (a teleport, not walking
//  into the next landblock), each with the landblock.
//
//  Why: 2026-09-28 a client crashed in AC's own teardown code
//  (acclient+0x18712F, null read in a hash-table clear) during the death
//  portal to the lifestone, and nothing in the log recorded the death or the
//  teleport. With these lines a crash can be lined up with what the character
//  was doing.
//
//  Sampled from GameTickHooks.UseTimeDetour on AC's main thread, before AC's
//  tick, so the reads see a consistent world. Alloc-free except on an edge.
// ============================================================================

using System;
using System.Threading;

namespace RynthCore.Engine.Compatibility;

internal static class PlayerLifecycleLog
{
    private static bool _init;
    private static volatile bool _portaling, _dead;
    private static long _transitionUntil;
    private const long TransitionSettleMs = 5_000;

    /// <summary>
    /// Dead, in portal space, or within 5 s after either: AC is tearing down and
    /// loading the world, so nothing should poke at its state (BusyCountHooks'
    /// force-clear). As of the last game tick; any thread.
    /// </summary>
    public static bool InWorldTransition =>
        _dead || _portaling || Environment.TickCount64 < Interlocked.Read(ref _transitionUntil);
    private static uint _landblock;
    private static long _portalEnteredAt, _diedAt;

    /// <summary>AC main thread, once per game tick.</summary>
    public static void SampleOnGameThread(bool portaling)
    {
        bool dead = false;
        uint health = 0, maxHealth = 0;
        if (PlayerVitalsHooks.TryGetSnapshot(out PlayerVitalsSnapshot v))
        {
            health = v.Health;
            maxHealth = v.MaxHealth;
            dead = v.MaxHealth > 0 && v.Health == 0;
        }
        uint cell = 0;
        if (PlayerPhysicsHooks.TryGetPlayerPose(out uint c, out _, out _, out _, out _, out _, out _, out _))
            cell = c;
        uint landblock = cell >> 16;

        if (!_init)
        {
            _init = true;
            _portaling = portaling;
            _dead = dead;
            _landblock = landblock;
            return;
        }

        long now = Environment.TickCount64;
        if (dead || portaling || _dead || _portaling)
            Interlocked.Exchange(ref _transitionUntil, now + TransitionSettleMs);
        if (dead != _dead)
        {
            if (dead)
            {
                _diedAt = now;
                RynthLog.Info($"[Lifecycle] player DIED (health 0/{maxHealth}) in landblock 0x{landblock:X4} (cell 0x{cell:X8}).");
            }
            else
                RynthLog.Info($"[Lifecycle] player alive again (health {health}/{maxHealth}) {Ago(_diedAt, now)} after death, landblock 0x{landblock:X4}.");
            _dead = dead;
        }

        if (portaling != _portaling)
        {
            if (portaling)
            {
                _portalEnteredAt = now;
                string why = _diedAt != 0 && now - _diedAt < 30_000 ? " (death portal)" : "";
                RynthLog.Info($"[Lifecycle] portal space ENTERED{why} from landblock 0x{landblock:X4} (cell 0x{cell:X8}).");
            }
            else
                RynthLog.Info($"[Lifecycle] portal space EXITED into landblock 0x{landblock:X4} (cell 0x{cell:X8}) after {Ago(_portalEnteredAt, now)}.");
            _portaling = portaling;
        }

        if (landblock != 0 && _landblock != 0 && landblock != _landblock)
        {
            int dx = (int)((landblock >> 8) & 0xFF) - (int)((_landblock >> 8) & 0xFF);
            int dy = (int)(landblock & 0xFF) - (int)(_landblock & 0xFF);
            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
                RynthLog.Info($"[Lifecycle] landblock jump 0x{_landblock:X4} -> 0x{landblock:X4}{(portaling ? " (in portal space)" : "")}.");
        }
        if (landblock != 0) _landblock = landblock;
    }

    private static string Ago(long since, long now) =>
        since == 0 ? "?" : $"{(now - since) / 1000.0:0.0}s";
}
