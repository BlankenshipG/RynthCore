// ============================================================================
//  RynthCore.Engine - Compatibility/ChatLinkProbe.cs
//
//  A temporary probe to learn how AC reports a click on a link in its own chat window,
//  so map coordinates there can become RynthNav links natively (RynthChat already has
//  them; AC's window has had none without Decal).
//
//  /rc chatlinkprobe [seconds]  (default 60, at most 600; "off" ends it)
//    - coordinates in AC's chat are printed as AC's clickable-name markup,
//      <Tell:IIDString:110011:42.1N, 33.6E>42.1N, 33.6E<\Tell> (ChatCallbackHooks prints
//      the linked text; plugins and RynthChat still get the line as the server sent it);
//    - every message gmMainChatUI::ListenToElementMessage receives is logged (ChatHooks):
//      its first dwords and any text those point at, so a click on one of those links
//      (and on a player's name, to compare) shows which message carries it.
//  Nothing changes while it is off.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.Compatibility;

internal static unsafe class ChatLinkProbe
{
    /// <summary>The link id coordinates carry (GoArrow's and UtilityBelt's).</summary>
    public const int CoordLinkId = 110011;

    private const int DefaultSeconds = 60;
    private const int MaxSeconds = 600;
    /// <summary>Messages logged per probe run (mouse moves can arrive every frame).</summary>
    private const int MaxLogged = 400;
    private const int DwordsLogged = 12;
    private const int MaxTextChars = 80;

    private static long _untilMs;
    private static int _logged;
    private static int _linked;

    /// <summary>The probe is running (coordinates linked, chat UI messages logged). Any thread.</summary>
    public static bool Active => Environment.TickCount64 < Volatile.Read(ref _untilMs);

    /// <summary>/rc chatlinkprobe [seconds|off]: what to tell the player.</summary>
    public static string Command(string arg)
    {
        arg = (arg ?? "").Trim();
        if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            Volatile.Write(ref _untilMs, 0);
            RynthLog.Compat("ChatLinkProbe: stopped.");
            return "Chat link probe off.";
        }
        int seconds = int.TryParse(arg, out int s) ? Math.Clamp(s, 5, MaxSeconds) : DefaultSeconds;
        _logged = 0;
        _linked = 0;
        Volatile.Write(ref _untilMs, Environment.TickCount64 + seconds * 1000L);
        RynthLog.Compat($"ChatLinkProbe: on for {seconds} s - coordinates in AC's chat are links; gmMainChatUI messages are logged.");
        return $"Chat link probe on for {seconds} s: new coordinates in AC's chat window are links. Click one, then click a player's name, then wait for it to end.";
    }

    /// <summary>
    /// <paramref name="raw"/> with each coordinate pair outside markup wrapped as an AC chat link
    /// (CoordLinkId), or null when there is nothing to wrap or the probe is off.
    /// </summary>
    public static string? LinkCoords(string? raw)
    {
        if (!Active || string.IsNullOrEmpty(raw) || raw.Contains("IIDString:" + CoordLinkId + ":", StringComparison.Ordinal))
            return null;
        List<ChatCoord> coords = ChatCoords.Find(raw);
        if (coords.Count == 0) return null;
        var sb = new StringBuilder(raw.Length + coords.Count * 40);
        int at = 0;
        foreach (ChatCoord c in coords)
        {
            if (InsideMarkup(raw, c.Start)) continue;
            string shown = raw.Substring(c.Start, c.Length);
            sb.Append(raw, at, c.Start - at);
            sb.Append("<Tell:IIDString:").Append(CoordLinkId).Append(':').Append(c.Text).Append('>').Append(shown).Append("<\\Tell>");
            at = c.Start + c.Length;
        }
        if (at == 0) return null;
        sb.Append(raw, at, raw.Length - at);
        string linked = sb.ToString();
        if (Interlocked.Increment(ref _linked) <= 10)
            RynthLog.Compat($"ChatLinkProbe: linked '{Cut(linked, 160)}'");
        return linked;
    }

    /// <summary>Inside a tag, or inside a link's shown text: AC's own markup is never rewritten.</summary>
    private static bool InsideMarkup(string text, int at)
    {
        int open = text.LastIndexOf('<', at);
        if (open >= 0 && text.IndexOf('>', open) >= at) return true;
        int tellOpen = text.LastIndexOf("<Tell:", at, StringComparison.Ordinal);
        if (tellOpen < 0) return false;
        int tellClose = text.IndexOf("<\\Tell>", tellOpen, StringComparison.Ordinal);
        return tellClose < 0 || tellClose >= at;
    }

    /// <summary>
    /// One message to gmMainChatUI::ListenToElementMessage (AC's main thread, before the original):
    /// logged while the probe runs. Reads only memory that probes as accessible; never throws.
    /// </summary>
    public static void OnChatUiMessage(IntPtr thisPtr, IntPtr msgInfo)
    {
        if (!Active || msgInfo == IntPtr.Zero) return;
        if (Interlocked.Increment(ref _logged) > MaxLogged) return;
        try
        {
            var sb = new StringBuilder(256);
            sb.Append($"ChatLinkProbe: msg this=0x{thisPtr.ToInt32():X8} info=0x{msgInfo.ToInt32():X8}");
            var texts = new StringBuilder();
            for (int i = 0; i < DwordsLogged; i++)
            {
                IntPtr at = msgInfo + i * 4;
                if (!ClientObjectHooks.IsReadablePointer(at)) break;
                int v = Marshal.ReadInt32(at);
                sb.Append($" [{i * 4:X2}]={v:X8}");
                string? t = TextAt(new IntPtr(v));
                if (t != null) texts.Append($" [{i * 4:X2}]->\"{t}\"");
            }
            RynthLog.Compat(sb.ToString() + (texts.Length > 0 ? " |" + texts : ""));
        }
        catch { }
    }

    /// <summary>Printable text at <paramref name="p"/> (wide, a PStringBase's buffer, or ASCII), or null.</summary>
    private static string? TextAt(IntPtr p)
    {
        if (p.ToInt64() < 0x10000 || !ClientObjectHooks.IsReadablePointer(p)) return null;
        string? wide = Wide(p);
        if (wide != null) return "w:" + wide;
        IntPtr inner = Marshal.ReadIntPtr(p);
        if (inner.ToInt64() >= 0x10000 && ClientObjectHooks.IsReadablePointer(inner))
        {
            string? viaPtr = Wide(inner);
            if (viaPtr != null) return "*w:" + viaPtr;
        }
        string? ascii = Ascii(p);
        return ascii != null ? "a:" + ascii : null;
    }

    private static string? Wide(IntPtr p)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < MaxTextChars; i++)
        {
            IntPtr c = p + i * 2;
            if (!ClientObjectHooks.IsReadablePointer(c)) break;
            char ch = (char)Marshal.ReadInt16(c);
            if (ch == '\0') break;
            if (ch < 0x20 || ch > 0x7E) return null;
            sb.Append(ch);
        }
        return sb.Length >= 3 ? sb.ToString() : null;
    }

    private static string? Ascii(IntPtr p)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < MaxTextChars; i++)
        {
            IntPtr c = p + i;
            if (!ClientObjectHooks.IsReadablePointer(c)) break;
            byte b = Marshal.ReadByte(c);
            if (b == 0) break;
            if (b < 0x20 || b > 0x7E) return null;
            sb.Append((char)b);
        }
        return sb.Length >= 3 ? sb.ToString() : null;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "...";
}
