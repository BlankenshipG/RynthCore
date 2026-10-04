// ============================================================================
//  RynthCore.Engine - Compatibility/MessageBoxHooks.cs
//  Logs every message box shown in this process (user32!MessageBoxW / A):
//  caption, text, style and the calling module, when it opens and when it
//  closes.
//
//  Why: AC reports some errors in a modal MessageBox on its main thread
//  (acclient+0x3CE80, formatted by the caller at acclient+0x1558B5). The main
//  thread then waits on the dialog, MainThreadHangWatchdog reports a wedge,
//  and the launcher kills and relaunches the client - with the dialog's text
//  lost (the 2026-09-28 Drakkon6 close: heap isn't in the hang minidump).
//  Now the text is in the log, and the watchdog names the open dialog in its
//  hang report (see Describe).
//
//  The detour only observes: it always calls the original and returns its
//  result. Installed per engine generation; EngineLifecycle's
//  MH_DisableHook(ALL) removes it on reload.
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Threading;
using RynthCore.Engine.Hooking;

namespace RynthCore.Engine.Compatibility;

internal static class MessageBoxHooks
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MessageBoxDelegate(IntPtr hWnd, IntPtr text, IntPtr caption, uint type);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern ushort RtlCaptureStackBackTrace(uint framesToSkip, uint framesToCapture, IntPtr[] backTrace, out uint hash);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static MessageBoxDelegate? _detourW, _detourA, _originalW, _originalA;
    private static int _installed;

    // The dialog currently open (last one wins if several nest). Read by the
    // hang watchdog's thread; written on the thread showing the dialog.
    private static volatile string? _openDescription;
    private static long _openedAtTicks;
    private static int _openCount;

    public static void Initialize()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;
        try
        {
            IntPtr user32 = GetModuleHandleW("user32.dll");
            if (user32 == IntPtr.Zero) user32 = LoadLibraryW("user32.dll");
            if (user32 == IntPtr.Zero)
            {
                RynthLog.Compat("MessageBoxHooks: user32 not found.");
                return;
            }
            _detourW = MessageBoxWDetour;
            _detourA = MessageBoxADetour;
            _originalW = Hook(user32, "MessageBoxW", _detourW);
            _originalA = Hook(user32, "MessageBoxA", _detourA);
        }
        catch (Exception ex)
        {
            RynthLog.Compat($"MessageBoxHooks: install failed - {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static MessageBoxDelegate? Hook(IntPtr user32, string name, MessageBoxDelegate detour)
    {
        IntPtr target = GetProcAddress(user32, name);
        if (target == IntPtr.Zero)
        {
            RynthLog.Compat($"MessageBoxHooks: GetProcAddress({name}) failed.");
            return null;
        }
        IntPtr trampoline = MinHook.HookCreate(target, Marshal.GetFunctionPointerForDelegate(detour));
        var original = Marshal.GetDelegateForFunctionPointer<MessageBoxDelegate>(trampoline);
        Thread.MemoryBarrier();
        MinHook.Enable(target);
        RynthLog.Info($"MessageBoxHooks: hooked user32!{name} @ 0x{target.ToInt32():X8}.");
        return original;
    }

    /// <summary>
    /// The message box open right now, e.g. "AC message box open 12s on the
    /// main thread: 'Asheron's Call' - '…'", or null. Any thread.
    /// </summary>
    public static string? Describe()
    {
        string? open = _openDescription;
        if (open == null) return null;
        long secs = (Environment.TickCount64 - Interlocked.Read(ref _openedAtTicks)) / 1000;
        return $"message box open {secs}s: {open}";
    }

    private static int MessageBoxWDetour(IntPtr hWnd, IntPtr text, IntPtr caption, uint type) =>
        Show(hWnd, text, caption, type, wide: true);

    private static int MessageBoxADetour(IntPtr hWnd, IntPtr text, IntPtr caption, uint type) =>
        Show(hWnd, text, caption, type, wide: false);

    private static int Show(IntPtr hWnd, IntPtr text, IntPtr caption, uint type, bool wide)
    {
        MessageBoxDelegate? original = wide ? _originalW : _originalA;
        string? description = null;
        long opened = Environment.TickCount64;
        try
        {
            description = Opened(text, caption, type, wide);
        }
        catch { /* observation only: never block the dialog */ }

        int result = original != null ? original(hWnd, text, caption, type) : 0;

        try
        {
            long secs = (Environment.TickCount64 - opened) / 1000;
            RynthLog.Info($"MessageBoxHooks: closed after {secs}s (result={result}): {description ?? "?"}");
            if (Interlocked.Decrement(ref _openCount) <= 0)
            {
                Interlocked.Exchange(ref _openCount, 0);
                _openDescription = null;
            }
        }
        catch { }
        return result;
    }

    private static string Opened(IntPtr text, IntPtr caption, uint type, bool wide)
    {
        string body = Read(text, wide);
        string title = Read(caption, wide);
        bool mainThread = MainThreadGuard.IsOnMainThread();

        // The first frame outside user32 and this file: who asked for the dialog.
        string caller = "?";
        var frames = new IntPtr[8];
        int n = RtlCaptureStackBackTrace(1, (uint)frames.Length, frames, out _);
        for (int i = 0; i < n; i++)
        {
            string module = ProcessExitHooks.ResolveModule(frames[i], out int rva);
            if (module.Contains("RynthCore.Engine", StringComparison.OrdinalIgnoreCase)
                || module.Contains("user32", StringComparison.OrdinalIgnoreCase))
                continue;
            caller = $"{module}+0x{rva:X}";
            break;
        }

        string description = $"'{title}' - '{Flatten(body)}' (style=0x{type:X}, {(mainThread ? "AC main thread" : $"thread {GetCurrentThreadId()}")}, caller {caller})";
        Interlocked.Exchange(ref _openedAtTicks, Environment.TickCount64);
        Interlocked.Increment(ref _openCount);
        _openDescription = description;
        RynthLog.Error($"MessageBoxHooks: MESSAGE BOX OPENED {description}");
        return description;
    }

    private static string Read(IntPtr p, bool wide)
    {
        if (p == IntPtr.Zero) return "";
        string? s = wide ? Marshal.PtrToStringUni(p) : Marshal.PtrToStringAnsi(p);
        if (s == null) return "";
        return s.Length > 2000 ? s.Substring(0, 2000) + "…" : s;
    }

    private static string Flatten(string s) => s.Replace("\r\n", " | ").Replace('\n', ' ').Replace('\r', ' ');
}
