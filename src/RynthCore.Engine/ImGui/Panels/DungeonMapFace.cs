// ============================================================================
//  RynthCore.Engine - ImGui/Panels/DungeonMapFace.cs
//  The Dungeon Map: a large, pannable map of the current dungeon. It replaces
//  RynthAi's plugin-drawn DungeonMapUi window (plugin ImGui rendering is no
//  longer called; RYNTHCORE_FORCE_IMGUI only) and is opened from the RynthAi
//  dashboard's Map launcher.
//
//  Data: UiSources.Radar, the same snapshot the Radar face draws
//  (RynthPluginGetRadarSnapshot): every floor's walls and fill strips, the
//  visited cells, portal / door / creature markers with labels, the player pose.
//  The plugin's D3D9 floor textures are not used; floors are strips and walls
//  are lines, drawn without anti-aliasing and culled to the canvas.
//
//  Like the plugin window it replaces:
//    - toolbar: 1U1D (current floor +-1), F1..Fn floor toggles, All, Here, zoom,
//      Follow, Rotate, Reset, Doors, Creatures, opacity; it can be hidden;
//    - markers: the radar's kinds (RadarKind); the kinds added 2026-10-04 use the
//      radar's shapes and colours (RadarMarkerStyle) and its per-kind show boxes;
//    - drag pans (and turns Follow off), the wheel zooms;
//    - portals always shown with their destination; doors and creatures optional;
//    - hidden while you're outdoors or in portal space, back in the next dungeon
//      (the panel stays open; ImGuiPanelHost skips drawing it docked).
//  Settings are the engine's (dungeon_map_settings.txt beside radar_settings.txt),
//  not RynthAi's per-character Map* fields. Zoom, pan and floor picks last for the
//  session, as they did.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class DungeonMapFace : IImGuiPanel
{
    public const string Title = "Dungeon Map";

    public static void Register()
    {
        DungeonMapSettingsStore.Load(); // engine init thread: the file read stays off AC's thread
        ImGuiPanelHost.Register(Title,
            new PanelSpec(new Vector2(480, 520), new Vector2(240, 200), Background: 0, EdgeToEdge: true),
            () => new DungeonMapFace());
    }

    // ── Palette (the plugin map's colours, ARGB) ──────────────────────────
    private const uint CanvasBgArgb = 0xFF0A121F, ToolbarBgArgb = 0xE00A121A;
    private static readonly uint CanvasBorder = C(0xFF334D66), Wall = C(0xFF8CCCFF),
        PlayerCol = C(0xFF33FF66), Portal = C(0xFFE673FF), PortalRing = C(0xB3FFBFFF), PortalLabel = C(0xFFFFE0FF),
        Door = C(0xFFBF8C40), DoorRing = C(0xB3E6BF80), DoorLabel = C(0xFFF2D9A6),
        Monster = C(0xFFFF4033), MonsterRing = C(0xB3FF8C80), Npc = C(0xFFFFD933), NpcRing = C(0xB3FFF28C),
        CreatureLabel = C(0xFFFFE6E6), Hint = C(0xFF8FA3B8), ButtonBg = C(0xC0141F29), ButtonBorder = C(0xFF34587A),
        FloorOn = C(0xFF2673B3), FloorHere = C(0xFF1AA64D);
    private const uint FillFlat = 0x662E8C38, FillUp = 0x668C1F14, FillDown = 0x661A8026, FillVisited = 0xB84D6B94;
    private static uint C(uint argb) => RynthTheme.Argb(argb);

    /// <summary>ARGB colour with its alpha scaled, as an ImGui colour.</summary>
    private static uint MulAlpha(uint argb, float k) =>
        RynthTheme.Argb((argb & 0x00FFFFFF) | ((uint)Math.Clamp((int)MathF.Round((argb >> 24) * k), 0, 255) << 24));

    private const float DimOtherFloors = 0.25f;   // the plugin map's DimCol
    private const float DefaultZoom = 5f, MinZoom = 1f, MaxZoom = 20f;

    // ── Session state (per open) ─────────────────────────────────────────
    private float _zoom = DefaultZoom;
    private Vector2 _pan;
    private bool _follow = true;
    private bool _oneUpOneDown = true;
    private readonly HashSet<int> _visible = new();
    private int _autoLayer = -1;
    private uint _landblock;
    private int[] _drawOrder = Array.Empty<int>();

    public int PopOutIdleHz => 20;   // markers move

    public bool HiddenWhileDocked
    {
        get
        {
            RadarView? view = UiSources.Radar.Current?.Value;
            return view != null && !view.Live.IsIndoor;
        }
    }

    public void OnShown()
    {
        DungeonMapSettingsStore.Load();
        UiSources.Radar.Subscribe();
        UiSources.Radar.RequestRefresh();
    }

    public void OnHidden() => UiSources.Radar.Unsubscribe();

    public void Draw()
    {
        Vector2 origin = ImGuiNET.ImGui.GetCursorScreenPos();
        Vector2 size = ImGuiNET.ImGui.GetContentRegionAvail();
        if (size.X < 16 || size.Y < 16) return;
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        RadarView? view = UiSources.Radar.Current?.Value;

        ImGuiNET.ImGui.PushFont(ImGuiFonts.Get(UiFont.Ui11));
        try
        {
            if (view == null) { Message(dl, origin, size, "Waiting for map data from RynthAi..."); return; }
            RadarSnapshot live = view.Live;
            if (!live.IsIndoor) { Message(dl, origin, size, "Outdoors: the map shows while you're in a dungeon."); return; }

            int layers = live.LayerZs.Count;
            TrackLayers(view, live.Player.Landblock, layers);

            // ── Toolbar ──
            float top = origin.Y;
            if (DungeonMapSettingsStore.ShowToolbar)
            {
                float barH = Toolbar(dl, origin, size.X, view, layers);
                top += barH;
            }

            // ── Canvas ──
            var canvasMin = new Vector2(origin.X, top);
            var canvasMax = origin + size;
            if (canvasMax.Y - canvasMin.Y < 24) return;
            dl.AddRectFilled(canvasMin, canvasMax, MulAlpha(CanvasBgArgb, Math.Clamp(DungeonMapSettingsStore.BgOpacity, 0f, 1f)));
            dl.AddRect(canvasMin, canvasMax, CanvasBorder);

            if (_follow) _pan = Vector2.Zero;
            Vector2 centre = (canvasMin + canvasMax) * 0.5f + _pan;

            if (layers == 0)
                DrawCentredText(dl, (canvasMin + canvasMax) * 0.5f, Hint,
                    $"Loading dungeon geometry for {live.Player.Landblock:X4}...");
            else
            {
                ImDrawListFlags saved = dl.Flags;
                dl.Flags = saved & ~(ImDrawListFlags.AntiAliasedLines | ImDrawListFlags.AntiAliasedLinesUseTex | ImDrawListFlags.AntiAliasedFill);
                dl.PushClipRect(canvasMin, canvasMax, true);
                var proj = new Projection(centre, live.Player, DungeonMapSettingsStore.RotateWithPlayer, _zoom);
                DrawFloors(dl, view, proj, canvasMin, canvasMax);
                dl.Flags = saved;
                DrawMarkers(dl, live, proj, canvasMin, canvasMax);
                DrawPlayer(dl, centre, proj);
                dl.PopClipRect();
            }

            // Show-toolbar button over the canvas corner (submitted before the surface so it wins the hover).
            if (!DungeonMapSettingsStore.ShowToolbar)
            {
                if (IconButton("##dmap_showbar", canvasMin + new Vector2(4, 4), PhosphorIcons.CaretDown))
                {
                    DungeonMapSettingsStore.ShowToolbar = true;
                    DungeonMapSettingsStore.Save();
                }
                ImGuiNET.ImGui.SetItemTooltip("Show the map settings");
            }

            CanvasInput(canvasMin, canvasMax);
        }
        finally
        {
            ImGuiNET.ImGui.PopFont();
        }
    }

    // ── Floors ────────────────────────────────────────────────────────────

    /// <summary>Follows the player's floor: 1U1D keeps current +-1, Follow alone keeps just the current one.</summary>
    private void TrackLayers(RadarView view, uint landblock, int layers)
    {
        if (landblock != _landblock)
        {
            _landblock = landblock;
            _pan = Vector2.Zero;
            _autoLayer = -1;
            _visible.Clear();
        }
        int best = view.CurrentLayer;
        if (best < 0 || best == _autoLayer) return;
        _autoLayer = best;
        if (_oneUpOneDown) SetOneUpOneDown(layers);
        else if (_follow || _visible.Count == 0) { _visible.Clear(); _visible.Add(best); }
    }

    private void SetOneUpOneDown(int layers)
    {
        _visible.Clear();
        if (_autoLayer < 0) return;
        if (_autoLayer > 0) _visible.Add(_autoLayer - 1);
        _visible.Add(_autoLayer);
        if (_autoLayer < layers - 1) _visible.Add(_autoLayer + 1);
    }

    private void DrawFloors(ImDrawListPtr dl, RadarView view, in Projection proj, Vector2 clipMin, Vector2 clipMax)
    {
        int layers = view.Live.LayerZs.Count;
        int cur = view.CurrentLayer;
        // Other floors first, the player's floor on top.
        if (_drawOrder.Length < layers) _drawOrder = new int[layers];
        int n = 0;
        foreach (int i in _visible) if (i >= 0 && i < layers && i != cur) _drawOrder[n++] = i;
        bool curVisible = cur >= 0 && _visible.Contains(cur);
        if (curVisible) _drawOrder[n++] = cur;

        // Fill strips: 5 floats (x0, y0, x1, y1, type).
        for (int k = 0; k < n; k++)
        {
            int layer = _drawOrder[k];
            if (view.FillsByLayer[layer] is not float[] strips) continue;
            float a = layer == cur ? 1f : DimOtherFloors;
            uint flat = MulAlpha(FillFlat, a), up = MulAlpha(FillUp, a), down = MulAlpha(FillDown, a);
            for (int i = 0; i + 4 < strips.Length; i += 5)
            {
                int type = (int)strips[i + 4];
                FillRect(dl, proj, strips[i], strips[i + 1], strips[i + 2], strips[i + 3],
                    type == 1 ? up : type == 2 ? down : flat, clipMin, clipMax);
            }
        }

        // Visited cells, the player's floor only: 4 floats per strip.
        if (curVisible && view.VisitedByLayer[cur] is float[] visited)
        {
            uint fill = RynthTheme.Argb(FillVisited);
            for (int i = 0; i + 3 < visited.Length; i += 4)
                FillRect(dl, proj, visited[i], visited[i + 1], visited[i + 2], visited[i + 3], fill, clipMin, clipMax);
        }

        // Walls: 6 floats (ax, ay, bx, by, cell x, cell y).
        for (int k = 0; k < n; k++)
        {
            int layer = _drawOrder[k];
            if (view.WallsByLayer[layer] is not float[] segs) continue;
            bool current = layer == cur;
            uint col = current ? Wall : MulAlpha(0xFF8CCCFF, DimOtherFloors);
            float thick = current ? 1.5f : 1f;
            for (int i = 0; i + 5 < segs.Length; i += 6)
            {
                Vector2 pa = proj.P(segs[i], segs[i + 1]);
                Vector2 pb = proj.P(segs[i + 2], segs[i + 3]);
                if (MathF.Max(pa.X, pb.X) < clipMin.X || MathF.Min(pa.X, pb.X) > clipMax.X ||
                    MathF.Max(pa.Y, pb.Y) < clipMin.Y || MathF.Min(pa.Y, pb.Y) > clipMax.Y) continue;
                dl.AddLine(pa, pb, col, thick);
            }
        }
    }

    private static void FillRect(ImDrawListPtr dl, in Projection proj, float x0, float y0, float x1, float y1,
        uint col, Vector2 clipMin, Vector2 clipMax)
    {
        Vector2 p00 = proj.P(x0, y0), p11 = proj.P(x1, y1);
        if (proj.Rotated)
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

    // ── Markers and player ────────────────────────────────────────────────

    private static void DrawMarkers(ImDrawListPtr dl, RadarSnapshot live, in Projection proj, Vector2 clipMin, Vector2 clipMax)
    {
        const float Margin = 20f;
        bool doors = DungeonMapSettingsStore.ShowDoors, creatures = DungeonMapSettingsStore.ShowCreatures;
        RadarMarkerStyle.FillColors(KindCols);
        foreach (RadarMarker m in live.Markers)
        {
            if (!ShowOnMap(m.Kind, doors, creatures)) continue;
            Vector2 p = proj.P(m.X, m.Y);
            if (p.X < clipMin.X - Margin || p.X > clipMax.X + Margin || p.Y < clipMin.Y - Margin || p.Y > clipMax.Y + Margin) continue;
            switch (m.Kind)
            {
                case 0:
                    dl.AddCircleFilled(p, 4f, Monster, 10);
                    dl.AddCircle(p, 6f, MonsterRing, 10, 1f);
                    Label(dl, p + new Vector2(7, -7), CreatureLabel, m.Label, 20);
                    break;
                case 1:
                    dl.AddCircleFilled(p, 4f, Npc, 10);
                    dl.AddCircle(p, 6f, NpcRing, 10, 1.2f);
                    Label(dl, p + new Vector2(7, -7), CreatureLabel, m.Label, 20);
                    break;
                case 2:
                    dl.AddCircleFilled(p, 5f, Portal, 12);
                    dl.AddCircle(p, 7f, PortalRing, 12, 1.5f);
                    Label(dl, p + new Vector2(9, -7), PortalLabel, m.Label, 22);
                    break;
                case 3:
                    const float r = 5f, o = r + 1.5f;
                    dl.AddQuadFilled(p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0), Door);
                    dl.AddQuad(p + new Vector2(0, -o), p + new Vector2(o, 0), p + new Vector2(0, o), p + new Vector2(-o, 0), DoorRing, 1.2f);
                    Label(dl, p + new Vector2(8, -7), DoorLabel, m.Label, 20);
                    break;
                default:
                    // The kinds added 2026-10-04: the radar's shape and (user-pickable) colour, a size up.
                    RadarMarkerStyle.Draw(dl, m.Kind, p, KindCols[m.Kind], 1.35f);
                    if (m.Kind is not RadarKind.GroundItem)
                        Label(dl, p + new Vector2(8, -7), CreatureLabel, m.Label, 20);
                    break;
            }
        }
    }

    private static readonly uint[] KindCols = new uint[RadarKind.Count];

    /// <summary>
    /// Portals always; doors with the map's Doors box; monsters and NPCs with its Creatures box.
    /// The kinds added 2026-10-04 also follow the radar's show box for that kind (so items on the
    /// ground stay off unless turned on there): players, fellows, pets and vendors with Creatures,
    /// corpses, lifestones and ground items on the radar's box alone. Unknown kinds: hidden.
    /// </summary>
    private static bool ShowOnMap(byte kind, bool doors, bool creatures) => kind switch
    {
        RadarKind.Portal => true,
        RadarKind.Door => doors,
        RadarKind.Monster or RadarKind.Npc => creatures,
        RadarKind.Player or RadarKind.Fellow or RadarKind.Pet or RadarKind.Vendor =>
            creatures && UI.Panels.RadarSettingsStore.ShowKind(kind),
        _ => UI.Panels.RadarSettingsStore.ShowKind(kind),
    };

    private static void Label(ImDrawListPtr dl, Vector2 pos, uint col, string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > max) text = text[..max] + "…";
        dl.AddText(pos, col, text);
    }

    private static void DrawPlayer(ImDrawListPtr dl, Vector2 at, in Projection proj)
    {
        dl.AddCircleFilled(at, 4f, PlayerCol, 10);
        // Rotating: facing is always up. North-up: AC heading 0 = east, 90 = north.
        Vector2 tip = proj.Rotated ? at + new Vector2(0, -10) : at + new Vector2(proj.Cos * 10, -proj.Sin * 10);
        dl.AddLine(at, tip, PlayerCol, 1.5f);
    }

    /// <summary>World (AC x east, y north) to screen around the player, north-up or rotated so facing is up.</summary>
    private readonly struct Projection
    {
        private readonly float _cx, _cy, _pwx, _pwy, _sin, _cos, _zoom;
        public readonly bool Rotated;

        public Projection(Vector2 playerAt, RadarPlayer p, bool rotate, float zoom)
        {
            _cx = playerAt.X; _cy = playerAt.Y; _pwx = p.WorldX; _pwy = p.WorldY;
            float h = p.Heading * MathF.PI / 180f;
            _sin = MathF.Sin(h); _cos = MathF.Cos(h);
            Rotated = rotate; _zoom = zoom;
        }

        public float Sin => _sin;
        public float Cos => _cos;

        public Vector2 P(float wx, float wy)
        {
            float dx = wx - _pwx, dy = wy - _pwy;
            if (Rotated)
            {
                float right = _sin * dx - _cos * dy;
                float forward = _cos * dx + _sin * dy;
                return new Vector2(_cx + right * _zoom, _cy - forward * _zoom);
            }
            return new Vector2(_cx + dx * _zoom, _cy - dy * _zoom);
        }
    }

    // ── Canvas input ──────────────────────────────────────────────────────

    private void CanvasInput(Vector2 canvasMin, Vector2 canvasMax)
    {
        ImGuiNET.ImGui.SetCursorScreenPos(canvasMin);
        ImGuiNET.ImGui.InvisibleButton("##dmap_canvas", Vector2.Max(canvasMax - canvasMin, new Vector2(1, 1)));
        if (ImGuiNET.ImGui.IsItemActive() && ImGuiNET.ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            Vector2 delta = ImGuiNET.ImGui.GetIO().MouseDelta;
            if (float.IsFinite(delta.X) && float.IsFinite(delta.Y) && delta != Vector2.Zero)
            {
                _follow = false;
                _pan += delta;
            }
        }
        if (ImGuiNET.ImGui.IsItemHovered())
        {
            float wheel = ImGuiNET.ImGui.GetIO().MouseWheel;
            if (float.IsFinite(wheel) && wheel != 0f)
                _zoom = Math.Clamp(_zoom + wheel * 0.5f, MinZoom, MaxZoom);
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────────────

    /// <summary>Draws the settings rows (they wrap to the width). Returns their height.</summary>
    private float Toolbar(ImDrawListPtr dl, Vector2 origin, float width, RadarView view, int layers)
    {
        const float pad = 4;
        // Background first, sized once the rows are laid out: a channel split keeps it underneath.
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGuiNET.ImGui.SetCursorScreenPos(origin + new Vector2(pad, pad));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 1));
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));
        float limit = origin.X + width - pad;
        bool changed = false;

        if (IconButton("##dmap_hidebar", ImGuiNET.ImGui.GetCursorScreenPos(), PhosphorIcons.CaretUp))
        {
            DungeonMapSettingsStore.ShowToolbar = false;
            changed = true;
        }
        ImGuiNET.ImGui.SetItemTooltip("Hide the map settings");

        // 1U1D and the floors.
        SameLineIfFits(40, limit);
        if (ToggleButton("1U1D##dmap_1u1d", _oneUpOneDown, FloorOn))
        {
            _oneUpOneDown = !_oneUpOneDown;
            if (_oneUpOneDown) SetOneUpOneDown(layers);
        }
        ImGuiNET.ImGui.SetItemTooltip("Show your floor and the ones just above and below it");
        if (layers > 1)
        {
            SameLineIfFits(ImGuiNET.ImGui.CalcTextSize("Floors:").X, limit);
            ImGuiNET.ImGui.AlignTextToFramePadding();
            ImGuiNET.ImGui.TextUnformatted("Floors:");
            for (int i = 0; i < layers; i++)
            {
                string label = FloorLabel(i);
                SameLineIfFits(ImGuiNET.ImGui.CalcTextSize(label).X + 8, limit, 2);
                bool on = _visible.Contains(i);
                if (ToggleButton(label, on, i == view.CurrentLayer ? FloorHere : FloorOn))
                {
                    _oneUpOneDown = false;
                    if (on) _visible.Remove(i); else _visible.Add(i);
                }
            }
            SameLineIfFits(24, limit);
            if (ImGuiNET.ImGui.SmallButton("All##dmap_all"))
            {
                _oneUpOneDown = false;
                _visible.Clear();
                for (int i = 0; i < layers; i++) _visible.Add(i);
            }
            SameLineIfFits(34, limit, 2);
            if (ImGuiNET.ImGui.SmallButton("Here##dmap_here"))
            {
                _oneUpOneDown = false;
                _visible.Clear();
                if (view.CurrentLayer >= 0) _visible.Add(view.CurrentLayer);
            }
        }

        // Zoom.
        SameLineIfFits(130, limit, 12);
        ImGuiNET.ImGui.AlignTextToFramePadding();
        ImGuiNET.ImGui.TextUnformatted("Zoom:");
        ImGuiNET.ImGui.SameLine();
        ImGuiNET.ImGui.SetNextItemWidth(80);
        ImGuiNET.ImGui.SliderFloat("##dmap_zoom", ref _zoom, MinZoom, MaxZoom, "%.1fx");
        if (!float.IsFinite(_zoom)) _zoom = DefaultZoom;

        // Follow / Rotate / Reset.
        SameLineIfFits(190, limit, 12);
        ImGuiNET.ImGui.Checkbox("Follow##dmap_follow", ref _follow);
        ImGuiNET.ImGui.SetItemTooltip("Keep the map centred on you (dragging the map turns this off)");
        ImGuiNET.ImGui.SameLine();
        changed |= ImGuiNET.ImGui.Checkbox("Rotate##dmap_rotate", ref DungeonMapSettingsStore.RotateWithPlayer);
        ImGuiNET.ImGui.SetItemTooltip("Turn the map so the way you face is up");
        ImGuiNET.ImGui.SameLine();
        if (ImGuiNET.ImGui.SmallButton("Reset##dmap_reset"))
        {
            _pan = Vector2.Zero;
            _zoom = DefaultZoom;
            _follow = true;
            _oneUpOneDown = true;
            SetOneUpOneDown(layers);
        }

        // Doors / Creatures.
        SameLineIfFits(160, limit, 12);
        changed |= ImGuiNET.ImGui.Checkbox("Doors##dmap_doors", ref DungeonMapSettingsStore.ShowDoors);
        ImGuiNET.ImGui.SameLine();
        changed |= ImGuiNET.ImGui.Checkbox("Creatures##dmap_creatures", ref DungeonMapSettingsStore.ShowCreatures);
        ImGuiNET.ImGui.SetItemTooltip("Monsters, NPCs, players, fellows, your pet and vendors. Players, fellows, pets, vendors, corpses, lifestones and ground items also follow their box in the Radar's settings.");

        // Opacity (of the map's background).
        SameLineIfFits(130, limit, 12);
        ImGuiNET.ImGui.AlignTextToFramePadding();
        ImGuiNET.ImGui.TextUnformatted("Opacity:");
        ImGuiNET.ImGui.SameLine(0, 4);
        ImGuiNET.ImGui.SetNextItemWidth(70);
        if (ImGuiNET.ImGui.SliderFloat("##dmap_opacity", ref DungeonMapSettingsStore.BgOpacity, 0f, 1f, "%.2f"))
            DungeonMapSettingsStore.BgOpacity = Math.Clamp(DungeonMapSettingsStore.BgOpacity, 0f, 1f);
        // Save once the drag ends, not on every step.
        if (ImGuiNET.ImGui.IsItemDeactivatedAfterEdit()) changed = true;

        ImGuiNET.ImGui.PopStyleVar(2);
        float bottom = ImGuiNET.ImGui.GetItemRectMax().Y + pad;
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(origin, new Vector2(origin.X + width, bottom), C(ToolbarBgArgb));
        dl.ChannelsMerge();

        if (changed) DungeonMapSettingsStore.Save();
        return bottom - origin.Y;
    }

    private static readonly List<string> FloorLabels = new();

    private static string FloorLabel(int i)
    {
        while (FloorLabels.Count <= i) FloorLabels.Add("F" + (FloorLabels.Count + 1) + "##dmap_floor" + FloorLabels.Count);
        return FloorLabels[i];
    }

    /// <summary>Keeps the next item on this row when it fits before <paramref name="limit"/>, else starts a new row.</summary>
    private static void SameLineIfFits(float nextWidth, float limit, float gap = 8)
    {
        if (ImGuiNET.ImGui.GetItemRectMax().X + gap + nextWidth <= limit)
            ImGuiNET.ImGui.SameLine(0, gap);
    }

    private static bool ToggleButton(string label, bool on, uint onColour)
    {
        if (on)
        {
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Button, onColour);
            ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonHovered, onColour);
        }
        bool clicked = ImGuiNET.ImGui.SmallButton(label);
        if (on) ImGuiNET.ImGui.PopStyleColor(2);
        return clicked;
    }

    private static bool IconButton(string id, Vector2 pos, string glyph)
    {
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        var size = new Vector2(18, 18);
        ImGuiNET.ImGui.SetCursorScreenPos(pos);
        bool clicked = ImGuiNET.ImGui.InvisibleButton(id, size);
        uint bg = ImGuiNET.ImGui.IsItemActive() ? C(0xE0101820) : ImGuiNET.ImGui.IsItemHovered() ? C(0xE0243444) : ButtonBg;
        dl.AddRectFilled(pos, pos + size, bg, 3);
        dl.AddRect(pos, pos + size, ButtonBorder, 3);
        PhosphorIcons.DrawCentered(dl, ImGuiFonts.Get(UiFont.Ui11), glyph, pos, size, C(0xFFF2F8FF));
        return clicked;
    }

    private static void Message(ImDrawListPtr dl, Vector2 origin, Vector2 size, string text)
    {
        dl.AddRectFilled(origin, origin + size, MulAlpha(CanvasBgArgb, Math.Clamp(DungeonMapSettingsStore.BgOpacity, 0.6f, 1f)));
        DrawCentredText(dl, origin + size * 0.5f, Hint, text);
    }

    private static void DrawCentredText(ImDrawListPtr dl, Vector2 centre, uint col, string text)
    {
        Vector2 ts = ImGuiNET.ImGui.CalcTextSize(text);
        dl.AddText(centre - ts * 0.5f, col, text);
    }
}

