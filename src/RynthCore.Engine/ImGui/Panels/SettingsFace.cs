// ============================================================================
//  RynthCore.Engine - ImGui/Panels/SettingsFace.cs
//  ImGui face of RynthAi's Advanced Settings (docs/IMGUI_PARITY_PLAN.md §2.4).
//
//  Draws SettingsSchema (UI/Data/SettingsData.cs), the same list the Avalonia
//  face builds from, with that face's look: 150 px tab sidebar, "Advanced
//  Settings > Tab" header, square toggle dots, - [value] + steppers (typed
//  values commit on Enter or when the field loses focus, then clamp; bad
//  input reverts), combo pickers, amber section headers, tooltips.
//
//  Edits a private copy of the hub's settings and saves the whole copy
//  (SettingsCommands.Save) on each change; a newer fetch replaces the copy
//  only while no field here is being typed in. Rows that show or hide other
//  rows need nothing special: visibility is re-evaluated every frame.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using ImGuiNET;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class SettingsFace : IImGuiPanel
{
    public const string Title = "Settings";

    public static void Register() => ImGuiPanelHost.Register(Title,
        new PanelSpec(new Vector2(400, 500), new Vector2(320, 240), EdgeToEdge: true),
        () => new SettingsFace());

    // ── Palette (SettingsPanel's, ARGB) ─────────────────────────────────
    private static readonly uint Teal = C(0xFF26D9E6), Amber = C(0xFFE8B333), Mute = C(0xFFB8C8D8),
        TextDim = C(0xFFF2F7FC), ShellBg = C(0xFF0A0F14), PanelBg = C(0xFF141F29), BtnFill = C(0xFF16283A),
        BtnBord = C(0xFF34587A), ToggleOn = C(0xFF33FF33), ToggleOff = C(0xFF334455), TabActive = C(0xFF1A2E42);
    private static uint C(uint argb) => RynthTheme.Argb(argb);

    private static readonly string[] Headers = BuildHeaders();

    private RynthAiSettings _data = new();
    private long _seenVersion = -1;
    private int _tab;
    private bool _editing;

    // Typed-value buffers, one per numeric row (UTF-8, NUL-terminated).
    private sealed class NumberEdit
    {
        public readonly byte[] Buffer = new byte[24];
        public double Shown = double.NaN;
        public bool Active;     // the box had keyboard focus last frame
    }
    private readonly Dictionary<SettingRow, NumberEdit> _edits = new();

    public void OnShown()
    {
        UiSources.Settings.Subscribe();
        UiSources.Settings.RequestRefresh();
    }

    public void OnHidden() => UiSources.Settings.Unsubscribe();

    public void Draw()
    {
        // Take a newer fetch unless a value is being typed in.
        var snap = UiSources.Settings.Current;
        if (snap != null && snap.Version != _seenVersion && !_editing)
        {
            _seenVersion = snap.Version;
            _data = snap.Value.Clone();
        }
        _editing = false;

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 origin = ImGuiNET.ImGui.GetCursorScreenPos();
        Vector2 size = ImGuiNET.ImGui.GetContentRegionAvail();
        dl.AddRectFilled(origin, origin + size, ShellBg, 4);

        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui11));
        Sidebar(size.Y);
        ImGuiNET.ImGui.SetCursorScreenPos(origin + new Vector2(150, 0));
        Content(new Vector2(size.X - 150, size.Y));
        ImGuiNET.ImGui.PopFont();

        dl.AddRect(origin, origin + size, BtnBord, 4);
    }

    // ── Sidebar: one button per tab ────────────────────────────────────────

    private void Sidebar(float height)
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
        ImGuiNET.ImGui.BeginChild("##settings_tabs", new Vector2(150, height), ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float w = ImGuiNET.ImGui.GetContentRegionAvail().X;
        SettingsTab[] tabs = SettingsSchema.Tabs;
        for (int i = 0; i < tabs.Length; i++)
        {
            string tabName = tabs[i].Name;
            ImGuiNET.ImGui.Dummy(new Vector2(0, 1));
            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            ImGuiNET.ImGui.PushID(i);
            if (ImGuiNET.ImGui.InvisibleButton("##tab", new Vector2(w, 26))) _tab = i;
            bool hot = ImGuiNET.ImGui.IsItemHovered();
            ImGuiNET.ImGui.PopID();
            bool sel = i == _tab;
            dl.AddRectFilled(p, p + new Vector2(w, 26), sel ? TabActive : hot ? Lighten(BtnFill) : BtnFill);
            dl.AddRect(p, p + new Vector2(w, 26), BtnBord);
            float ty = p.Y + (26 - ImGuiNET.ImGui.GetFontSize()) * 0.5f;
            dl.AddText(new Vector2(p.X + 8, ty), sel ? Teal : Mute, TabLabel(tabName));
        }
        ImGuiNET.ImGui.EndChild();
    }

    // ── Content: header + the tab's rows ───────────────────────────────────

    private void Content(Vector2 size)
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, 0);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 0));
        ImGuiNET.ImGui.BeginChild("##settings_content", size, ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float w = ImGuiNET.ImGui.GetContentRegionAvail().X;

        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        if (_tab >= SettingsSchema.Tabs.Length) _tab = 0;
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFF26D9E6), Headers[_tab]);
        ImGuiNET.ImGui.PopFont();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));
        Vector2 lp = ImGuiNET.ImGui.GetCursorScreenPos();
        dl.AddLine(lp, lp + new Vector2(w, 0), BtnBord);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 6));

        SettingRow[] rows = SettingsSchema.Tabs[_tab].Rows;
        for (int i = 0; i < rows.Length; i++)
        {
            SettingRow row = rows[i];
            if (!row.IsVisible(_data)) continue;
            ImGuiNET.ImGui.PushID(i);
            DrawRow(row, w);
            ImGuiNET.ImGui.PopID();
        }

        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.EndChild();
    }

    private void DrawRow(SettingRow row, float w)
    {
        switch (row.Kind)
        {
            case SettingKind.Bool: BoolRow(row, w); break;
            case SettingKind.Int:
            case SettingKind.Float:
            case SettingKind.Double: NumberRow(row, w); break;
            case SettingKind.Combo: ComboRow(row, w); break;
            case SettingKind.Section: Section(row.Label, w); break;
            case SettingKind.Spacer: ImGuiNET.ImGui.Dummy(new Vector2(0, 6)); break;
            case SettingKind.Note: Note(row.Label, w); break;
            case SettingKind.CraftingStatus: CraftingStatus(row); break;
        }
    }

    private void Changed(SettingRow row, double value)
    {
        row.Set!(_data, value);
        SettingsCommands.Save(_data.Clone());
    }

    // Square dot + label; the whole row toggles.
    private void BoolRow(SettingRow row, float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        bool on = row.Get!(_data) != 0;
        float labelW = ImGuiNET.ImGui.CalcTextSize(row.Label).X;
        if (ImGuiNET.ImGui.InvisibleButton("##b", new Vector2(Math.Min(w, 18 + labelW), 16)))
            Changed(row, on ? 0 : 1);
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        Tooltip(row);
        dl.AddRectFilled(p + new Vector2(0, 2), p + new Vector2(12, 14), on ? ToggleOn : ToggleOff, 2);
        dl.AddText(p + new Vector2(18, (16 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), TextDim, row.Label);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
    }

    // Label | [-] [value] [+]; the value commits on Enter or focus loss.
    private void NumberRow(SettingRow row, float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        bool isInt = row.Kind == SettingKind.Int;
        float boxW = isInt ? 52 : 64, boxCol = isInt ? 56 : 68;
        double value = Math.Clamp(row.Get!(_data), row.Min, row.Max);

        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        float right = p.X + w;
        dl.AddText(p + new Vector2(0, (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), TextDim, row.Label);
        ImGuiNET.ImGui.InvisibleButton("##label", new Vector2(Math.Max(1, w - 40 - boxCol), 22));
        Tooltip(row);

        float plusX = right - 20, boxX = plusX - boxCol + (boxCol - boxW) * 0.5f, minusX = right - 20 - boxCol - 20;
        if (StepButton("##minus", new Vector2(minusX + 1, p.Y + 1), PhosphorIcons.Minus))
            Changed(row, Math.Clamp(value - row.Step, row.Min, row.Max));

        // Typed value (amber on the panel colour).
        NumberEdit edit = EditFor(row);
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(boxX, p.Y + 1));
        ImGuiNET.ImGui.SetNextItemWidth(boxW);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBg, PanelBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Amber);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, (20 - ImGuiNET.ImGui.GetFontSize()) * 0.5f));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        if (!edit.Active && edit.Shown != value)
            WriteValue(edit, value, isInt);
        ImGuiNET.ImGui.InputText("##v", edit.Buffer, (uint)edit.Buffer.Length,
            ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll);
        bool active = ImGuiNET.ImGui.IsItemActive();
        edit.Active = active;
        _editing |= active;
        if (ImGuiNET.ImGui.IsItemDeactivatedAfterEdit())
        {
            if (TryParse(edit.Buffer, out double typed))
            {
                double clamped = Math.Clamp(isInt ? Math.Round(typed) : typed, row.Min, row.Max);
                Changed(row, clamped);
                WriteValue(edit, clamped, isInt);
            }
            else WriteValue(edit, value, isInt); // bad input reverts
        }
        ImGuiNET.ImGui.PopStyleVar(3);
        ImGuiNET.ImGui.PopStyleColor(3);

        if (StepButton("##plus", new Vector2(plusX + 1, p.Y + 1), PhosphorIcons.Plus))
            Changed(row, Math.Clamp(value + row.Step, row.Min, row.Max));

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 22));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
    }

    private NumberEdit EditFor(SettingRow row)
    {
        if (!_edits.TryGetValue(row, out NumberEdit? e))
            _edits[row] = e = new NumberEdit();
        return e;
    }

    private static void WriteValue(NumberEdit edit, double value, bool isInt)
    {
        string text = isInt ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
                            : value.ToString("G4", CultureInfo.InvariantCulture);
        int n = Encoding.UTF8.GetBytes(text, 0, text.Length, edit.Buffer, 0);
        edit.Buffer[Math.Min(n, edit.Buffer.Length - 1)] = 0;
        edit.Shown = value;
    }

    private static bool TryParse(byte[] buffer, out double value)
    {
        int len = Array.IndexOf(buffer, (byte)0);
        if (len < 0) len = buffer.Length;
        return double.TryParse(Encoding.UTF8.GetString(buffer, 0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // Label | [current item]; click opens a 220 px list with the current item highlighted.
    private void ComboRow(SettingRow row, float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        string[] items = row.Items!;
        int current = Math.Clamp((int)row.Get!(_data), 0, items.Length - 1);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        dl.AddText(p + new Vector2(0, (22 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), TextDim, row.Label);

        ImFontPtr f10 = ImGuiFonts.Get(UiFont.Ui10);
        const float btnW = 160;
        var bp = new Vector2(p.X + w - btnW, p.Y + 1);
        ImGuiNET.ImGui.SetCursorScreenPos(bp);
        if (ImGuiNET.ImGui.InvisibleButton("##combo", new Vector2(btnW, 20)))
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(Math.Max(bp.X + btnW - 220, 0), bp.Y + 20));
            ImGuiNET.ImGui.OpenPopup("##combo_list");
        }
        bool hot = ImGuiNET.ImGui.IsItemHovered();
        Tooltip(row);
        dl.AddRectFilled(bp, bp + new Vector2(btnW, 20), hot ? Lighten(BtnFill) : BtnFill);
        dl.AddRect(bp, bp + new Vector2(btnW, 20), BtnBord);
        dl.PushClipRect(bp, bp + new Vector2(btnW - 16, 20), true);
        dl.AddText(f10, f10.FontSize, bp + new Vector2(4, (20 - f10.FontSize) * 0.5f), TextDim, items[current]);
        dl.PopClipRect();
        PhosphorIcons.DrawCentered(dl, f10, PhosphorIcons.CaretDown, bp + new Vector2(btnW - 16, 0), new Vector2(14, 20), Mute);

        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
        ImGuiNET.ImGui.SetNextWindowSize(new Vector2(220, items.Length * 21 + 4));
        bool open = ImGuiNET.ImGui.BeginPopup("##combo_list");
        ImGuiNET.ImGui.PopStyleVar(2);
        ImGuiNET.ImGui.PopStyleColor(2);
        if (open)
        {
            var pdl = ImGuiNET.ImGui.GetWindowDrawList();
            float iw = ImGuiNET.ImGui.GetContentRegionAvail().X;
            for (int i = 0; i < items.Length; i++)
            {
                ImGuiNET.ImGui.PushID(i);
                Vector2 ip = ImGuiNET.ImGui.GetCursorScreenPos();
                bool picked = ImGuiNET.ImGui.InvisibleButton("##item", new Vector2(iw, 20));
                bool ih = ImGuiNET.ImGui.IsItemHovered();
                ImGuiNET.ImGui.PopID();
                bool sel = i == current;
                pdl.AddRectFilled(ip, ip + new Vector2(iw, 20), sel ? TabActive : ih ? Lighten(BtnFill) : BtnFill);
                pdl.AddRect(ip, ip + new Vector2(iw, 20), BtnBord);
                pdl.AddText(f10, f10.FontSize, ip + new Vector2(6, (20 - f10.FontSize) * 0.5f), sel ? Teal : TextDim, items[i]);
                ImGuiNET.ImGui.Dummy(new Vector2(0, 1));
                if (picked)
                {
                    Changed(row, i);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
            }
            ImGuiNET.ImGui.EndPopup();
        }

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + 22));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
    }

    private static void Section(string text, float w)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));
        Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
        ImFontPtr bold = ImGuiFonts.Get(UiFont.UiBold11);
        float h = bold.FontSize + 4;
        dl.AddRectFilled(p, p + new Vector2(w, h), TabActive);
        dl.AddText(bold, bold.FontSize, p + new Vector2(4, 2), Amber, text);
        ImGuiNET.ImGui.Dummy(new Vector2(w, h));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
    }

    private static void Note(string text, float w)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        bool small = !text.StartsWith('(');
        if (small) ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui10));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Mute);
        ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
        ImGuiNET.ImGui.TextUnformatted(text);
        ImGuiNET.ImGui.PopTextWrapPos();
        ImGuiNET.ImGui.PopStyleColor();
        if (small) ImGuiNET.ImGui.PopFont();
    }

    private void CraftingStatus(SettingRow row)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), row.Label);
        ImGuiNET.ImGui.SameLine(0, 6);
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(_data.MissileCraftingActive ? 0xFFE8B333 : 0xFFB8C8D8), _data.MissileCraftingState);
        ImGuiNET.ImGui.PopFont();
        if (!string.IsNullOrEmpty(_data.MissileCraftingStatus))
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui10));
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, TextDim);
            ImGuiNET.ImGui.TextWrapped(_data.MissileCraftingStatus);
            ImGuiNET.ImGui.PopStyleColor();
            ImGuiNET.ImGui.PopFont();
        }
    }

    private static bool StepButton(string id, Vector2 pos, string glyph)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(id, new Vector2(18, 20));
        uint bg = ImGuiNET.ImGui.IsItemActive() ? Darken(BtnFill) : ImGuiNET.ImGui.IsItemHovered() ? Lighten(BtnFill) : BtnFill;
        dl.AddRectFilled(pos, pos + new Vector2(18, 20), bg);
        dl.AddRect(pos, pos + new Vector2(18, 20), BtnBord);
        PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui11), glyph, pos, new Vector2(18, 20), TextDim);
        return clicked;
    }

    // Tab icons by tab name (SettingsSchema); a tab not listed shows its name alone.
    private static readonly System.Collections.Generic.Dictionary<string, string> TabIcons = new()
    {
        ["Display"] = PhosphorIcons.Monitor,
        ["UI"] = PhosphorIcons.AppWindow,
        ["Misc"] = PhosphorIcons.DotsThree,
        ["Recharge"] = PhosphorIcons.BatteryCharging,
        ["Melee Combat"] = PhosphorIcons.Sword,
        ["Spell Combat"] = PhosphorIcons.MagicWand,
        ["Ranges"] = PhosphorIcons.Ruler,
        ["Navigation"] = PhosphorIcons.SneakerMove,
        ["Buffing"] = PhosphorIcons.Sparkle,
        ["Crafting"] = PhosphorIcons.Hammer,
        ["Looting"] = PhosphorIcons.Bag,
        ["Vendoring"] = PhosphorIcons.Storefront,
    };
    private static readonly System.Collections.Generic.Dictionary<string, string> TabLabels = new();

    /// <summary>"icon  Name", built once per tab.</summary>
    private static string TabLabel(string name)
    {
        if (TabLabels.TryGetValue(name, out string? label)) return label;
        label = TabIcons.TryGetValue(name, out string? icon) ? icon + "  " + name : name;
        TabLabels[name] = label;
        return label;
    }

    private static void Tooltip(SettingRow row)
    {
        if (row.Tooltip != null) ImGuiNET.ImGui.SetItemTooltip(row.Tooltip);
    }

    private static uint Lighten(uint abgr) => Scale(abgr, 1.25f, 12);
    private static uint Darken(uint abgr) => Scale(abgr, 0.8f, 0);

    private static uint Scale(uint abgr, float k, int add)
    {
        uint a = abgr & 0xFF000000;
        uint r = (uint)Math.Min(255, (int)((abgr & 0xFF) * k) + add);
        uint g = (uint)Math.Min(255, (int)(((abgr >> 8) & 0xFF) * k) + add);
        uint b = (uint)Math.Min(255, (int)(((abgr >> 16) & 0xFF) * k) + add);
        return a | (b << 16) | (g << 8) | r;
    }

    private static string[] BuildHeaders()
    {
        var headers = new string[SettingsSchema.Tabs.Length];
        for (int i = 0; i < headers.Length; i++)
            headers[i] = "Advanced Settings > " + SettingsSchema.Tabs[i].Name;
        return headers;
    }
}
