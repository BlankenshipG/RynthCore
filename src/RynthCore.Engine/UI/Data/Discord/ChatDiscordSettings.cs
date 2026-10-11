// ============================================================================
//  RynthCore.Engine - UI/Data/Discord/ChatDiscordSettings.cs
//  RynthChat's Discord relay settings, shared by every client and character
//  (%AppData%\RynthCore\rynthchat_discord.json, beside rynthchat_settings.json).
//
//  Threads: the Discord panel (AC's render thread) edits fields in place, each
//  a single reference or 32-bit value, so the pump's reads never tear. Arrays
//  (Routes, a route's Tabs) are replaced, never changed in place, so the pump
//  can walk the one it read without a lock.
//
//  Secrets (webhook URL, bot token) are stored only as DPAPI blobs
//  (DiscordSecrets); the JSON never holds them in plain text.
//
//  The first load with no file imports RynthAi 0.5.33's discord.json (the
//  relay lived in RynthAi for one release); its blobs use the same DPAPI
//  entropy, so saved webhooks keep working.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RynthCore.Engine.UI.Data.Discord;

/// <summary>Route state the panel shows; replaced as a whole by the pump.</summary>
internal sealed record DiscordRouteStatus(int Queued, int MessagesSent, int LinesSent, int Dropped, bool Paused,
    string LastError, DateTime LastSent);

/// <summary>One Discord destination and the chat it receives.</summary>
internal sealed class DiscordRoute
{
    public const string ModeWebhook = "webhook";
    public const string ModeBot = "bot";
    public static readonly string[] Modes = { ModeWebhook, ModeBot };
    public static readonly string[] ModeNames = { "Webhook URL", "Bot token + channel ID" };

    /// <summary>Label shown in the panel, the log and /rc discord.</summary>
    public string Name = "Discord";

    public bool Enabled = true;

    public string Mode = ModeWebhook;

    /// <summary>DPAPI blob of the webhook URL (the URL embeds the webhook token).</summary>
    public string WebhookProtected = string.Empty;

    /// <summary>DPAPI blob of the bot token (bot mode).</summary>
    public string BotTokenProtected = string.Empty;

    /// <summary>Target channel snowflake (bot mode). Not secret.</summary>
    public string ChannelId = string.Empty;

    /// <summary>Webhook display name; blank uses the character's name. Ignored in bot mode.</summary>
    public string DisplayName = string.Empty;

    /// <summary>Speech channels relayed (names, <see cref="ChatSpeech.Name"/>). Saved form of <see cref="SpeechMask"/>.</summary>
    public string[]? Speech;

    /// <summary>
    /// Chat tabs relayed: every line that shows in one of them (channel tabs like System, or
    /// tabs the chat rules fill). Replaced, never changed in place.
    /// </summary>
    public string[] Tabs = Array.Empty<string>();

    /// <summary>Comma-separated words; when set, only lines containing one of them are relayed.</summary>
    public string Keywords = string.Empty;

    /// <summary>Use this route's own pacing instead of the relay's.</summary>
    public bool CustomPace;

    /// <summary>With <see cref="CustomPace"/>: lines arriving within this window go out in one message.</summary>
    public int BatchMs = 1500;

    /// <summary>With <see cref="CustomPace"/>: minimum spacing between this route's messages.</summary>
    public int MinIntervalMs = 2000;

    // ── Runtime only ─────────────────────────────────────────────────────

    /// <summary>Bits of <see cref="SpeechChannel"/> (<see cref="ChatSpeech.Bit"/>).</summary>
    [JsonIgnore] public uint SpeechMask;

    /// <summary>Written by the pump, read by the panel.</summary>
    [JsonIgnore] public volatile DiscordRouteStatus? Status;

    /// <summary>True when a send secret is stored for the current mode (the plain text is never kept here).</summary>
    [JsonIgnore]
    public bool HasSecret => Mode == ModeBot
        ? BotTokenProtected.Length > 0 && ChannelId.Length > 0
        : WebhookProtected.Length > 0;

    public bool RelaysSpeech(SpeechChannel ch) => (SpeechMask & ChatSpeech.Bit(ch)) != 0;

    /// <summary>Tells, fellowship and the allegiance channels: private chat a player usually wants on their phone.</summary>
    public static uint DefaultSpeechMask()
    {
        uint m = 0;
        foreach (var ch in new[]
                 {
                     SpeechChannel.Tell, SpeechChannel.Fellowship, SpeechChannel.Allegiance,
                     SpeechChannel.Patron, SpeechChannel.Vassals, SpeechChannel.Covassals, SpeechChannel.Monarch,
                 })
            m |= ChatSpeech.Bit(ch);
        return m;
    }

    public static uint AllSpeechMask()
    {
        uint m = 0;
        foreach (var ch in ChatSpeech.All) m |= ChatSpeech.Bit(ch);
        return m;
    }

    public static string[] NamesFromMask(uint mask)
    {
        var names = new List<string>();
        foreach (var ch in ChatSpeech.All)
            if ((mask & ChatSpeech.Bit(ch)) != 0) names.Add(ChatSpeech.Name(ch));
        return names.ToArray();
    }

