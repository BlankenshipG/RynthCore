// ============================================================================
//  RynthCore.Engine - ImGui/Panels/SettingsDrawerBodies.cs
//  The dashboard drawers that show Settings rows, drawn into RynthAi's own
//  dashboard window (2026-10-21, plugin API v90 RenderSettingsDrawer). RynthAi
//  draws its dashboard itself now, its drawers' tabs and frames included, but
//  three drawers show the Settings panel's rows, and the Settings panel is still
//  RynthCore's: until it moves to RynthAi, RynthAi's drawer windows ask for
//  these bodies here (the frame and the title row are RynthAi's):
//    ranges   the selected monster and the key ranges (RangesSlideOut.cs)
//    patrol   Start patrol, the Dungeon Patrol settings, this dungeon's hazard
//             cells, the recorded hazards and the saved routes (the Patrol
//             drawer, RynthAiFace.Drawers.cs)
//    pinned   My settings, the rows the player pinned (QuickSettingsDrawer.cs)
//  Each is the same drawer the engine's own dashboard shows, so a value changed
//  here is saved the same way and a pin made here is the same pin. A drawer
//  that wasn't drawn in a frame drops its hub subscriptions (EndFrame).
//
//  AC thread only (inside RynthAi's window, and EndFrame after the panels).
// ============================================================================

using System;
using System.Numerics;
using RynthCore.Engine.UI;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal static class SettingsDrawerBodies
{
    private static RangesSlideOut? _ranges;
    private static RynthAiFace.PatrolDrawer? _patrol;
    private static QuickSettingsDrawer? _pinned;

    // The frame each was last drawn in (EndFrame counts frames).
    private static long _frame, _rangesDrawn = -1, _patrolDrawn = -1, _pinnedDrawn = -1;

    /// <summary>
    /// Draws <paramref name="key"/>'s body at the cursor, <paramref name="width"/> pixels wide, in at most
    /// <paramref name="room"/> pixels of height (0: as tall as it wants); <paramref name="openPicker"/>:
    /// open My settings' list of settings to pin. Returns the height it wants (pixels), or -1 for a key
    /// that isn't one of these.
    /// </summary>
    public static float Draw(string key, float width, float room, bool openPicker)
    {
        Vector2 at = ImGuiNET.ImGui.GetCursorScreenPos();
        float w = MathF.Max(1, width);
        float k = MathF.Max(1f, ImGuiFonts.Get(UiFont.Dash11).FontSize / 11f);
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash10));
        try
        {
            switch (key)
            {
                case "ranges":
                {
                    _ranges ??= new RangesSlideOut();
                    _rangesDrawn = _frame;
                    _ranges.Update(true);
                    float h = RangesSlideOut.BodyHeight(k);
                    ImGuiNET.ImGui.PushID("##sdb_ranges");
                    try { _ranges.DrawBody(at.X, at.Y, w, k); }
                    finally { ImGuiNET.ImGui.PopID(); }
                    ImGuiNET.ImGui.SetCursorScreenPos(at);
                    ImGuiNET.ImGui.Dummy(new Vector2(w, h));
                    return h;
                }
                case "patrol":
                {
                    _patrol ??= new RynthAiFace.PatrolDrawer();
                    _patrolDrawn = _frame;
                    _patrol.Update(true);
                    RynthAiFace.UseScale(k);
                    float h = room > 0 ? room : 400 * k;
                    float want;
                    ImGuiNET.ImGui.PushID("##sdb_patrol");
                    try { want = _patrol.DrawBody(at.X, at.Y, w, h, k); }
                    finally { ImGuiNET.ImGui.PopID(); }
                    ImGuiNET.ImGui.SetCursorScreenPos(at);
                    ImGuiNET.ImGui.Dummy(new Vector2(w, h));
                    return want;
                }
                case "pinned":
                {
                    _pinned ??= new QuickSettingsDrawer();
                    _pinnedDrawn = _frame;
                    _pinned.Update(true);
                    float h = room > 0 ? room : 120 * k;
                    float want;
                    ImGuiNET.ImGui.PushID("##sdb_pinned");
                    try { want = _pinned.DrawBody(at.X, at.Y, w, h, k, openPicker); }
                    finally { ImGuiNET.ImGui.PopID(); }
                    ImGuiNET.ImGui.SetCursorScreenPos(at);
                    ImGuiNET.ImGui.Dummy(new Vector2(w, h));
                    return want;
                }
                default:
                    return -1f;
            }
        }
        finally
        {
            ImGuiNET.ImGui.PopFont();
        }
    }

    /// <summary>After the panels each frame: a body that wasn't drawn this frame lets its hub sources go.</summary>
    public static void EndFrame()
    {
        if (_ranges != null && _rangesDrawn != _frame) _ranges.Update(false);
        if (_patrol != null && _patrolDrawn != _frame) _patrol.Update(false);
        if (_pinned != null && _pinnedDrawn != _frame) _pinned.Update(false);
        _frame++;
    }
}
