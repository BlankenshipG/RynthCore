using System.Text.Json;
using System.Text.RegularExpressions;
using RynthCore.Engine.UI.Data;

namespace ChatFilterTests;

/// <summary>
/// RynthChat rule match modes and standard filters without AC: a Regex rule compiles and matches
/// exactly as before; Contains / Starts with / Ends with match plain text; "mode" loads and saves,
/// and a file without it is a file of Regex rules, written back unchanged; every standard filter
/// catches its example lines and spares player speech.
/// </summary>
internal static class Program
{
    private static int _checks, _failed;
    private static string _case = "";

    // Real lines from the RynthCore logs ([BuffChat] type=... text=...), plus player speech.
    private static readonly string[] Lines =
    {
        "You evaded Tusker Guard!",
        "Tusker Guard grazes your foot for 4 points of bludgeoning damage!",
        "Critical hit! Olthoi Swarm Nymph nicks your head for 21 points of piercing damage!",
        "You say, \"Malar Zhavik\"",
        "The spell consumed the following components: Prismatic Taper",
        "Your spell fizzled.",
        "You cast Focus Self I on yourself, but it is surpassed by Incantation of Focus Other",
        "You don't have all the components for this spell.",
        "You're too busy!",
        "AutoRun ON",
        "In portal Space",
        "[General] Aja says, \"lol\"",
        "Bob tells you, \"you evade like a pro (really)\"",
        "Bob says, \"$5 for the Black Opal?\"",
        "",
    };

