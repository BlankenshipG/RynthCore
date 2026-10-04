// ============================================================================
//  RynthCore.Engine - ImGui/MonsterHud/MonsterHudSettingsUi.cs
//  The RynthVision overlay settings, drawn as two tabs of the Vision panel
//  (Panels/VisionFace.cs): "Nameplates" (monster plates, NPC / player names,
//  debuff icons, your plate, look) and "Combat text" (numbers, kill burst, gains). Square toggle
//  dots and amber section headers; sliders save when the drag ends. Values
//  live engine-side in MonsterHudSettings. AC's render thread, inside the
//  face's content child.
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;

namespace RynthCore.Engine.ImGuiBackend.Hud;

internal static class MonsterHudSettingsUi
{
    private static readonly uint Amber = C(0xFFE8B333), TextDim = C(0xFFF2F7FC), Mute = C(0xFFB8C8D8),
        ToggleOn = C(0xFF33FF33), ToggleOff = C(0xFF334455), BtnBord = C(0xFF34587A), BtnFill = C(0xFF16283A);
    private static uint C(uint argb) => RynthTheme.Argb(argb);

    private static bool Loading(float w)
    {
        if (MonsterHudSettings.Loaded) return false;
        MonsterHudSettings.EnsureLoadQueued();
        Note("Loading...", w);
        return true;
    }

    /// <summary>The "Nameplates" tab body at the cursor, <paramref name="w"/> wide.</summary>
    public static void DrawNameplates(float w)
    {
        if (Loading(w)) return;
        bool changed = false;

        Section("Monster plates", w);
        changed |= Toggle("Show monster nameplates", ref MonsterHudSettings.Enabled,
            "Health bars and names over monsters in the world. Chat: /rv plates on|off.");
        changed |= Toggle("Click a plate to select", ref MonsterHudSettings.ClickToSelect,
            "Left-click a monster's plate (name or health bar) to select that monster, as clicking it in the world does. " +
            "Off: plates never take a click. Chat: /rv plates click.", sub: true);
        Note("Plates sit under every panel; a click on a plate selects its monster, every other click goes to the game.", w);
        bool all = MonsterHudSettings.Filter == PlateFilter.All;
        if (Toggle("All monsters in range", ref all, "Every attackable monster within the distance, nearest first.", radio: true))
        {
            MonsterHudSettings.Filter = PlateFilter.All;
            changed = true;
        }
        bool engaged = MonsterHudSettings.Filter == PlateFilter.Engaged;
        if (Toggle("Only target + attackers", ref engaged,
                "Your selected target, monsters that hit you in the last 20 s, and monsters that are hurt.", radio: true))
        {
            MonsterHudSettings.Filter = PlateFilter.Engaged;
            changed = true;
        }
        changed |= SliderF("Max distance (yd)", ref MonsterHudSettings.MaxDistance,
            MonsterHudSettings.MinDistance, MonsterHudSettings.MaxDistanceLimit, "%.0f", w);
        changed |= SliderI("Max plates", ref MonsterHudSettings.MaxPlates,
            MonsterHudSettings.MinPlates, MonsterHudSettings.MaxPlatesLimit, w);

        Section("On the plate", w);
        changed |= Toggle("Name", ref MonsterHudSettings.ShowNames, null);
        changed |= Toggle("HP numbers", ref MonsterHudSettings.ShowHpNumbers,
            "\"142 / 190\" when the monster's max HP is known (appraised), else a percentage. Hidden until its health has been seen.");
        changed |= Toggle("Level badge", ref MonsterHudSettings.ShowLevel,
            "Coloured against your level: grey, green, white, orange, red. Needs an appraisal.");
        changed |= Toggle("Weak-to element", ref MonsterHudSettings.ShowWeakness,
            "What the monster takes most damage from, from RynthAi's Damage data (needs RynthAi).");
        changed |= Toggle("Distance", ref MonsterHudSettings.ShowDistance, null);

        Section("Names over NPCs and players", w);
        changed |= Toggle("NPC names", ref MonsterHudSettings.NpcNames,
            "The name over vendors, quest givers, town criers and other NPCs: yellow, vendors gold (the radar's colours). " +
            "Name only, no health bar; click one to select it. Chat: /rv plates npcs.");
        changed |= SliderF("NPC name distance (yd)", ref MonsterHudSettings.NpcMaxDistance,
            MonsterHudSettings.MinNpcDistance, MonsterHudSettings.MaxNpcDistanceLimit, "%.0f", w);
        changed |= SliderI("Max names", ref MonsterHudSettings.MaxNpcLabels,
            MonsterHudSettings.MinNpcLabels, MonsterHudSettings.MaxNpcLabelsLimit, w);
        changed |= Toggle("Player names", ref MonsterHudSettings.PlayerNames,
            "The name over other players too (blue), within the same distance and count. Chat: /rv plates players.");
        Note("Names count apart from the monster plates, so they never push one out; the nearest win.", w);

        Section("Debuffs", w);
        changed |= Toggle("Debuff icons", ref MonsterHudSettings.ShowDebuffs,
            "Your own debuffs on each monster (vulnerabilities, Imperil, Magic Yield, attribute and skill debuffs...), " +
            "one icon per kind with a line that runs out with the spell. From your \"You cast ... on ...\" lines. Chat: /rv plates debuffs.");
        changed |= Toggle("Debuffed by others", ref MonsterHudSettings.ShowOthersDebuff,
            "A small marker when an appraisal shows a lowered attribute or vital that none of your own debuffs explains. " +
            "It only updates when the monster is appraised (your combat target is appraised often).", sub: true);

        Section("Your plate", w);
        changed |= Toggle("Show your plate", ref MonsterHudSettings.SelfPlate,
            "Health, stamina and mana bars over your own head. Chat: /rv plates self.");
        changed |= Toggle("Numbers on your bars", ref MonsterHudSettings.SelfNumbers, "\"cur / max\" inside each bar (taller bars).", sub: true);
        changed |= Toggle("Your name", ref MonsterHudSettings.SelfName, null, sub: true);
        changed |= Toggle("Hide in first person", ref MonsterHudSettings.SelfHideFirstPerson,
            "When the camera is at your head the plate would sit mid-screen; hide it then.", sub: true);
        changed |= SelfPositionPick("Over your head", SelfPlatePosition.Above);
        changed |= SelfPositionPick("Under your feet", SelfPlatePosition.Below);
        changed |= SelfPositionPick("Beside you, left", SelfPlatePosition.Left);
        changed |= SelfPositionPick("Beside you, right", SelfPlatePosition.Right);

        Section("Look", w);
        changed |= SliderF("Scale", ref MonsterHudSettings.Scale, MonsterHudSettings.MinScale, MonsterHudSettings.MaxScale, "%.2f", w);
        changed |= Toggle("Keep on screen", ref MonsterHudSettings.KeepOnScreen,
            "When the camera closes in (walls), plates and numbers stay pinned to the screen edge instead of leaving it. Chat: /rv plates onscreen.");
        changed |= SliderF("Opacity", ref MonsterHudSettings.Opacity, MonsterHudSettings.MinOpacity, MonsterHudSettings.MaxOpacity, "%.2f", w);
        changed |= SliderF("Height lift (m)", ref MonsterHudSettings.HeightLift, MonsterHudSettings.MinLift, MonsterHudSettings.MaxLift, "%.1f", w);
        changed |= Toggle("Fade and shrink with distance", ref MonsterHudSettings.FadeWithDistance, null);

        ImGuiNET.ImGui.Dummy(new Vector2(0, 8));
        if (SmallButton("Reset all to defaults"))
        {
            MonsterHudSettings.ResetToDefaults();
            changed = false;   // ResetToDefaults saved already
        }

        if (changed)
            MonsterHudSettings.Save();
    }

