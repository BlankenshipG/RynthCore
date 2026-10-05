// ============================================================================
//  RynthCore.Engine - UI/Data/ChatData.cs
//  RynthChat's state, shared by both chat faces (ImGui/Panels/ChatFace.cs and
//  the Avalonia UI/Panels/RynthChatPanel.cs) and both filter editors:
//
//    • ChatSource (hub, 100 ms) pulls new lines from RynthChatGetScrollbackJson
//      on the pump thread, keeps the newest 500, routes and colours them with
//      the rules, and writes the chat log.
//    • ChatModel holds the settings (rynthchat_settings.json), the regex
//      rules and custom tabs, and the chat input line.
//
//  Rules (2026-09-29 overhaul). Each rule is a case-insensitive regex matched
//  against the line's text as AC printed it (no timestamp, so ^ and $ work),
//  optionally only for one channel or chat type. It can
//    Move  the line to a tab (it leaves All and its channel tab),
//    Copy  it to a tab as well (it stays where it was),
//    Hide  it everywhere, or
//    Colour it only,
//  and it can colour the whole line or just the matched text. Order: top to
//  bottom. Copy rules add up; the first matching Move or Hide decides and
//  stops the routing. The first matching line colour wins; matched-text
//  colours add up, a higher rule winning where two overlap.
//
//  Threads: rules are evaluated ONLY on the pump (ChatSource) - each new line
//  once, and every kept line again when the rules change - and the result is
//  stored on the line (ChatLine.Route). The faces just read routes; no regex
//  runs per frame. The one UI-thread evaluation is the rule editor's test box,
//  on a keystroke. Every regex has a 15 ms match timeout; a rule that times
//  out is marked Slow and skipped until it is edited, so a pathological
//  pattern costs one timeout, not one per line. A rule that never times out
//  but spends over 100 ms re-routing the kept lines is marked Slow too: the
//  pump is the plugin tick thread, and a slow re-route holds every plugin's
//  tick (ChatRouter.Charge).
//
//  The input line: while ChatCaptureActive, Win32Backend's WndProc hook turns
//  keys into the OnChat* callbacks on AC's main thread. ChatModel installs
//  them once at init (InstallInput), so typing works whichever face is on
//  screen; RynthChatSendLine runs there too (it feeds AC's chat parser). The
//  export is resolved by ChatSource on the pump, since PluginExportBinder
//  refuses AC's thread.
//
//  Nothing here touches Avalonia: EntryPoint calls ChatModel.EnsureSettingsLoaded
//  before the overlay starts (see the dispatcher invariant there).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.Plugins;

namespace RynthCore.Engine.UI.Data;

/// <summary>One scrollback line. Immutable except its Route, which the pump replaces.</summary>
internal sealed class ChatLine
{
    public ChatLine(long id, string ts, string chan, int chatType, string? sender, string text)
    {
        Id = id;
        Timestamp = ts;
        Channel = chan;
        ChatType = chatType;
        Sender = sender;
        Text = text;
        // The text already names the speaker ("Bob says, ..."); until 2026-09-29
        // the sender was printed in front of it as well ("Bob: Bob says, ...").
        FormattedText = $"{ts} {text}";
    }

    /// <summary>Engine-assigned, increasing for the whole session (the plugin's own
    /// seq restarts after RL).</summary>
    public long Id { get; }
    public string Timestamp { get; }
    /// <summary>The channel tab it belongs to (ChatClassifier in the plugin).</summary>
    public string Channel { get; }
    /// <summary>AC's ChatMessageType; -1 from RynthChat before 0.2.0.</summary>
    public int ChatType { get; }
    /// <summary>Who spoke, for player speech; metadata only (mentions, rules from a line).</summary>
    public string? Sender { get; }
    /// <summary>The line as AC printed it. Rules match this.</summary>
    public string Text { get; }
    /// <summary>"HH:mm:ss text": copy, log and the Avalonia face.</summary>
    public string FormattedText { get; }

    private ChatRoute _route = ChatRoute.Default;

    /// <summary>Where the rules put this line and how it's coloured. Written by the
    /// pump (before the line is published, and again when the rules change); any thread reads.</summary>
    public ChatRoute Route
    {
        get => Volatile.Read(ref _route);
        internal set => Volatile.Write(ref _route, value);
    }
}

/// <summary>A run of the line's Text drawn in its own colour (0xAARRGGBB).</summary>
internal readonly record struct ChatSpan(int Start, int Length, uint Argb);

/// <summary>The rules' verdict for one line. Immutable.</summary>
internal sealed class ChatRoute
{
    public static readonly ChatRoute Default = new(false, true, null, null, null, false);

    public ChatRoute(bool hidden, bool inHome, string[]? tabs, uint? lineArgb, ChatSpan[]? spans, bool mention)
    {
        Hidden = hidden;
        InHome = inHome;
        Tabs = tabs;
        LineArgb = lineArgb;
        Spans = spans;
        Mention = mention;
    }

    /// <summary>A Hide rule matched: shown nowhere.</summary>
    public bool Hidden { get; }
    /// <summary>Shown under All and its channel tab (false once a Move rule took it away).</summary>
    public bool InHome { get; }
    /// <summary>Tabs a Move or Copy rule sent it to.</summary>
    public string[]? Tabs { get; }
    /// <summary>Whole-line colour from a rule; null = the channel colour.</summary>
    public uint? LineArgb { get; }
    /// <summary>Matched-text colours: offsets into Text, sorted, not overlapping.</summary>
    public ChatSpan[]? Spans { get; }
    /// <summary>Someone said your name (or a mention word).</summary>
    public bool Mention { get; }

