// ============================================================================
//  RynthCore.Engine - Graphics/AcGraphics.cs
//  The engine side of RynthAi's Settings > Graphics (AcGraphicsPrefs.cs has the
//  shared model): this account's graphics profile, the edits from the panel,
//  and the size AC really draws at (for the window code).
//
//  Per account (2026.10.18.3): graphics.json holds a profile per "server|account".
//  A client edits only its own account's profile, re-reading the file first so it
//  never overwrites another client's. The launcher writes the launching account's
//  profile into UserPreferences.ini just before that client starts.
//
//  Never fights AC live (2026.10.18.3): the window is sized for the resolution AC
//  is actually drawing (its back buffer, sampled from EndScene; else what it
//  launched with), never for a Graphics value AC isn't using yet. A Resolution
//  set here applies at the next launch. Clients don't write the shared INI on an
//  edit either (each would put its own account's values over the others'); only
//  the launcher, and "Write to AC's settings now", do.
//
//  Window mode is the exception that applies now: it is RynthCore's own window
//  chrome (AcWindowChrome), not an AC setting (except exclusive full screen, which
//  starts at the next launch).
//
//  Threads: reads are cheap snapshots from any thread; edits come from the
//  Settings panel (AC's main thread) and queue their file writes on
//  UiBackgroundWriter.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Plugins;
using RynthCore.Graphics;

namespace RynthCore.Engine.Graphics;

internal static class AcGraphics
{
    private static readonly object Sync = new();
    private static AcGraphicsProfiles _profiles = new();   // under Sync
    private static bool _loaded;
    private static DateTime _configStamp;
    private static long _nextReloadCheck;
    // UserPreferences.ini as AC launched with it (read once, at the first use).
    private static Dictionary<string, string>? _launchIni;
    private static string _launchContextKey = "";
    private static string _launchAccountName = "";
    // AC's back buffer, sampled from EndScene (0 = not seen yet).
    private static int _backBufferW, _backBufferH;

    // ── Reads ────────────────────────────────────────────────────────────────

    /// <summary>"server|account" of this client: the logged-in one, else the launch context's.</summary>
    public static string AccountKey
    {
        get
        {
            string key = AcGraphicsPrefs.AccountKey(SessionStateRegistry.LastServerName, SessionStateRegistry.LastAccountName);
            if (key.Length > 0) return key;
            EnsureLoaded();
            return _launchContextKey;
        }
    }

    /// <summary>This client's account name for the panel, empty before one is known.</summary>
    public static string AccountName
    {
        get
        {
            string name = SessionStateRegistry.LastAccountName;
            if (name.Length > 0) return name;
            EnsureLoaded();
            return _launchAccountName;
        }
    }

    /// <summary>A copy of this account's profile (empty when it has none).</summary>
    private static AcGraphicsConfig Profile()
    {
        string key = AccountKey;
        lock (Sync) { EnsureLoadedLocked(); return _profiles.For(key).Clone(); }
    }

    /// <summary>The size AC is drawing at: its back buffer once seen, else the resolution it launched
    /// with. The window is sized for this, so it never fights AC's own resolution.</summary>
    public static bool TryGetDrawSize(out int width, out int height)
    {
        width = Volatile.Read(ref _backBufferW);
        height = Volatile.Read(ref _backBufferH);
        if (width >= 640 && height >= 480) return true;
        return AcGraphicsPrefs.TryParseResolution(LaunchValue(AcGraphicsPrefs.ResolutionId), out width, out height);
    }

    /// <summary>EndScene, about once a second: remembers AC's back-buffer size.</summary>
    public static void SampleBackBuffer(IntPtr device)
    {
        if (!ImGuiBackend.DX9Backend.TryGetBackBufferSize(device, out int w, out int h)) return;
        int oldW = Volatile.Read(ref _backBufferW), oldH = Volatile.Read(ref _backBufferH);
        if (w == oldW && h == oldH) return;
        Volatile.Write(ref _backBufferW, w);
        Volatile.Write(ref _backBufferH, h);
        RynthLog.UI($"AcGraphics: AC draws at {w}x{h}" + (oldW > 0 ? $" (was {oldW}x{oldH})." : "."));
    }

    /// <summary>The window mode in effect (one of AcGraphicsPrefs.WindowModeIds): this account's, else
    /// the Display toggles'.</summary>
    public static string WindowMode => Profile().WindowMode ?? LegacyWindowMode();

    /// <summary>The account's own window mode, or null (the Display toggles decide).</summary>
    public static string? ProfileWindowMode => Profile().WindowMode;

    /// <summary>RynthCore should strip the window frame (either borderless mode).</summary>
    public static bool WantBorderless => WindowMode is AcGraphicsPrefs.ModeBorderlessFill or AcGraphicsPrefs.ModeBorderlessSized;

