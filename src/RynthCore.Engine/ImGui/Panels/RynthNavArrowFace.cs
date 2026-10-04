// ============================================================================
//  RynthCore.Engine - ImGui/Panels/RynthNavArrowFace.cs
//  RynthNav's arrow: a small draggable overlay that points at RynthNav's arrow
//  target relative to the way you face, with the distance, the compass point
//  and the target's name (our take on GoArrow's arrow; Digero 2006, Virindi
//  2011, MIT). Shown and hidden from the RynthNav panel's Arrow tab; opens by
//  itself when a command sets the arrow (RynthNavArrowWatch). Works popped out.
//
//    arrow      turns with you every frame (the pose snapshot's heading); green
//               within 30 yd, amber while it points at a portal on a route; a
//               wand instead of the arrow when the next step is a recall.
//    text       name, distance and compass point, the route step.
//    buttons    pop out, hide; Go (RynthNav walks there) and Stop.
//
//  The target comes from RynthNav's status (UiSources.RynthNav); RynthNav owns
//  it and decides arrival. AC's render thread: snapshots only, no AC calls, and
//  strings are rebuilt only when what they show changes.
// ============================================================================

using System;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class RynthNavArrowFace : IImGuiPanel
{
    public const string Title = "RynthNav Arrow";

    public static void Register() => ImGuiPanelHost.Register(Title,
        new PanelSpec(new Vector2(236, 84), new Vector2(236, 84), PanelChrome.None, Background: 0, Rounding: 6, EdgeToEdge: true),
        () => new RynthNavArrowFace());

    /// <summary>Shows the overlay (any thread). Only where ImGui faces are drawn.</summary>
    public static void Show()
    {
        if (!ImGuiPanelHost.IsOpen(Title) && PanelRouter.ResolveDockedFace(Title) == PanelFace.ImGui)
            ImGuiPanelHost.Open(Title);
    }

    /// <summary>The arrow turns with you: redraw popped out often enough to look smooth.</summary>
    public int PopOutIdleHz => 20;

    private static readonly uint Glass = RynthTheme.Argb(0xDC0A0F14), Ring = RynthTheme.Argb(0xFF1A2A39);

    private readonly ArrowReadout _read = new();

    public void OnShown()
    {
        UiSources.RynthNav.Subscribe();
        UiSources.RynthNav.RequestRefresh();
    }

    public void OnHidden() => UiSources.RynthNav.Unsubscribe();

    public void Draw()
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 origin = ImGuiNET.ImGui.GetCursorScreenPos();
        Vector2 size = ImGuiNET.ImGui.GetContentRegionAvail();
        if (size.X < 40 || size.Y < 40) return;
        RynthNavStatus? st = UiSources.RynthNav.Current?.Value;
        _read.Update(st);

        dl.AddRectFilled(origin, origin + size, Glass, 6);
        dl.AddRect(origin, origin + size, BtnBord, 6);

        // The arrow, in a ring on the left.
        float d = size.Y - 12;
        Vector2 c = origin + new Vector2(6 + d * 0.5f, size.Y * 0.5f);
        dl.AddCircleFilled(c, d * 0.5f, ShellBg, 32);
        dl.AddCircle(c, d * 0.5f, Ring, 32, 1.5f);
        _read.DrawPointer(dl, c, d * 0.5f - 6);

        // Text.
        float tx = origin.X + d + 14, right = origin.X + size.X - 6;
        ImFontPtr bold = ImGuiFonts.Get(UiFont.UiBold11), f10 = ImGuiFonts.Get(UiFont.Ui10), f9 = ImGuiFonts.Get(UiFont.Ui9);
        dl.PushClipRect(new Vector2(tx, origin.Y), new Vector2(right - 46, origin.Y + size.Y), true);
        dl.AddText(bold, bold.FontSize, new Vector2(tx, origin.Y + 6), Teal, _read.Line1);
        dl.PopClipRect();
        dl.PushClipRect(new Vector2(tx, origin.Y), new Vector2(right, origin.Y + size.Y), true);
        dl.AddText(f10, f10.FontSize, new Vector2(tx, origin.Y + 26), Text, _read.Line2);
        dl.PopClipRect();
        dl.PushClipRect(new Vector2(tx, origin.Y), new Vector2(right - 48, origin.Y + size.Y), true);
        dl.AddText(f9, f9.FontSize, new Vector2(tx, origin.Y + size.Y - 6 - f9.FontSize - 2), Mute, _read.Line3);
        dl.PopClipRect();

        // Buttons (before the surface, so they win the hover).
        const float b = 20, gap = 3;
        var closePos = new Vector2(right - b, origin.Y + 5);
        var popPos = closePos - new Vector2(b + gap, 0);
        if (PanelRouter.CanPopOut)
        {
            if (IconButton("##rna_pop", ImGuiPanelHost.PopOutGlyph, popPos, new Vector2(b, b), Mute, BtnFill, font: UiFont.Ui11))
                ImGuiPanelHost.TogglePopOut(Title);
            ImGuiNET.ImGui.SetItemTooltip(ImGuiPanelHost.PopOutTooltip);
        }
        if (IconButton("##rna_close", PhosphorIcons.X, closePos, new Vector2(b, b), Mute, BtnFill, font: UiFont.Ui11))
            ImGuiPanelHost.Close(Title);
        ImGuiNET.ImGui.SetItemTooltip("Hide the arrow (the RynthNav panel's Arrow tab shows it again)");
        var stopPos = new Vector2(right - b, origin.Y + size.Y - b - 5);
        var goPos = stopPos - new Vector2(b + gap, 0);
        bool canGo = _read.CanGo;
        if (IconButton("##rna_go", PhosphorIcons.Play, goPos, new Vector2(b, b), Green, BtnFill, enabled: canGo, font: UiFont.Ui11)
            && st?.Arrow is RynthNavArrow ar)
            RynthNavCommands.Go(ar.Final, ar.FinalNs, ar.FinalEw);
        ImGuiNET.ImGui.SetItemTooltip("Go: RynthNav walks there (with portals and recalls when its routing is on)");
        if (IconButton("##rna_stop", PhosphorIcons.Stop, stopPos, new Vector2(b, b), Red, BtnFill, font: UiFont.Ui11))
            RynthNavCommands.Move(5);
        ImGuiNET.ImGui.SetItemTooltip("Stop walking");

        // Surface: drag moves the overlay (the OS window when popped out).
        ImGuiNET.ImGui.SetCursorScreenPos(origin);
        ImGuiNET.ImGui.InvisibleButton("##rna_surface", size);
        ImGuiPanelHost.DragWindowWithLastItem();
        if (ImGuiNET.ImGui.IsItemHovered() && _read.Tooltip.Length > 0) ImGuiNET.ImGui.SetItemTooltip(_read.Tooltip);
    }
}