    /// <summary>The "Combat text" tab body at the cursor, <paramref name="w"/> wide.</summary>
    public static void DrawCombatText(float w)
    {
        if (Loading(w)) return;
        bool changed = false;

        Section("Combat numbers", w);
        changed |= Toggle("Show combat numbers", ref MonsterHudSettings.Numbers,
            "Floating numbers over monsters and over you. Chat: /rv plates numbers.");
        changed |= Toggle("Damage you deal", ref MonsterHudSettings.NumDealt,
            "White to red by size; gold and bigger with \"!\" on a crit. A killing blow is estimated from the last health seen.", sub: true);
        changed |= Toggle("Damage you take", ref MonsterHudSettings.NumTaken, "Red numbers over your head.", sub: true);
        changed |= Toggle("Heals", ref MonsterHudSettings.NumHeals,
            "Green \"+N\" when you or a monster gains more health than a regen tick.", sub: true);
        changed |= Toggle("Kill burst", ref MonsterHudSettings.NumKills, "Expanding rings where a monster you damaged dies.", sub: true);
        changed |= Toggle("Stamina and mana restores", ref MonsterHudSettings.NumRestores,
            "\"+N stam\" (amber) and \"+N mana\" (blue) over you when a spell, potion or kit restores more than a regen tick.", sub: true);
        changed |= SliderF("Number size", ref MonsterHudSettings.NumSize, MonsterHudSettings.MinNumSize, MonsterHudSettings.MaxNumSize, "%.2f", w);
        changed |= SliderF("Number duration", ref MonsterHudSettings.NumTime, MonsterHudSettings.MinNumTime, MonsterHudSettings.MaxNumTime, "%.2fx", w);

        Section("Gains", w);
        changed |= Toggle("Show gains", ref MonsterHudSettings.Gains,
            "Rising text over your head when you earn. Chat: /rv plates gains.");
        changed |= Toggle("Experience", ref MonsterHudSettings.GainXp, "\"+12,345 XP\"", sub: true);
        changed |= Toggle("Luminance", ref MonsterHudSettings.GainLum, "\"+500 Lum\"", sub: true);
        changed |= Toggle("Radiance", ref MonsterHudSettings.GainRadiance,
            "\"+10 Radiance\", from Aelrynth's \"You gain N Radiance.\" lines.", sub: true);
        changed |= SliderF("Gain text size", ref MonsterHudSettings.GainSize, MonsterHudSettings.MinNumSize, MonsterHudSettings.MaxNumSize, "%.2f", w);
        changed |= SliderF("Gain text duration", ref MonsterHudSettings.GainTime, MonsterHudSettings.MinNumTime, MonsterHudSettings.MaxNumTime, "%.2fx", w);
        Note("Spending experience or luminance never shows.", w);

        if (changed)
            MonsterHudSettings.Save();
    }