    /// <summary>Borderless fills the monitor (else it is AC's drawing size).</summary>
    public static bool FillMonitor => WindowMode != AcGraphicsPrefs.ModeBorderlessSized;

    /// <summary>The value this account enforces for <paramref name="id"/> ("Section.Key"), or null (AC's choice).</summary>
    public static string? Get(string id) => Profile().Values.TryGetValue(id, out string? v) ? v : null;

    /// <summary>What AC launched with for <paramref name="id"/>, or null.</summary>
    public static string? LaunchValue(string id)
    {
        EnsureLoaded();
        Dictionary<string, string>? ini = Volatile.Read(ref _launchIni);
        return ini != null && ini.TryGetValue(id, out string? v) ? v : null;
    }

    // ── Edits (Settings > Graphics), this account's profile only ─────────────

    /// <summary>Sets (or with null clears: AC's choice) a value in this account's profile.</summary>
    public static void Set(string id, string? value) =>
        Edit(cfg => { if (value == null) cfg.Values.Remove(id); else cfg.Values[id] = value; }, windowChanged: false);

    /// <summary>Sets this account's window mode (null clears it: the Display toggles decide).</summary>
    public static void SetWindowMode(string? mode) => Edit(cfg => cfg.WindowMode = mode, windowChanged: true);

    /// <summary>"Reset Window Borders": this account's window mode becomes Windowed if it was set. Saves only.</summary>
    public static void ResetWindowModeToWindowed()
    {
        if (Profile().WindowMode is null or AcGraphicsPrefs.ModeWindowed) return;
        Edit(cfg => cfg.WindowMode = AcGraphicsPrefs.ModeWindowed, windowChanged: false);
    }

    /// <summary>The Display toggles were used: this account's window mode is cleared so they decide again. Saves only.</summary>
    public static void DisplayTogglesChanged()
    {
        if (Profile().WindowMode == null) return;
        Edit(cfg => cfg.WindowMode = null, windowChanged: false);
    }

    /// <summary>Clears this account's whole profile (AC's own settings stay as they are).</summary>
    public static void ResetAll() => Edit(cfg => { cfg.Values.Clear(); cfg.WindowMode = null; }, windowChanged: true);

    /// <summary>"Write to AC's settings now": this account's profile into UserPreferences.ini.</summary>
    public static void WriteIniNow()
    {
        Dictionary<string, string> values = Profile().IniValues();
        UI.UiBackgroundWriter.Enqueue("graphics settings (write now)", () =>
        {
            try
            {
                if (values.Count == 0) { RynthLog.UI("AcGraphics: nothing to write (every setting is AC's choice)."); return; }
                foreach (string ini in AcGraphicsPrefs.IniPaths(Environment.CurrentDirectory))
                {
                    List<string> changed = AcPrefsIni.Apply(ini, values);
                    RynthLog.UI(changed.Count > 0
                        ? $"AcGraphics: wrote {string.Join(", ", changed)} to {ini}."
                        : $"AcGraphics: {ini} already matches this account's profile.");
                }
            }
            catch (Exception ex) { RynthLog.UI($"AcGraphics: writing UserPreferences.ini failed - {ex.Message}"); }
        });
    }

    /// <summary>Called by the window code every couple of seconds: picks up another client's edits.</summary>
    public static void PollReload()
    {
        long now = Environment.TickCount64;
        if (now < _nextReloadCheck) return;
        _nextReloadCheck = now + 3000;
        try
        {
            DateTime stamp = File.Exists(AcGraphicsPrefs.ConfigPath) ? File.GetLastWriteTimeUtc(AcGraphicsPrefs.ConfigPath) : default;
            lock (Sync)
            {
                if (!_loaded || stamp == _configStamp) return;
            }
            AcGraphicsProfiles all = AcGraphicsProfiles.Load(AcGraphicsPrefs.ConfigPath);
            lock (Sync) { _profiles = all; _configStamp = stamp; }
        }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: re-read failed - {ex.Message}"); }
    }

    /// <summary>One line for the top of Settings > Graphics.</summary>
    public static string StatusLine()
    {
        string launched = LaunchValue(AcGraphicsPrefs.ResolutionId) ?? "?";
        bool launchedFull = string.Equals(LaunchValue(AcGraphicsPrefs.FullScreenId), "True", StringComparison.OrdinalIgnoreCase);
        string draws = TryGetDrawSize(out int w, out int h) ? w + "x" + h : "?";
        int modeIdx = Array.IndexOf(AcGraphicsPrefs.WindowModeIds, WindowMode);
        string mode = modeIdx >= 0 ? AcGraphicsPrefs.WindowModeLabels[modeIdx] : WindowMode;
        string who = AccountName.Length > 0 ? $"Profile for account '{AccountName}'. " : "No account known yet. ";
        return $"{who}AC started at {launched}{(launchedFull ? " full screen" : "")} and draws at {draws}; window: {mode}.";
    }

