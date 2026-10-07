// ============================================================================
//  RynthCore.PluginSdk - Manifest/RynthPluginManifest.cs
//  A plugin's manifest: who it is, which engine API it needs, which plugins it
//  needs. Built from the plugin's csproj at build time (build\RynthCore.PluginSdk
//  .targets) and embedded in the DLL as a Windows resource (RCDATA
//  "RYNTH_PLUGIN_MANIFEST"), so it travels with the binary and is read without
//  loading it (PluginManifestReader).
//
//  Compiled into the SDK, the engine (PluginManager) and the launcher (updater);
//  keep it free of anything but the BCL.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RynthCore.PluginSdk.Manifest;

/// <summary>One plugin another needs (or can use).</summary>
public sealed class RynthPluginDependency
{
    /// <summary>The other plugin's manifest name (for a plugin without a manifest: its file name without "RynthCore.Plugin." and ".dll").</summary>
    public string Name { get; set; } = "";
    /// <summary>The lowest version that will do; empty for any.</summary>
    public string MinVersion { get; set; } = "";
    /// <summary>True: used when present, the plugin still runs without it. False: required, the plugin isn't started without it.</summary>
    public bool Optional { get; set; }
}

/// <summary>A plugin's manifest (schema 1). Unknown JSON fields are ignored, so later schemas can add fields.</summary>
public sealed class RynthPluginManifest
{
    /// <summary>Windows resource type the manifest is stored under (RT_RCDATA).</summary>
    public const int ResourceType = 10;
    /// <summary>Windows resource name the manifest is stored under.</summary>
    public const string ResourceName = "RYNTH_PLUGIN_MANIFEST";
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>The oldest engine API the plugin runs on: its highest unguarded API use.</summary>
    public uint MinEngineApi { get; set; }
    /// <summary>The newest engine API the plugin is known to work with; 0 = no upper limit (the usual case: the table is append-only).</summary>
    public uint MaxEngineApi { get; set; }
    public List<RynthPluginDependency> Dependencies { get; set; } = new();
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string License { get; set; } = "";