    private static int Main()
    {
        RegexModeUnchanged();
        LiveSettingsRulesUnchanged();
        Contains();
        StartsWith();
        EndsWith();
        EmptyAndBadPatterns();
        ModeJson();
        SettingsRoundTrip();
        WhenParsing();
        PresetsCatchAndSpare();
        PresetsWellFormed();
        LineColorTags();
        LineColorKinds();
        LineColorDefaultsUnchanged();
        LineColorPrecedence();
        LineColorJson();

        Console.WriteLine();
        Console.WriteLine($"{_checks - _failed}/{_checks} checks passed.");
        Console.WriteLine(_failed == 0 ? "ALL CHAT FILTER TESTS PASSED." : $"{_failed} FAILED.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Case(string name)
    {
        _case = name;
        Console.WriteLine("- " + name);
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"  FAIL [{_case}] {what}");
    }

    /// <summary>What a rule compiled to before match modes (ChatFilterRule.Recompile, 2026-09-29).</summary>
    private static Regex? Before(string pattern, out string? error)
    {
        error = null;
        if (pattern.Length == 0) return null;
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(15)); }
        catch (ArgumentException ex) { error = ex.Message; return null; }
    }

    private static Regex? Now(ChatMatchMode mode, string pattern) => ChatRuleMatching.Compile(mode, pattern, 15, out _);

    private static void SameAsBefore(string pattern, string why)
    {
        Regex? before = Before(pattern, out string? errBefore);
        Regex? now = ChatRuleMatching.Compile(ChatMatchMode.Regex, pattern, 15, out string? errNow);
        Check((before == null) == (now == null), $"{why}: compiles the same ('{pattern}')");
        Check(errBefore == errNow, $"{why}: same error ('{pattern}')");
        if (before == null || now == null) return;
        Check(before.ToString() == now.ToString() && before.Options == now.Options, $"{why}: same regex and options ('{pattern}')");
        foreach (string line in Lines)
        {
            Match a = before.Match(line), b = now.Match(line);
            Check(a.Success == b.Success && a.Index == b.Index && a.Length == b.Length, $"{why}: same match on '{line}' ('{pattern}')");
        }
    }

    // ── Regex: as before ─────────────────────────────────────────────────

    private static void RegexModeUnchanged()
    {
        Case("regex rules compile and match exactly as before");
        foreach (string p in new[]
        {
            "You evade", "^You evaded ", @"for \d+ points? of", "^Bob tells you, \"", "Autorun On", "In portal Space",
            @"^\[General\]", "(?i)fizzled", "busy!$", ".", "a|b", "[unclosed", "(", @"\", "  spaced  ",
        })
            SameAsBefore(p, "regex");
        Check(ChatRuleMatching.ToRegex(ChatMatchMode.Regex, "^You evaded ") == "^You evaded ", "a regex pattern is used as typed");
    }

    /// <summary>Every rule in the live settings file (if any) still compiles to what it did.</summary>
    private static void LiveSettingsRulesUnchanged()
    {
        Case("the rules in this PC's rynthchat_settings.json are unchanged (read only)");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "rynthchat_settings.json");
        if (!File.Exists(path)) { Console.WriteLine("  (no live settings file; skipped)"); return; }
        string json = File.ReadAllText(path);
        RynthChatSettingsDto? dto = JsonSerializer.Deserialize(json, RynthChatJsonContext.Default.RynthChatSettingsDto);
        Check(dto != null, "the live file reads");
        int n = 0;
        foreach (RynthChatFilterDto f in dto?.Filters ?? Array.Empty<RynthChatFilterDto>())
        {
            if (f.Mode != null) continue;   // a plain-text rule saved by a newer build
            Check(ChatRuleMatching.Parse(f.Mode) == ChatMatchMode.Regex, "a rule without mode is Regex");
            SameAsBefore(f.Pattern ?? "", "live rule");
            n++;
        }
        Console.WriteLine($"  ({n} live rule(s) checked)");
    }

    // ── Plain text ───────────────────────────────────────────────────────

    private static void Contains()
    {
        Case("Contains: plain text anywhere, any case, nothing special");
        Regex rx = Now(ChatMatchMode.Contains, "evaded TUSKER")!;
        Check(rx.IsMatch("You evaded Tusker Guard!"), "anywhere, any case");
        Check(!rx.IsMatch("You evaded Olthoi Slayer!"), "not other text");
        Regex special = Now(ChatMatchMode.Contains, "(really)")!;
        Check(special.IsMatch("Bob tells you, \"you evade like a pro (really)\""), "( ) are characters");
        Check(!special.IsMatch("really"), "...not a group");
        Regex money = Now(ChatMatchMode.Contains, "$5 for the")!;
        Check(money.IsMatch("Bob says, \"$5 for the Black Opal?\""), "$ is a character");
        Regex dot = Now(ChatMatchMode.Contains, "fizzled.")!;
        Check(dot.IsMatch("Your spell fizzled."), "a dot matches a dot");
        Check(!dot.IsMatch("Your spell fizzledX"), "...not any character");
        Regex quote = Now(ChatMatchMode.Contains, "You say, \"")!;
        Check(quote.IsMatch("You say, \"Malar Zhavik\""), "a quote");
        Regex star = Now(ChatMatchMode.Contains, "a*b")!;
        Check(star.IsMatch("x a*b y") && !star.IsMatch("aaab"), "* is a character");
        Regex spaced = Now(ChatMatchMode.Contains, " evaded ")!;
        Check(spaced.IsMatch("You evaded Tusker Guard!") && !spaced.IsMatch("evaded"), "spaces in Contains count");
        Match m = rx.Match("You evaded Tusker Guard!");
        Check(m.Index == 4 && m.Length == "evaded TUSKER".Length, "the match is the text (Colour match colours just it)");
    }

    private static void StartsWith()
    {
        Case("Starts with");
        Regex rx = Now(ChatMatchMode.StartsWith, "you say, \"")!;
        Check(rx.IsMatch("You say, \"Malar Zhavik\""), "the start, any case");
        Check(!rx.IsMatch("Bob says, \"You say, \\\"x\\\"\""), "not later in the line");
        Regex lead = Now(ChatMatchMode.StartsWith, "  [General]")!;
        Check(lead.IsMatch("[General] Aja says, \"lol\""), "leading spaces in the pattern don't count");
        Check(!lead.IsMatch("Bob says, \"[General]\""), "brackets are characters, at the start only");
    }

    private static void EndsWith()
    {
        Case("Ends with");
        Regex rx = Now(ChatMatchMode.EndsWith, "evaded your attack.  ")!;
        Check(rx.IsMatch("Drudge Slinker evaded your attack."), "the end, trailing pattern spaces ignored");
        Check(rx.IsMatch("Drudge Slinker evaded your attack.   "), "trailing line spaces ignored");
        Check(!rx.IsMatch("Drudge Slinker evaded your attack. Again"), "not earlier in the line");
        Check(!rx.IsMatch("Drudge Slinker evaded your attackX"), "the dot is a dot");
    }

    private static void EmptyAndBadPatterns()
    {
        Case("empty and bad patterns");
        foreach (ChatMatchMode mode in Enum.GetValues<ChatMatchMode>())
        {
            Check(ChatRuleMatching.Compile(mode, "", 15, out string? e) == null && e == null, $"{mode}: empty matches nothing, no error");
            Check(ChatRuleMatching.Compile(mode, null, 15, out _) == null, $"{mode}: null matches nothing");
        }
        foreach (ChatMatchMode mode in new[] { ChatMatchMode.Contains, ChatMatchMode.StartsWith, ChatMatchMode.EndsWith })
        {
            Check(ChatRuleMatching.Compile(mode, "   ", 15, out _) == null, $"{mode}: spaces only match nothing");
            Check(ChatRuleMatching.Compile(mode, "[unclosed (", 15, out string? e) != null && e == null, $"{mode}: regex syntax is just text");
        }
        Check(ChatRuleMatching.Compile(ChatMatchMode.Regex, "[unclosed", 15, out string? err) == null && err != null, "Regex: a bad regex reports its error");
        Check(ChatRuleMatching.Compile(ChatMatchMode.Regex, "   ", 15, out _) != null, "Regex: spaces are a pattern (as before)");
    }

    // ── Saving and loading ───────────────────────────────────────────────

    private static void ModeJson()
    {
        Case("mode in JSON");
        Check(ChatRuleMatching.ToJson(ChatMatchMode.Regex) == null, "Regex writes no mode");
        foreach (ChatMatchMode mode in Enum.GetValues<ChatMatchMode>())
            Check(ChatRuleMatching.Parse(ChatRuleMatching.ToJson(mode)) == mode, $"{mode} round-trips");
        Check(ChatRuleMatching.Parse(null) == ChatMatchMode.Regex, "absent: Regex");
        Check(ChatRuleMatching.Parse("") == ChatMatchMode.Regex, "empty: Regex");
        Check(ChatRuleMatching.Parse("CONTAINS") == ChatMatchMode.Contains, "any case");
        Check(ChatRuleMatching.Parse("StartsWith") == ChatMatchMode.StartsWith, "StartsWith (enum name)");
        Check(ChatRuleMatching.Parse("EndsWith") == ChatMatchMode.EndsWith, "EndsWith (enum name)");
        Check(ChatRuleMatching.Parse("fuzzy") == ChatMatchMode.Regex, "unknown: Regex");
        Check(ChatRuleMatching.Labels.Length == Enum.GetValues<ChatMatchMode>().Length, "a label per mode");
    }

    private const string OldFile = """
        {"version":2,"fontSize":10,"backgroundAlpha":242,"autoScroll":true,"suppressChat":false,"logEnabled":false,
         "ctrlGatedClickThrough":false,"showTimestamps":true,"mentionHighlight":true,"mentionSound":false,
         "customTabs":["Junk"],
         "filters":[{"pattern":"In portal Space","tab":"Junk","enabled":true,"action":"move"},
                    {"pattern":"^You evaded ","tab":"","enabled":false,"action":"hide","when":"type:21"},
                    {"pattern":"fizzled","tab":"","enabled":true,"action":"color","colorMode":"match","color":"#FFD27A"}]}
        """;

    private static void SettingsRoundTrip()
    {
        Case("settings: an old file loads as Regex rules and saves unchanged; plain-text rules round-trip");
        var opts = RynthChatJsonContext.Default.RynthChatSettingsDto;
        RynthChatSettingsDto old = JsonSerializer.Deserialize(OldFile, opts)!;
        Check(old.Filters!.Length == 3, "three rules");
        Check(old.Filters.All(f => f.Mode == null && ChatRuleMatching.Parse(f.Mode) == ChatMatchMode.Regex), "every old rule is Regex");
        string saved = JsonSerializer.Serialize(old, opts);
        Check(!saved.Contains("\"mode\"", StringComparison.Ordinal), "saving old rules writes no mode key");
        string savedAgain = JsonSerializer.Serialize(JsonSerializer.Deserialize(saved, opts)!, opts);
        Check(saved == savedAgain, "load + save is stable");

        // A plain-text rule, as ChatModel.ToDto writes it, and back.
        old.Filters[0].Mode = ChatRuleMatching.ToJson(ChatMatchMode.Contains);
        old.Filters[1].Mode = ChatRuleMatching.ToJson(ChatMatchMode.StartsWith);
        string withModes = JsonSerializer.Serialize(old, opts);
        Check(withModes.Contains("\"mode\":\"contains\"", StringComparison.Ordinal), "Contains saved as mode=contains");
        RynthChatSettingsDto back = JsonSerializer.Deserialize(withModes, opts)!;
        Check(ChatRuleMatching.Parse(back.Filters![0].Mode) == ChatMatchMode.Contains, "Contains loads back");
        Check(ChatRuleMatching.Parse(back.Filters[1].Mode) == ChatMatchMode.StartsWith, "StartsWith loads back");
        Check(ChatRuleMatching.Parse(back.Filters[2].Mode) == ChatMatchMode.Regex, "the untouched rule is still Regex");
        Check(back.Filters[1].When == "type:21" && back.Filters[1].Action == "hide" && !back.Filters[1].Enabled, "other fields kept");

        // Export / Import uses the rule-set DTO: same field.
        var set = new RynthChatRuleSetDto { Format = RynthChatRuleSetDto.FormatName, Rules = back.Filters };
        string exported = JsonSerializer.Serialize(set, RynthChatJsonContext.Default.RynthChatRuleSetDto);
        RynthChatRuleSetDto imported = JsonSerializer.Deserialize(exported, RynthChatJsonContext.Default.RynthChatRuleSetDto)!;
        Check(ChatRuleMatching.Parse(imported.Rules![0].Mode) == ChatMatchMode.Contains, "export / import keeps the mode");
        RynthChatFilterDto[]? bare = JsonSerializer.Deserialize("[{\"pattern\":\"x\"}]", RynthChatJsonContext.Default.RynthChatFilterDtoArray);
        Check(bare != null && ChatRuleMatching.Parse(bare[0].Mode) == ChatMatchMode.Regex, "a bare old rule array imports as Regex");
    }

    // ── Line colours (ChatLineColors, 2026-10-08) ────────────────────────

    private static void LineColorTags()
    {
        Case("line colours: the [Tag] a channel line starts with");
        Check(ChatLineColors.TagOf("[General] Aja says, \"lol\"") == "General", "[General]");
        Check(ChatLineColors.TagOf("[Trade] You say, \"wts\"") == "Trade", "[Trade], your own line");
        Check(ChatLineColors.TagOf("[Celestial Hand] Bob says, \"x\"") == "Celestial Hand", "a spaced tag");
        Check(ChatLineColors.TagOf("[RynthAi] Started") == null, "a Rynth plugin's prefix is no channel");
        Check(ChatLineColors.TagOf("[General]Aja says") == null, "no space after the tag");
        Check(ChatLineColors.TagOf("Bob says, \"[General] hi\"") == null, "a tag later in the line");
        Check(ChatLineColors.TagOf("[] x") == null && ChatLineColors.TagOf("[ ] x") == null, "an empty tag");
        Check(ChatLineColors.TagOf("[General") == null && ChatLineColors.TagOf("[General]") == null, "unclosed / nothing after it");
        Check(ChatLineColors.TagOf("[" + new string('a', 40) + "] x") != null, "40 characters");
        Check(ChatLineColors.TagOf("[" + new string('a', 41) + "] x") == null, "41 characters: too long");
        Check(ChatLineColors.TagOf("[[x]] y") == null, "nested brackets");
        Check(ChatLineColors.TagOf("") == null && ChatLineColors.TagOf(null) == null, "empty / null");
    }

    private static void LineColorKinds()
    {
        Case("line colours: which kind a line is");
        static string? K(int type, string text, string chan = "Chat") => ChatLineColors.KindOf(type, chan, text);
        Check(K(0x02, "Bob says, \"hi\"") == ChatLineColors.Say, "say (0x02)");
        Check(K(0x03, "Bob tells you, \"hi\"") == ChatLineColors.Tell, "tell to you (0x03)");
        Check(K(0x04, "You tell Bob, \"hi\"") == ChatLineColors.TellOut, "tell you send (0x04)");
        Check(K(0x12, "Bob says to your allegiance, \"hi\"") == ChatLineColors.Allegiance, "allegiance (0x12)");
        Check(K(0x12, "[Allegiance] Bob says, \"hi\"") == ChatLineColors.Allegiance, "[Allegiance] typed 0x12");
        Check(K(0x08, "[Allegiance] Bob says, \"hi\"", "Channels") == ChatLineColors.Allegiance, "[Allegiance] typed 0x08");
        Check(K(0x13, "Bob says to your fellowship, \"hi\"") == ChatLineColors.Fellowship, "fellowship (0x13)");
        Check(K(0x0C, "Bob dances") == ChatLineColors.Emote && K(0x0A, "Bob bows") == ChatLineColors.Emote, "emotes (0x0C, 0x0A)");
        Check(K(0x08, "[General] Aja says, \"lol\"", "Channels") == "chan:General", "/cg: [General]");
        Check(K(0x09, "[General] You say, \"lol\"", "Channels") == "chan:General", "your own /cg line (0x09)");
        Check(K(0x08, "[general] Aja says, \"lol\"", "Channels") == "chan:General", "the listed spelling whatever the case");
        Check(K(0x08, "[Trade] x says", "Channels") == "chan:Trade", "[Trade]");
        Check(K(0x08, "[LFG] x says", "Channels") == "chan:LFG", "[LFG]");
        Check(K(0x08, "[Roleplay] x says", "Channels") == "chan:Roleplay", "[Roleplay]");
        Check(K(0x08, "[Society] x says", "Channels") == "chan:Society", "[Society]");
        Check(K(0x08, "[Radiant Blood] x says", "Channels") == "chan:Society", "a society's own room: Society");
        Check(K(0x08, "[Olthoi] x says", "Channels") == "chan:Olthoi", "[Olthoi]");
        Check(K(0x08, "[Wiki] x says", "Channels") == "chan:Wiki", "any other tag: its own channel");
        Check(ChatLineColors.IsOtherChannel("chan:Wiki") && !ChatLineColors.IsOtherChannel("chan:General")
            && !ChatLineColors.IsOtherChannel("say") && !ChatLineColors.IsOtherChannel(null), "IsOtherChannel");
        Check(K(0x08, "Bob says on the General channel", "Channels") == null, "an untagged channel line: the tab's colour");
        Check(K(0x05, "[General] looks like a tag", "System") == null, "a system line with a tag is no channel line");
        Check(K(0x16, "You hit the drudge") == null && K(0x00, "Welcome", "System") == null, "combat / broadcast: no kind");
        Check(K(-1, "[General] Aja says", "Channels") == "chan:General", "RynthChat before 0.2.0 (no type), Channels tab");
        Check(K(-1, "[General] Aja says", "System") == null, "no type, not a chat tab");
        Check(K(0x02, "[RynthAi] hi") == ChatLineColors.Say, "say keeps its kind whatever the text");

        ChatLineColors.NoteKind("chan:Wiki");
        ChatLineColors.NoteKind("chan:wiki");
        ChatLineColors.NoteKind("chan:General");
        ChatLineColors.NoteKind("say");
        ChatLineColors.NoteKind(null);
        Check(ChatLineColors.SeenTags.Length == 1 && ChatLineColors.SeenTags[0] == "Wiki", "only unlisted tags are remembered, once");
    }

    /// <summary>ChatModel.ChannelArgb before 2026-10-08, copied here.</summary>
    private static uint OldChannelArgb(string chan) => chan switch
    {
        "Chat" => 0xFFE0E0E0, "Channels" => 0xFF7AB8F5, "System" => 0xFF8CA6BF,
        "Combat" => 0xFFD93333, "Rynth" => 0xFFE6B450, _ => 0xFFAAAAAA,
    };

    private static readonly string[] AllTabsAndOdd = { "Chat", "Channels", "System", "Combat", "Rynth", "Other", "Junk", "" };

    private static void LineColorDefaultsUnchanged()
    {
        Case("line colours: with nothing set every line looks as before");
        ChatLineColors.ResetAll();
        Check(!ChatLineColors.AnySet && ChatLineColors.ToJson() == null, "nothing set");
        var kinds = ChatLineColors.ChatKinds.Concat(ChatLineColors.ChannelKinds).Select(k => k.Key)
            .Append("chan:Wiki").Append(null).ToArray();
        foreach (string tab in AllTabsAndOdd)
        {
            Check(ChatLineColors.TabArgb(tab) == OldChannelArgb(tab), $"tab '{tab}'");
            foreach (string? kind in kinds)
                Check(ChatLineColors.Resolve(null, kind, tab) == OldChannelArgb(tab), $"kind {kind ?? "none"} in '{tab}'");
            Check(ChatLineColors.Resolve(0xFF123456, "say", tab) == 0xFF123456, $"a rule's colour in '{tab}'");
        }
        foreach (ChatColorKind k in ChatLineColors.ChatKinds.Concat(ChatLineColors.ChannelKinds))
            Check(ChatLineColors.EffectiveArgb(k) == OldChannelArgb(k.Tab), $"{k.Label} shows its tab's colour");
        foreach (ChatColorKind k in ChatLineColors.TabKinds)
            Check(ChatLineColors.EffectiveArgb(k) == OldChannelArgb(k.Tab), $"tab {k.Label} shows its built-in colour");
    }

    private static void LineColorPrecedence()
    {
        Case("line colours: rule, then kind, then tab, then built-in");
        ChatLineColors.ResetAll();
        const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF, Rule = 0xFFABCDEF;
        ChatLineColors.Set("fellowship", Red);
        ChatLineColors.Set("chan:General", Green);
        Check(ChatLineColors.Resolve(null, "fellowship", "Chat") == Red, "fellowship set: red");
        Check(ChatLineColors.Resolve(Rule, "fellowship", "Chat") == Rule, "a rule's colour still wins");
        Check(ChatLineColors.Resolve(null, "say", "Chat") == OldChannelArgb("Chat"), "say not set: the Chat colour");
        Check(ChatLineColors.Resolve(null, "chan:General", "Channels") == Green, "/cg lines: green");
        Check(ChatLineColors.Resolve(null, "chan:Trade", "Channels") == OldChannelArgb("Channels"), "Trade not set: the Channels colour");
        Check(ChatLineColors.Resolve(null, ChatLineColors.KindOf(0x08, "Channels", "[general] Aja says"), "Channels") == Green,
            "a line's kind finds its colour (any case)");

        ChatLineColors.Set("tab:Chat", Blue);
        Check(ChatLineColors.Resolve(null, "say", "Chat") == Blue, "Chat tab set: unset kinds in it follow");
        Check(ChatLineColors.Resolve(null, null, "Chat") == Blue, "a kindless Chat line follows");
        Check(ChatLineColors.Resolve(null, "fellowship", "Chat") == Red, "a kind's own colour beats its tab's");
        Check(ChatLineColors.TabArgb("Combat") == OldChannelArgb("Combat"), "other tabs untouched");
        Check(ChatLineColors.EffectiveArgb(ChatLineColors.ChatKinds[0]) == Blue, "Say shows the Chat tab's colour");
        Check(ChatLineColors.EffectiveArgb(ChatLineColors.TabKinds[0]) == Blue, "the Chat tab shows its colour");

        ChatLineColors.Reset("fellowship");
        Check(ChatLineColors.Resolve(null, "fellowship", "Chat") == Blue, "fellowship reset: the Chat tab's colour");
        ChatLineColors.Set("FELLOWSHIP", 0x00112233);
        Check(ChatLineColors.Get("fellowship") == 0xFF112233, "keys any case; colours made opaque");
        ChatLineColors.ResetAll();
        Check(!ChatLineColors.AnySet && ChatLineColors.Resolve(null, "fellowship", "Chat") == OldChannelArgb("Chat"), "reset all: as before");
    }

    private static void LineColorJson()
    {
        Case("line colours: lineColors in rynthchat_settings.json");
        var opts = RynthChatJsonContext.Default.RynthChatSettingsDto;
        RynthChatSettingsDto old = JsonSerializer.Deserialize(OldFile, opts)!;
        Check(old.LineColors == null, "an older file has none");
        ChatLineColors.Set("say", 0xFF010203);
        ChatLineColors.Load(old.LineColors);
        Check(!ChatLineColors.AnySet, "loading it leaves nothing set");
        old.LineColors = ChatLineColors.ToJson();
        Check(!JsonSerializer.Serialize(old, opts).Contains("lineColors", StringComparison.Ordinal), "and saving it writes no lineColors");

        ChatLineColors.Load(new Dictionary<string, string>
        {
            ["say"] = "#FF0000", ["Chan:general"] = "#00FF00", ["tab:chat"] = "0000FF", ["chan:Wiki"] = "#FFFFFF",
            ["bogus"] = "#123456", ["tab:All"] = "#111111", ["tab:Junk"] = "#111111", ["chan:[x]"] = "#222222",
            ["chan:"] = "#333333", ["fellowship"] = "not a colour", ["emote"] = "",
        });
        Check(ChatLineColors.Get("say") == 0xFFFF0000, "say loads");
        Check(ChatLineColors.Get("chan:General") == 0xFF00FF00, "a channel key in any case loads");
        Check(ChatLineColors.Get("tab:Chat") == 0xFF0000FF, "a tab loads");
        Check(ChatLineColors.Get("chan:Wiki") == 0xFFFFFFFF, "another channel loads");
        Check(ChatLineColors.SetKeys.Count == 4, "bad keys and colours are skipped");

        Dictionary<string, string> json = ChatLineColors.ToJson()!;
        Check(json.ContainsKey("chan:General") && json.ContainsKey("tab:Chat") && json["say"] == "#FF0000", "saved in the listed spelling");
        old.LineColors = json;
        string saved = JsonSerializer.Serialize(old, opts);
        Check(saved.Contains("\"lineColors\":{", StringComparison.Ordinal), "written as lineColors");
        RynthChatSettingsDto back = JsonSerializer.Deserialize(saved, opts)!;
        Check(JsonSerializer.Serialize(back, opts) == saved, "load + save is stable");
        Check(back.Filters!.Length == 3 && back.CustomTabs![0] == "Junk", "the rest of the file is kept");
        ChatLineColors.Load(back.LineColors);
        Check(ChatLineColors.Resolve(null, "chan:General", "Channels") == 0xFF00FF00, "a colour survives the round trip");
        ChatLineColors.Load(null);
        Check(!ChatLineColors.AnySet, "loading a file without it clears them");
    }

    private static void WhenParsing()
    {
        Case("When: the same reading as before");
        Check(ChatRuleMatching.ParseWhen("") == (null, -1), "empty: any");
        Check(ChatRuleMatching.ParseWhen(null) == (null, -1), "null: any");
        Check(ChatRuleMatching.ParseWhen(" chan:Combat ") == ("Combat", -1), "a channel");
        Check(ChatRuleMatching.ParseWhen("CHAN:Combat") == ("Combat", -1), "chan: any case");
        Check(ChatRuleMatching.ParseWhen("type:21") == (null, 21), "a type");
        Check(ChatRuleMatching.ParseWhen("type:x") == (null, -1), "a bad type: any");
        Check(ChatRuleMatching.ParseWhen("type:-1") == (null, -1), "a negative type: any");
        Check(ChatRuleMatching.ParseWhen("chan:") == (null, -1), "chan: with nothing: any");
        Check(ChatRuleMatching.WhenMatches("combat", -1, "Combat", 3), "channel any case");
        Check(!ChatRuleMatching.WhenMatches("Combat", -1, "Chat", 21), "other channel");
        Check(ChatRuleMatching.WhenMatches(null, 21, "Combat", 21) && !ChatRuleMatching.WhenMatches(null, 21, "Combat", 22), "type");
        Check(ChatRuleMatching.WhenMatches(null, -1, "Chat", 2), "any");
    }

    // ── Standard filters ─────────────────────────────────────────────────

    private static void PresetsCatchAndSpare()
    {
        Case("standard filters catch their lines and spare the others");
        foreach (ChatFilterPreset p in ChatFilterPresets.All)
        {
            Check(p.Catches.Length > 0, $"{p.Id}: has example lines");
            foreach (var (text, chan, type) in p.Catches)
                Check(p.Matches(text, chan, type), $"{p.Id}: catches '{text}' ({chan}, type {type})");
            foreach (var (text, chan, type) in p.Spares)
                Check(!p.Matches(text, chan, type), $"{p.Id}: spares '{text}' ({chan}, type {type})");
            // The same rule built the way ChatFilterRule.Recompile builds it.
            Regex? rx = ChatRuleMatching.Compile(p.Mode, p.Pattern, 15, out string? err);
            Check(rx != null && err == null, $"{p.Id}: compiles as a rule");
        }

        // Player speech: no filter that is held to a type or channel may take a say, a tell or an emote's text.
        string[] speech = { "Bob says, \"You evaded Tusker Guard!\"", "Bob tells you, \"Your spell fizzled.\"", "You say, \"You're too busy!\"" };
        foreach (ChatFilterPreset p in ChatFilterPresets.All)
        {
            if (p.When.Length == 0) continue;   // "" presets are checked by their own Spares
            foreach (string s in speech)
            {
                Check(!p.Matches(s, "Chat", 2), $"{p.Id}: never a Local say ('{s}')");
                Check(!p.Matches(s, "Chat", 3), $"{p.Id}: never a tell ('{s}')");
            }
        }
    }

    private static void PresetsWellFormed()
    {
        Case("standard filters are well formed");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ChatFilterPreset p in ChatFilterPresets.All)
        {
            Check(ids.Add(p.Id), $"{p.Id}: unique id");
            Check(p.Group.Length > 0 && p.Label.Length > 0 && p.Description.Length > 0, $"{p.Id}: named and described");
            Check(p.Pattern.Trim().Length > 0, $"{p.Id}: has a pattern");
            var (chan, type) = ChatRuleMatching.ParseWhen(p.When);
            Check(p.When.Length == 0 || chan != null || type >= 0, $"{p.Id}: When '{p.When}' is one the rules read");
            Check(p.SameAs(p.Mode, p.Pattern, p.When), $"{p.Id}: SameAs itself");
            Check(!p.SameAs(p.Mode == ChatMatchMode.Regex ? ChatMatchMode.Contains : ChatMatchMode.Regex, p.Pattern, p.When), $"{p.Id}: another mode is another rule");
            Check(!p.SameAs(p.Mode, p.Pattern + "x", p.When), $"{p.Id}: another pattern is another rule");
            Check(ReferenceEquals(ChatFilterPresets.Get(p.Id), p), $"{p.Id}: found by id");
        }
        Check(ChatFilterPresets.Get("no.such") == null, "an unknown id is null");
        Check(ChatFilterPresets.All.Length >= 10, "a useful set");
    }
}
