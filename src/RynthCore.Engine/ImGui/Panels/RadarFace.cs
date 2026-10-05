// ============================================================================
//  RynthCore.Engine - ImGui/Panels/RadarFace.cs
//  ImGui face of the Radar (docs/IMGUI_PARITY_PLAN.md §2.2), drawn the way
//  the Avalonia face's RadarSurface.Render does: gold rim, canvas, crosshair,
//  floors (current layer full, the one above and below at 30%) with slope
//  tints, walls (blue when visited), visited overlay, markers, player arrow,
//  outlined compass on the playfield perimeter, coordinate strip. Top-right
//  pop-out and settings (gear) buttons, wheel zoom, left-drag moves, left-click on a dot
//  selects it in game by object id (hover: name, distance, health), right-click menu, settings
//  popup. Settings are shared with the Avalonia face (RadarSettingsStore).
//  Marker kinds (RadarKind) each have a shape (RadarMarkerStyle), a show box
//  and a colour the user can pick; the settings popup doubles as the legend.
//
//  Data: UiSources.Radar (RadarView, lookups precomputed). World geometry is
//  drawn without anti-aliasing (crisp tiling like the Avalonia face's aliased
//  mode, and far fewer vertices for big dungeons); no per-frame allocation.
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;
using RynthCore.Engine.UI.Panels;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class RadarFace : IImGuiPanel
{
    public const string Title = "Radar";

    public static void Register()
    {
        ImGuiPanelHost.Register(Title,
            new PanelSpec(new Vector2(320, 320), new Vector2(120, 120), PanelChrome.None,
                Background: 0, Rounding: 0, EdgeToEdge: true, GoldGrip: true),
            () => new RadarFace());
        // The Inventory panel (engine data only, like the radar): registered here so
        // engine init needs no new line in EntryPoint (as LootEditor / DungeonMap
        // register from RynthAiFace). Opened from the bar and /rc inv.
        InventoryFace.Register();
    }

    // ── Palette (RadarPanel's, ARGB) ────────────────────────────────────
    private const uint FrameOuterArgb = 0xFF0A0D14, CanvasBgArgb = 0xFF0A121A;
    private static readonly uint FrameInner = C(0xFF9E853D), FrameAccent = C(0xFFE6C766),
        Crosshair = C(0x994D6680), Cardinal = C(0xFFFFFFFF), CoordText = C(0xFFF2F8FF), Outline = C(0xF2000000),
        PlayerCol = C(0xFF33FF66), ButtonBg = C(0xC0141F29), ButtonBorder = C(0xFF34587A);
    // Marker colours per kind (RadarSettingsStore.KindColors, user-pickable), refreshed each frame.
    private static readonly uint[] KindCols = new uint[RadarKind.Count];
    private const uint WallUnseen = 0xFFFFFFFF, WallVisited = 0xFF4099FF, FloorFlat = 0x8C2E8C38,
        FloorUp = 0x668C1F14, FloorDown = 0x661A8026, FloorVisited = 0xA64D6B94;
    private static uint C(uint argb) => RynthTheme.Argb(argb);

    /// <summary>ARGB colour with its alpha scaled, as an ImGui colour.</summary>
    private static uint MulAlpha(uint argb, float k) =>
        RynthTheme.Argb((argb & 0x00FFFFFF) | ((uint)Math.Clamp((int)MathF.Round((argb >> 24) * k), 0, 255) << 24));

    private static readonly (string Label, float WorldX, float WorldY)[] Compass =
        { ("N", 0, 1), ("E", 1, 0), ("S", 0, -1), ("W", -1, 0) };

    private bool _settingsOpen;

    /// <summary>Markers move: popped out, redraw smoothly even when idle.</summary>
    public int PopOutIdleHz => 20;

    public bool ClickThrough =>
        RadarSettingsStore.CtrlGatedClickThrough && !ImGuiNET.ImGui.GetIO().KeyCtrl;

    public void OnShown()
    {
        RadarSettingsStore.Load();
        UiSources.Radar.Subscribe();
        UiSources.Radar.RequestRefresh();
    }

    public void OnHidden() => UiSources.Radar.Unsubscribe();

    public void Draw()
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        Vector2 origin = ImGuiNET.ImGui.GetCursorScreenPos();
        Vector2 size = ImGuiNET.ImGui.GetContentRegionAvail();
        if (size.X < 16 || size.Y < 16) return;
        RadarView? view = UiSources.Radar.Current?.Value;

        bool rim = RadarSettingsStore.ShowGoldRim;
        float canvasInset = rim ? 5 : 0;
        const float coordStrip = 14;
        float opacity = Math.Clamp(RadarSettingsStore.Opacity, 0f, 1f);
        Vector2 frameMax = origin + size;

        // ── Frame: outer black (fades) + gold inner border + accent ──
        if (rim)
        {
            dl.AddRectFilled(origin, frameMax, MulAlpha(FrameOuterArgb, opacity));
            dl.AddRect(origin + new Vector2(1, 1), frameMax - new Vector2(1, 1), FrameInner, 0, ImDrawFlags.None, 2);
            dl.AddRect(origin + new Vector2(3, 3), frameMax - new Vector2(3, 3), FrameAccent, 0, ImDrawFlags.None, 1);
        }

        // ── Canvas and world playfield (canvas minus the coord strip) ──
        Vector2 canvasMin = origin + new Vector2(canvasInset, canvasInset);
        Vector2 canvasMax = frameMax - new Vector2(canvasInset, canvasInset);
        dl.AddRectFilled(canvasMin, canvasMax, MulAlpha(CanvasBgArgb, opacity));
        var worldMin = canvasMin;
        var worldMax = new Vector2(canvasMax.X, canvasMax.Y - coordStrip);
        Vector2 centre = (worldMin + worldMax) * 0.5f;
        dl.AddLine(new Vector2(centre.X, worldMin.Y), new Vector2(centre.X, worldMax.Y), Crosshair);
        dl.AddLine(new Vector2(worldMin.X, centre.Y), new Vector2(worldMax.X, centre.Y), Crosshair);

        if (view != null)
        {
            ImDrawListFlags saved = dl.Flags;
            dl.Flags = saved & ~(ImDrawListFlags.AntiAliasedLines | ImDrawListFlags.AntiAliasedLinesUseTex | ImDrawListFlags.AntiAliasedFill);
            dl.PushClipRect(worldMin, worldMax, true);
            DrawWorld(dl, view, centre, worldMin, worldMax);
            dl.PopClipRect();
            dl.Flags = saved;

            DrawOutlinedText(dl, ImGuiFonts.Get(UiFont.UiBold11), view.CoordText,
                new Vector2((canvasMin.X + canvasMax.X) * 0.5f, canvasMax.Y), CoordText, alignBottom: true);
            DrawCardinals(dl, view, centre, (worldMax.X - worldMin.X) * 0.5f, (worldMax.Y - worldMin.Y) * 0.5f);
        }

        // ── Buttons (submitted before the surface so they win the hover) ──
        const float btn = 22, gap = 4, inset = 6;
        var gearPos = new Vector2(frameMax.X - inset - btn, origin.Y + inset);
        var popPos = gearPos - new Vector2(btn + gap, 0);
        if (PanelRouter.CanPopOut)
        {
            if (IconButton("##radar_pop", popPos, ImGuiPanelHost.PopOutGlyph))
                ImGuiPanelHost.TogglePopOut(Title);
            ImGuiNET.ImGui.SetItemTooltip(ImGuiPanelHost.PopOutTooltip);
        }
        bool gear = IconButton("##radar_gear", gearPos, PhosphorIcons.Gear);
        ImGuiNET.ImGui.SetItemTooltip("Radar settings");
        if (gear)
        {
            if (_settingsOpen) _settingsOpen = false;
            else
            {
                _settingsOpen = true;
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(gearPos.X + btn, gearPos.Y + btn + 4), ImGuiCond.Always, new Vector2(1, 0));
            }
        }

        // ── Surface: drag moves the panel, a click on a dot selects, wheel zooms, right-click menu ──
        ImGuiNET.ImGui.SetCursorScreenPos(origin);
        bool surfaceClicked = ImGuiNET.ImGui.InvisibleButton("##radar_surface", size);
        ImGuiPanelHost.DragWindowWithLastItem();
        if (view != null && RadarSettingsStore.ClickToSelect)
            MarkerInput(dl, view, centre, worldMin, worldMax, surfaceClicked);
        if (ImGuiNET.ImGui.IsItemHovered())
        {
            float wheel = ImGuiNET.ImGui.GetIO().MouseWheel;
            if (wheel != 0)
            {
                float next = Math.Clamp(RadarSettingsStore.Zoom + wheel * 0.15f, 0.5f, 6.0f);
                if (next != RadarSettingsStore.Zoom) { RadarSettingsStore.Zoom = next; RadarSettingsStore.Save(); }
            }
        }
        if (ImGuiNET.ImGui.BeginPopupContextItem("##radar_menu"))
        {
            if (PanelRouter.CanPopOut && ImGuiNET.ImGui.MenuItem(ImGuiPopOuts.InPopOutFrame ? "Dock back" : "Pop out"))
                ImGuiPanelHost.TogglePopOut(Title);
            ImGuiNET.ImGui.Separator();
            if (ImGuiNET.ImGui.MenuItem("Rotate with player", "", RadarSettingsStore.RotateWithPlayer))
            {
                RadarSettingsStore.RotateWithPlayer = !RadarSettingsStore.RotateWithPlayer;
                RadarSettingsStore.Save();
            }
            ImGuiPanelHost.TextSizeMenu(Title);
            ImGuiNET.ImGui.Separator();
            if (ImGuiNET.ImGui.MenuItem("Close"))
                ImGuiPanelHost.Close(Title);
            ImGuiNET.ImGui.EndPopup();
        }

        SettingsWindow();
    }

    // ── World ─────────────────────────────────────────────────────────────

    /// <summary>World (AC x east, y north) → screen, north-up or rotated so facing is up.</summary>
    private readonly struct Projection
    {
        private readonly float _cx, _cy, _pwx, _pwy, _sin, _cos, _zoom;
        private readonly bool _rotate;

        public Projection(Vector2 centre, RadarPlayer p, bool rotate, float zoom)
        {
            _cx = centre.X; _cy = centre.Y; _pwx = p.WorldX; _pwy = p.WorldY;
            float h = p.Heading * MathF.PI / 180f; // AC: 0 = east, 90 = north
            _sin = MathF.Sin(h); _cos = MathF.Cos(h);
            _rotate = rotate; _zoom = zoom;
        }

        public float Sin => _sin;
        public float Cos => _cos;

        public Vector2 P(float wx, float wy)
        {
            float dx = wx - _pwx, dy = wy - _pwy;
            if (_rotate)
            {
                float right = _sin * dx - _cos * dy;
                float forward = _cos * dx + _sin * dy;
                return new Vector2(_cx + right * _zoom, _cy - forward * _zoom);
            }
            return new Vector2(_cx + dx * _zoom, _cy - dy * _zoom);
        }
    }

    private static void DrawWorld(ImDrawListPtr dl, RadarView view, Vector2 centre, Vector2 clipMin, Vector2 clipMax)
    {
        RadarSnapshot live = view.Live;
        bool rotate = RadarSettingsStore.RotateWithPlayer;
        var proj = new Projection(centre, live.Player, rotate, MathF.Max(0.5f, RadarSettingsStore.Zoom));

        int cur = view.CurrentLayer, count = live.LayerZs.Count;
        // Same order as the Avalonia/ImGui-era radar: below, above (dimmed), current.
        if (cur - 1 >= 0) DrawLayer(dl, view, cur - 1, 0.30f, false, proj, rotate, clipMin, clipMax);
        if (cur + 1 < count && cur >= 0) DrawLayer(dl, view, cur + 1, 0.30f, false, proj, rotate, clipMin, clipMax);
        if (cur >= 0) DrawLayer(dl, view, cur, 1.00f, true, proj, rotate, clipMin, clipMax);

        // Markers (shapes: RadarMarkerStyle). Corpses and items first, creatures on top.
        RadarMarkerStyle.FillColors(KindCols);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (RadarMarker m in live.Markers)
            {
                if (!RadarSettingsStore.ShowKind(m.Kind)) continue;
                if ((pass == 1) != (RadarKind.IsCreature(m.Kind) || m.Kind is RadarKind.Portal or RadarKind.Door)) continue;
                Vector2 pp = proj.P(m.X, m.Y);
                if (pp.X < clipMin.X || pp.X > clipMax.X || pp.Y < clipMin.Y || pp.Y > clipMax.Y) continue;
                RadarMarkerStyle.Draw(dl, m.Kind, pp, KindCols[m.Kind]);
            }
        }

        // Player dot + 10 px heading arrow (always up when rotating).
        dl.AddCircleFilled(centre, 3.5f, PlayerCol, 10);
        Vector2 tip = rotate ? centre + new Vector2(0, -10) : centre + new Vector2(proj.Cos * 10, -proj.Sin * 10);
        dl.AddLine(centre, tip, PlayerCol, 2);
    }

    private static void DrawLayer(ImDrawListPtr dl, RadarView view, int layer, float a, bool current,
        in Projection proj, bool rotate, Vector2 clipMin, Vector2 clipMax)
    {
        // Floor strips: 5 floats (x0, y0, x1, y1, type).
        if (view.FillsByLayer[layer] is float[] strips)
        {
            uint flat = MulAlpha(FloorFlat, a), up = MulAlpha(FloorUp, a), down = MulAlpha(FloorDown, a);
            for (int i = 0; i + 4 < strips.Length; i += 5)
            {
                int type = (int)strips[i + 4];
                uint col = type == 1 ? up : type == 2 ? down : flat;
                FillRect(dl, proj, rotate, strips[i], strips[i + 1], strips[i + 2], strips[i + 3], col, clipMin, clipMax);
            }
        }

        // Walls: 6 floats (ax, ay, bx, by, cell x, cell y); visited tint on the current layer.
        if (view.WallsByLayer[layer] is float[] segs)
        {
            uint unseen = MulAlpha(WallUnseen, a), visited = MulAlpha(WallVisited, a);
            float thick = current ? 1.5f : 1.0f;
            bool[]? flags = current ? view.CurrentWallVisited : null;
            for (int i = 0, w = 0; i + 5 < segs.Length; i += 6, w++)
            {
                Vector2 pa = proj.P(segs[i], segs[i + 1]);
                Vector2 pb = proj.P(segs[i + 2], segs[i + 3]);
                if (MathF.Max(pa.X, pb.X) < clipMin.X || MathF.Min(pa.X, pb.X) > clipMax.X ||
                    MathF.Max(pa.Y, pb.Y) < clipMin.Y || MathF.Min(pa.Y, pb.Y) > clipMax.Y) continue;
                bool v = flags != null && w < flags.Length && flags[w];
                dl.AddLine(pa, pb, v ? visited : unseen, thick);
            }
        }

        // Visited overlay (current layer only): 4 floats per strip.
        if (current && view.VisitedByLayer[layer] is float[] vStrips)
        {
            uint fill = MulAlpha(FloorVisited, a);
            for (int i = 0; i + 3 < vStrips.Length; i += 4)
                FillRect(dl, proj, rotate, vStrips[i], vStrips[i + 1], vStrips[i + 2], vStrips[i + 3], fill, clipMin, clipMax);
        }
    }

    private static void FillRect(ImDrawListPtr dl, in Projection proj, bool rotate, float x0, float y0, float x1, float y1,
        uint col, Vector2 clipMin, Vector2 clipMax)
    {
        Vector2 p00 = proj.P(x0, y0), p11 = proj.P(x1, y1);
        if (rotate)
        {
            Vector2 p10 = proj.P(x1, y0), p01 = proj.P(x0, y1);
            float minX = MathF.Min(MathF.Min(p00.X, p11.X), MathF.Min(p10.X, p01.X));
            float maxX = MathF.Max(MathF.Max(p00.X, p11.X), MathF.Max(p10.X, p01.X));
            float minY = MathF.Min(MathF.Min(p00.Y, p11.Y), MathF.Min(p10.Y, p01.Y));
            float maxY = MathF.Max(MathF.Max(p00.Y, p11.Y), MathF.Max(p10.Y, p01.Y));
            if (maxX < clipMin.X || minX > clipMax.X || maxY < clipMin.Y || minY > clipMax.Y) return;
            dl.AddQuadFilled(p00, p10, p11, p01, col);
        }
        else
        {
            var a = Vector2.Min(p00, p11);
            var b = Vector2.Max(p00, p11);
            if (b.X < clipMin.X || a.X > clipMax.X || b.Y < clipMin.Y || a.Y > clipMax.Y) return;
            dl.AddRectFilled(a, b, col);
        }
    }

    // ── Click-to-select ─────────────────────────────────────────────────────
    // Left-click a dot: that object is selected in game, as a click on it in the
    // 3D world would. A press that moves more than a few pixels is a drag (the
    // panel moves) and selects nothing. Hover shows the object's name and
    // distance (and its last known health, when the engine has seen one).
    // RynthAi 2026-10-01 and later send each marker's object id, and the click
    // selects exactly that id. Older RynthAi builds send no id (Id = 0): then
    // the clicked marker is matched to the client's object list by position
    // (the engine's ~0.1 s position snapshot, same landblock, name preferred),
    // which can pick the wrong one of two same-name monsters close together.
    // The select itself is queued for AC's main thread
    // (AcMainThreadQueue.EnqueueOverlaySelect), which re-checks the object
    // still exists before selecting.

    private const float ClickSlop = 4f;          // px a press may move and still be a click
    private const float MatchTolerance = 5f;     // metres between a marker and its object (snapshot lag)
    private const uint TypeCreature = 0x10, TypePortal = 0x10000;

    private Vector2 _pressPos;
    private float _pressTravel;
    private byte _tipKind = byte.MaxValue;
    private string? _tipLabel;
    private int _tipDistance = int.MinValue;
    private int _tipPct = -1;
    private string _tipText = "";

    private void MarkerInput(ImDrawListPtr dl, RadarView view, Vector2 centre, Vector2 clipMin, Vector2 clipMax, bool clicked)
    {
        Vector2 mouse = ImGuiNET.ImGui.GetIO().MousePos;
        if (ImGuiNET.ImGui.IsItemActivated())
        {
            _pressPos = mouse;
            _pressTravel = 0f;
        }
        if (ImGuiNET.ImGui.IsItemActive())
        {
            _pressTravel = MathF.Max(_pressTravel, Vector2.Distance(mouse, _pressPos));
            // Popped out, a 2 px drag hands the move to the OS window (DragWindowWithLastItem),
            // which then follows the cursor: the context's mouse barely moves, so mark it a drag.
            if (ImGuiPopOuts.InPopOutFrame && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f))
                _pressTravel = float.MaxValue;
        }

        bool hovered = ImGuiNET.ImGui.IsItemHovered();
        if (!hovered && !clicked) return;
        if (mouse.X < clipMin.X || mouse.X > clipMax.X || mouse.Y < clipMin.Y || mouse.Y > clipMax.Y) return;

        float zoom = MathF.Max(0.5f, RadarSettingsStore.Zoom);
        var proj = new Projection(centre, view.Live.Player, RadarSettingsStore.RotateWithPlayer, zoom);
        // Hit radius: a little more than the dot, growing with zoom, in UI-scaled pixels.
        float radius = Math.Clamp(5f + 1.5f * zoom, 6f, 14f) * MathF.Max(1f, EngineFrameController.FontScale);
        RadarMarker? hit = MarkerAt(view, proj, mouse, radius, clipMin, clipMax, out Vector2 hitPos);
        if (hit == null) return;

        if (clicked && _pressTravel <= ClickSlop)
        {
            uint id = hit.Id != 0 ? hit.Id : ResolveMarker(view, hit);
            if (id != 0)
                Compatibility.AcMainThreadQueue.EnqueueOverlaySelect(id);
            return;
        }

        if (!hovered || ImGuiNET.ImGui.IsItemActive()) return;
        dl.PushClipRect(clipMin, clipMax, true);
        dl.AddCircle(hitPos, 6.5f, CoordText, 16, 1.5f);
        dl.PopClipRect();
        ImGuiNET.ImGui.BeginTooltip();
        ImGuiNET.ImGui.TextUnformatted(TipText(view, hit));
        ImGuiNET.ImGui.EndTooltip();
    }

    /// <summary>The shown marker nearest <paramref name="mouse"/> within <paramref name="radius"/> px, or null.</summary>
    private static RadarMarker? MarkerAt(RadarView view, in Projection proj, Vector2 mouse, float radius,
        Vector2 clipMin, Vector2 clipMax, out Vector2 at)
    {
        RadarMarker? best = null;
        float bestD2 = radius * radius;
        at = default;
        foreach (RadarMarker m in view.Live.Markers)
        {
            if (!RadarSettingsStore.ShowKind(m.Kind)) continue;
            Vector2 pp = proj.P(m.X, m.Y);
            if (pp.X < clipMin.X || pp.X > clipMax.X || pp.Y < clipMin.Y || pp.Y > clipMax.Y) continue;
            float d2 = Vector2.DistanceSquared(pp, mouse);
            if (d2 > bestD2) continue;
            bestD2 = d2;
            best = m;
            at = pp;
        }
        return best;
    }

    /// <summary>
    /// The object a marker stands for: the nearest object in the player's landblock
    /// (world = landblock-local + 192 m per landblock step, as the radar's markers)
    /// of the marker's kind, within <see cref="MatchTolerance"/>; a name match wins
    /// over a slightly nearer stranger. 0 when nothing matches. Only for markers
    /// without an id (older RynthAi builds).
    /// </summary>
    private static uint ResolveMarker(RadarView view, RadarMarker m)
    {
        uint landblock = view.Live.Player.Landblock;
        if (landblock == 0) return 0;
        uint playerId = Compatibility.ClientHelperHooks.GetPlayerId();
        string? label = string.IsNullOrEmpty(m.Label) ? null : m.Label;
        const float tol2 = MatchTolerance * MatchTolerance;
        uint best = 0;
        float bestScore = float.MaxValue;
        foreach (uint id in Compatibility.ClientObjectHooks.LiveObjectIds)
        {
            if (id == 0 || id == playerId) continue;
            if (!Compatibility.ClientObjectHooks.TryGetSnapshotPosition(id, out uint cell, out float x, out float y, out float z)) continue;
            if ((cell >> 16) != landblock) continue;
            float dx = x + ((cell >> 24) & 0xFF) * 192f - m.X;
            float dy = y + ((cell >> 16) & 0xFF) * 192f - m.Y;
            float dz = z - m.Z;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 > tol2) continue;
            if (m.Kind <= 2)
            {
                if (!Compatibility.ClientObjectHooks.TryGetSnapshotItemType(id, out uint type)) continue;
                uint need = m.Kind == 2 ? TypePortal : TypeCreature;
                if ((type & need) == 0) continue;
            }
            bool named = label != null && Compatibility.ClientObjectHooks.TryGetSnapshotName(id, out string name)
                         && name.Equals(label, StringComparison.Ordinal);
            float score = named ? d2 : d2 + tol2;
            if (score < bestScore) { bestScore = score; best = id; }
        }
        return best;
    }

    /// <summary>
    /// "Name\n12 yd" for the hovered marker, plus "  75%" when the marker has an id and
    /// the engine has a health reading for it (MonsterHudData: one locked lookup, hover
    /// only). Rebuilt only when the marker, whole yards or whole percent change.
    /// </summary>
    private string TipText(RadarView view, RadarMarker m)
    {
        RadarPlayer p = view.Live.Player;
        float dx = m.X - p.WorldX, dy = m.Y - p.WorldY, dz = m.Z - p.Z;
        int d = (int)MathF.Round(MathF.Sqrt(dx * dx + dy * dy + dz * dz));
        int pct = -1;
        if (m.Id != 0 && RadarKind.IsCreature(m.Kind) && Hud.MonsterHudData.TryGetHealth(m.Id, out Hud.HealthObservation h))
            pct = (int)MathF.Round(h.Ratio * 100f);
        // Markers are rebuilt on every radar poll: key on what the text shows, not the instance.
        if (m.Kind == _tipKind && d == _tipDistance && pct == _tipPct
            && string.Equals(m.Label, _tipLabel, StringComparison.Ordinal))
            return _tipText;
        _tipKind = m.Kind;
        _tipLabel = m.Label;
        _tipDistance = d;
        _tipPct = pct;
        // A label is the object's name; the kinds a name alone doesn't tell apart say what they are.
        string name = string.IsNullOrEmpty(m.Label) ? RadarKind.Name(m.Kind)
            : m.Kind is RadarKind.Fellow or RadarKind.Pet or RadarKind.Vendor or RadarKind.OwnCorpse or RadarKind.Player
                ? m.Label + "  (" + RadarKind.Name(m.Kind) + ")"
                : m.Label!;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _tipText = name + "\n" + d.ToString(inv) + " yd"
                   + (pct >= 0 ? "   " + pct.ToString(inv) + "%" : "");
        return _tipText;
    }

    // ── Compass and text ───────────────────────────────────────────────────

    private static void DrawCardinals(ImDrawListPtr dl, RadarView view, Vector2 centre, float halfW, float halfH)
    {
        const float sz = 14;
        bool rotate = RadarSettingsStore.RotateWithPlayer;
        float h = view.Live.Player.Heading * MathF.PI / 180f;
        float sinH = MathF.Sin(h), cosH = MathF.Cos(h);
        float effW = MathF.Max(1, halfW - sz), effH = MathF.Max(1, halfH - sz);
        ImFontPtr font = ImGuiFonts.Get(UiFont.UiBold14);

        foreach (var c in Compass)
        {
            float dirX, dirY;
            if (rotate)
            {
                dirX = sinH * c.WorldX - cosH * c.WorldY;
                dirY = -(cosH * c.WorldX + sinH * c.WorldY);
            }
            else
            {
                dirX = c.WorldX;
                dirY = -c.WorldY;
            }
            // Onto the rectangular perimeter: max(|dx t|/effW, |dy t|/effH) = 1.
            float m = MathF.Max(MathF.Abs(dirX) / effW, MathF.Abs(dirY) / effH);
            if (m < 1e-4f) continue;
            float t = 1 / m;
            DrawOutlinedText(dl, font, c.Label, new Vector2(centre.X + dirX * t, centre.Y + dirY * t), Cardinal, alignBottom: false);
        }
    }

    /// <summary>Text with a 1 px 4-way black outline, centred on x; centred or bottom-aligned on y.</summary>
    private static void DrawOutlinedText(ImDrawListPtr dl, ImFontPtr font, string text, Vector2 anchor, uint col, bool alignBottom)
    {
        ImGuiNET.ImGui.PushFont(font);
        Vector2 ts = ImGuiNET.ImGui.CalcTextSize(text);
        ImGuiNET.ImGui.PopFont();
        var pos = new Vector2(anchor.X - ts.X * 0.5f, alignBottom ? anchor.Y - ts.Y - 1 : anchor.Y - ts.Y * 0.5f);
        float fs = font.FontSize;
        dl.AddText(font, fs, pos + new Vector2(-1, 0), Outline, text);
        dl.AddText(font, fs, pos + new Vector2(1, 0), Outline, text);
        dl.AddText(font, fs, pos + new Vector2(0, -1), Outline, text);
        dl.AddText(font, fs, pos + new Vector2(0, 1), Outline, text);
        dl.AddText(font, fs, pos, col, text);
    }

    private static bool IconButton(string id, Vector2 pos, string glyph)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        var size = new Vector2(22, 22);
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(id, size);
        uint bg = ImGuiNET.ImGui.IsItemActive() ? C(0xE0101820) : ImGuiNET.ImGui.IsItemHovered() ? C(0xE0243444) : ButtonBg;
        dl.AddRectFilled(pos, pos + size, bg, 3);
        dl.AddRect(pos, pos + size, ButtonBorder, 3);
        PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui14), glyph, pos, size, CoordText);
        return clicked;
    }

    // ── Settings (gear) ─────────────────────────────────────────────────────

    private void SettingsWindow()
    {
        if (!_settingsOpen) return;
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.WindowBg, C(0xFF0A121A));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, ButtonBorder);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse;
        bool visible = ImGuiNET.ImGui.Begin("##radar_settings", ref _settingsOpen, flags);
        ImGuiNET.ImGui.PopStyleVar(3);
        ImGuiNET.ImGui.PopStyleColor(2);
        if (visible)
        {
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui11));
            ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
            ImGuiNET.ImGui.TextUnformatted("Radar Settings");
            ImGuiNET.ImGui.PopFont();
            ImGuiNET.ImGui.Separator();

            bool changed = false;
            changed |= Check("Rotate with player", ref RadarSettingsStore.RotateWithPlayer);
            changed |= Check("Show gold rim", ref RadarSettingsStore.ShowGoldRim);
            changed |= Check("Docked click-through (hold Ctrl to interact)", ref RadarSettingsStore.CtrlGatedClickThrough);
            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip("On: docked clicks pass through to the game; hold Ctrl to drag, open the gear, or resize.");
            changed |= Check("Click a dot to select it", ref RadarSettingsStore.ClickToSelect);
            if (ImGuiNET.ImGui.IsItemHovered())
                ImGuiNET.ImGui.SetTooltip("Left-click a dot to select that object in game. Hover a dot for its name and distance.");
            changed |= Slider("Opacity", ref RadarSettingsStore.Opacity, 0.05f, 1.0f, "%.2f");
            changed |= Slider("Zoom", ref RadarSettingsStore.Zoom, 0.5f, 6.0f, "%.1f");
            ImGuiNET.ImGui.Separator();
            changed |= KindRows();
            if (changed)
            {
                AvaloniaOverlay.RadarCtrlGatedClickThrough = RadarSettingsStore.CtrlGatedClickThrough;
                RadarSettingsStore.Save();
            }
            ImGuiNET.ImGui.PopFont();
        }
        ImGuiNET.ImGui.End();
    }

    private static bool Check(string label, ref bool value) => ImGuiNET.ImGui.Checkbox(label, ref value);

    // Legend order: creatures, then corpses, then places, then items.
    private static readonly (byte Kind, string Label)[] KindRowsOrder =
    {
        (RadarKind.Monster, "Monsters"), (RadarKind.Player, "Players"), (RadarKind.Fellow, "Fellows"),
        (RadarKind.Npc, "NPCs"), (RadarKind.Vendor, "Vendors"), (RadarKind.Pet, "Your pets"),
        (RadarKind.Corpse, "Corpses"), (RadarKind.OwnCorpse, "Your corpses"), (RadarKind.Lifestone, "Lifestones"),
        (RadarKind.Portal, "Portals"), (RadarKind.Door, "Doors"), (RadarKind.GroundItem, "Items on the ground"),
    };

    private static readonly string[] KindTips =
    {
        "Creatures you can attack.",
        "Creatures you can't attack (townsfolk, quest givers).",
        "Portals, with their destination.",
        "Doors, gates and hatches.",
        "Other players.",
        "Players in your fellowship.",
        "Your own combat pet.",
        "Shopkeepers.",
        "Corpses you can't loot (or not identified yet: selecting one tells).",
        "Corpses you may loot: your kills, your fellows' kills and your own corpse. Known once the corpse has been identified (looting or selecting it does that).",
        "Lifestones.",
        "Items lying on the ground. Off by default: there can be a lot of them.",
    };

    /// <summary>
    /// The legend and per-kind settings: show box, the marker as drawn, its name and a colour
    /// swatch (click to pick). One row per kind, then "Reset colours". True when something changed.
    /// </summary>
    private static bool KindRows()
    {
        bool changed = false;
        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.UiBold11));
        ImGuiNET.ImGui.TextUnformatted("Show (click a swatch to change its colour):");
        ImGuiNET.ImGui.PopFont();
        RadarMarkerStyle.FillColors(KindCols);
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float scale = MathF.Max(1f, EngineFrameController.FontScale);
        const ImGuiColorEditFlags pickFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel
            | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf;
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 3));
        foreach (var (kind, label) in KindRowsOrder)
        {
            ImGuiNET.ImGui.PushID(kind);
            changed |= ImGuiNET.ImGui.Checkbox("##show", ref RadarSettingsStore.ShowFlag(kind));
            if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(KindTips[kind]);
            ImGuiNET.ImGui.SameLine();
            // The marker exactly as the radar draws it.
            float box = ImGuiNET.ImGui.GetFrameHeight();
            Vector2 at = ImGuiNET.ImGui.GetCursorScreenPos();
            ImGuiNET.ImGui.Dummy(new Vector2(box, box));
            RadarMarkerStyle.Draw(dl, kind, at + new Vector2(box, box) * 0.5f, KindCols[kind], 1.3f * scale);
            ImGuiNET.ImGui.SameLine();
            ImGuiNET.ImGui.AlignTextToFramePadding();
            ImGuiNET.ImGui.TextUnformatted(label);
            if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(KindTips[kind]);
            ImGuiNET.ImGui.SameLine(190 * scale);
            Vector4 v = RynthTheme.Vec(RadarSettingsStore.KindColors[kind]);
            if (ImGuiNET.ImGui.ColorEdit4("##col", ref v, pickFlags))
            {
                RadarSettingsStore.KindColors[kind] = ToArgb(v);
                _colorDirty = true;
            }
            ImGuiNET.ImGui.PopID();
        }
        ImGuiNET.ImGui.PopStyleVar();
        // The picker reports every drag step: save once no colour edit is in progress.
        if (_colorDirty && !ImGuiNET.ImGui.IsAnyItemActive())
        {
            _colorDirty = false;
            changed = true;
        }
        if (ImGuiNET.ImGui.SmallButton("Reset colours"))
        {
            RadarSettingsStore.ResetKindColors();
            changed = true;
        }
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip("Every kind back to its default colour (show boxes stay as they are).");
        return changed;
    }

    private static bool _colorDirty;

    private static uint ToArgb(Vector4 v)
    {
        static uint B(float f) => (uint)Math.Clamp((int)MathF.Round(f * 255f), 0, 255);
        return (B(v.W) << 24) | (B(v.X) << 16) | (B(v.Y) << 8) | B(v.Z);
    }

    private static bool Slider(string label, ref float value, float min, float max, string fmt)
    {
        ImGuiNET.ImGui.TextUnformatted(label);
        ImGuiNET.ImGui.SameLine(60);
        ImGuiNET.ImGui.SetNextItemWidth(170);
        ImGuiNET.ImGui.PushID(label);
        bool changed = ImGuiNET.ImGui.SliderFloat("##v", ref value, min, max, fmt);
        ImGuiNET.ImGui.PopID();
        // Save once the drag ends, not on every step.
        return changed && !ImGuiNET.ImGui.IsItemActive() || ImGuiNET.ImGui.IsItemDeactivatedAfterEdit();
    }
}