    public bool ShowsIn(string tab, string channel)
    {
        if (Hidden) return false;
        if (InHome && (tab == "All" || string.Equals(tab, channel, StringComparison.OrdinalIgnoreCase))) return true;
        if (Tabs != null)
            foreach (string t in Tabs)
                if (string.Equals(t, tab, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

internal enum ChatRuleAction
{
    /// <summary>Only in the rule's tab: leaves All and its channel tab.</summary>
    Move,
    /// <summary>Also in the rule's tab.</summary>
    Copy,
    /// <summary>Nowhere.</summary>
    Hide,
    /// <summary>No routing: the rule only colours.</summary>
    Color,
}

internal enum ChatColorMode { None, Line, Match }

/// <summary>How a rule's Pattern is matched against the raw message text.</summary>
internal enum ChatMatchMode
{
    /// <summary>The pattern is a regex (case-insensitive).</summary>
    Regex,
    /// <summary>Plain text anywhere in the message.</summary>
    Contains,
    /// <summary>Plain text at the start of the message.</summary>
    StartsWith,
    /// <summary>Plain text at the end of the message (trailing spaces ignored).</summary>
    EndsWith,
}

/// <summary>
/// One rule. Its fields are edited in place by a rule editor (then
/// ChatModel.FiltersChanged); the pump reads each field once per line, and a
/// half-applied edit is re-routed by the version bump that follows it.
/// </summary>
internal sealed class ChatFilterRule
{
    public const int TimeoutMs = 15;

    public volatile bool Enabled = true;
    public volatile string Pattern = "";
    /// <summary>The Move / Copy target. Empty: a Move or Copy rule routes nowhere (it can still colour).</summary>
    public volatile string Tab = "";
    public volatile ChatRuleAction Action = ChatRuleAction.Move;
    public volatile ChatColorMode ColorMode = ChatColorMode.None;
    /// <summary>Regex, or a plain-text mode (escaped into a regex by <see cref="Recompile"/>).</summary>
    public volatile ChatMatchMode Mode = ChatMatchMode.Regex;
    /// <summary>A new rule's colour (amber).</summary>
    public const uint DefaultColor = 0xFFFFD27A;
    /// <summary>0xAARRGGBB.</summary>
    public volatile uint ColorArgb = DefaultColor;

    private volatile WhenSpec _when = WhenSpec.Any;
    /// <summary>"" any line; "chan:Combat" one channel tab; "type:3" one ChatMessageType (see ChatModel.Conditions).</summary>
    public string When
    {
        get => _when.Raw;
        set => _when = WhenSpec.Parse(value);
    }

    /// <summary>Kept lines this rule matched (recounted when the rules change, then
    /// counted up as lines arrive). Written by the pump; the editor shows it.</summary>
    public int Hits;

    public volatile Regex? Compiled;
    /// <summary>The pattern doesn't compile (or is empty).</summary>
    public volatile bool Invalid;
    /// <summary>Why it doesn't compile; null when it does or is just empty.</summary>
    public volatile string? Error;
    /// <summary>A match timed out: skipped until the pattern is edited.</summary>
    public volatile bool Slow;

    /// <summary>Compiles the pattern once (after every pattern edit). Any thread.</summary>
    public void Recompile()
    {
        Slow = false;
        string pattern = Pattern;
        if (pattern.Length == 0) { Compiled = null; Error = null; Invalid = true; return; }
        string expr = Mode switch
        {
            ChatMatchMode.Contains   => Regex.Escape(pattern),
            ChatMatchMode.StartsWith => "^" + Regex.Escape(pattern),
            ChatMatchMode.EndsWith   => Regex.Escape(pattern.TrimEnd()) + @"\s*$",
            _                        => pattern,
        };
        try
        {
            Compiled = new Regex(expr, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(TimeoutMs));
            Error = null;
            Invalid = false;
        }
        catch (ArgumentException ex)
        {
            Compiled = null;
            Error = ex.Message;
            Invalid = true;
        }
    }

    /// <summary>Does the rule route (Move / Copy with a tab, or Hide)?</summary>
    public bool Routes => Action == ChatRuleAction.Hide
        || (Action is ChatRuleAction.Move or ChatRuleAction.Copy && Tab.Length > 0);

    internal bool WhenMatches(string channel, int chatType)
    {
        WhenSpec w = _when;
        if (w.Channel != null) return string.Equals(w.Channel, channel, StringComparison.OrdinalIgnoreCase);
        if (w.Type >= 0) return w.Type == chatType;
        return true;
    }

    private sealed class WhenSpec
    {
        public static readonly WhenSpec Any = new("", null, -1);
        public readonly string Raw;
        public readonly string? Channel;
        public readonly int Type;
        private WhenSpec(string raw, string? channel, int type) { Raw = raw; Channel = channel; Type = type; }

        public static WhenSpec Parse(string? raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.StartsWith("chan:", StringComparison.OrdinalIgnoreCase) && raw.Length > 5)
                return new WhenSpec(raw, raw[5..], -1);
            if (raw.StartsWith("type:", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw[5..], out int t) && t >= 0)
                return new WhenSpec(raw, null, t);
            return Any;
        }
    }
}

/// <summary>New RynthChat lines while a chat face is open (100 ms). Publishes the newest 500, routed.</summary>
internal sealed unsafe class ChatSource : UiSource<ChatLine[]>
{
    private const int MaxLines = 500;
    private delegate* unmanaged[Cdecl]<ulong, IntPtr> _get;
    private ulong _sinceSeq;
    private long _nextId = 1;
    private bool _boundLogged;
    private long _routedVersion = -1;
    private string _routedName = "";

    public ChatSource() : base("Chat", periodMs: 100) { }

    protected internal override void Poll()
    {
        if (_get == null)
        {
            _get = (delegate* unmanaged[Cdecl]<ulong, IntPtr>)PluginExportBinder.Resolve("RynthChat", "RynthChatGetScrollbackJson");
            if (_get != null && !_boundLogged)
            {
                _boundLogged = true;
                RynthLog.UI("ChatSource: bound RynthChat plugin exports.");
            }
        }
        if (ChatModel.SendLinePtr == IntPtr.Zero)
            ChatModel.SendLinePtr = PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine");
        ChatModel.PluginBound = _get != null;
        ChatModel.PumpLog(null);

        // The rules (or the name mentions look for) changed: route the kept lines again.
        long version = ChatModel.FiltersVersion;
        string name = ChatModel.PumpCharacterName();
        ChatRouter router = ChatModel.PumpRouter(name);
        if (version != _routedVersion || name != _routedName)
        {
            _routedVersion = version;
            _routedName = name;
            ChatLine[]? kept = Current?.Value;
            router.BeginPass();
            try
            {
                if (kept != null)
                    foreach (ChatLine line in kept) line.Route = router.Evaluate(line, null);
            }
            finally { router.EndPass(); }
            ChatModel.BumpRoutes();
        }

        if (_get == null) return;

        IntPtr ptr = _get(_sinceSeq);
        if (ptr == IntPtr.Zero) return;
        string? json = Marshal.PtrToStringAnsi(ptr);
        if (string.IsNullOrEmpty(json) || json == "[]") return;

        RynthChatLineDto[]? dtos = ParseLines(json, out ulong skippedSeq);
        // Lines that couldn't be read still move the mark past them, or they'd come back every poll.
        if (skippedSeq > _sinceSeq) _sinceSeq = skippedSeq;
        if (dtos == null || dtos.Length == 0) return;

        var added = new List<ChatLine>(dtos.Length);
        foreach (RynthChatLineDto dto in dtos)
        {
            if (dto.Seq <= _sinceSeq) continue;
            _sinceSeq = dto.Seq;
            string text = StripAcLinks(dto.Text ?? "", out string? linkedName);
            var line = new ChatLine(_nextId++, dto.Ts ?? "", dto.Chan ?? "Other", dto.Type,
                                    dto.Sender is { Length: > 0 } s ? StripAcLinks(s, out _) : linkedName, text);
            line.Route = router.Evaluate(line, null);
            if (line.Route.Mention) ChatModel.PumpMentionSound();
            added.Add(line);
        }
        if (added.Count == 0) return;

        ChatLine[] old = Current?.Value ?? Array.Empty<ChatLine>();
        int keepOld = Math.Max(0, Math.Min(old.Length, MaxLines - added.Count));
        int skipAdded = Math.Max(0, added.Count - MaxLines);
        var lines = new ChatLine[keepOld + added.Count - skipAdded];
        Array.Copy(old, old.Length - keepOld, lines, 0, keepOld);
        added.CopyTo(skipAdded, lines, keepOld, added.Count - skipAdded);
        Publish(lines);
        ChatModel.PumpLog(added);
    }

    private int _badLinesLogged;

    // AC's clickable-name markup: <Tell:IIDString:0:Chucky>Chucky<\Tell>. The retail chat
    // window renders it as a link; the panel printed it raw (fellowship, allegiance and
    // channel lines, 2026-09-30). Shown as the name alone.
    private static readonly System.Text.RegularExpressions.Regex AcTellLink = new(
        @"<Tell:IIDString:\d+:([^>]*)>(.*?)<\\Tell>",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20));

    /// <summary>The text with AC's name links reduced to their display text; <paramref name="firstName"/> is the first linked name.</summary>
    internal static string StripAcLinks(string text, out string? firstName)
    {
        firstName = null;
        if (text.IndexOf("<Tell:", StringComparison.Ordinal) < 0) return text;
        try
        {
            string? first = null;
            string result = AcTellLink.Replace(text, m =>
            {
                string shown = m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : m.Groups[1].Value;
                first ??= m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : shown;
                return shown;
            });
            firstName = first;
            return result;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return text;
        }
    }

    /// <summary>
    /// The scrollback batch. A line System.Text.Json refuses (a lone UTF-16
    /// surrogate, which a plugin's chat text can carry) used to fail the whole
    /// batch: _sinceSeq never moved, the same batch failed on every poll, and the
    /// chat stood still until that line left the plugin's 500-line ring. Now the
    /// batch is read line by line when it has to be; an unreadable line is
    /// skipped and its seq returned in <paramref name="skippedSeq"/>.
    /// </summary>
    private RynthChatLineDto[]? ParseLines(string json, out ulong skippedSeq)
    {
        skippedSeq = 0;
        try { return JsonSerializer.Deserialize(json, RynthChatJsonContext.Default.RynthChatLineDtoArray); }
        catch (Exception) { }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<RynthChatLineDto>();
            foreach (JsonElement el in doc.RootElement.EnumerateArray())
            {
                try
                {
                    RynthChatLineDto? dto = el.Deserialize(RynthChatJsonContext.Default.RynthChatLineDto);
                    if (dto != null) list.Add(dto);
                }
                catch (Exception ex)
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("seq", out JsonElement s)
                        && s.TryGetUInt64(out ulong seq) && seq > skippedSeq)
                        skippedSeq = seq;
                    if (_badLinesLogged++ < 5)
                        RynthLog.UI($"ChatSource: skipped a chat line it couldn't read ({ex.GetType().Name}: {ex.Message}).");
                }
            }
            return list.ToArray();
        }
        catch (Exception ex)
        {
            if (_badLinesLogged++ < 5)
                RynthLog.UI($"ChatSource: scrollback JSON unreadable ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }

    protected internal override void Reset()
    {
        // RL loads a fresh plugin copy whose scrollback numbers from 1 again.
        // The lines already shown stay.
        _get = null;
        _sinceSeq = 0;
        ChatModel.SendLinePtr = IntPtr.Zero;
        ChatModel.PluginBound = false;
    }
}

/// <summary>
/// Evaluates the rules for one line: a snapshot of the rule list plus the
/// mention regex. Built by ChatModel.PumpRouter (pump) or for the editor's
/// test box (UI thread). One thread per router: each one's cost counters are
/// its own (the pump's lives on the pump; the test box builds a fresh one).
/// </summary>
internal sealed class ChatRouter
{
    /// <summary>Mentions colour the name like this (amber).</summary>
    public const uint MentionArgb = 0xFFFFC857;

    // The 15 ms timeout only catches a pattern that is slow on one line. One that
    // costs a millisecond or two per line never times out, yet re-routing the 500
    // kept lines (on every keystroke in the rule editor) then holds the pump -
    // which is the plugin tick thread - for a second or more. So during a
    // re-route pass each rule's matching time is added up, and a rule that uses
    // more than PassBudgetMs is marked Slow like a timed-out one. An ordinary
    // rule takes a few microseconds a line, a few ms for the pass. One reading
    // counts at most MaxChargeMs, so a GC pause or a descheduled thread in the
    // middle of a match doesn't condemn the rule on its own.
    private const double PassBudgetMs = 100, MaxChargeMs = 30;
    private static readonly long PassBudgetTicks = (long)(Stopwatch.Frequency * PassBudgetMs / 1000);
    private static readonly long MaxChargeTicks = (long)(Stopwatch.Frequency * MaxChargeMs / 1000);

    /// <summary>ChatMessageType Emote (0x0C).</summary>
    private const int EmoteType = 0x0C;

    private readonly ChatFilterRule[] _rules;
    private readonly Regex? _mention;
    private readonly string _self;
    private readonly bool _countHits;
    private readonly long[] _passTicks;
    private bool _inPass;

    /// <param name="self">Your character's name ("" unknown): your own emotes don't mention you.</param>
    /// <param name="countHits">Count matches into ChatFilterRule.Hits (the pump's router; not the test box's).</param>
    public ChatRouter(ChatFilterRule[] rules, Regex? mention, string self, bool countHits)
    {
        _rules = rules;
        _mention = mention;
        _self = self.Trim();
        _countHits = countHits;
        _passTicks = new long[rules.Length];
    }

    /// <summary>Before routing every kept line again: hit counts restart, and each rule's time is watched.</summary>
    public void BeginPass()
    {
        ResetHits();
        Array.Clear(_passTicks);
        _inPass = true;
    }

    public void EndPass() => _inPass = false;

    /// <summary>Adds a rule's matching time during a pass; marks it Slow past the budget. False once it's Slow.</summary>
    private bool Charge(int i, long ticks, List<Step>? trace)
    {
        if (!_inPass) return true;
        _passTicks[i] += Math.Min(ticks, MaxChargeTicks);
        if (_passTicks[i] <= PassBudgetTicks) return true;
        MarkSlow(_rules[i], trace, i, $"over {PassBudgetMs:0} ms to re-route the kept lines");
        return false;
    }

    /// <summary>Before routing every kept line again.</summary>
    public void ResetHits()
    {
        foreach (ChatFilterRule rule in _rules) Volatile.Write(ref rule.Hits, 0);
    }

    /// <summary>One step of the editor's test: rule index (-1 = mention, -2 = a standard filter) and what it did.</summary>
    public readonly record struct Step(int Rule, string What);

    /// <summary><see cref="Step.Rule"/> of a standard-filter (RynthChatPresets) step.</summary>
    public const int StandardFilterStep = -2;

    public ChatRoute Evaluate(ChatLine line, List<Step>? trace) =>
        Evaluate(line.Text, line.Channel, line.ChatType, trace);

    public ChatRoute Evaluate(string text, string channel, int chatType, List<Step>? trace)
    {
        bool hidden = false, inHome = true, routed = false;
        List<string>? tabs = null;
        uint? lineArgb = null;
        List<(int Start, int Length, uint Argb)>? spans = null;

        for (int i = 0; i < _rules.Length; i++)
        {
            ChatFilterRule rule = _rules[i];
            if (!rule.Enabled || rule.Slow) continue;
            Regex? rx = rule.Compiled;
            if (rx == null) continue;
            ChatRuleAction action = rule.Action;
            ChatColorMode mode = rule.ColorMode;
            string tab = rule.Tab;
            bool routes = action == ChatRuleAction.Hide
                || (action is ChatRuleAction.Move or ChatRuleAction.Copy && tab.Length > 0);
            bool wantsRoute = routes && !routed;
            bool wantsLine = mode == ChatColorMode.Line && lineArgb == null;
            bool wantsMatch = mode == ChatColorMode.Match;
            // Nothing left for it to decide (the test box still shows that it matches).
            if (!wantsRoute && !wantsLine && !wantsMatch && trace == null) continue;
            if (!rule.WhenMatches(channel, chatType)) continue;

            Match m;
            long t0 = Stopwatch.GetTimestamp();
            try { m = rx.Match(text); }
            catch (RegexMatchTimeoutException) { MarkSlow(rule, trace, i, $"a match took over {ChatFilterRule.TimeoutMs} ms"); continue; }
            if (!Charge(i, Stopwatch.GetTimestamp() - t0, trace)) continue;
            if (!m.Success) continue;
            if (_countHits) Interlocked.Increment(ref rule.Hits);

            var what = trace != null ? new StringBuilder() : null;
            if (wantsRoute)
            {
                switch (action)
                {
                    case ChatRuleAction.Hide:
                        hidden = routed = true;
                        what?.Append("hides it");
                        break;
                    case ChatRuleAction.Move:
                        inHome = false;
                        routed = true;
                        AddTab(ref tabs, tab);
                        what?.Append("moves it to ").Append(tab);
                        break;
                    case ChatRuleAction.Copy:
                        AddTab(ref tabs, tab);
                        what?.Append("copies it to ").Append(tab);
                        break;
                }
            }
            else if (routes && trace != null)
                what!.Append("(a Move/Hide above already decided the tab)");
            if (hidden)
            {
                trace?.Add(new Step(i, what!.ToString()));
                break;
            }
            if (wantsLine)
            {
                lineArgb = rule.ColorArgb;
                what?.Append(what.Length > 0 ? ", " : "").Append("colours the line");
            }
            else if (mode == ChatColorMode.Line && trace != null)
                what!.Append(what.Length > 0 ? ", " : "").Append("(line colour already set above)");
            if (wantsMatch)
            {
                int n = 0;
                long t1 = Stopwatch.GetTimestamp();
                try
                {
                    for (; m.Success && n < 16; m = m.NextMatch())
                        if (m.Length > 0) { (spans ??= new()).Add((m.Index, m.Length, rule.ColorArgb)); n++; }
                }
                catch (RegexMatchTimeoutException) { MarkSlow(rule, trace, i, $"a match took over {ChatFilterRule.TimeoutMs} ms"); }
                if (!rule.Slow) Charge(i, Stopwatch.GetTimestamp() - t1, trace);
                what?.Append(what.Length > 0 ? ", " : "").Append(n == 1 ? "colours the match" : $"colours {n} matches");
            }
            if (what is { Length: 0 }) what.Append("matches, but has nothing to do yet (no tab, no colour)");
            trace?.Add(new Step(i, what!.ToString()));
        }

        // Standard filters (RynthChatPresets) decide only what the custom rules left undecided.
        if (!routed)
        {
            var preset = RynthCore.Engine.UI.Panels.RynthChatPresets.FirstMatch(text, channel);
            if (preset != null)
            {
                string presetTab = preset.Tab;
                routed = true;
                if (presetTab.Length == 0)
                {
                    hidden = true;
                    trace?.Add(new Step(StandardFilterStep, $"standard filter \"{preset.Label}\" hides it"));
                }
                else
                {
                    inHome = false;
                    AddTab(ref tabs, presetTab);
                    trace?.Add(new Step(StandardFilterStep, $"standard filter \"{preset.Label}\" moves it to {presetTab}"));
                }
            }
        }

        bool mention = false;
        if (!hidden && _mention != null && ChatModel.MentionEligible(channel, chatType))
        {
            try
            {
                Match m = _mention.Match(text);
                for (int n = 0; m.Success && n < 8; m = m.NextMatch(), n++)
                {
                    // Your own emote prints as "Tom waves." (type Emote, your name first): that's you, not a mention.
                    if (m.Index == 0 && chatType == EmoteType && _self.Length > 0
                        && string.Equals(m.Value, _self, StringComparison.OrdinalIgnoreCase)) continue;
                    mention = true;
                    (spans ??= new()).Add((m.Index, m.Length, MentionArgb));
                }
            }
            catch (RegexMatchTimeoutException) { }
            if (mention) trace?.Add(new Step(-1, "mentions you (highlighted)"));
        }

        if (!hidden && inHome && tabs == null && lineArgb == null && spans == null && !mention)
            return ChatRoute.Default;
        return new ChatRoute(hidden, inHome, tabs?.ToArray(), lineArgb, Resolve(spans, text.Length), mention);
    }

    private static void AddTab(ref List<string>? tabs, string tab)
    {
        tabs ??= new List<string>(2);
        foreach (string t in tabs) if (string.Equals(t, tab, StringComparison.OrdinalIgnoreCase)) return;
        tabs.Add(tab);
    }

    private static void MarkSlow(ChatFilterRule rule, List<Step>? trace, int index, string why)
    {
        if (!rule.Slow)
        {
            rule.Slow = true;
            RynthLog.UI($"ChatModel: rule \"{rule.Pattern}\" is too slow ({why}) and is skipped until edited.");
            // Lines already routed with it are routed again without it.
            ChatModel.BumpFiltersVersion();
        }
        trace?.Add(new Step(index, "too slow - now skipped until edited"));
    }

    /// <summary>Overlapping spans: the earlier one (a higher rule) keeps its characters.</summary>
    private static ChatSpan[]? Resolve(List<(int Start, int Length, uint Argb)>? spans, int textLength)
    {
        if (spans == null || spans.Count == 0) return null;
        if (spans.Count == 1)
            return new[] { new ChatSpan(spans[0].Start, spans[0].Length, spans[0].Argb) };
        var owner = new int[textLength];   // 0 = free, else span index + 1
        for (int s = 0; s < spans.Count; s++)
        {
            int end = Math.Min(textLength, spans[s].Start + spans[s].Length);
            for (int c = spans[s].Start; c < end; c++)
                if (owner[c] == 0) owner[c] = s + 1;
        }
        var result = new List<ChatSpan>();
        for (int c = 0; c < textLength;)
        {
            int o = owner[c];
            int start = c;
            while (c < textLength && owner[c] == o) c++;
            if (o != 0) result.Add(new ChatSpan(start, c - start, spans[o - 1].Argb));
        }
        return result.ToArray();
    }
}

internal static class ChatModel
{
    /// <summary>Channel tabs; must match ChatClassifier in the plugin.</summary>
    public static readonly string[] BaseTabs = { "All", "Chat", "Channels", "System", "Combat", "Rynth", "Other" };

    /// <summary>Channel colour as 0xAARRGGBB.</summary>
    public static uint ChannelArgb(string chan) => chan switch
    {
        "Chat"     => 0xFFE0E0E0,
        "Channels" => 0xFF7AB8F5,
        "System"   => 0xFF8CA6BF,
        "Combat"   => 0xFFD93333,
        "Rynth"    => 0xFFE6B450,   // amber, distinct from the blue/grey channels
        _          => 0xFFAAAAAA,
    };

    /// <summary>The line's colour: a rule's line colour, else its channel's. 0xAARRGGBB.</summary>
    public static uint LineArgb(ChatLine line) => line.Route.LineArgb ?? ChannelArgb(line.Channel);

    /// <summary>
    /// What a rule's "When" can be: (stored value, label). Channel tabs, then
    /// single ChatMessageTypes (ACE.Entity.Enum.ChatMessageType; retail's too).
    /// </summary>
    public static readonly (string Value, string Label)[] Conditions =
    {
        ("", "Any line"),
        ("chan:Chat", "Chat tab (say, tells, allegiance, fellowship)"),
        ("chan:Channels", "Channels tab (General, Trade, LFG...)"),
        ("chan:System", "System tab"),
        ("chan:Combat", "Combat tab (combat and magic)"),
        ("chan:Rynth", "Rynth tab (plugin output)"),
        ("chan:Other", "Other tab"),
        ("type:3", "Tells to you"),
        ("type:4", "Tells you send"),
        ("type:2", "Say (local)"),
        ("type:12", "Emotes"),
        ("type:18", "Allegiance"),
        ("type:19", "Fellowship"),
        ("type:8", "Channels (General, Trade...)"),
        ("type:17", "Spell words"),
        ("type:7", "Magic"),
        ("type:22", "Your combat"),
        ("type:21", "Enemy combat"),
        ("type:13", "Advancement"),
        ("type:20", "World broadcast"),
        ("type:16", "Appraisal"),
        ("type:24", "Crafting"),
        ("type:25", "Salvaging"),
        ("type:23", "Recall"),
        ("type:5", "System messages"),
    };

    /// <summary>The channel tab a ChatMessageType lands in; mirrors ChatClassifier.ChannelFor in the plugin.</summary>
    public static string ChannelForType(int chatType) => (uint)chatType switch
    {
        0x02 or 0x03 or 0x04 or 0x0A or 0x0B or 0x0C or 0x12 or 0x13 => "Chat",
        0x08 or 0x09 => "Channels",
        0x00 or 0x05 or 0x0D or 0x14 or 0x17 or 0x18 or 0x19 or 0x1F => "System",
        0x06 or 0x07 or 0x11 or 0x15 or 0x16 => "Combat",
        _ => "Other",
    };

    public static string ConditionLabel(string when)
    {
        foreach (var c in Conditions)
            if (string.Equals(c.Value, when, StringComparison.OrdinalIgnoreCase)) return c.Label;
        return when.Length == 0 ? "Any line" : when;
    }

    // ── Settings (any thread; saved with SaveSettings) ───────────────────

    public static volatile float FontSize = 10f;
    public static volatile int BackgroundAlpha = 0xF2;
    public static volatile bool AutoScroll = true;
    public static volatile bool LogEnabled;
    /// <summary>Docked chat lets clicks through unless Ctrl is held (mirrors the radar option).
    /// AvaloniaOverlay.ChatCtrlGatedClickThrough is the Avalonia hit-test gate; set both.</summary>
    public static volatile bool CtrlGatedClickThrough;
    public static volatile string ActiveChannel = "All";
    public static volatile bool ShowTimestamps = true;
    /// <summary>Highlight your character's name (and MentionWords) in chat and channel lines.</summary>
    public static volatile bool MentionHighlight = true;
    /// <summary>A short system beep on a new mention (at most every 3 s).</summary>
    public static volatile bool MentionSound;
    /// <summary>Extra words to highlight, comma-separated.</summary>
    public static volatile string MentionWords = "";

    public static void SetCtrlGatedClickThrough(bool on)
    {
        CtrlGatedClickThrough = on;
        AvaloniaOverlay.ChatCtrlGatedClickThrough = on;
        SaveSettings();
    }

    // ── Rules and custom tabs ────────────────────────────────────────────
    //
    // The lists are replaced, never changed in place, so readers on other
    // threads can walk them; a rule's own fields are edited in place and then
    // FiltersChanged.

    private static readonly object EditLock = new();
    private static ChatFilterRule[] _filters = Array.Empty<ChatFilterRule>();
    private static string[] _customTabs = Array.Empty<string>();
    private static long _filtersVersion = 1;
    private static long _routesVersion = 1;
    private static (long Version, string[] Tabs) _allTabs;

    public static ChatFilterRule[] Filters => Volatile.Read(ref _filters);

    /// <summary>Bumped on every rule / tab / mention-setting change. The pump re-routes when it moves.</summary>
    public static long FiltersVersion => Interlocked.Read(ref _filtersVersion);

    /// <summary>Re-route without saving (a rule was just found too slow). Any thread.</summary>
    internal static void BumpFiltersVersion() => Interlocked.Increment(ref _filtersVersion);

    /// <summary>Bumped by the pump once the kept lines carry routes for the current rules; faces rebuild then.</summary>
    public static long RoutesVersion => Interlocked.Read(ref _routesVersion);
    internal static void BumpRoutes() => Interlocked.Increment(ref _routesVersion);

    /// <summary>Moves when the tabs or the routes change (the Avalonia face rebuilds on it).</summary>
    public static long ViewVersion => FiltersVersion + RoutesVersion;

    /// <summary>A rule just made from a chat line: the rule editor scrolls to it and flashes it.</summary>
    public static volatile ChatFilterRule? FocusRule;

    public static void EditFilters(Action<List<ChatFilterRule>> edit)
    {
        lock (EditLock)
        {
            var list = new List<ChatFilterRule>(_filters);
            edit(list);
            Volatile.Write(ref _filters, list.ToArray());
        }
        FiltersChanged();
    }

    /// <summary>Moves a rule one place up (-1) or down (+1). Order matters (see the header).</summary>
    public static void MoveFilter(ChatFilterRule rule, int delta) => EditFilters(list =>
    {
        int i = list.IndexOf(rule), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        list.RemoveAt(i);
        list.Insert(j, rule);
    });

    /// <summary>After a rule's fields changed: re-route lines and save.</summary>
    public static void FiltersChanged()
    {
        Interlocked.Increment(ref _filtersVersion);
        SaveSettings();
    }

    /// <summary>Adds <paramref name="rule"/> at the end (its tab too) and asks the editor to show it.</summary>
    public static void AddRule(ChatFilterRule rule)
    {
        rule.Recompile();
        if (rule.Tab.Length > 0) EnsureCustomTab(rule.Tab);
        FocusRule = rule;
        EditFilters(list => list.Add(rule));
    }

    /// <summary>
    /// A pattern for "lines like this": player speech keeps the speaker and the
    /// way they spoke ("^Bob tells you, \""); anything else is the whole line
    /// with each run of digits made \d+. A long line (an appraisal, a long
    /// broadcast) gives only its start, cut at a word: the whole of one could
    /// outgrow the rule editor's pattern box, which would cut it mid-escape.
    /// </summary>
    public static string PatternFor(ChatLine line)
    {
        const int MaxChars = 140;
        string t = line.Text;
        if (line.Sender != null)
        {
            int q = t.IndexOf(", \"", StringComparison.Ordinal);
            if (q > 0 && q < 120) return "^" + Escape(t[..(q + 3)]);
        }
        bool cut = t.Length > MaxChars;
        if (cut)
        {
            int space = t.LastIndexOf(' ', MaxChars - 1, MaxChars / 2);
            t = t[..(space > 0 ? space : MaxChars)];
            if (t.Length > 0 && char.IsHighSurrogate(t[^1])) t = t[..^1];
        }
        var sb = new StringBuilder("^");
        for (int i = 0; i < t.Length;)
        {
            int j = i;
            if (char.IsDigit(t[i]))
            {
                while (j < t.Length && char.IsDigit(t[j])) j++;
                sb.Append(@"\d+");
            }
            else
            {
                while (j < t.Length && !char.IsDigit(t[j])) j++;
                sb.Append(Escape(t[i..j]));
            }
            i = j;
        }
        return cut ? sb.ToString() : sb.Append('$').ToString();
    }

    // Regex.Escape escapes spaces too, which is legal but hard to read.
    private static string Escape(string s) => Regex.Escape(s).Replace("\\ ", " ");

    /// <summary>
    /// Every tab: the channel tabs, then the created ones, then any named by an
    /// enabled Move/Copy rule that isn't a created tab (a rule set up in the
    /// Avalonia editor, or imported). Cached per filter version. Any thread.
    /// </summary>
    public static string[] AllTabs()
    {
        long version = FiltersVersion;
        var cached = _allTabs;
        if (cached.Tabs != null && cached.Version == version) return cached.Tabs;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tabs = new List<string>();
        foreach (string t in BaseTabs) if (seen.Add(t)) tabs.Add(t);
        foreach (string t in Volatile.Read(ref _customTabs)) if (seen.Add(t)) tabs.Add(t);
        foreach (ChatFilterRule rule in Filters)
            if (rule.Enabled && rule.Routes && rule.Tab.Length > 0 && seen.Add(rule.Tab)) tabs.Add(rule.Tab);
        foreach (string t in RynthCore.Engine.UI.Panels.RynthChatPresets.TargetTabs())
            if (seen.Add(t)) tabs.Add(t);
        string[] result = tabs.ToArray();
        _allTabs = (version, result);
        return result;
    }

    /// <summary>The tabs a Move/Copy rule can send lines to: every tab but All.</summary>
    public static string[] TargetTabs() => AllTabs().Where(t => t != "All").ToArray();

    public static bool IsBaseTab(string tab) => BaseTabs.Contains(tab, StringComparer.OrdinalIgnoreCase);

    /// <summary>The tab to show: the active one, or All when it no longer exists.</summary>
    public static string CurrentTab()
    {
        string active = ActiveChannel;
        return AllTabs().Contains(active, StringComparer.OrdinalIgnoreCase) ? active : "All";
    }

    public static void SelectTab(string tab)
    {
        if (string.Equals(ActiveChannel, tab, StringComparison.Ordinal)) return;
        ActiveChannel = tab;
        SaveSettings();
    }

    /// <summary>Creates a tab (or finds the existing one) and switches to it.</summary>
    public static void AddCustomTab(string name) => AddCustomTab(name, select: true);

    /// <summary>Creates a tab (or finds the existing one); returns its name, or null for a blank name.</summary>
    public static string? AddCustomTab(string name, bool select)
    {
        name = name.Trim();
        if (name.Length == 0) return null;
        string? existing = AllTabs().FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        if (existing == null || !IsBaseTab(existing)) EnsureCustomTab(existing ?? name);
        if (select) ActiveChannel = existing ?? name;
        SaveSettings();
        return existing ?? name;
    }

    private static void EnsureCustomTab(string name)
    {
        if (IsBaseTab(name)) return;
        bool added = false;
        lock (EditLock)
        {
            if (!_customTabs.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _customTabs, _customTabs.Append(name).ToArray());
                added = true;
            }
        }
        if (added) Interlocked.Increment(ref _filtersVersion);
    }

    /// <summary>Renames a created tab and the rules that send lines to it. False when the name is taken or blank.</summary>
    public static bool RenameCustomTab(string tab, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || IsBaseTab(tab)) return false;
        if (!string.Equals(tab, newName, StringComparison.OrdinalIgnoreCase)
            && AllTabs().Contains(newName, StringComparer.OrdinalIgnoreCase)) return false;
        lock (EditLock)
        {
            string[] tabs = _customTabs.Select(t => string.Equals(t, tab, StringComparison.OrdinalIgnoreCase) ? newName : t).ToArray();
            if (!tabs.Contains(newName)) tabs = tabs.Append(newName).ToArray();
            Volatile.Write(ref _customTabs, tabs);
        }
        foreach (ChatFilterRule rule in Filters)
            if (string.Equals(rule.Tab, tab, StringComparison.OrdinalIgnoreCase))
                rule.Tab = newName;
        foreach (var preset in RynthCore.Engine.UI.Panels.RynthChatPresets.All)
            if (string.Equals(preset.Tab, tab, StringComparison.OrdinalIgnoreCase))
                preset.Tab = newName;
        if (string.Equals(ActiveChannel, tab, StringComparison.OrdinalIgnoreCase)) ActiveChannel = newName;
        FiltersChanged();
        return true;
    }

