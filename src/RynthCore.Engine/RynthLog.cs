namespace RynthCore.Engine;

/// <summary>
/// Centralised logging — every log call in the Engine routes through here.
/// Toggle categories on/off at runtime to suppress entire subsystems.
/// Messages keep their existing prefix text (e.g. "D3D9VTable: scanning…").
/// </summary>
internal static class RynthLog
{
    // ── Category toggles (flip to false to silence a subsystem) ──────────

    internal static bool D3D9Enabled    = false;
    internal static bool CompatEnabled  = true;
    internal static bool RenderEnabled  = false;   // ImGui, DX9Backend, Win32Backend
    internal static bool PluginEnabled  = true;
    internal static bool UIEnabled      = false;

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

    internal static void Trace(string msg) => WriteAt(EntryPoint.EngineLogLevel.Trace, msg);
    internal static void Debug(string msg) => WriteAt(EntryPoint.EngineLogLevel.Debug, msg);
    internal static void Info(string msg) => WriteAt(EntryPoint.EngineLogLevel.Info, msg);
    internal static void Warning(string msg) => WriteAt(EntryPoint.EngineLogLevel.Warning, msg);
    internal static void Error(string msg) => WriteAt(EntryPoint.EngineLogLevel.Error, msg);

    // ── Sink ─────────────────────────────────────────────────────────────

    private static void WriteAt(EntryPoint.EngineLogLevel level, string message)
    {
        if (EntryPoint.ShouldLog(level))
            EntryPoint.Log(message);
    }
}
