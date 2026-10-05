// ============================================================================
//  RynthCore.Engine - ImGui/Panels/DashboardDrawers.cs
//  The RynthAi dashboard's slide-out drawers (Tom 2026-10-05: every drawer on
//  the dashboard's left edge; only the vitals / target bars stay at its
//  bottom). One tab per drawer, stacked down the left edge; a click slides
//  that drawer's panel out to the left (to the right when the dashboard sits
//  too close to the screen's left edge for the widest drawer). One drawer is
//  open at a time: opening one closes the other, so the panels never stack
//  and the tabs never jump.
//
//  The mechanics live here (tabs, slide, side, placement, the one window);
//  each drawer only draws its panel (DashboardDrawer.DrawPanel) and says how
//  big it is. Drawers today: Ranges (RangesSlideOut.cs), Loaded files and
//  Patrol (RynthAiFace.Drawers.cs).
//
//  A top-level ImGui window of its own, begun from inside the dashboard's Draw
//  and placed against the dashboard's window every frame, so it follows the
//  dashboard (and goes away with it). Each drawer's open/closed is saved with
//  the dashboard's state (RynthAiDashboardState). Popped out the tabs are
//  hidden: a popped-out dashboard is its own small window and the area around
//  it lets clicks through to the game, so a drawer beside it couldn't be used
//  (the dashboard falls back to its inline files block and patrol popup).
//
//  AC thread only (inside the dashboard's Draw).
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;

namespace RynthCore.Engine.ImGuiBackend.Panels;

/// <summary>One drawer on the dashboard's left edge: its tab's look and its panel.</summary>
internal abstract class DashboardDrawer
{
    /// <summary>Set by the host: close this drawer from inside the panel (its close button).</summary>
    public DashboardDrawers? Host { get; set; }

    /// <summary>Unique among the drawers ("##drawer_tab_" + Key is the tab's id).</summary>
    public abstract string Key { get; }
    /// <summary>Phosphor glyph on the tab.</summary>
    public abstract string Icon { get; }
    /// <summary>Lower-case name for "Hide the …" tooltips.</summary>
    public abstract string Name { get; }
    /// <summary>The closed tab's tooltip; <paramref name="right"/>: it slides out to the right.</summary>
    public abstract string TabTooltip(bool right);

    /// <summary>Panel size in pixels at text scale <paramref name="k"/>.</summary>
    public abstract float Width(float k);
    public abstract float Height(float k);

    /// <summary>Every frame before drawing: <paramref name="showing"/> while any of the panel is out (subscriptions, data).</summary>
    public virtual void Update(bool showing) { }

    /// <summary>Draws the whole panel at <paramref name="origin"/> (the host clips it to the part slid out).</summary>
    public abstract void DrawPanel(Vector2 origin, float width, float height, float k);

    /// <summary>The saved open/closed (RynthAiDashboardState).</summary>
    public abstract bool SavedOpen { get; }
    public abstract void SaveOpen(bool open);

    private string? _tabId, _hideTip;
    /// <summary>The tab's ImGui id and open tooltip (built once).</summary>
    internal string TabId => _tabId ??= "##drawer_tab_" + Key;
    internal string HideTip => _hideTip ??= "Hide the " + Name + ".";

    protected void Close() => Host?.Close(this);

    protected static float CalcWidth(ImFontPtr font, string text)
    {
        ImGuiNET.ImGui.PushFont(font);
        float w = ImGuiNET.ImGui.CalcTextSize(text).X;
        ImGuiNET.ImGui.PopFont();
        return w;
    }

    // ── The shared panel frame: background, title row (icon + TITLE + note | close) ──

    protected static readonly uint DTeal = RynthTheme.Argb(0xFF26D9E6), DMute = RynthTheme.Argb(0xFFB8C8D8),
        DWhite = RynthTheme.Argb(0xFFF2F7FC), DBtnFill = RynthTheme.Argb(0xFF16283A), DBtnBord = RynthTheme.Argb(0xFF34587A);
    protected const float Pad = 6, TitleH = 18;

    /// <summary>Paints the panel's background and title row; returns the y under the title (pixels).</summary>
    protected float PanelFrame(Vector2 origin, float panelW, float panelH, float k, string icon, string title, string? note, string closeTip)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(panelW, panelH), RynthTheme.RynthAiBackground, RynthTheme.RynthAiRounding);
        dl.AddRect(origin, origin + new Vector2(panelW, panelH), RynthTheme.RynthAiBorder, RynthTheme.RynthAiRounding);

        float x = origin.X + Pad * k, w = panelW - 2 * Pad * k, y = origin.Y + Pad * k;
        ImFontPtr f11 = ImGuiFonts.Get(UiFont.Dash11), f9 = ImGuiFonts.Get(UiFont.Dash9);
        float th = TitleH * k;
        float iconW = CalcWidth(f11, icon);
        dl.AddText(f11, f11.FontSize, new Vector2(x, y + (th - f11.FontSize) * 0.5f), DTeal, icon);
        dl.AddText(f11, f11.FontSize, new Vector2(x + iconW + 4 * k, y + (th - f11.FontSize) * 0.5f), DWhite, title);
        if (note != null)
        {
            float tw = CalcWidth(f11, title);
            dl.AddText(f9, f9.FontSize, new Vector2(x + iconW + 4 * k + tw + 4 * k, y + (th - f9.FontSize) * 0.5f + 1), DMute, note);
        }
        Vector2 closeSize = new(18 * k, 16 * k);
        if (FaceKit.IconButton("##drawer_close", PhosphorIcons.X, new Vector2(x + w - closeSize.X, y + (th - closeSize.Y) * 0.5f),
                closeSize, DMute, DBtnFill, border: DBtnBord, font: UiFont.Dash10))
            Close();
        ImGuiNET.ImGui.SetItemTooltip(closeTip);
        return y + th + 4 * k;
    }
}

