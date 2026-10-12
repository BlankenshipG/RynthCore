// ============================================================================
//  RynthCore.Engine - UI/ServerMenus.cs
//  Server sections: the bottom half of the RynthAi dashboard's Char menu and of
//  the Mini Remote's right-click menu (the top half, "Character", is the
//  windows every server has).
//
//  A server (Infinite Leaftide today) is defined once: how to tell the client
//  is on it, and the command that forces it. Each installed plugin with windows
//  for that server adds its items (EntryPoint, by which plugin DLLs exist). A
//  section shows only while its server is detected or forced, and only with at
//  least one item, so it appears exactly when you are on that server with one
//  of its plugins installed. A new server or server plugin is one DefineServer
//  plus its AddItem calls; neither menu changes.
//
//  Items are "/rc" chat commands: the engine runs them itself
//  (RynthCoreChatCommands) and RynthAi runs them through InvokeChatParser.
//  RynthAi learns the sections from the "servermenu" remote command this file
//  pushes (Tick: when they change, and every ResendMs in case it missed one).
//  Payload: one line per section ("S", id, name, active 0/1, force command)
//  followed by its items ("I", label, command, tooltip, feature), tab-separated.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.UI;

/// <summary>
/// One menu item of a server section. <paramref name="Feature"/> is an optional key RynthAi
/// checks before enabling it ("tab:quests": that Mini Remote tab is available).
/// </summary>
internal sealed record ServerMenuItem(string Label, string Command, string Tooltip, string Feature = "");

/// <summary>A server's menu section: detection, force command and the installed plugins' items.</summary>
internal sealed class ServerMenuSection
{
    public ServerMenuSection(string id, string name, Func<bool> isActive, string forceCommand)
    {
        Id = id;
        Name = name;
        IsActiveFn = isActive;
        ForceCommand = forceCommand;
    }

    public string Id { get; }
    public string Name { get; }
    public string ForceCommand { get; }
    internal Func<bool> IsActiveFn { get; }
    internal List<ServerMenuItem> ItemList { get; } = new();
    public IReadOnlyList<ServerMenuItem> Items => ItemList;

    /// <summary>On this server (detected or forced). A failing check counts as not active.</summary>
    public bool IsActive
    {
        get
        {
            try { return IsActiveFn(); }
            catch { return false; }
        }
    }
}

internal static class ServerMenus
{
    /// <summary>How often RynthAi gets the sections again even when nothing changed.</summary>
    private const long ResendMs = 30_000;
    /// <summary>How often the payload is rebuilt to look for a change.</summary>
    private const long CheckMs = 1_000;

    private static readonly object Gate = new();
    private static readonly List<ServerMenuSection> Defined = new();
    // Copy-on-write view for the render thread (definitions only change during engine init).
    private static volatile ServerMenuSection[] _sections = Array.Empty<ServerMenuSection>();

    private static long _nextCheckMs;
    private static long _lastPushMs;
    private static string _lastPayload = string.Empty;

    /// <summary>RynthAi is installed: Tick pushes it the sections.</summary>
    public static bool PushToRynthAi { get; set; }

    /// <summary>Every defined server, in definition order (with or without items).</summary>
    public static IReadOnlyList<ServerMenuSection> All => _sections;

    /// <summary>
    /// Defines a server (once; a second call with the same id is ignored).
    /// <paramref name="isActive"/> runs on the render thread, so it must be cheap.
    /// </summary>
    public static void DefineServer(string id, string name, Func<bool> isActive, string forceCommand)
    {
        lock (Gate)
        {
            if (Find(id) != null) return;
            Defined.Add(new ServerMenuSection(id, name, isActive, forceCommand));
            _sections = Defined.ToArray();
        }
    }

    /// <summary>An installed plugin's window for server <paramref name="serverId"/> (ignored for an unknown server).</summary>
    public static void AddItem(string serverId, ServerMenuItem item)
    {
        lock (Gate)
        {
            ServerMenuSection? section = Find(serverId);
            if (section == null)
            {
                RynthLog.UI($"ServerMenus: item '{item.Label}' names unknown server '{serverId}'; ignored.");
                return;
            }
            section.ItemList.Add(item);
            _sections = Defined.ToArray();
        }
    }

