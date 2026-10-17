// ============================================================================
//  RynthCore.Engine - Graphics/AcGraphicsPrefs.cs
//  AC's graphics settings (the retail Options > Graphics and Rendering pages),
//  kept by RynthAi in %APPDATA%\RynthCore\graphics.json and enforced on AC's
//  UserPreferences.ini every launch, so a value set in RynthAi wins over
//  whatever AC (or its own options page) last wrote there.
//
//  Shared source: compiled into the engine AND the launcher
//  (RynthCore.App.Avalonia links this file), so it depends on nothing else.
//
//  Model: one profile per account ("server|account", lower case; 2026.10.18.3 -
//  a single shared set made every client fight over one resolution). A profile:
//    - Values: "Section.Key" -> the value as AC writes it ("Medium", "True",
//      "0.89", "1760x990"). A key not in Values is "AC's choice": never touched.
//    - WindowMode: Windowed / BorderlessFill / BorderlessSized / FullScreen
//      (null = not set). FullScreen writes Display.FullScreen=True, every other
//      mode False; the borderless modes are RynthCore's window chrome.
//  The launcher writes the launching account's profile into UserPreferences.ini
//  just before that client starts. The 2026.10.18.1 single global set is ignored.
//
//  The value lists come from acclient.exe's own strings (Render.* keys and the
//  ID_Graphics_Value_* / TextureFiltering_* labels): VeryLow..Extreme, Bilinear /
//  Trilinear / Sharp / Anisotropic, Normal / Wide, RefreshRate Auto or Hz.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RynthCore.Graphics;

/// <summary>How a graphics setting is edited.</summary>
public enum AcGraphicsKind { Choice, Bool, Float }

/// <summary>One AC graphics setting: its UserPreferences.ini place, its label, and its values.</summary>
public sealed record AcGraphicsSetting(
    string Section,
    string Key,
    string Label,
    AcGraphicsKind Kind,
    string[]? Choices = null,
    double Min = 0,
    double Max = 1,
    double Step = 0.01,
    string Help = "")
{
    /// <summary>"Section.Key", the id used in graphics.json.</summary>
    public string Id => Section + "." + Key;
}

/// <summary>The settings, window modes and the INI file AC reads.</summary>
public static class AcGraphicsPrefs
{
    public const string ResolutionId = "Display.Resolution";
    public const string FullScreenId = "Display.FullScreen";
    public const string RefreshRateId = "Display.RefreshRate";
    public const string SyncToRefreshId = "Display.SyncToRefresh";

    // Window modes: ids stored in graphics.json, labels shown in the panel (same order).
    public const string ModeWindowed = "Windowed";
    public const string ModeBorderlessFill = "BorderlessFill";
    public const string ModeBorderlessSized = "BorderlessSized";
    public const string ModeFullScreen = "FullScreen";
    public static readonly string[] WindowModeIds = { ModeWindowed, ModeBorderlessFill, ModeBorderlessSized, ModeFullScreen };
    public static readonly string[] WindowModeLabels =
        { "Windowed", "Borderless (fill the monitor)", "Borderless (resolution size)", "Full screen (exclusive)" };

    private static readonly string[] DetailLevels = { "VeryLow", "Low", "Medium", "High", "VeryHigh" };
    private static readonly string[] DistanceLevels = { "VeryLow", "Low", "Medium", "High", "VeryHigh", "Extreme" };

    /// <summary>The Render settings (retail Graphics page), in the retail page's order.</summary>
    public static readonly AcGraphicsSetting[] RenderSettings =
    {
        new("Render", "LandscapeDrawDistance", "Landscape draw distance", AcGraphicsKind.Choice, DistanceLevels,
            Help: "How far away the land is drawn."),
        new("Render", "SceneryDrawDistance", "Scenery draw distance", AcGraphicsKind.Choice, DistanceLevels,
            Help: "How far away trees, rocks and other scenery are drawn."),
        new("Render", "LandscapeTextureDetail", "Landscape texture detail", AcGraphicsKind.Choice, DetailLevels,
            Help: "Detail of the ground textures."),
        new("Render", "EnvironmentTextureDetail", "Environment texture detail", AcGraphicsKind.Choice, DetailLevels,
            Help: "Detail of building, dungeon and object textures."),
        new("Render", "TextureFiltering", "Texture filtering", AcGraphicsKind.Choice,
            new[] { "Bilinear", "Trilinear", "Sharp", "Anisotropic" },
            Help: "How textures are smoothed: Bilinear is the cheapest, Anisotropic the sharpest at an angle."),
        new("Render", "LandscapeDetailTextures", "Landscape detail textures", AcGraphicsKind.Bool,
            Help: "Extra close-up detail on the ground."),
        new("Render", "BuildingDetailTextures", "Building detail textures", AcGraphicsKind.Bool,
            Help: "Extra close-up detail on buildings."),
        new("Render", "MultiPassAlpha", "Multi-pass alpha", AcGraphicsKind.Bool,
            Help: "Better see-through surfaces (foliage, water) at some cost."),
        new("Render", "FieldOfView", "Field of view", AcGraphicsKind.Float, Min: 60, Max: 160, Step: 1,
            Help: "The camera's field of view."),
        new("Render", "ScreenBrightness", "Screen brightness", AcGraphicsKind.Float, Min: -1, Max: 1, Step: 0.05,
            Help: "Gamma: 0 is AC's default."),
        new("Render", "AspectRatio", "Aspect ratio", AcGraphicsKind.Choice, new[] { "Normal", "Wide" },
            Help: "Wide for a widescreen resolution, so the picture isn't stretched."),
        new("Render", "AutomaticDegrades", "Adaptive degrade", AcGraphicsKind.Bool,
            Help: "Let AC lower detail on its own to keep the frame rate up."),
        new("Render", "GraphicsPerformance", "Adaptive degrade bias", AcGraphicsKind.Float, Min: -1, Max: 1, Step: 0.05,
            Help: "With adaptive degrade on: lower favours speed, higher favours looks."),
        new("Render", "DegradeDistance", "Degrade distance", AcGraphicsKind.Float, Min: 0, Max: 1, Step: 0.01,
            Help: "How far away objects start being drawn with less detail (higher = further)."),
    };

