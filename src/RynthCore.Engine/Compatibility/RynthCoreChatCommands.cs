using System;
using S = RynthCore.Engine.ImGuiBackend.Hud.MonsterHudSettings;

namespace RynthCore.Engine.Compatibility;

/// <summary>
/// Engine-level <c>/rc ...</c> chat commands. Intercepted on every chat-entry
/// path BEFORE plugin pre-dispatch and before the line reaches AC's chat:
/// <list type="bullet">
///   <item>typed in-game — <see cref="ChatCallbackHooks"/> OutgoingChatDetour</item>
///   <item>dispatch.txt / OnLoginCommandRunner / Host.InvokeChatParser —
///         <see cref="ChatCommandDispatcher.Dispatch"/></item>
/// </list>
/// Commands:
/// <list type="bullet">
///   <item><c>/rc resetbar</c> — recover the RynthCore overlay bar when it has
///         been dragged off the visible client area (its in-overlay "Rs" reset
///         button rides the bar itself, so it's unreachable once the bar is).
///         The launcher button writes this to dispatch.txt as the out-of-band
///         recovery.</item>
///   <item><c>/rc actionstate</c> (alias <c>/rc actions</c>) — read-only: the client's
///         item-action gates (pending item request, attacking flag, busy count,
///         target mode, combat state), see <see cref="ClientActionGates"/>.
///         <c>/rc unlockactions</c> (alias <c>/rc unlock</c>) opens those gates now.</item>
///   <item><c>/rc vitals</c> (alias <c>/rc hud</c>) — toggle the custom D3D9
///         Health/Stamina/Mana HUD on/off (persisted to engine.json).</item>
///   <item><c>/rc ui retailvitals [on|off|status]</c> — hide AC's own
///         health/stamina/mana bars (<see cref="RetailVitalsHooks"/>; saved for the
///         PC, also in Settings &gt; UI). Alone it reports the state.</item>
///   <item><c>/rc ui barsize [0-10|+|-|status]</c> - the RynthCore bar's size (its own text
///         size step, <see cref="UI.PanelTextScale"/>; saved for the PC, also in Settings &gt;
///         Display). Alone it reports the size.</item>
///   <item><c>/rc vendor [list|info|status|buy &lt;id|sel&gt; [n]|sell &lt;id|sel&gt; [id ...]]</c>
///         — read / trade with the open vendor through the engine's vendor
///         primitives (<see cref="VendorTrade"/>); a manual test hook for the
///         plugin API. Replies go to chat from the main-thread drain and to the log.</item>
///   <item><c>/rc chathold [on|off|status]</c> — incoming chat waits for the plugins'
///         verdict before AC prints it, so a line a plugin eats is kept out of AC's chat
///         window too (<see cref="ChatCallbackHooks"/>). On at every start; not saved.</item>
///   <item><c>/rc discord [on|off|status|window|test [route]|resume [route]]</c> — RynthChat's
///         Discord relay (<see cref="UI.Data.Discord.ChatDiscord"/>): switch it on or off for the
///         character you're playing (off for every character by default), list the routes,
///         post a test line, clear a pause; window opens its panel (Chat &gt; gear &gt; Discord).</item>
///   <item><c>/rv plates</c> (aliases <c>/rv nameplates</c>, <c>/rc plates</c>) — the
///         RynthVision nameplates and combat text: monster plates on|off, self, debuffs,
///         numbers, gains [on|off] (with their sub-switches), all on|off, all|engaged,
///         dist/max/scale/opacity/lift &lt;n&gt;, field toggles, reset, status
///         (<see cref="ImGuiBackend.Hud.MonsterHud"/>). "/rv plates" is caught here,
///         before plugin pre-dispatch, so the RynthVision plugin's own "/rv" handler
///         never sees it; every other "/rv" line still goes to the plugin.</item>
///   <item><c>/rc server [auto|aelrynth|other]</c> — which server the engine detects
///         (<see cref="ServerInfo"/>) and why; aelrynth/other force it for this session.</item>
///   <item><c>/rc hooks [on|off &lt;name&gt;]</c> (alias <c>/rc uihooks</c>) — the UI hooks
///         (screen mode, client cleanup, tooltip, drag/drop; <see cref="UiHookRegistry"/>):
///         status, or switch one off/on (now and in engine.json).</item>
///   <item><c>/rc video [status|off|world|unfocused|all|fps &lt;n&gt;]</c> (alias
///         <c>/rc videopatch</c>; "on" = world) — the video patch
///         (<see cref="D3D9.VideoPatch"/>): stop drawing AC's 3D world (world), only while
///         the client is in the background (unfocused), or AC's UI too (all, session only).
///         Saved per character (also Settings &gt; Misc); fps = the frame cap while patched.</item>
///   <item><c>/rc mastery</c> — the Aelrynth mastery feed's state (<see cref="MasteryFeed"/>),
///         and ask the server for /mastery-data again.</item>
///   <item><c>/rc worldlayer [auto|uipass|transition|endscene]</c> — where the world overlays
///         (nameplates, combat text, Nav3D markers) draw in AC's frame
///         (<see cref="D3D9.Nav3DRenderInjector"/>): alone it reports the path and counters;
///         a mode forces it for this session (A/B checks). Auto at every start; not saved.</item>
///   <item><c>/rc acmouse [watch [seconds]]</c> (alias <c>/rc mouseover</c>) - what AC's own UI
///         has under the cursor (<see cref="AcMouseOver"/>: the 3D world or one of its windows,
///         with the element's id and type), i.e. whether a nameplate there may take the click;
///         watch = every change for a while (default 20 s), to chat and the log.</item>
///   <item><c>/rc netmsg [status|on|off|log on|off|capture on|off]</c> - the reassembled
///         server-message stream for plugins (<see cref="Net.ServerMessageStream"/>, API v78):
///         counters; on/off for this session (engine.json "ServerMessageStream": false is the
///         kill switch); log = the SDK parsers run on live messages, results to the log;
///         capture = raw datagrams to a .rnc file for tools/NetMessageTests.</item>
///   <item><c>/rc flightdump [note]</c> - write the flight recorder (<see cref="Net.FlightRecorder"/>:
///         the last minutes of client/server messages and engine markers) to
///         Logs\flight-&lt;pid&gt;-&lt;time&gt;.txt; <c>/rc flightdump auto &lt;reason&gt;</c> is the rate-limited
///         automatic form. <c>/rc flight [status|mark &lt;text&gt;|on|off|auto on|off]</c>.</item>
///   <item><c>/rc report [text]</c> - "Report a problem" (<see cref="Report.ProblemReport"/>): alone it
///         opens the panel; with text it sends that report now, with the recent log attached
///         (the panel is where the log can be left out). One report a minute.</item>
/// </list>
/// </summary>
internal static class RynthCoreChatCommands
{
    private const string Prefix = "/rc ";