    /// <summary>Parses a manifest; throws <see cref="InvalidDataException"/> with the reason when it isn't one.</summary>
    public static RynthPluginManifest Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException ex) { throw new InvalidDataException("the plugin manifest is not valid JSON: " + ex.Message); }

        using (doc)
        {
            JsonElement r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("the plugin manifest is not a JSON object");

            var m = new RynthPluginManifest();
            if (r.TryGetProperty("schema", out JsonElement s))
            {
                if (s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out int schema) || schema < 1)
                    throw new InvalidDataException("the plugin manifest's schema is not a positive number");
                m.Schema = schema;
            }
            m.Name = Str(r, "name").Trim();
            if (m.Name.Length == 0)
                throw new InvalidDataException("the plugin manifest has no name");
            m.Version = Str(r, "version").Trim();
            m.MinEngineApi = Api(r, "minEngineApi");
            m.MaxEngineApi = Api(r, "maxEngineApi");
            if (m.MaxEngineApi != 0 && m.MaxEngineApi < m.MinEngineApi)
                throw new InvalidDataException($"the plugin manifest's maxEngineApi ({m.MaxEngineApi}) is below its minEngineApi ({m.MinEngineApi})");
            m.Author = Str(r, "author");
            m.Description = Str(r, "description");
            m.Homepage = Str(r, "homepage");
            m.License = Str(r, "license");

            if (r.TryGetProperty("dependencies", out JsonElement deps) && deps.ValueKind != JsonValueKind.Null)
            {
                if (deps.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("the plugin manifest's dependencies is not an array");
                foreach (JsonElement d in deps.EnumerateArray())
                {
                    var dep = new RynthPluginDependency();
                    if (d.ValueKind == JsonValueKind.String)
                    {
                        // Shorthand: "RynthNav" or "RynthNav@2026.10.1.1" (required).
                        string text = d.GetString() ?? "";
                        int at = text.IndexOf('@');
                        dep.Name = (at >= 0 ? text[..at] : text).Trim();
                        dep.MinVersion = at >= 0 ? text[(at + 1)..].Trim() : "";
                    }
                    else if (d.ValueKind == JsonValueKind.Object)
                    {
                        dep.Name = Str(d, "name").Trim();
                        dep.MinVersion = Str(d, "minVersion").Trim();
                        dep.Optional = d.TryGetProperty("optional", out JsonElement o) && o.ValueKind == JsonValueKind.True;
                    }
                    else
                    {
                        throw new InvalidDataException("a plugin manifest dependency is neither a name nor an object");
                    }
                    if (dep.Name.Length == 0)
                        throw new InvalidDataException("a plugin manifest dependency has no name");
                    if (dep.MinVersion.Length > 0 && !PluginVersion.TryParse(dep.MinVersion, out _))
                        throw new InvalidDataException($"dependency {dep.Name}: minVersion '{dep.MinVersion}' is not a version");
                    if (string.Equals(dep.Name, m.Name, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("the plugin manifest lists the plugin as its own dependency");
                    m.Dependencies.Add(dep);
                }
            }
            return m;
        }
    }

    /// <summary>Parses, or returns null with the reason in <paramref name="error"/>.</summary>
    public static RynthPluginManifest? TryParse(string json, out string error)
    {
        try { error = ""; return Parse(json); }
        catch (InvalidDataException ex) { error = ex.Message; return null; }
    }

    /// <summary>The manifest as JSON (UTF-8, indented): what the build embeds.</summary>
    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", Schema);
            w.WriteString("name", Name);
            if (Version.Length > 0) w.WriteString("version", Version);
            w.WriteNumber("minEngineApi", MinEngineApi);
            if (MaxEngineApi != 0) w.WriteNumber("maxEngineApi", MaxEngineApi);
            w.WriteStartArray("dependencies");
            foreach (RynthPluginDependency d in Dependencies)
            {
                w.WriteStartObject();
                w.WriteString("name", d.Name);
                if (d.MinVersion.Length > 0) w.WriteString("minVersion", d.MinVersion);
                if (d.Optional) w.WriteBoolean("optional", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (Author.Length > 0) w.WriteString("author", Author);
            if (Description.Length > 0) w.WriteString("description", Description);
            if (Homepage.Length > 0) w.WriteString("homepage", Homepage);
            if (License.Length > 0) w.WriteString("license", License);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Null when this engine API can run the plugin, else why not, in a form fit for chat:
    /// "RynthOracle needs a newer RynthCore (API 77)".
    /// </summary>
    public string? CheckEngine(uint engineApi)
    {
        if (MinEngineApi > engineApi)
            return $"{Name} needs a newer RynthCore (API {MinEngineApi})";
        if (MaxEngineApi != 0 && MaxEngineApi < engineApi)
            return $"{Name} is too old for this RynthCore (it works up to API {MaxEngineApi}, this is API {engineApi})";
        return null;
    }

    /// <summary>"RynthCore.Plugin.RynthNav.dll" -> "RynthNav": the name a plugin without a manifest goes by.</summary>
    public static string NameFromFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path) ?? "";
        const string prefix = "RynthCore.Plugin.";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
    }

    private static string Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

    private static uint Api(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out JsonElement e) || e.ValueKind == JsonValueKind.Null) return 0;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out uint v)) return v;
        if (e.ValueKind == JsonValueKind.String && uint.TryParse(e.GetString(), out uint sv)) return sv;
        throw new InvalidDataException($"the plugin manifest's {name} is not a whole number");
    }
}

/// <summary>Plugin version strings: "2026.10.5.1", "1.2", "2026.10.5.1+8ca322f".</summary>
public static class PluginVersion
{
    /// <summary>Parses the numeric part (build metadata after '+' and a '-prerelease' tail are ignored).</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t[1..];
        int cut = t.IndexOfAny(new[] { '+', '-', ' ' });
        if (cut >= 0) t = t[..cut];
        if (t.Length == 0) return false;
        if (!t.Contains('.')) t += ".0";
        return System.Version.TryParse(t, out version!);
    }

    /// <summary>
    /// An unstamped build: the SDK's default version, 1.0.0 (Deploy-RynthCore.ps1, IDE builds).
    /// It satisfies any minimum version, so dev setups keep working; the engine logs it.
    /// </summary>
    public static bool IsDevBuild(string? text) =>
        TryParse(text, out Version v) && v.Major == 1 && v.Minor == 0 && v.Build <= 0 && v.Revision <= 0;

    /// <summary>True when <paramref name="actual"/> is at least <paramref name="minimum"/> (or either is missing, or actual is a dev build).</summary>
    public static bool Satisfies(string? actual, string? minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum)) return true;
        if (!TryParse(minimum, out Version min)) return true;
        if (!TryParse(actual, out Version have)) return false;
        if (IsDevBuild(actual)) return true;
        return Normalize(have) >= Normalize(min);
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
