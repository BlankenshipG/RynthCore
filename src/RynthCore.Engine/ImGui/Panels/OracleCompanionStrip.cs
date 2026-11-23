// ============================================================================
//  RynthCore.Engine - ImGui/Panels/OracleCompanionStrip.cs
//  Header-style toggles drawn at the top of RynthOracle's main window (same
//  idea as RynthNav's Radar / Sense / Map buttons). Vision, Tracker and the
//  Infinite Leaftide (server plugin) leaf left the top bar 2026-10-16; they
//  still open through PanelRouter / OverlayHost / /rc ui — only the bar icon
//  moved here. ScriptWindowFace calls Draw() before replaying Oracle's tabs.
// ============================================================================

using System;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI;

namespace RynthCore.Engine.ImGuiBackend.Panels;

/// <summary>Companion toggles for panels that used to live on the top bar, now under Oracle.</summary>
internal static class OracleCompanionStrip
{
    /// <summary>ILT ImGui faces (RynthILT): one leaf stands for them, like the old bar button.</summary>
    private static readonly string[] IltPanels = { BountiesFace.Title, GuardianFace.Title };

    /// <summary>
    /// Draws Vision / Tracker / ILT toggles in the Oracle body (above the script tab strip).
    /// No-op when none of those faces are registered. AC thread.
    /// </summary>
    public static void Draw()
    {
        bool any = false;
        if (ImGuiPanelHost.HasFace(VisionFace.Title))
        {
            Toggle(VisionFace.Title, PhosphorIcons.Eye + " Vision",
                "Vision: terrain overlays, nameplates and combat text (RynthVision)");
            any = true;
        }
        if (ImGuiPanelHost.HasFace("Tracker"))
        {
            if (any) ImGui.SameLine();
            Toggle("Tracker", PhosphorIcons.ChartLineUp + " Tracker",
                "Tracker: session stats HUD (RynthTracker)");
            any = true;
        }

        int ilt = IltPanelCount();
        if (ilt > 0)
        {
            if (any) ImGui.SameLine();
            string? only = ilt == 1 ? FirstIltPanel() : null;
            bool open = only != null && ImGuiPanelHost.IsOpen(only);
            if (ImGui.Button((open ? PhosphorIcons.Leaf + " ILT*" : PhosphorIcons.Leaf + " ILT") + "##oracle_ilt"))
            {
                if (only != null) PanelRouter.Toggle(only);
                else ImGui.OpenPopup("##oracle_ilt_menu");
            }
            ImGui.SetItemTooltip(only != null
                ? "Infinite Leaftide: " + only
                : "Infinite Leaftide (Bounties, Guardian) — server plugin panels");
            any = true;
            IltMenu();
        }

        if (any) ImGui.Separator();
    }

    private static void Toggle(string title, string label, string tip)
    {
        bool open = ImGuiPanelHost.IsOpen(title);
        if (open) ImGui.PushStyleColor(ImGuiCol.Button, RynthTheme.Argb(0xFF1A2E38));
        if (ImGui.Button(label + "##oracle_" + title))
            PanelRouter.Toggle(title);
        if (open) ImGui.PopStyleColor();
        ImGui.SetItemTooltip(tip);
    }

    private static int IltPanelCount()
    {
        int n = 0;
        foreach (string title in IltPanels)
            if (ImGuiPanelHost.HasFace(title)) n++;
        return n > 0 && IltInfo.IsIlt && IltInfo.PluginLoaded ? n : 0;
    }

    private static string? FirstIltPanel()
    {
        foreach (string title in IltPanels)
            if (ImGuiPanelHost.HasFace(title)) return title;
        return null;
    }

    private static void IltMenu()
    {
        if (!ImGui.BeginPopup("##oracle_ilt_menu")) return;
        try
        {
            ImGui.TextDisabled("Infinite Leaftide");
            foreach (string title in IltPanels)
            {
                if (!ImGuiPanelHost.HasFace(title)) continue;
                bool open = ImGuiPanelHost.IsOpen(title);
                if (ImGui.MenuItem(title, "", open))
                    PanelRouter.Toggle(title);
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }
}
