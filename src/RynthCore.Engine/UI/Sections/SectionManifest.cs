// ============================================================================
//  RynthCore.Engine - UI/Sections/SectionManifest.cs
//  What a plugin adds to the Mini Remote's sections, read from its optional
//  export  const char* RynthPluginGetSectionsJson(void)  (UTF-8 JSON, the same
//  parameterless JSON-getter convention as RynthPluginGetSnapshotJson).
//
//  Pure (no AC, no engine state): tools/SectionsTests compiles this file as is.
//
//  Schema (version 1; unknown keys are ignored, so later versions only add):
//  {
//    "version": 1,
//    "servers":  [ { "id": "ilt", "name": "Infinite Leaftide",
//                    "worldNames": ["*Leaftide*"], "hosts": ["infiniteleaftide.online"] } ],
//    "sections": [ { "id": "quests", "label": "Quests", "order": 150 } ],
//    "contributions": [
//      { "section": "pets", "server": "", "label": "Pets", "surface": "section:pets",
//        "primary": true, "width": 560, "height": 580 },
//      { "section": "skills", "server": "ilt", "label": "Progression",
//        "command": "/rc progression", "tooltip": "...", "enabled": true, "note": "" } ]
//  }
//  servers       servers the plugin knows. A built-in id (ilt, aelrynth) keeps the
//                engine's own detection; a new id is detected from its lists.
//  sections      extra sections (beyond the core twelve) the plugin adds.
//  contributions what the plugin puts in a section: "server" "" = every world, else
//                only while that server is active (detected or forced). "surface"
//                = drawn inline through RynthPluginRenderEmbed; "command" = a button
//                that runs it. "primary" = the section's main view when the engine
//                has no core panel for it (Pets, Crafting, Spells, ...).
//
//  Everything from a plugin is checked here: ids are short lower-case words,
//  text is capped, a command must be a single "/..." chat command, and the lists
//  are capped, so a damaged or hostile manifest can't flood the UI.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RynthCore.Engine.UI.Sections;

/// <summary>A server a plugin declares (detected by the engine from its lists unless the id is built in).</summary>
internal sealed record SectionServerDecl(string Id, string Name, string[] WorldNames, string[] Hosts);

/// <summary>An extra section a plugin adds.</summary>
internal sealed record SectionDecl(string Id, string Label, int Order);

/// <summary>One thing a plugin puts in a section.</summary>
internal sealed record SectionContribution(
    string Plugin,
    string Section,
    string Server,
    string Label,
    string Surface,
    string Command,
    string Tooltip,
    bool Primary,
    bool Enabled,
    string Note,
    float Width,
    float Height,
    int Order)
{
    /// <summary>Drawn inline (a plugin surface), not a button.</summary>
    public bool IsSurface => Surface.Length > 0;
    /// <summary>For every world, not one server.</summary>
    public bool IsUniversal => Server.Length == 0;
}

/// <summary>One plugin's parsed manifest.</summary>
internal sealed record SectionManifest(
    string Plugin,
    SectionServerDecl[] Servers,
    SectionDecl[] Sections,
    SectionContribution[] Contributions)
{
    public static readonly SectionManifest Empty =
        new("", Array.Empty<SectionServerDecl>(), Array.Empty<SectionDecl>(), Array.Empty<SectionContribution>());
}

internal static class SectionManifestParser
{
    public const int SupportedVersion = 1;
    public const int MaxServers = 16;
    public const int MaxSections = 32;
    public const int MaxContributions = 128;
    public const int MaxPatterns = 32;
    public const int MaxIdLength = 32;
    public const int MaxLabelLength = 48;
    public const int MaxTextLength = 300;
    public const int MaxCommandLength = 200;
    public const int MaxSurfaceLength = 64;
    public const float MaxSurfaceSize = 4096f;
    /// <summary>Largest manifest read at all (a manifest is a few KB).</summary>
    public const int MaxJsonLength = 256 * 1024;

