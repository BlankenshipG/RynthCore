// ============================================================================
//  RynthCore.Engine - UI/AcWindowChrome.cs
//  Settings > Display helpers for the Asheron's Call game HWND:
//    - Borderless windowed: strip caption / thick frame while leaving the client
//      windowed (not exclusive fullscreen). Two sizes (Borderless Fills Monitor):
//        on (default) - fill the monitor the window is on (borderless fullscreen);
//        off          - keep the game area exactly where the framed window had it,
//                       so a custom borderless size/spot is set by placing the framed
//                       window first, then turning borderless on.
//      Stripping the frame alone left the window at AC's resolution with no border
//      to drag and no maximize box: stuck small (worst under DXVK, where AC re-sized
//      the window to its resolution after a device reset). The borderless rect is
//      kept (bounded re-applies), the framed rect comes back when it's turned off,
//      and "Reset Window Borders" / "/rc window reset" always gets a normal,
//      resizable window back.
//    - Remember placement: persist and restore the window across launches when
//      "Remember open windows and HUD positions" is on — the framed rect and the
//      borderless rect separately (ac_window.txt: left/top/width/height and
//      bl_left/bl_top/bl_width/bl_height).
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
    private const int GwlExStyle = -20;
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
    // The first borderless apply this process may take the saved borderless rect (launch restore);
    // later ones (the Settings toggle) use where the window is now.
    private static bool _borderlessRestoreTried;
    // The windowed (framed) rect from before borderless, put back when it's turned off.
    private static RECT? _savedRect;
    // Where the borderless window should be (the monitor, or the kept game area).
    private static RECT? _borderlessTarget;
    // Re-applies after AC re-sized the borderless window to its resolution: counted so the two
    // never fight forever (MaxRefills in RefillWindowMs, then left alone with a log line).
    private const int MaxRefills = 4;
    private const long RefillWindowMs = 60_000;
    private static int _refills;
    private static long _refillWindowStart;
    private static bool _refillGaveUp;

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
            // Another client may have changed Settings > Graphics (graphics.json).
            Graphics.AcGraphics.PollReload();

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
        // Settings > Graphics window mode (this account's profile), else the Display toggles.
        bool want = Graphics.AcGraphics.WantBorderless;
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
                {
                    // Already borderless: keep it where it should be. AC sizes its window to
                    // its own resolution after a device reset (seen with DXVK), which left a
                    // frame-less window too small to fix by hand.
                    KeepAtTarget(hwnd);
                    return;
                }

                // Remember a style that still has chrome. After a hot reload the window
                // can already be borderless while our statics were cleared — OR the mask
                // back on so turning the setting off can still restore a real frame.
                if (_savedStyle is null)
                    _savedStyle = missingChrome ? (style | BorderChromeMask) : style;

                Placement file = EngineSettings.RememberWindowLayout ? ReadPlacementFile() : default;
                bool launch = !_borderlessRestoreTried;
                _borderlessRestoreTried = true;

                // The framed rect to go back to: the saved one at launch (TryRestorePlacement
                // skips while borderless), else where the framed window is now.
                if (_savedRect is null)
                {
                    if (launch && file.Framed is RECT ff && IsRectOnAnyMonitor(ff) && !SavedClientIsLoginSize(hwnd, ff))
                        _savedRect = FitToWorkArea(ff);
                    else if (!missingChrome && GetWindowRect(hwnd, out RECT framed))
                        _savedRect = framed;
                }

                RECT target = ChooseBorderlessTarget(hwnd, missingChrome, launch ? file.Borderless : null);

                SetWindowLong(hwnd, GwlStyle, style & ~BorderChromeMask);
                MoveTo(hwnd, target);
                _borderlessTarget = target;
                _borderlessApplied = true;
                _refills = 0;
                _refillGaveUp = false;
                string how = Graphics.AcGraphics.FillMonitor ? "filling the monitor" : "resolution size";
                RynthLog.UI(FormattableString.Invariant(
                    $"AcWindowChrome: borderless windowed on at ({target.Left},{target.Top}) {Width(target)}x{Height(target)} ({how})."));
                if (EngineSettings.RememberWindowLayout) SavePlacement(hwnd);
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
                    SetWindowLong(hwnd, GwlStyle, restore);
                // Back to a framed window where it was before borderless (else the saved framed
                // placement, else centred), sized for AC's resolution.
                RECT at = _savedRect
                    ?? (ReadPlacementFile().Framed is RECT ff && IsRectOnAnyMonitor(ff) ? ff : DefaultWindowedRect(hwnd));
                RECT target = FramedAtResolution(hwnd, at, restore);
                MoveTo(hwnd, target);
                _borderlessApplied = false;
                _borderlessTarget = null;
                _savedStyle = null;
                _savedRect = null;
                RynthLog.UI(FormattableString.Invariant(
                    $"AcWindowChrome: window borders restored ({Width(target)}x{Height(target)})."));
            }
        }
    }

    /// <summary>Settings > Display "Borderless Fills Monitor" changed: re-place a borderless window
    /// (fill: its monitor; kept size: where the framed window was) and save it.</summary>
    public static void ApplyFillMonitorChanged(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        lock (Sync)
        {
            if (!_borderlessApplied || (GetWindowLong(hwnd, GwlStyle) & WsCaption) != 0) return;
            RECT target;
            if (Graphics.AcGraphics.FillMonitor)
            {
                if (!TryMonitorRect(hwnd, out target, out _)) return;
            }
            else
            {
                // Resolution size, where the framed window was (else centred on the monitor).
                RECT at = _savedRect is RECT fr && IsRectOnAnyMonitor(fr) ? fr : DefaultWindowedRect(hwnd);
                target = BorderlessAtResolution(hwnd, at.Left, at.Top);
            }
            MoveTo(hwnd, target);
            _borderlessTarget = target;
            _refills = 0;
            _refillGaveUp = false;
            string how = Graphics.AcGraphics.FillMonitor ? "filling the monitor" : "resolution size";
            RynthLog.UI(FormattableString.Invariant(
                $"AcWindowChrome: borderless now {Width(target)}x{Height(target)} ({how})."));
            if (EngineSettings.RememberWindowLayout) SavePlacement(hwnd);
        }
    }

    /// <summary>
    /// Settings > Display "Reset Window Borders" and "/rc window reset": borderless off (saved),
    /// the frame back, and a normal window of a sensible size, centred; the saved placement is
    /// replaced by it (the borderless rect is forgotten) so the next launch isn't stuck either.
    /// For a window stuck too small or off-screen. AC's main thread.
    /// </summary>
    public static string ResetWindow()
    {
        IntPtr hwnd = ResolveGameHwnd();
        if (hwnd == IntPtr.Zero) return "The game window isn't known yet.";
        if (EngineSettings.BorderlessWindowed) EngineSettings.BorderlessWindowed = false;
        // This account's Settings > Graphics window mode too, or it would win.
        Graphics.AcGraphics.ResetWindowModeToWindowed();
        lock (Sync)
        {
            int style = GetWindowLong(hwnd, GwlStyle);
            SetWindowLong(hwnd, GwlStyle, style | BorderChromeMask);
            // Centred, sized for the resolution AC is really drawing at.
            RECT target = DefaultWindowedRect(hwnd);
            MoveTo(hwnd, target);
            _styleHwnd = hwnd;
            _borderlessApplied = false;
            _borderlessTarget = null;
            _savedStyle = null;
            _savedRect = null;
            WritePlacementFile(new Placement { Framed = target, Borderless = null });
            RynthLog.UI(FormattableString.Invariant(
                $"AcWindowChrome: window reset to a framed {Width(target)}x{Height(target)}."));
            return FormattableString.Invariant(
                $"Window reset: borderless off, normal resizable window ({Width(target)}x{Height(target)}).");
        }
    }

    /// <summary>"/rc window save": write the window's placement now (framed or borderless).</summary>
    public static string SaveWindowNow()
    {
        IntPtr hwnd = ResolveGameHwnd();
        if (hwnd == IntPtr.Zero) return "The game window isn't known yet.";
        if (!SavePlacement(hwnd, force: true)) return "Nothing saved (not in the world, exclusive fullscreen, or the window is too small).";
        bool borderless = (GetWindowLong(hwnd, GwlStyle) & WsCaption) == 0;
        return borderless ? "Borderless window position saved." : "Window position saved.";
    }

    /// <summary>Where a borderless window goes: fill (the saved rect's monitor at launch, else the
    /// window's) or resolution size (at the saved spot at launch, else where the framed window's game
    /// area is, so the picture doesn't move).</summary>
    private static RECT ChooseBorderlessTarget(IntPtr hwnd, bool missingChrome, RECT? savedBorderless)
    {
        bool useSaved = savedBorderless is RECT sb && IsRectOnAnyMonitor(sb) && !IsLoginScreenSize(Width(sb), Height(sb));
        if (Graphics.AcGraphics.FillMonitor)
        {
            if (useSaved && TryMonitorRectAt(savedBorderless!.Value, out RECT savedMon)) return savedMon;
            if (TryMonitorRect(hwnd, out RECT mon, out _)) return mon;
        }
        else
        {
            if (useSaved) return BorderlessAtResolution(hwnd, savedBorderless!.Value.Left, savedBorderless.Value.Top);
            if (!missingChrome && TryClientRectOnScreen(hwnd, out RECT client)) return BorderlessAtResolution(hwnd, client.Left, client.Top);
            if (GetWindowRect(hwnd, out RECT wr)) return BorderlessAtResolution(hwnd, wr.Left, wr.Top);
        }
        return DefaultWindowedRect(hwnd);
    }

    /// <summary>
    /// Settings > Graphics changed the window mode: apply it to the window now (a Resolution change
    /// waits for the next launch: the window always follows what AC really draws at).
    /// Borderless modes go through <see cref="ApplyBorderlessDesired"/> (and re-place a window that
    /// is already borderless); windowed and full screen get a frame sized for the resolution, where
    /// the window is. Full screen itself (AC's exclusive mode) starts at the next launch. AC's main thread.
    /// </summary>
    public static void ApplyGraphicsChanged(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        lock (Sync)
        {
            bool framed = (GetWindowLong(hwnd, GwlStyle) & WsCaption) != 0;
            if (Graphics.AcGraphics.WantBorderless)
            {
                if (_borderlessApplied && !framed) ApplyFillMonitorChanged(hwnd);
                else ApplyBorderlessDesired(hwnd);
                return;
            }
            if (_borderlessApplied || !framed)
            {
                ApplyBorderlessDesired(hwnd);   // frame back, sized for the resolution
                return;
            }
            if (!GetWindowRect(hwnd, out RECT wr)) return;
            RECT target = FramedAtResolution(hwnd, wr, GetWindowLong(hwnd, GwlStyle));
            if (!SameRect(target, wr)) MoveTo(hwnd, target);
            string later = Graphics.AcGraphics.WindowMode == AcGraphicsModes.FullScreen ? " (full screen starts at the next launch)" : "";
            RynthLog.UI(FormattableString.Invariant(
                $"AcWindowChrome: window set to {Width(target)}x{Height(target)} for {Graphics.AcGraphics.WindowMode}{later}."));
            if (EngineSettings.RememberWindowLayout) SavePlacement(hwnd);
        }
    }

    /// <summary>Short names for the window mode ids (RynthCore.Graphics.AcGraphicsPrefs).</summary>
    private static class AcGraphicsModes
    {
        public const string FullScreen = global::RynthCore.Graphics.AcGraphicsPrefs.ModeFullScreen;
    }

    /// <summary>The outer size of a window with <paramref name="style"/> whose game area is the size AC
    /// is really drawing at (its back buffer, else the resolution it launched with). False when unknown.</summary>
    private static bool TryFramedSizeForResolution(IntPtr hwnd, int style, out int width, out int height)
    {
        width = height = 0;
        if (!Graphics.AcGraphics.TryGetDrawSize(out int rw, out int rh)) return false;
        var r = new RECT { Left = 0, Top = 0, Right = rw, Bottom = rh };
        // The frame the window really has (AC's is fixed: no sizing border). A frame-less window
        // gets the frame it will be given back (its style plus the chrome mask).
        int framedStyle = (style & WsCaption) != 0 ? style : style | BorderChromeMask;
        if (!AdjustWindowRectEx(ref r, framedStyle, false, GetWindowLong(hwnd, GwlExStyle))) return false;
        width = Width(r);
        height = Height(r);
        return true;
    }

    /// <summary>A framed rect at <paramref name="at"/>'s top-left, sized for AC's resolution (else
    /// <paramref name="at"/>'s size), kept inside the screen.</summary>
    private static RECT FramedAtResolution(IntPtr hwnd, RECT at, int style)
    {
        if (!TryFramedSizeForResolution(hwnd, style, out int w, out int h)) return FitToWorkArea(at);
        return FitToWorkArea(new RECT { Left = at.Left, Top = at.Top, Right = at.Left + w, Bottom = at.Top + h });
    }

    /// <summary>A frame-less rect of AC's resolution at (<paramref name="left"/>, <paramref name="top"/>),
    /// kept inside the screen; the monitor when the resolution isn't known.</summary>
    private static RECT BorderlessAtResolution(IntPtr hwnd, int left, int top)
    {
        if (!Graphics.AcGraphics.TryGetDrawSize(out int rw, out int rh))
            return TryMonitorRect(hwnd, out RECT mon, out _) ? mon : DefaultWindowedRect(hwnd);
        return FitToWorkArea(new RECT { Left = left, Top = top, Right = left + rw, Bottom = top + rh });
    }

    /// <summary>Borderless but no longer where it should be (AC re-sized it): put it back, a few times at most.</summary>
    private static void KeepAtTarget(IntPtr hwnd)
    {
        if (_refillGaveUp || _borderlessTarget is not RECT target) return;
        if (!GetWindowRect(hwnd, out RECT wr)) return;
        if (SameRect(wr, target)) return;
        long now = Environment.TickCount64;
        if (now - _refillWindowStart > RefillWindowMs) { _refillWindowStart = now; _refills = 0; }
        if (++_refills > MaxRefills)
        {
            _refillGaveUp = true;
            RynthLog.UI("AcWindowChrome: AC keeps re-sizing the borderless window; leaving it (Settings > Display > Reset Window Borders, or \"/rc window reset\", for a normal window).");
            return;
        }
        RynthLog.UI(FormattableString.Invariant(
            $"AcWindowChrome: borderless window was {Width(wr)}x{Height(wr)}; putting it back to {Width(target)}x{Height(target)}."));
        MoveTo(hwnd, target);
    }

    private static void MoveTo(IntPtr hwnd, RECT r) =>
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, Width(r), Height(r),
            SwpNozorder | SwpNoactivate | SwpFramechanged);

    private static int Width(RECT r) => r.Right - r.Left;
    private static int Height(RECT r) => r.Bottom - r.Top;

    private static bool SameRect(RECT a, RECT b) =>
        Math.Abs(a.Left - b.Left) <= 2 && Math.Abs(a.Top - b.Top) <= 2
        && Math.Abs(a.Right - b.Right) <= 2 && Math.Abs(a.Bottom - b.Bottom) <= 2;

    /// <summary>A framed window centred on the monitor's work area: sized for AC's resolution, else
    /// three quarters of the work area. Never bigger than the work area.</summary>
    private static RECT DefaultWindowedRect(IntPtr hwnd)
    {
        if (!TryMonitorRect(hwnd, out _, out RECT work))
            return new RECT { Left = 50, Top = 50, Right = 50 + 1296, Bottom = 50 + 759 };
        int ww = Width(work), wh = Height(work);
        int w = ww * 3 / 4, h = wh * 3 / 4;
        if (TryFramedSizeForResolution(hwnd, GetWindowLong(hwnd, GwlStyle), out int rw, out int rh))
        {
            w = Math.Min(rw, ww);
            h = Math.Min(rh, wh);
        }
        int left = work.Left + (ww - w) / 2, top = work.Top + (wh - h) / 2;
        return new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
    }

    /// <summary>The whole rect and the work area of the monitor nearest the window.</summary>
    private static bool TryMonitorRect(IntPtr hwnd, out RECT monitor, out RECT work) =>
        TryMonitorInfo(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), out monitor, out work);

    /// <summary>The whole rect of the monitor holding the centre of <paramref name="r"/>.</summary>
    private static bool TryMonitorRectAt(RECT r, out RECT monitor)
    {
        var pt = new POINT { X = r.Left + Width(r) / 2, Y = r.Top + Height(r) / 2 };
        return TryMonitorInfo(MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */), out monitor, out _);
    }

    private static bool TryMonitorInfo(IntPtr mon, out RECT monitor, out RECT work)
    {
        monitor = default; work = default;
        if (mon == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        monitor = mi.rcMonitor;
        work = mi.rcWork;
        return true;
    }

    /// <summary>The client area in screen coordinates (the game picture, without the frame).</summary>
    private static bool TryClientRectOnScreen(IntPtr hwnd, out RECT r)
    {
        r = default;
        if (!GetClientRect(hwnd, out RECT c)) return false;
        var tl = new POINT { X = c.Left, Y = c.Top };
        if (!ClientToScreen(hwnd, ref tl)) return false;
        r = new RECT { Left = tl.X, Top = tl.Y, Right = tl.X + Width(c), Bottom = tl.Y + Height(c) };
        return Width(r) >= 160 && Height(r) >= 120;
    }

    /// <summary>Write the AC window rect when layout remembering is on (or always when
    /// <paramref name="force"/> for "Save layout now"): the framed rect while it has a frame, the
    /// borderless rect while it's borderless (ours, not exclusive fullscreen). The other is kept.
    /// True when something was written.</summary>
    public static bool SavePlacement(IntPtr hwnd, bool force = false)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (!force && !EngineSettings.RememberWindowLayout) return false;
        // Only the world's window is the player's placement: at the login and character-select
        // screens AC shrinks its window to 800x600, and saving that made every client start small.
        if (Compatibility.UiFlowHooks.CurrentMode != Compatibility.UiFlowHooks.ModeGamePlay) return false;
        int style = GetWindowLong(hwnd, GwlStyle);
        bool framed = (style & WsCaption) != 0;
        // Frame-less and not made by us: exclusive fullscreen (or not ours to remember).
        if (!framed && !_borderlessApplied) return false;
        if (!GetWindowRect(hwnd, out RECT rc)) return false;
        if (Width(rc) < 160 || Height(rc) < 120) return false;
        if (GetClientRect(hwnd, out RECT client) && IsLoginScreenSize(Width(client), Height(client))) return false;

        Placement p = ReadPlacementFile();
        if (framed) p.Framed = rc;
        else p.Borderless = rc;
        return WritePlacementFile(p);
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
        // Borderless restores its own rect (ApplyBorderlessDesired); the framed one would only
        // move the window first.
        if (Graphics.AcGraphics.WantBorderless) return;
        int style = GetWindowLong(hwnd, GwlStyle);
        if (IsLikelyExclusiveFullscreen(hwnd, style)) return;

        if (ReadPlacementFile().Framed is not RECT saved) return;
        // Only the saved position is the player's: the size always comes from the resolution AC is
        // really drawing at (its back buffer, else what it launched with), so a size saved at another resolution
        // or at the login screen can't shrink the window.
        bool sized = TryFramedSizeForResolution(hwnd, style, out int rw, out int rh);
        RECT r = sized
            ? new RECT { Left = saved.Left, Top = saved.Top, Right = saved.Left + rw, Bottom = saved.Top + rh }
            : saved;
        // Without a known resolution, a rect saved at the character-select screen (800x600 game
        // area, before 2026.10.17.8) isn't the player's window: restoring it shrank the world window.
        if (!sized && SavedClientIsLoginSize(hwnd, r))
        {
            RynthLog.UI(FormattableString.Invariant(
                $"AcWindowChrome: ignoring saved placement {Width(r)}x{Height(r)} (AC's 800x600 login-screen size); leaving AC's own size."));
            return;
        }
        // Keep the restored rect on a visible monitor so a missing display does not
        // park the client off-screen.
        if (!IsRectOnAnyMonitor(r))
        {
            RynthLog.UI("AcWindowChrome: saved placement is off-screen; leaving AC where it is.");
            return;
        }

        // AC anchors its toolbar to the window's bottom-right: a saved rect bigger than this
        // monitor (another PC, a lower resolution, a torn read) parked it off-screen.
        RECT fitted = FitToWorkArea(r);
        MoveTo(hwnd, fitted);
        string note = SameRect(fitted, r) ? ""
            : FormattableString.Invariant($" (saved {Width(r)}x{Height(r)} at ({r.Left},{r.Top}) didn't fit the screen)");
        RynthLog.UI(FormattableString.Invariant(
            $"AcWindowChrome: restored AC window to ({fitted.Left},{fitted.Top}) {Width(fitted)}x{Height(fitted)}{note}."));
    }

    /// <summary>AC's login / character-select window: an 800x600 game area.</summary>
    private static bool IsLoginScreenSize(int clientW, int clientH) => clientW == 800 && clientH == 600;

    /// <summary>True when the saved framed rect <paramref name="saved"/> holds an 800x600 game area
    /// with this window's frame (the frame size is taken from the window as it is now).</summary>
    private static bool SavedClientIsLoginSize(IntPtr hwnd, RECT saved)
    {
        if (!GetWindowRect(hwnd, out RECT wr) || !GetClientRect(hwnd, out RECT cr)) return false;
        if ((GetWindowLong(hwnd, GwlStyle) & WsCaption) == 0) return false;
        int frameW = Width(wr) - Width(cr), frameH = Height(wr) - Height(cr);
        return IsLoginScreenSize(Width(saved) - frameW, Height(saved) - frameH);
    }

    /// <summary><paramref name="r"/> moved, and shrunk if needed, to lie inside the work area of the
    /// monitor holding its centre, so no edge of the window (or AC's toolbar) is off-screen.</summary>
    private static RECT FitToWorkArea(RECT r)
    {
        var pt = new POINT { X = r.Left + Width(r) / 2, Y = r.Top + Height(r) / 2 };
        if (!TryMonitorInfo(MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */), out _, out RECT work)) return r;
        int w = Math.Min(Width(r), Width(work));
        int h = Math.Min(Height(r), Height(work));
        int left = Math.Clamp(r.Left, work.Left, work.Right - w);
        int top = Math.Clamp(r.Top, work.Top, work.Bottom - h);
        return new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
    }

    /// <summary>ac_window.txt: the framed rect (left/top/width/height, as before) and the
    /// borderless rect (bl_ keys). Either may be missing.</summary>
    private struct Placement
    {
        public RECT? Framed;
        public RECT? Borderless;
    }

    private static Placement ReadPlacementFile()
    {
        var p = new Placement();
        string path = PlacementPath;
        if (!File.Exists(path)) return p;

        int left = 0, top = 0, width = 0, height = 0;
        int bl = 0, bt = 0, bw = 0, bh = 0;
        try
        {
            // Every client on the PC shares this file: read with sharing on, so a read never
            // fails because another client has it open.
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim().ToLowerInvariant();
                string val = line[(eq + 1)..].Trim();
                if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
                switch (key)
                {
                    case "left": left = n; break;
                    case "top": top = n; break;
                    case "width": width = n; break;
                    case "height": height = n; break;
                    case "bl_left": bl = n; break;
                    case "bl_top": bt = n; break;
                    case "bl_width": bw = n; break;
                    case "bl_height": bh = n; break;
                }
            }
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"AcWindowChrome: read placement failed - {ex.Message}"); } catch { }
            return p;
        }

        if (width >= 160 && height >= 120)
            p.Framed = new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
        if (bw >= 160 && bh >= 120)
            p.Borderless = new RECT { Left = bl, Top = bt, Right = bl + bw, Bottom = bt + bh };
        return p;
    }

    private static bool WritePlacementFile(Placement p)
    {
        try
        {
            string path = PlacementPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder("# RynthCore AC window placement — auto-generated.\n");
            if (p.Framed is RECT f)
                sb.Append(FormattableString.Invariant(
                    $"left={f.Left}\ntop={f.Top}\nwidth={Width(f)}\nheight={Height(f)}\n"));
            if (p.Borderless is RECT b)
                sb.Append(FormattableString.Invariant(
                    $"bl_left={b.Left}\nbl_top={b.Top}\nbl_width={Width(b)}\nbl_height={Height(b)}\n"));
            string text = sb.ToString();

            // Every client saves every 10 s: skip a write that changes nothing, so clients
            // rarely touch the shared file at all.
            if (ReadPlacementText(path) == text) return true;

            // Write a temp file and swap it in, so another client never reads a half-written
            // file (a torn read restored a wrong rect). A sharing clash is retried, then left
            // for the next save.
            string tmp = FormattableString.Invariant($"{path}.{Environment.ProcessId}.tmp");
            File.WriteAllText(tmp, text);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return true;
                }
                catch (IOException) when (attempt < 3)
                {
                    System.Threading.Thread.Sleep(15);
                }
                catch (UnauthorizedAccessException) when (attempt < 3)
                {
                    System.Threading.Thread.Sleep(15);
                }
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(FormattableString.Invariant($"{PlacementPath}.{Environment.ProcessId}.tmp")); } catch { }
            try { RynthLog.UI($"AcWindowChrome: save placement failed - {ex.Message}"); } catch { }
            return false;
        }
    }

    /// <summary>The placement file's text (shared read), or null when missing or unreadable.</summary>
    private static string? ReadPlacementText(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch { return null; }
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
        if (!TryMonitorRect(hwnd, out RECT mon, out _)) return false;
        return Math.Abs(Width(wr) - Width(mon)) <= 4 && Math.Abs(Height(wr) - Height(mon)) <= 4
            && Math.Abs(wr.Left - mon.Left) <= 4
            && Math.Abs(wr.Top - mon.Top) <= 4;
    }

    private static bool IsRectOnAnyMonitor(RECT r)
    {
        // Probe the center of the saved rect; if no monitor claims it, skip restore.
        var pt = new POINT { X = r.Left + Width(r) / 2, Y = r.Top + Height(r) / 2 };
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
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT lpRect, int dwStyle, bool bMenu, int dwExStyle);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
