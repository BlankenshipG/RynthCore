// ============================================================================
//  RynthCore.Engine - UI/Data/Discord/ChatDiscordLogic.cs
//  The relay's per-line decisions, kept free of engine state so
//  tools/ChatDiscordTests can run them: what a chat line is (speech on a
//  channel, or plain text), whether a route wants it and under which label,
//  how it is written for Discord, and whether it is scrollback from before
//  the relay started.
// ============================================================================

using System;
using System.Collections.Generic;

namespace RynthCore.Engine.UI.Data.Discord;

/// <summary>A chat line split for the relay. <see cref="Speech"/> null: not player speech.</summary>
internal readonly record struct DiscordLineParts(SpeechChannel? Speech, string Speaker, string Message, bool Own)
{
    /// <summary>"Speaker message" (or the message alone): what route keywords are matched against.</summary>
    public string Haystack => Speaker.Length > 0 ? Speaker + " " + Message : Message;
}

internal static class ChatDiscordLogic
{
    /// <summary>
    /// Splits <paramref name="text"/> (AC's name links already stripped). Player speech gets its
    /// channel and speaker (yours as <paramref name="me"/> when known); anything else is the text
    /// alone. False for blank lines.
    /// </summary>
    public static bool TryClassify(string? text, int chatType, string me, out DiscordLineParts parts)
    {
        parts = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (ChatSpeech.TryParse(text, chatType, out var ch, out string who, out string msg, out bool own))
        {
            parts = new DiscordLineParts(ch, own && me.Length > 0 ? me : who, msg, own);
            return true;
        }
        string plain = ChatSpeech.Decode(text).Trim();
        if (plain.Length == 0) return false;
        parts = new DiscordLineParts(null, string.Empty, plain, false);
        return true;
    }

    /// <summary>
    /// True when <paramref name="route"/> takes the line: its speech channel is ticked, or the line
    /// shows in one of the route's tabs (<paramref name="showsInTab"/>). <paramref name="label"/> is
    /// what the Discord line is tagged with: the speech channel, else the tab that matched.
    /// </summary>
    public static bool Wants(DiscordRoute route, SpeechChannel? speech, Func<string, bool> showsInTab, out string label)
    {
        label = string.Empty;
        if (speech is { } ch && route.RelaysSpeech(ch))
        {
            label = ChatSpeech.Name(ch);
            return true;
        }
        foreach (string tab in route.Tabs)
        {
            if (!showsInTab(tab)) continue;
            label = speech is { } c ? ChatSpeech.Name(c) : tab;
            return true;
        }
        return false;
    }

    /// <summary>The Discord text for one relayed line.</summary>
    public static string Format(ChatDiscordSettings s, DateTime time, in DiscordLineParts parts, string label, string me)
        => DiscordFormat.FormatLine(time, label.Length > 0 ? label : null,
            parts.Speech != null && parts.Speaker.Length > 0 ? parts.Speaker : null,
            parts.Message, s.IncludeTimestamp, s.TagCharacter && me.Length > 0 ? me : null);

    /// <summary>
    /// True for a line printed before the relay started (the scrollback a fresh engine, or a hot
    /// reload, reads in its first poll): those were sent already or are history, not news. Stamps
    /// only have whole seconds, so the line's second counts as "after".
    /// </summary>
    public static bool IsBacklog(DateTime lineTime, DateTime activeSince)
        => lineTime < activeSince.AddTicks(-(activeSince.Ticks % TimeSpan.TicksPerSecond));

    /// <summary>Key for the per-route repeat filter (the same line twice within a few seconds).</summary>
    public static string DedupKey(in DiscordLineParts parts, string label) => $"{label}|{parts.Speaker}|{parts.Message}";

    /// <summary>Drops dedup entries older than <paramref name="windowMs"/>.</summary>
    public static void PruneRecent(Dictionary<string, long> recent, long now, int windowMs)
    {
        List<string>? stale = null;
        foreach (var kv in recent)
            if (now - kv.Value > windowMs) (stale ??= new List<string>()).Add(kv.Key);
        if (stale != null) foreach (string k in stale) recent.Remove(k);
    }
}