    /// <summary>
    /// True if <paramref name="line"/> was a RynthCore engine command and has
    /// been handled — the caller must consume it (do NOT forward to AC chat
    /// or to plugins).
    /// </summary>
    public static bool TryHandle(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        string trimmed = line.Trim();

        // /rv plates ... : the RynthVision overlays live engine-side, so the engine answers
        // before the plugin's "/rv" prefix handler would (it runs after this, on pre-dispatch).
        if (trimmed.StartsWith("/rv ", StringComparison.OrdinalIgnoreCase))
        {
            string rv = trimmed.Substring(4).Trim();
            if (IsVerb(rv, "plates", out string rvArgs) || IsVerb(rv, "nameplates", out rvArgs))
            {
                try { HandlePlatesCommand(rvArgs); }
                catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rv plates failed - {ex.GetType().Name}: {ex.Message}"); }
                return true;
            }
            return false;
        }

        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string sub = trimmed.Substring(Prefix.Length).Trim();

        // /rc vendor [list|info|status|buy <id|sel> [n]|sell <id|sel> [id ...]]
        if (sub.Equals("vendor", StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith("vendor ", StringComparison.OrdinalIgnoreCase))
        {
            try { VendorTrade.HandleChatCommand(sub.Length > 6 ? sub.Substring(7) : string.Empty); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc vendor failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc imgui [on|off|fonttest]
        if (sub.Equals("imgui", StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith("imgui ", StringComparison.OrdinalIgnoreCase))
        {
            try { HandleImGuiCommand(sub.Length > 5 ? sub.Substring(6).Trim() : string.Empty); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc imgui failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc ui list | /rc ui <Panel> avalonia|imgui|both
        if (sub.Equals("ui", StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith("ui ", StringComparison.OrdinalIgnoreCase))
        {
            try { HandleUiCommand(sub.Length > 2 ? sub.Substring(3).Trim() : string.Empty); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc ui failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc chathold [on|off|status]: lines a plugin eats stay out of AC's chat window too
        if (IsVerb(sub, "chathold", out string holdArgs))
        {
            try { HandleChatHoldCommand(holdArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc chathold failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc discord [on|off|status|window|test [route]|resume [route]] : RynthChat's Discord relay
        if (IsVerb(sub, "discord", out string discordArgs))
        {
            try { HandleDiscordCommand(discordArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc discord failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc plates [on|off|all|engaged|dist N|max N|scale X|opacity X|lift X|names|hp|level|weak|distance|fade|reset|status]
        if (IsVerb(sub, "plates", out string platesArgs) || IsVerb(sub, "nameplates", out platesArgs))
        {
            try { HandlePlatesCommand(platesArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc plates failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc server [<server> auto|on|off | names ilt ...] : the servers, why each is active, per-character overrides
        if (IsVerb(sub, "server", out string serverArgs))
        {
            try { HandleServerCommand(serverArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc server failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc ilt [auto|on|off] : is this Infinite Leaftide (the ILT section), with a per-character override
        if (IsVerb(sub, "ilt", out string iltArgs))
        {
            try { HandleIltCommand(iltArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc ilt failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc mastery : the Aelrynth mastery feed's state, and ask the server again
        if (IsVerb(sub, "mastery", out _))
        {
            try { HandleMasteryCommand(); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc mastery failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc worldlayer [auto|uipass|transition|endscene] : where the world overlays draw
        if (IsVerb(sub, "worldlayer", out string worldArgs))
        {
            try { HandleWorldLayerCommand(worldArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc worldlayer failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc acmouse [watch [seconds]] : what AC's own UI has under the cursor (the 3D world or
        // one of its windows) - whether a nameplate there may take the click (AcMouseOver)
        if (IsVerb(sub, "acmouse", out string acMouseArgs) || IsVerb(sub, "mouseover", out acMouseArgs))
        {
            try
            {
                string[] p = acMouseArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length > 0 && p[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
                {
                    int secs = p.Length > 1 && int.TryParse(p[1], out int n) ? n : 20;
                    AcMouseOver.Watch(secs);
                    Reply($"Watching AC's mouse-over for {Math.Clamp(secs, 1, 120)} s: move the cursor over the world and over AC's windows.");
                }
                else
                {
                    Reply(AcMouseOver.Describe());
                }
            }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc acmouse failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc video [status|off|world|unfocused|all|fps <n>] : the video patch (stop drawing the 3D world)
        if (IsVerb(sub, "video", out string videoArgs) || IsVerb(sub, "videopatch", out videoArgs))
        {
            try
            {
                foreach (string l in D3D9.VideoPatch.HandleCommand(videoArgs))
                    Reply(l);
            }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc video failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc hooks [on|off <name>] : the UI hooks (screen mode, client cleanup, tooltip, drag/drop)
        if (IsVerb(sub, "hooks", out string hooksArgs) || IsVerb(sub, "uihooks", out hooksArgs))
        {
            try { HandleHooksCommand(hooksArgs); }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc hooks failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc flightdump [note] | /rc flightdump auto <reason> : write the flight recorder now
        if (IsVerb(sub, "flightdump", out string dumpArgs))
        {
            try
            {
                foreach (string l in Net.FlightRecorder.HandleDumpCommand(dumpArgs))
                    Reply(l);
            }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc flightdump failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc report [text] : Report a problem - alone it opens the panel; with text it sends now (with the log)
        if (IsVerb(sub, "report", out string reportArgs))
        {
            try
            {
                foreach (string l in Report.ProblemReport.HandleCommand(reportArgs))
                    Reply(l);
            }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc report failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc flight [status|mark <text>|on|off|auto on|off] : the flight recorder
        if (IsVerb(sub, "flight", out string flightArgs))
        {
            try
            {
                foreach (string l in Net.FlightRecorder.HandleCommand(flightArgs))
                    Reply(l);
            }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc flight failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc netmsg [status|on|off|log on|off|capture on|off] : the v78 server message stream
        if (IsVerb(sub, "netmsg", out string netArgs))
        {
            try
            {
                foreach (string l in Net.ServerMessageStream.HandleCommand(netArgs))
                    Reply(l);
            }
            catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc netmsg failed - {ex.GetType().Name}: {ex.Message}"); }
            return true;
        }

        // /rc vtank [clear] : what the engine knows about VTank's macro (Decal bridge only);
        // clear = VTank was stopped with its own button, which the engine can't see.
        if (IsVerb(sub, "vtank", out string vtankArgs))
        {
            if (vtankArgs.Equals("clear", StringComparison.OrdinalIgnoreCase))
                Reply(VTankWatch.Clear() ? "VTank marked as not running; RynthAi's macro can start again." : "VTank was already marked as not running.");
            Reply(VTankWatch.Describe());
            return true;
        }

        // /rc bounties|guardian [show|hide|toggle] : the ILT section's panels, like the bar's
        // ILT button (Bounties: the RynthILT plugin's tracker; Guardian: RynthAi's Temple
        // guardian tools). Only on Infinite Leaftide with the plugin installed.
        if (IsVerb(sub, "bounties", out string panelArgs) || IsVerb(sub, "bounty", out panelArgs) || IsVerb(sub, "bountys", out panelArgs))
        {
            IltPanel(ImGuiBackend.Panels.BountiesFace.Title, "Bounties", "RynthILT", panelArgs);
            return true;
        }
        if (IsVerb(sub, "guardian", out panelArgs) || IsVerb(sub, "guardians", out panelArgs))
        {
            IltPanel(ImGuiBackend.Panels.GuardianFace.Title, "Guardian", "RynthAi", panelArgs);
            return true;
        }

        // /rc miniremote [show|hide|toggle|tab <tab>|section <id>] : the ImGui Mini Remote panel, like
        // its bar button (RynthAi's dashboard and "/ra hub" use show / hide). "tab" opens it on one of
        // RynthAi's tabs (character, quests, pets, crafting, fellowship, banking, gear, games; none = the remote).
        // "section" opens it on one of its sections (char, map, skills, ...; "remote" = the remote).
        if (IsVerb(sub, "miniremote", out string remoteArgs) || IsVerb(sub, "remote", out remoteArgs))
        {
            if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.MiniRemoteFace.Title))
                Reply("The Mini Remote panel isn't available (the ImGui layer is off?).");
            else if (IsVerb(remoteArgs, "tab", out string tab)
                     && tab.Length > 0 && ImGuiBackend.Panels.MiniRemoteFace.TryRequestSurface("section:" + tab.Trim().ToLowerInvariant()))
            {
                // A RynthAi that publishes sections: its tabs are sections here.
                ImGuiBackend.ImGuiPanelHost.Open(ImGuiBackend.Panels.MiniRemoteFace.Title);
            }
            else if (IsVerb(remoteArgs, "tab", out tab))
            {
                ImGuiBackend.Panels.MiniRemoteFace.RequestSection(UI.Sections.SectionCatalog.RemoteId);
                ImGuiBackend.ImGuiPanelHost.Open(ImGuiBackend.Panels.MiniRemoteFace.Title);
                UI.Data.RynthAiCommands.ApplyRemoteCommand("hub", tab.Length > 0 ? "tab " + tab : "tab");
            }
            else if (IsVerb(remoteArgs, "section", out string section))
            {
                string id = UI.Sections.SectionManifestParser.Id(section);
                var snap = UI.Sections.SectionRegistry.Current;
                if (id != UI.Sections.SectionCatalog.RemoteId && snap.Find(id) == null)
                {
                    var names = new System.Collections.Generic.List<string> { UI.Sections.SectionCatalog.RemoteId };
                    foreach (UI.Sections.SectionView v in snap.Sections) names.Add(v.Id);
                    Reply($"Usage: /rc miniremote section <{string.Join("|", names)}>");
                }
                else
                {
                    ImGuiBackend.Panels.MiniRemoteFace.RequestSection(id);
                    ImGuiBackend.ImGuiPanelHost.Open(ImGuiBackend.Panels.MiniRemoteFace.Title);
                }
            }
            else if (!TryPanelMode(remoteArgs, out bool? show))
                Reply("Usage: /rc miniremote [show|hide|toggle|tab <tab>|section <id>]");
            else
                SetPanelOpen(ImGuiBackend.Panels.MiniRemoteFace.Title, show);
            return true;
        }

        // /rc skills [show|hide|toggle] : the ImGui Skills panel; /rc progression opens it on its
        // Progression tab (ILT worlds; the tab shows once RynthAi reports ILT features).
        if (IsVerb(sub, "skills", out string skillsArgs))
        {
            if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.SkillsFace.Title))
                Reply("The Skills panel isn't available (the ImGui layer is off?).");
            else if (!TryPanelMode(skillsArgs, out bool? show))
                Reply("Usage: /rc skills [show|hide|toggle]");
            else
                SetPanelOpen(ImGuiBackend.Panels.SkillsFace.Title, show);
            return true;
        }
        if (sub.Equals("progression", StringComparison.OrdinalIgnoreCase))
        {
            if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.SkillsFace.Title))
                Reply("The Skills panel isn't available (the ImGui layer is off?).");
            else
                ImGuiBackend.Panels.SkillsFace.ShowProgression();
            return true;
        }

        // /rc spells [show|hide|toggle] : RynthAi's Spells window (spell catalog, buff profiles).
        if (IsVerb(sub, "spells", out string spellsArgs))
        {
            if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.RynthAiFace.Title))
                Reply("The Spells window comes with RynthAi; it isn't installed.");
            else if (!TryPanelMode(spellsArgs, out bool? show))
                Reply("Usage: /rc spells [show|hide|toggle]");
            else
                UI.Data.RynthAiCommands.ApplyRemoteCommand("spells", show == null ? "toggle" : show.Value ? "show" : "hide");
            return true;
        }

        switch (sub.ToLowerInvariant())
        {
            case "rl":
            case "reload":
                // Same as the bar's RL button. Signalled from a pool thread: the
                // loader's reload shuts this engine down, and this handler runs
                // inside AC's outgoing-chat hook.
                RynthLog.Compat("RynthCoreChatCommands: /rc rl — engine reload requested.");
                System.Threading.ThreadPool.UnsafeQueueUserWorkItem(static _ => EngineLifecycle.SignalReload(), null);
                return true;

            case "inv":
            case "inventory":
                // The ImGui Inventory panel (open/close), like its bar button.
                if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.InventoryFace.Title))
                    Reply("The Inventory panel isn't available (the ImGui layer is off?).");
                else
                    UI.PanelRouter.Toggle(ImGuiBackend.Panels.InventoryFace.Title);
                return true;

            case "sense":
                // The ImGui Sense panel (open/close), like its bar button.
                if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.SenseFace.Title))
                    Reply("The Sense panel isn't available (the ImGui layer is off?).");
                else
                    UI.PanelRouter.Toggle(ImGuiBackend.Panels.SenseFace.Title);
                return true;

            case "itemcount":
            case "itemhud":
                // The ImGui Item Count panel (open/close), like its bar button.
                if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.ItemCountFace.Title))
                    Reply("The Item Count panel isn't available (the ImGui layer is off?).");
                else
                    UI.PanelRouter.Toggle(ImGuiBackend.Panels.ItemCountFace.Title);
                return true;

            case "resetbar":
            case "barreset":
                RynthLog.Compat("RynthCoreChatCommands: /rc resetbar — resetting overlay bar position.");
                ImGuiBackend.RynthCoreShell.RequestExternalReset();
                return true;

            case "actionstate":
            case "actions":
                // Read-only: every client-side gate on item use/equip/move
                // (ClientActionGates). Works while those are locked — chat still does.
                try
                {
                    foreach (string stateLine in ClientActionGates.DescribeLines())
                        Reply(stateLine);
                }
                catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc actionstate failed - {ex.GetType().Name}: {ex.Message}"); }
                return true;

            case "unlockactions":
            case "unlock":
                try
                {
                    foreach (string unlockLine in ClientActionGates.RequestUnlock())
                        Reply(unlockLine);
                }
                catch (Exception ex) { RynthLog.Compat($"RynthCoreChatCommands: /rc unlockactions failed - {ex.GetType().Name}: {ex.Message}"); }
                return true;

            case "vitals":
            case "hud":
            {
                // The old D3D9 vitals HUD is gone (2026-09-29): your vitals are the
                // RynthVision player plate now.
                Reply("The old vitals HUD was removed. Your health/stamina/mana bars are the RynthVision player plate: /rv plates self [on|off].");
                return true;
            }

            default:
                // Consume anything else under the /rc namespace so a typo
                // does not leak into world chat.
                RynthLog.Compat($"RynthCoreChatCommands: unknown subcommand '/rc {sub}'.");
                return true;
        }
    }

    /// <summary>
    /// /rc ui list, or /rc ui &lt;Panel&gt; avalonia|imgui|both: which face shows a
    /// panel while it is docked in the client (UI.PanelRouter). Replies are
    /// queued to chat, not written from inside this outgoing-chat hook.
    /// </summary>
    private static void HandleUiCommand(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string line in UI.PanelRouter.Describe())
                Reply(line);
            return;
        }

        // /rc ui retailvitals [on|off]: hide AC's own health/stamina/mana bars
        // (RetailVitalsHooks). Alone it reports; anything but on/off toggles.
        if (parts[0].Equals("retailvitals", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length >= 2 && parts[1].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Reply(RetailVitalsHooks.Describe());
                return;
            }
            if (parts.Length >= 2)
                RetailVitalsHooks.HideRetailVitals = Flag(parts[1], RetailVitalsHooks.HideRetailVitals);
            Reply(RetailVitalsHooks.Describe());
            return;
        }

        // /rc ui barsize [0-10|+|-|status]: the bar's size. Alone it reports it.
        if (parts[0].Equals("barsize", StringComparison.OrdinalIgnoreCase))
        {
            int cur = UI.PanelTextScale.GetChrome();
            if (parts.Length >= 2 && !parts[1].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                int next;
                // Bare + / -: one step. +n / -n: that many steps. Plain n: that size.
                if (parts[1] == "+") next = cur + 1;
                else if (parts[1] == "-") next = cur - 1;
                else if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out next))
                {
                    Reply("Usage: /rc ui barsize [0-10|+|-|status]");
                    return;
                }
                else if (parts[1][0] is '+' or '-')
                    next += cur;
                UI.PanelTextScale.SetChrome(next);
            }
            Reply("Bar size: " + UI.PanelTextScale.Describe(UI.PanelTextScale.GetChrome()) + ".");
            return;
        }

        // /rc ui open|close|popout|redock|snap <Panel>: drive a panel by name (tests,
        // dispatch.txt). Names may have spaces ("Monster Detail").
        string verb = parts[0].ToLowerInvariant();
        if (verb is "open" or "close" or "popout" or "redock" or "snap" && parts.Length >= 2)
        {
            // "Monsters" (the retired basic panel) is the Damage panel now.
            string title = UI.PanelRouter.Canonical(string.Join(' ', parts, 1, parts.Length - 1));
            if (title.Equals("bar", StringComparison.OrdinalIgnoreCase) && verb is "popout" or "redock")
            {
                ImGuiBackend.ImGuiBar.SetPoppedOut(verb == "popout");
                Reply($"/rc ui {verb} bar: requested.");
                return;
            }
            if (title.Equals("bar", StringComparison.OrdinalIgnoreCase) && verb == "snap") title = "__bar";
            else if (!ImGuiBackend.ImGuiPanelHost.HasFace(title)) { Reply($"No ImGui panel named '{title}'."); return; }
            switch (verb)
            {
                case "open": ImGuiBackend.ImGuiPanelHost.Open(title); break;
                case "close": ImGuiBackend.ImGuiPanelHost.Close(title); break;
                case "popout": ImGuiBackend.ImGuiPanelHost.PopOut(title); break;
                case "redock": ImGuiBackend.ImGuiPanelHost.Redock(title); break;
                case "snap":
                    ImGuiBackend.Win32Backend.PostToGameThread(() =>
                        RynthLog.UI($"/rc ui snap {title}: " + (ImGuiBackend.ImGuiPopOuts.RequestSnapshot(title) ?? "not popped out")));
                    break;
            }
            Reply($"/rc ui {verb} {title}: requested.");
            return;
        }

        if (parts.Length != 2 || !UI.PanelFaceStore.TryParseFace(parts[1], out UI.PanelFace face))
        {
            Reply("Usage: /rc ui list  |  /rc ui <Panel|bar> avalonia|imgui|both  |  /rc ui open|close|popout|redock|snap <Panel>  |  /rc ui retailvitals [on|off]  |  /rc ui barsize [0-10|+|-|status]");
            return;
        }

        Reply(UI.PanelRouter.SetDockedFace(parts[0], face));
    }

    /// <summary>
    /// /rc imgui on|off switches the engine's in-client ImGui layer live and
    /// saves it to engine.json; /rc imgui fonttest toggles the font/theme test
    /// window; /rc imgui alone reports the state.
    /// </summary>
    private static void HandleImGuiCommand(string arg)
    {
        switch (arg.ToLowerInvariant())
        {
            case "on":
                Plugins.EngineSettings.EnableImGuiBackend = true;
                ImGuiBackend.ImGuiBar.ApplyFace();
                Reply("ImGui layer ON (saved). The self-test result is in the log (ImGuiSelfTest:). Panels: /rc ui list.");
                return;
            case "off":
                Plugins.EngineSettings.EnableImGuiBackend = false;
                ImGuiBackend.Win32Backend.ClearCaptureFlags();
                ImGuiBackend.Win32Backend.DiscardQueuedInput();
                ImGuiBackend.ImGuiBar.ApplyFace(); // the Avalonia bar comes back
                Reply("ImGui layer OFF (saved).");
                return;
            case "fonttest":
                ImGuiBackend.ImGuiFontTest.Enabled = !ImGuiBackend.ImGuiFontTest.Enabled;
                Reply($"ImGui font test window {(ImGuiBackend.ImGuiFontTest.Enabled ? "shown" : "hidden")}"
                      + (Plugins.EngineSettings.EnableImGuiBackend ? "." : " (the ImGui layer is off: /rc imgui on)."));
                return;
            case "diag":
                foreach (string line in ImGuiBackend.EngineFrameController.DescribeState())
                    RynthLog.Info("ImGuiDiag: " + line);
                foreach (string line in ImGuiBackend.ImGuiPanelHost.DescribeFaces())
                    RynthLog.Info("ImGuiDiag: " + line);
                foreach (string line in UI.ScriptWindows.ScriptWindowRegistry.Describe())
                    RynthLog.Info("ImGuiDiag: " + line);
                RynthLog.Info($"ImGuiDiag: bar visible={ImGuiBackend.ImGuiBar.Visible} face={UI.PanelFaceStore.FaceName(ImGuiBackend.ImGuiBar.ResolveFace())} avaloniaBarSuppressed={UI.AvaloniaOverlay.DockedBarSuppressed}");
                Reply("ImGui diagnostics written to the RynthCore log (ImGuiDiag:).");
                return;
            case "":
                Reply($"ImGui layer is {(Plugins.EngineSettings.EnableImGuiBackend ? "ON" : "OFF")}. Usage: /rc imgui on|off|fonttest|diag");
                return;
            default:
                Reply("Usage: /rc imgui on|off|fonttest|diag");
                return;
        }
    }

    /// <summary>
    /// /rc chathold on|off|status: incoming chat waits for the plugins before AC prints it,
    /// so a line a plugin eats (RynthAi's Meta Manager eats the reply to its own /myquests)
    /// is not shown in AC's chat window either (ChatCallbackHooks, IncomingChatHold.cs).
    /// On at every start; off prints every line at once, as before, and plugins can then
    /// only keep a line out of RynthChat.
    /// </summary>
    private static void HandleChatHoldCommand(string arg)
    {
        switch (arg.ToLowerInvariant())
        {
            case "on":
                ChatCallbackHooks.SetHoldEnabled(true);
                break;
            case "off":
                ChatCallbackHooks.SetHoldEnabled(false);
                break;
            case "":
            case "status":
                break;
            default:
                Reply("Usage: /rc chathold [on|off|status]");
                return;
        }
        Reply(ChatCallbackHooks.HoldStatusText());
    }

    /// <summary>"verb" or "verb args..." (case-insensitive); args trimmed.</summary>
    private const string ServerUsage =
        "Usage: /rc server [<server> auto|on|off] | [aelrynth|other|auto] | names ilt [add|remove <pattern>]";

    /// <summary>
    /// /rc server: every server the engine and the plugins know, whether each is active and why.
    /// "&lt;server&gt; on|off|auto" forces one on or off, or back to detecting, saved for this
    /// character (this session only before login). "aelrynth" / "other" / "auto" alone are the
    /// older Aelrynth override. "names ilt" lists, adds or removes Infinite Leaftide's world-name
    /// patterns (engine.json IltServerNames).
    /// </summary>
    private static void HandleServerCommand(string args)
    {
        string[] words = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string first = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;

        if (first == "names")
        {
            HandleServerNames(words);
            return;
        }
        if (words.Length == 1)
        {
            // The older single-word form: the Aelrynth override.
            ServerInfo.Mode? legacy = first switch
            {
                "aelrynth" or "on" => ServerInfo.Mode.ForceAelrynth,
                "other" or "off" => ServerInfo.Mode.ForceOther,
                "auto" => ServerInfo.Mode.Auto,
                _ => null,
            };
            if (legacy == null) { Reply(ServerUsage); return; }
            SetServerForce(ServerInfo.ServerId, legacy switch
            {
                ServerInfo.Mode.ForceAelrynth => UI.Sections.ServerForce.On,
                ServerInfo.Mode.ForceOther => UI.Sections.ServerForce.Off,
                _ => UI.Sections.ServerForce.Auto,
            });
        }
        else if (words.Length == 2)
        {
            if (!UI.Sections.ServerForceText.TryParse(words[1], out UI.Sections.ServerForce mode)) { Reply(ServerUsage); return; }
            if (!SetServerForce(first, mode)) return;
        }
        else if (words.Length > 2)
        {
            Reply(ServerUsage);
            return;
        }

        ReplyServers();
        ServerVerdict v = ServerInfo.Current;
        Reply($"Aelrynth signals: host {ServerInfo.HostDescription}; world name {AccountHooks.AnnouncedWorldName ?? "(not announced)"}"
            + (v.IsStaging ? "; staging" : "") + ".");
        Reply($"Titles (plugin API v75): {CharacterTitles.Describe()}.");
    }

    /// <summary>Saves a server override (per character) and says where it went. False for an unknown server.</summary>
    private static bool SetServerForce(string serverId, UI.Sections.ServerForce mode)
    {
        string id = UI.Sections.SectionManifestParser.Id(serverId);
        if (id.Length == 0 || (!UI.Sections.SectionRegistry.IsBuiltInServer(id)
                               && UI.Sections.SectionRegistry.Current.Server(id) == null))
        {
            Reply($"No server called '{serverId}'. /rc server lists them.");
            return false;
        }
        if (!UI.Sections.ServerForceStore.Set(id, mode, out string where))
        {
            Reply($"Server override not changed: {where}.");
            return false;
        }
        UI.Sections.SectionRegistry.Invalidate();
        Reply($"Server override {id} = {UI.Sections.ServerForceText.Name(mode)} ({where}).");
        return true;
    }

    /// <summary>One line per server: active or not, detected or not, the override, why.</summary>
    private static void ReplyServers()
    {
        var snap = UI.Sections.SectionRegistry.Current;
        if (snap.Servers.Count == 0)
        {
            // Before the first frame built a snapshot: the built-in verdicts directly.
            IltVerdict ilt = IltInfo.Current;
            ServerVerdict ael = ServerInfo.Current;
            Reply($"Server ilt (Infinite Leaftide): {(ilt.IsIlt ? "active" : "not active")} - {ilt.Reason}.");
            Reply($"Server aelrynth (Aelrynth): {(ael.IsAelrynth ? "active" : "not active")} - {ael.Reason}.");
            return;
        }
        foreach (UI.Sections.ServerState s in snap.Servers)
        {
            string force = s.Force == UI.Sections.ServerForce.Auto ? "auto"
                : $"forced {UI.Sections.ServerForceText.Name(s.Force)} (detected: {(s.Detected ? "yes" : "no")})";
            Reply($"Server {s.Id} ({s.Name}): {(s.Active ? "active" : "not active")}, {force} - {s.Reason}.");
        }
    }

    /// <summary>/rc server names ilt [add|remove &lt;pattern&gt;]: Infinite Leaftide's world-name patterns.</summary>
    private static void HandleServerNames(string[] words)
    {
        if (words.Length < 2 || !words[1].Equals(IltInfo.ServerId, StringComparison.OrdinalIgnoreCase))
        {
            Reply("Usage: /rc server names ilt [add|remove <pattern>] (other servers' lists come from their plugins).");
            return;
        }
        if (words.Length >= 4)
        {
            bool add = words[2].Equals("add", StringComparison.OrdinalIgnoreCase);
            if (!add && !words[2].Equals("remove", StringComparison.OrdinalIgnoreCase))
            {
                Reply("Usage: /rc server names ilt [add|remove <pattern>]");
                return;
            }
            string pattern = string.Join(' ', words, 3, words.Length - 3);
            bool changed = Plugins.EngineSettings.EditIltServerName(pattern, add);
            Reply(changed
                ? $"Infinite Leaftide world names: {(add ? "added" : "removed")} \"{pattern}\" (engine.json)."
                : $"Infinite Leaftide world names: \"{pattern}\" was {(add ? "already there (or too long)" : "not there")}.");
            IltInfo.Invalidate();
            UI.Sections.SectionRegistry.Invalidate();
        }
        else if (words.Length == 3)
        {
            Reply("Usage: /rc server names ilt [add|remove <pattern>]");
            return;
        }
        Reply($"Infinite Leaftide lists: {IltInfo.ListsDescription}.");
    }

    /// <summary>
    /// /rc ilt: the Infinite Leaftide verdict, its signals and the lists in force. "on" /
    /// "off" force it, saved for this character (this session only before login); "auto" goes
    /// back to detecting. The lists are engine.json's IltServerNames / IltHosts.
    /// </summary>
    private static void HandleIltCommand(string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on": case "ilt": SetServerForce(IltInfo.ServerId, UI.Sections.ServerForce.On); break;
            case "off": case "other": SetServerForce(IltInfo.ServerId, UI.Sections.ServerForce.Off); break;
            case "auto": SetServerForce(IltInfo.ServerId, UI.Sections.ServerForce.Auto); break;
            case "": break;
            default:
                Reply("Usage: /rc ilt [auto|on|off]");
                return;
        }
        IltVerdict v = IltInfo.Current;
        Reply($"ILT: {(v.IsIlt ? "Infinite Leaftide" : "not Infinite Leaftide")} - {v.Reason}.");
        Reply($"Signals: host {ServerInfo.HostDescription}; world name {AccountHooks.AnnouncedWorldName ?? "(not announced)"}; "
            + $"override {IltInfo.Override}. Lists: {IltInfo.ListsDescription}.");
        bool bounties = ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.BountiesFace.Title);
        bool guardian = ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.GuardianFace.Title);
        Reply(bounties ? "RynthILT: installed (Bounties: the ILT bar button or /rc bounties)."
            : "RynthILT: not installed (no Bounties panel).");
        Reply(guardian ? "RynthAi: installed (Guardian: the ILT bar button or /rc guardian; the ILT tabs: the Mini Remote)."
            : "RynthAi: not installed (no Guardian panel or ILT tabs).");
    }

    private static void HandleMasteryCommand()
    {
        if (!ServerInfo.IsAelrynth)
        {
            Reply("Mastery: not on Aelrynth, so nothing is asked (/rc server for why).");
            return;
        }
        MasterySnapshot? s = MasteryFeed.Snapshot;
        Reply($"Mastery: {MasteryFeed.Status}"
            + (s != null ? $"; last reply {(Environment.TickCount64 - s.ReceivedMs) / 1000}s ago" : "; no reply this login")
            + (MasteryFeed.Unsupported ? "" : ". Asking again."));
        MasteryFeed.RequestRefresh();
    }

    /// <summary>
    /// /rc worldlayer: report (no argument / status) or force (auto, uipass, transition,
    /// endscene) where the world overlays draw. The render thread picks the mode up next
    /// frame; the DIP hook is switched at the next frame boundary.
    /// </summary>
    private static void HandleWorldLayerCommand(string arg)
    {
        D3D9.WorldLayerMode? mode = arg.ToLowerInvariant() switch
        {
            "auto" => D3D9.WorldLayerMode.Auto,
            "uipass" or "ui" => D3D9.WorldLayerMode.UiPass,
            "transition" or "zenable" => D3D9.WorldLayerMode.Transition,
            "endscene" => D3D9.WorldLayerMode.EndScene,
            _ => null,
        };
        if (mode is { } m)
        {
            D3D9.Nav3DRenderInjector.Mode = m;
            Reply($"World overlays now: {D3D9.Nav3DRenderInjector.DescribePath()} (this session only).");
            return;
        }
        if (arg.Length > 0 && !arg.Equals("status", StringComparison.OrdinalIgnoreCase))
            Reply("Usage: /rc worldlayer [auto|uipass|transition|endscene]");
        Reply(D3D9.Nav3DRenderInjector.Describe());
        Reply(ImGuiBackend.UnderUiLayer.Describe());
    }

    /// <summary>
    /// "" / "toggle" → null (flip), "show" / "on" / "open" → true, "hide" / "off" / "close" →
    /// false. False for anything else.
    /// </summary>
    private static bool TryPanelMode(string args, out bool? show)
    {
        show = null;
        switch (args.Trim().ToLowerInvariant())
        {
            case "": case "toggle": return true;
            case "show": case "on": case "open": show = true; return true;
            case "hide": case "off": case "close": show = false; return true;
            default: return false;
        }
    }

    /// <summary>Opens (true), closes (false) or toggles (null) an ImGui panel, like its bar button.</summary>
    private static void SetPanelOpen(string title, bool? show)
    {
        if (show == null || show.Value != ImGuiBackend.ImGuiPanelHost.IsOpen(title))
            UI.PanelRouter.Toggle(title);
    }

    /// <summary>"/rc bounties|guardian [show|hide|toggle]": one of the ILT section's panels.</summary>
    private static void IltPanel(string title, string label, string plugin, string args)
    {
        if (!ImGuiBackend.ImGuiPanelHost.HasFace(title))
            Reply($"The {label} panel isn't available (the {plugin} plugin isn't installed, or the ImGui layer is off).");
        else if (!TryPanelMode(args, out bool? show))
            Reply($"Usage: /rc {label.ToLowerInvariant()} [show|hide|toggle]");
        else if (show != false && !IltInfo.IsIlt && !ImGuiBackend.ImGuiPanelHost.IsOpen(title))
            Reply($"{label} is an Infinite Leaftide feature, and this isn't Infinite Leaftide (/rc ilt for why).");
        else
            SetPanelOpen(title, show);
    }

    private static bool IsVerb(string sub, string verb, out string args)
    {
        args = string.Empty;
        if (sub.Equals(verb, StringComparison.OrdinalIgnoreCase)) return true;
        if (!sub.StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase)) return false;
        args = sub.Substring(verb.Length + 1).Trim();
        return true;
    }

    /// <summary>
    /// /rv plates (alias /rc plates): the RynthVision nameplates and combat text
    /// (ImGui/MonsterHud). Alone it toggles the monster plates; on|off, all on|off,
    /// self|debuffs|others|numbers|gains [sub] [on|off], all|engaged, dist|max|scale|
    /// opacity|lift &lt;n&gt;, names|hp|level|weak|distance|fade|click [on|off], reset, status.
    /// Every change is saved.
    /// </summary>
    private static void HandlePlatesCommand(string args)
    {
        const string usage = "Usage: /rv plates [on|off] | all on|off | self [numbers|name|firstperson|lock] [on|off] | " +
                             "self fixed|follow|unlock|reset | " +
                             "debuffs [on|off] | others [on|off] | numbers [dealt|taken|heals|kills] [on|off] | " +
                             "gains [xp|lum|radiance] [on|off] | npcs [on|off] | npcs dist <yd>|max <n> | players [on|off] | all|engaged |dist <yd>|max <n>|scale <x>|opacity <x>|lift <m> | " +
                             "names|hp|level|weak|distance|fade|click [on|off] | reset | status | test";
        if (!ImGuiBackend.Hud.HudFeed.VisionInstalled)
        {
            Reply("RynthVision isn't in the plugin list (launcher, Plugins tab), so its nameplates and combat text are off.");
            return;
        }
        if (!ImGuiBackend.Hud.MonsterHudSettings.Loaded)
        {
            ImGuiBackend.Hud.MonsterHudSettings.EnsureLoadQueued();
            Reply("Nameplate settings are still loading - try again in a moment.");
            return;
        }

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        string? value = parts.Length > 1 ? parts[1] : null;
        string? sub = value != null && !IsOnOff(value) ? value.ToLowerInvariant() : null;
        string? subValue = sub != null ? (parts.Length > 2 ? parts[2] : null) : value;
        bool save = true;

        switch (verb)
        {
            case "":
                S.Enabled = !S.Enabled;
                break;
            case "on": S.Enabled = true; break;
            case "off": S.Enabled = false; break;
            case "all":
                // "all on|off": every overlay; "all" alone: plates for all monsters (the filter).
                if (value != null && IsOnOff(value)) S.SetAll(Flag(value, true));
                else S.Filter = ImGuiBackend.Hud.PlateFilter.All;
                break;
            case "engaged":
            case "combat":
            case "target":
                S.Filter = ImGuiBackend.Hud.PlateFilter.Engaged; break;
            case "self":
            case "me":
                switch (sub)
                {
                    case null: S.SelfPlate = Flag(subValue, S.SelfPlate); break;
                    case "numbers": case "text": S.SelfNumbers = Flag(subValue, S.SelfNumbers); break;
                    case "name": S.SelfName = Flag(subValue, S.SelfName); break;
                    case "firstperson": case "fp": S.SelfHideFirstPerson = Flag(subValue, S.SelfHideFirstPerson); break;
                    // Placement: fixed on screen (drag it while unlocked) or following the character.
                    case "fixed": case "screen": S.SelfPlacement = ImGuiBackend.Hud.SelfPlacement.Fixed; break;
                    case "follow": case "world": S.SelfPlacement = ImGuiBackend.Hud.SelfPlacement.Follow; break;
                    case "lock": S.SelfLocked = subValue == null || Flag(subValue, S.SelfLocked); break;
                    case "unlock": S.SelfLocked = false; break;
                    case "reset": S.SelfFixedX = S.DefaultSelfFixedX; S.SelfFixedY = S.DefaultSelfFixedY; break;
                    case "position": case "pos":
                    {
                        string? w = subValue?.ToLowerInvariant();
                        ImGuiBackend.Hud.SelfPlatePosition? p = w switch
                        {
                            "above" or "over" or "top" or "head" => ImGuiBackend.Hud.SelfPlatePosition.Above,
                            "below" or "under" or "feet" or "bottom" => ImGuiBackend.Hud.SelfPlatePosition.Below,
                            "left" => ImGuiBackend.Hud.SelfPlatePosition.Left,
                            "right" => ImGuiBackend.Hud.SelfPlatePosition.Right,
                            _ => null,
                        };
                        if (p == null) { Reply("usage: /rv plates self position above|below|left|right"); return; }
                        S.SelfPosition = p.Value;
                        break;
                    }
                    default: Reply(usage); return;
                }
                break;
            case "click":
            case "clickselect":
                S.ClickToSelect = Flag(value, S.ClickToSelect); break;
            case "onscreen":
            case "keeponscreen":
                S.KeepOnScreen = Flag(value, S.KeepOnScreen); break;
            case "debuffs":
            case "debuff":
                S.ShowDebuffs = Flag(value, S.ShowDebuffs); break;
            case "others":
                S.ShowOthersDebuff = Flag(value, S.ShowOthersDebuff); break;
            case "numbers":
            case "juice":
                switch (sub)
                {
                    case null: S.Numbers = Flag(subValue, S.Numbers); break;
                    case "dealt": case "damage": S.NumDealt = Flag(subValue, S.NumDealt); break;
                    case "taken": S.NumTaken = Flag(subValue, S.NumTaken); break;
                    case "heals": case "heal": S.NumHeals = Flag(subValue, S.NumHeals); break;
                    case "kills": case "kill": case "burst": S.NumKills = Flag(subValue, S.NumKills); break;
                    case "restores": case "stamina": case "mana": S.NumRestores = Flag(subValue, S.NumRestores); break;
                    case "size":
                        if (!TryNum(subValue, out float nsz)) { Reply("usage: /rv plates numbers size 0.5-2.5"); return; }
                        S.NumSize = Math.Clamp(nsz, S.MinNumSize, S.MaxNumSize); break;
                    case "time": case "duration":
                        if (!TryNum(subValue, out float ntm)) { Reply("usage: /rv plates numbers time 0.5-3"); return; }
                        S.NumTime = Math.Clamp(ntm, S.MinNumTime, S.MaxNumTime); break;
                    default: Reply(usage); return;
                }
                break;
            case "gains":
            case "gain":
                switch (sub)
                {
                    case "size":
                        if (!TryNum(subValue, out float gsz)) { Reply("usage: /rv plates gains size 0.5-2.5"); return; }
                        S.GainSize = Math.Clamp(gsz, S.MinNumSize, S.MaxNumSize); break;
                    case "time": case "duration":
                        if (!TryNum(subValue, out float gtm)) { Reply("usage: /rv plates gains time 0.5-3"); return; }
                        S.GainTime = Math.Clamp(gtm, S.MinNumTime, S.MaxNumTime); break;
                    case null: S.Gains = Flag(subValue, S.Gains); break;
                    case "xp": S.GainXp = Flag(subValue, S.GainXp); break;
                    case "lum": case "luminance": S.GainLum = Flag(subValue, S.GainLum); break;
                    case "radiance": S.GainRadiance = Flag(subValue, S.GainRadiance); break;
                    default: Reply(usage); return;
                }
                break;
            case "dist":
            case "range":
                if (!TryNum(value, out float d)) { Reply(usage); return; }
                S.MaxDistance = Math.Clamp(d, S.MinDistance, S.MaxDistanceLimit);
                break;
            case "max":
                if (!TryNum(value, out float n)) { Reply(usage); return; }
                S.MaxPlates = Math.Clamp((int)Math.Round(n), S.MinPlates, S.MaxPlatesLimit);
                break;
            case "scale":
                if (!TryNum(value, out float sc)) { Reply(usage); return; }
                S.Scale = Math.Clamp(sc, S.MinScale, S.MaxScale);
                break;
            case "opacity":
            case "alpha":
                if (!TryNum(value, out float o)) { Reply(usage); return; }
                if (o > 1f) o /= 100f;   // "opacity 80" means 80 %
                S.Opacity = Math.Clamp(o, S.MinOpacity, S.MaxOpacity);
                break;
            case "lift":
            case "height":
                if (!TryNum(value, out float h)) { Reply(usage); return; }
                S.HeightLift = Math.Clamp(h, S.MinLift, S.MaxLift);
                break;
            case "npcs":
            case "npc":
                switch (sub)
                {
                    case null: S.NpcNames = Flag(subValue, S.NpcNames); break;
                    case "dist": case "range":
                        if (!TryNum(subValue, out float nd)) { Reply("usage: /rv plates npcs dist 5-80"); return; }
                        S.NpcMaxDistance = Math.Clamp(nd, S.MinNpcDistance, S.MaxNpcDistanceLimit); break;
                    case "max":
                        if (!TryNum(subValue, out float nm)) { Reply("usage: /rv plates npcs max 1-40"); return; }
                        S.MaxNpcLabels = Math.Clamp((int)Math.Round(nm), S.MinNpcLabels, S.MaxNpcLabelsLimit); break;
                    default: Reply(usage); return;
                }
                break;
            case "players":
                S.PlayerNames = Flag(value, S.PlayerNames); break;
            case "playerbars": S.PlayerBars = Flag(value, S.PlayerBars); break;
            case "names": S.ShowNames = Flag(value, S.ShowNames); break;
            case "hp": S.ShowHpNumbers = Flag(value, S.ShowHpNumbers); break;
            case "level": S.ShowLevel = Flag(value, S.ShowLevel); break;
            case "weak": S.ShowWeakness = Flag(value, S.ShowWeakness); break;
            case "distance": S.ShowDistance = Flag(value, S.ShowDistance); break;
            case "fade": S.FadeWithDistance = Flag(value, S.FadeWithDistance); break;
            case "reset":
                S.ResetToDefaults();
                save = false;
                break;
            case "status":
            case "diag":
                foreach (string line in ImGuiBackend.Hud.MonsterHud.DescribeLines())
                    Reply(line);
                return;
            case "test":
                ImGuiBackend.Hud.CombatText.SpawnTest();
                Reply("Sample combat numbers, a kill burst and gains spawned around you.");
                return;
            default:
                Reply(usage);
                return;
        }

        if (save) S.Save();
        Reply(S.Describe()
              + (Plugins.EngineSettings.EnableImGuiBackend ? "" : " (The ImGui layer is off, so nothing draws: /rc imgui on.)"));
    }

    private static bool IsOnOff(string v) =>
        v.Equals("on", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase) ||
        v == "1" || v == "0" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("toggle", StringComparison.OrdinalIgnoreCase);

    private static bool TryNum(string? s, out float v)
    {
        v = 0;
        return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
    }

    /// <summary>
    /// /rc hooks: one line per UI hook (UiHookRegistry: UseNewMode, ClientCleanup,
    /// StartTooltip, ResetTooltip, CheckTooltip, StartDragandDrop, CatchDroppedItem) with
    /// its address, how it was found, on/off and how often it fired, then the screen and
    /// tooltip/drag state. "/rc hooks off &lt;name&gt;" makes that hook pass straight to AC
    /// now and leaves it out at the next start (engine.json DisabledUiHooks); "on" undoes it.
    /// </summary>
    private static void HandleHooksCommand(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && (parts[0].Equals("on", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            Reply(UiHookRegistry.SetEnabled(parts[1], parts[0].Equals("on", StringComparison.OrdinalIgnoreCase)));
            return;
        }
        if (parts.Length != 0 && !(parts.Length == 1 && parts[0].Equals("status", StringComparison.OrdinalIgnoreCase)))
        {
            Reply("Usage: /rc hooks [status] | /rc hooks on|off <name>");
            return;
        }
        foreach (string line in UiHookRegistry.DescribeLines())
            Reply(line);
    }

    /// <summary>"on"/"off" sets; anything else (or nothing) toggles.</summary>
    private static bool Flag(string? value, bool current) =>
        value?.ToLowerInvariant() switch { "on" or "1" or "true" => true, "off" or "0" or "false" => false, _ => !current };

    /// <summary>
    /// /rc discord: the relay's switch, status, test and resume (UI.Data.Discord.ChatDiscord);
    /// "window" opens its panel, like Chat > gear > Discord settings.
    /// </summary>
    private static void HandleDiscordCommand(string args)
    {
        if (!ImGuiBackend.ImGuiPanelHost.HasFace(ImGuiBackend.Panels.ChatDiscordFace.Title))
        {
            Reply("The Discord relay needs the RynthChat plugin.");
            return;
        }
        string verb = args.Trim();
        if (verb.Equals("window", StringComparison.OrdinalIgnoreCase) || verb.Equals("show", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("settings", StringComparison.OrdinalIgnoreCase))
        {
            UI.PanelRouter.Toggle(ImGuiBackend.Panels.ChatDiscordFace.Title);
            return;
        }
        foreach (string line in UI.Data.Discord.ChatDiscord.HandleCommand(verb))
            Reply(line);
    }

    private static void Reply(string text)
    {
        RynthLog.Compat($"RynthCoreChatCommands: {text}");
        AcMainThreadQueue.EnqueueWriteToChat("[RynthCore] " + text, 1);
    }
}
