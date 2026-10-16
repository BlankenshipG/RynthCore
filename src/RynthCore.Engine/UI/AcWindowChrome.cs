// ============================================================================
//  RynthCore.Engine - UI/AcWindowChrome.cs
//  Settings > Display helpers for the Asheron's Call game HWND:
//    - Borderless windowed: strip caption / thick frame while leaving the client
//      windowed (not exclusive fullscreen).
//    - Remember placement: persist and restore the outer window rect across
//      launches when "Remember open windows and HUD positions" is on.
//  Styles use SetWindowLong (x86); SWP_FRAMECHANGED so the non-client area
//  rebuilds after GWL_STYLE changes.
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.UI;

internal static class AcWindowChrome
{
    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsSysMenu = 0x00080000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsBorder = 0x00800000;
    // Caption + frame + sysmenu/minmax: the chrome "Borderless windowed" removes.
    private const int BorderChromeMask =
        WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox | WsBorder;

    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpFramechanged = 0x0020;

    private static readonly object Sync = new();
    private static int? _savedStyle;
    private static IntPtr _styleHwnd;
    private static bool _borderlessApplied;
    private static long _nextApplyTick;
    private static long _nextPlacementSaveTick;
    private static bool _placementRestored;

    private static string PlacementPath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "RynthCore", "ac_window.txt");
        }
    }

    /// <summary>Re-apply borderless / restore placement when the HWND is known.
    /// Cheap when nothing changed; call from the frame loop at a low rate.</summary>
    public static void Tick(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        long now = Environment.TickCount64;
        if (now < _nextApplyTick) return;
        _nextApplyTick = now + 2000;

        try
        {
            if (EngineSettings.RememberWindowLayout && !_placementRestored)
                TryRestorePlacement(hwnd);

            ApplyBorderlessDesired(hwnd);

            // Keep AC placement fresh while the user drags/resizes (throttled).
            if (EngineSettings.RememberWindowLayout && now >= _nextPlacementSaveTick)
            {
                _nextPlacementSaveTick = now + 10000;
                SavePlacement(hwnd);
            }
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"AcWindowChrome: tick failed - {ex.Message}"); } catch { }
        }
    }

    /// <summary>Immediate borderless apply (Settings toggle).</summary>
    public static void ApplyBorderlessDesired(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        bool want = EngineSettings.BorderlessWindowed;
        lock (Sync)
        {
            if (hwnd != _styleHwnd)
            {
                _styleHwnd = hwnd;
                _savedStyle = null;
                _borderlessApplied = false;
            }

            int style = GetWindowLong(hwnd, GwlStyle);
            bool missingChrome = (style & WsCaption) == 0;

            if (want)
            {
                // Leave true exclusive fullscreen alone — but a maximized borderless
                // window we made ourselves still needs to be able to restore later.
                if (!_borderlessApplied && IsLikelyExclusiveFullscreen(hwnd, style))
                {
                    RynthLog.UI("AcWindowChrome: skip borderless — looks like exclusive fullscreen.");
                    return;
                }

                if (_borderlessApplied && missingChrome)
                    return;

                // Remember a style that still has chrome. After a hot reload the window
                // can already be borderless while our statics were cleared — OR the mask
                // back on so turning the setting off can still restore a real frame.
                if (_savedStyle is null)
                    _savedStyle = missingChrome ? (style | BorderChromeMask) : style;

                int next = style & ~BorderChromeMask;
                if (next == style && _borderlessApplied) return;
                SetWindowLong(hwnd, GwlStyle, next);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNomove | SwpNosize | SwpNozorder | SwpNoactivate | SwpFramechanged);
                _borderlessApplied = true;
                RynthLog.UI("AcWindowChrome: borderless windowed on.");
            }
            else
            {
                // Restore even when the window fills the monitor (maximized borderless
                // looks like exclusive fullscreen and used to early-return forever).
                // Also heal a stuck chrome-less window after engine reload with the
                // setting already off (_borderlessApplied false, _savedStyle null).
                if (!_borderlessApplied && _savedStyle is null && !missingChrome) return;

                int restore;
                if (_savedStyle is int saved && (saved & WsCaption) != 0)
                    restore = saved;
                else
                    restore = style | BorderChromeMask;

                if (style != restore)
                {
                    SetWindowLong(hwnd, GwlStyle, restore);
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        SwpNomove | SwpNosize | SwpNozorder | SwpNoactivate | SwpFramechanged);
                }
                _borderlessApplied = false;
                _savedStyle = null;
                RynthLog.UI("AcWindowChrome: window borders restored.");
            }
        }
    }

    /// <summary>Write the AC outer window rect when layout remembering is on
    /// (or always when <paramref name="force"/> for "Save layout now").</summary>
    public static void SavePlacement(IntPtr hwnd, bool force = false)
    {
        if (hwnd == IntPtr.Zero) return;
        if (!force && !EngineSettings.RememberWindowLayout) return;
        if (!GetWindowRect(hwnd, out RECT rc)) return;
        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        if (w < 160 || h < 120) return;
        if (IsLikelyExclusiveFullscreen(hwnd, GetWindowLong(hwnd, GwlStyle))) return;

        try
        {
            string path = PlacementPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path,
                FormattableString.Invariant(
                    $"# RynthCore AC window placement — auto-generated.\nleft={rc.Left}\ntop={rc.Top}\nwidth={w}\nheight={h}\n"));
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"AcWindowChrome: save placement failed - {ex.Message}"); } catch { }
        }
    }

    /// <summary>Force-save panels (already on disk via PanelStateStore), AC window,
    /// Vision self-plate, and ImGui.ini. Used by Settings > Display "Save layout now".</summary>
    public static void SaveLayoutNow()
    {
        IntPtr hwnd = ResolveGameHwnd();
        if (hwnd != IntPtr.Zero)
            SavePlacement(hwnd, force: true);

        try { ImGuiBackend.Hud.MonsterHudSettings.Save(); } catch { /* best-effort */ }

        try
        {
            string iniPath = Path.Combine(RynthCore.Install.RynthInstallPaths.RynthAiDir, "imgui.ini");
            Directory.CreateDirectory(RynthCore.Install.RynthInstallPaths.RynthAiDir);
            if (ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGuiNET.ImGui.SaveIniSettingsToDisk(iniPath);
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"AcWindowChrome: imgui.ini save failed - {ex.Message}"); } catch { }
        }

        // PanelStateStore writes on every move already; Flush ensures the file exists
        // even if nothing moved this session (re-write current in-memory state).
        try { PanelStateStore.Flush(); } catch { /* best-effort */ }

        try
        {
            Compatibility.AcMainThreadQueue.EnqueueWriteToChat(
                "[RynthCore] Saved open windows and HUD positions.", 1);
        }
        catch { /* chat may not be ready */ }
    }

    private static void TryRestorePlacement(IntPtr hwnd)
    {
        _placementRestored = true;
        if (!EngineSettings.RememberWindowLayout) return;
        if (IsLikelyExclusiveFullscreen(hwnd, GetWindowLong(hwnd, GwlStyle))) return;

        string path = PlacementPath;
        if (!File.Exists(path)) return;

        int left = 0, top = 0, width = 0, height = 0;
        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
                if (string.Equals(key, "left", StringComparison.OrdinalIgnoreCase)) left = n;
                else if (string.Equals(key, "top", StringComparison.OrdinalIgnoreCase)) top = n;
                else if (string.Equals(key, "width", StringComparison.OrdinalIgnoreCase)) width = n;
                else if (string.Equals(key, "height", StringComparison.OrdinalIgnoreCase)) height = n;
            }
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"AcWindowChrome: read placement failed - {ex.Message}"); } catch { }
            return;
        }

        if (width < 160 || height < 120) return;
        // Keep the restored rect on a visible monitor so a missing display does not
        // park the client off-screen.
        if (!IsRectOnAnyMonitor(left, top, width, height))
        {
            RynthLog.UI("AcWindowChrome: saved placement is off-screen; leaving AC where it is.");
            return;
        }

        SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height,
            SwpNozorder | SwpNoactivate | SwpFramechanged);
        RynthLog.UI(FormattableString.Invariant(
            $"AcWindowChrome: restored AC window to ({left},{top}) {width}x{height}."));
    }

    /// <summary>Game HWND for Settings toggles (EntryPoint, else Win32Backend).</summary>
    internal static IntPtr ResolveGameHwndForSettings() => ResolveGameHwnd();

    private static IntPtr ResolveGameHwnd()
    {
        IntPtr hwnd = EntryPoint.GameHwnd;
        if (hwnd != IntPtr.Zero) return hwnd;
        try { return ImGuiBackend.Win32Backend.GameHwnd; } catch { return IntPtr.Zero; }
    }

    /// <summary>True when the outer rect fills a monitor and has no caption — exclusive
    /// fullscreen or already-borderless fullscreen; do not fight it.</summary>
    private static bool IsLikelyExclusiveFullscreen(IntPtr hwnd, int style)
    {
        if ((style & WsCaption) != 0) return false;
        if (!GetWindowRect(hwnd, out RECT wr)) return false;
        IntPtr mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (mon == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        int mw = mi.rcMonitor.Right - mi.rcMonitor.Left;
        int mh = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        int ww = wr.Right - wr.Left;
        int wh = wr.Bottom - wr.Top;
        return Math.Abs(ww - mw) <= 4 && Math.Abs(wh - mh) <= 4
            && Math.Abs(wr.Left - mi.rcMonitor.Left) <= 4
            && Math.Abs(wr.Top - mi.rcMonitor.Top) <= 4;
    }

    private static bool IsRectOnAnyMonitor(int left, int top, int width, int height)
    {
        // Probe the center of the saved rect; if no monitor claims it, skip restore.
        var pt = new POINT { X = left + width / 2, Y = top + height / 2 };
        return MonitorFromPoint(pt, 0 /* MONITOR_DEFAULTTONULL */) != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