    private static ServerMenuSection? Find(string id)
    {
        foreach (ServerMenuSection s in Defined)
            if (s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    /// <summary>Runs an item's command through the engine's /rc dispatcher.</summary>
    public static void Run(string command)
    {
        if (!Compatibility.RynthCoreChatCommands.TryHandle(command))
            RynthLog.UI($"ServerMenus: '{command}' is not an engine command.");
    }

    // ── RynthAi push ───────────────────────────────────────────────────

    /// <summary>
    /// Render thread, every frame: sends RynthAi the sections when they changed (a server
    /// detected or forced, or not any more) and every <see cref="ResendMs"/>.
    /// </summary>
    public static void Tick()
    {
        if (!PushToRynthAi) return;
        long now = Environment.TickCount64;
        if (now < _nextCheckMs) return;
        _nextCheckMs = now + CheckMs;

        string payload = BuildPayload();
        if (payload == _lastPayload && now - _lastPushMs < ResendMs) return;
        _lastPayload = payload;
        _lastPushMs = now;
        RynthAiCommands.ApplyRemoteCommand("servermenu", payload);
    }

    private static string BuildPayload()
    {
        var sb = new StringBuilder(256);
        foreach (ServerMenuSection s in _sections)
        {
            if (s.Items.Count == 0) continue;   // no plugin for it is installed
            sb.Append('S').Append('\t').Append(Clean(s.Id)).Append('\t').Append(Clean(s.Name)).Append('\t')
              .Append(s.IsActive ? '1' : '0').Append('\t').Append(Clean(s.ForceCommand)).Append('\n');
            foreach (ServerMenuItem i in s.Items)
                sb.Append('I').Append('\t').Append(Clean(i.Label)).Append('\t').Append(Clean(i.Command)).Append('\t')
                  .Append(Clean(i.Tooltip)).Append('\t').Append(Clean(i.Feature)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Field text without the payload's separators (the push is ANSI, so ASCII only).</summary>
    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(c is '\t' or '\n' or '\r' ? ' ' : c < 128 ? c : '?');
        return sb.ToString();
    }

    // ── ImGui menu ─────────────────────────────────────────────────────

    /// <summary>
    /// The server half of an ImGui popup menu: each active section's name and items. With
    /// sections installed but none active, a note and one "Treat this world as ..." item per
    /// installed server. Draws nothing (not even <paramref name="separatorFirst"/>'s separator)
    /// when no server plugin is installed. Returns true when it drew something.
    /// </summary>
    public static bool DrawImGuiMenu(bool separatorFirst = false)
    {
        ServerMenuSection[] sections = _sections;
        bool any = false, installed = false;
        foreach (ServerMenuSection s in sections)
        {
            if (s.Items.Count == 0) continue;
            if (!installed && separatorFirst) ImGuiNET.ImGui.Separator();
            installed = true;
            if (!s.IsActive) continue;
            any = true;
            ImGuiNET.ImGui.TextDisabled(s.Name);
            foreach (ServerMenuItem item in s.Items)
            {
                if (ImGuiNET.ImGui.MenuItem(item.Label + "##srv_" + s.Id, item.Command))
                    Run(item.Command);
                if (item.Tooltip.Length > 0) ImGuiNET.ImGui.SetItemTooltip(item.Tooltip);
            }
        }
        if (any || !installed) return any;

        ImGuiNET.ImGui.TextDisabled("Server: no installed server plugin matches this world");
        foreach (ServerMenuSection s in sections)
        {
            if (s.Items.Count == 0 || s.ForceCommand.Length == 0) continue;
            if (ImGuiNET.ImGui.MenuItem("Treat this world as " + s.Name + "##srvforce_" + s.Id, s.ForceCommand))
                Run(s.ForceCommand);
            ImGuiNET.ImGui.SetItemTooltip("For this session only (testing); the world check goes back to automatic on restart.");
        }
        return true;
    }
}