    /// <summary>The mask for <paramref name="names"/>; <paramref name="fallback"/> when there is no list at all.</summary>
    public static uint MaskFromNames(IEnumerable<string>? names, uint fallback)
    {
        if (names == null) return fallback;
        uint m = 0;
        foreach (string n in names)
            if (ChatSpeech.TryParseName(n, out var ch)) m |= ChatSpeech.Bit(ch);
        return m;
    }
}

internal sealed class ChatDiscordSettings
{
    public const int CurrentVersion = 1;

    public int Version = CurrentVersion;

    /// <summary>Master switch; off means nothing is queued or sent (and the chat feed isn't kept open for it).</summary>
    public bool Enabled;

    /// <summary>Destinations. Replaced (copy-on-write) when one is added or removed.</summary>
    public DiscordRoute[] Routes = Array.Empty<DiscordRoute>();

    /// <summary>
    /// Comma-separated character names allowed to relay; blank = every character. With several
    /// clients in the same allegiance or fellowship, set this so only one of them posts each line.
    /// </summary>
    public string OnlyCharacters = string.Empty;

    /// <summary>Leave out lines you typed yourself ("You say", "You tell").</summary>
    public bool SkipOwn;

    /// <summary>Leave out lines a chat rule hides.</summary>
    public bool SkipHidden = true;

    /// <summary>Start each relayed line with the game time (HH:mm:ss).</summary>
    public bool IncludeTimestamp = true;

    /// <summary>Start each relayed line with "[Character]" (useful when several characters share a channel).</summary>
    public bool TagCharacter;

    /// <summary>Lines arriving within this window go out in one Discord message.</summary>
    public int BatchMs = 1500;

    /// <summary>Minimum spacing between messages to one route (Discord allows about 30 per minute per channel).</summary>
    public int MinIntervalMs = 2000;

    /// <summary>Lines waiting per route; the oldest are dropped beyond this.</summary>
    public int MaxQueue = 200;

    /// <summary>Entries kept in the panel's log.</summary>
    public int MaxLog = 200;

    // Pacing limits, shared by the panel's sliders and Normalize.
    public const int BatchMinMs = 0, BatchMaxMs = 60_000;
    public const int IntervalMinMs = 1000, IntervalMaxMs = 600_000;
    public const int QueueMin = 20, QueueMax = 2000;
    public const int LogMin = 20, LogMax = 2000;

    /// <summary>The batch window <paramref name="route"/> uses.</summary>
    public int BatchFor(DiscordRoute route) => route.CustomPace ? route.BatchMs : BatchMs;

    /// <summary>The message spacing <paramref name="route"/> uses.</summary>
    public int IntervalFor(DiscordRoute route) => route.CustomPace ? route.MinIntervalMs : MinIntervalMs;
}