    // ── Display modes for the panel ──────────────────────────────────────────

    /// <summary>The primary monitor's resolutions (24/32-bit), largest first, plus the launched one.</summary>
    public static string[] AvailableResolutions()
    {
        var set = new SortedSet<(int W, int H)>(Comparer<(int W, int H)>.Create((a, b) => b.W != a.W ? b.W.CompareTo(a.W) : b.H.CompareTo(a.H)));
        foreach (DEVMODE dm in DisplayModes())
            if (dm.dmBitsPerPel >= 24 && dm.dmPelsWidth >= 800 && dm.dmPelsHeight >= 600)
                set.Add((dm.dmPelsWidth, dm.dmPelsHeight));
        if (AcGraphicsPrefs.TryParseResolution(LaunchValue(AcGraphicsPrefs.ResolutionId), out int lw, out int lh)) set.Add((lw, lh));
        var list = new List<string>();
        foreach ((int w, int h) in set) list.Add(w + "x" + h);
        return list.ToArray();
    }

    /// <summary>The primary monitor's refresh rates (Hz), highest first.</summary>
    public static string[] AvailableRefreshRates()
    {
        var set = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        foreach (DEVMODE dm in DisplayModes())
            if (dm.dmDisplayFrequency > 1) set.Add(dm.dmDisplayFrequency);
        var list = new List<string>();
        foreach (int hz in set) list.Add(hz.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    // ── Memory safeguard (2026.10.18.11) ─────────────────────────────────────

    // A client this short of address space this soon after it started is being filled by AC's own
    // loading (seen: 450 MB -> 3.1 GB in 13 s after login with Adaptive degrade off), not a slow leak.
    private const long LowFreeMb = 200, LowLargestFreeMb = 128;
    private const double LowMemoryWindowMinutes = 10;
    private static int _lowMemoryHandled;
    private static string _lowMemoryNote = string.Empty;
    private static readonly DateTime ProcessStartLocal = ReadProcessStart();

    private static DateTime ReadProcessStart()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime; }
        catch { return DateTime.Now; }
    }

    /// <summary>What AC uses for <paramref name="id"/>: this account's value, else what it launched with.</summary>
    private static string? Effective(string id) => Get(id) ?? LaunchValue(id);

    /// <summary>The Graphics panel's warning: Adaptive degrade off with a far draw distance (empty when fine),
    /// plus what the low-memory fallback did this session.</summary>
    public static string MemoryWarning()
    {
        string risk = AcGraphicsPrefs.MemoryRisk(Effective(AcGraphicsPrefs.AutomaticDegradesId),
            Effective(AcGraphicsPrefs.SceneryDrawDistanceId), Effective(AcGraphicsPrefs.LandscapeDrawDistanceId));
        string note = Volatile.Read(ref _lowMemoryNote);
        return note.Length == 0 ? risk : risk.Length == 0 ? note : note + "\n" + risk;
    }

