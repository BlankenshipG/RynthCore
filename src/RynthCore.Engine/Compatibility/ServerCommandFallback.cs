// ============================================================================
//  RynthCore.Engine - Compatibility/ServerCommandFallback.cs
//  A slash command with no captured chat target goes to the server as "@command".
//
//  ChatCommandDispatcher.DispatchRaw sends a "/command" through AC's own outgoing-chat
//  function, using the chat manager AC passed the last time a line went through the chat
//  bar (ChatCallbackHooks.TryDispatchDirect). Until that happens in a login it typed the
//  line into AC's window with simulated keys. Silentkelpie (2026-10-30): /fship list typed in
//  RynthChat showed nowhere on two of five clients. Their flight recorders show no Talk sent
//  at all, while the dispatch reported ok; the other three sent it and got the reply.
//  A server command (anything AC doesn't handle itself) now goes out as "@command" through
//  Event_Talk, which needs no captured target: ACE runs "@x" the same as "/x".
//  Pure: tools/ChatFilterTests compiles this file.
// ============================================================================

using System;

namespace RynthCore.Engine.Compatibility;

internal static class ServerCommandFallback
{
    /// <summary>Commands the AC client runs itself (never sent to the server): these still need the keystrokes.</summary>
    private static readonly string[] ClientCommands =
    {
        "/framerate", "/render", "/quit", "/exit", "/logout", "/logoff", "/clear", "/cls", "/filter", "/config",
        "/fillcomps", "/hide", "/chatlog", "/screenshot", "/ss", "/camera", "/loc", "/help", "/h",
        "/die", "/lifestone", "/marketplace", "/house", "/emotes", "/soundtrack", "/music",
        "/f", "/a", "/p", "/v", "/c", "/m", "/fellowship", "/allegiance", "/patron", "/vassals",
        "/covassals", "/monarch", "/say", "/s", "/tell", "/t", "/reply", "/r", "/retell", "/rt",
        "/emote", "/e", "/me", "/em", "/general", "/trade", "/lfg", "/roleplay", "/society", "/olthoi",
        "/cg", "/ct", "/clfg", "/crp", "/cs", "/co",
    };

    /// <summary>
    /// "/fship list" → "@fship list". False for a client command, a line that isn't a slash
    /// command, or one with characters ACE's Talk can't carry.
    /// </summary>
    public static bool TryAsServerCommand(string? command, out string? atCommand)
    {
        atCommand = null;
        string t = (command ?? "").Trim();
        if (t.Length < 2 || t[0] != '/' || t.Length > 200) return false;
        foreach (char c in t)
            if (c < 0x20 || c > 0x7E) return false;
        int sp = t.IndexOf(' ');
        string verb = sp < 0 ? t : t[..sp];
        if (verb.Length < 2 || !char.IsLetter(verb[1])) return false;
        foreach (string client in ClientCommands)
            if (verb.Equals(client, StringComparison.OrdinalIgnoreCase)) return false;
        atCommand = "@" + t.Substring(1);
        return true;
    }
}