    /// <summary>Deletes a created tab and disables the rules that fill it, so it
    /// doesn't come back through AllTabs and its lines don't vanish.</summary>
    public static void DeleteCustomTab(string tab)
    {
        lock (EditLock)
            Volatile.Write(ref _customTabs,
                _customTabs.Where(t => !string.Equals(t, tab, StringComparison.OrdinalIgnoreCase)).ToArray());
        foreach (ChatFilterRule rule in Filters)
            if (rule.Action is ChatRuleAction.Move or ChatRuleAction.Copy
                && string.Equals(rule.Tab, tab, StringComparison.OrdinalIgnoreCase))
                rule.Enabled = false;
        foreach (var preset in RynthCore.Engine.UI.Panels.RynthChatPresets.All)
            if (string.Equals(preset.Tab, tab, StringComparison.OrdinalIgnoreCase))
                preset.Enabled = false;
        FiltersChanged();
    }

    /// <summary>Is the line shown under <paramref name="tab"/> (rules applied, no search)?</summary>
    public static bool LineInTab(ChatLine line, string tab) => line.Route.ShowsIn(tab, line.Channel);

    public static bool LineVisible(ChatLine line, string tab, string search) =>
        LineInTab(line, tab)
        && (search.Length == 0 || line.FormattedText.Contains(search, StringComparison.OrdinalIgnoreCase));

