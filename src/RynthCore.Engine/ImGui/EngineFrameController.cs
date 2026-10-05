// ============================================================================
//  RynthCore.Engine - ImGui/EngineFrameController.cs
//  Top-level orchestrator: drives the per-frame engine pipeline from EndScene.
//
//  Per-frame work splits into two layers:
//    * Always-on (when EndScene fires): GameMatrixCapture, Nav3DRenderInjector
//      install/reset, PluginManager init/pending/delete/tick, DX9Backend.RenderNav3D
//      fallback. These run regardless of EngineSettings.EnableImGuiBackend so
//      world→screen, nav markers, and plugin lifecycle stay alive even when
//      the ImGui surface is disabled.
//    * ImGui-gated (only when EngineSettings.EnableImGuiBackend is true):
//      Win32Backend.NewFrame / DX9Backend.NewFrame, ImGui.NewFrame/EndFrame/
//      Render, RynthCoreShell + plugin RenderAll, DX9Backend.RenderDrawData,
//      mouse/keyboard capture flag updates.
//
//  Renamed 2026-05-10 from ImGuiController — the type now owns more than
//  ImGui's frame; the ImGui block is just one branch of its work.
// ============================================================================

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImGuiNET;
using RynthCore.Engine.D3D9;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.ImGuiBackend;

internal static class EngineFrameController
{
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static bool _imguiInitialized;
    private static bool _imguiInitFailed;
    private static bool _coreResolved;
    private static bool _pluginsInitialized;
    private static IntPtr _context;
    private static IntPtr _gameHwnd;

    /// <summary>
    /// Live D3D9 device pointer, published by <see cref="OnEndScene"/> on AC's
    /// render thread and consumed by <see cref="PumpPluginFrame"/> on the
    /// dedicated plugin pump thread. Zero until the first real backbuffer
    /// frame — the pump no-ops until then (we need the HWND/core resolved and
    /// a device to hand plugins). Volatile: written by render thread, read by
    /// pump thread; IntPtr writes are atomic on this platform.
    /// </summary>
    internal static volatile IntPtr CachedDevice;
    private static int _pluginPumpInFrame; // re-entrancy guard for the pump
    private static long _lastFrameTicks;
    private static int _frameCount;

    /// <summary>
    /// Resolves the AC game HWND (via the D3D9 device, falling back to
    /// EnumWindows) and seeds <see cref="EntryPoint.GameHwnd"/>. Required by
    /// the always-on path (Nav3DRenderInjector + DX9Backend core delegates)
    /// and by the ImGui init path. Idempotent — safe to call every frame.
    /// </summary>
    public static bool EnsureCore(IntPtr pDevice)
    {
        if (_coreResolved && _gameHwnd != IntPtr.Zero)
            return true;

        // Prefer the actual D3D device window so startup does not latch onto a splash/loading HWND.
        _gameHwnd = DX9Backend.GetDeviceWindow(pDevice);
        if (_gameHwnd == IntPtr.Zero)
        {
            RynthLog.Render("EngineFrameController: D3D device window unavailable, falling back to EnumWindows.");
            _gameHwnd = FindGameWindow();
        }

        if (_gameHwnd == IntPtr.Zero)
        {
            RynthLog.Render("EngineFrameController: Could not find game window.");
            return false;
        }

        RynthLog.Render($"EngineFrameController: Game HWND = 0x{_gameHwnd:X8}");
        EntryPoint.GameHwnd = _gameHwnd;
        _coreResolved = true;
        // Pop-outs hold D3DPOOL_DEFAULT render targets: release them before AC resets a lost device.
        D3D9.DeviceResetHook.InstallFromDevice(pDevice);
        return true;
    }

