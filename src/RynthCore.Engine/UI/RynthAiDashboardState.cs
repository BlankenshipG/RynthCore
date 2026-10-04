// ============================================================================
//  RynthCore.Engine - UI/RynthAiDashboardState.cs
//  The RynthAi dashboard's minimized state, shared by its Avalonia and ImGui
//  faces, and the ImGui face's target bar and player vitals toggles,
//  persisted to %AppData%\RynthCore\rynthai_avalonia.cache (line 1
//  minimized|expanded, line 2 unused, line 3 target|notarget, line 4
//  vitals|novitals). Line 2 was the Monsters button's Simple/Advanced mode,
//  retired 2026-10-01 with the basic Monsters panel: it is still written
//  ("advanced") so the lines after it keep their places, and ignored on read.
//
//  Deliberately NOT on RynthAiPanel: the engine loads this before
//  AvaloniaOverlay.Start(), and touching RynthAiPanel there runs its static
//  constructor, whose brushes create Dispatcher.UIThread on the wrong thread
//  and kill the whole overlay (PlatformNotSupportedException in MainLoop; see
//  the invariant in EntryPoint next to ChatModel.EnsureSettingsLoaded).
//  Keep Avalonia types out of this class.
// ============================================================================

using System;
using System.IO;

namespace RynthCore.Engine.UI;

internal static class RynthAiDashboardState
{
    private static volatile bool _minimized;
    private static volatile bool _showTargetBar = true;
    private static volatile bool _showVitals = true;
    private static volatile bool _showFiles;            // the Profile/Nav/Loot/Meta block (folded by default)
    private static volatile bool _barCollapsed;         // the RynthCore bar shows only its grip + expand
    // The dashboard's size (logical units) with the files folded and open, so flipping
    // between them brings back each one's own size. Zero = not set yet (fit to content).
    private static float _foldedW, _foldedH, _openW, _openH;
    private static bool _loaded;
    private static readonly object LoadSync = new();

    private static string CachePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "rynthai_avalonia.cache");

    /// <summary>Minimized (loaded from disk on first use). Any thread.</summary>
    public static bool Minimized
    {
        get { EnsureLoaded(); return _minimized; }
    }

    /// <summary>Any thread; the file is written in the background.</summary>
    public static void SetMinimized(bool minimized)
    {
        EnsureLoaded();
        _minimized = minimized;
        UiBackgroundWriter.Enqueue("rynthai minimized", Save);
    }

    /// <summary>The ImGui dashboard shows the target's name and health bar (at its bottom). Any thread.</summary>
    public static bool ShowTargetBar
    {
        get { EnsureLoaded(); return _showTargetBar; }
    }

    public static void SetShowTargetBar(bool show)
    {
        EnsureLoaded();
        _showTargetBar = show;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    /// <summary>The ImGui dashboard shows the player's health, stamina and mana bars (at its bottom). Any thread.</summary>
    public static bool ShowVitals
    {
        get { EnsureLoaded(); return _showVitals; }
    }

    public static void SetShowVitals(bool show)
    {
        EnsureLoaded();
        _showVitals = show;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    /// <summary>The dashboard shows the loaded-files block (Profile/Nav/Loot/Meta pickers, meta state). Any thread.</summary>
    public static bool ShowFiles
    {
        get { EnsureLoaded(); return _showFiles; }
    }

    public static void SetShowFiles(bool show)
    {
        EnsureLoaded();
        _showFiles = show;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    /// <summary>The ImGui bar is collapsed to its grip and an expand button. Any thread.</summary>
    public static bool BarCollapsed
    {
        get { EnsureLoaded(); return _barCollapsed; }
    }

    public static void SetBarCollapsed(bool collapsed)
    {
        EnsureLoaded();
        _barCollapsed = collapsed;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    /// <summary>The size the dashboard had last time with the files folded (open). False if never set.</summary>
    public static bool TryGetFilesModeSize(bool open, out float width, out float height)
    {
        EnsureLoaded();
        width = open ? _openW : _foldedW;
        height = open ? _openH : _foldedH;
        return width > 0 && height > 0;
    }

    public static void SetFilesModeSize(bool open, float width, float height)
    {
        EnsureLoaded();
        if (width <= 0 || height <= 0) return;
        if (open) { _openW = width; _openH = height; } else { _foldedW = width; _foldedH = height; }
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    private static void ParseSize(string line, out float w, out float h)
    {
        w = h = 0;
        string[] parts = line.Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pw)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ph))
        {
            w = pw; h = ph;
        }
    }

    private static string SizeText(float w, float h) =>
        w > 0 && h > 0 ? FormattableString.Invariant($"{w:0.#},{h:0.#}") : "-";

    /// <summary>Reads the file if it hasn't been read yet. Engine init calls this so AC's thread never does.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (LoadSync)
        {
            if (_loaded) return;
            try
            {
                string path = CachePath;
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    _minimized = lines.Length > 0 && lines[0].Trim().Equals("minimized", StringComparison.OrdinalIgnoreCase);
                    _showTargetBar = lines.Length < 3 || !lines[2].Trim().Equals("notarget", StringComparison.OrdinalIgnoreCase);
                    _showVitals = lines.Length < 4 || !lines[3].Trim().Equals("novitals", StringComparison.OrdinalIgnoreCase);
                    // "filesopen" only once the player opened them; the old "files" default reads as folded.
                    _showFiles = lines.Length >= 5 && lines[4].Trim().Equals("filesopen", StringComparison.OrdinalIgnoreCase);
                    _barCollapsed = lines.Length >= 6 && lines[5].Trim().Equals("barcollapsed", StringComparison.OrdinalIgnoreCase);
                    if (lines.Length >= 7) ParseSize(lines[6].Trim(), out _foldedW, out _foldedH);
                    if (lines.Length >= 8) ParseSize(lines[7].Trim(), out _openW, out _openH);
                }
            }
            catch { /* corrupt cache: start expanded */ }
            _loaded = true;
        }
    }

    private static void Save()
    {
        try
        {
            string path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (_minimized ? "minimized" : "expanded") + "\nadvanced\n"
                + (_showTargetBar ? "target" : "notarget") + "\n" + (_showVitals ? "vitals" : "novitals") + "\n"
                + (_showFiles ? "filesopen" : "nofiles") + "\n" + (_barCollapsed ? "barcollapsed" : "bar") + "\n"
                + SizeText(_foldedW, _foldedH) + "\n" + SizeText(_openW, _openH) + "\n");
        }
        catch { /* best-effort */ }
    }
}
