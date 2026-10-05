// ============================================================================
//  RynthCore.Engine - ImGuiStackRecovery.cs
//
//  Closes ImGui scopes a plugin left open when it threw mid-frame (Begin without
//  End, BeginTable without EndTable, unpopped IDs / style vars, ...).
//
//  The shipped cimgui (Dear ImGui 1.89.7, static CRT, asserts on) reports a
//  mismatched stack at EndFrame through IM_ASSERT, which shows a modal
//  MessageBoxA on AC's main thread. The client then freezes until the hang
//  watchdog kills it. ImGui's own recovery routine,
//  ErrorCheckEndFrameRecover, unwinds everything down to the implicit fallback
//  window; it is documented as safe to call right before EndFrame.
//
//  1.89.7 has no io.ConfigErrorRecovery* fields, so ImGui.NET's 1.91 IO layout
//  must not be used to switch the assert off; the export is called directly.
// ============================================================================

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RynthCore.Engine.ImGuiBackend;

internal static unsafe class ImGuiStackRecovery
{
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    // void igErrorCheckEndFrameRecover(ImGuiErrorLogCallback log_callback, void* user_data)
    private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> _recover;
    private static bool _resolved;
    private static bool _loggedUnavailable;

    private static int _recoveredThisFrame;
    private static long _totalRecovered;
    private static long _nextLogAtMs;
    private static string _lastMessage = string.Empty;

    /// <summary>Scopes closed since the engine started.</summary>
    internal static long TotalRecovered => _totalRecovered;

    /// <summary>
    /// Unwinds any ImGui scope still open at the end of plugin rendering. Must be
    /// called on the render thread, with the engine's context current, outside
    /// every Begin the caller itself opened (i.e. right before EndFrame).
    /// </summary>
    internal static void RecoverBeforeEndFrame()
    {
        if (!_resolved) Resolve();
        if (_recover == null) return;

        _recoveredThisFrame = 0;
        _recover((IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte*, byte*, void>)&OnRecovered, IntPtr.Zero);
        if (_recoveredThisFrame == 0) return;

        _totalRecovered += _recoveredThisFrame;
        long now = Environment.TickCount64;
        if (now < _nextLogAtMs) return;
        _nextLogAtMs = now + 10_000;
        RynthLog.Warn($"ImGuiStackRecovery: closed {_recoveredThisFrame} ImGui scope(s) a plugin left open "
                      + $"(total {_totalRecovered}); last: {_lastMessage}");
    }

    private static void Resolve()
    {
        _resolved = true;
        IntPtr module = EntryPoint.ImGuiNativeHandle;
        IntPtr fn = module != IntPtr.Zero ? GetProcAddress(module, "igErrorCheckEndFrameRecover") : IntPtr.Zero;
        _recover = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)fn;
        if (fn == IntPtr.Zero && !_loggedUnavailable)
        {
            _loggedUnavailable = true;
            RynthLog.Warn("ImGuiStackRecovery: igErrorCheckEndFrameRecover not exported by cimgui; plugin frame errors can still assert.");
        }
        else if (fn != IntPtr.Zero)
        {
            RynthLog.Info($"ImGuiStackRecovery: armed (igErrorCheckEndFrameRecover @ 0x{fn:X8}).");
        }
    }

    // ImGuiErrorLogCallback is variadic cdecl (caller cleans the stack). Every recovery
    // message in 1.89.7 carries exactly one %s (the window name), so the first variadic
    // argument is read as a third fixed parameter.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnRecovered(IntPtr userData, byte* fmt, byte* arg)
    {
        try
        {
            _recoveredThisFrame++;
            string format = fmt != null ? Marshal.PtrToStringAnsi((IntPtr)fmt) ?? string.Empty : string.Empty;
            string name = arg != null ? Marshal.PtrToStringAnsi((IntPtr)arg) ?? string.Empty : string.Empty;
            _lastMessage = format.Replace("%s", name);
        }
        catch
        {
            // Never let a managed exception unwind into native ImGui.
        }
    }
}