    public static bool Init(IntPtr pDevice)
    {
        if (_imguiInitialized)
            return true;

        if (!EnsureCore(pDevice))
            return false;

        // Before any context exists: the loaded cimgui.dll must be the exact
        // build ImGui.NET wraps, or every struct read below is at wrong offsets.
        if (!ImGuiSelfTest.Run(EntryPoint.ImGuiNativeHandle))
            return false;

        IntPtr previousContext = ImGuiNET.ImGui.GetCurrentContext();
        if (previousContext != IntPtr.Zero)
            RynthLog.Render($"EngineFrameController: Existing ImGui context detected (0x{previousContext:X8}) - isolating RynthCore context.");

        _context = ImGuiNET.ImGui.CreateContext();
        bool initSucceeded = false;
        try
        {
            ImGuiNET.ImGui.SetCurrentContext(_context);
            ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();

            // Pin imgui.ini to a stable absolute path so window positions/sizes
            // persist across plugin reloads (RL) and AC restarts. The default
            // ImGui behavior writes to the working directory which can differ
            // between launches. Allocated once, kept alive for the process lifetime.
            try
            {
                const string iniPath = @"C:\Games\RynthSuite\RynthAi\imgui.ini";
                System.IO.Directory.CreateDirectory(@"C:\Games\RynthSuite\RynthAi");
                IntPtr iniPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(iniPath);
                unsafe { io.NativePtr->IniFilename = (byte*)iniPtr; }
                RynthLog.Render($"EngineFrameController: imgui.ini pinned to {iniPath}");
            }
            catch (Exception ex)
            {
                RynthLog.Render($"EngineFrameController: failed to pin imgui.ini path - {ex.Message}");
            }

            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            // Multi-viewport (ViewportsEnable) is deliberately never enabled: it
            // runs many native->managed platform/renderer callbacks per frame on
            // AC's thread and creates HWNDs from inside EndScene. Pop-out panels,
            // if built, are driven explicitly by the engine instead
            // (docs/IMGUI_PARITY_PLAN.md section 4.2).

            // DPI scale for fonts and style metrics. Logged next to Avalonia's
            // scale: docked positions are stored in Avalonia logical units, so
            // the two must agree for a panel to land in the same place in
            // either face (plan section 4.4).
            _uiScale = UiScale.ForWindow(_gameHwnd);
            RynthLog.UI($"EngineFrameController: ImGui scale {_uiScale:0.##} (Avalonia input scale {UI.AvaloniaOverlay.InputScale:0.##}).");
            RynthTheme.Apply(ImGuiNET.ImGui.GetStyle(), _uiScale);
            ImGuiFonts.Build(io, _uiScale);

            if (!Win32Backend.Init(_gameHwnd))
                return false;

            if (!DX9Backend.InitImGui(pDevice))
                return false;

            _lastFrameTicks = Stopwatch.GetTimestamp();
            _imguiInitialized = true;
            initSucceeded = true;

            RynthLog.UI("EngineFrameController: ImGui initialized.");
            return true;
        }
        finally
        {
            if (!initSucceeded && _context != IntPtr.Zero)
            {
                ImGuiNET.ImGui.DestroyContext(_context);
                _context = IntPtr.Zero;
            }

            ImGuiNET.ImGui.SetCurrentContext(previousContext);
        }
    }

    public static void Shutdown()
    {
        if (!_imguiInitialized)
            return;

        IntPtr previousContext = ImGuiNET.ImGui.GetCurrentContext();
        IntPtr contextToDestroy = _context;

        try
        {
            ImGuiNET.ImGui.SetCurrentContext(contextToDestroy);

            PluginManager.ShutdownAll();
            // No context may be destroyed while a font worker runs (ImGuiFonts, re-bake note).
            ImGuiFonts.WaitForWorkers();
            // Pop-outs share the main font atlas: their contexts go before the main one.
            ImGuiPopOuts.Shutdown();
            OverlayTextureRenderer.Shutdown();
            // Its lists are drawn by DX9Backend from the DIP hook, which is still live.
            UnderUiLayer.Shutdown();
            ScriptIcons.Shutdown();   // script window icon textures, before the backend goes
            RetailSprites.Shutdown(); // retail UI pictures (Inventory Classic view), likewise
            DX9Backend.Shutdown();
            // Do NOT call Win32Backend.Shutdown() here. EngineLifecycle.Shutdown
            // owns that step explicitly, AFTER AvaloniaOverlay.Stop — floating
            // panels can only be destroyed on the game thread by our subclass
            // WndProc (Win32Backend.Shutdown sweeps any still open before it
            // unhooks), so restoring AC's original WndProc this early sends
            // those destroys to AC's proc, which ignores them, and the orphaned
            // panel HWND outlives the engine load.
            ImGuiNET.ImGui.DestroyContext(contextToDestroy);
            ImGuiFonts.Free(); // the atlas referenced these buffers; the context is gone now

            _imguiInitialized = false;
            _coreResolved = false;
            _context = IntPtr.Zero;
        }
        finally
        {
            ImGuiNET.ImGui.SetCurrentContext(previousContext == contextToDestroy ? IntPtr.Zero : previousContext);
        }
    }

