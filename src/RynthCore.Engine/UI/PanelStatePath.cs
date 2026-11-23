// ============================================================================
//  RynthCore.Engine - UI/PanelStatePath.cs
//  Which panel_state file a client keeps its window layout in (PanelStateStore).
//
//  Until 2026-10-24 every client on the machine shared one panel_state.txt and
//  rewrote all of it whenever any window moved, opened or closed, so each client's
//  windows came back (after a restart or an engine reload) wherever the client that
//  saved last had its own: Silentkelpie's chat window kept "resetting". Each
//  character now keeps its own layout:
//
//    panel_state.<server>.<character>.txt   the character the launcher started
//    panel_state.txt                        no launch context (started outside the
//                                           launcher): the shared file, as before; also
//                                           the starting layout of a character's first run
//
//  The character is the launch context's TargetCharacter for this process
//  (%AppData%\RynthCore\launch_contexts\launch_context_<pid>.json), known before any
//  window is placed. Pure (no engine state): tools/PanelStateTests compiles this file.
// ============================================================================

using System;
using System.Text;
using System.Text.Json;

namespace RynthCore.Engine.UI;

internal static class PanelStatePath
{
    /// <summary>The layout every client shared before 2026-10-24; a character's first-run start.</summary>
    public const string SharedFileName = "panel_state.txt";

    /// <summary>A name part in a file name is cut to this many characters.</summary>
    public const int MaxPartLength = 48;

    /// <summary>
    /// "panel_state.<server>.<character>.txt" (lower case, unsafe characters as "_"), or the
    /// shared file name when the character isn't known. A blank server is left out.
    /// </summary>
    public static string FileNameFor(string? server, string? character)
    {
        string c = Part(character);
        if (c.Length == 0) return SharedFileName;
        string s = Part(server);
        return s.Length == 0 ? $"panel_state.{c}.txt" : $"panel_state.{s}.{c}.txt";
    }

    /// <summary>The launch context's ServerName and TargetCharacter ("" when absent or unreadable).</summary>
    public static (string Server, string Character) ParseLaunchContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ("", "");
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ("", "");
            return (Str(root, "ServerName"), Str(root, "TargetCharacter"));
        }
        catch (JsonException)
        {
            return ("", "");
        }
    }

    private static string Str(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>Letters and digits kept (lower case), anything else one "_", trimmed of "_".</summary>
    private static string Part(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text.Trim())
        {
            if (ch < 128 && char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        string part = sb.ToString().Trim('_');
        return part.Length > MaxPartLength ? part[..MaxPartLength].TrimEnd('_') : part;
    }
}
