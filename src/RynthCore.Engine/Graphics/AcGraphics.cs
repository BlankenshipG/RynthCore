// ============================================================================
//  RynthCore.Engine - Graphics/AcGraphics.cs
//  The engine side of RynthAi's Settings > Graphics (AcGraphicsPrefs.cs has the
//  shared model): which resolution and window mode this client should have,
//  the edits from the panel, and writing them to graphics.json and AC's
//  UserPreferences.ini.
//
//  Effective values for this client: the character's override, else the
//  global one, else what AC launched with (UserPreferences.ini as read at
//  engine start) and, for the window mode, the older Display toggles
//  (EngineSettings.BorderlessWindowed / BorderlessFillMonitor).
//
//  When things apply:
//    - Window mode and resolution: the window now (AcWindowChrome); AC's picture
//      (its D3D back buffer) takes a new resolution at the next launch.
//    - Every other setting: the next launch. AC reads UserPreferences.ini only at
//      startup and rewrites it itself, so the launcher re-applies graphics.json
//      just before each client starts; edits here are also written to the INI
//      at once, for a client started some other way.
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
    private static AcGraphicsConfig _config = new();      // under Sync
    private static bool _loaded;
    private static DateTime _configStamp;
    private static long _nextReloadCheck;
    // UserPreferences.ini as AC launched with it (read once, at the first use).
    private static Dictionary<string, string>? _launchIni;
    private static string _launchContextKey = "";

    // ── Reads ────────────────────────────────────────────────────────────────

    /// <summary>"server|character" of this client: the logged-in one, else the launch context's.</summary>
    public static string CharKey
    {
        get
        {
            string key = AcGraphicsPrefs.CharacterKey(SessionStateRegistry.LastServerName, SessionStateRegistry.LastCharacterName);
            if (key.Length > 0) return key;
            EnsureLoaded();
            return _launchContextKey;
        }
    }

    /// <summary>Display name of this client's character, empty before one is known.</summary>
    public static string CharacterName => SessionStateRegistry.LastCharacterName;

    /// <summary>The resolution this client should run at (width, height), or false when nothing says.</summary>
    public static bool TryGetResolution(out int width, out int height)
    {
        string? res;
        lock (Sync) { EnsureLoadedLocked(); res = _config.EffectiveResolution(CharKey); }
        res ??= LaunchValue(AcGraphicsPrefs.ResolutionId);
        return AcGraphicsPrefs.TryParseResolution(res, out width, out height);
    }

    /// <summary>The window mode in effect (one of AcGraphicsPrefs.WindowModeIds).</summary>
    public static string WindowMode
    {
        get
        {
            string? mode;
            lock (Sync) { EnsureLoadedLocked(); mode = _config.EffectiveWindowMode(CharKey); }
            return mode ?? LegacyWindowMode();
        }
    }

    /// <summary>RynthCore should strip the window frame (either borderless mode).</summary>
    public static bool WantBorderless => WindowMode is AcGraphicsPrefs.ModeBorderlessFill or AcGraphicsPrefs.ModeBorderlessSized;

    /// <summary>Borderless fills the monitor (else it is the resolution's size).</summary>
    public static bool FillMonitor => WindowMode != AcGraphicsPrefs.ModeBorderlessSized;

    /// <summary>The value RynthAi enforces for <paramref name="id"/> ("Section.Key"), or null (AC's choice).</summary>
    public static string? Get(string id)
    {
        lock (Sync) { EnsureLoadedLocked(); return _config.Values.TryGetValue(id, out string? v) ? v : null; }
    }

    /// <summary>The global window mode set in RynthAi, or null (the Display toggles decide).</summary>
    public static string? GlobalWindowMode
    {
        get { lock (Sync) { EnsureLoadedLocked(); return _config.WindowMode; } }
    }

    /// <summary>This character's own resolution / window mode, or null (the global one).</summary>
    public static AcGraphicsOverride? CharacterOverride
    {
        get
        {
            string key = CharKey;
            if (key.Length == 0) return null;
            lock (Sync) { EnsureLoadedLocked(); return _config.Characters.TryGetValue(key, out AcGraphicsOverride? o) ? o : null; }
        }
    }

    /// <summary>What AC launched with for <paramref name="id"/>, or null.</summary>
    public static string? LaunchValue(string id)
    {
        EnsureLoaded();
        Dictionary<string, string>? ini = Volatile.Read(ref _launchIni);
        return ini != null && ini.TryGetValue(id, out string? v) ? v : null;
    }

    // ── Edits (Settings > Graphics) ──────────────────────────────────────────

    /// <summary>Sets (or with null clears: AC's choice) a value, saves, writes the INI.</summary>
    public static void Set(string id, string? value)
    {
        lock (Sync)
        {
            EnsureLoadedLocked();
            if (value == null) _config.Values.Remove(id);
            else _config.Values[id] = value;
        }
        Changed(windowChanged: id == AcGraphicsPrefs.ResolutionId);
    }

    /// <summary>Sets the global window mode (null clears it). Also mirrors it onto the Display toggles.</summary>
    public static void SetGlobalWindowMode(string? mode)
    {
        lock (Sync) { EnsureLoadedLocked(); _config.WindowMode = mode; }
        if (mode != null)
        {
            EngineSettings.BorderlessWindowed = mode is AcGraphicsPrefs.ModeBorderlessFill or AcGraphicsPrefs.ModeBorderlessSized;
            if (EngineSettings.BorderlessWindowed) EngineSettings.BorderlessFillMonitor = mode == AcGraphicsPrefs.ModeBorderlessFill;
        }
        Changed(windowChanged: true);
    }

    /// <summary>Sets this character's own resolution and/or window mode (null = the global one).</summary>
    public static void SetCharacterOverride(string? resolution, string? windowMode, bool setResolution, bool setMode)
    {
        string key = CharKey;
        if (key.Length == 0) return;
        lock (Sync)
        {
            EnsureLoadedLocked();
            if (!_config.Characters.TryGetValue(key, out AcGraphicsOverride? o)) _config.Characters[key] = o = new AcGraphicsOverride();
            if (setResolution) o.Resolution = resolution;
            if (setMode) o.WindowMode = windowMode;
            if (o.IsEmpty) _config.Characters.Remove(key);
        }
        Changed(windowChanged: true);
    }

    /// <summary>"Reset Window Borders": the global mode becomes Windowed and this character's own mode is
    /// dropped, so neither puts borderless back. Saves only (the caller places the window).</summary>
    public static void ResetWindowModeToWindowed()
    {
        bool changed = false;
        string key = CharKey;
        lock (Sync)
        {
            EnsureLoadedLocked();
            if (_config.WindowMode != null && _config.WindowMode != AcGraphicsPrefs.ModeWindowed)
            {
                _config.WindowMode = AcGraphicsPrefs.ModeWindowed;
                changed = true;
            }
            if (key.Length > 0 && _config.Characters.TryGetValue(key, out AcGraphicsOverride? o) && o.WindowMode != null)
            {
                o.WindowMode = null;
                if (o.IsEmpty) _config.Characters.Remove(key);
                changed = true;
            }
        }
        if (changed) QueueWrite(saveConfig: true);
    }

    /// <summary>The Display toggles (Borderless Windowed / Fills Monitor, /rc borderless) were used: the
    /// global Graphics window mode is cleared so they decide again. Saves only.</summary>
    public static void DisplayTogglesChanged()
    {
        lock (Sync)
        {
            EnsureLoadedLocked();
            if (_config.WindowMode == null) return;
            _config.WindowMode = null;
        }
        QueueWrite(saveConfig: true);
    }

    /// <summary>Clears every RynthAi graphics value (AC's own settings stay as they are).</summary>
    public static void ResetAll()
    {
        lock (Sync) { EnsureLoadedLocked(); _config = new AcGraphicsConfig(); }
        Changed(windowChanged: true);
    }

    /// <summary>Writes the INI now (Settings > Graphics "Write to AC's settings now").</summary>
    public static void WriteIniNow() => QueueWrite(saveConfig: false);

    /// <summary>Called by the window code once a second or so: picks up another client's edits.</summary>
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
            AcGraphicsConfig cfg = AcGraphicsConfig.Load(AcGraphicsPrefs.ConfigPath);
            lock (Sync) { _config = cfg; _configStamp = stamp; }
            RynthLog.UI("AcGraphics: graphics.json changed (another client); re-read.");
        }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: re-read failed - {ex.Message}"); }
    }

    /// <summary>One line for the top of Settings > Graphics: what AC started with and what applies now.</summary>
    public static string StatusLine()
    {
        string launched = LaunchValue(AcGraphicsPrefs.ResolutionId) ?? "?";
        bool launchedFull = string.Equals(LaunchValue(AcGraphicsPrefs.FullScreenId), "True", StringComparison.OrdinalIgnoreCase);
        string res = TryGetResolution(out int w, out int h) ? w + "x" + h : "?";
        int modeIdx = Array.IndexOf(AcGraphicsPrefs.WindowModeIds, WindowMode);
        string mode = modeIdx >= 0 ? AcGraphicsPrefs.WindowModeLabels[modeIdx] : WindowMode;
        string who = CharacterName.Length > 0 ? " for " + CharacterName : "";
        return $"AC started at {launched}{(launchedFull ? " full screen" : "")}. Now{who}: {res}, {mode}.";
    }

    // ── Display modes for the panel ──────────────────────────────────────────

    /// <summary>The primary monitor's resolutions (32-bit), largest first, plus the launched one.</summary>
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

    // ── Internals ────────────────────────────────────────────────────────────

    private static string LegacyWindowMode() =>
        !EngineSettings.BorderlessWindowed ? AcGraphicsPrefs.ModeWindowed
        : EngineSettings.BorderlessFillMonitor ? AcGraphicsPrefs.ModeBorderlessFill
        : AcGraphicsPrefs.ModeBorderlessSized;

    private static void Changed(bool windowChanged)
    {
        QueueWrite(saveConfig: true);
        if (!windowChanged) return;
        try { UI.AcWindowChrome.ApplyGraphicsChanged(UI.AcWindowChrome.ResolveGameHwndForSettings()); }
        catch (Exception ex) { RynthLog.UI($"AcGraphics: window apply failed - {ex.Message}"); }
    }

    // One queued write at a time: a slider drag edits every frame, and the job writes whatever is
    // current when it runs (an edit after it starts queues the next one).
    private static int _writeQueued;
    private static int _configDirty;

    private static void QueueWrite(bool saveConfig)
    {
        if (saveConfig) Volatile.Write(ref _configDirty, 1);
        if (Interlocked.Exchange(ref _writeQueued, 1) == 1) return;
        UI.UiBackgroundWriter.Enqueue("graphics settings", () =>
        {
            Volatile.Write(ref _writeQueued, 0);
            bool save = Interlocked.Exchange(ref _configDirty, 0) == 1;
            string charKey = CharKey;
            AcGraphicsConfig snapshot;
            lock (Sync) { EnsureLoadedLocked(); snapshot = Clone(_config); }
            try
            {
                if (save)
                {
                    snapshot.Save(AcGraphicsPrefs.ConfigPath);
                    lock (Sync) _configStamp = File.GetLastWriteTimeUtc(AcGraphicsPrefs.ConfigPath);
                }
                Dictionary<string, string> values = snapshot.IniValuesFor(charKey);
                if (values.Count == 0) return;
                foreach (string ini in AcGraphicsPrefs.IniPaths(Environment.CurrentDirectory))
                {
                    List<string> changed = AcPrefsIni.Apply(ini, values);
                    if (changed.Count > 0)
                        RynthLog.UI($"AcGraphics: wrote {string.Join(", ", changed)} to {ini}.");
                }
            }
            catch (Exception ex) { RynthLog.UI($"AcGraphics: saving graphics settings failed - {ex.Message}"); }
        });
    }

    private static AcGraphicsConfig Clone(AcGraphicsConfig c)
    {
        var copy = new AcGraphicsConfig { WindowMode = c.WindowMode };
        foreach (KeyValuePair<string, string> kv in c.Values) copy.Values[kv.Key] = kv.Value;
        foreach (KeyValuePair<string, AcGraphicsOverride> kv in c.Characters)
            copy.Characters[kv.Key] = new AcGraphicsOverride { Resolution = kv.Value.Resolution, WindowMode = kv.Value.WindowMode };
        return copy;
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
            _config = AcGraphicsConfig.Load(path);
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
            (_, string server, string character) = CharacterCaptureHooks.ReadLaunchContext();
            _launchContextKey = AcGraphicsPrefs.CharacterKey(server, character);
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