/// <summary>
/// What the arrow shows this frame: the target from RynthNav's status, your position and
/// heading from the pose snapshot. Shared by the overlay and the panel's Arrow tab. AC's
/// render thread; the text is rebuilt only when what it shows changes.
/// </summary>
internal sealed class ArrowReadout
{
    public string Line1 = "No arrow", Line2 = "", Line3 = "", Tooltip = "";
    public bool HasTarget, CanGo, Pointing;
    public float Relative;            // degrees clockwise from straight ahead
    public double Distance = double.NaN;
    public string Kind = "";

    private RynthNavArrow? _arrow;
    private int _distKey = int.MinValue, _compassKey = -1;
    private bool _wasInDungeon, _wasAvailable = true;
    private string _compass = "";

    private static readonly uint NearCol = RynthTheme.Argb(0xFF40D973);

    public void Update(RynthNavStatus? st)
    {
        RynthNavArrow? a = st?.Arrow;
        bool available = st == null || st.Travel;
        if (!ReferenceEquals(a, _arrow) || available != _wasAvailable)
        {
            _arrow = a;
            _wasAvailable = available;
            _distKey = int.MinValue;
            _compassKey = -1;
            HasTarget = a != null;
            Kind = a?.Kind ?? "";
            CanGo = a != null && (a.FinalNs != 0 || a.FinalEw != 0);
            Line1 = a == null ? (available ? "No arrow" : "RynthNav is too old for the arrow") : a.Name;
            Line3 = a == null ? (available ? "Atlas, chat coords or /rnav arrow" : "")
                : a.Kind == "recall" ? (a.StepText.Length > 0 ? a.StepText : "cast the recall")
                : a.Kind == "portal" ? "walk into the portal" + (a.Steps > 1 ? $" · step {a.Step}/{a.Steps}" : "")
                : a.Steps > 1 ? $"step {a.Step}/{a.Steps}" : a.Type;
            Tooltip = a == null ? "" : (a.Kind == "recall" ? "Cast " + a.Name : a.Name + "  " + a.CoordText)
                + (a.Steps > 1 ? "\nOn the way to " + a.Final : "");
            Line2 = a == null ? "" : a.Kind == "recall" ? "Cast " + a.Name : a.CoordText;
        }
        Pointing = false;
        if (a == null || a.Kind == "recall" || !a.OnMap) return;
        if (!RynthNavAtlas.TryPlayerPos(out double ns, out double ew, out bool onMap, out _)) return;
        if (!onMap)
        {
            if (!_wasInDungeon) { Line2 = "in a dungeon: no direction"; _wasInDungeon = true; _distKey = int.MinValue; }
            return;
        }
        _wasInDungeon = false;
        Distance = RynthNavAtlas.Distance(ns, ew, a.Ns, a.Ew);
        double bearing = RynthNavAtlas.Bearing(ns, ew, a.Ns, a.Ew);
        int compassKey = (int)Math.Round(((bearing % 360.0) + 360.0) % 360.0 / 22.5) & 15;
        if (compassKey != _compassKey) { _compassKey = compassKey; _compass = RynthNavAtlas.CompassPoint(bearing); _distKey = int.MinValue; }
        int key = Distance < 1000 ? (int)Math.Round(Distance) : 100000 + (int)Math.Round(Distance / 100);
        if (key != _distKey)
        {
            _distKey = key;
            Line2 = RynthNavStatus.Yards(Distance) + "  " + _compass;
        }
        if (TryHeading(out float heading))
        {
            Relative = (float)bearing - heading;
            Pointing = true;
        }
    }