    /// <summary>
    /// Parses <paramref name="json"/> from plugin <paramref name="plugin"/>. Never throws:
    /// a malformed document gives <see cref="SectionManifest.Empty"/> and the reason in
    /// <paramref name="error"/>; a bad entry is skipped.
    /// </summary>
    public static SectionManifest Parse(string plugin, string? json, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json)) { error = "empty"; return SectionManifest.Empty with { Plugin = plugin }; }
        if (json.Length > MaxJsonLength) { error = $"too large ({json.Length} chars)"; return SectionManifest.Empty with { Plugin = plugin }; }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "not a JSON object"; return SectionManifest.Empty with { Plugin = plugin }; }
            int version = root.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 1;
            if (version < 1) { error = $"version {version}"; return SectionManifest.Empty with { Plugin = plugin }; }

            var servers = new List<SectionServerDecl>();
            foreach (JsonElement e in Array(root, "servers"))
            {
                if (servers.Count >= MaxServers) break;
                string id = Id(Str(e, "id"));
                if (id.Length == 0 || servers.Exists(s => s.Id == id)) continue;
                string name = Text(Str(e, "name"), MaxLabelLength);
                servers.Add(new SectionServerDecl(id, name.Length > 0 ? name : id, Patterns(e, "worldNames"), Patterns(e, "hosts")));
            }

            var sections = new List<SectionDecl>();
            foreach (JsonElement e in Array(root, "sections"))
            {
                if (sections.Count >= MaxSections) break;
                string id = Id(Str(e, "id"));
                string label = Text(Str(e, "label"), MaxLabelLength);
                if (id.Length == 0 || label.Length == 0 || sections.Exists(s => s.Id == id)) continue;
                sections.Add(new SectionDecl(id, label, Int(e, "order", 1000)));
            }

            var contributions = new List<SectionContribution>();
            foreach (JsonElement e in Array(root, "contributions"))
            {
                if (contributions.Count >= MaxContributions) break;
                string section = Id(Str(e, "section"));
                string label = Text(Str(e, "label"), MaxLabelLength);
                if (section.Length == 0 || label.Length == 0) continue;
                string rawServer = Str(e, "server");
                string server = rawServer.Length == 0 ? string.Empty : Id(rawServer);
                if (rawServer.Length > 0 && server.Length == 0) continue;   // a server id that isn't one
                string surface = Surface(Str(e, "surface"));
                string command = Command(Str(e, "command"));
                if (surface.Length == 0 && command.Length == 0) continue;   // nothing to draw or run
                contributions.Add(new SectionContribution(
                    plugin, section, server, label, surface, command,
                    Text(Str(e, "tooltip"), MaxTextLength, multiline: true),
                    Bool(e, "primary", false),
                    Bool(e, "enabled", true),
                    Text(Str(e, "note"), MaxTextLength, multiline: true),
                    Size(e, "width"), Size(e, "height"),
                    Int(e, "order", 1000)));
            }

            return new SectionManifest(plugin, servers.ToArray(), sections.ToArray(), contributions.ToArray());
        }
        catch (JsonException ex)
        {
            error = "bad JSON: " + ex.Message;
            return SectionManifest.Empty with { Plugin = plugin };
        }
    }

    // ── Field rules ─────────────────────────────────────────────────────

    /// <summary>A lower-case id of letters, digits, '-' and '_' (at most MaxIdLength), else "".</summary>
    public static string Id(string? text)
    {
        string t = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0 || t.Length > MaxIdLength) return string.Empty;
        foreach (char c in t)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return string.Empty;
        return t;
    }

    /// <summary>
    /// Display text: control characters become spaces (line breaks too, unless
    /// <paramref name="multiline"/>: tooltips and notes), trimmed, capped.
    /// </summary>
    public static string Text(string? text, int max, bool multiline = false)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i]) && !(multiline && chars[i] == '\n')) chars[i] = ' ';
        string t = new string(chars).Trim();
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>A plugin surface name: letters, digits and ":_.-", not the engine's ":popout" suffix.</summary>
    public static string Surface(string? text)
    {
        string t = (text ?? string.Empty).Trim();
        if (t.Length == 0 || t.Length > MaxSurfaceLength || t.EndsWith(":popout", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        foreach (char c in t)
            if (!(char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '.' or '-')) return string.Empty;
        return t;
    }

    /// <summary>One chat command ("/rc progression"): starts with '/', one line, capped; else "".</summary>
    public static string Command(string? text)
    {
        string t = (text ?? string.Empty).Trim();
        if (t.Length < 2 || t.Length > MaxCommandLength || t[0] != '/') return string.Empty;
        foreach (char c in t)
            if (char.IsControl(c)) return string.Empty;
        return t;
    }

    private static IEnumerable<JsonElement> Array(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement a) || a.ValueKind != JsonValueKind.Array) yield break;
        foreach (JsonElement e in a.EnumerateArray())
            if (e.ValueKind == JsonValueKind.Object) yield return e;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static bool Bool(JsonElement e, string name, bool fallback)
        => e.TryGetProperty(name, out JsonElement v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        } : fallback;

    private static int Int(JsonElement e, string name, int fallback)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)
            ? Math.Clamp(n, -100_000, 100_000) : fallback;

    /// <summary>A surface's preferred width or height in pixels (0 = not given).</summary>
    private static float Size(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
           && d > 0 && !double.IsNaN(d) ? (float)Math.Min(d, MaxSurfaceSize) : 0f;

    private static string[] Patterns(JsonElement e, string name)
    {
        var list = new List<string>();
        if (!e.TryGetProperty(name, out JsonElement a) || a.ValueKind != JsonValueKind.Array) return list.ToArray();
        foreach (JsonElement p in a.EnumerateArray())
        {
            if (list.Count >= MaxPatterns) break;
            if (p.ValueKind != JsonValueKind.String) continue;
            string t = Text(p.GetString(), MaxLabelLength);
            if (t.Length > 0 && !list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
        }
        return list.ToArray();
    }
}
