// ============================================================================
//  RynthCore.Engine - UI/Data/ChatReplyTab.cs
//  Shows the reply to a command typed in RynthChat's box in the tab it was typed in.
//
//  The server's reply to a /command is system text (ACE's ChatMessageType.Broadcast)
//  and a plugin's reply is "[Rynth...]" output, so RynthChat files them under System
//  and Rynth: they showed only in All and those tabs. Silentkelpie (2026-10-24) typed
//  /fship on another tab and saw nothing: the seven "[FSHIP]: use /fship ..." lines
//  were under All. A slash command sent from the box now marks this client's tab for
//  WindowMs; System and Rynth lines arriving in that time also show in it.
//
//  Not for chat (/say, /tell, /f, /a, emotes: those land in Chat as typed), from All
//  (it shows everything), or for automated lines (a plugin's own polls, the login burst).
//  Pure: tools/ChatFilterTests compiles this file. NoteSent runs on AC's main thread,
//  TabFor on the pump thread: the window is one immutable record swapped in whole.
// ============================================================================

using System;
using System.Threading;

namespace RynthCore.Engine.UI.Data;

internal sealed class ChatReplyTab
{
    /// <summary>How long after a command its replies also go to the tab it was typed in.</summary>
    public const long WindowMs = 5000;

    /// <summary>Channels a command's reply arrives in (RynthChat's ChatClassifier): the server's, a plugin's.</summary>
    public const string SystemChannel = "System", RynthChannel = "Rynth";

    // Commands that are chat: AC files what they say under Chat (or a channel) already.
    private static readonly string[] ChatVerbs =
    {
        "/say", "/s", "/tell", "/t", "/reply", "/r", "/retell", "/rt", "/f", "/a",
        "/emote", "/e", "/me", "/em", "/general", "/trade", "/lfg", "/roleplay", "/society",
    };

    private sealed record Window(string Tab, long UntilMs);
    private Window? _window;

    /// <summary>A slash command that isn't chat: "/fship", "/bank d", "/ra hub", "@help".</summary>
    public static bool IsCommand(string? text)
    {
        string t = (text ?? "").Trim();
        if (t.Length < 2 || (t[0] != '/' && t[0] != '@')) return false;
        int sp = t.IndexOf(' ');
        string verb = "/" + (sp < 0 ? t[1..] : t[1..sp]);
        foreach (string chat in ChatVerbs)
            if (verb.Equals(chat, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>The player sent <paramref name="text"/> from <paramref name="tab"/>. AC's main thread.</summary>
    public void NoteSent(string? text, string tab, long nowMs)
    {
        if (!IsCommand(text) || string.IsNullOrEmpty(tab) || tab.Equals("All", StringComparison.OrdinalIgnoreCase))
            return;
        Volatile.Write(ref _window, new Window(tab, nowMs + WindowMs));
    }

    /// <summary>
    /// The tab a line arriving now also shows in, or null. Only System and Rynth lines, never an
    /// automated one, and not when the command was typed in the line's own channel tab.
    /// </summary>
    public string? TabFor(string channel, bool automated, long nowMs)
    {
        Window? w = Volatile.Read(ref _window);
        if (w == null || automated || nowMs > w.UntilMs) return null;
        if (!channel.Equals(SystemChannel, StringComparison.OrdinalIgnoreCase)
            && !channel.Equals(RynthChannel, StringComparison.OrdinalIgnoreCase)) return null;
        return channel.Equals(w.Tab, StringComparison.OrdinalIgnoreCase) ? null : w.Tab;
    }
}