    // ── Mentions ─────────────────────────────────────────────────────────

    /// <summary>Only what other players say mentions you: chat and channel lines, never your own.</summary>
    internal static bool MentionEligible(string channel, int chatType) =>
        (channel == "Chat" || channel == "Channels") && chatType is not (4 or 9 or 11);

    private static string? _pumpName;
    private static (long Version, string Name, ChatRouter Router)? _pumpRouter;
    private static long _lastBeepTicks;

    [DllImport("user32.dll")] private static extern bool MessageBeep(uint uType);

    /// <summary>Your character's name, "" until known. Pump thread (reads the launch context once).</summary>
    internal static string PumpCharacterName()
    {
        string live = SessionStateRegistry.LastCharacterName;
        if (!string.IsNullOrWhiteSpace(live)) return live.Trim();
        return _pumpName ??= TryReadCharacterName() ?? "";
    }

    /// <summary>The router for the current rules and mention settings. Pump thread; rebuilt when they change.</summary>
    internal static ChatRouter PumpRouter(string name)
    {
        long version = FiltersVersion;
        var cached = _pumpRouter;
        if (cached is { } c && c.Version == version && c.Name == name) return c.Router;
        var router = new ChatRouter(Filters, BuildMentionRegex(name), name, countHits: true);
        _pumpRouter = (version, name, router);
        return router;
    }

