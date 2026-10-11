// ============================================================================
//  RynthCore.Engine - UI/Data/Discord/ChatDiscord.cs
//  RynthChat's Discord relay: copies chosen chat (speech channels, or every
//  line that shows in a chat tab) to Discord channels by webhook or bot.
//  Configured in the Chat panel's settings (gear > Discord) and its Discord
//  panel (ImGui/Panels/ChatDiscordFace.cs); /rc discord for quick control.
//
//  Threads
//    • ChatSource.Poll (the pump) hands every new chat line to PumpLines and
//      calls PumpTick each poll: routing, batching, at most one HTTP post per
//      route in flight, applying results. The relay keeps the chat feed
//      subscribed while it is on (or its panel is open), so it runs with the
//      Chat panel closed.
//    • The panel and /rc discord (AC's thread) edit Settings in place and
//      queue Resume / Send test / Clear log; secrets are encrypted and the
//      file written on UiBackgroundWriter, never on AC's thread.
//    • HTTP posts run on the thread pool and hand results back through a queue.
//
//  Per route, lines arriving within the batch window go out as one message
//  (<= 2000 characters), spaced by the route's minimum interval or whatever
//  Discord's rate limit asks for. Network / 5xx failures retry with backoff;
//  400 / 401 / 403 / 404 pause the route until it's resumed or its
//  credential changes. Scrollback read when the relay starts (engine start,
//  hot reload, switching it on) is not sent.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RynthCore.Engine.UI.Data.Discord;

internal enum DiscordLogKind { Sent, Error, Info }

/// <summary>One line in the Discord panel's log (immutable once added).</summary>
internal sealed record DiscordLogEntry(DateTime Time, DiscordLogKind Kind, string Route, string Text);

internal static class ChatDiscord
{
    private const int DedupWindowMs = 3000;
    private const int MaxTransientRetries = 5;
    private const int MaxBackoffMs = 60000;

    /// <summary>A packed message waiting for, or in, an HTTP post.</summary>
    private sealed class Batch
    {
        public required List<string> Lines;
        public required string Content;
        public bool IsTest;
        public int Attempts;
    }

    /// <summary>Pump-thread state for one route.</summary>
    private sealed class RouteState
    {
        public readonly List<string> Pending = new();
        public long FirstPendingMs;
        public long NextAllowedMs;
        public Batch? InFlight;
        public Batch? Retry;
        public int BackoffMs;
        public bool Paused;
        public string LastError = string.Empty;
        public int MessagesSent, LinesSent, Dropped;
        public DateTime LastSent;
        public readonly Dictionary<string, long> Recent = new(StringComparer.Ordinal);
    }

    private static ChatDiscordSettings _settings = new();
    private static int _loaded;
    private static readonly string FilePath = ChatDiscordStore.DefaultPath;

    // Any thread -> pump.
    private static readonly ConcurrentQueue<(DiscordRoute Route, Batch Batch, DiscordSendResult Result)> Done = new();
    private static readonly ConcurrentQueue<DiscordRoute> ResumeQueue = new();
    private static readonly ConcurrentQueue<DiscordRoute> TestQueue = new();
    private static volatile bool _clearLog;
    private static volatile bool _shutdown;
    private static readonly CancellationTokenSource Cts = new();

    // Pump only.
    private static readonly Dictionary<DiscordRoute, RouteState> States = new(ReferenceEqualityComparer.Instance);
    private static readonly List<DiscordLogEntry> Log = new();

    // Pump writes, panel reads.
    private static DiscordLogEntry[] _logSnapshot = Array.Empty<DiscordLogEntry>();

    // The chat feed (UiSources.Chat) is held while the relay is on or its panel is open.
    private static int _feedHeld, _panelsOpen;
    private static long _activeSinceTicks = DateTime.Now.Ticks;

    // The character playing, as the pump last saw it (the panel can't ask: the lookup may read a file).
    private static string _characterName = string.Empty;

