// ============================================================================
//  RynthCore.Engine - ImGui/Panels/RynthAiFace.cs
//  ImGui face of the RynthAi dashboard (docs/IMGUI_PARITY_PLAN.md §2.1),
//  first laid out from the Avalonia face (UI/Panels/RynthAiPanel.cs), then
//  made compact with Phosphor icons, after DrakBot's dashboard. Top to bottom:
//    title row    RYNTHAI DASHBOARD | target, vitals, lock, opacity -/+,
//                 minimize, pop out
//    header grid  popped out only: meta state, bot activity | pickers (docked,
//                 they are the Loaded files drawer)
//    control row  macro start/stop, combat, buffing, nav, looting and meta
//                 toggles, bot activity ... force rebuff
//    launchers    one row of icon buttons (names in tooltips; labels too
//                 when the panel is wide enough)
//    footer       FPS | version | uptime
//    bars         player vitals, then the target (name + health), each
//                 toggled from the title row. Last on purpose, like DrakBot's
//                 target row: what comes and goes with a fight never moves
//                 the buttons above it.
//    drawers      tabs on the left edge (DashboardDrawers.cs), one open at a
//                 time, a window of their own beside the dashboard: Ranges
//                 (RangesSlideOut.cs), Loaded files, Patrol and Mini Remote
//                 (RynthAiFace.Drawers.cs). Everything that slides out goes
//                 left; only the bars stay at the bottom (Tom 2026-10-05).
//  Minimized keeps the title row, the control row and the bars. The window's
//  minimum height is the content's, and toggling the bars or minimize fits the
//  window to the content. Data: UiSources.RynthAi (a RynthAiView with every
//  string preformatted); clicks: RynthAiCommands.
//
//  Chromeless (like the Avalonia face): the title row is the drag surface.
//  AC thread only; no per-frame string building except the FPS/uptime footer,
//  which is cached until its value changes.
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed partial class RynthAiFace : IImGuiPanel
{
    public const string Title = "RynthAi";

    public static void Register()
    {
        ImGuiPanelHost.Register(Title,
            new PanelSpec(new Vector2(296, 200), new Vector2(MinWidth, 54), PanelChrome.None,
                Background: RynthTheme.RynthAiBackground, BorderColor: RynthTheme.RynthAiBorder,
                Rounding: RynthTheme.RynthAiRounding),
            () => new RynthAiFace());
        // The Loot Editor edits RynthAi's loot profiles and opens from this dashboard.
        LootEditorFace.Register();
        // The Map launcher's panel. Registered here (only with RynthAi present, which
        // feeds its data) so engine init needs no new line in EntryPoint.
        DungeonMapFace.Register();
    }

    private const float MinWidth = 280;

    // ── Palette (RynthAiPanel's, ARGB) ─────────────────────────────────
    private static readonly uint Teal = C(0xFF26D9E6), TealSoft = C(0xFF264C59), Amber = C(0xFFE8B333),
        Green = C(0xFF40D973), Mute = C(0xFFB8C8D8), TextDim = C(0xFFF2F7FC), White = C(0xFFFFFFFF),
        Hp = C(0xFFD93333), Mana = C(0xFF268CF2), ShellBg = C(0xFF0A0F14), BarBg = C(0xFF141F29),
        BtnFill = C(0xFF16283A), BtnBord = C(0xFF34587A), MacroRun = C(0xFF1A5926), MacroStop = C(0xFF401F1F),
        FrFill = C(0xFF47330A), SelectorBg = C(0xFF2D3847);
    private static uint C(uint argb) => RynthTheme.Argb(argb);

    private static readonly uint[] SegmentColors = BuildSegmentColors();

    private string _fpsText = "FPS —", _upText = "Up —";
    private int _fpsShown = -1;
    private long _upShown = -1;
    private bool _patrolOpen;
    // The drawers on the dashboard's left edge (their own window, placed against this one).
    private readonly DashboardDrawers _drawers;
    private readonly PatrolDrawer _patrol = new();
    private readonly RemoteDrawer _remote = new();
    // This frame's data, for the drawers.
    private RynthAiView? _view;
    private RynthAiSnapshot _raw = Empty;
    // Picker toggle: ImGui closes a popup on the press outside it, so remember
    // whether it was showing when the selector press began (a second click closes).
    private readonly int[] _pickerShownFrame = { -10, -10, -10, -10, -10 };
    private readonly bool[] _pressedWhileOpen = new bool[5];
    // The content's height (logical units, body padding included), measured each
    // frame: the window's minimum height. _fit: resize the window to it once.
    private float _contentHeight;
    private bool _fit;
    private const float GripRoom = 12;

    public RynthAiFace()
    {
        // Tab order top to bottom; the first one saved open wins at load.
        _drawers = new DashboardDrawers(new RangesSlideOut(), new FilesDrawer(this), _patrol, _remote);
    }

    public Vector2? MinSize => new Vector2(MinWidth, _contentHeight > 0 ? _contentHeight
        : RynthAiDashboardState.Minimized ? 54 : 180);

    public void OnShown()
    {
        UiSources.RynthAi.Subscribe();
        UiSources.RynthAi.RequestRefresh();
        _fit = true; // drop the empty space a taller saved size leaves under the content
    }

    public void OnHidden()
    {
        UiSources.RynthAi.Unsubscribe();
        if (_patrolOpen) { UiSources.Patrol.Unsubscribe(); _patrolOpen = false; }
        _drawers.Detach();
    }

    public void Draw()
    {
        RynthAiView? view = UiSources.RynthAi.Current?.Value;
        RynthAiSnapshot raw = view?.Raw ?? Empty;
        bool minimized = RynthAiDashboardState.Minimized;
        bool popped = ImGuiPopOuts.InPopOutFrame;
        _view = view;
        _raw = raw;
        uint boxBg = (C(0xFF0A121A) & 0x00FFFFFF) | ((uint)(view?.PanelAlpha ?? 242) << 24);

        Vector2 start = ImGuiNET.ImGui.GetCursorScreenPos();
        float x0 = start.X;
        float width = ImGuiNET.ImGui.GetContentRegionAvail().X;

        TitleRow(raw, x0, width, minimized);
        Gap(4);
        // Popped out (no drawers there): the Profile/Nav/Loot/Meta pickers inline while expanded
        // (the caret on the control row). Docked they are the Loaded files drawer.
        if (popped && !minimized && RynthAiDashboardState.FilesOpen)
        {
            HeaderGrid(view, raw, x0, width);
            Gap(4);
        }
        ControlRow(view, raw, x0, width, minimized, popped, boxBg);
        if (!minimized)
        {
            Gap(4);
            Launchers(x0, width);
            Gap(6);
            Footer(view, x0, width);
        }
        Bars(view, raw, x0, width, boxBg);
        PatrolPopup();
        CharMenu();
        MeasureAndFit(start.Y);
        // Last: a window of its own, placed against this one's rect (hidden popped out).
        _drawers.Draw();
    }

    /// <summary>
    /// Records the content height as the window's minimum (so the bars at the
    /// bottom are never cut off) and, after a toggle, fits the window to it.
    /// </summary>
    private void MeasureAndFit(float startY)
    {
        float pad = ImGuiNET.ImGui.GetCursorStartPos().Y;   // the body's top padding (the bottom's is the same)
        float px = ImGuiNET.ImGui.GetCursorScreenPos().Y - startY + 2 * pad;
        float place = ImGuiPanelHost.PlacementScale();
        // + room for the host's resize grip, which is painted over the body's bottom-right corner.
        _contentHeight = MathF.Ceiling(px / place) + GripRoom;
        if (_fit && !ImGuiPopOuts.InPopOutFrame)
        {
            _fit = false;
            ImGuiPanelHost.RequestSize(Title, new Vector2(ImGuiNET.ImGui.GetWindowWidth() / place, _contentHeight));
        }
    }

    // ── Title row: "R" + "YNTHAI DASHBOARD" | chips (drag surface) ──────

    private void TitleRow(RynthAiSnapshot raw, float x0, float width, bool minimized)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float y = ImGuiNET.ImGui.GetCursorScreenPos().Y;
        const int chips = 7;
        const float chipW = 18, chipH = 16, chipGap = 2;
        float chipsW = chips * chipW + (chips - 1) * chipGap;
        float rowH = 20;

        // Drag surface: everything left of the chips.
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, y));
        ImGuiNET.ImGui.InvisibleButton("##dash_drag", new Vector2(Math.Max(1, width - chipsW), rowH));
        ImGuiPanelHost.DragWindowWithLastItem();

        ImFontPtr title = ImGuiFonts.Get(UiFont.Dash14);
        float ty = y + (rowH - title.FontSize) * 0.5f;
        dl.PushClipRect(new Vector2(x0, y), new Vector2(x0 + Math.Max(1, width - chipsW - 4), y + rowH), true);
        dl.AddText(title, title.FontSize, new Vector2(x0, ty), Teal, "R");
        float rw = CalcWidth(title, "R");
        dl.AddText(title, title.FontSize, new Vector2(x0 + rw, ty), White, "YNTHAI DASHBOARD");
        dl.PopClipRect();

        float cx = x0 + width - chipsW;
        float cy = y + (rowH - chipH) * 0.5f;
        var chip = new Vector2(chipW, chipH);
        const UiFont chipFont = UiFont.Dash11;

        bool target = RynthAiDashboardState.ShowTargetBar;
        if (IconButton("##chip_target", new Vector2(cx, cy), chip, PhosphorIcons.Crosshair, chipFont, BtnFill, target ? Teal : Mute, BtnBord, 2))
        {
            RynthAiDashboardState.SetShowTargetBar(!target);
            _fit = true;
        }
        ImGuiNET.ImGui.SetItemTooltip(target
            ? "Target bar on: the target's name and health at the bottom. Click to hide it."
            : "Target bar off. Click to show the target's name and health at the bottom.");
        cx += chipW + chipGap;
        bool vitals = RynthAiDashboardState.ShowVitals;
        if (IconButton("##chip_vitals", new Vector2(cx, cy), chip, PhosphorIcons.Heartbeat, chipFont, BtnFill, vitals ? Teal : Mute, BtnBord, 2))
        {
            RynthAiDashboardState.SetShowVitals(!vitals);
            _fit = true;
        }
        ImGuiNET.ImGui.SetItemTooltip(vitals
            ? "Player vitals on: your health, stamina and mana at the bottom. Click to hide them."
            : "Player vitals off. Click to show your health, stamina and mana at the bottom.");
        cx += chipW + chipGap;
        if (IconButton("##chip_lock", new Vector2(cx, cy), chip, raw.IsLocked ? PhosphorIcons.Lock : PhosphorIcons.LockOpen,
                chipFont, BtnFill, raw.IsLocked ? Amber : Mute, BtnBord, 2))
            RynthAiCommands.TogglePanelLock();
        ImGuiNET.ImGui.SetItemTooltip(raw.IsLocked ? "Locked. Click to unlock the dashboard." : "Click to lock the dashboard in place.");
        cx += chipW + chipGap;
        if (IconButton("##chip_minus", new Vector2(cx, cy), chip, PhosphorIcons.Minus, chipFont, BtnFill, Mute, BtnBord, 2))
            RynthAiCommands.AdjustOpacity(-0.1f);
        ImGuiNET.ImGui.SetItemTooltip("More transparent");
        cx += chipW + chipGap;
        if (IconButton("##chip_plus", new Vector2(cx, cy), chip, PhosphorIcons.Plus, chipFont, BtnFill, Mute, BtnBord, 2))
            RynthAiCommands.AdjustOpacity(0.1f);
        ImGuiNET.ImGui.SetItemTooltip("More opaque");
        cx += chipW + chipGap;
        if (IconButton("##chip_min", new Vector2(cx, cy), chip,
                minimized ? PhosphorIcons.ArrowsOutLineVertical : PhosphorIcons.ArrowsInLineVertical, chipFont, BtnFill, Mute, BtnBord, 2))
        {
            RynthAiDashboardState.SetMinimized(!minimized);
            _fit = true;   // the window fits the other mode's content next frame
        }
        ImGuiNET.ImGui.SetItemTooltip(minimized ? "Expand" : "Minimize");
        cx += chipW + chipGap;
        if (PanelRouter.CanPopOut)
        {
            if (IconButton("##chip_dock", new Vector2(cx, cy), chip, ImGuiPanelHost.PopOutGlyph, chipFont, BtnFill, Mute, BtnBord, 2))
                ImGuiPanelHost.TogglePopOut(Title);
            ImGuiNET.ImGui.SetItemTooltip(ImGuiPanelHost.PopOutTooltip);
        }

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, y + rowH));
    }

    // ── Header: macro + state (40%) | Profile/Nav/Loot/Meta/Buffs pickers (60%) ──

    private void HeaderGrid(RynthAiView? view, RynthAiSnapshot raw, float x0, float width)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float y0 = ImGuiNET.ImGui.GetCursorScreenPos().Y + 2;
        float leftW = width * 0.4f;

        // Left: Meta State / Bot Activity (the macro start/stop is the control row's first square).
        ImFontPtr f10 = ImGuiFonts.Get(UiFont.Dash10);
        float ly = y0;
        dl.AddText(f10, f10.FontSize, new Vector2(x0, ly), Mute, "Meta State:");
        dl.AddText(f10, f10.FontSize, new Vector2(x0, ly + f10.FontSize + 1), Amber, view?.MetaStateText ?? "Default");
        ly += 2 * (f10.FontSize + 1) + 4;
        dl.AddText(f10, f10.FontSize, new Vector2(x0, ly), Mute, "Bot Activity:");
        dl.AddText(f10, f10.FontSize, new Vector2(x0, ly + f10.FontSize + 1), Amber, view?.BotActivityText ?? "Idle");
        float leftBottom = ly + 2 * (f10.FontSize + 1);

        // Right: five selector rows (label 36 px, picker fills, spacing 3).
        float rx = x0 + leftW, rw = width - leftW, ry = y0;
        // The fold arrow sits at the end of the Profile row, where it always fits.
        const float foldW = 18f;
        Selector(0, "Profile:", view?.ProfileText ?? "Default", raw.Profiles, raw.SelectedProfileIdx, 3, rx, ry, rw - foldW - 2, null);
        if (IconButton("##files_fold", new Vector2(rx + rw - foldW, ry), new Vector2(foldW, 16), PhosphorIcons.CaretUp, UiFont.Dash11, BtnFill, Mute, BtnBord, 2))
            RynthAiDashboardState.SetFilesOpen(false);
        ImGuiNET.ImGui.SetItemTooltip("Fold the loaded files into one line");
        ry += 16 + 3;
        Selector(1, "Nav:", view?.NavText ?? "None", raw.NavProfiles, raw.SelectedNavIdx, 0, rx, ry, rw, null);
        ry += 16 + 3;
        Selector(2, "Loot:", view?.LootText ?? "None", raw.LootProfiles, raw.SelectedLootIdx, 1, rx, ry, rw, raw);
        ry += 16 + 3;
        Selector(3, "Meta:", view?.MetaText ?? "None", raw.MetaProfiles, raw.SelectedMetaIdx, 2, rx, ry, rw, null);
        ry += 16 + 3;
        Selector(4, "Buffs:", view?.BuffText ?? "Built-in", raw.BuffProfiles, raw.SelectedBuffIdx, 4, rx, ry, rw, null, spellsButton: true);
        ry += 16;

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, Math.Max(leftBottom, ry) + 4));
    }

    private static readonly string[] SelectorIds = { "##sel_profile", "##sel_nav", "##sel_loot", "##sel_meta", "##sel_buff" };
    private static readonly string[] PickerIds = { "##pick_profile", "##pick_nav", "##pick_loot", "##pick_meta", "##pick_buff" };
    private static readonly string[] NoneItems = { "None" };

    /// <summary>
    /// One file picker row. <paramref name="kind"/> is RynthPluginSelectProfile's list (0 nav,
    /// 1 loot, 2 meta, 3 settings profile, 4 buff profile). <paramref name="lootEdit"/> adds the
    /// Loot Editor button, <paramref name="spellsButton"/> the RynthAi Spells window button.
    /// </summary>
    private void Selector(int slot, string label, string text, string[] items, int selected, int kind,
        float x, float y, float w, RynthAiSnapshot? lootEdit, bool spellsButton = false)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImFontPtr f10 = ImGuiFonts.Get(UiFont.Dash10), f9 = ImGuiFonts.Get(UiFont.Dash9);
        dl.AddText(f10, f10.FontSize, new Vector2(x, y + (16 - f10.FontSize) * 0.5f), Mute, label);

        float editW = 0;
        if (spellsButton)
        {
            editW = 18;
            var ep = new Vector2(x + w - editW, y);
            if (IconButton("##buff_spells", ep, new Vector2(editW, 16), PhosphorIcons.Sparkle, UiFont.Dash11, SelectorBg, Mute, 0, 2))
                RynthAiCommands.ApplyRemoteCommand("spells", "toggle");
            ImGuiNET.ImGui.SetItemTooltip("RynthAi Spells: browse the spell list and build buff profiles.");
            editW += 3;
        }
        if (lootEdit != null)
        {
            editW = 18;
            var ep = new Vector2(x + w - editW, y);
            if (IconButton("##loot_edit", ep, new Vector2(editW, 16), PhosphorIcons.TreasureChest, UiFont.Dash11, SelectorBg, Mute, 0, 2))
            {
                string path = !string.IsNullOrEmpty(lootEdit.VendorProfilePath) ? lootEdit.VendorProfilePath : lootEdit.CurrentLootPath;
                // The in-game Loot Editor panel. The external editor is one click away on the
                // panel's toolbar (LootEditorFace.LaunchExternal, an STA thread of its own).
                LootEditorFace.ToggleFor(path);
            }
            ImGuiNET.ImGui.SetItemTooltip("Loot Editor: the loot profile in use, or the open vendor's AutoVendor profile.");
            editW += 3;
        }

        var pos = new Vector2(x + 36, y);
        var size = new Vector2(Math.Max(20, w - 36 - editW), 16);
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(SelectorIds[slot], size);
        bool hovered = ImGuiNET.ImGui.IsItemHovered();
        if (ImGuiNET.ImGui.IsItemActivated())
            _pressedWhileOpen[slot] = ImGuiNET.ImGui.GetFrameCount() - _pickerShownFrame[slot] <= 1;
        bool open = ImGuiNET.ImGui.IsPopupOpen(PickerIds[slot]);
        dl.AddRectFilled(pos, pos + size, hovered ? Lighten(SelectorBg) : SelectorBg, 2);
        dl.AddRect(pos, pos + size, BtnBord, 2);
        const float arrowW = 12;
        dl.PushClipRect(pos, pos + size - new Vector2(arrowW + 4, 0), true);
        dl.AddText(f9, f9.FontSize, pos + new Vector2(5, (16 - f9.FontSize) * 0.5f), White, text);
        dl.PopClipRect();
        PhosphorIcons.DrawCentered(dl, f10, open ? PhosphorIcons.CaretUp : PhosphorIcons.CaretDown,
            new Vector2(pos.X + size.X - arrowW - 3, y), new Vector2(arrowW, 16), Mute);

        if (clicked && !_pressedWhileOpen[slot] && !open)
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(pos.X, pos.Y + size.Y + 2));
            ImGuiNET.ImGui.OpenPopup(PickerIds[slot]);
        }
        Picker(slot, items.Length == 0 ? NoneItems : items, Math.Clamp(selected, 0, Math.Max(0, items.Length - 1)), kind, items.Length > 0);
    }

    private void Picker(int slot, string[] items, int selected, int kind, bool real)
    {
        // Same look as the Avalonia picker: 200 wide, teal border, radius 6.
        float maxH = Math.Min(items.Length * 26 + 12, Math.Max(60,
            ImGuiNET.ImGui.GetIO().DisplaySize.Y - ImGuiNET.ImGui.GetCursorScreenPos().Y - 20));
        ImGuiNET.ImGui.SetNextWindowSize(new Vector2(200, maxH));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));
        bool open = ImGuiNET.ImGui.BeginPopup(PickerIds[slot]);
        ImGuiNET.ImGui.PopStyleVar(4);
        ImGuiNET.ImGui.PopStyleColor(2);
        if (!open) return;
        _pickerShownFrame[slot] = ImGuiNET.ImGui.GetFrameCount();

        float w = ImGuiNET.ImGui.GetContentRegionAvail().X;
        for (int i = 0; i < items.Length; i++)
        {
            ImGuiNET.ImGui.PushID(i);
            bool isSel = i == selected;
            Vector2 p = ImGuiNET.ImGui.GetCursorScreenPos();
            if (ItemButton("##item", p, new Vector2(w, 24), items[i], isSel ? TealSoft : BtnFill, isSel ? Teal : White))
            {
                if (real) RynthAiCommands.SelectProfile(kind, i);
                ImGuiNET.ImGui.CloseCurrentPopup();
            }
            ImGuiNET.ImGui.PopID();
        }
        ImGuiNET.ImGui.EndPopup();
    }

    private static bool ItemButton(string id, Vector2 pos, Vector2 size, string text, uint bg, uint fg)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(id, size);
        uint fill = ImGuiNET.ImGui.IsItemActive() ? Darken(bg) : ImGuiNET.ImGui.IsItemHovered() ? Lighten(bg) : bg;
        dl.AddRectFilled(pos, pos + size, fill, 3);
        dl.AddRect(pos, pos + size, BtnBord, 3);
        ImFontPtr f = ImGuiFonts.Get(UiFont.Dash11);
        dl.PushClipRect(pos, pos + size, true);
        dl.AddText(f, f.FontSize, pos + new Vector2(8, (size.Y - f.FontSize) * 0.5f), fg, text);
        dl.PopClipRect();
        return clicked;
    }

    // ── Control row: (ON/OFF) combat, buffing, nav, looting, meta ... force rebuff ──

    private const float Square = 24, SquareStep = 27, RowPad = 3;

    private void ControlRow(RynthAiView? view, RynthAiSnapshot raw, float x0, float width, bool minimized, bool popped, uint boxBg)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float top = ImGuiNET.ImGui.GetCursorScreenPos().Y;
        float bottom = top + Square + 2 * RowPad;
        dl.AddRectFilled(new Vector2(x0, top), new Vector2(x0 + width, bottom), boxBg, 3);
        dl.AddRect(new Vector2(x0, top), new Vector2(x0 + width, bottom), BtnBord, 3);

        float x = x0 + 5, y = top + RowPad;
        // Macro start/stop: a square like the toggles, green with a Stop glyph while running.
        {
            bool on = raw.MacroRunning;
            if (IconButton("##macro", new Vector2(x, y), new Vector2(Square, Square), on ? PhosphorIcons.Stop : PhosphorIcons.Play,
                    UiFont.Dash14, on ? MacroRun : BarBg, on ? White : Mute, on ? Green : BtnBord, 3))
                RynthAiCommands.ToggleMacro();
            ImGuiNET.ImGui.SetItemTooltip(on ? "Macro running. Click to stop it." : "Macro stopped. Click to start it.");
            x += SquareStep + 6;
        }
        Toggle("##t_combat", PhosphorIcons.Sword, raw.CombatEnabled, 0, new Vector2(x, y), "Combat");
        x += SquareStep;
        Toggle("##t_buff", PhosphorIcons.Sparkle, raw.BuffingEnabled, 1, new Vector2(x, y), "Buffing");
        x += SquareStep;
        Toggle("##t_nav", PhosphorIcons.SneakerMove, raw.NavigationEnabled, 2, new Vector2(x, y), "Navigation");
        x += SquareStep;
        Toggle("##t_loot", PhosphorIcons.Bag, raw.LootingEnabled, 3, new Vector2(x, y), "Looting");
        x += SquareStep;
        Toggle("##t_meta", PhosphorIcons.Code, raw.MetaEnabled, 4, new Vector2(x, y), "Meta (the macro rules)");
        x += SquareStep;

        // Popped out: the pickers fold out from a caret left of FR (expanded mode, while folded away).
        // Docked they are the Loaded files drawer on the left edge.
        const float caretW = 16f;
        bool caret = popped && !minimized && !RynthAiDashboardState.FilesOpen;
        float frX = x0 + width - 5 - Square;
        if (caret)
        {
            if (IconButton("##files_open", new Vector2(frX - 4 - caretW, y), new Vector2(caretW, Square), PhosphorIcons.CaretDown,
                    UiFont.Dash11, BtnFill, Teal, BtnBord, 2))
                RynthAiDashboardState.SetFilesOpen(true);
            ImGuiNET.ImGui.SetItemTooltip("Profile: " + (view?.ProfileText ?? "Default") + "\nNav: " + (view?.NavText ?? "None") +
                "\nLoot: " + (view?.LootText ?? "None") + "\nMeta: " + (view?.MetaText ?? "None") +
                "\nBuffs: " + (view?.BuffText ?? "Built-in") +
                "\n\nClick to show the Profile, Nav, Loot, Meta and Buffs pickers.");
        }

        // What the bot is doing, always in view (every mode, minimized too): between the toggles and FR.
        {
            ImFontPtr af = ImGuiFonts.Get(UiFont.Dash10);
            float ax = x + 2, aRight = (caret ? frX - 4 - caretW : frX) - 6;
            if (aRight - ax > 20)
            {
                string activity = view?.BotActivityText ?? "Idle";
                float ay = y + (Square - af.FontSize) * 0.5f;
                dl.PushClipRect(new Vector2(ax, y), new Vector2(aRight, y + Square), true);
                dl.AddText(af, af.FontSize, new Vector2(ax, ay), Amber, activity);
                dl.PopClipRect();
                ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(ax, y));
                ImGuiNET.ImGui.InvisibleButton("##activity", new Vector2(aRight - ax, Square));
                ImGuiNET.ImGui.SetItemTooltip("Bot activity: " + activity + "\nMeta state: " + (view?.MetaStateText ?? "Default"));
            }
        }

        // Force rebuff, set apart at the right (DrakBot's place for it).
        if (IconButton("##fr", new Vector2(x0 + width - 5 - Square, y), new Vector2(Square, Square), PhosphorIcons.ArrowsClockwise,
                UiFont.Dash14, FrFill, Amber, BtnBord, 3))
            RynthAiCommands.ForceRebuff();
        if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right))
            RynthAiCommands.CancelForceRebuff();
        ImGuiNET.ImGui.SetItemTooltip("Force rebuff (FR): recast all buffs. Right-click: cancel.");

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, bottom));
        ImGuiNET.ImGui.Dummy(new Vector2(width, 0));
    }

    private void Toggle(string id, string icon, bool on, int subsystem, Vector2 pos, string name)
    {
        if (IconButton(id, pos, new Vector2(Square, Square), icon, UiFont.Dash14, on ? TealSoft : BarBg, on ? Teal : Mute, on ? Teal : BtnBord, 3))
            RynthAiCommands.SetSubsystemEnabled(subsystem, !on);
        ImGuiNET.ImGui.SetItemTooltip(on ? name + " on. Click to turn it off." : name + " off. Click to turn it on.");
    }

    // ── Launchers: one row of icon buttons ──────────────────────────────

    private const float LauncherH = 22, LauncherGap = 2;

    private void Launchers(float x0, float width)
    {
        float y = ImGuiNET.ImGui.GetCursorScreenPos().Y;
        const int buttons = 10;
        float bw = MathF.Floor((width - buttons * LauncherGap) / buttons);
        // Short labels only when every button has room for its icon and label.
        bool labels = bw >= 68;
        float x = x0;

        Launcher("##l_meta", PhosphorIcons.ListChecks, "Meta", "Meta: the macro rules", x, y, bw, labels, () => PanelRouter.Toggle("Meta"));
        x += bw + LauncherGap;
        Launcher("##l_lua", PhosphorIcons.FileCode, "Lua", "Lua scripts", x, y, bw, labels, () => PanelRouter.Toggle("Lua"));
        x += bw + LauncherGap;
        // Monsters opens the Damage panel (the basic Monsters panel and its
        // Simple/Advanced chip were retired 2026-10-01).
        Launcher("##l_mon", PhosphorIcons.Skull, "Monsters", "Monsters: weapons, spells and rules per monster (the Damage panel)",
            x, y, bw, labels, PanelRouter.ToggleMonsters);
        x += bw + LauncherGap;
        Launcher("##l_set", PhosphorIcons.Gear, "Settings", "Settings", x, y, bw, labels, () => PanelRouter.Toggle("Settings"));
        x += bw + LauncherGap;
        Launcher("##l_nav", PhosphorIcons.MapTrifold, "Nav", "Nav: routes", x, y, bw, labels, () => PanelRouter.Toggle("Nav"));
        x += bw + LauncherGap;
        Launcher("##l_map", PhosphorIcons.MapPin, "Map", "Dungeon Map (shows while you're in a dungeon)", x, y, bw, labels,
            () => PanelRouter.Toggle(DungeonMapFace.Title));
        x += bw + LauncherGap;
        Launcher("##l_items", PhosphorIcons.Backpack, "Items", "Items", x, y, bw, labels, () => PanelRouter.Toggle("Items"));
        x += bw + LauncherGap;
        bool popped = ImGuiPopOuts.InPopOutFrame;
        Launcher("##l_patrol", PhosphorIcons.Footprints, "Patrol",
            popped ? "Patrol. Left-click: start dungeon patrol.  Right-click: routes & recorded hazards."
                   : "Patrol. Left-click: start dungeon patrol.  Right-click: routes & recorded hazards (the Patrol drawer, left).",
            x, y, bw, labels, () => RynthAiCommands.SendNavCommand("{\"Cmd\":\"dunPatrol\"}"));
        if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (popped)
            {
                // Popped out there are no drawers: the popup, as before.
                ImGuiNET.ImGui.SetNextWindowPos(ImGuiNET.ImGui.GetItemRectMin(), ImGuiCond.Always, new Vector2(0, 1));
                ImGuiNET.ImGui.OpenPopup("##patrol");
            }
            else _drawers.Toggle(_patrol);
        }
        x += bw + LauncherGap;

        // Char and Hub open RynthAi's own ImGui overlay windows (Mini Remote, which is the ILT
        // Hub, its section windows, Inventory HUDs). They draw only when the engine hands plugins
        // its ImGui context.
        Launcher("##l_char", PhosphorIcons.User, "Char",
            "Char. Left-click: ILT Hub / Mini Remote (/ra hub show).  Right-click: Hub windows, Progression (Skills panel).",
            x, y, bw, labels, () => RynthAiCommands.ApplyRemoteCommand("hub", "show"));
        if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            ImGuiNET.ImGui.SetNextWindowPos(ImGuiNET.ImGui.GetItemRectMin(), ImGuiCond.Always, new Vector2(0, 1));
            ImGuiNET.ImGui.OpenPopup("##charmenu");
        }
        x += bw + LauncherGap;
        // Right-click is the Mini Remote drawer, as Patrol's is its drawer; popped out there are
        // no drawers, so it shows the floating Mini Remote. Inventory HUDs setup is in the
        // Mini Remote's Options menu.
        Launcher("##l_hub", PhosphorIcons.SquaresFour, "Hub",
            popped ? "Hub. Left-click: floating Mini Remote (/ra remote).  Right-click: show the floating Mini Remote."
                   : "Hub. Left-click: floating Mini Remote (/ra remote).  Right-click: the Mini Remote drawer (left), with its Options.",
            x, y, bw, labels, () => RynthAiCommands.ApplyRemoteCommand("remote", "toggle"));
        if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (popped) RynthAiCommands.ApplyRemoteCommand("remote", "show");
            else _drawers.Toggle(_remote);
        }

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, y + LauncherH));
    }

    private void Launcher(string id, string icon, string label, string tooltip, float x, float y, float w, bool showLabel, Action? onClick)
    {
        var pos = new Vector2(x, y);
        var size = new Vector2(Math.Max(10, w), LauncherH);
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(id, size);
        ImGuiNET.ImGui.SetItemTooltip(tooltip);
        uint fill = ImGuiNET.ImGui.IsItemActive() ? Darken(BtnFill) : ImGuiNET.ImGui.IsItemHovered() ? Lighten(BtnFill) : BtnFill;
        dl.AddRectFilled(pos, pos + size, fill, 3);
        dl.AddRect(pos, pos + size, BtnBord, 3);

        ImFontPtr iconFont = ImGuiFonts.Get(UiFont.Dash14);
        if (showLabel)
        {
            ImFontPtr f9 = ImGuiFonts.Get(UiFont.Dash9);
            const float iconBox = 16;
            PhosphorIcons.DrawCentered(dl, iconFont, icon, pos + new Vector2(4, 0), new Vector2(iconBox, LauncherH), Mute);
            dl.PushClipRect(pos, pos + size, true);
            dl.AddText(f9, f9.FontSize, new Vector2(pos.X + 4 + iconBox + 4, pos.Y + (LauncherH - f9.FontSize) * 0.5f), Mute, label);
            dl.PopClipRect();
        }
        else
        {
            PhosphorIcons.DrawCentered(dl, iconFont, icon, pos, size, Mute);
        }
        if (clicked && onClick != null)
        {
            onClick();
        }
    }

    // ── Footer: FPS | version | uptime ─────────────────────────────────────

    private void Footer(RynthAiView? view, float x0, float width)
    {
        int fps = (int)MathF.Round(D3D9.EndSceneHook.MeasuredFps);
        if (fps != _fpsShown) { _fpsShown = fps; _fpsText = fps > 0 ? $"FPS {fps}" : "FPS —"; }
        DateTime started = EntryPoint.InitStartedUtc;
        if (started != DateTime.MinValue)
        {
            TimeSpan up = DateTime.UtcNow - started;
            long secs = (long)up.TotalSeconds;
            if (secs != _upShown)
            {
                _upShown = secs;
                _upText = up.TotalHours >= 1 ? $"Up {(int)up.TotalHours}:{up.Minutes:D2}:{up.Seconds:D2}" : $"Up {up.Minutes}:{up.Seconds:D2}";
            }
        }
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImFontPtr f10 = ImGuiFonts.Get(UiFont.Dash10);
        float y = ImGuiNET.ImGui.GetCursorScreenPos().Y;
        float fpsW = CalcWidth(f10, _fpsText), upW = CalcWidth(f10, _upText);
        dl.AddText(f10, f10.FontSize, new Vector2(x0, y), Mute, _fpsText);
        dl.AddText(f10, f10.FontSize, new Vector2(x0 + width - upW, y), Mute, _upText);

        // The version between them: centred when there's room, else after FPS, clipped short of the uptime.
        string version = view?.Version ?? string.Empty;
        if (version.Length > 0)
        {
            float left = x0 + fpsW + 8, right = x0 + width - upW - 8;
            if (right > left)
            {
                float vw = CalcWidth(f10, version);
                float vx = x0 + (width - vw) * 0.5f;
                if (vx < left || vx + vw > right) vx = left;
                dl.PushClipRect(new Vector2(left, y), new Vector2(right, y + f10.FontSize + 2), true);
                dl.AddText(f10, f10.FontSize, new Vector2(vx, y), Mute, version);
                dl.PopClipRect();
            }
        }
        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, y + f10.FontSize + 2));
        ImGuiNET.ImGui.Dummy(new Vector2(width, 0));
    }

    // ── Bars (bottom): player vitals, then the target ──────────────────────

    private const float VitalsH = 14 + 3 + 14 + 3 + 14;

    private void Bars(RynthAiView? view, RynthAiSnapshot raw, float x0, float width, uint boxBg)
    {
        bool vitals = RynthAiDashboardState.ShowVitals, target = RynthAiDashboardState.ShowTargetBar;
        if (!vitals && !target) return;
        Gap(4);

        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImFontPtr f11 = ImGuiFonts.Get(UiFont.Dash11), f10 = ImGuiFonts.Get(UiFont.Dash10);
        // The target's stamina/mana rows are held while that option is on, so they
        // don't grow the box (and the window) as targets come and go.
        bool subRows = raw.ShowTargetStaminaMana;
        float targetH = f11.FontSize + 2 + 3 + 12 + (subRows ? 2 * (13 + 3) - 3 : 0);
        float top = ImGuiNET.ImGui.GetCursorScreenPos().Y;
        float height = 5 + (vitals ? VitalsH : 0) + (vitals && target ? 6 : 0) + (target ? targetH : 0) + 4;
        var tl = new Vector2(x0, top);
        var br = new Vector2(x0 + width, top + height);
        dl.AddRectFilled(tl, br, boxBg, 3);
        dl.AddRect(tl, br, BtnBord, 3);

        float rx = x0 + 5, rw = width - 10, ry = top + 5;
        if (vitals)
        {
            VitalRow(dl, rx, ry, rw, PhosphorIcons.Heart, view?.Health ?? default, Hp, f10); ry += 14 + 3;
            VitalRow(dl, rx, ry, rw, PhosphorIcons.Lightning, view?.Stamina ?? default, Green, f10); ry += 14 + 3;
            VitalRow(dl, rx, ry, rw, PhosphorIcons.Drop, view?.Mana ?? default, Mana, f10); ry += 14;
            if (target) ry += 6;
        }
        if (target)
        {
            // Crosshair + name | health, then the 15-segment bar.
            string hpText = raw.TargetHealthDisplay;
            float hpW = CalcWidth(f11, hpText);
            float iconW = CalcWidth(f11, PhosphorIcons.Crosshair);
            dl.AddText(f11, f11.FontSize, new Vector2(rx, ry), Mute, PhosphorIcons.Crosshair);
            dl.PushClipRect(new Vector2(rx, ry), new Vector2(rx + rw - hpW - 4, ry + f11.FontSize + 2), true);
            dl.AddText(f11, f11.FontSize, new Vector2(rx + iconW + 3, ry), TextDim, view?.TargetHeadline ?? "NO TARGET");
            dl.PopClipRect();
            dl.AddText(f11, f11.FontSize, new Vector2(rx + rw - hpW, ry), White, hpText);
            ry += f11.FontSize + 2 + 3;

            // 15-segment gradient bar, 10 high, 2 px between segments.
            int lit = view?.TargetSegmentsLit ?? 0;
            float segW = (rw - 2 * (RynthAiView.Segments - 1)) / RynthAiView.Segments;
            for (int i = 0; i < RynthAiView.Segments; i++)
            {
                float sx = rx + i * (segW + 2);
                dl.AddRectFilled(new Vector2(sx, ry + 1), new Vector2(sx + segW, ry + 11), i < lit ? SegmentColors[i] : BarBg, 1);
            }
            ry += 12;

            if (subRows)
            {
                bool shown = view != null && view.ShowTargetSubBars;
                ry += 3;
                CompactBar(dl, rx, ry, rw, shown ? view!.TargetStamina : default, Green, f10); ry += 13 + 3;
                CompactBar(dl, rx, ry, rw, shown ? view!.TargetMana : default, Mana, f10);
            }
        }

        ImGuiNET.ImGui.SetCursorScreenPos(new Vector2(x0, top + height));
        ImGuiNET.ImGui.Dummy(new Vector2(width, 0));
    }

    private static void CompactBar(ImDrawListPtr dl, float x, float y, float w, VitalView bar, uint color, ImFontPtr font)
    {
        var a = new Vector2(x, y + 1);
        var b = new Vector2(x + w, y + 12);
        dl.AddRectFilled(a, b, BarBg, 2);
        if (bar.Fraction > 0) dl.AddRectFilled(a, new Vector2(x + w * bar.Fraction, b.Y), color, 2);
        dl.AddText(font, font.FontSize, new Vector2(x + 4, y + 1 + (11 - font.FontSize) * 0.5f), White, bar.Text ?? "");
    }

    private static void VitalRow(ImDrawListPtr dl, float x, float y, float w, string icon, VitalView bar, uint color, ImFontPtr font)
    {
        var a = new Vector2(x, y + 1);
        var b = new Vector2(x + w, y + 14);
        dl.AddRectFilled(a, b, BarBg, 6);
        if (bar.Fraction > 0) dl.AddRectFilled(a, new Vector2(x + w * bar.Fraction, b.Y), color, 6);
        float ty = y + 1 + (13 - font.FontSize) * 0.5f;
        dl.AddText(font, font.FontSize, new Vector2(x + 4, ty), Mute, icon);
        dl.AddText(font, font.FontSize, new Vector2(x + 4 + CalcWidth(font, icon) + 3, ty), White, bar.Text ?? "");
    }

    // ── Char menu (right-click Char): RynthAi's character windows ──────────

    /// <summary>ILT Hub section windows: menu label and the "/ra hub open" section name RynthAi parses.</summary>
    private static readonly (string Label, string Section)[] HubSections =
    {
        ("Character", "character"), ("Quests", "quests"), ("Pets", "pets"),
        ("Banking", "banking"), ("Gear", "gear"), ("Games", "games"), ("Guardian", "guardian"),
    };

    private static void CharMenu()
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        bool open = ImGuiNET.ImGui.BeginPopup("##charmenu");
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor(2);
        if (!open) return;
        try
        {
            // The Mini Remote is the ILT Hub; each former Hub tab is its own window (toggled).
            if (ImGuiNET.ImGui.MenuItem("Mini Remote (ILT Hub)", "/ra hub"))
                RynthAiCommands.ApplyRemoteCommand("hub", "toggle");
            ImGuiNET.ImGui.Separator();
            foreach ((string label, string section) in HubSections)
            {
                if (ImGuiNET.ImGui.MenuItem(label, "/ra hub open " + section))
                    RynthAiCommands.ApplyRemoteCommand("hub", "open " + section + " toggle");
            }
            ImGuiNET.ImGui.Separator();
            if (ImGuiNET.ImGui.MenuItem("Progression (Skills panel)"))
                SkillsFace.ShowProgression();
            ImGuiNET.ImGui.SetItemTooltip("XP planner, augmentations and enlightenment (ILT worlds)");
        }
        finally
        {
            ImGuiNET.ImGui.EndPopup();
        }
    }

    // ── Patrol popup (right-click Patrol, popped out; docked it's the Patrol drawer) ──

    private void PatrolPopup()
    {
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        ImGuiNET.ImGui.SetNextWindowSizeConstraints(new Vector2(260, 0), new Vector2(260, 376));
        bool open = ImGuiNET.ImGui.BeginPopup("##patrol");
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor(2);

        if (open != _patrolOpen)
        {
            _patrolOpen = open;
            if (open) { UiSources.Patrol.Subscribe(); UiSources.Patrol.RequestRefresh(); }
            else UiSources.Patrol.Unsubscribe();
        }
        if (!open) return;

        var snap = UiSources.Patrol.Current;
        PatrolInfo info = snap?.Value ?? new PatrolInfo();
        PatrolBody(info, withTitle: true);
        ImGuiNET.ImGui.EndPopup();
    }

    /// <summary>The patrol lists at the cursor (the popup's body and the Patrol drawer's).</summary>
    private static void PatrolBody(PatrolInfo info, bool withTitle)
    {
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash10));
        if (withTitle)
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash11));
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFF26D9E6), PatrolTitle);
            ImGuiNET.ImGui.PopFont();
        }

        Section("THIS DUNGEON");
        if (info.InDungeon)
        {
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFF2F7FC), $"0x{info.CurrentLandblock}  ·  {info.CurrentHazards} hazard cell(s)");
            if (info.CurrentHazards > 0)
            {
                ImGuiNET.ImGui.SameLine();
                if (Mini("Clear##cur"))
                    RynthAiCommands.SendNavCommand($"{{\"Cmd\":\"clearHazards\",\"NavName\":\"{info.CurrentLandblock}\"}}", refreshPatrol: true);
            }
            if (Mini(MarkHazardLabel))
                RynthAiCommands.SendNavCommand("{\"Cmd\":\"markHazardHere\"}", refreshPatrol: true);
            ImGuiNET.ImGui.SameLine();
            if (Mini("Unmark"))
                RynthAiCommands.SendNavCommand("{\"Cmd\":\"unmarkHazardHere\"}", refreshPatrol: true);
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash9));
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, RynthTheme.Vec(0xFFB8C8D8));
            ImGuiNET.ImGui.TextWrapped("Stand on the lava/acid, click Mark, then re-run Patrol.");
            ImGuiNET.ImGui.PopStyleColor();
            ImGuiNET.ImGui.PopFont();
        }
        else
        {
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "Not in a dungeon.");
        }

        Section($"RECORDED HAZARDS ({info.Dungeons.Length})");
        if (info.Dungeons.Length == 0)
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "None recorded yet.");
        for (int i = 0; i < info.Dungeons.Length; i++)
        {
            DungeonHazards d = info.Dungeons[i];
            ImGuiNET.ImGui.PushID(i);
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFF2F7FC), $"0x{d.Landblock}  ·  {d.Cells} cell(s)");
            ImGuiNET.ImGui.SameLine();
            if (Mini("Clear"))
                RynthAiCommands.SendNavCommand($"{{\"Cmd\":\"clearHazards\",\"NavName\":\"{d.Landblock}\"}}", refreshPatrol: true);
            ImGuiNET.ImGui.PopID();
        }
        if (info.Dungeons.Length > 0 && Mini("Clear all recorded hazards"))
            RynthAiCommands.SendNavCommand("{\"Cmd\":\"clearHazardsAll\"}", refreshPatrol: true);

        Section($"SAVED ROUTES ({info.Routes.Length})");
        if (info.Routes.Length == 0)
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), "No .nav routes saved.");
        foreach (string name in info.Routes)
            ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFF2F7FC), "• " + name);

        ImGuiNET.ImGui.PopFont();
    }

    private const string PatrolTitle = PhosphorIcons.Footprints + " PATROL & ROUTES";
    private const string MarkHazardLabel = PhosphorIcons.Warning + " Mark cell as hazard";

    private static void Section(string text)
    {
        ImGuiNET.ImGui.Dummy(new Vector2(0, 4));
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash9));
        ImGuiNET.ImGui.TextColored(RynthTheme.Vec(0xFFB8C8D8), text);
        ImGuiNET.ImGui.PopFont();
    }

    private static bool Mini(string label)
    {
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash9));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Button, BtnFill);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Lighten(BtnFill));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonActive, Darken(BtnFill));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Amber);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, BtnBord);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 1));
        bool clicked = ImGuiNET.ImGui.Button(label);
        ImGuiNET.ImGui.PopStyleVar(3);
        ImGuiNET.ImGui.PopStyleColor(5);
        ImGuiNET.ImGui.PopFont();
        return clicked;
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    /// <summary>Draws the Avalonia face's flat button frame; returns true on left click.</summary>
    private static bool ButtonFrame(string id, Vector2 pos, Vector2 size, uint bg, uint border, float rounding)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight)
                       && ImGuiNET.ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        uint fill = ImGuiNET.ImGui.IsItemActive() ? Darken(bg) : ImGuiNET.ImGui.IsItemHovered() ? Lighten(bg) : bg;
        dl.AddRectFilled(pos, pos + size, fill, rounding);
        if (border != 0) dl.AddRect(pos, pos + size, border, rounding);
        return clicked;
    }

    /// <summary>A flat button with centred text; returns true on left click.</summary>
    private static bool Button(string id, Vector2 pos, Vector2 size, string text, UiFont font,
        uint bg, uint fg, uint border, float rounding)
    {
        bool clicked = ButtonFrame(id, pos, size, bg, border, rounding);
        ImFontPtr f = ImGuiFonts.Get(font);
        float tw = CalcWidth(f, text);
        ImGuiNET.ImGui.GetWindowDrawList().AddText(f, f.FontSize, pos + new Vector2((size.X - tw) * 0.5f, (size.Y - f.FontSize) * 0.5f), fg, text);
        return clicked;
    }

    /// <summary>A flat button with a Phosphor icon centred on it; returns true on left click.</summary>
    private static bool IconButton(string id, Vector2 pos, Vector2 size, string icon, UiFont font,
        uint bg, uint fg, uint border, float rounding)
    {
        bool clicked = ButtonFrame(id, pos, size, bg, border, rounding);
        PhosphorIcons.DrawCentered(ImGuiNET.ImGui.GetWindowDrawList(), ImGuiFonts.Get(font), icon, pos, size, fg);
        return clicked;
    }

    private static float CalcWidth(ImFontPtr font, string text)
    {
        ImGuiNET.ImGui.PushFont(font);
        float w = ImGuiNET.ImGui.CalcTextSize(text).X;
        ImGuiNET.ImGui.PopFont();
        return w;
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

    private static void Gap(float h) => ImGuiNET.ImGui.Dummy(new Vector2(0, h));

    private static uint[] BuildSegmentColors()
    {
        var colors = new uint[RynthAiView.Segments];
        for (int i = 0; i < colors.Length; i++)
        {
            float t = (float)i / (colors.Length - 1);
            uint r, g, b;
            if (t < 0.33f) { float f = t / 0.33f; r = 255; g = (uint)(f * 255); b = 0; }
            else if (t < 0.66f) { float f = (t - 0.33f) / 0.33f; r = (uint)((1f - f) * 255); g = 255; b = 0; }
            else { float f = (t - 0.66f) / 0.34f; r = 0; g = 255; b = (uint)(f * 255); }
            colors[i] = C(0xFF000000 | (r << 16) | (g << 8) | b);
        }
        return colors;
    }

    private static readonly RynthAiSnapshot Empty = new();
}