    /// <summary>A router for the rule editor's test box (UI thread, on an edit).</summary>
    public static ChatRouter TestRouter()
    {
        string name = SessionStateRegistry.LastCharacterName;
        if (string.IsNullOrWhiteSpace(name)) name = _pumpName ?? "";
        return new ChatRouter(Filters, BuildMentionRegex(name.Trim()), name, countHits: false);
    }

    private static Regex? BuildMentionRegex(string name)
    {
        if (!MentionHighlight) return null;
        var words = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) words.Add(name.Trim());
        foreach (string w in (MentionWords ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!words.Contains(w, StringComparer.OrdinalIgnoreCase)) words.Add(w);
        if (words.Count == 0) return null;
        string alt = string.Join("|", words.Select(Regex.Escape));
        return new Regex($@"(?<![\w'])(?:{alt})(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(ChatFilterRule.TimeoutMs));
    }

    /// <summary>A new line mentions you. Pump thread.</summary>
    internal static void PumpMentionSound()
    {
        if (!MentionSound) return;
        long now = Environment.TickCount64;
        if (now - _lastBeepTicks < 3000) return;
        _lastBeepTicks = now;
        try { MessageBeep(0x40); } catch { }   // MB_ICONASTERISK; queued, returns at once
    }

    // ── Rule sets (import / export) ──────────────────────────────────────

    /// <summary>The rules and the created tabs as JSON, for the clipboard.</summary>
    public static string ExportRules()
    {
        var dto = new RynthChatRuleSetDto
        {
            Format = RynthChatRuleSetDto.FormatName,
            Tabs = Volatile.Read(ref _customTabs),
            Rules = Filters.Select(ToDto).ToArray(),
        };
        return JsonSerializer.Serialize(dto, RynthChatJsonContext.Default.RynthChatRuleSetDto);
    }

    /// <summary>
    /// Adds the rules in <paramref name="json"/> (an export, a whole
    /// rynthchat_settings.json, or a bare rule array) after the current ones.
    /// Returns a message for the user.
    /// </summary>
    public static string ImportRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "Clipboard is empty.";
        RynthChatFilterDto[]? rules = null;
        string[]? tabs = null;
        try
        {
            json = json.Trim();
            if (json.StartsWith('['))
                rules = JsonSerializer.Deserialize(json, RynthChatJsonContext.Default.RynthChatFilterDtoArray);
            else
            {
                var set = JsonSerializer.Deserialize(json, RynthChatJsonContext.Default.RynthChatRuleSetDto);
                rules = set?.Rules;
                tabs = set?.Tabs;
                if (rules == null)
                {
                    var settings = JsonSerializer.Deserialize(json, RynthChatJsonContext.Default.RynthChatSettingsDto);
                    rules = settings?.Filters;
                    tabs = settings?.CustomTabs;
                }
            }
        }
        catch (JsonException ex) { return "Not a rule set: " + ex.Message; }
        if (rules == null || rules.Length == 0) return "No rules found in the clipboard.";
        var imported = rules.Select(FromDto).ToList();
        foreach (string t in tabs ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(t)) EnsureCustomTab(t.Trim());
        foreach (ChatFilterRule r in imported)
            if (r.Tab.Length > 0 && r.Action is ChatRuleAction.Move or ChatRuleAction.Copy) EnsureCustomTab(r.Tab);
        EditFilters(list => list.AddRange(imported));
        return $"Imported {imported.Count} rule{(imported.Count == 1 ? "" : "s")}.";
    }

