using System;

namespace RynthCore.Engine;

/// <summary>
/// Centralised logging — every log call in the Engine routes through here.
/// Toggle categories on/off at runtime to suppress entire subsystems.
/// Messages keep their existing prefix text (e.g. "D3D9VTable: scanning…").
/// </summary>
internal static class RynthLog
{
    // ── Category toggles (flip to false to silence a subsystem) ──────────

    internal static bool D3D9Enabled    = true;
    internal static bool CompatEnabled  = true;
    internal static bool RenderEnabled  = false;   // ImGui, DX9Backend, Win32Backend
    internal static bool PluginEnabled  = true;
    internal static bool UIEnabled      = true;

    // ── Category methods ─────────────────────────────────────────────────

    /// <summary>D3D9 subsystem: vtable, EndScene, bootstrapper, matrix capture, nav3D.</summary>
    internal static void D3D9(string msg)
    {
        if (D3D9Enabled) Write(msg);
    }

    /// <summary>Compatibility hooks: SmartBox, client objects, combat, movement, vitals, chat, etc.</summary>
    internal static void Compat(string msg)
    {
        if (CompatEnabled) Write(msg);
    }

    /// <summary>ImGui rendering: context, DX9 backend, Win32 input, shell.</summary>
    internal static void Render(string msg)
    {
        if (RenderEnabled) Write(msg);
    }

    /// <summary>Plugin system: loader, manager, lifecycle callbacks.</summary>
    internal static void Plugin(string msg)
    {
        if (PluginEnabled) Write(msg);
    }

    /// <summary>UI / Avalonia overlay subsystem.</summary>
    internal static void UI(string msg)
    {
        if (UIEnabled) Write(msg);
    }

    /// <summary>Verbose-only log (any category). Only written when VerboseLogging is on.</summary>
    internal static void Verbose(string msg)
    {
        if (EntryPoint.VerboseLogging) Write(msg);
    }

    /// <summary>Always-on log for critical / uncategorised messages (INFO).</summary>
    internal static void Info(string msg) => Write(msg);

    // Most recent WRN/ERR text + when, surfaced via the GetEngineStatusJson host bridge so a stuck box
    // can be diagnosed remotely without reading the PC log.
    internal static volatile string? LastIssue;
    internal static DateTime LastIssueUtc;

    /// <summary>Always-on WARN line — a recoverable problem worth grepping for.</summary>
    internal static void Warn(string msg)
    {
        LastIssue = msg; LastIssueUtc = DateTime.UtcNow;
        EntryPoint.LogTagged("engine", msg, "WRN");
    }

    /// <summary>Always-on ERROR line — a fault/crash/disable. Triage with grep "[ERR]".</summary>
    internal static void Error(string msg)
    {
        LastIssue = msg; LastIssueUtc = DateTime.UtcNow;
        EntryPoint.LogTagged("engine", msg, "ERR");
    }

    // ── Diagnostic traces (2026-10-05, Drakkon's held casts on DreamWeave) ──
    // A movement packet sent while a cast winds up can cancel the cast or leave the server
    // holding it open, and the old movement lines were 1 in 8 with no caller. Both switches
    // are on by default; each costs one bool test when off.

    /// <summary>Every plugin movement call (SetAutoRun, SetMotion, DoMovement/StopMovement,
    /// StopCompletely, TurnToHeading, jumps), one "Move:" line each with the calling plugin.
    /// Identical calls in a row from one caller (turns: any angle) are counted, not written,
    /// for up to MoveRepeatWindowMs; the count rides on the next line written.</summary>
    internal static bool MoveTraceEnabled = true;

    /// <summary>Every server UseDone (0x01C7) with its sequence number and error code
    /// (before: the first 25 of a session).</summary>
    internal static bool ActionDoneTraceEnabled = true;

    private const long MoveRepeatWindowMs = 1000;
    private static readonly object _moveGate = new();
    private static string _moveLastKey = "";
    private static long _moveLastWrittenMs = long.MinValue;
    private static int _moveRepeats;
    private static long _moveRepeatFirstMs;
    private static string _moveRepeatLastArg = "";

    /// <summary>
    /// One plugin movement call (see <see cref="MoveTraceEnabled"/>). <paramref name="name"/>
    /// must be a string literal (it also goes into AcActionTrace, which keeps the last 256
    /// actions for crash dumps); <paramref name="a"/>/<paramref name="b"/> are its trace args.
    /// </summary>
    internal static void Move(string name, string arg, bool ok, bool collapseByKind = false, uint a = 0, uint b = 0)
    {
        Compatibility.AcActionTrace.Record(name, a, b);
        if (!MoveTraceEnabled) return;
        string? line = null;
        try
        {
            string caller = MoveCaller();
            lock (_moveGate)
            {
                long now = Environment.TickCount64;
                string key = collapseByKind ? $"{caller}|{name}" : $"{caller}|{name}({arg})|{ok}";
                if (key == _moveLastKey && now - _moveLastWrittenMs < MoveRepeatWindowMs)
                {
                    if (_moveRepeats == 0) _moveRepeatFirstMs = now;
                    _moveRepeats++;
                    _moveRepeatLastArg = arg;
                    return;
                }
                string more = "";
                if (_moveRepeats > 0)
                {
                    string[] parts = _moveLastKey.Split('|');
                    string what = parts.Length > 1 ? parts[1] : "?";
                    string last = parts.Length > 2 ? "" : $", last {_moveRepeatLastArg}";
                    more = $" (+{_moveRepeats} more {what}{last} by {parts[0]} before this, over {now - _moveRepeatFirstMs} ms)";
                }
                line = $"Move: {name}({arg}) by {caller} -> {(ok ? "ok" : "FAILED")}{more}";
                _moveLastKey = key;
                _moveLastWrittenMs = now;
                _moveRepeats = 0;
                _moveRepeatLastArg = "";
            }
        }
        catch { return; }
        Write(line);
    }

    /// <summary>The plugin whose tick or event is calling, else the thread.</summary>
    private static string MoveCaller()
    {
        var plugin = Plugins.PluginManager.CurrentDispatch;
        if (plugin != null)
            return string.IsNullOrEmpty(plugin.DisplayName) ? plugin.FileName : plugin.DisplayName;
        return Compatibility.MainThreadGuard.IsOnMainThread()
            ? "AC main thread"
            : $"thread {Environment.CurrentManagedThreadId}";
    }

    // ── Sink ─────────────────────────────────────────────────────────────

    private static void Write(string message) => EntryPoint.Log(message);
}