    public static void OnEndScene(IntPtr pDevice)
    {
        // ── Always-on engine pipeline ────────────────────────────────────
        // These run on every EndScene tick, regardless of
        // EngineSettings.EnableImGuiBackend, so world→screen capture, nav-
        // marker rendering, and plugin lifecycle stay alive when the ImGui
        // surface is disabled.
        try
        {
            // Resolve game HWND and seed EntryPoint.GameHwnd. Required even
            // in ImGui-off mode so Avalonia owner-window binding works.
            EnsureCore(pDevice);

            // Cache the D3D9 device function pointers used by Nav3DRenderInjector,
            // RenderNav3D, and the always-on render path. This is a strict subset
            // of what InitImGui needs and is safe to run without ImGui state.
            DX9Backend.InitCore(pDevice);

            // Install the DrawIndexedPrimitive hook that injects Nav3D markers at
            // the 3D→UI ZENABLE transition so they render BEHIND AC's UI. Idempotent
            // (installs once); placed here because its Detour reads the device
            // delegates InitCore (above) just cached. WITHOUT THIS the injector never
            // accumulates, RenderedThisFrame stays false on every frame, and the
            // end-of-frame fallback below paints markers OVER the UI. This call was
            // dropped in the v0.19 Nav3D double-buffer refactor — restored here.
            // Since 2026-10-04 this is the second choice: with AcUiPassHook live the
            // overlays draw at AC's UI pass and SyncHookState (end of frame) switches
            // this hook off; it comes back for /rc worldlayer transition or without it.
            D3D9.Nav3DRenderInjector.Install(pDevice);

            // Capture the game's View/Projection matrices before ImGui touches them
            GameMatrixCapture.CaptureFrame(pDevice);
            // ...and AC's 3D view viewport: at AC's UI pass (AcUiPassHook) the viewport is
            // full-screen, and the Nav3D markers draw in this one there.
            DX9Backend.CaptureSceneViewport(pDevice);

            // Refresh the player-skill snapshot on AC's main thread (this
            // EndScene runs on it). The plugin tick is pumped off-thread and
            // can't read skills live (TryGetObjectQualitiesPtr fail-closes);
            // without this it gets (0,0) → tier-1 casts and never buffs.
            // Throttled internally.
            Compatibility.ClientObjectHooks.PrefetchPlayerSkills();
            // Same rationale + same main-thread guard: snapshot the spellbook
            // so the off-thread pump can resolve tiers against spells the char
            // actually knows (throttled internally).
            Compatibility.ClientObjectHooks.PrefetchKnownSpells();
            // Same main-thread guard + rationale: snapshot AC's real
            // ObjectIsAttackable for every live object so the off-thread pump
            // can tell NPC/vendor (attackable=false) from monster. Without it
            // the pump got hardcoded true and the classifier promoted NPCs to
            // attackable creatures → war magic cast at NPCs (throttled
            // internally).
            Compatibility.ClientObjectHooks.PrefetchAttackable();
            // Same main-thread guard + rationale: snapshot object names and
            // item types for every live object so the off-thread plugin pump
            // can classify objects without walking AC's live CObjectMaint
            // table (0x0067E779 READ-AV class; throttled internally at 3 s).
            Compatibility.ClientObjectHooks.PrefetchObjectIdentity();
            // Same main-thread guard: snapshot every live object's POSITION so the
            // off-thread plugin pump reads position from cache instead of resolving
            // _getWeenieObject live — that resolution is AC's CObjectMaint walk and
            // is the dump-verified 0x0067E779 READ-AV when done on the pump thread.
            // Sampled every frame (positions are volatile), zero-alloc.
            Compatibility.ClientObjectHooks.PrefetchPositions();
            // Refresh the cached player id every frame (unthrottled: the native leaf is
            // two loads) and the other small main-thread snapshots (cast gate, pose,
            // ...). Off-thread callers are served from these instead of reading AC
            // memory from the pump thread. Also runs from the Client::UseTime drain.
            Compatibility.MainThreadSnapshots.Tick();
            // Same main-thread guard: snapshot XP/luminance/deaths/vitae (main-thread-only
            // Inq* reads) so the GetEngineStatusJson host bridge can serve them off-thread (throttled).
            Compatibility.ClientObjectHooks.PrefetchPlayerStats();

            // Cold-login object backfill on the SAME AC main thread as the
            // skill prefetch above: deliver objects already present at login
            // (the incremental CreateObject hook only catches NEW spawns, so
            // login-present mobs were never reaching the plugin — proven via
            // per-id ClassifyTrace). One-shot per login, IsOnMainThread-gated
            // and attempt-bounded internally; reuses the guarded
            // QueueCreateObject delivery path. EndScene is a build-independent
            // anchor (D3D9 hook, not string-resolved like SmartBox).
            try { Plugins.PluginManager.TrySeedLiveObjectsFromCObjectMaintOnce(); }
            catch (Exception ex) { RynthLog.Plugin($"CObjectMaint seed threw {ex.GetType().Name}: {ex.Message}"); }

            // P1: drain off-thread bot actions (UseObject / ChangeCombatMode / melee /
            // missile / movement / jump) here on AC's MAIN thread. The pump enqueues
            // them instead of calling AC directly, so AC mutates its single-threaded
            // object/animation (CSequence) state on the same thread that updates it —
            // closing the off-thread-mutation corruption class (the CSequence::
            // update_internal AV captured 2026-06-03, the 0x0055FA24 range-list AV, etc).
            // CastSpell is deliberately NOT routed here (stays off-thread + SEH).
            try { Compatibility.AcMainThreadQueue.Drain(); }
            catch (Exception ex) { RynthLog.Plugin($"AcMainThreadQueue.Drain threw {ex.GetType().Name}: {ex.Message}"); }

            // The world overlays (Nav3D markers, nameplates, combat text) normally drew
            // mid-frame, before this EndScene: at the start of AC's 2D UI pass
            // (AcUiPassHook), or without that hook at the 3D->UI ZENABLE transition
            // (Nav3DRenderInjector's DIP hook). A frame where neither fired (portal space,
            // a loading screen, no AC UI) draws them at EndScene instead - see
            // Nav3DRenderInjector.PresentAtEndScene below. ResetFrame at the END of
            // OnEndScene primes the next frame.

            // Publish the live device pointer for the off-render-thread plugin
            // pump (see PumpPluginFrame). The D3D9 device is a stable COM
            // pointer for the session, so caching it is safe; the pump only
            // passes it opaquely to plugins (RynthAi in Avalonia-only mode
            // never dereferences it). Atomic IntPtr write; volatile read.
            CachedDevice = pDevice;

            // NOTE (2026-05-16): PluginManager.InitPlugins / ProcessPendingActions
            // / FlushPendingDeletes / TickAll are NO LONGER called here. The
            // 2026-05-10 refactor put them on this EndScene reverse-P/Invoke
            // (AC's D3D9 render thread); a GC during the plugin tick on that
            // AC-owned thread fail-fasts in NativeAOT's
            // RhpReversePInvokeAttachOrTrapThread2 (proven from the
            // 2026-05-16 09:39 dump). They now run on the dedicated managed
            // plugin pump thread (EntryPoint.StartNormalPluginPump →
            // PumpPluginFrame), which mirrors the proven DecalCoexistence
            // pump. EXACTLY ONE TickAll driver: this render path no longer
            // calls it at all.

            // ── ImGui-gated work ─────────────────────────────────────────
            // The frame is BUILT here and SUBMITTED after the Nav3D fallback and
            // the vital HUD, so ImGui windows sit above both. The Avalonia quad
            // (EndSceneHook, after this returns) stays topmost while both stacks
            // run, matching input order: Avalonia gets the first hit test.
            bool imguiEnabled = Plugins.EngineSettings.EnableImGuiBackend;
            bool imguiBuilt = false;
            long uiTicks = 0;
            if (!imguiEnabled)
                _imguiIdle = true; // switched off in game: input state resets when it comes back
            else
            {
                long t0 = Stopwatch.GetTimestamp();
                imguiBuilt = BuildImGuiFrame(pDevice);
                uiTicks = Stopwatch.GetTimestamp() - t0;
            }

            // Fallback: the Nav3D markers and the world overlays built last frame
            // (nameplates, combat text), when neither the UI pass nor the transition
            // drew them this frame - still under the ImGui panels. No-op when they
            // already drew (one per-frame flag for every path; UnderUiLayer presents
            // a list once). _coreInitialized is enough for the markers.
            D3D9.Nav3DRenderInjector.PresentAtEndScene(pDevice);

            if (imguiEnabled)
            {
                long t0 = Stopwatch.GetTimestamp();
                if (imguiBuilt)
                    SubmitImGuiFrame(pDevice);
                // Popped-out panels: their own contexts, windows and render targets.
                if (_imguiInitialized)
                    ImGuiPopOuts.RenderAll(pDevice, _uiScale, PanelsLive);
                UiFrameStats.Record(uiTicks + (Stopwatch.GetTimestamp() - t0));
            }
        }
        catch (Exception ex)
        {
            RynthLog.Info($"EngineFrameController: frame {_frameCount} engine error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Prime the injector for the next frame's DIP accumulation.
            // Without this, _markersRenderedThisFrame latches true after
            // the first 3D→UI transition in the session and the injector
            // never fires again — every subsequent frame falls back to the
            // end-of-frame draw (which paints over the UI).
            D3D9.Nav3DRenderInjector.ResetFrame();
            // The DIP hook only runs while the transition path is in use (no UI-pass
            // hook, or /rc worldlayer transition); switched here, between frames.
            try { D3D9.Nav3DRenderInjector.SyncHookState(); }
            catch (Exception ex) { RynthLog.D3D9($"Nav3DRenderInjector.SyncHookState threw {ex.GetType().Name}: {ex.Message}"); }
            // What this frame built for the world overlays is presented next frame.
            UnderUiLayer.EndFrame();
        }
    }

    /// <summary>
    /// The main-thread prefetches of the always-on pipeline above, for a client without
    /// the EndScene hook (Decal in the process): called from the Client::UseTime detour
    /// (AC's main thread) once GameTickHooks.HeadlessPrefetch is set. Same calls, same
    /// order, each throttled and main-thread-gated internally. The EndScene path doesn't
    /// use this (it runs the calls inline, unchanged).
    /// </summary>
    internal static void RunHeadlessPrefetch()
    {
        Compatibility.ClientObjectHooks.PrefetchPlayerSkills();
        Compatibility.ClientObjectHooks.PrefetchKnownSpells();
        Compatibility.ClientObjectHooks.PrefetchAttackable();
        Compatibility.ClientObjectHooks.PrefetchObjectIdentity();
        Compatibility.ClientObjectHooks.PrefetchPositions();
        Compatibility.ClientObjectHooks.PrefetchPlayerStats();
        try { Plugins.PluginManager.TrySeedLiveObjectsFromCObjectMaintOnce(); }
        catch (Exception ex) { RynthLog.Plugin($"CObjectMaint seed threw {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// The plugin lifecycle/tick work that used to run inline in
    /// <see cref="OnEndScene"/>. Driven by the dedicated managed plugin pump
    /// thread (EntryPoint.StartNormalPluginPump) — NOT AC's render thread —
    /// so a GC during the plugin tick can't fail-fast NativeAOT's reverse-
    /// P/Invoke transition on an AC-owned thread (the 2026-05-16 root cause).
    ///
    /// No-ops until the render path has resolved the game HWND and published
    /// a device (CachedDevice != 0). Single-threaded by contract: only the
    /// one pump thread calls this; the render path no longer touches
    /// PluginManager at all, so there is exactly one TickAll driver. The
    /// re-entrancy guard is belt-and-braces against an accidental second
    /// caller.
    /// </summary>
    public static void PumpPluginFrame()
    {
        IntPtr device = CachedDevice;
        if (device == IntPtr.Zero || _gameHwnd == IntPtr.Zero)
            return; // render path hasn't established core state yet

        if (System.Threading.Interlocked.Exchange(ref _pluginPumpInFrame, 1) != 0)
            return; // never re-enter PluginManager from two stacks
        try
        {
            // Plugins only get the ImGui context when they may draw with it
            // (EnableImGuiShell, FORCE-gated). The engine's own ImGui panels
            // don't need plugins to see it, and handing it over switched
            // RynthAi into its legacy-ImGui setup (first ImGui-on test,
            // 2026-09-28) - plugins must behave exactly as in Avalonia-only mode.
            IntPtr pluginContext = Plugins.EngineSettings.EnableImGuiShell ? _context : IntPtr.Zero;
            if (!_pluginsInitialized)
            {
                _pluginsInitialized = true;
                PluginManager.InitPlugins(pluginContext, device, _gameHwnd);
            }

            // Decal bridge mode with the in-game renderer (DecalInGameImGui): chat and
            // chat-bar lines come from the bridge. One flag check in every other client.
            if (Compatibility.DecalBridgeHost.Active)
                Compatibility.DecalBridgeHost.Drain();
            PluginManager.ProcessPendingActions(pluginContext, device, _gameHwnd);
            PluginManager.FlushPendingDeletes(); // closes the create→delete race window
            PluginManager.TickAll();
            // Panel data: plugin exports for the UI are called here, on the same
            // thread as the plugin tick, and published as immutable snapshots.
            UI.Data.UiDataHub.Step();
        }
        catch (Exception ex)
        {
            RynthLog.Plugin($"EngineFrameController.PumpPluginFrame: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _pluginPumpInFrame, 0);
        }
    }

    /// <summary>True when anything ImGui should be drawn this frame.</summary>
    private static bool HasImGuiContent()
    {
        if (Plugins.EngineSettings.EnableImGuiShell || ImGuiFontTest.Enabled) return true;
        if (!PanelsLive) return false;
        ImGuiPanelHost.RestoreOnce();
        return ImGuiPanelHost.NeedsFrame || ImGuiBar.Visible || ImGuiBar.PoppedOut || Hud.MonsterHud.WantsFrame;
    }

    /// <summary>
    /// Panel faces show only in the world, like the Avalonia overlay (hidden
    /// between characters, not closed).
    /// </summary>
    private static bool PanelsLive => Compatibility.LoginLifecycleHooks.HasObservedLoginComplete;

    /// <summary>DPI scale the fonts and style were built for (see Init).</summary>
    private static float _uiScale = 1f;

    /// <summary>The scale the fonts and style were built for.</summary>
    internal static float FontScale => _uiScale;

    /// <summary>A popped-out panel was clicked: the main context's text edit ends next frame. AC thread.</summary>
    internal static void DropMainTextFocus() => _dropImGuiTextFocus = true;

    // ── Diagnostics (/rc imgui diag) ─────────────────────────────────
    private static long _framesBuilt;
    private static System.Numerics.Vector2 _lastDisplaySize;

    /// <summary>One line per fact about the ImGui layer's state. Game thread (reads plain fields).</summary>
    internal static System.Collections.Generic.List<string> DescribeState()
    {
        var lines = new System.Collections.Generic.List<string>
        {
            $"layer={(Plugins.EngineSettings.EnableImGuiBackend ? "on" : "off")} initialized={_imguiInitialized} initFailed={_imguiInitFailed} selfTest={ImGuiSelfTest.LastResult}",
            $"inWorld={PanelsLive} content={HasImGuiContentNoRestore()} idle={_imguiIdle} framesBuilt={_framesBuilt} uiScale={_uiScale:0.##} avaloniaScale={UI.AvaloniaOverlay.InputScale:0.##} display={_lastDisplaySize.X:0}x{_lastDisplaySize.Y:0}",
            DX9Backend.DescribeLastFrame(),
            UnderUiLayer.Describe(),
            D3D9.Nav3DRenderInjector.Describe(),
            $"capture: {Win32Backend.DescribeCapture()}",
        };
        lines.AddRange(ImGuiPopOuts.Describe());
        return lines;
    }

    private static bool HasImGuiContentNoRestore() =>
        Plugins.EngineSettings.EnableImGuiShell || ImGuiFontTest.Enabled || (PanelsLive && (ImGuiPanelHost.NeedsFrame || ImGuiBar.Visible || ImGuiBar.PoppedOut || Hud.MonsterHud.WantsFrame));

    // Keyboard owner between the stacks (plan §3.2 d): whichever side started
    // text entry most recently keeps the keyboard.
    private static bool _prevImGuiText, _prevAvaloniaText, _dropImGuiTextFocus;

    /// <summary>True while frames are being skipped because nothing ImGui is open.</summary>
    private static bool _imguiIdle = true;

    /// <summary>
    /// Builds the ImGui frame (NewFrame ... Render) and publishes the capture
    /// flags. Returns true when there is draw data for <see cref="SubmitImGuiFrame"/>.
    /// Only called while EngineSettings.EnableImGuiBackend is true.
    ///
    /// Early-out: with no ImGui content there is no ImGui frame at all. Queued
    /// input is dropped (nothing would drain it) and capture flags are cleared,
    /// so the idle cost is one GetClientRect.
    /// </summary>
    private static bool BuildImGuiFrame(IntPtr pDevice)
    {
        if (_imguiInitFailed) return false;

        if (!_imguiInitialized)
        {
            // Initialised as soon as the backend is enabled (not lazily on first
            // content), so the cimgui self-test result is in the log at startup.
            if (!Init(pDevice))
            {
                _imguiInitFailed = true;
                return false;
            }
        }

        Win32Backend.UpdateClientMetrics(out int clientW, out int clientH);

        // Monster nameplates: pick the monsters (throttled) before deciding
        // whether there is a frame to build, so plates alone can ask for one.
        Hud.MonsterHud.Update(PanelsLive);

        if (!HasImGuiContent())
        {
            _imguiIdle = true;
            Win32Backend.DiscardQueuedInput();
            Win32Backend.ClearCaptureFlags();
            ImGuiTextFocus.ClearMain();
            return false;
        }

        IntPtr previousContext = ImGuiNET.ImGui.GetCurrentContext();
        ImGuiNET.ImGui.SetCurrentContext(_context);
        bool frameStarted = false;
        bool frameEnded = false;

        try
        {
            ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
            long now = Stopwatch.GetTimestamp();
            if (_imguiIdle)
            {
                // Resuming: keys/buttons released while idle must not read as held,
                // and the idle gap must not become one huge DeltaTime.
                _imguiIdle = false;
                Win32Backend.ResetImGuiInputState(io);
                _lastFrameTicks = now;
            }

            _frameCount++;
            // A panel's text size changed: re-bake the fonts (only the sizes in
            // use) and replace the font texture, between frames.
            // The new atlas is baked on a worker (ImGuiFonts.PumpRebuild), once per run of
            // changes (RequestRebuild debounces); here it is swapped in and its texture made.
            DX9Backend.ReleaseRetiredFontTexture();   // the texture a rebuild replaced last frame
            long rebuildStart = Stopwatch.GetTimestamp();
            if (ImGuiFonts.PumpRebuild(io, _uiScale, out string rebuilt))
            {
                DX9Backend.RebuildFontTexture(pDevice);
                double rebuildMs = (Stopwatch.GetTimestamp() - rebuildStart) * 1000.0 / Stopwatch.Frequency;
                RynthLog.UI($"ImGuiFonts: text size change: {rebuilt}; render thread {rebuildMs:0.0} ms (swap + texture).");
            }
            // Script window icons decoded since the last frame become textures, and the least
            // recently drawn go, before anything draws (no draw list points at them any more).
            ScriptIcons.BeginFrame(pDevice);
            RetailSprites.BeginFrame(pDevice);   // retail UI pictures decoded since the last frame (kept, never evicted)
            DX9Backend.NewFrame();
            Win32Backend.NewFrame(clientW, clientH);

            // Fallback: if Win32Backend could not determine display size, read from the D3D9 viewport.
            if (io.DisplaySize.X <= 1 || io.DisplaySize.Y <= 1)
            {
                DX9Backend.GetViewportSize(pDevice, out int vpW, out int vpH);
                if (vpW > 1 && vpH > 1)
                    io.DisplaySize = new System.Numerics.Vector2(vpW, vpH);
            }

            float dt = (float)(now - _lastFrameTicks) / Stopwatch.Frequency;
            _lastFrameTicks = now;
            io.DeltaTime = dt > 0f ? dt : 1f / 60f;

            ImGuiNET.ImGui.NewFrame();
            frameStarted = true;
            ImGuiTextFocus.BeginFrame();
            if (_dropImGuiTextFocus)
            {
                // An Avalonia TextBox took the keyboard: end ImGui's text edit.
                _dropImGuiTextFocus = false;
                ImGuiNET.ImGui.SetWindowFocus(null);
            }
            // EnableImGuiShell gates the old diagnostic shell and plugin-drawn
            // ImGui windows (plugin code on AC's thread: FORCE-gated, stays off).
            if (Plugins.EngineSettings.EnableImGuiShell)
            {
                RynthCoreShell.Render(_frameCount);
                PluginManager.RenderAll();
            }
            if (PanelsLive)
            {
                // World overlays: built into UnderUiLayer's list, drawn next frame at
                // AC's 3D->UI transition (under AC's UI and every panel), no input.
                Hud.MonsterHud.Draw(_uiScale);
                if (ImGuiBar.Visible)
                    ImGuiBar.Draw(_uiScale);
                ImGuiPanelHost.DrawAll(_uiScale);
                ImGuiBar.SyncPopOut(_uiScale);
            }
            if (ImGuiFontTest.Enabled)
                ImGuiFontTest.Draw(_uiScale);

            ImGuiNET.ImGui.EndFrame();
            frameEnded = true;
            ImGuiNET.ImGui.Render();
            ImGuiAssertLog.Poll();
            _framesBuilt++;
            _lastDisplaySize = io.DisplaySize;

            // io.WantCaptureMouse already applies ImGui's own button-ownership
            // rule; io.WantTextInput is true only while a text field is edited.
            // MonsterHud.WantsMouse: the cursor is on a clickable nameplate (or a
            // press that began on one is held), so that click is the plate's.
            // Keyboard: io.WantTextInput lags the box by a frame (ImGui publishes it from
            // the previous frame's widgets), so a box that went active this frame counts
            // too (ImGuiTextFocus): the keys typed right after the click stay out of AC.
            ImGuiTextFocus.EndMainFrame(io.WantTextInput);
            bool typing = io.WantTextInput || ImGuiTextFocus.MainBoxActive;
            Win32Backend.UpdateCaptureFlags(io.WantCaptureMouse || Hud.MonsterHud.WantsMouse, typing);
            // The UI is released (Insert): no text box keeps the keyboard. Keys are not
            // gated by the toggle any more (Win32Backend.UpdateCaptureFlags), so the edit
            // ends instead and the next keys go to the game.
            if (typing && !Win32Backend.UiCaptureEnabled)
                _dropImGuiTextFocus = true;
            ArbitrateTextFocus(io.WantTextInput);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                // Keep Dear ImGui's frame state balanced so the next frame does not assert.
                if (frameStarted && !frameEnded && ImGuiNET.ImGui.GetCurrentContext() == _context)
                    ImGuiNET.ImGui.EndFrame();
            }
            catch
            {
            }

            Win32Backend.ClearCaptureFlags();
            ImGuiTextFocus.ClearMain();
            RynthLog.Info($"EngineFrameController: frame {_frameCount} ImGui error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            ImGuiNET.ImGui.SetCurrentContext(previousContext);
        }
    }

    private static void ArbitrateTextFocus(bool imguiText)
    {
        bool avaloniaText = Win32Backend.AvaloniaTextInputActive;
        if (imguiText && !_prevImGuiText && avaloniaText)
        {
            // An ImGui field just started typing: Avalonia gives up the keyboard.
            Win32Backend.AvaloniaTextInputActive = false;
            UI.AvaloniaOverlay.ClearTextFocus();
            avaloniaText = false;
        }
        else if (avaloniaText && !_prevAvaloniaText && imguiText)
        {
            _dropImGuiTextFocus = true;
        }
        _prevImGuiText = imguiText;
        _prevAvaloniaText = avaloniaText;
    }

    /// <summary>Draws the frame <see cref="BuildImGuiFrame"/> produced.</summary>
    private static void SubmitImGuiFrame(IntPtr pDevice)
    {
        IntPtr previousContext = ImGuiNET.ImGui.GetCurrentContext();
        ImGuiNET.ImGui.SetCurrentContext(_context);
        try
        {
            DX9Backend.RenderDrawData(ImGuiNET.ImGui.GetDrawData(), pDevice);
        }
        catch (Exception ex)
        {
            RynthLog.Info($"EngineFrameController: frame {_frameCount} ImGui submit error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ImGuiNET.ImGui.SetCurrentContext(previousContext);
        }
    }

    internal static IntPtr FindGameWindow()
    {
        uint pid = GetCurrentProcessId();
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == pid && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
