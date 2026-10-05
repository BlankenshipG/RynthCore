// ============================================================================
//  RynthCore.Engine - ImGui/Panels/RynthAiFace.Drawers.cs
//  The RynthAi dashboard's left-edge drawers that draw with the dashboard's
//  own helpers (DashboardDrawers.cs has the tabs and the slide; the Ranges
//  drawer is RangesSlideOut.cs):
//    Loaded files  meta state and bot activity, then the Profile / Nav / Loot
//                  / Meta pickers and the Loot Editor button. Until
//                  2026-10-05 this block folded out inside the dashboard
//                  (the caret on the control row); a popped-out dashboard
//                  still shows it inline that way (no drawers popped out).
//    Patrol        this dungeon's hazard cells (mark / unmark / clear), the
//                  recorded hazards and the saved routes. Was the Patrol
//                  launcher's right-click popup; right-click now opens this
//                  drawer (popped out: still the popup).
//  AC thread only (inside the dashboard's Draw).
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed partial class RynthAiFace
{
    /// <summary>Meta state, bot activity and the four file pickers.</summary>
    private sealed class FilesDrawer : DashboardDrawer
    {
        private readonly RynthAiFace _face;
        public FilesDrawer(RynthAiFace face) => _face = face;

        public override string Key => "files";
        public override string Icon => PhosphorIcons.Files;
        public override string Name => "loaded files";
        public override bool SavedOpen => RynthAiDashboardState.FilesOpen;
        public override void SaveOpen(bool open) => RynthAiDashboardState.SetFilesOpen(open);

        public override string TabTooltip(bool right)
        {
            RynthAiView? v = _face._view;
            return "Loaded files: the Profile, Nav, Loot and Meta pickers, meta state and bot activity.\n" +
                   "Profile: " + (v?.ProfileText ?? "Default") + "\nNav: " + (v?.NavText ?? "None") +
                   "\nLoot: " + (v?.LootText ?? "None") + "\nMeta: " + (v?.MetaText ?? "None") +
                   (right ? "\n\nClick to slide them out (to the right: there's no room on the left)." : "\n\nClick to slide them out.");
        }

        private const float LineH = 15, SelRow = 16, SelGap = 3;

        public override float Width(float k) => 230 * k;
        public override float Height(float k) =>
            (Pad + TitleH + 4 + 2 * LineH + 6) * k + 4 * SelRow + 3 * SelGap + Pad * k;

        public override void DrawPanel(Vector2 origin, float panelW, float panelH, float k)
        {
            float y = PanelFrame(origin, panelW, panelH, k, PhosphorIcons.Files, "LOADED FILES", null, "Hide the loaded files");
            float x = origin.X + Pad * k, w = panelW - 2 * Pad * k;
            RynthAiView? view = _face._view;
            RynthAiSnapshot raw = _face._raw;
            var dl = ImGuiNET.ImGui.GetWindowDrawList();
            ImFontPtr f10 = ImGuiFonts.Get(UiFont.Dash10);
            float lh = LineH * k;

            // Meta state / bot activity: label, then the value (amber) on the same line.
            float labelW = MathF.Max(CalcWidth(f10, "Meta state:"), CalcWidth(f10, "Bot activity:")) + 6 * k;
            InfoLine(dl, f10, x, y, w, lh, labelW, "Meta state:", view?.MetaStateText ?? "Default");
            y += lh;
            InfoLine(dl, f10, x, y, w, lh, labelW, "Bot activity:", view?.BotActivityText ?? "Idle");
            y += lh + 6 * k;

            _face.Selector(0, "Profile:", view?.ProfileText ?? "Default", raw.Profiles, raw.SelectedProfileIdx, 3, x, y, w, null);
            y += SelRow + SelGap;
            _face.Selector(1, "Nav:", view?.NavText ?? "None", raw.NavProfiles, raw.SelectedNavIdx, 0, x, y, w, null);
            y += SelRow + SelGap;
            _face.Selector(2, "Loot:", view?.LootText ?? "None", raw.LootProfiles, raw.SelectedLootIdx, 1, x, y, w, raw);
            y += SelRow + SelGap;
            _face.Selector(3, "Meta:", view?.MetaText ?? "None", raw.MetaProfiles, raw.SelectedMetaIdx, 2, x, y, w, null);
        }

        private static void InfoLine(ImDrawListPtr dl, ImFontPtr f, float x, float y, float w, float lh, float labelW, string label, string value)
        {
            float ty = y + (lh - f.FontSize) * 0.5f;
            dl.AddText(f, f.FontSize, new Vector2(x, ty), Mute, label);
            dl.PushClipRect(new Vector2(x + labelW, y), new Vector2(x + w, y + lh), true);
            dl.AddText(f, f.FontSize, new Vector2(x + labelW, ty), Amber, value);
            dl.PopClipRect();
        }
    }

    /// <summary>Hazard cells and saved routes (the Patrol launcher's right-click).</summary>
    private sealed class PatrolDrawer : DashboardDrawer
    {
        private bool _sub;

        public override string Key => "patrol";
        public override string Icon => PhosphorIcons.Footprints;
        public override string Name => "patrol drawer";
        public override bool SavedOpen => RynthAiDashboardState.PatrolOpen;
        public override void SaveOpen(bool open) => RynthAiDashboardState.SetPatrolOpen(open);

        public override string TabTooltip(bool right) => right
            ? "Patrol: this dungeon's hazard cells, recorded hazards and saved routes (also: right-click Patrol).\nClick to slide it out (to the right: there's no room on the left)."
            : "Patrol: this dungeon's hazard cells, recorded hazards and saved routes (also: right-click Patrol).\nClick to slide it out.";

        public override float Width(float k) => 260 * k;
        public override float Height(float k) => 320 * k;

        public override void Update(bool showing)
        {
            if (showing == _sub) return;
            _sub = showing;
            if (showing) { UiSources.Patrol.Subscribe(); UiSources.Patrol.RequestRefresh(); }
            else UiSources.Patrol.Unsubscribe();
        }

        public override void DrawPanel(Vector2 origin, float panelW, float panelH, float k)
        {
            float y = PanelFrame(origin, panelW, panelH, k, PhosphorIcons.Footprints, "PATROL & ROUTES", null, "Hide the patrol drawer");
            float x = origin.X + Pad * k;
            // A scrolling child for the lists. A child window is clipped to its parent's clip rect,
            // so the host's slide clip covers it too.
            ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x, y));
            Vector2 size = new(panelW - 2 * Pad * k, MathF.Max(1, origin.Y + panelH - Pad * k - y));
            ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            bool open = ImGuiNET.ImGui.BeginChild("##patrol_body", size, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
            ImGuiNET.ImGui.PopStyleVar();
            try
            {
                if (open)
                {
                    PatrolInfo info = UiSources.Patrol.Current?.Value ?? new PatrolInfo();
                    PatrolBody(info, withTitle: false);
                }
            }
            finally { ImGuiNET.ImGui.EndChild(); }
        }
    }
}