/// <summary>
/// The Dungeon Map's settings (the plugin map's MapShowDoors / MapShowCreatures /
/// MapShowToolbar / MapBgOpacity / MapRotateWithPlayer, same defaults). Engine-wide,
/// in %LOCALAPPDATA%\RynthCore\dungeon_map_settings.txt, like radar_settings.txt.
/// </summary>
internal static class DungeonMapSettingsStore
{
    private static readonly object _sync = new();
    private static bool _loaded;

    public static bool ShowDoors = true;
    public static bool ShowCreatures = true;
    public static bool ShowToolbar = true;
    public static float BgOpacity = 1.0f;
    public static bool RotateWithPlayer;

    private static string FilePath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "RynthCore", "dungeon_map_settings.txt");
        }
    }

    public static void Load()
    {
        lock (_sync)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line[..eq].Trim();
                    string val = line[(eq + 1)..].Trim();
                    switch (key)
                    {
                        case "showDoors":     ShowDoors = val == "1"; break;
                        case "showCreatures": ShowCreatures = val == "1"; break;
                        case "showToolbar":   ShowToolbar = val == "1"; break;
                        case "rotate":        RotateWithPlayer = val == "1"; break;
                        case "opacity":
                            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float o))
                                BgOpacity = Math.Clamp(o, 0f, 1f);
                            break;
                    }
                }
            }
            catch { /* best-effort: defaults already in place */ }
        }
    }

    /// <summary>Writes the settings in the background (the caller is AC's render thread).</summary>
    public static void Save()
    {
        // Snapshot on the caller's thread: the writer runs later.
        string text = "# RynthCore dungeon map settings - auto-generated, hand-edits OK.\n"
            + $"showDoors={(ShowDoors ? "1" : "0")}\n"
            + $"showCreatures={(ShowCreatures ? "1" : "0")}\n"
            + $"showToolbar={(ShowToolbar ? "1" : "0")}\n"
            + $"rotate={(RotateWithPlayer ? "1" : "0")}\n"
            + FormattableString.Invariant($"opacity={BgOpacity:0.##}\n");
        UiBackgroundWriter.Enqueue("dungeon map settings", () =>
        {
            lock (_sync)
            {
                try
                {
                    string path = FilePath;
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(path, text);
                }
                catch { /* non-fatal */ }
            }
        });
    }
}