    /// <summary>Display.SyncToRefresh (retail "Sync to display refresh").</summary>
    public static readonly AcGraphicsSetting SyncToRefresh =
        new("Display", "SyncToRefresh", "Sync to display refresh", AcGraphicsKind.Bool,
            Help: "VSync: no tearing, the frame rate capped at the monitor's refresh.");

    /// <summary>Where the config is kept (all clients on this PC).</summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "graphics.json");

    /// <summary>The UserPreferences.ini files AC may read: the game folder's (this install's clients
    /// write it) and My Documents\Asheron's Call's. Only existing ones; the game folder's when neither is.</summary>
    public static List<string> IniPaths(string? gameDirectory)
    {
        var list = new List<string>();
        string? gameIni = string.IsNullOrWhiteSpace(gameDirectory) ? null : Path.Combine(gameDirectory, "UserPreferences.ini");
        string docsIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Asheron's Call", "UserPreferences.ini");
        if (gameIni != null && File.Exists(gameIni)) list.Add(gameIni);
        if (File.Exists(docsIni)) list.Add(docsIni);
        if (list.Count == 0 && gameIni != null) list.Add(gameIni);
        return list;
    }

    /// <summary>"server|account", trimmed and lower case; empty when either is missing.</summary>
    public static string AccountKey(string? server, string? account)
    {
        string s = (server ?? "").Trim(), a = (account ?? "").Trim();
        if (s.Length == 0 || a.Length == 0) return "";
        return (s + "|" + a).ToLowerInvariant();
    }

    /// <summary>"1760x990" -> (1760, 990); false for anything else.</summary>
    public static bool TryParseResolution(string? text, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Trim().ToLowerInvariant().Split('x');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
            && width >= 640 && height >= 480 && width <= 16384 && height <= 16384;
    }

    /// <summary>A float as AC writes it ("0.89").</summary>
    public static string FormatFloat(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>One account's graphics profile: what RynthAi enforces on AC's graphics settings.</summary>
public sealed class AcGraphicsConfig
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? WindowMode { get; set; }

    /// <summary>True when there is anything to enforce.</summary>
    public bool HasAny => Values.Count > 0 || WindowMode != null;

    /// <summary>Every "Section.Key" -> value to write (FullScreen from the window mode).</summary>
    public Dictionary<string, string> IniValues()
    {
        var map = new Dictionary<string, string>(Values, StringComparer.OrdinalIgnoreCase);
        if (WindowMode != null) map[AcGraphicsPrefs.FullScreenId] = WindowMode == AcGraphicsPrefs.ModeFullScreen ? "True" : "False";
        return map;
    }

    public AcGraphicsConfig Clone()
    {
        var copy = new AcGraphicsConfig { WindowMode = WindowMode };
        foreach (KeyValuePair<string, string> kv in Values) copy.Values[kv.Key] = kv.Value;
        return copy;
    }

    internal static AcGraphicsConfig Parse(JsonElement el)
    {
        var cfg = new AcGraphicsConfig();
        if (el.TryGetProperty("values", out JsonElement vals) && vals.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty p in vals.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v)
                    cfg.Values[p.Name] = v;
        if (el.TryGetProperty("windowMode", out JsonElement wm) && wm.ValueKind == JsonValueKind.String
            && Array.IndexOf(AcGraphicsPrefs.WindowModeIds, wm.GetString()) >= 0)
            cfg.WindowMode = wm.GetString();
        return cfg;
    }

    internal void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject("values");
        foreach (KeyValuePair<string, string> kv in Values) w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
        if (WindowMode != null) w.WriteString("windowMode", WindowMode);
    }
}

