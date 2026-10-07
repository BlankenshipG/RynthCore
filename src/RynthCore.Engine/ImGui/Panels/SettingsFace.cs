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
    // Text rows being typed in (the saved value shows again once the box loses focus).
    private readonly Dictionary<SettingRow, string> _textEdits = new();

    /// <summary>Extra sidebar tab after the schema tabs; drawn from UiSources.Charms, not settings rows.</summary>
    private const string CharmsTabName = "Charms Tracking";
    private static int CharmsTab => SettingsSchema.Tabs.Length;
    private const string CharmsHeader = "Advanced Settings > " + CharmsTabName;

    public void OnShown()
    {
        UiSources.Settings.Subscribe();
        UiSources.Settings.RequestRefresh();
        UiSources.Charms.Subscribe();
        UiSources.Charms.RequestRefresh();
    }

    public void OnHidden()
    {
        UiSources.Settings.Unsubscribe();
        UiSources.Charms.Unsubscribe();
    }

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
        for (int i = 0; i <= tabs.Length; i++)
        {
            string tabName = i < tabs.Length ? tabs[i].Name : CharmsTabName;
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
        if (_tab > CharmsTab) _tab = 0;
        bool charms = _tab == CharmsTab;
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFF26D9E6), charms ? CharmsHeader : Headers[_tab]);
        ImGuiNET.ImGui.PopFont();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));
        Vector2 lp = ImGuiNET.ImGui.GetCursorScreenPos();
        dl.AddLine(lp, lp + new Vector2(w, 0), BtnBord);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 6));

        if (charms)
        {
            CharmsTabContent(w);
            ImGuiNET.ImGui.PopStyleVar();
            ImGuiNET.ImGui.EndChild();
            return;
        }

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
            case SettingKind.Button: ButtonRow(row, w); break;
            case SettingKind.TraceCategories: TraceCategoriesRow(row, w); break;
            case SettingKind.Text: TextRow(row, w); break;
            case SettingKind.Status: StatusRow(row, w); break;
        }
    }

    // [All on] [All off] [ILT Hub on], then a 3-column grid of square toggles, one per
    // RynthAi trace category. Edits the copy's DiagCategories and saves like any other row.
    private void TraceCategoriesRow(SettingRow row, float w)
    {
        var cats = TraceCategoryLevels.Parse(_data.DiagCategories);
        if (cats.Count == 0)
        {
            Note("Waiting for RynthAi...", w);
            return;
        }

        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        if (ImGuiNET.ImGui.Button("All on")) SaveCategories(TraceCategoryLevels.Set(_data.DiagCategories, "", true));
        ImGuiNET.ImGui.SameLine(0, 6);
        if (ImGuiNET.ImGui.Button("All off")) SaveCategories(TraceCategoryLevels.Set(_data.DiagCategories, "", false));
        ImGuiNET.ImGui.SameLine(0, 6);
        if (ImGuiNET.ImGui.Button("ILT Hub on")) SaveCategories(TraceCategoryLevels.Set(_data.DiagCategories, "", true, "Ilt"));
        Tooltip(row);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float colW = Math.Max(80f, w / 3f);
        int perRow = Math.Max(1, (int)(w / colW));
        Vector2 start = ImGuiNET.ImGui.GetCursorScreenPos();
        for (int i = 0; i < cats.Count; i++)
        {
            var (name, level) = cats[i];
            bool on = level != TraceCategoryLevels.Off;
            string label = TraceCategoryLevels.Label(name, level);
            var p = start + new Vector2(i % perRow * colW, i / perRow * 18f);
            ImGuiNET.ImGui.SetCursorScreenPos(p);
            ImGuiNET.ImGui.PushID(name);
            if (ImGuiNET.ImGui.InvisibleButton("##cat", new Vector2(colW - 4, 16)))
                SaveCategories(TraceCategoryLevels.Set(_data.DiagCategories, name, !on));
            if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGuiNET.ImGui.PopID();
            dl.AddRectFilled(p + new Vector2(0, 2), p + new Vector2(12, 14), on ? ToggleOn : ToggleOff, 2);
            dl.PushClipRect(p, p + new Vector2(colW - 4, 16), true);
            dl.AddText(p + new Vector2(18, (16 - ImGuiNET.ImGui.GetFontSize()) * 0.5f), TextDim, label);
            dl.PopClipRect();
        }
        int rowsUsed = (cats.Count + perRow - 1) / perRow;
        ImGuiNET.ImGui.SetCursorScreenPos(start + new Vector2(0, rowsUsed * 18f));
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));
    }

    private void SaveCategories(string levels)
    {
        _data.DiagCategories = levels;
        SettingsCommands.Save(_data.Clone());
    }

    // Label, then a full-width box; the value commits on Enter or focus loss (like the number boxes).
    private void TextRow(SettingRow row, float w)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, TextDim);
        ImGuiNET.ImGui.TextUnformatted(row.Label);
        ImGuiNET.ImGui.PopStyleColor();
        Tooltip(row);

        string saved = row.GetText?.Invoke(_data) ?? string.Empty;
        string text = _textEdits.TryGetValue(row, out string? typing) ? typing : saved;
        ImGuiNET.ImGui.SetNextItemWidth(Math.Max(80, w - 4));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBg, PanelBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Amber);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        bool enter = ImGuiNET.ImGui.InputTextWithHint("##text", row.Hint ?? string.Empty, ref text, 512u,
            ImGuiInputTextFlags.EnterReturnsTrue);
        bool active = ImGuiNET.ImGui.IsItemActive();
        bool committed = enter || ImGuiNET.ImGui.IsItemDeactivatedAfterEdit();
        ImGuiNET.ImGui.PopStyleVar(2);
        ImGuiNET.ImGui.PopStyleColor(3);
        Tooltip(row);
        _editing |= active;

        if (committed)
        {
            _textEdits.Remove(row);
            if (!string.Equals(text, saved, StringComparison.Ordinal))
            {
                row.SetText?.Invoke(_data, text);
                SettingsCommands.Save(_data.Clone());
            }
        }
        else if (active) _textEdits[row] = text;
        else _textEdits.Remove(row);
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
    }

    // RynthAi's one-line status (teal), wrapped to the column.
    private void StatusRow(SettingRow row, float w)
    {
        string text = row.GetText?.Invoke(_data) ?? string.Empty;
        if (text.Length == 0) return;
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Teal);
        ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetCursorPosX() + w);
        ImGuiNET.ImGui.TextUnformatted(text);
        ImGuiNET.ImGui.PopTextWrapPos();
        ImGuiNET.ImGui.PopStyleColor();
    }

    // Label | [caption]; Click posts its own work (never runs plugin code on AC's thread).
    private static void ButtonRow(SettingRow row, float w)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 2));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, TextDim);
        ImGuiNET.ImGui.AlignTextToFramePadding();
        ImGuiNET.ImGui.TextUnformatted(row.Label);
        ImGuiNET.ImGui.PopStyleColor();
        ImGuiNET.ImGui.SameLine(Math.Max(ImGuiNET.ImGui.GetCursorPosX() + 8, w * 0.45f));
        if (ImGuiNET.ImGui.Button(row.ButtonText ?? row.Label))
            row.Click?.Invoke();
        Tooltip(row);
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

    // ── Charms Tracking tab ───────────────────────────────────────────────

    private static readonly uint CharmOn = C(0xFF33FF33), CharmDisabled = C(0xFFFF5555);

    /// <summary>Summary line, one table row per ACECustom registry charm, then how each column is known.</summary>
    private static void CharmsTabContent(float w)
    {
        var snap = UiSources.Charms.Current;
        if (snap == null)
        {
            Note("Waiting for RynthAi...", w);
            return;
        }
        CharmsInfo info = snap.Value;
        if (!info.Available)
        {
            Note("Charm tracking is for ILT (Infinite Leaftide / ACECustom) worlds."
                 + (info.World.Length > 0 ? " Current world: " + info.World + "." : ""), w);
            Note("Use /ra hub force on to treat this world as ILT.", w);
            return;
        }

        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFE8B333),
            $"{info.AcquiredCount} of {info.Total} acquired  |  {info.ActiveCount} active");
        ImGuiNET.ImGui.PopFont();
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));

        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 3));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.TableRowBg, PanelBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, TabActive);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.TableBorderLight, BtnBord);
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit;
        if (ImGuiNET.ImGui.BeginTable("##charms", 5, flags, new Vector2(w, 0)))
        {
            ImGuiNET.ImGui.TableSetupColumn("Charm", ImGuiTableColumnFlags.WidthStretch);
            ImGuiNET.ImGui.TableSetupColumn("Acquired", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGuiNET.ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 44);
            ImGuiNET.ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 32);
            ImGuiNET.ImGui.TableSetupColumn("Server", ImGuiTableColumnFlags.WidthFixed, 56);
            ImGuiNET.ImGui.TableHeadersRow();

            CharmRow[] rows = info.Charms;
            for (int i = 0; i < rows.Length; i++)
            {
                CharmRow r = rows[i];
                ImGuiNET.ImGui.PushID(i);
                ImGuiNET.ImGui.TableNextRow();

                ImGuiNET.ImGui.TableNextColumn();
                ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFF2F7FC), r.Name);
                if (r.Effect.Length > 0) ImGuiNET.ImGui.SetItemTooltip(r.Effect);

                ImGuiNET.ImGui.TableNextColumn();
                switch (r.Acquired)
                {
                    case "carried":
                        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFF26D9E6), r.Count > 1 ? $"Carried x{r.Count}" : "Carried");
                        ImGuiNET.ImGui.SetItemTooltip("In your pack or equipped.");
                        break;
                    case "seen":
                        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFE8B333), "Stored");
                        ImGuiNET.ImGui.SetItemTooltip("Owned but not carried"
                            + (r.LastSeen.Length > 0 ? " (last carried " + r.LastSeen + ")" : "")
                            + ". The client only sees items you carry.");
                        break;
                    default:
                        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "Not yet");
                        break;
                }

                ImGuiNET.ImGui.TableNextColumn();
                if (r.Acquired != "carried")
                    ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "-");
                else if (r.Active == "on")
                    ColoredText(CharmOn, "ON");
                else if (r.Active == "off")
                    ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "OFF");
                else
                {
                    ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFE8B333), "?");
                    ImGuiNET.ImGui.SetItemTooltip("Waiting for the charm's appraisal (requested automatically).");
                }

                ImGuiNET.ImGui.TableNextColumn();
                string tier = r.Tier <= 0 ? "-" : r.MaxTier > 0 ? $"{r.Tier}/{r.MaxTier}" : r.Tier.ToString(CultureInfo.InvariantCulture);
                ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFF2F7FC), tier);

                ImGuiNET.ImGui.TableNextColumn();
                if (r.Server == "off")
                {
                    ColoredText(CharmDisabled, "Disabled");
                    ImGuiNET.ImGui.SetItemTooltip("The server refused or reported this charm as disabled.");
                }
                else if (r.Server == "on")
                    ColoredText(CharmOn, "Enabled");
                else
                {
                    ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "-");
                    ImGuiNET.ImGui.SetItemTooltip("Not reported. The server only tells players when a charm is refused.");
                }

                ImGuiNET.ImGui.PopID();
            }
            ImGuiNET.ImGui.EndTable();
        }
        ImGuiNET.ImGui.PopStyleColor(4);
        ImGuiNET.ImGui.PopStyleVar();

        ImGuiNET.ImGui.Dummy(new Vector2(0, 6));
        Note("Double-click a carried charm in your pack to toggle it. Status comes from the charm's appraisal "
             + "(Status: ON/OFF) or your character's ability flag. Charms not carried are inactive.", w);
        Note("Stored = carried by this character before; RynthAi remembers it per character.", w);
    }

    /// <summary>TextUnformatted in a packed ImGui colour.</summary>
    private static void ColoredText(uint abgr, string text)
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, abgr);
        ImGuiNET.ImGui.TextUnformatted(text);
        ImGuiNET.ImGui.PopStyleColor();
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
        ["Diagnostics"] = PhosphorIcons.Wrench,
        [CharmsTabName] = PhosphorIcons.Gift,
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
