// ============================================================================
//  RynthCore.Engine - UI/RynthAiDashboardState.cs
//  The RynthAi dashboard's minimized state, shared by its Avalonia and ImGui
//  faces, and the ImGui face's target bar and player vitals toggles,
//  persisted to %AppData%\RynthCore\rynthai_avalonia.cache (line 1
//  minimized|expanded, line 2 unused, line 3 target|notarget, line 4
//  vitals|novitals; line 5 filesopen|nofiles, the Loaded files drawer; line 9
//  ranges|noranges, the Ranges drawer; line 10 patrol|nopatrol, the Patrol
//  drawer; line 11 remote|noremote, the Mini Remote drawer: the left-edge
//  drawers, ImGui/Panels/DashboardDrawers.cs). Line 2
//  was the Monsters button's Simple/Advanced mode, retired 2026-10-01 with the
//  basic Monsters panel: it is still written ("advanced") so the lines after
//  it keep their places, and ignored on read. Lines 7 and 8 (the dashboard's
//  size with the files block folded / open) are unused since the files moved
//  to a drawer (2026-10-05); kept as read so the lines after them keep their
//  places.
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
    private static volatile bool _filesOpen;            // the Loaded files drawer (Profile/Nav/Loot/Meta pickers); a popped-out
                                                        // dashboard shows the same block inline instead
    private static volatile bool _barCollapsed;         // the RynthCore bar shows only its grip + expand
    private static volatile bool _rangesOpen;           // the Ranges drawer beside the dashboard is open
    private static volatile bool _patrolOpen;           // the Patrol drawer beside the dashboard is open
    private static volatile bool _remoteOpen;           // the Mini Remote drawer beside the dashboard is open
    // Lines 7 and 8, unused since 2026-10-05: written back as read.
    private static string _line7 = "-", _line8 = "-";
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

    /// <summary>
    /// The loaded-files block (Profile/Nav/Loot/Meta pickers, meta state) is open: the Loaded files drawer
    /// beside the dashboard, or the block inline on a popped-out dashboard. Any thread.
    /// </summary>
    public static bool FilesOpen
    {
        get { EnsureLoaded(); return _filesOpen; }
    }

    public static void SetFilesOpen(bool open)
    {
        EnsureLoaded();
        _filesOpen = open;
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

    /// <summary>The Ranges drawer (ImGui/Panels/RangesSlideOut.cs) is open beside the dashboard. Any thread.</summary>
    public static bool RangesOpen
    {
        get { EnsureLoaded(); return _rangesOpen; }
    }

    public static void SetRangesOpen(bool open)
    {
        EnsureLoaded();
        _rangesOpen = open;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    /// <summary>The Patrol drawer (routes and recorded hazards) is open beside the dashboard. Any thread.</summary>
    public static bool PatrolOpen
    {
        get { EnsureLoaded(); return _patrolOpen; }
    }

    public static void SetPatrolOpen(bool open)
    {
        EnsureLoaded();
        _patrolOpen = open;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

    /// <summary>The Mini Remote drawer (RynthAi's Mini Remote drawn by the plugin) is open beside the dashboard. Any thread.</summary>
    public static bool RemoteOpen
    {
        get { EnsureLoaded(); return _remoteOpen; }
    }

    public static void SetRemoteOpen(bool open)
    {
        EnsureLoaded();
        _remoteOpen = open;
        UiBackgroundWriter.Enqueue("rynthai dashboard state", Save);
    }

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
                    _filesOpen = lines.Length >= 5 && lines[4].Trim().Equals("filesopen", StringComparison.OrdinalIgnoreCase);
                    _barCollapsed = lines.Length >= 6 && lines[5].Trim().Equals("barcollapsed", StringComparison.OrdinalIgnoreCase);
                    if (lines.Length >= 7 && lines[6].Trim().Length > 0) _line7 = lines[6].Trim();
                    if (lines.Length >= 8 && lines[7].Trim().Length > 0) _line8 = lines[7].Trim();
                    _rangesOpen = lines.Length >= 9 && lines[8].Trim().Equals("ranges", StringComparison.OrdinalIgnoreCase);
                    _patrolOpen = lines.Length >= 10 && lines[9].Trim().Equals("patrol", StringComparison.OrdinalIgnoreCase);
                    _remoteOpen = lines.Length >= 11 && lines[10].Trim().Equals("remote", StringComparison.OrdinalIgnoreCase);
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
                + (_filesOpen ? "filesopen" : "nofiles") + "\n" + (_barCollapsed ? "barcollapsed" : "bar") + "\n"
                + _line7 + "\n" + _line8 + "\n"
                + (_rangesOpen ? "ranges" : "noranges") + "\n" + (_patrolOpen ? "patrol" : "nopatrol") + "\n"
                + (_remoteOpen ? "remote" : "noremote") + "\n");
        }
        catch { /* best-effort */ }
    }
}
