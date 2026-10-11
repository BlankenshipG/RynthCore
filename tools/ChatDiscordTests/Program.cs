using System.Text.Json;
using RynthCore.Engine.UI.Data.Discord;

namespace ChatDiscordTests;

/// <summary>
/// RynthChat's Discord relay, the parts that don't need the engine: what a chat line is, which
/// routes take it, how it reads in Discord, scrollback, settings files, the request body and
/// rate-limit waits, and the DPAPI wrapper. Fake webhook URLs and tokens are built at run time
/// (no credential-shaped literals in the source).
/// </summary>
internal static class Program
{
    private static int _checks, _failed;

    // A well-formed but fake webhook URL / bot token / channel id.
    private static readonly string FakeWebhook =
        "https://discord.com/api/webhooks/" + new string('1', 18) + "/" + new string('x', 68);
    private static readonly string FakeBotToken =
        string.Join(".", new string('A', 24), new string('b', 6), new string('c', 38));
    private static readonly string FakeChannel = new string('7', 19);

    private static int Main()
    {
        Speech();
        Classify();
        Routing();
        Formatting();
        Times();
        Packing();
        Validation();
        RequestBody();
        Settings();
        Import();
        Secrets();
        Dedup();

        Console.WriteLine(_failed == 0 && _checks > 0 ? $"PASS ({_checks} checks)" : $"FAIL ({_failed} of {_checks})");
        return _failed == 0 && _checks > 0 ? 0 : 1;
    }

    // ── Speech parsing ───────────────────────────────────────────────────

    private static void Speech()
    {
        Said("Bob tells you, \"hi there\"", 0x03, SpeechChannel.Tell, "Bob", "hi there", own: false);
        Said("You tell Bob, \"yo\"", 0x03, SpeechChannel.Tell, "You", "yo", own: true);
        Said("Bob says on the General channel, \"wts pyreal\"", 0x1B, SpeechChannel.General, "Bob", "wts pyreal", own: false);
        Said("[Trade] Bob says, \"wtb mana stones\"", 0x00, SpeechChannel.Trade, "Bob", "wtb mana stones", own: false);
        Said("Bob says, \"hello\"", 0x02, SpeechChannel.Local, "Bob", "hello", own: false);
        Said("+Bob says to your fellowship, \"pull\"", 0x00, SpeechChannel.Fellowship, "Bob", "pull", own: false);
        Said("Bob says to your covassals, \"gz\"", 0x00, SpeechChannel.Covassals, "Bob", "gz", own: false);
        Said("Bob says to your vassals, \"hi\"", 0x00, SpeechChannel.Vassals, "Bob", "hi", own: false);
        Said("Bob says, \"caf<00e9> time\"", 0x12, SpeechChannel.Allegiance, "Bob", "caf\u00e9 time", own: false);
        Said("Bob says on the LFG channel, \"lf colo\"", 0x00, SpeechChannel.Lfg, "Bob", "lf colo", own: false);

        NotSpeech("You say, \"Zojak Bor\"", ChatSpeech.ChatTypeSpellcasting);   // spell words
        NotSpeech("You have gained 500 experience.", 0x00);
        NotSpeech("Bob says, \"   \"", 0x02);                                   // nothing said
        NotSpeech("", 0x02);
        NotSpeech(null, 0x02);
        NotSpeech("He says \"hi\"", 0x00);                                       // no channel at all

        Check(ChatSpeech.Name(SpeechChannel.Lfg) == "LFG", "LFG is shown upper case");
        Check(ChatSpeech.TryParseName("lfg", out var lfg) && lfg == SpeechChannel.Lfg, "LFG parses back");
        Check(ChatSpeech.TryParseName(" Fellow ", out var fel) && fel == SpeechChannel.Fellowship, "short names parse");
        Check(!ChatSpeech.TryParseName("guild", out _), "unknown names don't parse");
        foreach (var ch in ChatSpeech.All)
            Check(ChatSpeech.TryParseName(ChatSpeech.Name(ch), out var back) && back == ch, $"{ch} survives a save");
        Check(ChatSpeech.Decode("<zzzz> and <0041>") == "<zzzz> and A", "only hex escapes decode");
    }

