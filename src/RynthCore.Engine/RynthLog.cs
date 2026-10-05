using System;

namespace RynthCore.Engine;

/// <summary>
/// Centralised logging — every log call in the Engine routes through here.
/// Each category method writes at that category's emit level (engine.json "LogCategories",
/// editable live from the launcher; see <see cref="LogSettings"/>), gated by the global
/// "LoggingLevel". Messages keep their existing prefix text (e.g. "D3D9VTable: scanning…").
/// </summary>
internal static class RynthLog
{
    // ── Category methods ─────────────────────────────────────────────────

    /// <summary>D3D9 subsystem: vtable, EndScene, bootstrapper, matrix capture, nav3D.</summary>
    internal static void D3D9(string msg) => WriteCategory(LogCategory.D3D9, msg);

    /// <summary>Compatibility hooks: SmartBox, client objects, combat, movement, vitals, chat, etc.</summary>
    internal static void Compat(string msg) => WriteCategory(LogCategory.Compat, msg);

    /// <summary>ImGui rendering: context, DX9 backend, Win32 input, shell.</summary>
    internal static void Render(string msg) => WriteCategory(LogCategory.Render, msg);

    /// <summary>Plugin system: loader, manager, lifecycle callbacks, plugin host Log lines.</summary>
    internal static void Plugin(string msg) => WriteCategory(LogCategory.Plugin, msg);

    /// <summary>UI / Avalonia overlay subsystem.</summary>
    internal static void UI(string msg) => WriteCategory(LogCategory.UI, msg);

    /// <summary>Verbose detail (any subsystem). Debug by default; set the Verbose category to Info to see it in the normal log.</summary>
    internal static void Verbose(string msg) => WriteCategory(LogCategory.Verbose, msg);

    /// <summary>Same as <see cref="Verbose"/>.</summary>
    internal static void Debug(string msg) => WriteCategory(LogCategory.Verbose, msg);

    /// <summary>Trace-level line: written only when the global level is Trace.</summary>
    internal static void Trace(string msg) => WriteAt(EntryPoint.EngineLogLevel.Trace, msg);

    /// <summary>Uncategorised line (General category, Info by default).</summary>
    internal static void Info(string msg) => WriteCategory(LogCategory.General, msg);

    /// <summary>Alias kept for SK-local call sites; same as <see cref="Warn"/>.</summary>
    internal static void Warning(string msg) => Warn(msg);

    // Most recent WRN/ERR text + when, surfaced via the GetEngineStatusJson host bridge so a stuck box
    // can be diagnosed remotely without reading the PC log. Recorded even when the line is filtered.
    internal static volatile string? LastIssue;
    internal static DateTime LastIssueUtc;

    /// <summary>WARN line — a recoverable problem worth grepping for. Hidden only at level Error/Off.</summary>
    internal static void Warn(string msg)
    {
        LastIssue = msg; LastIssueUtc = DateTime.UtcNow;
        if (EntryPoint.ShouldLog(EntryPoint.EngineLogLevel.Warning))
            EntryPoint.LogTagged("engine", msg, "WRN");
    }

    /// <summary>Always-on ERROR line — a fault/crash/disable, written even at level Off. Triage with grep "[ERR]".</summary>
    internal static void Error(string msg)
    {
        LastIssue = msg; LastIssueUtc = DateTime.UtcNow;
        EntryPoint.LogTagged("engine", msg, "ERR");
    }

    /// <summary>True when a line in <paramref name="category"/> would be written (guard expensive messages).</summary>
    internal static bool IsEnabled(LogCategory category) => LogSettings.IsEnabled(category);

    // ── Sink ─────────────────────────────────────────────────────────────

    private static void WriteCategory(LogCategory category, string message)
        => WriteAt(LogSettings.LevelFor(category), message);

    private static void WriteAt(EntryPoint.EngineLogLevel level, string message)
    {
        if (EntryPoint.ShouldLog(level))
            EntryPoint.LogTagged("engine", message, LogSettings.Tag(level));
    }
}