    /// <summary>
    /// Heartbeat (once a second, its own thread): when the address space is nearly gone within
    /// <see cref="LowMemoryWindowMinutes"/> of the client starting and AC runs with Adaptive degrade
    /// off, this account's profile turns it back on, saved at once (the client may be about to
    /// die), so the launcher writes it into UserPreferences.ini at the next launch. Once a session.
    /// </summary>
    public static void CheckLowMemory(long freeMb, long largestFreeMb)
    {
        bool low = (freeMb >= 0 && freeMb < LowFreeMb) || (largestFreeMb >= 0 && largestFreeMb < LowLargestFreeMb);
        if (!low || Volatile.Read(ref _lowMemoryHandled) != 0) return;
        if ((DateTime.Now - ProcessStartLocal).TotalMinutes > LowMemoryWindowMinutes) return;
        if (Interlocked.Exchange(ref _lowMemoryHandled, 1) != 0) return;

        string? degrade = Effective(AcGraphicsPrefs.AutomaticDegradesId);
        string where = $"free address space {freeMb} MB, largest block {largestFreeMb} MB, "
                     + $"{(DateTime.Now - ProcessStartLocal).TotalSeconds:0} s after the client started";
        if (!string.Equals(degrade, "False", StringComparison.OrdinalIgnoreCase))
        {
            RynthLog.UI($"AcGraphics: low memory ({where}); Adaptive degrade is already {(degrade ?? "AC's default")}, nothing changed.");
            return;
        }
        string key = AccountKey;
        if (key.Length == 0)
        {
            RynthLog.UI($"AcGraphics: low memory ({where}) with Adaptive degrade off, but no account is known; nothing changed.");
            return;
        }
        try
        {
            AcGraphicsConfig mine;
            lock (Sync)
            {
                EnsureLoadedLocked();
                mine = _profiles.For(key).Clone();
                mine.Values[AcGraphicsPrefs.AutomaticDegradesId] = "True";
                _profiles.Put(key, mine);
            }
            SaveAccountProfile(key, mine);
            string scenery = Effective(AcGraphicsPrefs.SceneryDrawDistanceId) ?? "AC's default";
            Volatile.Write(ref _lowMemoryNote,
                $"This client ran low on memory {(DateTime.Now - ProcessStartLocal).TotalSeconds:0} s after it started, with Adaptive degrade off "
                + $"(scenery distance {scenery}): Adaptive degrade is now on for this account from the next launch.");
            RynthLog.UI($"AcGraphics: LOW MEMORY ({where}) with Adaptive degrade off: turned it on in {key}'s profile; "
                + $"the launcher writes it into UserPreferences.ini at the next launch (scenery distance {scenery}).");
        }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: low-memory fallback failed - {ex.Message}"); }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>Re-reads graphics.json and replaces only <paramref name="key"/>'s entry. Any thread.</summary>
    private static void SaveAccountProfile(string key, AcGraphicsConfig mine)
    {
        string path = AcGraphicsPrefs.ConfigPath;
        AcGraphicsProfiles onDisk = AcGraphicsProfiles.Load(path);
        onDisk.Put(key, mine);
        onDisk.Save(path);
        lock (Sync)
        {
            _profiles = onDisk;
            _configStamp = File.GetLastWriteTimeUtc(path);
        }
    }

    private static string LegacyWindowMode() =>
        !EngineSettings.BorderlessWindowed ? AcGraphicsPrefs.ModeWindowed
        : EngineSettings.BorderlessFillMonitor ? AcGraphicsPrefs.ModeBorderlessFill
        : AcGraphicsPrefs.ModeBorderlessSized;

    /// <summary>Changes this account's profile in memory and queues the save (re-read, replace this
    /// account's entry, write), so another client's account is never overwritten.</summary>
    private static void Edit(Action<AcGraphicsConfig> change, bool windowChanged)
    {
        string key = AccountKey;
        if (key.Length == 0)
        {
            RynthLog.UI("AcGraphics: no account known yet; graphics change not saved.");
            return;
        }
        AcGraphicsConfig mine;
        lock (Sync)
        {
            EnsureLoadedLocked();
            mine = _profiles.For(key).Clone();
            change(mine);
            _profiles.Put(key, mine);
        }
        QueueSave();
        if (!windowChanged) return;
        try { UI.AcWindowChrome.ApplyGraphicsChanged(UI.AcWindowChrome.ResolveGameHwndForSettings()); }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: window apply failed - {ex.Message}"); }
    }

    // One queued save at a time: a slider drag edits every frame, and the job saves whatever is
    // current when it runs (an edit after it starts queues the next one).
    private static int _saveQueued;

    private static void QueueSave()
    {
        if (Interlocked.Exchange(ref _saveQueued, 1) == 1) return;
        UI.UiBackgroundWriter.Enqueue("graphics settings", () =>
        {
            Volatile.Write(ref _saveQueued, 0);
            string key = AccountKey;
            AcGraphicsConfig mine;
            lock (Sync) { mine = _profiles.For(key).Clone(); }
            // Only this account's entry changes: the others come from the file as it is now.
            try { SaveAccountProfile(key, mine); }
            catch (Exception ex) { RynthLog.UI($"AcGraphics: saving graphics.json failed - {ex.Message}"); }
        });
    }

    private static void EnsureLoaded()
    {
        lock (Sync) EnsureLoadedLocked();
    }

    private static void EnsureLoadedLocked()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            string path = AcGraphicsPrefs.ConfigPath;
            _profiles = AcGraphicsProfiles.Load(path);
            _configStamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: reading graphics.json failed - {ex.Message}"); }
        try
        {
            // The game folder's INI first (this install's clients read and write it).
            foreach (string ini in AcGraphicsPrefs.IniPaths(Environment.CurrentDirectory))
            {
                _launchIni = AcPrefsIni.Read(ini);
                if (_launchIni.Count > 0) break;
            }
        }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: reading UserPreferences.ini failed - {ex.Message}"); }
        try
        {
            (string account, string server, _) = CharacterCaptureHooks.ReadLaunchContext();
            _launchContextKey = AcGraphicsPrefs.AccountKey(server, account);
            _launchAccountName = account ?? "";
        }
        catch { /* no launch context: a client the launcher didn't start */ }
    }

    private static IEnumerable<DEVMODE> DisplayModes()
    {
        var list = new List<DEVMODE>();
        try
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            for (int i = 0; EnumDisplaySettings(null, i, ref dm) && i < 2000; i++)
            {
                list.Add(dm);
                dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            }
        }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: listing display modes failed - {ex.Message}"); }
        return list;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);
}