    private static void Said(string text, int chatType, SpeechChannel ch, string who, string msg, bool own)
    {
        bool ok = ChatSpeech.TryParse(text, chatType, out var c, out string s, out string m, out bool o);
        Check(ok, $"speech: {text}");
        if (!ok) return;
        Check(c == ch, $"{text} -> {ch} (got {c})");
        Check(s == who, $"{text} speaker {who} (got {s})");
        Check(m == msg, $"{text} message \"{msg}\" (got \"{m}\")");
        Check(o == own, $"{text} own={own}");
    }

    private static void NotSpeech(string? text, int chatType)
        => Check(!ChatSpeech.TryParse(text, chatType, out _, out _, out _, out _), $"not speech: {text ?? "(null)"}");

    // ── Line classification ──────────────────────────────────────────────

    private static void Classify()
    {
        Check(ChatDiscordLogic.TryClassify("You tell Bob, \"omw\"", 0x03, "Alice", out var mine), "own tell classifies");
        Check(mine.Own && mine.Speaker == "Alice" && mine.Speech == SpeechChannel.Tell, "own lines are shown as the character");
        Check(ChatDiscordLogic.TryClassify("You tell Bob, \"omw\"", 0x03, "", out var anon) && anon.Speaker == "You",
            "own lines before login keep \"You\"");

        Check(ChatDiscordLogic.TryClassify("You gain 500 xp.", 0x00, "Alice", out var plain), "plain text classifies");
        Check(plain.Speech == null && plain.Speaker.Length == 0 && plain.Message == "You gain 500 xp.", "plain text has no channel");
        Check(plain.Haystack == "You gain 500 xp.", "plain haystack is the text");
        Check(ChatDiscordLogic.TryClassify("Bob tells you, \"rare drop\"", 0x03, "", out var tell) && tell.Haystack == "Bob rare drop",
            "speech haystack is speaker + message");

        Check(!ChatDiscordLogic.TryClassify("   ", 0x00, "Alice", out _), "blank lines are skipped");
        Check(!ChatDiscordLogic.TryClassify(null, 0x00, "Alice", out _), "null lines are skipped");
    }

    // ── Which routes take a line ─────────────────────────────────────────

