// ============================================================================
//  RynthCore.Engine - ImGui/RynthTheme.cs
//  Colours and metrics for the ImGui panel faces, taken from the Avalonia
//  panels so a docked ImGui face looks like its popped-out Avalonia face
//  (docs/IMGUI_PARITY_PLAN.md §4.4). Tokens are ABGR-packed ImGui colours
//  (ImGui.GetColorU32 order) built from the Avalonia #AARRGGBB literals.
//
//  Apply() sets the base style once at context creation; per-window colours
//  (bar, RynthAi, Radar) are pushed around Begin by the panel host.
// ============================================================================

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace RynthCore.Engine.ImGuiBackend;

internal static class RynthTheme
{
    /// <summary>Avalonia #AARRGGBB → ImGui packed colour (0xAABBGGRR).</summary>
    public static uint Argb(uint argb) =>
        (argb & 0xFF00FF00) | ((argb & 0x00FF0000) >> 16) | ((argb & 0x000000FF) << 16);

    public static Vector4 Vec(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, (argb >> 24) / 255f);

    // ── Bar (AvaloniaOverlay.cs BuildRoot) ─────────────────────────────
    public static readonly uint BarBackground = Argb(0xE60A0A14);
    public const float BarRounding = 4f;
    public static readonly uint BarButton = Argb(0xFF1A2230);
    public static readonly uint BarButtonBorder = Argb(0xFF3A4A5A);
    public static readonly uint BarText = Argb(0xFFFFFFFF);
    public static readonly uint RcLabel = Argb(0xFF90EE90);      // LightGreen
    public static readonly uint ReloadLabel = Argb(0xFFF0E68C);  // Khaki
    public static readonly uint PopOutLabel = Argb(0xFF87CEFA);  // LightSkyBlue

    // ── Standard panel frame ───────────────────────────────────────────
    public static readonly uint PanelGlass = Argb(0xF212121C);   // was D2: the game showed through behind text
    public static readonly uint PanelBorder = Argb(0xFF008080);  // Teal
    public const float PanelRounding = 4f;
    public static readonly uint PanelHeader = Argb(0xFF1A1A28);
    public static readonly uint DragLabel = Argb(0xFF008080);
    public const float HeaderHeight = 24f;
    public static readonly uint GripIdle = Argb(0x2026C1A6);
    public static readonly uint GripHover = Argb(0x5526C1A6);
    public static readonly uint GripGlyph = Argb(0xFF008080);

    // ── RynthAi dashboard ──────────────────────────────────────────────
    public static readonly uint RynthAiBackground = Argb(0xF20A0F14);
    public static readonly uint RynthAiBorder = Argb(0xFF264059);
    public const float RynthAiRounding = 3f;

    // ── Radar ──────────────────────────────────────────────────────────
    public static readonly uint RadarGold = Argb(0xFFE6C766);
    public static readonly uint RadarGripIdle = Argb(0x400A121A);
    public static readonly uint RadarGripHover = Argb(0xA00A121A);

    /// <summary>
    /// Base style for every ImGui face, scaled for <paramref name="scale"/>
    /// (DPI / 96). AC thread, once, right after the context is created.
    /// </summary>
    public static void Apply(ImGuiStylePtr style, float scale)
    {
        ImGuiNET.ImGui.StyleColorsDark();

        style.WindowRounding = PanelRounding;
        style.ChildRounding = 3f;
        style.FrameRounding = 2f;
        style.PopupRounding = 3f;
        style.GrabRounding = 2f;
        style.ScrollbarRounding = 3f;
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.WindowPadding = new Vector2(6f, 6f);
        style.FramePadding = new Vector2(5f, 2f);
        style.ItemSpacing = new Vector2(6f, 4f);
        style.ScrollbarSize = 12f;
        style.WindowMinSize = new Vector2(20f, 20f);

        Set(style, ImGuiCol.Text, 0xFFE6E6E6);
        Set(style, ImGuiCol.TextDisabled, 0xFF808080);
        Set(style, ImGuiCol.WindowBg, 0xD212121C);
        Set(style, ImGuiCol.ChildBg, 0x00000000);
        Set(style, ImGuiCol.PopupBg, 0xF214141E);
        Set(style, ImGuiCol.Border, 0xFF008080);
        Set(style, ImGuiCol.FrameBg, 0xFF1A2230);
        Set(style, ImGuiCol.FrameBgHovered, 0xFF243044);
        Set(style, ImGuiCol.FrameBgActive, 0xFF2C3A52);
        Set(style, ImGuiCol.TitleBg, 0xFF1A1A28);
        Set(style, ImGuiCol.TitleBgActive, 0xFF1A1A28);
        Set(style, ImGuiCol.Button, 0xFF1A2230);
        Set(style, ImGuiCol.ButtonHovered, 0xFF2A3850);
        Set(style, ImGuiCol.ButtonActive, 0xFF34466A);
        Set(style, ImGuiCol.Header, 0xFF1A2230);
        Set(style, ImGuiCol.HeaderHovered, 0xFF2A3850);
        Set(style, ImGuiCol.HeaderActive, 0xFF34466A);
        Set(style, ImGuiCol.CheckMark, 0xFF26C1A6);
        Set(style, ImGuiCol.SliderGrab, 0xFF26A18C);
        Set(style, ImGuiCol.SliderGrabActive, 0xFF26C1A6);
        Set(style, ImGuiCol.Separator, 0x503A4A5A);
        Set(style, ImGuiCol.ResizeGrip, 0x2026C1A6);
        Set(style, ImGuiCol.ResizeGripHovered, 0x5526C1A6);
        Set(style, ImGuiCol.ResizeGripActive, 0x8026C1A6);
        Set(style, ImGuiCol.ScrollbarBg, 0x40000000);
        Set(style, ImGuiCol.ScrollbarGrab, 0xFF2A3850);
        Set(style, ImGuiCol.TableHeaderBg, 0xFF1A1A28);
        Set(style, ImGuiCol.TableBorderStrong, 0xFF3A4A5A);
        Set(style, ImGuiCol.TableBorderLight, 0x503A4A5A);
        Set(style, ImGuiCol.TableRowBgAlt, 0x10FFFFFF);
        Set(style, ImGuiCol.ModalWindowDimBg, 0x00000000);

        if (scale != 1f)
            style.ScaleAllSizes(scale);
    }

    private static void Set(ImGuiStylePtr style, ImGuiCol col, uint argb) => style.Colors[(int)col] = Vec(argb);
}

/// <summary>DPI scale for the ImGui faces.</summary>
internal static class UiScale
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// GetDpiForWindow(game) / 96, at least 1. For a DPI-unaware acclient this
    /// is 1 (Windows scales the whole window); ImGui then works in the same
    /// client pixels AC does.
    /// </summary>
    public static float ForWindow(IntPtr hwnd)
    {
        try
        {
            uint dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 0;
            return dpi > 0 ? Math.Max(1f, dpi / 96f) : 1f;
        }
        catch (EntryPointNotFoundException)
        {
            return 1f; // pre-1607 Windows 10
        }
    }
}
