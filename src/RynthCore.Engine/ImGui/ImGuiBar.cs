// ============================================================================
//  RynthCore.Engine - ImGui/ImGuiBar.cs
//  The overlay bar's ImGui face (docs/IMGUI_PARITY_PLAN.md §2.0).
//
//  Same content as the Avalonia bar, as square Phosphor icon buttons with
//  the names in tooltips: the grip (RC: build stamp tooltip), one button per
//  registered panel except the ones the RynthAi launcher opens (PanelIcons;
//  an unknown panel shows its first letter), reload (RL) and pop out.
//  Dragging the grip moves the bar; the position is the shared bar= row in
//  panel_state.txt. Right-click a panel button (or the grip for the bar
//  itself) to choose which face shows it docked. Pop out moves the ImGui bar
//  into its own window (ImGuiPopOuts, sized to its content; dock back
//  returns it); a bar whose face is Avalonia/Both still pops out as the
//  Avalonia bar.
//
//  Face key "__bar" in panel_faces.txt; code default Avalonia. While the ImGui
//  bar shows, the docked Avalonia bar is hidden and takes no clicks
//  (AvaloniaOverlay.DockedBarSuppressed). AC thread except where noted.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using ImGuiNET;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.ScriptWindows;

namespace RynthCore.Engine.ImGuiBackend;

internal static class ImGuiBar
{
    public const string FaceKey = "__bar";

    // Opened from the RynthAi launcher, not the bar (AvaloniaOverlay.BuildRoot). Lua has
    // its own bar button since it became the RynthLua plugin. "Monsters" (retired 2026-10-01,
    // still registered by EntryPoint) must never get a button: PanelRouter sends it to Damage.
    private static readonly HashSet<string> LauncherOnly = new(StringComparer.OrdinalIgnoreCase)
        { "Monsters", "Damage", "Settings", "Nav", "Meta", "Items" };

    private static readonly PanelFace[] Faces = { PanelFace.Avalonia, PanelFace.ImGui, PanelFace.Both };
    private static readonly string[] FaceNames = { "Avalonia", "ImGui", "Both (side by side)" };

    private sealed record ButtonInfo(string Title, string Id, string PopupId, string Icon, bool Letter, string Tooltip,
        bool ImGuiOnly = false);

    // Engine panels with an ImGui face only (no OverlayHost entry) that get a bar button.
    private static readonly string[] ImGuiOnlyBarPanels = { Panels.InventoryFace.Title, Panels.SkillsFace.Title, Panels.SenseFace.Title };