    private static void Routing()
    {
        var route = Route(DiscordRoute.DefaultSpeechMask(), "System");

        Check(ChatDiscordLogic.Wants(route, SpeechChannel.Tell, _ => false, out string l1) && l1 == "Tell", "default routes take tells");
        Check(ChatDiscordLogic.Wants(route, SpeechChannel.Monarch, _ => false, out _), "default routes take the allegiance channels");
        Check(!ChatDiscordLogic.Wants(route, SpeechChannel.Local, _ => false, out _), "default routes skip local");
        Check(!ChatDiscordLogic.Wants(route, SpeechChannel.General, _ => false, out _), "default routes skip General");

        Check(ChatDiscordLogic.Wants(route, null, t => t == "System", out string l2) && l2 == "System",
            "a line in a ticked tab is taken, labelled with the tab");
        Check(ChatDiscordLogic.Wants(route, SpeechChannel.Local, t => t == "System", out string l3) && l3 == "Local",
            "speech taken through a tab keeps its channel label");
        Check(!ChatDiscordLogic.Wants(route, null, t => t == "Combat", out _), "lines only in other tabs are skipped");

        var none = Route(0);
        Check(!ChatDiscordLogic.Wants(none, SpeechChannel.Tell, _ => true, out _), "a route with nothing ticked takes nothing");

        var all = Route(DiscordRoute.AllSpeechMask());
        foreach (var ch in ChatSpeech.All)
            Check(all.RelaysSpeech(ch), $"all-channels route relays {ch}");

        Check(DiscordFormat.MatchesKeywords("Bob rare drop", DiscordFormat.SplitList("loot, RARE ,")), "keywords match case-insensitively");
        Check(!DiscordFormat.MatchesKeywords("Bob hi", DiscordFormat.SplitList("loot,rare")), "keywords filter");
        Check(DiscordFormat.MatchesKeywords("anything", DiscordFormat.SplitList(" , ")), "no keywords = everything");

        Check(DiscordFormat.CharacterAllowed("", "Alice"), "blank allow-list allows everyone");
        Check(DiscordFormat.CharacterAllowed("bob, +Alice", "Alice"), "allow-list match (case, + prefix)");
        Check(!DiscordFormat.CharacterAllowed("bob", "Alice"), "allow-list excludes others");
        Check(!DiscordFormat.CharacterAllowed("bob", ""), "allow-list needs a character name");

        var s = new ChatDiscordSettings { BatchMs = 1500, MinIntervalMs = 2000 };
        var own = new DiscordRoute { CustomPace = true, BatchMs = 0, MinIntervalMs = 60_000 };
        Check(s.BatchFor(route) == 1500 && s.IntervalFor(route) == 2000, "routes use the relay's pacing by default");
        Check(s.BatchFor(own) == 0 && s.IntervalFor(own) == 60_000, "own pacing overrides it");

        Check(!new DiscordRoute().HasSecret, "a new route has no secret");
        Check(new DiscordRoute { WebhookProtected = "blob" }.HasSecret, "webhook routes need the webhook");
        Check(!new DiscordRoute { Mode = DiscordRoute.ModeBot, BotTokenProtected = "blob" }.HasSecret, "bot routes need a channel too");
        Check(new DiscordRoute { Mode = DiscordRoute.ModeBot, BotTokenProtected = "blob", ChannelId = FakeChannel }.HasSecret, "bot route ready");
    }

    private static DiscordRoute Route(uint speech, params string[] tabs)
        => new() { SpeechMask = speech, Tabs = tabs };

    // ── Discord text ─────────────────────────────────────────────────────

    private static void Formatting()
    {
        var at = new DateTime(2026, 10, 11, 12, 34, 56);
        var s = new ChatDiscordSettings { IncludeTimestamp = true, TagCharacter = false };
        ChatDiscordLogic.TryClassify("Bob tells you, \"hi *there*\"", 0x03, "Alice", out var tell);
        Same(ChatDiscordLogic.Format(s, at, tell, "Tell", "Alice"), "`12:34:56` **\\[Tell\\] Bob:** hi \\*there\\*", "speech line");

        s.IncludeTimestamp = false;
        s.TagCharacter = true;
        Same(ChatDiscordLogic.Format(s, at, tell, "Tell", "Alice"), "\\[Alice\\] **\\[Tell\\] Bob:** hi \\*there\\*", "character tag");
        Same(ChatDiscordLogic.Format(s, at, tell, "Tell", ""), "**\\[Tell\\] Bob:** hi \\*there\\*", "no tag before login");

        s.TagCharacter = false;
        ChatDiscordLogic.TryClassify("# You found a rare!", 0x00, "Alice", out var plain);
        Same(ChatDiscordLogic.Format(s, at, plain, "", "Alice"), "\\# You found a rare!", "plain text can't become a heading");
        Same(ChatDiscordLogic.Format(s, at, plain, "System", "Alice"), "**\\[System\\]** # You found a rare!", "tab label");

        string pinged = DiscordFormat.Escape("@everyone look <@123> [x](y)");
        Check(!pinged.Contains("@everyone"), "@everyone is defused");
        Check(pinged.Contains("\\<@123\\>") && pinged.Contains("\\[x\\]"), "mentions and links are escaped");
        Same(DiscordFormat.Escape("a\r\nb"), "a  b", "newlines flatten");

        string longLine = DiscordFormat.FormatLine(at, null, null, new string('z', 5000), false, null);
        Check(longLine.Length == DiscordFormat.MaxLine && longLine.EndsWith('\u2026'), "long lines are cut");
        string cut = DiscordFormat.Truncate(new string('a', 8) + "\\*", 10);
        Check(cut.Length <= 10 && !cut.TrimEnd('\u2026').EndsWith('\\'), "cuts don't strand an escape");

        Same(DiscordFormat.SafeWebhookName("+Bob"), "Bob", "webhook name drops +");
        Same(DiscordFormat.SafeWebhookName("Discord Bob"), "", "Discord refuses names with 'discord'");
        Same(DiscordFormat.SafeWebhookName("everyone"), "", "no 'everyone' name");
        Check(DiscordFormat.SafeWebhookName(new string('n', 100)).Length == 80, "webhook names cap at 80");
    }

