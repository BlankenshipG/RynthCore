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
        if (D3D9Enabled) WriteAt(EntryPoint.EngineLogLevel.Info, msg);
    }

    /// <summary>Compatibility hooks: SmartBox, client objects, combat, movement, vitals, chat, etc.</summary>
    internal static void Compat(string msg)
    {
        if (CompatEnabled) WriteAt(EntryPoint.EngineLogLevel.Info, msg);
    }

    /// <summary>ImGui rendering: context, DX9 backend, Win32 input, shell.</summary>
    internal static void Render(string msg)
    {
        if (RenderEnabled) WriteAt(EntryPoint.EngineLogLevel.Info, msg);
    }

    /// <summary>Plugin system: loader, manager, lifecycle callbacks.</summary>
    internal static void Plugin(string msg)
    {
        if (PluginEnabled) WriteAt(EntryPoint.EngineLogLevel.Info, msg);
    }

    /// <summary>UI / Avalonia overlay subsystem.</summary>
    internal static void UI(string msg)
    {
        if (UIEnabled) WriteAt(EntryPoint.EngineLogLevel.Info, msg);
    }

    /// <summary>Debug-level log (any category).</summary>
    internal static void Verbose(string msg)
    {
        WriteAt(EntryPoint.EngineLogLevel.Debug, msg);
    }

    // Level-gated helpers (engine.json "LoggingLevel"). Warning/Error delegate to the always-on
    // tagged WRN/ERR writers below so they are never filtered and still feed LastIssue.
    internal static void Trace(string msg) => WriteAt(EntryPoint.EngineLogLevel.Trace, msg);
    internal static void Debug(string msg) => WriteAt(EntryPoint.EngineLogLevel.Debug, msg);

    /// <summary>INFO line for uncategorised messages (written at the default Info level and finer).</summary>
    internal static void Info(string msg) => WriteAt(EntryPoint.EngineLogLevel.Info, msg);

    /// <summary>Alias kept for SK-local call sites; same as <see cref="Warn"/>.</summary>
    internal static void Warning(string msg) => Warn(msg);

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

    // ── Sink ─────────────────────────────────────────────────────────────

    private static void WriteAt(EntryPoint.EngineLogLevel level, string message)
    {
        if (EntryPoint.ShouldLog(level))
            EntryPoint.Log(message);
    }
}