    // Known panels' icons (DrakBot's where the same window exists); others show their first letter.
    private static readonly Dictionary<string, string> PanelIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RynthAi"] = PhosphorIcons.Robot,
        ["Chat"] = PhosphorIcons.ChatCircleText,
        ["ChatFilters"] = PhosphorIcons.Funnel,
        ["Radar"] = PhosphorIcons.Compass,
        ["Vision"] = PhosphorIcons.Eye,
        ["Nav"] = PhosphorIcons.MapTrifold,
        ["RynthNav"] = PhosphorIcons.NavigationArrow,
        ["Tracker"] = PhosphorIcons.ChartLineUp,
        ["Lua"] = PhosphorIcons.FileCode,
        ["Settings"] = PhosphorIcons.Gear,
        ["Damage"] = PhosphorIcons.ChartBar,
        ["Meta"] = PhosphorIcons.ListChecks,
        ["Items"] = PhosphorIcons.Backpack,
        ["Loot Editor"] = PhosphorIcons.TreasureChest,
        ["Skills"] = PhosphorIcons.GraduationCap,
        ["Sense"] = PhosphorIcons.Binoculars,
        ["Status"] = PhosphorIcons.Info,
        ["Inventory"] = PhosphorIcons.Package,   // not Backpack: the dashboard's Items launcher has that
        ["Log"] = PhosphorIcons.TerminalWindow,
    };

    /// <summary>A panel's bar icon: its Phosphor icon, else its first letter (letter = true).</summary>
    internal static string IconFor(string title, out bool letter)
    {
        letter = !PanelIcons.TryGetValue(title, out string? icon);
        if (!letter) return icon!;
        foreach (char c in title)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    // Square buttons, logical px (x the DPI scale).
    private const float ButtonSize = 20f, ButtonGap = 3f;

    private static ButtonInfo[]? _buttons;
    private static string _tooltip = "";
    private static bool _placed;
    private static Vector2 _pos, _persistedPos;

    private const string PopOutTitle = "__bar";
    // Popped out (the ImGui bar in its own window). Read from panel_state once,
    // then kept here so a switch shows at once (the file is written in the background).
    private static bool? _floating;
    private static bool Floating => _floating ??= PanelStateStore.BarFloating;

    /// <summary>True when the ImGui bar is popped out into its own window.</summary>
    public static bool PoppedOut => ResolveFace() == PanelFace.ImGui && Floating;

    /// <summary>The bar's docked face. Any thread.</summary>
    public static PanelFace ResolveFace()
    {
        if (!Plugins.EngineSettings.EnableImGuiBackend) return PanelFace.Avalonia;
        if (!Plugins.EngineSettings.EnableAvaloniaOverlay) return PanelFace.ImGui;
        return PanelFaceStore.TryGet(FaceKey, out PanelFace f) ? f : PanelFace.ImGui;
    }

    /// <summary>
    /// Shows/hides the docked Avalonia bar to match the bar's face. Call at
    /// init and whenever the face or the ImGui layer changes. Any thread.
    /// </summary>
    public static void ApplyFace()
    {
        AvaloniaOverlay.SetDockedBarSuppressed(ResolveFace() == PanelFace.ImGui);
        _placed = false; // re-read the shared position next frame
    }

    /// <summary>True when the ImGui bar should draw (docked face ImGui/Both, not popped out).</summary>
    public static bool Visible => ResolveFace() != PanelFace.Avalonia && !Floating;

    /// <summary>
    /// Opens or closes the bar's own window to match <see cref="PoppedOut"/>. AC
    /// thread, in the world, with the main context current (after the panels).
    /// </summary>
    public static void SyncPopOut(float s)
    {
        bool have = ImGuiPopOuts.IsPopped(PopOutTitle);
        if (PoppedOut && !have)
        {
            EnsureButtons();
            var saved = PanelStateStore.BarFloatingPosition;
            Vector2 at = saved.HasValue
                ? new Vector2((float)saved.Value.Left, (float)saved.Value.Top)
                : ImGuiPopOuts.ClientOriginOnScreen() + _pos;
            ImGuiPopOuts.Open(PopOutTitle, (int)at.X, (int)at.Y, (int)(400 * s), (int)(28 * s), 0, 0, s,
                draw: DrawPopped,
                onMoved: static (x, y) => UiBackgroundWriter.Enqueue("persist bar", () => PanelStateStore.SetBarFloatingState(true, x, y)),
                sizeToContent: true);
        }
        else if (!PoppedOut && have)
        {
            ImGuiPopOuts.Close(PopOutTitle);
        }
    }

    /// <summary>
    /// Pops the ImGui bar out into its own window (at the docked bar's screen
    /// spot) or docks it back where it was docked. The window opens/closes on
    /// the next frame (SyncPopOut). Any thread.
    /// </summary>
    public static void SetPoppedOut(bool popped)
    {
        if (popped == Floating) return;
        _floating = popped;
        if (popped)
        {
            Vector2 screen = ImGuiPopOuts.ClientOriginOnScreen() + _pos;
            UiBackgroundWriter.Enqueue("pop out bar", () => PanelStateStore.SetBarFloatingState(true, screen.X, screen.Y));
        }
        else
        {
            _placed = false; // back where it was docked (bar= row); the popped-out spot is kept
            var at = PanelStateStore.BarFloatingPosition ?? (0, 0);
            UiBackgroundWriter.Enqueue("dock bar", () => PanelStateStore.SetBarFloatingState(false, at.Item1, at.Item2));
        }
    }

    /// <summary>The bar in its own window: the same content at the pop-out's panel origin, sized to fit. AC thread.</summary>
    private static void DrawPopped(float s, Vector2 display)
    {
        EnsureButtons();
        float place = AvaloniaOverlay.InputScale > 0 ? AvaloniaOverlay.InputScale : 1f;
        ImGuiNET.ImGui.SetNextWindowPos(ImGuiPopOuts.PanelOrigin, ImGuiCond.Always);
        PushBarWindowStyle(s);
        ImGuiNET.ImGui.Begin("##rc_bar", BarFlags);
        try
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui11));
            DrawContent(s, place, popped: true);
            ImGuiNET.ImGui.PopFont();
            // Not on its first frame: an auto-resizing window has no content size yet
            // and reports just its padding (8x6), which would shrink the window to it.
            if (!ImGuiNET.ImGui.IsWindowAppearing())
                ImGuiPopOuts.ReportContentSize(ImGuiNET.ImGui.GetWindowSize());
        }
        finally
        {
            ImGuiNET.ImGui.End();
            PopBarWindowStyle();
        }
    }

    private const ImGuiWindowFlags BarFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;

    private static void PushBarWindowStyle(float s)
    {
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 3) * s);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonGap, ButtonGap) * s);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, RynthTheme.BarRounding);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.WindowBg, RynthTheme.BarBackground);
    }

    private static void PopBarWindowStyle()
    {
        ImGuiNET.ImGui.PopStyleColor();
        ImGuiNET.ImGui.PopStyleVar(5);
    }

    public static void Draw(float s)
    {
        EnsureButtons();
        float place = AvaloniaOverlay.InputScale > 0 ? AvaloniaOverlay.InputScale : 1f;
        bool both = ResolveFace() == PanelFace.Both;

        if (!_placed)
        {
            var saved = PanelStateStore.BarPosition;
            Vector2 logical = saved.HasValue ? new Vector2((float)saved.Value.Left, (float)saved.Value.Top) : new Vector2(50, 5);
            if (both) logical += new Vector2(24, 24);
            _pos = _persistedPos = logical * place;
            ImGuiNET.ImGui.SetNextWindowPos(_pos, ImGuiCond.Always);
            _placed = true;
        }

        PushBarWindowStyle(s);
        ImGuiNET.ImGui.Begin("##rc_bar", BarFlags);
        try
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui11));
            DrawContent(s, place, popped: false);
            ImGuiNET.ImGui.PopFont();
        }
        finally
        {
            _pos = ImGuiNET.ImGui.GetWindowPos();
            ImGuiNET.ImGui.End();
            PopBarWindowStyle();
        }

        if (!ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Left) && _pos != _persistedPos)
        {
            _persistedPos = _pos;
            Vector2 logical = _pos / place - (both ? new Vector2(24, 24) : Vector2.Zero);
            double left = logical.X, top = logical.Y;
            UiBackgroundWriter.Enqueue("persist bar", () => PanelStateStore.SetBarPosition(left, top));
            if (!both) AvaloniaOverlay.SyncDockedBarPosition(left, top);
        }
    }

    private static void DrawContent(float s, float place, bool popped)
    {
        var square = new Vector2(ButtonSize, ButtonSize) * s;
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImFontPtr iconFont = ImGuiFonts.Get(UiFont.Ui14);

        // Grip (the RC label's place, in its light green): the bar's drag handle
        // (the Avalonia bar drags from its left edge, over the same spot).
        Vector2 rcPos = ImGuiNET.ImGui.GetCursorScreenPos();
        ImGuiNET.ImGui.InvisibleButton("##bardrag", new Vector2(square.X * 0.8f, square.Y));
        if (popped)
        {
            // Popped out, the window itself moves (it follows the cursor from here on).
            if (ImGuiNET.ImGui.IsItemActive() && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f))
                ImGuiPopOuts.BeginMoveCurrent();
        }
        else if (ImGuiNET.ImGui.IsItemActive() && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
        {
            Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
            Vector2 size = ImGuiNET.ImGui.GetWindowSize();
            Vector2 next = ImGuiNET.ImGui.GetWindowPos() + ImGuiNET.ImGui.GetIO().MouseDelta;
            if (display.X > 1)
                next = Vector2.Clamp(next, Vector2.Zero, Vector2.Max(Vector2.Zero, display - size));
            ImGuiNET.ImGui.SetWindowPos(next);
        }
        ImGuiNET.ImGui.SetItemTooltip(_tooltip);
        if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGuiNET.ImGui.OpenPopup("##face_bar");
        PhosphorIcons.DrawCentered(dl, iconFont, PhosphorIcons.DotsSixVertical, rcPos, new Vector2(square.X * 0.8f, square.Y), RynthTheme.RcLabel);
        FacePopup("##face_bar", FaceKey, hasImGuiFace: true);

        // Collapse: the bar folds to its grip and this caret.
        bool collapsed = RynthAiDashboardState.BarCollapsed;
        ImGuiNET.ImGui.SameLine(0, 0);
        Vector2 cPos = ImGuiNET.ImGui.GetCursorScreenPos();
        var caretSize = new Vector2(square.X * 0.6f, square.Y);
        if (ImGuiNET.ImGui.InvisibleButton("##barcollapse", caretSize))
            RynthAiDashboardState.SetBarCollapsed(!collapsed);
        bool caretHot = ImGuiNET.ImGui.IsItemHovered();
        ImGuiNET.ImGui.SetItemTooltip(collapsed ? "Show the bar" : "Fold the bar to this button");
        PhosphorIcons.DrawCentered(dl, iconFont, collapsed ? PhosphorIcons.CaretRight : PhosphorIcons.CaretLeft, cPos, caretSize,
            caretHot ? RynthTheme.RcLabel : 0xFF9A9A9Au);
        if (collapsed) return;

        PushBarButtonStyle(s);
        ImFontPtr letterFont = ImGuiFonts.Get(UiFont.UiBold11);
        foreach (ButtonInfo b in _buttons!)
        {
            ImGuiNET.ImGui.SameLine();
            if (IconButton(b.Id, b.Letter ? letterFont : iconFont, b.Icon, square, RynthTheme.BarText))
                PanelRouter.Toggle(b.Title);
            ImGuiNET.ImGui.SetItemTooltip(b.Tooltip);
            if (b.ImGuiOnly) continue;   // no Avalonia face: no face to choose
            if (ImGuiNET.ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGuiNET.ImGui.OpenPopup(b.PopupId);
            FacePopup(b.PopupId, b.Title, ImGuiPanelHost.HasFace(b.Title));
        }

        // Script windows (decision 2): one button with a menu, only while a ShowInBar
        // script window exists. They never get buttons of their own (EnsureButtons
        // lists OverlayHost's engine panels only; script windows are dynamic entries).
        ScriptBarItem[] scripts = ScriptWindowRegistry.BarItems;
        if (scripts.Length > 0)
        {
            ImGuiNET.ImGui.SameLine();
            if (IconButton("##scripts", iconFont, PhosphorIcons.AppWindow, square, RynthTheme.BarText))
                ImGuiNET.ImGui.OpenPopup("##scripts_menu");
            ImGuiNET.ImGui.SetItemTooltip("Script windows");
        }

        ImGuiNET.ImGui.SameLine(0, 6 * s);
        if (IconButton("##reload", iconFont, PhosphorIcons.ArrowsClockwise, square, RynthTheme.ReloadLabel))
            ThreadPool.UnsafeQueueUserWorkItem(static _ => EngineLifecycle.SignalReload(), null);
        ImGuiNET.ImGui.SetItemTooltip("Engine reload (RL) — shut down, FreeLibrary, reload from disk, re-init.");

        if (ResolveFace() == PanelFace.ImGui)
        {
            ImGuiNET.ImGui.SameLine();
            if (IconButton("##popout", iconFont, popped ? PhosphorIcons.ArrowSquareIn : PhosphorIcons.ArrowSquareOut, square, RynthTheme.PopOutLabel))
                SetPoppedOut(!popped);
            ImGuiNET.ImGui.SetItemTooltip(popped ? "Dock the bar back into the game window." : "Pop the bar out into its own floating window.");
        }
        else if (Plugins.EngineSettings.EnableAvaloniaOverlay)   // Avalonia/Both bar: pops out as the Avalonia bar
        {
            ImGuiNET.ImGui.SameLine();
            if (IconButton("##popout", iconFont, PhosphorIcons.ArrowSquareOut, square, RynthTheme.PopOutLabel))
            {
                Vector2 logical = ImGuiNET.ImGui.GetWindowPos() / place;
                AvaloniaOverlay.PopOutBarFrom(logical.X, logical.Y);
            }
            ImGuiNET.ImGui.SetItemTooltip("Pop the bar out into its own floating window.");
        }
        PopBarButtonStyle();

        // Drawn after the bar buttons' style is popped, so the menu has the normal look.
        ScriptsMenu(scripts);
    }

    /// <summary>
    /// The Scripts button's popup: the ShowInBar script windows grouped by script, a
    /// checkbox item each for open. Toggling tells the owner (Visibility, reason 2).
    /// </summary>
    private static void ScriptsMenu(ScriptBarItem[] items)
    {
        if (!ImGuiNET.ImGui.BeginPopup("##scripts_menu")) return;
        try
        {
            if (items.Length == 0) ImGuiNET.ImGui.CloseCurrentPopup();
            uint disabled = ImGuiNET.ImGui.GetColorU32(ImGuiCol.TextDisabled);
            foreach (ScriptBarItem item in items)
            {
                if (item.GroupStart)
                {
                    // A script's own text: never a format string.
                    ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, disabled);
                    ImGuiNET.ImGui.TextUnformatted(item.Group);
                    ImGuiNET.ImGui.PopStyleColor();
                }
                bool open = ImGuiPanelHost.IsOpen(item.PanelKey);
                if (ImGuiNET.ImGui.MenuItem(item.MenuLabel, "", open))
                    ScriptWindowRegistry.PlayerSetVisible(item.Window, !open);
            }
        }
        finally
        {
            ImGuiNET.ImGui.EndPopup();
        }
    }

    /// <summary>A square bar button (the bar's button style) with an icon, or a letter, centred on it.</summary>
    private static bool IconButton(string id, ImFontPtr font, string icon, Vector2 size, uint color)
    {
        Vector2 at = ImGuiNET.ImGui.GetCursorScreenPos();
        bool clicked = ImGuiNET.ImGui.Button(id, size);
        PhosphorIcons.DrawCentered(ImGuiNET.ImGui.GetWindowDrawList(), font, icon, at, size, color);
        return clicked;
    }

    private static void FacePopup(string popupId, string title, bool hasImGuiFace)
    {
        if (!ImGuiNET.ImGui.BeginPopup(popupId)) return;
        ImGuiNET.ImGui.TextDisabled(title == FaceKey ? "Bar, docked, shows as" : "Docked, shows as");
        PanelFace current = title == FaceKey ? ResolveFace() : PanelRouter.ResolveDockedFace(title);
        for (int i = 0; i < Faces.Length; i++)
        {
            bool enabled = Faces[i] == PanelFace.Avalonia || hasImGuiFace;
            if (ImGuiNET.ImGui.MenuItem(FaceNames[i], "", current == Faces[i], enabled))
                PanelRouter.SetDockedFace(title, Faces[i]);
        }
        if (!hasImGuiFace)
            ImGuiNET.ImGui.TextDisabled("(no ImGui version yet)");
        ImGuiNET.ImGui.EndPopup();
    }

    private static void PushBarButtonStyle(float s)
    {
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Button, RynthTheme.BarButton);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonHovered, RynthTheme.Argb(0xFF2A3850));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonActive, RynthTheme.Argb(0xFF34466A));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, RynthTheme.BarButtonBorder);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, RynthTheme.BarText);
    }

    private static void PopBarButtonStyle()
    {
        ImGuiNET.ImGui.PopStyleColor(5);
        ImGuiNET.ImGui.PopStyleVar(3);
    }

    private static void EnsureButtons()
    {
        if (_buttons != null) return;
        var list = new List<ButtonInfo>();
        foreach (var (title, _) in OverlayHost.GetPanels())
            if (!LauncherOnly.Contains(title))
            {
                string icon = IconFor(title, out bool letter);
                list.Add(new ButtonInfo(title, "##bar_" + title, "##face_" + title, icon, letter,
                    title + "\nRight-click: which face shows it docked."));
            }
        foreach (string title in ImGuiOnlyBarPanels)
            if (ImGuiPanelHost.HasFace(title) && !LauncherOnly.Contains(title))
            {
                string icon = IconFor(title, out bool letter);
                list.Add(new ButtonInfo(title, "##bar_" + title, "##face_" + title, icon, letter, title, ImGuiOnly: true));
            }
        _buttons = list.ToArray();
        _tooltip = $"RynthCore {EntryPoint.BuildStamp}\nDrag here to move the bar. Right-click: bar face.";
    }
}