    // ── Line times and scrollback ────────────────────────────────────────

    private static void Times()
    {
        var now = new DateTime(2026, 10, 11, 0, 0, 30);
        Check(DiscordFormat.LineTime("23:59:50", now) == new DateTime(2026, 10, 10, 23, 59, 50), "a late stamp just after midnight is yesterday");
        Check(DiscordFormat.LineTime("00:00:20", now) == new DateTime(2026, 10, 11, 0, 0, 20), "earlier today");
        Check(DiscordFormat.LineTime("00:01:00", now) == new DateTime(2026, 10, 11, 0, 1, 0), "a little clock skew stays today");
        Check(DiscordFormat.LineTime("noon", now) == now, "unreadable stamps are now");
        Check(DiscordFormat.LineTime(null, now) == now, "missing stamps are now");

        var since = new DateTime(2026, 10, 11, 10, 0, 0, 700);
        Check(!ChatDiscordLogic.IsBacklog(new DateTime(2026, 10, 11, 10, 0, 0), since), "the start second is live");
        Check(ChatDiscordLogic.IsBacklog(new DateTime(2026, 10, 11, 9, 59, 59), since), "older lines are scrollback");
        Check(!ChatDiscordLogic.IsBacklog(new DateTime(2026, 10, 11, 10, 5, 0), since), "newer lines are live");
    }

    // ── Batching ─────────────────────────────────────────────────────────

    private static void Packing()
    {
        var lines = new List<string> { new('a', 900), new('b', 900), new('c', 900) };
        string first = DiscordFormat.Pack(lines, out int taken);
        Check(taken == 2 && first.Length == 1801, $"two 900-char lines fit one message (took {taken}, {first.Length} chars)");

        var huge = new List<string> { new('h', 2500), "next" };
        string one = DiscordFormat.Pack(huge, out int t2);
        Check(t2 == 1 && one.Length == DiscordFormat.MaxContent, "an oversized line still goes, cut to 2000");

        Check(DiscordFormat.Pack(new List<string>(), out int t3) == string.Empty && t3 == 0, "nothing to pack");
    }

    // ── Pasted values ────────────────────────────────────────────────────

    private static void Validation()
    {
        Check(DiscordFormat.TryNormalizeWebhookUrl(FakeWebhook, out string n1) && n1 == FakeWebhook, "webhook URL accepted");
        Check(DiscordFormat.TryNormalizeWebhookUrl(" " + FakeWebhook + "/?wait=true ", out string n2) && n2 == FakeWebhook,
            "query and trailing slash are dropped");
        Check(DiscordFormat.TryNormalizeWebhookUrl(FakeWebhook.Replace("discord.com", "ptb.discordapp.com").Replace("/api/", "/api/v10/"), out _),
            "ptb / discordapp / versioned URLs accepted");
        Check(!DiscordFormat.TryNormalizeWebhookUrl(FakeWebhook.Replace("https:", "http:"), out _), "plain http refused");
        Check(!DiscordFormat.TryNormalizeWebhookUrl(FakeWebhook.Replace("discord.com", "discord.com.example.net"), out _), "other hosts refused");
        Check(!DiscordFormat.TryNormalizeWebhookUrl("", out _), "blank refused");

        Check(DiscordFormat.LooksLikeBotToken(FakeBotToken), "bot token shape accepted");
        Check(!DiscordFormat.LooksLikeBotToken("Bot " + FakeBotToken), "the 'Bot ' prefix isn't part of the token");
        Check(!DiscordFormat.LooksLikeBotToken("short.x.y"), "short tokens refused");
        Check(DiscordFormat.IsSnowflake(FakeChannel), "channel id accepted");
        Check(!DiscordFormat.IsSnowflake("#general"), "channel names refused");

        string secret = new string('s', 40);
        Same(DiscordFormat.Redact("error near " + secret, secret), "error near ***", "secrets are redacted");
        Same(DiscordFormat.Redact("no secret here", ""), "no secret here", "nothing to redact");
    }

