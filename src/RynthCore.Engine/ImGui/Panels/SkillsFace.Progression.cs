// ============================================================================
//  RynthCore.Engine - ImGui/Panels/SkillsFace.Progression.cs
//  The Skills panel's Progression tab, shown on ILT-like worlds only:
//    - XP planner: exact XP for the next N raises of every attribute and vital,
//      from the client's XP tables (raising stays on the Attributes & Vitals tab).
//    - Attribute raiser: infinite attributes past the retail cap. RynthAi reads the
//      server's costs ("/xp all") and raises with "/attr", by hand (+1 / +10) or on a
//      timer from unassigned XP, in the order and mode set here.
//    - Augmentations / Enlightenment: RynthAi's ILT Hub planners. RynthAi owns the
//      math and the saved inputs (UiSources.Progression snapshot); edits and
//      actions go back as "prog ..." remote commands. /enl and arming
//      auto-enlighten ask here first; the server still shows its own dialog.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed partial class SkillsFace
{
    private const string ProgConfirmId = "##skills_prog_confirm";

    private int _planLevels = 10;          // XP planner: raises to price per row
    private bool _progSubscribed;

    // The confirm the Progression tab asked for; the command is sent only on its Yes button.
    private string _progConfirmTitle = "", _progConfirmText = "", _progConfirmYes = "", _progConfirmCommand = "";
    private bool _progConfirmOpenRequested;

    // Set by ShowProgression (any panel, AC thread); the tab is selected once RynthAi's snapshot
    // says it exists (the first poll lands a step after the panel opens).
    private static bool _progressionRequested;

    /// <summary>RynthAi reports an ILT-like world (its ILT Hub exists and the server has ILT features).</summary>
    private static bool ProgressionTabVisible => UiSources.Progression.Current?.Value.Available == true;

    /// <summary>Opens the Skills panel (if closed) on the Progression tab. AC thread.</summary>
    public static void ShowProgression()
    {
        _progressionRequested = true;
        if (!ImGuiPanelHost.IsOpen(Title)) PanelRouter.Toggle(Title);
    }

    /// <summary>Draw: applies a pending ShowProgression once the tab exists.</summary>
    private void ApplyProgressionRequest()
    {
        if (!_progressionRequested || !ProgressionTabVisible) return;
        _progressionRequested = false;
        _tab = 2;
    }

    private void ProgressionShown()
    {
        if (_progSubscribed) return;
        _progSubscribed = true;
        UiSources.Progression.Subscribe();
        UiSources.Progression.RequestRefresh();
    }

    private void ProgressionHidden()
    {
        _progressionRequested = false;
        if (!_progSubscribed) return;
        _progSubscribed = false;
        UiSources.Progression.Unsubscribe();
    }

    /// <summary>Sends "prog &lt;value&gt;" to RynthAi; the snapshot re-polls after its next tick applies it.</summary>
    private static void SendProg(string value)
    {
        RynthAiCommands.ApplyRemoteCommand("prog", value);
        UiSources.Progression.RequestRefreshAfterPluginTick();
    }

    private void AskProgConfirm(string title, string text, string yes, string command)
    {
        _progConfirmTitle = title;
        _progConfirmText = text;
        _progConfirmYes = yes;
        _progConfirmCommand = command;
        _progConfirmOpenRequested = true;
    }

    // ── Body ────────────────────────────────────────────────────────────

    private void DrawProgression(float w)
    {
        ProgressionInfo? info = UiSources.Progression.Current?.Value;
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));
        try
        {
            DrawXpPlanner(w);
            ImGuiNET.ImGui.Dummy(new Vector2(0, 6));
            if (info == null || !info.Available)
            {
                Message("Augmentations and Enlightenment need RynthAi's ILT Hub on an ILT world.", w);
                return;
            }
            DrawAttrRaiser(info.Attr, w);
            ImGuiNET.ImGui.Dummy(new Vector2(0, 6));
            DrawAugPlanner(info.Aug, w);
            ImGuiNET.ImGui.Dummy(new Vector2(0, 6));
            DrawEnlPlanner(info.Enl, w);
            ImGuiNET.ImGui.Dummy(new Vector2(0, 8));
        }
        finally
        {
            ImGuiNET.ImGui.PopStyleVar();
        }
    }

    private void DrawXpPlanner(float w)
    {
        Section("XP planner", w);
        if (_snap == null || _dat == null || _attributes.Count == 0)
        {
            Message("Waiting for your character's data...", w);
            return;
        }
        ImGuiNET.ImGui.Indent(8);
        long have = _snap.UnassignedXp;
        Label($"Unassigned XP: {have:N0}", have > 0 ? Teal : Mute);
        ImGuiNET.ImGui.SetNextItemWidth(160);
        ImGuiNET.ImGui.SliderInt("Raises to plan##prog_lv", ref _planLevels, 1, 100);

        if (ImGuiNET.ImGui.BeginTable("##prog_xp", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp,
                new Vector2(w - 16, 0)))
        {
            ImGuiNET.ImGui.TableSetupColumn("Stat");
            ImGuiNET.ImGui.TableSetupColumn("Base", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGuiNET.ImGui.TableSetupColumn("Next raise", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGuiNET.ImGui.TableSetupColumn($"Next {_planLevels}", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGuiNET.ImGui.TableHeadersRow();
            foreach (Row r in _attributes) PlannerRow(r, have);
            foreach (Row r in _vitals) PlannerRow(r, have);
            ImGuiNET.ImGui.EndTable();
        }
        Label("Exact costs from the client's XP tables. Raise on the Attributes & Vitals tab"
              + (ProgressionTabVisible ? ", or past the cap with the Attribute raiser below." : "."), Dim);
        ImGuiNET.ImGui.Unindent(8);
    }

    private void PlannerRow(Row r, long have)
    {
        ImGuiNET.ImGui.TableNextRow();
        ImGuiNET.ImGui.TableNextColumn(); ImGuiNET.ImGui.TextUnformatted(r.Name);
        ImGuiNET.ImGui.TableNextColumn(); ImGuiNET.ImGui.TextUnformatted(r.Base.ToString(CultureInfo.InvariantCulture));
        uint[]? table = r.Table;
        int left = table == null ? 0 : Math.Max(0, table.Length - 1 - (int)Math.Min(r.Ranks, (uint)int.MaxValue));
        ImGuiNET.ImGui.TableNextColumn();
        if (table == null || left == 0) { Label(table == null ? "-" : "max", Dim); ImGuiNET.ImGui.TableNextColumn(); Label("-", Dim); return; }
        long one = SkillDat.CostOf(table, r.Ranks, r.Spent, 1);
        Label(one >= 0 ? one.ToString("N0", CultureInfo.InvariantCulture) : "-", one >= 0 && one <= have ? Teal : Dim);
        ImGuiNET.ImGui.TableNextColumn();
        int n = Math.Min(_planLevels, left);
        long many = SkillDat.CostOf(table, r.Ranks, r.Spent, n);
        string text = many >= 0 ? many.ToString("N0", CultureInfo.InvariantCulture) : "-";
        if (n < _planLevels) text += $" ({n} to max)";
        Label(text, many >= 0 && many <= have ? Teal : Dim);
    }

    private static readonly string[] AttrModes =
    {
        "Priority - top of the order first",
        "Round robin - one level each, in order",
        "Cheapest next level first",
    };

    private string? _attrKeepEdit;   // reserve box being typed in (null = show RynthAi's value)
    private int? _attrEveryEdit;     // interval box being typed in

    private void DrawAttrRaiser(AttrRaiser? a, float w)
    {
        Section("Attribute raiser (infinite attributes)", w);
        ImGuiNET.ImGui.Indent(8);
        float wrap = ImGuiNET.ImGui.GetCursorPosX() + w - 24;
        ImGuiNET.ImGui.PushTextWrapPos(wrap);
        try
        {
            if (a == null) { Label("Update RynthAi to use the attribute raiser.", Mute); return; }
            if (a.Off) { Label(a.Reason, Mute); return; }

            Label(a.Unassigned, Teal);
            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            if (a.Running)
            {
                if (Button("##attr_stop", "Stop", p, new Vector2(90, 24), Text, StopBg)) SendProg("attrstop");
            }
            else if (Button("##attr_run", "Raise now", p, new Vector2(110, 24), Text, StartBg, border: Teal))
                SendProg("attrrun");
            if (Button("##attr_costs", "Refresh costs", new Vector2(p.X + 116, p.Y), new Vector2(110, 24), Text, BtnFill))
                SendProg("attrcosts");
            NextLine(p, 28);
            if (a.Status.Length > 0) ImGuiNET.ImGui.TextWrapped(a.Status);
            if (a.LastRun.Length > 0) Label("Last run: " + a.LastRun, Dim);

            ImGuiNET.ImGui.Separator();
            bool auto = a.Auto;
            if (ImGuiNET.ImGui.Checkbox("Auto-raise from unassigned XP##attr_auto", ref auto))
                SendProg("attrauto " + (auto ? "on" : "off"));
            ImGuiNET.ImGui.SameLine();
            // Commit on Enter, the +/- buttons or focus loss (not on every keystroke: each send
            // restarts the auto-raise timer).
            int every = _attrEveryEdit ?? a.Every;
            ImGuiNET.ImGui.SetNextItemWidth(90);
            bool everyCommit = ImGuiNET.ImGui.InputInt("every (min)##attr_every", ref every, 1, 10, ImGuiInputTextFlags.EnterReturnsTrue);
            bool everyActive = ImGuiNET.ImGui.IsItemActive();
            if (everyCommit || ImGuiNET.ImGui.IsItemDeactivatedAfterEdit())
            {
                _attrEveryEdit = null;
                if (every != a.Every) SendProg("attrevery " + Math.Clamp(every, 1, 1440).ToString(CultureInfo.InvariantCulture));
            }
            else _attrEveryEdit = everyActive ? every : null;
            if (a.Auto && a.Next.Length > 0) Label($"Next auto-raise: {a.Next}", Dim);

            int mode = Math.Clamp(a.Mode, 0, AttrModes.Length - 1);
            ImGuiNET.ImGui.SetNextItemWidth(260);
            if (ImGuiNET.ImGui.Combo("Raise order##attr_mode", ref mode, AttrModes, AttrModes.Length))
                SendProg("attrmode " + mode.ToString(CultureInfo.InvariantCulture));

            // The box holds the exact reserve (KeepFull); it is sent only when the text changed, so an
            // Enter on an untouched box can't round the reserve.
            string shownKeep = a.KeepFull.Length > 0 ? a.KeepFull : a.Keep;
            string keep = _attrKeepEdit ?? shownKeep;
            ImGuiNET.ImGui.SetNextItemWidth(140);
            bool enter = ImGuiNET.ImGui.InputTextWithHint("Keep in reserve (XP)##attr_keep", "0, 500m, 2b", ref keep, 32u,
                ImGuiInputTextFlags.EnterReturnsTrue);
            bool keepActive = ImGuiNET.ImGui.IsItemActive();
            if (enter || ImGuiNET.ImGui.IsItemDeactivatedAfterEdit())
            {
                _attrKeepEdit = null;
                if (!string.Equals(keep.Trim(), shownKeep, StringComparison.Ordinal))
                    SendProg("attrkeep " + keep.Replace(" ", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal));
            }
            else _attrKeepEdit = keepActive ? keep : null;
            ImGuiNET.ImGui.SetItemTooltip("Unassigned XP the raiser never spends. Accepts k / m / b / t suffixes.");

            DrawAttrTable(a, w);
            Label("Tick the stats Raise now / auto-raise may spend on; arrows set the order. " + a.CostsAge + ".", Dim);
        }
        finally
        {
            ImGuiNET.ImGui.PopTextWrapPos();
            ImGuiNET.ImGui.Unindent(8);
        }
    }

    private void DrawAttrTable(AttrRaiser a, float w)
    {
        if (!ImGuiNET.ImGui.BeginTable("##prog_attr", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp,
                new Vector2(w - 16, 0)))
            return;
        ImGuiNET.ImGui.TableSetupColumn("Raise", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGuiNET.ImGui.TableSetupColumn("Stat");
        ImGuiNET.ImGui.TableSetupColumn("Base", ImGuiTableColumnFlags.WidthFixed, 56);
        ImGuiNET.ImGui.TableSetupColumn("Next level", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGuiNET.ImGui.TableSetupColumn("Order / by hand", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGuiNET.ImGui.TableHeadersRow();
        for (int i = 0; i < a.Rows.Length; i++)
        {
            AttrRow r = a.Rows[i];
            ImGuiNET.ImGui.PushID("attr_" + r.Key);
            ImGuiNET.ImGui.TableNextRow();

            ImGuiNET.ImGui.TableNextColumn();
            bool on = r.On;
            if (ImGuiNET.ImGui.Checkbox("##on", ref on)) SendProg($"attron {r.Key} {(on ? "on" : "off")}");

            ImGuiNET.ImGui.TableNextColumn();
            Label($"{i + 1}. {r.Label}", r.On ? Text : Mute);

            ImGuiNET.ImGui.TableNextColumn();
            int clientBase = ClientBase(r);
            Label(clientBase > 0 ? clientBase.ToString("N0", CultureInfo.InvariantCulture)
                  : r.Base >= 0 ? r.Base.ToString("N0", CultureInfo.InvariantCulture) : "-", Text);

            ImGuiNET.ImGui.TableNextColumn();
            Label(r.Cost, r.Afford ? Teal : Dim);

            ImGuiNET.ImGui.TableNextColumn();
            ImGuiNET.ImGui.BeginDisabled(i == 0);
            if (ImGuiNET.ImGui.ArrowButton("##up", ImGuiDir.Up)) SendProg($"attrmove {r.Key} up");
            ImGuiNET.ImGui.EndDisabled();
            ImGuiNET.ImGui.SameLine(0, 2);
            ImGuiNET.ImGui.BeginDisabled(i == a.Rows.Length - 1);
            if (ImGuiNET.ImGui.ArrowButton("##down", ImGuiDir.Down)) SendProg($"attrmove {r.Key} down");
            ImGuiNET.ImGui.EndDisabled();
            ImGuiNET.ImGui.SameLine(0, 6);
            ImGuiNET.ImGui.BeginDisabled(a.Running);
            if (ImGuiNET.ImGui.SmallButton("+1")) SendProg($"attrraise {r.Key} 1");
            ImGuiNET.ImGui.SameLine(0, 2);
            if (ImGuiNET.ImGui.SmallButton("+10")) SendProg($"attrraise {r.Key} 10");
            ImGuiNET.ImGui.EndDisabled();
            ImGuiNET.ImGui.PopID();
        }
        ImGuiNET.ImGui.EndTable();
    }

    /// <summary>The client's own base value for a raiser row (0 when the snapshot has none).</summary>
    private int ClientBase(AttrRow r)
    {
        List<Row> rows = r.Vital ? _vitals : _attributes;
        foreach (Row row in rows)
            if (row.Name.Equals(r.Label, StringComparison.OrdinalIgnoreCase)) return row.Base;
        return 0;
    }

    private void DrawAugPlanner(AugPlanner a, float w)
    {
        Section("Augmentations", w);
        ImGuiNET.ImGui.Indent(8);
        try
        {
            if (a.Off) { Label("/aug is not available on this server.", Mute); return; }
            if (ImGuiNET.ImGui.SmallButton("Load /aug##prog")) SendProg("augload");
            ImGuiNET.ImGui.SameLine();
            Label(a.Status, Dim);

            string lpc = a.LumPerCoin.ToString(CultureInfo.InvariantCulture);
            ImGuiNET.ImGui.SetNextItemWidth(160);
            if (ImGuiNET.ImGui.InputTextWithHint("Lum per Enlightened Coin##prog_lpc", "0 = don't price coins", ref lpc, 24u))
                SendProg("lumpercoin " + lpc.Replace(" ", "", StringComparison.Ordinal));

            if (ImGuiNET.ImGui.BeginTable("##prog_augs", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp,
                    new Vector2(w - 16, 0)))
            {
                ImGuiNET.ImGui.TableSetupColumn("Aug");
                ImGuiNET.ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthFixed, 56);
                ImGuiNET.ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthFixed, 86);
                ImGuiNET.ImGui.TableSetupColumn("Luminance", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGuiNET.ImGui.TableSetupColumn("Coins", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGuiNET.ImGui.TableHeadersRow();
                foreach (AugRow row in a.Rows)
                {
                    ImGuiNET.ImGui.TableNextRow();
                    ImGuiNET.ImGui.TableNextColumn(); ImGuiNET.ImGui.TextUnformatted(row.Label);
                    ImGuiNET.ImGui.TableNextColumn(); ImGuiNET.ImGui.TextUnformatted(row.Current.ToString(CultureInfo.InvariantCulture));
                    ImGuiNET.ImGui.TableNextColumn();
                    int tgt = row.Target;
                    ImGuiNET.ImGui.SetNextItemWidth(80);
                    if (ImGuiNET.ImGui.InputInt("##prog_tgt_" + row.Key, ref tgt, 0))
                        SendProg($"augtarget {row.Key} {Math.Clamp(tgt, 0, Math.Max(row.Cap, 0))}");
                    ImGuiNET.ImGui.TableNextColumn(); ImGuiNET.ImGui.TextUnformatted(row.Lum);
                    ImGuiNET.ImGui.TableNextColumn(); ImGuiNET.ImGui.TextUnformatted(row.Coins > 0 ? row.Coins.ToString(CultureInfo.InvariantCulture) : "-");
                }
                ImGuiNET.ImGui.EndTable();
            }
            Label(a.Total, Amber);
            if (a.Short.Length > 0) Label(a.Short, Text);
            Label(a.Banked, Dim);
        }
        finally
        {
            ImGuiNET.ImGui.Unindent(8);
        }
    }

    private void DrawEnlPlanner(EnlPlanner e, float w)
    {
        Section("Enlightenment", w);
        ImGuiNET.ImGui.Indent(8);
        float wrap = ImGuiNET.ImGui.GetCursorPosX() + w - 24;   // window-local x
        ImGuiNET.ImGui.PushTextWrapPos(wrap);
        try
        {
            if (e.Off) { Label("/enl is not available on this server.", Mute); return; }
            Label(e.Header, Text);
            Label(e.Next, Teal);

            int target = e.Target;
            ImGuiNET.ImGui.SetNextItemWidth(100);
            if (ImGuiNET.ImGui.InputInt("Plan to level##prog_enlt", ref target))
                SendProg("enltarget " + Math.Max(e.Level + 1, target).ToString(CultureInfo.InvariantCulture));
            foreach (string line in e.Plan) ImGuiNET.ImGui.BulletText(line);

            int coinsPerToken = e.CoinsPerToken;
            ImGuiNET.ImGui.SetNextItemWidth(100);
            if (ImGuiNET.ImGui.InputInt("Coins per token (shop price)##prog_cpt", ref coinsPerToken))
                SendProg("coinspertoken " + Math.Max(0, coinsPerToken).ToString(CultureInfo.InvariantCulture));
            if (e.TokensShort.Length > 0) Label(e.TokensShort, Dim);

            ImGuiNET.ImGui.Separator();
            if (e.Blockers.Length == 0) Label("Client-side checks pass.", Green);
            foreach (string b in e.Blockers) Label(b, Amber);
            Label(e.ServerChecks, Dim);

            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            if (Button("##prog_enl", "Enlighten now...", p, new Vector2(150, 24), Text, BtnFill, border: Teal))
                AskProgConfirm("Enlighten",
                    "Send /enl now?\n\nEnlightening resets your level and wipes unassigned XP.\n"
                    + "The server will show its own Yes/No dialog - you must click Yes there.",
                    "Send /enl", "enlighten");
            NextLine(p, 28);

            ImGuiNET.ImGui.Separator();
            bool auto = e.Auto;
            if (ImGuiNET.ImGui.Checkbox("Auto-enlighten when ready##prog_auto", ref auto))
            {
                if (auto)
                    AskProgConfirm("Arm auto-enlighten",
                        "Auto-enlighten will send /enl whenever the checks pass (peace mode, level, materials).\n"
                        + "You still have to click Yes in the AC dialog each time.\n\nArm it for this session?",
                        "Arm", "autoenl on");
                else
                    SendProg("autoenl off");
            }
            if (e.Auto && !e.Armed)
            {
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("Arm for this session...##prog_arm"))
                    AskProgConfirm("Arm auto-enlighten", "Arm auto-enlighten for this session?", "Arm", "autoenl arm");
            }
            bool spend = e.SpendFirst;
            if (ImGuiNET.ImGui.Checkbox("Spend unassigned XP on attributes first##prog_spend", ref spend))
                SendProg("spendfirst " + (spend ? "on" : "off"));
            int every = e.CheckEvery;
            ImGuiNET.ImGui.SetNextItemWidth(100);
            if (ImGuiNET.ImGui.InputInt("Check every (s)##prog_every", ref every))
                SendProg("checkevery " + Math.Max(10, every).ToString(CultureInfo.InvariantCulture));
            Label(e.Armed ? "Armed." : "Disarmed.", e.Armed ? Amber : Dim);
            if (e.Status.Length > 0) ImGuiNET.ImGui.TextWrapped(e.Status);
        }
        finally
        {
            ImGuiNET.ImGui.PopTextWrapPos();
            ImGuiNET.ImGui.Unindent(8);
        }
    }

    // ── Confirm ─────────────────────────────────────────────────────────

    /// <summary>The Progression tab's Yes/No popup (outside the body child, like DrawConfirm).</summary>
    private void DrawProgressionConfirm()
    {
        if (_progConfirmOpenRequested)
        {
            _progConfirmOpenRequested = false;
            ImGuiNET.ImGui.OpenPopup(ProgConfirmId);
        }
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        bool open = ImGuiNET.ImGui.BeginPopup(ProgConfirmId);
        ImGuiNET.ImGui.PopStyleVar(2);
        ImGuiNET.ImGui.PopStyleColor(2);
        if (!open) return;
        try
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
            Label(_progConfirmTitle, Amber);
            ImGuiNET.ImGui.PopFont();
            ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + 320);
            Label(_progConfirmText, Text);
            ImGuiNET.ImGui.PopTextWrapPos();

            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos() + new Vector2(0, 4);
            if (Button("##prog_yes", _progConfirmYes, p, new Vector2(120, 24), Text, StartBg))
            {
                SendProg(_progConfirmCommand);
                ImGuiNET.ImGui.CloseCurrentPopup();
            }
            if (Button("##prog_no", "Cancel", new Vector2(p.X + 126, p.Y), new Vector2(90, 24), Text, BtnFill))
                ImGuiNET.ImGui.CloseCurrentPopup();
            NextLine(p, 28);
        }
        finally
        {
            ImGuiNET.ImGui.EndPopup();
        }
    }
}