    private static RynthChatFilterDto ToDto(ChatFilterRule f) => new()
    {
        Enabled = f.Enabled,
        Pattern = f.Pattern,
        Tab = f.Tab,
        Action = f.Action switch
        {
            ChatRuleAction.Copy => "copy",
            ChatRuleAction.Hide => "hide",
            ChatRuleAction.Color => "color",
            _ => "move",
        },
        When = f.When.Length > 0 ? f.When : null,
        ColorMode = f.ColorMode switch { ChatColorMode.Line => "line", ChatColorMode.Match => "match", _ => null },
        // Kept while the rule is set to "No colour" too, so switching colour back on
        // after a relaunch finds the colour that was picked.
        Color = f.ColorMode != ChatColorMode.None || f.ColorArgb != ChatFilterRule.DefaultColor ? FormatColor(f.ColorArgb) : null,
        Mode = f.Mode == ChatMatchMode.Regex ? null : f.Mode.ToString(),
    };

    /// <summary>
    /// A rule from its JSON. Rules saved before 2026-09-29 have no "action": an
    /// empty tab hid the line and a tab moved it (while leaving it under All,
    /// which is why a "Junk" rule seemed to do nothing there; a Move now takes
    /// it out of All too).
    /// </summary>
    private static ChatFilterRule FromDto(RynthChatFilterDto f)
    {
        string tab = (f.Tab ?? "").Trim();
        var rule = new ChatFilterRule
        {
            Enabled = f.Enabled,
            Pattern = f.Pattern ?? "",
            Tab = tab,
            Action = (f.Action ?? "").ToLowerInvariant() switch
            {
                "move" => ChatRuleAction.Move,
                "copy" => ChatRuleAction.Copy,
                "hide" => ChatRuleAction.Hide,
                "color" or "colour" => ChatRuleAction.Color,
                _ => tab.Length == 0 ? ChatRuleAction.Hide : ChatRuleAction.Move,
            },
            When = f.When ?? "",
            ColorMode = (f.ColorMode ?? "").ToLowerInvariant() switch
            {
                "line" => ChatColorMode.Line,
                "match" or "text" => ChatColorMode.Match,
                _ => ChatColorMode.None,
            },
            ColorArgb = ParseColor(f.Color) ?? ChatFilterRule.DefaultColor,
            // Absent (older settings, upstream format): Regex.
            Mode = Enum.TryParse(f.Mode, ignoreCase: true, out ChatMatchMode mode) ? mode : ChatMatchMode.Regex,
        };
        rule.Recompile();
        return rule;
    }