    // ── Rows ─────────────────────────────────────────────────────────────

    /// <summary>A square dot + label; the row toggles (a radio row only ever turns on; a sub row is indented).</summary>
    private static bool SelfPositionPick(string label, SelfPlatePosition pos)
    {
        bool on = MonsterHudSettings.SelfPosition == pos;
        if (!Toggle(label, ref on, "Where your plate sits. Chat: /rv plates self position above|below|left|right.", radio: true, sub: true))
            return false;
        MonsterHudSettings.SelfPosition = pos;
        return true;
    }

    private static bool Toggle(string label, ref bool value, string? tip, bool radio = false, bool sub = false)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        if (sub) ImGuiNET.ImGui.SetCursorScreenPos(ImGuiNET.ImGui.GetCursorScreenPos() + new Vector2(16, 0));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        float labelW = ImGuiNET.ImGui.CalcTextSize(label).X;
        ImGuiNET.ImGui.PushID(label);
        bool clicked = ImGuiNET.ImGui.InvisibleButton("##t", new Vector2(18 + labelW, 16));
        ImGuiNET.ImGui.PopID();
        if (ImGuiNET.ImGui.IsItemHovered())
        {
            ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (tip != null) ImGuiNET.ImGui.SetTooltip(tip);
        }
        if (clicked) value = radio || !value;
        dl.AddRectFilled(p + new Vector2(0, 2), p + new Vector2(12, 14), value ? ToggleOn : ToggleOff, 2);
        dl.AddText(p + new Vector2(18, (16 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), TextDim, label);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        return clicked;
    }

    private static bool SliderF(string label, ref float value, float min, float max, string fmt, float w)
    {
        BeginSliderRow(label, w);
        bool changed = ImGuiNET.ImGui.SliderFloat("##v", ref value, min, max, fmt, ImGuiSliderFlags.AlwaysClamp);
        return EndSliderRow(changed);
    }

    private static bool SliderI(string label, ref int value, int min, int max, float w)
    {
        BeginSliderRow(label, w);
        bool changed = ImGuiNET.ImGui.SliderInt("##v", ref value, min, max, "%d", ImGuiSliderFlags.AlwaysClamp);
        return EndSliderRow(changed);
    }

    private static void BeginSliderRow(string label, float w)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        ImGuiNET.ImGui.GetWindowDrawList().AddText(p + new Vector2(0, 2), TextDim, label);
        float labelCol = Math.Min(130, w * 0.45f);
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X + labelCol, p.Y));
        ImGuiNET.ImGui.SetNextItemWidth(Math.Max(60, w - labelCol));
        ImGuiNET.ImGui.PushID(label);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBg, BtnFill);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
    }

    /// <summary>True once the edit is finished (drag released or value typed), so saves aren't per-step.</summary>
    private static bool EndSliderRow(bool changed)
    {
        bool done = changed && !ImGuiNET.ImGui.IsItemActive() || ImGuiNET.ImGui.IsItemDeactivatedAfterEdit();
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor(2);
        ImGuiNET.ImGui.PopID();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        return done;
    }

    private static void Section(string text, float w)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 6));
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        ImGuiNET.ImGui.GetWindowDrawList().AddText(p, Amber, text);
        ImGuiNET.ImGui.Dummy(new Vector2(w, ImGuiNET.ImGui.GetFontSize() + 2));
        ImGuiNET.ImGui.PopFont();
    }

    private static void Note(string text, float w)
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Mute);
        ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
        ImGuiNET.ImGui.TextUnformatted(text);
        ImGuiNET.ImGui.PopTextWrapPos();
        ImGuiNET.ImGui.PopStyleColor();
    }

    private static bool SmallButton(string label)
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Button, BtnFill);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 3));
        bool clicked = ImGuiNET.ImGui.Button(label);
        ImGuiNET.ImGui.PopStyleVar(2);
        ImGuiNET.ImGui.PopStyleColor(2);
        return clicked;
    }
}