/// <summary>Loads / saves <see cref="ChatDiscordSettings"/>.</summary>
internal static class ChatDiscordStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "rynthchat_discord.json");

    /// <summary>RynthAi 0.5.33's settings, imported once when <see cref="DefaultPath"/> doesn't exist yet.</summary>
    public static string LegacyRynthAiPath => Path.Combine(RynthCore.Install.RynthInstallPaths.RynthAiDir, "discord.json");

    /// <summary>
    /// Reads the settings (or imports RynthAi's, or the defaults). <paramref name="note"/> says what
    /// happened, for the log. Never throws.
    /// </summary>
    public static ChatDiscordSettings Load(string path, string? legacyPath, out string note)
    {
        note = string.Empty;
        ChatDiscordSettings? s = null;
        try
        {
            if (File.Exists(path))
                s = JsonSerializer.Deserialize(File.ReadAllText(path), ChatDiscordJsonContext.Default.ChatDiscordSettings);
            else if (legacyPath != null && File.Exists(legacyPath))
            {
                s = ImportRynthAi(File.ReadAllText(legacyPath));
                if (s != null) note = $"imported {s.Routes.Length} route(s) from RynthAi's discord.json";
            }
        }
        catch (Exception ex)
        {
            note = $"settings unreadable ({ex.GetType().Name}: {ex.Message}); using defaults";
            KeepUnreadable(path);
            s = null;
        }
        s ??= new ChatDiscordSettings();
        Normalize(s);
        return s;
    }

    /// <summary>The settings as JSON (speech names refreshed from the masks first). Any thread.</summary>
    public static string Serialize(ChatDiscordSettings s)
    {
        foreach (var r in s.Routes) r.Speech = DiscordRoute.NamesFromMask(r.SpeechMask);
        return JsonSerializer.Serialize(s, ChatDiscordJsonContext.Default.ChatDiscordSettings);
    }

    /// <summary>Writes <paramref name="json"/> beside the file and swaps it in. Background writer thread.</summary>
    public static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = $"{path}.{Environment.ProcessId}.tmp";   // every client shares the file
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>Repairs missing / out-of-range values and builds the runtime speech masks.</summary>
    internal static void Normalize(ChatDiscordSettings s)
    {
        s.Version = ChatDiscordSettings.CurrentVersion;
        s.Routes = (s.Routes ?? Array.Empty<DiscordRoute>()).Where(r => r != null).ToArray();
        foreach (var r in s.Routes) NormalizeRoute(r);
        s.OnlyCharacters ??= string.Empty;
        s.BatchMs = Math.Clamp(s.BatchMs, ChatDiscordSettings.BatchMinMs, ChatDiscordSettings.BatchMaxMs);
        s.MinIntervalMs = Math.Clamp(s.MinIntervalMs, ChatDiscordSettings.IntervalMinMs, ChatDiscordSettings.IntervalMaxMs);
        s.MaxQueue = Math.Clamp(s.MaxQueue, ChatDiscordSettings.QueueMin, ChatDiscordSettings.QueueMax);
        s.MaxLog = Math.Clamp(s.MaxLog, ChatDiscordSettings.LogMin, ChatDiscordSettings.LogMax);
    }

    internal static void NormalizeRoute(DiscordRoute r)
    {
        r.Name = string.IsNullOrWhiteSpace(r.Name) ? "Discord" : r.Name.Trim();
        if (Array.IndexOf(DiscordRoute.Modes, r.Mode ?? string.Empty) < 0) r.Mode = DiscordRoute.ModeWebhook;
        r.WebhookProtected ??= string.Empty;
        r.BotTokenProtected ??= string.Empty;
        r.ChannelId = (r.ChannelId ?? string.Empty).Trim();
        r.DisplayName ??= string.Empty;
        r.Keywords ??= string.Empty;
        r.SpeechMask = DiscordRoute.MaskFromNames(r.Speech, DiscordRoute.DefaultSpeechMask());
        r.Tabs = (r.Tabs ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        r.BatchMs = Math.Clamp(r.BatchMs, ChatDiscordSettings.BatchMinMs, ChatDiscordSettings.BatchMaxMs);
        r.MinIntervalMs = Math.Clamp(r.MinIntervalMs, ChatDiscordSettings.IntervalMinMs, ChatDiscordSettings.IntervalMaxMs);
    }

    /// <summary>
    /// RynthAi 0.5.33's discord.json: the same routes, with "Channels" for the speech channels and
    /// "IncludeOther" for lines that aren't speech (the Other tab here). Null when unreadable.
    /// </summary>
    internal static ChatDiscordSettings? ImportRynthAi(string json)
    {
        var old = JsonSerializer.Deserialize(json, ChatDiscordJsonContext.Default.RynthAiDiscordDto);
        if (old == null) return null;
        var s = new ChatDiscordSettings
        {
            Enabled = old.Enabled,
            OnlyCharacters = old.OnlyCharacters ?? string.Empty,
            SkipOwn = old.SkipOwn,
            IncludeTimestamp = old.IncludeTimestamp,
            TagCharacter = old.TagCharacter,
            BatchMs = old.BatchMs,
            MinIntervalMs = old.MinIntervalMs,
            MaxQueue = old.MaxQueue,
            MaxLog = old.MaxLog,
        };
        var routes = new List<DiscordRoute>();
        foreach (var r in old.Routes ?? Array.Empty<RynthAiDiscordRouteDto>())
        {
            if (r == null) continue;
            routes.Add(new DiscordRoute
            {
                Name = r.Name ?? "Discord",
                Enabled = r.Enabled,
                Mode = r.Mode ?? DiscordRoute.ModeWebhook,
                WebhookProtected = r.WebhookProtected ?? string.Empty,
                BotTokenProtected = r.BotTokenProtected ?? string.Empty,
                ChannelId = r.ChannelId ?? string.Empty,
                DisplayName = r.DisplayName ?? string.Empty,
                Speech = r.Channels?.ToArray(),
                Tabs = r.IncludeOther ? new[] { "Other" } : Array.Empty<string>(),
                Keywords = r.Keywords ?? string.Empty,
            });
        }
        s.Routes = routes.ToArray();
        return s;
    }

    private static void KeepUnreadable(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Copy(path, Path.ChangeExtension(path, $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: false);
        }
        catch { }
    }
}

// ── RynthAi 0.5.33 import shapes (read only) ─────────────────────────────

internal sealed class RynthAiDiscordRouteDto
{
    public string? Name;
    public bool Enabled = true;
    public string? Mode;
    public string? WebhookProtected;
    public string? BotTokenProtected;
    public string? ChannelId;
    public string? DisplayName;
    public List<string>? Channels;
    public bool IncludeOther;
    public string? Keywords;
}

internal sealed class RynthAiDiscordDto
{
    public bool Enabled;
    public RynthAiDiscordRouteDto[]? Routes;
    public string? OnlyCharacters;
    public bool SkipOwn;
    public bool IncludeTimestamp = true;
    public bool TagCharacter;
    public int BatchMs = 1500;
    public int MinIntervalMs = 2000;
    public int MaxQueue = 200;
    public int MaxLog = 200;
}

// JsonSerializerContext must be at namespace scope (not nested) for source generation.
[JsonSerializable(typeof(ChatDiscordSettings))]
[JsonSerializable(typeof(RynthAiDiscordDto))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    IncludeFields = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
internal partial class ChatDiscordJsonContext : JsonSerializerContext { }