    /// <summary>
    /// "#RRGGBB" or "#AARRGGBB" to 0xAARRGGBB (opaque when no alpha is given).
    /// An alpha of 00 is taken as opaque: a colour that makes the text invisible
    /// is never what a rule wants (hiding is the Hide action).
    /// </summary>
    public static uint? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimStart('#');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length is not (6 or 8)) return null;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.AllowHexSpecifier, null, out uint v)) return null;
        if (s.Length == 6 || (v >> 24) == 0) v |= 0xFF000000;
        return v;
    }

    /// <summary>0xAARRGGBB as "#RRGGBB", or "#AARRGGBB" when it isn't opaque (an imported colour keeps its alpha).</summary>
    public static string FormatColor(uint argb) =>
        (argb >> 24) == 0xFF ? $"#{argb & 0xFFFFFF:X6}" : $"#{argb:X8}";

    // ── Plugin binding (set by ChatSource on the pump) ───────────────────

    public static volatile bool PluginBound;
    private static IntPtr _sendLinePtr;
    public static IntPtr SendLinePtr
    {
        get => Volatile.Read(ref _sendLinePtr);
        set => Volatile.Write(ref _sendLinePtr, value);
    }

    // ── Shown ────────────────────────────────────────────────────────────

    private static int _shown;

    /// <summary>
    /// A chat face went on or off screen. Whether RynthChat is shown decides who
    /// owns Enter and whether the retail chatbox is hidden (ChatHooks.RynthChatOwnsChat).
    /// Counted, since the ImGui face and the popped-out Avalonia face hand over
    /// in either order. Any thread.
    /// </summary>
    public static void SetShown(bool shown)
    {
        int n = shown ? Interlocked.Increment(ref _shown) : Interlocked.Decrement(ref _shown);
        if (n < 0) { Interlocked.Exchange(ref _shown, 0); n = 0; }
        ChatHooks.ChatPanelShown = n > 0;
        if (n == 0 && Win32Backend.ChatCaptureActive)
        {
            // Abandon a half-typed line rather than keep swallowing keys for a closed panel.
            Win32Backend.ChatCaptureActive = false;
            SetInput("", 0, null);
        }
    }

    // ── Input line (AC's main thread: the WndProc hook) ──────────────────

    private sealed record InputState(string Text, int Cursor, string? Hint);
    private static InputState _input = new("", 0, null);
    private static readonly List<string> History = new();
    private static int _historyIndex = -1;
    private static long _inputVersion;
    private static bool _inputInstalled;

    public static string InputText => Volatile.Read(ref _input).Text;
    public static int InputCursor => Volatile.Read(ref _input).Cursor;
    /// <summary>Shown in the empty input box instead of the usual hint (e.g. "select an NPC first").</summary>
    public static string? InputHint => Volatile.Read(ref _input).Hint;
    public static long InputVersion => Interlocked.Read(ref _inputVersion);

    /// <summary>Raised on AC's main thread after the input line changes.</summary>
    public static event Action? InputChanged;

    private static void SetInput(string text, int cursor, string? hint)
    {
        Volatile.Write(ref _input, new InputState(text, Math.Clamp(cursor, 0, text.Length), hint));
        Interlocked.Increment(ref _inputVersion);
        try { InputChanged?.Invoke(); }
        catch (Exception ex) { RynthLog.UI($"ChatModel: InputChanged handler threw {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>Clicking the input box starts typing, like Enter. AC's main thread.</summary>
    public static void BeginTyping()
    {
        if (Win32Backend.ChatCaptureActive || !ChatHooks.RynthChatOwnsChat) return;
        Win32Backend.ChatCaptureActive = true;
        OnActivated();
    }

    /// <summary>Hooks the chat keys up to the input line. Once, at init.</summary>
    public static void InstallInput()
    {
        if (_inputInstalled) return;
        _inputInstalled = true;
        Win32Backend.OnChatCaptureActivated = OnActivated;
        Win32Backend.OnChatTellSelected = () => BeginTellToSelected();
        Win32Backend.OnChatChar = c =>
        {
            // Ctrl+Backspace arrives as WM_CHAR 0x7F (DEL): it used to go into the line
            // as an invisible character and be sent with it.
            if (c == (char)0x7F) return;
            InputState s = _input;
            _historyIndex = -1;
            SetInput(s.Text[..s.Cursor] + c + s.Text[s.Cursor..], s.Cursor + 1, null);
        };
        Win32Backend.OnChatBackspace = () =>
        {
            InputState s = _input;
            if (s.Cursor > 0) SetInput(s.Text[..(s.Cursor - 1)] + s.Text[s.Cursor..], s.Cursor - 1, null);
        };
        Win32Backend.OnChatDelete = () =>
        {
            InputState s = _input;
            if (s.Cursor < s.Text.Length) SetInput(s.Text[..s.Cursor] + s.Text[(s.Cursor + 1)..], s.Cursor, null);
        };
        Win32Backend.OnChatLeft = () => { InputState s = _input; SetInput(s.Text, s.Cursor - 1, null); };
        Win32Backend.OnChatRight = () => { InputState s = _input; SetInput(s.Text, s.Cursor + 1, null); };
        Win32Backend.OnChatHome = () => SetInput(_input.Text, 0, null);
        Win32Backend.OnChatEnd = () => SetInput(_input.Text, _input.Text.Length, null);
        Win32Backend.OnChatUp = () =>
        {
            if (History.Count == 0) return;
            _historyIndex = _historyIndex < 0 ? History.Count - 1 : Math.Max(0, _historyIndex - 1);
            string text = History[_historyIndex];
            SetInput(text, text.Length, null);
        };
        Win32Backend.OnChatDown = () =>
        {
            if (_historyIndex < 0) return;
            string text;
            if (_historyIndex < History.Count - 1) text = History[++_historyIndex];
            else { _historyIndex = -1; text = ""; }
            SetInput(text, text.Length, null);
        };
        Win32Backend.OnChatSend = OnSend;
        Win32Backend.OnChatCancel = () =>
        {
            _historyIndex = -1;
            SetInput("", 0, null);
        };
    }

    private static void OnActivated()
    {
        _historyIndex = -1;
        SetInput("", 0, null);
    }

    /// <summary>
    /// Starts "/tell &lt;selected name&gt;, " and keeps typing. AC's main thread
    /// (the Tell button via WM_RYNTHCORE_TELL, or "/tell" alone in OnSend), where
    /// the selection and its name read live.
    /// </summary>
    private static bool BeginTellToSelected()
    {
        if (!TryGetSelectedTellName(out string name))
        {
            SetInput("", 0, "Select an NPC or player in the game first, then Tell.");
            return false;
        }
        string text = $"/tell {name}, ";
        _historyIndex = -1;
        Win32Backend.ChatCaptureActive = true;   // keys now go to this line
        SetInput(text, text.Length, null);
        return true;
    }

    // Win32Backend has already cleared ChatCaptureActive. RynthChatSendLine runs
    // here, on AC's main thread, so its CallWindowProcA does too.
    private static unsafe void OnSend()
    {
        string text = _input.Text.Trim();
        // "/tell" or "/t" alone addresses the selected NPC or player instead of
        // being sent (AC would only answer with the usage line).
        if (text.Equals("/tell", StringComparison.OrdinalIgnoreCase) || text.Equals("/t", StringComparison.OrdinalIgnoreCase))
        {
            if (!BeginTellToSelected()) _historyIndex = -1;
            return;
        }
        _historyIndex = -1;
        if (text.Length > 0 && (History.Count == 0 || History[^1] != text))
        {
            History.Add(text);
            if (History.Count > 100) History.RemoveAt(0);
        }
        SetInput("", 0, null);
        IntPtr send = SendLinePtr;
        if (text.Length == 0 || send == IntPtr.Zero) return;
        IntPtr ptr = Marshal.StringToHGlobalAnsi(text);
        try { ((delegate* unmanaged[Cdecl]<IntPtr, void>)send)(ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>The creature selected in the game (an NPC or another player), for /tell.
    /// AC's main thread only. Never yourself, never an item.</summary>
    private static bool TryGetSelectedTellName(out string name)
    {
        name = "";
        uint id = SelectedTargetHooks.ReadCurrentSelectedId();
        if (id == 0 || id == ClientHelperHooks.GetPlayerId()) return false;
        const uint ItemTypeCreature = 0x00000010;
        if (ClientObjectHooks.TryGetItemType(id, out uint flags) && (flags & ItemTypeCreature) == 0) return false;
        return ClientObjectHooks.TryGetObjectName(id, out name) && !string.IsNullOrWhiteSpace(name);
    }

    // ── Clipboard ────────────────────────────────────────────────────────

    // Written directly as CF_UNICODETEXT: Avalonia's clipboard service is
    // unreliable inside acclient (no proper TopLevel owner).
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);

    public static unsafe bool SetClipboardText(string text)
    {
        const uint CF_UNICODETEXT = 13;
        const uint GMEM_MOVEABLE = 0x0002;
        // The clipboard is shared: another app may hold it briefly.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    int bytes = (text.Length + 1) * 2;
                    IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                    if (hMem == IntPtr.Zero) return false;
                    IntPtr dst = GlobalLock(hMem);
                    if (dst == IntPtr.Zero) { GlobalFree(hMem); return false; }
                    fixed (char* src = text)
                        Buffer.MemoryCopy(src, (void*)dst, bytes, text.Length * 2);
                    ((char*)dst)[text.Length] = '\0';
                    GlobalUnlock(hMem);
                    if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                    {
                        GlobalFree(hMem);   // ownership not taken
                        return false;
                    }
                    return true;            // the system owns hMem now
                }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(10);
        }
        return false;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr hMem);

    /// <summary>The clipboard's text, or null (empty, not text, or busy). Rule import.</summary>
    public static unsafe string? GetClipboardText()
    {
        const uint CF_UNICODETEXT = 13;
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            IntPtr p = GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try
            {
                int maxChars = (int)Math.Min((ulong)GlobalSize(h) / 2, 1_000_000);
                int n = 0;
                char* c = (char*)p;
                while (n < maxChars && c[n] != '\0') n++;
                return new string(c, 0, n);
            }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>"Copied N lines", or the busy message.</summary>
    public static string CopyLines(IReadOnlyList<string> lines)
    {
        bool ok = SetClipboardText(string.Join("\r\n", lines));
        return ok ? $"Copied {lines.Count} line{(lines.Count == 1 ? "" : "s")}" : "Clipboard busy — try again";
    }

    // ── Chat log (pump thread, ChatSource) ───────────────────────────────

    private static string? _logCharacterName;
    private static StreamWriter? _logWriter;

    /// <summary>Writes <paramref name="lines"/> when logging is on; closes the file when it's off.</summary>
    internal static void PumpLog(List<ChatLine>? lines)
    {
        if (!LogEnabled)
        {
            if (_logWriter != null) { try { _logWriter.Close(); } catch { } _logWriter = null; }
            return;
        }
        if (lines == null || lines.Count == 0) return;
        try
        {
            // The character playing now: after logging in as another character in the
            // same client, the log used to carry on in the first one's file.
            string name = PumpCharacterName();
            if (name.Length == 0) return;
            if (_logWriter != null && !string.Equals(name, _logCharacterName, StringComparison.Ordinal))
            {
                try { _logWriter.Close(); } catch { }
                _logWriter = null;
            }
            if (_logWriter == null)
            {
                _logCharacterName = name;
                string file = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "ChatLogs");
                Directory.CreateDirectory(dir);
                // Shared for writing: an engine generation left behind by a hot reload
                // (it can't always close the file) must not lock the next one out.
                var stream = new FileStream(Path.Combine(dir, $"{file}.log"), FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _logWriter = new StreamWriter(stream, System.Text.Encoding.UTF8);
            }
            foreach (ChatLine line in lines) _logWriter.WriteLine(line.FormattedText);
            _logWriter.Flush();
        }
        catch (Exception ex)
        {
            RynthLog.UI($"ChatModel: chat log write failed: {ex.Message}");
            try { _logWriter?.Close(); } catch { }
            _logWriter = null;
        }
    }

    /// <summary>
    /// Closes the chat log. Engine shutdown, after the tick pump (PumpLog's
    /// thread) has stopped: the file stayed open in the old generation after a
    /// hot reload, and the new one couldn't open it (every write failed).
    /// </summary>
    internal static void CloseLog()
    {
        StreamWriter? w = _logWriter;
        _logWriter = null;
        try { w?.Close(); } catch { }
    }

    private static string? TryReadCharacterName()
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore");
            string perProcPath = Path.Combine(root, "launch_contexts", $"launch_context_{Environment.ProcessId}.json");
            string path = File.Exists(perProcPath) ? perProcPath : Path.Combine(root, "launch_context.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.TryGetProperty("TargetCharacter", out JsonElement tc))
            {
                string? name = tc.GetString();
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            return null;
        }
        catch { return null; }
    }

    // ── Settings persistence ─────────────────────────────────────────────
    //
    // %AppData%\RynthCore\rynthchat_settings.json. Version 2 (2026-09-29) adds
    // "version", showTimestamps, the mention settings, and per rule "action",
    // "when", "colorMode" and "color". A version-1 file loads as before (see
    // FromDto for what its rules become); the first load copies it to
    // rynthchat_settings.v1.bak, and the next save writes version 2.

    public const int SettingsVersion = 2;
    private static readonly object SaveLock = new();
    private static int _settingsLoaded;

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "rynthchat_settings.json");

    /// <summary>Loads the settings (incl. "Hide retail chat") once. Safe before the overlay starts.</summary>
    public static void EnsureSettingsLoaded()
    {
        if (Interlocked.Exchange(ref _settingsLoaded, 1) != 0) return;
        LoadSettings();
    }

    private static void LoadSettings()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path)) return;
            var dto = JsonSerializer.Deserialize(File.ReadAllText(path), RynthChatJsonContext.Default.RynthChatSettingsDto);
            if (dto == null) return;

            if (dto.Version < SettingsVersion)
            {
                try
                {
                    string backup = Path.ChangeExtension(path, $".v{Math.Max(1, dto.Version)}.bak");
                    if (!File.Exists(backup)) File.Copy(path, backup);
                }
                catch (Exception ex) { RynthLog.UI($"ChatModel: settings backup failed: {ex.Message}"); }
                RynthLog.UI($"ChatModel: migrating rynthchat_settings.json v{Math.Max(1, dto.Version)} -> v{SettingsVersion} ({dto.Filters?.Length ?? 0} rules).");
            }

            FontSize = (float)Math.Clamp(dto.FontSize <= 0 ? 10 : dto.FontSize, 8, 18);
            BackgroundAlpha = Math.Clamp(dto.BackgroundAlpha, 0, 255);
            AutoScroll = dto.AutoScroll;
            ChatHooks.SuppressOriginalChat = dto.SuppressChat;
            LogEnabled = dto.LogEnabled;
            CtrlGatedClickThrough = dto.CtrlGatedClickThrough;
            AvaloniaOverlay.ChatCtrlGatedClickThrough = dto.CtrlGatedClickThrough;
            ShowTimestamps = dto.ShowTimestamps;
            MentionHighlight = dto.MentionHighlight;
            MentionSound = dto.MentionSound;
            MentionWords = dto.MentionWords ?? "";

            var tabs = new List<string>();
            foreach (string t in dto.CustomTabs ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(t) && !IsBaseTab(t.Trim())
                    && !tabs.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase)) tabs.Add(t.Trim());
            var rules = new List<ChatFilterRule>();
            foreach (RynthChatFilterDto f in dto.Filters ?? Array.Empty<RynthChatFilterDto>())
            {
                ChatFilterRule rule = FromDto(f);
                rules.Add(rule);
                // A tab typed into a rule used to exist only while the rule was on:
                // make it a real tab, so turning the rule off doesn't drop the tab.
                if (rule.Action is ChatRuleAction.Move or ChatRuleAction.Copy && rule.Tab.Length > 0
                    && !IsBaseTab(rule.Tab) && !tabs.Contains(rule.Tab, StringComparer.OrdinalIgnoreCase))
                    tabs.Add(rule.Tab);
            }
            foreach (RynthChatPresetDto p in dto.Presets ?? Array.Empty<RynthChatPresetDto>())
            {
                var preset = p.Id == null ? null : RynthCore.Engine.UI.Panels.RynthChatPresets.Get(p.Id);
                if (preset == null) continue;   // a preset a later build removed
                preset.Enabled = p.Enabled;
                preset.Tab = (p.Tab ?? "").Trim();
                if (preset.Tab.Length > 0 && !IsBaseTab(preset.Tab)
                    && !tabs.Contains(preset.Tab, StringComparer.OrdinalIgnoreCase))
                    tabs.Add(preset.Tab);
            }
            _customTabs = tabs.ToArray();
            _filters = rules.ToArray();
            Interlocked.Increment(ref _filtersVersion);

            if (!string.IsNullOrEmpty(dto.ActiveChannel) && AllTabs().Contains(dto.ActiveChannel, StringComparer.OrdinalIgnoreCase))
                ActiveChannel = dto.ActiveChannel;
        }
        catch (Exception ex)
        {
            RynthLog.UI($"ChatModel: settings load failed: {ex.Message}");
            KeepUnreadable();
        }
    }

    /// <summary>
    /// The settings file didn't load: keep a copy before the next save writes the
    /// defaults over it, so the rules and tabs in it can be recovered (Import
    /// takes a whole settings file).
    /// </summary>
    private static void KeepUnreadable()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path)) return;
            string copy = Path.ChangeExtension(path, $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(path, copy, overwrite: false);
            RynthLog.UI($"ChatModel: kept the unreadable settings as {Path.GetFileName(copy)}.");
        }
        catch (Exception ex) { RynthLog.UI($"ChatModel: couldn't keep the unreadable settings: {ex.Message}"); }
    }

    /// <summary>Saves in the background (a rule edit saves on every keystroke). Any thread.</summary>
    public static void SaveSettings()
    {
        var dto = new RynthChatSettingsDto
        {
            Version = SettingsVersion,
            FontSize = FontSize,
            BackgroundAlpha = BackgroundAlpha,
            AutoScroll = AutoScroll,
            SuppressChat = ChatHooks.SuppressOriginalChat,
            LogEnabled = LogEnabled,
            CtrlGatedClickThrough = CtrlGatedClickThrough,
            ActiveChannel = ActiveChannel,
            ShowTimestamps = ShowTimestamps,
            MentionHighlight = MentionHighlight,
            MentionSound = MentionSound,
            MentionWords = MentionWords,
            CustomTabs = Volatile.Read(ref _customTabs),
            Filters = Filters.Select(ToDto).ToArray(),
            // Only presets the user touched: new presets in later builds start off.
            Presets = RynthCore.Engine.UI.Panels.RynthChatPresets.All
                .Where(p => p.Enabled || p.Tab.Length > 0)
                .Select(p => new RynthChatPresetDto { Id = p.Id, Enabled = p.Enabled, Tab = p.Tab.Length > 0 ? p.Tab : null })
                .ToArray(),
        };
        UiBackgroundWriter.Enqueue("persist chat settings", () =>
        {
            lock (SaveLock)
            {
                // Written beside it and swapped in: the client dying mid-write (a rule
                // edit saves on every keystroke) used to leave a cut-off file, which
                // loaded as nothing and was then saved over with the defaults.
                string path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = $"{path}.{Environment.ProcessId}.tmp";   // every client shares the file
                try
                {
                    File.WriteAllText(tmp, JsonSerializer.Serialize(dto, RynthChatJsonContext.Default.RynthChatSettingsDto));
                    File.Move(tmp, path, overwrite: true);
                }
                catch
                {
                    try { File.Delete(tmp); } catch { }
                    throw;   // UiBackgroundWriter logs it
                }
            }
        });
    }
}