    // ── Request body and status handling ─────────────────────────────────

    private static void RequestBody()
    {
        using (var doc = JsonDocument.Parse(DiscordClient.BuildBody("line \"one\"\nline two", "Alice")))
        {
            var root = doc.RootElement;
            Check(root.GetProperty("content").GetString() == "line \"one\"\nline two", "content round-trips");
            Check(root.GetProperty("username").GetString() == "Alice", "username sent");
            Check(root.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0, "no mentions can ping");
            Check(root.GetProperty("flags").GetInt32() == 4, "embeds suppressed");
        }
        using (var doc = JsonDocument.Parse(DiscordClient.BuildBody("x", "")))
            Check(!doc.RootElement.TryGetProperty("username", out _), "no username when blank (bot / webhook default)");

        Check(DiscordClient.ParseRetryAfterMs("{\"retry_after\":1.5,\"global\":false}", null) == 1500, "retry_after from JSON");
        Check(DiscordClient.ParseRetryAfterMs(null, "3") == 3000, "Retry-After header");
        Check(DiscordClient.ParseRetryAfterMs("not json", null) == 5000, "unknown wait is 5 s");
        Check(DiscordClient.ParseRetryAfterMs("{\"retry_after\":0.01}", null) == 250, "waits at least 0.25 s");
        Check(DiscordClient.ParseRetryAfterMs("{\"retry_after\":99999}", null) == 600_000, "waits at most 10 min");

        foreach (int st in new[] { 400, 401, 403, 404 }) Check(DiscordClient.IsPermanent(st), $"HTTP {st} pauses the route");
        foreach (int st in new[] { 429, 500, 502, 503 }) Check(!DiscordClient.IsPermanent(st), $"HTTP {st} is retried");
    }

    // ── Settings file ────────────────────────────────────────────────────