/// <summary>graphics.json: a graphics profile per account ("server|account").</summary>
public sealed class AcGraphicsProfiles
{
    public Dictionary<string, AcGraphicsConfig> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The account's profile, or an empty one (not added) when it has none.</summary>
    public AcGraphicsConfig For(string accountKey) =>
        accountKey.Length > 0 && Accounts.TryGetValue(accountKey, out AcGraphicsConfig? c) ? c : new AcGraphicsConfig();

    /// <summary>Sets (or with an empty profile removes) one account's profile.</summary>
    public void Put(string accountKey, AcGraphicsConfig cfg)
    {
        if (accountKey.Length == 0) return;
        if (cfg.HasAny) Accounts[accountKey] = cfg;
        else Accounts.Remove(accountKey);
    }

    public static AcGraphicsProfiles Load(string path)
    {
        var all = new AcGraphicsProfiles();
        if (!File.Exists(path)) return all;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        // Only "accounts": the 2026.10.18.1 file's top-level "values" / "characters" were one set for
        // every client (each fought the others over one resolution) and are left unused.
        if (doc.RootElement.TryGetProperty("accounts", out JsonElement accounts) && accounts.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty p in accounts.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                AcGraphicsConfig cfg = AcGraphicsConfig.Parse(p.Value);
                if (cfg.HasAny) all.Accounts[p.Name] = cfg;
            }
        return all;
    }

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("comment", "AC graphics profiles per account (server|account) that RynthAi enforces on UserPreferences.ini when the launcher starts that account (Settings > Graphics).");
            w.WriteStartObject("accounts");
            foreach (KeyValuePair<string, AcGraphicsConfig> kv in Accounts)
            {
                if (!kv.Value.HasAny) continue;
                w.WriteStartObject(kv.Key);
                kv.Value.Write(w);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Writes graphics.json atomically (temp file, then swap).</summary>
    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        AcPrefsIni.WriteAtomic(path, ToJson());
    }
}

/// <summary>Reads and edits UserPreferences.ini, keeping every line it doesn't change.</summary>
public static class AcPrefsIni
{
    /// <summary>"Section.Key" -> value for every key in the file.</summary>
    public static Dictionary<string, string> Read(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;
        string section = "";
        foreach (string raw in ReadLinesShared(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[section + "." + line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return map;
    }

    /// <summary>Puts <paramref name="values"/> ("Section.Key" -> value) in the file's lines: an existing key is
    /// replaced in place, a missing one added at the end of its section (the section added if needed).
    /// Returns the ids that changed.</summary>
    public static List<string> ApplyToLines(List<string> lines, IReadOnlyDictionary<string, string> values)
    {
        var changed = new List<string>();
        foreach (KeyValuePair<string, string> kv in values)
        {
            int dot = kv.Key.IndexOf('.');
            if (dot <= 0) continue;
            if (SetValue(lines, kv.Key[..dot], kv.Key[(dot + 1)..], kv.Value)) changed.Add(kv.Key);
        }
        return changed;
    }

    /// <summary>Applies <paramref name="values"/> to the file (written only when something changed).</summary>
    public static List<string> Apply(string path, IReadOnlyDictionary<string, string> values)
    {
        var lines = File.Exists(path) ? new List<string>(ReadLinesShared(path)) : new List<string>();
        List<string> changed = ApplyToLines(lines, values);
        if (changed.Count > 0)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WriteAtomic(path, string.Join("\r\n", lines) + "\r\n");
        }
        return changed;
    }

    /// <summary>Writes a temp file beside <paramref name="path"/> and swaps it in (retried on a sharing clash).</summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, path, overwrite: true); return; }
            catch (IOException) when (attempt < 5) { System.Threading.Thread.Sleep(20); }
            catch (UnauthorizedAccessException) when (attempt < 5) { System.Threading.Thread.Sleep(20); }
            catch { try { File.Delete(tmp); } catch { } throw; }
        }
    }

    private static string[] ReadLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }

    private static bool SetValue(List<string> lines, string section, string key, string value)
    {
        string header = "[" + section + "]";
        int start = lines.FindIndex(l => string.Equals(l.Trim(), header, StringComparison.OrdinalIgnoreCase));
        string wanted = key + "=" + value;
        if (start < 0)
        {
            lines.Add(header);
            lines.Add(wanted);
            return true;
        }
        int end = start + 1;
        while (end < lines.Count && !IsHeader(lines[end])) end++;
        for (int i = start + 1; i < end; i++)
        {
            string t = lines[i].Trim();
            int eq = t.IndexOf('=');
            if (eq <= 0 || !string.Equals(t[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(t[(eq + 1)..].Trim(), value, StringComparison.Ordinal)) return false;
            lines[i] = wanted;
            return true;
        }
        lines.Insert(end, wanted);
        return true;
    }

    private static bool IsHeader(string line)
    {
        string t = line.Trim();
        return t.StartsWith('[') && t.EndsWith(']');
    }
}