/// <summary>The tabs and the one open drawer beside the RynthAi dashboard.</summary>
internal sealed class DashboardDrawers
{
    private const string WindowId = "##rynthai_drawers";
    private const float SlideSeconds = 0.14f;

    private static readonly uint Teal = RynthTheme.Argb(0xFF26D9E6), Mute = RynthTheme.Argb(0xFFB8C8D8),
        White = RynthTheme.Argb(0xFFF2F7FC), BtnFill = RynthTheme.Argb(0xFF16283A), BtnBord = RynthTheme.Argb(0xFF34587A);

    private readonly DashboardDrawer[] _drawers;
    private int _open = -1;      // the drawer open (or opening); -1 none
    private int _shown = -1;     // the drawer whose panel draws (a closing one until it has slid in)
    private float _t;            // slide position of _shown, 0 closed .. 1 open
    private bool _loaded;

    public DashboardDrawers(params DashboardDrawer[] drawers)
    {
        _drawers = drawers;
        foreach (DashboardDrawer d in drawers) d.Host = this;
    }

    public bool IsOpen(DashboardDrawer d) => _open >= 0 && _drawers[_open] == d;

    /// <summary>Opens <paramref name="d"/> (closing the open one), or closes it when it is the open one.</summary>
    public void Toggle(DashboardDrawer d)
    {
        if (IsOpen(d)) Close(d);
        else Open(d);
    }

    public void Open(DashboardDrawer d)
    {
        EnsureLoaded();
        int i = Array.IndexOf(_drawers, d);
        if (i < 0 || i == _open) return;
        if (_open >= 0) _drawers[_open].SaveOpen(false);
        _open = i;
        d.SaveOpen(true);
        if (_shown != i)
        {
            // Another drawer was out (or sliding in): this one slides out from closed.
            _shown = i;
            _t = 0f;
        }
    }

    public void Close(DashboardDrawer d)
    {
        if (!IsOpen(d)) return;
        _open = -1;
        d.SaveOpen(false);
    }

    /// <summary>The dashboard closed: every drawer drops its subscriptions. AC thread.</summary>
    public void Detach()
    {
        foreach (DashboardDrawer d in _drawers) d.Update(false);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        // The first drawer saved open wins (one at a time); any other saved open is closed.
        for (int i = 0; i < _drawers.Length; i++)
        {
            if (!_drawers[i].SavedOpen) continue;
            if (_open < 0) _open = i;
            else _drawers[i].SaveOpen(false);
        }
        _shown = _open;
        _t = _open >= 0 ? 1f : 0f;
    }

