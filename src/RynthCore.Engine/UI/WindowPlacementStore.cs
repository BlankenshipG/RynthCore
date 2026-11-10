// ============================================================================
//  RynthCore.Engine - UI/WindowPlacementStore.cs
//  Where the player left each plugin's own floating ImGui window (an Item HUD, a
//  settings window), per character (plugin API v94, Get/SetWindowPlacement).
//
//  Plugin windows saved their spot in imgui.ini, one file every running client
//  shares: each client loads it at start and later rewrites all of it, so a window
//  moved in one client came back where another client's next save had it
//  (2026-10-26). Engine panels never had the problem (PanelStateStore, one file per
//  character); this gives plugin windows the same, with the plugin's own look.
//
//  File: %LocalAppData%\RynthCore\window_state.<server>.<character>.txt, one line
//  per window, "<key>=<x>,<y>,<width>,<height>" (ImGui screen pixels; width and
//  height 0 for a window that sizes itself). The character is the one logged in,
//  so a client that switches characters reads the new one's file. Only that
//  character's client writes it: written to a temp file and swapped in.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RynthCore.Engine.UI;

internal static class WindowPlacementStore
{
    /// <summary>A key longer than this, or with '=' or a line break, is refused.</summary>
    public const int MaxKeyLength = 96;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, float[]> Windows = new(StringComparer.OrdinalIgnoreCase);
    private static string? _file;   // the file Windows was read from (null: nothing read yet)

    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RynthCore");

    /// <summary>The logged-in character's file name, or null when no character is known yet.</summary>
    internal static string? FileNameFor(string? server, string? character)
    {
        string name = PanelStatePath.FileNameFor(server, character);
        if (name == PanelStatePath.SharedFileName) return null;   // no character: nothing is saved
        return "window_state" + name.Substring("panel_state".Length);
    }

    public static bool ValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= MaxKeyLength
        && key.IndexOf('=') < 0 && key.IndexOf('\n') < 0 && key.IndexOf('\r') < 0;

    /// <summary>The placement saved for <paramref name="key"/> on this character. Any thread.</summary>
    public static bool TryGet(string key, out float x, out float y, out float width, out float height)
    {
        x = y = width = height = 0;
        if (!ValidKey(key)) return false;
        lock (Sync)
        {
            if (!Select()) return false;
            if (!Windows.TryGetValue(key.Trim(), out float[]? v)) return false;
            (x, y, width, height) = (v[0], v[1], v[2], v[3]);
            return true;
        }
    }

    /// <summary>Saves <paramref name="key"/>'s placement for this character. False with no character or a bad key. Any thread.</summary>
    public static bool Set(string key, float x, float y, float width, float height)
    {
        if (!ValidKey(key) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height))
            return false;
        lock (Sync)
        {
            if (!Select() || _file == null) return false;
            float[] v = { x, y, Math.Max(0, width), Math.Max(0, height) };
            string k = key.Trim();
            if (Windows.TryGetValue(k, out float[]? old) && old.AsSpan().SequenceEqual(v)) return true;
            Windows[k] = v;
            Save(_file);
            return true;
        }
    }

    /// <summary>Reads the logged-in character's file when the character changed. Under Sync.</summary>
    private static bool Select()
    {
        string? name = FileNameFor(Compatibility.SessionStateRegistry.LastServerName, Compatibility.SessionStateRegistry.LastCharacterName);
        if (name == null) return false;
        string path = Path.Combine(Folder, name);
        if (string.Equals(path, _file, StringComparison.OrdinalIgnoreCase)) return true;
        _file = path;
        Windows.Clear();
        try
        {
            if (!File.Exists(path)) return true;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.LastIndexOf('=');
                if (eq <= 0) continue;
                string[] parts = line.Substring(eq + 1).Split(',');
                if (parts.Length != 4) continue;
                var v = new float[4];
                bool ok = true;
                for (int i = 0; i < 4 && ok; i++)
                    ok = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) && float.IsFinite(v[i]);
                if (ok) Windows[line.Substring(0, eq).Trim()] = v;
            }
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"WindowPlacementStore: {name} unreadable ({ex.GetType().Name}); windows start where they open."); } catch { }
        }
        return true;
    }

    private static void Save(string path)
    {
        try
        {
            var sb = new StringBuilder("# RynthCore plugin window spots for this character - auto-generated, hand-edits OK.\n");
            foreach (var (key, v) in Windows)
            {
                sb.Append(key).Append('=');
                for (int i = 0; i < 4; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(v[i].ToString("0.#", CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            try { RynthLog.UI($"WindowPlacementStore: saving {Path.GetFileName(path)} failed ({ex.GetType().Name}: {ex.Message})."); } catch { }
        }
    }
}