    private static void Settings()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "rynthchat_discord.json");

            var fresh = ChatDiscordStore.Load(path, legacyPath: null, out string n0);
            Check(!fresh.Enabled && fresh.Routes.Length == 0 && n0.Length == 0, "no file: defaults, off");

            var s = new ChatDiscordSettings
            {
                Enabled = true,
                OnlyCharacters = "Alice",
                SkipOwn = true,
                TagCharacter = true,
                BatchMs = 999_999,          // clamped on load
                MinIntervalMs = 5,          // clamped on load
                Routes = new[]
                {
                    new DiscordRoute
                    {
                        Name = "Guild", Mode = DiscordRoute.ModeBot, ChannelId = FakeChannel, BotTokenProtected = "blob",
                        SpeechMask = ChatSpeech.Bit(SpeechChannel.Tell) | ChatSpeech.Bit(SpeechChannel.Lfg),
                        Tabs = new[] { "System", "Loot" }, Keywords = "rare", CustomPace = true, BatchMs = 0, MinIntervalMs = 30_000,
                    },
                    new DiscordRoute { Name = "  ", SpeechMask = 0 },
                },
            };
            string json = ChatDiscordStore.Serialize(s);
            Check(json.Contains("\"speech\"") && json.Contains("\"LFG\""), "speech channels are saved by name");
            Check(!json.Contains("speechMask", StringComparison.OrdinalIgnoreCase), "the runtime mask isn't saved");
            Check(!json.Contains("hasSecret", StringComparison.OrdinalIgnoreCase) && !json.Contains("\"status\""), "runtime state isn't saved");
            ChatDiscordStore.Write(path, json);
            Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "the temp file is swapped in");

            var back = ChatDiscordStore.Load(path, legacyPath: null, out string n1);
            Check(n1.Length == 0, "clean load has no note");
            Check(back.Enabled && back.SkipOwn && back.TagCharacter && back.OnlyCharacters == "Alice", "switches round-trip");
            Check(back.BatchMs == ChatDiscordSettings.BatchMaxMs, "batch window clamped");
            Check(back.MinIntervalMs == ChatDiscordSettings.IntervalMinMs, "message spacing clamped");
            Check(back.Routes.Length == 2, "routes round-trip");
            var g = back.Routes[0];
            Check(g.Name == "Guild" && g.Mode == DiscordRoute.ModeBot && g.ChannelId == FakeChannel && g.BotTokenProtected == "blob",
                "route connection round-trips");
            Check(g.RelaysSpeech(SpeechChannel.Tell) && g.RelaysSpeech(SpeechChannel.Lfg) && !g.RelaysSpeech(SpeechChannel.Fellowship),
                "speech channels round-trip");
            Check(g.Tabs.SequenceEqual(new[] { "System", "Loot" }) && g.Keywords == "rare", "tabs and keywords round-trip");
            Check(g.CustomPace && g.BatchMs == 0 && g.MinIntervalMs == 30_000, "route pacing round-trips");
            Check(back.Routes[1].Name == "Discord", "blank route names get a name");
            Check(back.Routes[1].SpeechMask == 0, "a route with every channel unticked stays empty");

            // Hand-edited file: no speech list means the defaults; bad values repaired.
            File.WriteAllText(path, "{ \"enabled\": true, \"routes\": [ { \"name\": \"x\", \"mode\": \"carrier pigeon\", " +
                                    "\"tabs\": [\"System\", \"system\", \" \", null] }, null ], \"maxQueue\": 1 }");
            var hand = ChatDiscordStore.Load(path, legacyPath: null, out _);
            Check(hand.Routes.Length == 1, "null routes dropped");
            Check(hand.Routes[0].Mode == DiscordRoute.ModeWebhook, "unknown modes become webhook");
            Check(hand.Routes[0].SpeechMask == DiscordRoute.DefaultSpeechMask(), "no speech list = the defaults");
            Check(hand.Routes[0].Tabs.Length == 1, $"tabs de-duplicated (got {hand.Routes[0].Tabs.Length})");
            Check(hand.MaxQueue == ChatDiscordSettings.QueueMin, "queue size clamped");

            // A damaged file: defaults, a note for the log, and a copy kept for the user.
            File.WriteAllText(path, "{ not json");
            var bad = ChatDiscordStore.Load(path, legacyPath: null, out string n2);
            Check(!bad.Enabled && bad.Routes.Length == 0, "damaged file: defaults");
            Check(n2.Contains("unreadable"), "damaged file is reported");
            Check(Directory.GetFiles(dir, "*.unreadable-*.json").Length == 1, "damaged file is kept aside");
        }
        finally { TryDelete(dir); }
    }

    // ── RynthAi 0.5.33 import ────────────────────────────────────────────

    private static void Import()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "rynthchat_discord.json");
            string legacy = Path.Combine(dir, "discord.json");
            // RynthAi wrote PascalCase fields.
            File.WriteAllText(legacy, "{ \"Enabled\": true, \"SkipOwn\": true, \"BatchMs\": 3000, \"OnlyCharacters\": \"Alice\", " +
                                      "\"ShowWindow\": true, \"Routes\": [ { \"Name\": \"Guild\", \"Enabled\": false, \"Mode\": \"bot\", " +
                                      $"\"ChannelId\": \"{FakeChannel}\", \"BotTokenProtected\": \"blob\", \"Channels\": [\"Tell\", \"LFG\"], " +
                                      "\"IncludeOther\": true, \"Keywords\": \"rare\" }, { \"Name\": \"Fellow\" } ] }");

            var s = ChatDiscordStore.Load(path, legacy, out string note);
            Check(note.Contains("imported 2 route"), $"import is reported (\"{note}\")");
            Check(s.Enabled && s.SkipOwn && s.BatchMs == 3000 && s.OnlyCharacters == "Alice", "relay switches imported");
            Check(s.Routes.Length == 2, "routes imported");
            var g = s.Routes[0];
            Check(!g.Enabled && g.Mode == DiscordRoute.ModeBot && g.ChannelId == FakeChannel && g.BotTokenProtected == "blob",
                "route connection imported");
            Check(g.SpeechMask == (ChatSpeech.Bit(SpeechChannel.Tell) | ChatSpeech.Bit(SpeechChannel.Lfg)), "channels become speech channels");
            Check(g.Tabs.SequenceEqual(new[] { "Other" }), "IncludeOther becomes the Other tab");
            Check(s.Routes[1].SpeechMask == DiscordRoute.DefaultSpeechMask() && s.Routes[1].Tabs.Length == 0,
                "a route with no channel list gets the defaults");

            // Once the engine has its own file, RynthAi's is never read again.
            ChatDiscordStore.Write(path, ChatDiscordStore.Serialize(new ChatDiscordSettings()));
            var mine = ChatDiscordStore.Load(path, legacy, out string n2);
            Check(!mine.Enabled && mine.Routes.Length == 0 && n2.Length == 0, "the engine's own file wins");

            Check(ChatDiscordStore.ImportRynthAi("null") == null, "empty import is null");
        }
        finally { TryDelete(dir); }
    }

    // ── DPAPI ────────────────────────────────────────────────────────────

    private static void Secrets()
    {
        string blob = DiscordSecrets.Protect(FakeWebhook);
        Check(blob.Length > 0 && !blob.Contains(FakeWebhook) && !blob.Contains("webhooks"), "protected blobs hide the secret");
        Check(DiscordSecrets.TryUnprotect(blob, out string plain) && plain == FakeWebhook, "protect / unprotect round-trip");
        Check(DiscordSecrets.Protect(FakeWebhook) != blob, "each protect is salted");

        byte[] raw = Convert.FromBase64String(blob);
        raw[raw.Length / 2] ^= 0x55;
        Check(!DiscordSecrets.TryUnprotect(Convert.ToBase64String(raw), out _), "damaged blobs don't open");
        Check(!DiscordSecrets.TryUnprotect("not base64 !!", out _), "garbage doesn't open");
        Check(!DiscordSecrets.TryUnprotect("", out _), "empty doesn't open");
        Check(DiscordSecrets.Protect("") == string.Empty && DiscordSecrets.Protect(null) == string.Empty, "empty in, empty out");
    }

    // ── Repeat filter ────────────────────────────────────────────────────

    private static void Dedup()
    {
        ChatDiscordLogic.TryClassify("Bob tells you, \"hi\"", 0x03, "", out var a);
        ChatDiscordLogic.TryClassify("Bob tells you, \"hi!\"", 0x03, "", out var b);
        Check(ChatDiscordLogic.DedupKey(a, "Tell") != ChatDiscordLogic.DedupKey(b, "Tell"), "different lines, different keys");
        Check(ChatDiscordLogic.DedupKey(a, "Tell") != ChatDiscordLogic.DedupKey(a, "General"), "the label is part of the key");

        var recent = new Dictionary<string, long> { ["old"] = 1_000, ["new"] = 9_000 };
        ChatDiscordLogic.PruneRecent(recent, now: 10_000, windowMs: 5_000);
        Check(!recent.ContainsKey("old") && recent.ContainsKey("new"), "old repeat entries are pruned");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ChatDiscordTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static void Same(string got, string want, string what)
        => Check(got == want, $"{what}: want \"{want}\", got \"{got}\"");

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine("FAIL: " + what);
    }
}