    /// <summary>
    /// Draws the tabs and the open drawer. Call at the end of the dashboard's Draw, inside its
    /// body child (whose rect is the dashboard's: the face is chromeless).
    /// </summary>
    public void Draw()
    {
        if (ImGuiPopOuts.InPopOutFrame)
        {
            // Popped out: no tabs (see the header). Nothing polled meanwhile.
            Detach();
            return;
        }
        EnsureLoaded();

        // Slide.
        float target = _open >= 0 && _open == _shown ? 1f : 0f;
        if (_t != target)
        {
            float step = ImGuiNET.ImGui.GetIO().DeltaTime / SlideSeconds;
            if (step <= 0 || float.IsNaN(step)) step = 1f;
            _t = target > _t ? MathF.Min(1f, _t + step) : MathF.Max(0f, _t - step);
        }
        if (_t <= 0f && _open < 0) _shown = -1;
        float ease = _t * _t * (3f - 2f * _t);
        bool showing = _shown >= 0 && ease > 0.001f;
        for (int i = 0; i < _drawers.Length; i++) _drawers[i].Update(showing && i == _shown);

        // Geometry (pixels; k follows the dashboard's text size).
        Vector2 dashPos = ImGuiNET.ImGui.GetWindowPos(), dashSize = ImGuiNET.ImGui.GetWindowSize();
        Vector2 display = ImGuiNET.ImGui.GetIO().DisplaySize;
        float k = MathF.Max(1f, ImGuiFonts.Get(UiFont.Dash11).FontSize / 11f);
        int n = _drawers.Length;
        float tabW = 14 * k, tabGap = 3 * k, tabTop = 4 * k;
        // Tabs share the dashboard's height (40 tall at most, 20 at least: they may run past a minimized dashboard).
        float tabH = MathF.Max(20 * k, MathF.Min(40 * k, (dashSize.Y - 2 * tabTop - (n - 1) * tabGap) / Math.Max(1, n)));
        float tabsH = n * tabH + (n - 1) * tabGap;
        float widest = 0;
        foreach (DashboardDrawer d in _drawers) widest = MathF.Max(widest, d.Width(k));
        float panelW = showing ? _drawers[_shown].Width(k) : widest;
        float panelH = showing ? _drawers[_shown].Height(k) : 0;
        float dashRight = dashPos.X + dashSize.X;
        // Left unless there's no room there for the widest drawer and more on the right (so the tabs never switch sides
        // as drawers open and close).
        bool right = dashPos.X < tabW + widest && display.X - dashRight > dashPos.X;
        float shown = showing ? panelW * ease : 0;

        float winX = right ? dashRight : dashPos.X - tabW - shown;
        float winW = tabW + shown;
        float tabX = right ? dashRight : dashPos.X - tabW;
        // Neither side has room (a very wide dashboard): keep it on the screen, over the dashboard.
        float shift = 0;
        if (winX < 0) shift = -winX;
        else if (display.X > 1 && winX + winW > display.X) shift = display.X - (winX + winW);
        winX += shift;
        tabX += shift;

        float winY = dashPos.Y;
        float tabsBottom = tabTop + tabsH;   // relative to the dashboard's top
        float winH = showing ? MathF.Max(panelH, tabsBottom) : tabsBottom;
        if (display.Y > 1 && winY + winH > display.Y) winY = MathF.Max(0, display.Y - winH);
        float tabY = dashPos.Y + tabTop;
        winH = MathF.Max(winH, tabY + tabsH - winY);

        ImGuiNET.ImGui.SetNextWindowPos(new Vector2(winX, winY), ImGuiCond.Always);
        ImGuiNET.ImGui.SetNextWindowSize(new Vector2(winW, winH), ImGuiCond.Always);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        bool visible = ImGuiNET.ImGui.Begin(WindowId, WindowFlags);
        ImGuiNET.ImGui.PopStyleVar(4);
        try
        {
            if (visible)
            {
                ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Dash10));
                try
                {
                    if (showing)
                    {
                        // The panel's visible part; its content slides with the open edge.
                        float visX0 = right ? tabX + tabW : winX;
                        float contentX = right ? tabX + tabW + shown - panelW : winX;
                        Vector2 vMin = new(visX0, winY), vMax = new(visX0 + shown, winY + panelH);
                        DashboardDrawer d = _drawers[_shown];
                        ImGuiNET.ImGui.PushClipRect(vMin, vMax, true);
                        ImGuiNET.ImGui.PushID(d.Key);
                        try { d.DrawPanel(new Vector2(contentX, winY), panelW, panelH, k); }
                        finally
                        {
                            ImGuiNET.ImGui.PopID();
                            ImGuiNET.ImGui.PopClipRect();
                        }
                    }
                    for (int i = 0; i < n; i++)
                        DrawTab(_drawers[i], new Vector2(tabX, tabY + i * (tabH + tabGap)), new Vector2(tabW, tabH), right, k);
                }
                finally { ImGuiNET.ImGui.PopFont(); }
            }
        }
        finally
        {
            ImGuiNET.ImGui.End();
        }
    }

    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking
        | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav;

    private void DrawTab(DashboardDrawer d, Vector2 pos, Vector2 size, bool right, float k)
    {
        bool open = IsOpen(d);
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(d.TabId, size);
        bool hot = ImGuiNET.ImGui.IsItemHovered();
        // Like SetItemTooltip (same delay), but the closed tooltip is only built while it shows.
        if (ImGuiNET.ImGui.IsItemHovered(ImGuiHoveredFlags.ForTooltip))
            ImGuiNET.ImGui.SetTooltip(open ? d.HideTip : d.TabTooltip(right));
        if (clicked) Toggle(d);

        uint fill = ImGuiNET.ImGui.IsItemActive() ? FaceKit.Darken(BtnFill) : hot ? FaceKit.Lighten(BtnFill) : BtnFill;
        ImDrawFlags corners = right ? ImDrawFlags.RoundCornersRight : ImDrawFlags.RoundCornersLeft;
        dl.AddRectFilled(pos, pos + size, fill, 4 * k, corners);
        dl.AddRect(pos, pos + size, open ? Teal : BtnBord, 4 * k, corners);
        ImFontPtr f10 = ImGuiFonts.Get(UiFont.Dash10);
        float half = size.Y * 0.5f;
        PhosphorIcons.DrawCentered(dl, f10, d.Icon, pos, new Vector2(size.X, half), open ? Teal : Mute);
        // The caret points the way the drawer will move.
        string caret = right == open ? PhosphorIcons.CaretLeft : PhosphorIcons.CaretRight;
        PhosphorIcons.DrawCentered(dl, f10, caret, pos + new Vector2(0, half), new Vector2(size.X, half), hot ? White : Mute);
    }
}