// JsonSerializerContext must be at namespace scope (not nested) for source generation.
internal sealed class RynthChatLineDto
{
    [JsonPropertyName("seq")]    public ulong Seq      { get; set; }
    [JsonPropertyName("ts")]     public string Ts      { get; set; } = "";
    [JsonPropertyName("chan")]   public string Chan    { get; set; } = "";
    /// <summary>ChatMessageType; RynthChat before 0.2.0 doesn't send it.</summary>
    [JsonPropertyName("type")]   public int Type       { get; set; } = -1;
    [JsonPropertyName("sender")] public string? Sender { get; set; }
    [JsonPropertyName("text")]   public string Text    { get; set; } = "";
}

// Property names match the legacy hand-written JSON so existing settings load.
internal sealed class RynthChatFilterDto
{
    [JsonPropertyName("pattern")] public string? Pattern { get; set; }
    [JsonPropertyName("tab")]     public string? Tab     { get; set; }
    [JsonPropertyName("enabled")] public bool    Enabled { get; set; } = true;
    // Version 2:
    /// <summary>"move" | "copy" | "hide" | "color"; absent in version 1 (see ChatModel.FromDto).</summary>
    [JsonPropertyName("action")]    public string? Action    { get; set; }
    /// <summary>"" / absent: any line; "chan:Combat"; "type:3".</summary>
    [JsonPropertyName("when")]      public string? When      { get; set; }
    /// <summary>"line" | "match"; absent: no colour.</summary>
    [JsonPropertyName("colorMode")] public string? ColorMode { get; set; }
    /// <summary>"#RRGGBB".</summary>
    [JsonPropertyName("color")]     public string? Color     { get; set; }
    /// <summary>ChatMatchMode name; absent = Regex.</summary>
    [JsonPropertyName("mode")]      public string? Mode      { get; set; }
}

/// <summary>A standard filter's user state (RynthChatPresets), by preset id.</summary>
internal sealed class RynthChatPresetDto
{
    [JsonPropertyName("id")]      public string? Id      { get; set; }
    [JsonPropertyName("enabled")] public bool    Enabled { get; set; }
    [JsonPropertyName("tab")]     public string? Tab     { get; set; }
}

internal sealed class RynthChatSettingsDto
{
    /// <summary>Absent (0) in version-1 files.</summary>
    [JsonPropertyName("version")]         public int    Version         { get; set; }
    [JsonPropertyName("fontSize")]        public double FontSize        { get; set; } = 10;
    [JsonPropertyName("backgroundAlpha")] public int    BackgroundAlpha { get; set; } = 0xF2;
    [JsonPropertyName("autoScroll")]      public bool   AutoScroll      { get; set; } = true;
    [JsonPropertyName("suppressChat")]    public bool   SuppressChat    { get; set; }
    [JsonPropertyName("logEnabled")]      public bool   LogEnabled      { get; set; }
    // Chat's own Ctrl-gated click-through, like RadarPanel's (2026-09-02).
    [JsonPropertyName("ctrlGatedClickThrough")] public bool CtrlGatedClickThrough { get; set; }
    [JsonPropertyName("activeChannel")]   public string? ActiveChannel  { get; set; }
    [JsonPropertyName("showTimestamps")]  public bool   ShowTimestamps  { get; set; } = true;
    [JsonPropertyName("mentionHighlight")] public bool  MentionHighlight { get; set; } = true;
    [JsonPropertyName("mentionSound")]    public bool   MentionSound    { get; set; }
    [JsonPropertyName("mentionWords")]    public string? MentionWords   { get; set; }
    [JsonPropertyName("customTabs")]      public string[]? CustomTabs   { get; set; }
    [JsonPropertyName("filters")]         public RynthChatFilterDto[]? Filters { get; set; }
    [JsonPropertyName("presets")]         public RynthChatPresetDto[]? Presets { get; set; }
}

/// <summary>Export / import on the clipboard (the rule editor's Export and Import).</summary>
internal sealed class RynthChatRuleSetDto
{
    public const string FormatName = "rynthchat-rules/2";
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("tabs")]   public string[]? Tabs { get; set; }
    [JsonPropertyName("rules")]  public RynthChatFilterDto[]? Rules { get; set; }
}

[JsonSerializable(typeof(RynthChatLineDto[]))]
[JsonSerializable(typeof(RynthChatLineDto))]
[JsonSerializable(typeof(RynthChatSettingsDto))]
[JsonSerializable(typeof(RynthChatRuleSetDto))]
[JsonSerializable(typeof(RynthChatFilterDto[]))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class RynthChatJsonContext : JsonSerializerContext { }