    /// <summary>The pointer at <paramref name="c"/>: an arrow, a wand for a recall, a dot when there's nothing.</summary>
    public void DrawPointer(ImDrawListPtr dl, Vector2 c, float r)
    {
        if (!HasTarget) { dl.AddCircleFilled(c, 3, FaceKit.Mute); return; }
        if (Kind == "recall")
        {
            PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui14), PhosphorIcons.MagicWand, c - new Vector2(r, r), new Vector2(2 * r, 2 * r), FaceKit.Amber);
            return;
        }
        if (!Pointing) { PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui14), PhosphorIcons.Compass, c - new Vector2(r, r), new Vector2(2 * r, 2 * r), FaceKit.Mute); return; }
        uint col = Kind == "portal" ? FaceKit.Amber : Distance <= 30 ? NearCol : FaceKit.Teal;
        float rad = Relative * (MathF.PI / 180f);
        float s = MathF.Sin(rad), co = MathF.Cos(rad);
        Vector2 tip = Rot(c, 0, -r, s, co), left = Rot(c, -0.62f * r, 0.78f * r, s, co),
            notch = Rot(c, 0, 0.36f * r, s, co), right = Rot(c, 0.62f * r, 0.78f * r, s, co);
        dl.AddTriangleFilled(tip, left, notch, col);
        dl.AddTriangleFilled(tip, notch, right, col);
    }

    private static Vector2 Rot(Vector2 c, float x, float y, float s, float co) =>
        new(c.X + x * co - y * s, c.Y + x * s + y * co);

    /// <summary>Your heading this frame (compass degrees) from the pose snapshot; no AC call (SenseFace's way).</summary>
    private static bool TryHeading(out float heading)
    {
        heading = 0;
        if (!PlayerPhysicsHooks.TryGetPlayerPoseSnapshot(out _, out _, out _, out _, out float qw, out float qz, out bool valid, out float h))
            return false;
        if (valid) { heading = h; return true; }
        double yaw = 2.0 * Math.Atan2(qz, qw) * (180.0 / Math.PI);
        heading = (float)((-yaw % 360.0 + 720.0) % 360.0);
        return true;
    }
}