    /// <summary>The live settings. Any thread; fields are edited in place (see ChatDiscordSettings).</summary>
    public static ChatDiscordSettings Settings => Volatile.Read(ref _settings);

    /// <summary>The character this client relays as ("" before login). Any thread.</summary>
    public static string CharacterName => Volatile.Read(ref _characterName);

    // ── Setup (InitWorker / panel) ───────────────────────────────────────

    /// <summary>Loads the settings once (file I/O: InitWorker or a background thread) and opens the feed if on.</summary>
    public static void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref _loaded, 1) != 0) return;
        ChatDiscordSettings s = ChatDiscordStore.Load(FilePath, ChatDiscordStore.LegacyRynthAiPath, out string note);
        Volatile.Write(ref _settings, s);
        if (note.Length > 0) RynthLog.UI($"ChatDiscord: {note}.");
        if (note.StartsWith("imported", StringComparison.Ordinal)) Save();
        Interlocked.Exchange(ref _activeSinceTicks, DateTime.Now.Ticks);
        // Subscribing touches UiSources; done on the pump, like every other hub change.
        UiDataHub.Post("chat discord feed", SyncFeed);
    }

    /// <summary>Turns the relay on or off (and saves). Any thread.</summary>
    public static void SetEnabled(bool on)
    {
        var s = Settings;
        if (s.Enabled == on) return;
        // Lines already in the chat when it's switched on are history, not news.
        if (on) Interlocked.Exchange(ref _activeSinceTicks, DateTime.Now.Ticks);
        s.Enabled = on;
        Save();
        SyncFeed();
    }

    /// <summary>The Discord panel opened / closed: it keeps the feed (and so the relay's tick) running. AC thread.</summary>
    public static void PanelShown(bool shown)
    {
        int n = shown ? Interlocked.Increment(ref _panelsOpen) : Interlocked.Decrement(ref _panelsOpen);
        if (n < 0) Interlocked.Exchange(ref _panelsOpen, 0);
        SyncFeed();
    }

    private static void SyncFeed()
    {
        bool want = !_shutdown && (Settings.Enabled || Volatile.Read(ref _panelsOpen) > 0);
        if (want)
        {
            if (Interlocked.CompareExchange(ref _feedHeld, 1, 0) == 0)
            {
                UiSources.Chat.Subscribe();
                UiSources.Chat.RequestRefresh();
            }
        }
        else if (Interlocked.CompareExchange(ref _feedHeld, 0, 1) == 1)
            UiSources.Chat.Unsubscribe();
    }

    /// <summary>Saves in the background (the JSON is taken now). Any thread.</summary>
    public static void Save()
    {
        string json;
        try { json = ChatDiscordStore.Serialize(Settings); }
        catch (Exception ex)
        {
            RynthLog.UI($"ChatDiscord: settings not saved ({ex.GetType().Name}: {ex.Message}).");
            return;
        }
        UiBackgroundWriter.Enqueue("persist chat discord", () => ChatDiscordStore.Write(FilePath, json));
    }

    /// <summary>Engine shutdown, after the pump stopped: cancels posts in flight.</summary>
    public static void Shutdown()
    {
        _shutdown = true;
        try { Cts.Cancel(); } catch { }
    }

    // ── Panel / command requests (any thread) ────────────────────────────

    /// <summary>Clears a pause on <paramref name="route"/> (the Resume button, or a credential change).</summary>
    public static void Resume(DiscordRoute route) => ResumeQueue.Enqueue(route);

    /// <summary>Posts a test line to <paramref name="route"/> (ignoring its filters) and resumes it.</summary>
    public static void SendTest(DiscordRoute route)
    {
        ResumeQueue.Enqueue(route);
        TestQueue.Enqueue(route);
    }

    public static void ClearLog() => _clearLog = true;

    public static DiscordLogEntry[] LogSnapshot() => Volatile.Read(ref _logSnapshot);

    /// <summary>
    /// Stores a pasted webhook URL on <paramref name="route"/>, encrypted on the background writer.
    /// Returns an error for the panel, or empty when accepted. The plain text is not kept.
    /// </summary>
    public static string SetWebhook(DiscordRoute route, string url)
    {
        if (!DiscordFormat.TryNormalizeWebhookUrl(url, out string normalized))
            return "That isn't a Discord webhook URL (https://discord.com/api/webhooks/<id>/<token>).";
        UiBackgroundWriter.Enqueue("protect discord webhook", () =>
        {
            route.WebhookProtected = DiscordSecrets.Protect(normalized);
            Save();
            Resume(route);
        });
        return string.Empty;
    }

    /// <summary>Stores a pasted bot token (encrypted on the background writer). Returns an error, or empty.</summary>
    public static string SetBotToken(DiscordRoute route, string token)
    {
        if (!DiscordFormat.LooksLikeBotToken(token))
            return "That doesn't look like a bot token (Developer Portal > Bot > Reset Token).";
        string trimmed = token.Trim();
        UiBackgroundWriter.Enqueue("protect discord bot token", () =>
        {
            route.BotTokenProtected = DiscordSecrets.Protect(trimmed);
            Save();
            Resume(route);
        });
        return string.Empty;
    }

    // ── Pump ─────────────────────────────────────────────────────────────

    /// <summary>New chat lines (already routed by the chat rules). ChatSource.Poll, pump thread.</summary>
    internal static void PumpLines(IReadOnlyList<ChatLine> added)
    {
        var s = Settings;
        if (_shutdown || !s.Enabled || added.Count == 0) return;
        DiscordRoute[] routes = s.Routes;
        if (routes.Length == 0) return;
        string me = SafeName(ChatModel.PumpCharacterName());
        if (!DiscordFormat.CharacterAllowed(s.OnlyCharacters, me)) return;

        DateTime now = DateTime.Now;
        long nowMs = Environment.TickCount64;
        var since = new DateTime(Interlocked.Read(ref _activeSinceTicks));
        // Keyword lists parsed once per batch of lines (the panel may edit them between polls).
        var keywords = new List<string>[routes.Length];
        for (int i = 0; i < routes.Length; i++) keywords[i] = DiscordFormat.SplitList(routes[i].Keywords);

        foreach (ChatLine line in added)
        {
            ChatRoute chatRoute = line.Route;
            if (s.SkipHidden && chatRoute.Hidden) continue;
            DateTime time = DiscordFormat.LineTime(line.Timestamp, now);
            if (ChatDiscordLogic.IsBacklog(time, since)) continue;
            if (!ChatDiscordLogic.TryClassify(line.Text, line.ChatType, me, out DiscordLineParts parts)) continue;
            if (parts.Own && s.SkipOwn) continue;

            string channel = line.Channel;
            bool ShowsIn(string tab) => chatRoute.ShowsIn(tab, channel);
            for (int i = 0; i < routes.Length; i++)
            {
                DiscordRoute route = routes[i];
                if (!route.Enabled || !route.HasSecret) continue;
                if (!ChatDiscordLogic.Wants(route, parts.Speech, ShowsIn, out string label)) continue;
                if (!DiscordFormat.MatchesKeywords(parts.Haystack, keywords[i])) continue;

                RouteState st = State(route);
                if (st.Paused) continue;
                string key = ChatDiscordLogic.DedupKey(parts, label);
                if (st.Recent.TryGetValue(key, out long seen) && nowMs - seen < DedupWindowMs) continue;
                st.Recent[key] = nowMs;
                if (st.Recent.Count > 256) ChatDiscordLogic.PruneRecent(st.Recent, nowMs, DedupWindowMs);

                Enqueue(s, st, ChatDiscordLogic.Format(s, time, parts, label, me), nowMs);
            }
        }
    }

    /// <summary>Applies requests and results, starts due posts, publishes status. ChatSource.Poll, pump thread.</summary>
    internal static void PumpTick()
    {
        if (_shutdown) return;
        long now = Environment.TickCount64;
        var s = Settings;
        DiscordRoute[] routes = s.Routes;
        Volatile.Write(ref _characterName, SafeName(ChatModel.PumpCharacterName()));

        PruneStates(routes);
        while (ResumeQueue.TryDequeue(out var r))
        {
            if (!States.TryGetValue(r, out var st)) continue;
            st.Paused = false;
            st.LastError = string.Empty;
            st.BackoffMs = 0;
            st.NextAllowedMs = 0;
        }
        while (Done.TryDequeue(out var d)) ApplyResult(d.Route, d.Batch, d.Result, now);
        while (TestQueue.TryDequeue(out var t)) QueueTest(t);
        if (_clearLog)
        {
            _clearLog = false;
            Log.Clear();
            Volatile.Write(ref _logSnapshot, Array.Empty<DiscordLogEntry>());
        }

        foreach (var route in routes) StartNextPost(s, route, now);
        PublishStatus(routes);
    }

    private static RouteState State(DiscordRoute route)
    {
        if (!States.TryGetValue(route, out var st)) States[route] = st = new RouteState();
        return st;
    }

    private static void PruneStates(DiscordRoute[] routes)
    {
        if (States.Count <= routes.Length) return;
        var live = new HashSet<DiscordRoute>(routes, ReferenceEqualityComparer.Instance);
        var gone = new List<DiscordRoute>();
        foreach (var r in States.Keys) if (!live.Contains(r)) gone.Add(r);
        foreach (var r in gone) States.Remove(r);
    }

    private static void Enqueue(ChatDiscordSettings s, RouteState st, string line, long now)
    {
        if (st.Pending.Count == 0) st.FirstPendingMs = now;
        st.Pending.Add(line);
        int over = st.Pending.Count - s.MaxQueue;
        if (over > 0)
        {
            st.Pending.RemoveRange(0, over);
            st.Dropped += over;
        }
    }

    private static void QueueTest(DiscordRoute route)
    {
        string me = SafeName(ChatModel.PumpCharacterName());
        string line = DiscordFormat.FormatLine(DateTime.Now, null, null,
            $"RynthChat Discord relay test from {(me.Length > 0 ? me : "RynthCore")} (route \"{route.Name}\").",
            Settings.IncludeTimestamp, null);
        var st = State(route);
        st.Retry = new Batch { Lines = new List<string> { line }, Content = line, IsTest = true };
        st.NextAllowedMs = 0;
    }

    private static void StartNextPost(ChatDiscordSettings s, DiscordRoute route, long now)
    {
        if (!States.TryGetValue(route, out var st) || st.InFlight != null || st.Paused) return;
        if (now < st.NextAllowedMs) return;

        Batch? batch = st.Retry;
        if (batch == null)
        {
            if (!route.Enabled || !s.Enabled) { st.Pending.Clear(); return; }
            if (st.Pending.Count == 0) return;
            // Wait out the batch window unless the pending text already fills a message.
            int chars = 0;
            foreach (string l in st.Pending) chars += l.Length + 1;
            if (now - st.FirstPendingMs < s.BatchFor(route) && chars < DiscordFormat.MaxContent) return;

            string content = DiscordFormat.Pack(st.Pending, out int taken);
            batch = new Batch { Lines = st.Pending.GetRange(0, taken), Content = content };
            st.Pending.RemoveRange(0, taken);
            st.FirstPendingMs = now;
        }
        st.Retry = null;

        // Decrypt only now, on the way out; the plain text lives in the request task alone.
        string mode = route.Mode, channelId = route.ChannelId;
        string blob = mode == DiscordRoute.ModeBot ? route.BotTokenProtected : route.WebhookProtected;
        if (blob.Length == 0)
        {
            ApplyResult(route, batch, DiscordSendResult.Fail(
                mode == DiscordRoute.ModeBot ? "no bot token saved" : "no webhook URL saved", permanent: true), now);
            return;
        }
        if (!DiscordSecrets.TryUnprotect(blob, out string secret))
        {
            ApplyResult(route, batch, DiscordSendResult.Fail(
                "the saved credential can't be read on this Windows user; paste it again", permanent: true), now);
            return;
        }
        string displayName = route.DisplayName.Length > 0 ? route.DisplayName : SafeName(ChatModel.PumpCharacterName());

        st.InFlight = batch;
        st.NextAllowedMs = now + s.IntervalFor(route);
        CancellationToken ct = Cts.Token;
        _ = Task.Run(async () =>
        {
            DiscordSendResult r;
            try
            {
                r = mode == DiscordRoute.ModeBot
                    ? await DiscordClient.PostBotAsync(secret, channelId, batch.Content, ct).ConfigureAwait(false)
                    : await DiscordClient.PostWebhookAsync(secret, batch.Content, displayName, ct).ConfigureAwait(false);
            }
            catch (Exception ex) { r = DiscordSendResult.Fail(DiscordFormat.Redact(ex.Message, secret)); }
            Done.Enqueue((route, batch, r));
        });
    }

    private static void ApplyResult(DiscordRoute route, Batch batch, DiscordSendResult r, long now)
    {
        var st = State(route);
        st.InFlight = null;
        var s = Settings;

        if (r.Ok)
        {
            st.LastError = string.Empty;
            st.BackoffMs = 0;
            st.MessagesSent++;
            st.LinesSent += batch.Lines.Count;
            st.LastSent = DateTime.Now;
            st.NextAllowedMs = Math.Max(st.NextAllowedMs, now + Math.Max(s.IntervalFor(route), r.RetryAfterMs));
            AddLog(s, DiscordLogKind.Sent, route.Name, batch.IsTest ? "Test message sent." : $"{batch.Lines.Count} line(s): {batch.Content}");
            return;
        }

        st.LastError = r.Error;
        if (r.Permanent)
        {
            st.Paused = true;
            st.Dropped += batch.Lines.Count + st.Pending.Count;
            st.Pending.Clear();
            AddLog(s, DiscordLogKind.Error, route.Name, $"Paused: {r.Error}");
            RynthLog.UI($"ChatDiscord: route '{route.Name}' paused: {r.Error}");
            return;
        }

        if (r.Status == 429)
        {
            // Rate limits don't count as a failed attempt; Discord says exactly when to come back.
            st.Retry = batch;
            st.NextAllowedMs = now + Math.Max(r.RetryAfterMs, s.IntervalFor(route));
            AddLog(s, DiscordLogKind.Info, route.Name, r.Error);
            return;
        }

        batch.Attempts++;
        if (batch.Attempts >= MaxTransientRetries)
        {
            st.Dropped += batch.Lines.Count;
            AddLog(s, DiscordLogKind.Error, route.Name, $"Gave up after {batch.Attempts} tries ({r.Error}); {batch.Lines.Count} line(s) dropped.");
            RynthLog.UI($"ChatDiscord: route '{route.Name}' dropped a batch: {r.Error}");
            st.BackoffMs = 0;
            return;
        }
        st.BackoffMs = st.BackoffMs == 0 ? 2000 : Math.Min(st.BackoffMs * 2, MaxBackoffMs);
        st.Retry = batch;
        st.NextAllowedMs = now + Math.Max(st.BackoffMs, r.RetryAfterMs);
        AddLog(s, DiscordLogKind.Error, route.Name, $"{r.Error}; retrying in {st.BackoffMs / 1000}s.");
    }

    private static void PublishStatus(DiscordRoute[] routes)
    {
        foreach (var route in routes)
        {
            if (!States.TryGetValue(route, out var st)) continue;
            int queued = st.Pending.Count + (st.InFlight?.Lines.Count ?? 0) + (st.Retry?.Lines.Count ?? 0);
            DiscordRouteStatus? old = route.Status;
            if (old != null && old.Queued == queued && old.MessagesSent == st.MessagesSent && old.Dropped == st.Dropped
                && old.Paused == st.Paused && ReferenceEquals(old.LastError, st.LastError) && old.LastSent == st.LastSent)
                continue;
            route.Status = new DiscordRouteStatus(queued, st.MessagesSent, st.LinesSent, st.Dropped, st.Paused, st.LastError, st.LastSent);
        }
    }

    private static void AddLog(ChatDiscordSettings s, DiscordLogKind kind, string route, string text)
    {
        Log.Add(new DiscordLogEntry(DateTime.Now, kind, route, text));
        if (Log.Count > s.MaxLog) Log.RemoveRange(0, Log.Count - s.MaxLog);
        Volatile.Write(ref _logSnapshot, Log.ToArray());
    }

    private static string SafeName(string? name) => (name ?? string.Empty).Trim().TrimStart('+');

    // ── Status and /rc discord ───────────────────────────────────────────

    /// <summary>"2 of 3 route(s) ready, only Bob." Any thread.</summary>
    public static string StatusLine()
    {
        var s = Settings;
        int ready = 0;
        foreach (var r in s.Routes) if (r.Enabled && r.HasSecret) ready++;
        string only = s.OnlyCharacters.Trim().Length > 0 ? $", only {s.OnlyCharacters.Trim()}" : string.Empty;
        return $"{ready} of {s.Routes.Length} route(s) ready{only}.";
    }

    /// <summary>
    /// /rc discord [on|off|status|test [route]|resume [route]]. Returns the reply lines ("window"
    /// is handled by the caller: it opens the panel). AC's thread.
    /// </summary>
    public static List<string> HandleCommand(string args)
    {
        var reply = new List<string>();
        var s = Settings;
        string[] parts = (args ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
        string rest = parts.Length > 1 ? parts[1] : string.Empty;
        switch (verb)
        {
            case "on":
                SetEnabled(true);
                reply.Add($"Discord relay on. {StatusLine()}");
                break;
            case "off":
                SetEnabled(false);
                reply.Add("Discord relay off.");
                break;
            case "test":
            case "resume":
                if (verb == "test" && !s.Enabled && Volatile.Read(ref _panelsOpen) == 0)
                {
                    reply.Add("The relay is off: /rc discord on, or use Send test in the Discord panel (/rc discord window).");
                    break;
                }
                int n = 0;
                foreach (var r in s.Routes)
                {
                    if (rest.Length > 0 && !r.Name.Equals(rest, StringComparison.OrdinalIgnoreCase)) continue;
                    if (verb == "test") SendTest(r); else Resume(r);
                    n++;
                }
                reply.Add(n == 0
                    ? $"No route{(rest.Length > 0 ? $" named '{rest}'" : "s")}. Add one in Chat > gear > Discord (or /rc discord window)."
                    : $"{(verb == "test" ? "Test sent to" : "Resumed")} {n} route(s).");
                break;
            default:
                reply.Add($"Discord relay {(s.Enabled ? "on" : "off")}. {StatusLine()}");
                foreach (var r in s.Routes)
                {
                    var st = r.Status;
                    reply.Add($"  {r.Name}: {(r.Enabled ? "on" : "off")}, {(r.HasSecret ? r.Mode : "no destination set")}"
                              + (st == null ? string.Empty
                                  : $", {st.MessagesSent} sent, {st.Queued} queued, {st.Dropped} dropped{(st.Paused ? ", PAUSED" : string.Empty)}"
                                    + (st.LastError.Length > 0 ? $" - {st.LastError}" : string.Empty)));
                }
                reply.Add("/rc discord on|off | window | test [route] | resume [route] | status");
                break;
        }
        return reply;
    }
}
