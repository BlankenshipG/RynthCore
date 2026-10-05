// ============================================================================
//  RynthCore.Engine - ImGui/MonsterHud/PlayerPlate.cs
//  The player's own plate: three thin stacked bars over your head (health red,
//  stamina amber, mana blue) in the monster plates' style (edge, shine, the
//  chip that drains after a drop); optional "cur/max" in each bar and the
//  name above. No name and no numbers by default, to stay compact.
//
//  Data: PlayerVitalsHooks.TryGetSnapshot, the buffed values AC's own vital
//  bars show, kept on AC's main thread (a lock and a struct copy per frame).
//
//  Two placements (MonsterHudSettings.SelfPlacement):
//    Fixed on screen (default since 2026-10-05: tied to the head the plate
//      jittered while the character moved). The bars sit at a saved screen
//      spot (fractions of the screen, so a resize keeps it in place, then
//      clamped inside the screen). Drawn by DrawScreen into ImGui's
//      background list, i.e. with the normal UI (over AC's windows, under
//      every ImGui panel), not through the under-UI 3D layer: a screen HUD
//      element doesn't belong to the world. "Lock position" (on by default)
//      makes it untouchable; unlocked, a frame shows and a left-drag anywhere
//      on the plate moves it (WantsMouse keeps that press from AC), saved on
//      release. Reset puts it back at the default spot.
//    Follow character: the player's live pose, projected like the monster
//      plates (MonsterHud.Project handles the landblock frame), above the
//      head, under the feet or beside the character; drawn by Draw into the
//      monster plates' list (UnderUiLayer).
//
//  First person (follow mode): when the camera is inside or right at the
//  head, the plate would sit in the middle of the screen, so it hides
//  (setting; on by default). The head point is published either way, for the
//  gain text.
//
//  Render thread only. Engine-owned statics.
// ============================================================================

using System;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.D3D9;

namespace RynthCore.Engine.ImGuiBackend.Hud;

internal static class PlayerPlate
{
    private const float HeadHeight = 2.05f;          // metres above the feet (human-sized characters)
    private const float FirstPersonDistance = 1.3f;  // camera this close to the head = first person

    private static readonly uint HealthCol = MonsterHud.C(0xFFE04A4A), StaminaCol = MonsterHud.C(0xFFE8B333),
        ManaCol = MonsterHud.C(0xFF3F8FE8), NameCol = MonsterHud.C(0xFFF2E8DC), NumCol = MonsterHud.C(0xFFFFFFFF);

    private struct BarState
    {
        public float Shown, Trail;
        public long Hold;
        public bool Init;
        public uint Cur, Max;
        public string? Text;
        public long TextKey;
    }

    private static BarState _hp, _st, _mn;
    private static float _alpha;

    // Fixed mode: dragging (unlocked only).
    private static bool _hover, _dragging, _wantsMouse;
    private static Vector2 _grab;        // cursor minus the bars' centre when the drag began

    /// <summary>
    /// Fixed mode, unlocked: the cursor is on the plate (or a drag is held), so the click is the plate's.
    /// MonsterHud.WantsMouse includes it. Render thread.
    /// </summary>
    public static bool WantsMouse => _wantsMouse;

    /// <summary>Where the head is on screen this frame (valid when <see cref="HeadOnScreen"/>).</summary>
    public static Vector2 HeadScreen { get; private set; }
    public static bool HeadOnScreen { get; private set; }
    /// <summary>Top of the drawn plate (or the head point when it isn't drawn): where the gain text starts.</summary>
    public static float PlateTop { get; private set; }
    /// <summary>The camera is at the head (first person) this frame.</summary>
    public static bool FirstPerson { get; private set; }

    public static void Reset()
    {
        _hp = default; _st = default; _mn = default;
        _alpha = 0f;
        HeadOnScreen = false;
        FirstPerson = false;
        DropDrag();
    }

    /// <summary>No fixed plate this frame: no hover, no drag, no capture.</summary>
    public static void DropDrag()
    {
        _hover = _dragging = _wantsMouse = false;
    }

    /// <summary>Clears last frame's capture before a frame (Update); DrawScreen sets it again.</summary>
    public static void ClearMouse() => _wantsMouse = false;

    public static void Draw(ref MonsterHud.Frame f)
    {
        // Head point (AC landblock-local), used by the gain text even when the plate is off.
        float hz = f.Pz + HeadHeight + MonsterHudSettings.HeightLift;
        FirstPerson = IsFirstPerson(ref f);
        HeadOnScreen = MonsterHud.Project(ref f, f.PlayerCell, f.Px, f.Py, hz, out Vector2 head, keepOnScreen: true) &&
                       head.X > -40 && head.X < f.Display.X + 40 && head.Y > -40 && head.Y < f.Display.Y + 40;
        HeadScreen = head;
        PlateTop = head.Y;
        // Fixed on screen: DrawScreen paints it with the normal UI (gain text stays over the head).
        if (MonsterHudSettings.SelfPlacement == SelfPlacement.Fixed) return;

        bool show = MonsterHudSettings.SelfPlate && HeadOnScreen && !(FirstPerson && MonsterHudSettings.SelfHideFirstPerson) &&
                    PlayerVitalsHooks.TryGetSnapshot(out _);
        if (!Advance(ref f, show)) return;
        Layout l = Measure(f.UiScale);
        float k = l.K, barW = l.BarW, blockH = l.BlockH, nameH = l.NameH;

        // Where the block goes: above the head (default), under the feet, or beside the character.
        float left, y;
        SelfPlatePosition where = MonsterHudSettings.SelfPosition;
        if (where == SelfPlatePosition.Above)
        {
            left = head.X - barW * 0.5f;
            y = head.Y - 4f * k - blockH;
        }
        else
        {
            bool feetOk = MonsterHud.Project(ref f, f.PlayerCell, f.Px, f.Py, f.Pz, out Vector2 feet, keepOnScreen: true);
            if (!feetOk) feet = new Vector2(head.X, head.Y + 90f * k);
            float charH = MathF.Max(20f * k, feet.Y - head.Y);
            float midY = head.Y + charH * 0.5f;
            float side = MathF.Max(22f * k, charH * 0.28f);
            switch (where)
            {
                case SelfPlatePosition.Below:
                    left = feet.X - barW * 0.5f;
                    y = feet.Y + 6f * k + nameH;
                    break;
                case SelfPlatePosition.Left:
                    left = head.X - side - barW;
                    y = midY - blockH * 0.5f;
                    break;
                default: // Right
                    left = head.X + side;
                    y = midY - blockH * 0.5f;
                    break;
            }
        }
        if (MonsterHudSettings.KeepOnScreen)
        {
            left = Math.Clamp(left, 4f, MathF.Max(4f, f.Display.X - barW - 4f));
            y = Math.Clamp(y, 4f + nameH, MathF.Max(4f + nameH, f.Display.Y - blockH - 4f));
        }
        float top = Paint(f.Dl, ref l, MathF.Round(left), MathF.Round(y));
        // Gain text floats above the plate when it's over the head, else above the head itself.
        PlateTop = where == SelfPlatePosition.Above ? top : head.Y;
    }

    /// <summary>
    /// Fixed on screen: the plate at its saved spot, into ImGui's background list (the normal UI pass:
    /// over AC's windows, under every panel), and the drag while unlocked. After <see cref="Draw"/>,
    /// inside the ImGui frame. Render thread.
    /// </summary>
    public static void DrawScreen(ref MonsterHud.Frame f)
    {
        if (MonsterHudSettings.SelfPlacement != SelfPlacement.Fixed) { DropDrag(); return; }
        bool show = MonsterHudSettings.SelfPlate && PlayerVitalsHooks.TryGetSnapshot(out _);
        if (!Advance(ref f, show)) { DropDrag(); return; }
        Layout l = Measure(f.UiScale);

        // The bars' centre from the saved fractions, then the block kept inside the screen.
        Vector2 display = f.Display;
        float cx = MonsterHudSettings.SelfFixedX * display.X, cy = MonsterHudSettings.SelfFixedY * display.Y;
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        bool unlocked = !MonsterHudSettings.SelfLocked;
        if (_dragging)
        {
            if (unlocked && ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                cx = io.MousePos.X - _grab.X;
                cy = io.MousePos.Y - _grab.Y;
            }
            else
            {
                // Let go (or locked mid-drag): keep where it is, saved.
                _dragging = false;
                MonsterHudSettings.Save();
            }
        }
        float left = Math.Clamp(cx - l.BarW * 0.5f, 4f, MathF.Max(4f, display.X - l.BarW - 4f));
        float y = Math.Clamp(cy - l.BlockH * 0.5f, 4f + l.NameH, MathF.Max(4f + l.NameH, display.Y - l.BlockH - 4f));
        left = MathF.Round(left);
        y = MathF.Round(y);
        if (_dragging && display.X > 1 && display.Y > 1)
        {
            // The spot as fractions (clamped, so letting go off screen keeps it reachable).
            MonsterHudSettings.SelfFixedX = Math.Clamp((left + l.BarW * 0.5f) / display.X, 0f, 1f);
            MonsterHudSettings.SelfFixedY = Math.Clamp((y + l.BlockH * 0.5f) / display.Y, 0f, 1f);
        }

        // The plate's rect (name included), a little padded: the drag surface.
        float pad = MathF.Round(4f * l.K);
        var r0 = new Vector2(left - pad, y - l.NameH - pad);
        var r1 = new Vector2(left + l.BarW + pad, y + l.BlockH + pad);
        Drag(unlocked, r0, r1, new Vector2(left + l.BarW * 0.5f, y + l.BlockH * 0.5f));

        ImDrawListPtr dl = ImGuiNET.ImGui.GetBackgroundDrawList();
        if (unlocked)
        {
            // Unlocked: a frame says it can be moved (brighter while hovered or dragged).
            float a = Math.Clamp(_alpha, 0.35f, 1f);
            bool hot = _hover || _dragging;
            dl.AddRectFilled(r0, r1, MonsterHud.Mul(hot ? 0x40000000u : 0x28000000u, a), 3f);
            dl.AddRect(r0, r1, MonsterHud.Mul(hot ? FrameHot : FrameCol, a), 3f, ImDrawFlags.None, hot ? 1.5f : 1f);
        }
        Paint(dl, ref l, left, y);
    }

    private static readonly uint FrameCol = MonsterHud.C(0xB026D9E6), FrameHot = MonsterHud.C(0xFF26D9E6);

    /// <summary>Hover and a left-drag on the unlocked plate (the MonsterHud plate-click pattern).</summary>
    private static void Drag(bool unlocked, Vector2 r0, Vector2 r1, Vector2 centre)
    {
        bool prevHover = _hover;
        _hover = false;
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        bool usable = unlocked && Win32Backend.IsUiCaptureEnabled() && (!io.WantCaptureMouse || _dragging);
        if (usable)
        {
            Vector2 m = io.MousePos;
            _hover = m.X >= r0.X && m.X <= r1.X && m.Y >= r0.Y && m.Y <= r1.Y;
        }
        if (!_dragging && usable && (_hover || prevHover) && Win32Backend.MouseCaptured &&
            ImGuiNET.ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
            !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Right) && !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            // The press went where last frame's hover put the capture: the drag starts.
            _dragging = true;
            _grab = io.MousePos - centre;
        }
        if (_hover || _dragging)
        {
            ImGuiNET.ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (_hover && !_dragging)
                ImGuiNET.ImGui.SetTooltip(DragTip);
        }
        _wantsMouse = _hover || _dragging;
    }

    private const string DragTip = "Drag to move your plate.\nLock it again in Vision > Nameplates > Your plate (or /rv plates self lock).";

    /// <summary>Fades the plate in / out and steps the bars; false when nothing should be painted.</summary>
    private static bool Advance(ref MonsterHud.Frame f, bool show)
    {
        float kAlpha = 1f - MathF.Exp(-f.Dt * 12f);
        _alpha += ((show ? MonsterHudSettings.Opacity : 0f) - _alpha) * kAlpha;
        if (!show) { if (_alpha < 0.02f) _alpha = 0f; return false; }
        if (_alpha < 0.01f) return false;

        PlayerVitalsHooks.TryGetSnapshot(out PlayerVitalsSnapshot v);
        float kBar = 1f - MathF.Exp(-f.Dt * 14f);
        Step(ref _hp, v.Health, v.MaxHealth, f.Now, kBar, f.Dt);
        Step(ref _st, v.Stamina, v.MaxStamina, f.Now, kBar, f.Dt);
        Step(ref _mn, v.Mana, v.MaxMana, f.Now, kBar, f.Dt);
        return true;
    }

    /// <summary>The plate's sizes and fonts this frame.</summary>
    private struct Layout
    {
        public float K, BarW, BarH, Gap, Round, BlockH, NameH, SmallSize, NameSize;
        public bool Numbers;
        public ImFontPtr Small, NameFont;
    }

    private static Layout Measure(float uiScale)
    {
        Layout l = default;
        float k = l.K = MonsterHudSettings.Scale * uiScale;
        bool numbers = l.Numbers = MonsterHudSettings.SelfNumbers;
        // With numbers on, the bars grow to fit bold, outlined text that reads over any fill.
        // Width, height and text each have their own multiplier on top (1 = the default look).
        l.BarW = MathF.Round((numbers ? 124f : 96f) * k * MonsterHudSettings.SelfBarWidth);
        l.BarH = MathF.Round((numbers ? 15f : 5f) * k * MonsterHudSettings.SelfBarHeight);
        l.Gap = MathF.Max(2f, MathF.Round(2f * k));
        l.Round = MathF.Max(1f, 1.5f * k);
        // Sized from the 11 px bold bake, drawn from the smallest bake at least that big (no blur when scaled up;
        // nothing is re-baked when the text size changes).
        float text = MonsterHudSettings.Scale * MonsterHudSettings.SelfTextSize;
        l.SmallSize = MathF.Round(ImGuiFonts.Get(UiFont.UiBold11).FontSize * text);
        l.Small = ImGuiFonts.Sharp(l.SmallSize, bold: true);
        l.BlockH = 3 * l.BarH + 2 * l.Gap;
        l.NameSize = MathF.Round(ImGuiFonts.Get(UiFont.UiBold11).FontSize * text);
        l.NameFont = ImGuiFonts.Sharp(l.NameSize, bold: true);
        l.NameH = MonsterHudSettings.SelfName ? l.NameSize + 3f * k : 0f;
        return l;
    }

    /// <summary>Paints the three bars (and the name over them) with their top-left at (left, y); returns the plate's top.</summary>
    private static float Paint(ImDrawListPtr dl, ref Layout l, float left, float y)
    {
        float a = Math.Clamp(_alpha, 0f, 1f);
        PaintOne(dl, ref _hp, left, y, l.BarW, l.BarH, l.Round, a, HealthCol, l.Numbers, l.Small, l.SmallSize);
        PaintOne(dl, ref _st, left, y + l.BarH + l.Gap, l.BarW, l.BarH, l.Round, a, StaminaCol, l.Numbers, l.Small, l.SmallSize);
        PaintOne(dl, ref _mn, left, y + 2 * (l.BarH + l.Gap), l.BarW, l.BarH, l.Round, a, ManaCol, l.Numbers, l.Small, l.SmallSize);
        float top = y - 1f;
        if (MonsterHudSettings.SelfName)
        {
            string name = PlayerName();
            if (name.Length > 0)
            {
                float w = MonsterHud.TextWidth(l.NameFont, l.NameSize, name);
                var pos = new Vector2(MathF.Round(left + (l.BarW - w) * 0.5f), MathF.Round(y - 3f * l.K - l.NameSize));
                MonsterHud.OutlinedText(dl, l.NameFont, l.NameSize, pos, a, name);
                dl.AddText(l.NameFont, l.NameSize, pos, MonsterHud.Mul(NameCol, a), name);
                top = pos.Y;
            }
        }
        return top;
    }

    /// <summary>The camera at (or inside) the head: AC's first-person view.</summary>
    private static bool IsFirstPerson(ref MonsterHud.Frame f)
    {
        if (!GameMatrixCapture.TryGetCameraPosition(out float cx, out float cy, out float cz)) return false;
        if (!MonsterHud.TryOffset(f.FrameCell, f.PlayerCell, out float ox, out float oy)) return false;
        // Camera is in D3D axes (x east, y up, z north); the eyes sit about 1.6 m up.
        float dx = cx - (f.Px + ox), dy = cy - (f.Pz + 1.6f), dz = cz - (f.Py + oy);
        return dx * dx + dy * dy + dz * dz < FirstPersonDistance * FirstPersonDistance;
    }

    private static void Step(ref BarState b, uint cur, uint max, long now, float kBar, float dt)
    {
        float ratio = max > 0 ? Math.Clamp((float)cur / max, 0f, 1f) : 0f;
        b.Cur = cur; b.Max = max;
        if (!b.Init || _alpha <= 0.02f)
        {
            b.Shown = b.Trail = ratio;
            b.Hold = 0;
            b.Init = true;
            return;
        }
        MonsterHud.StepBar(ref b.Shown, ref b.Trail, ref b.Hold, ratio, now, kBar, dt);
    }

    private static void PaintOne(ImDrawListPtr dl, ref BarState b, float left, float top, float w, float h, float round, float a,
        uint col, bool numbers, ImFontPtr font, float size)
    {
        var b0 = new Vector2(left, top);
        var b1 = new Vector2(left + w, top + h);
        MonsterHud.PaintBar(dl, b0, b1, round, a, b.Shown, b.Trail, col, ticks: false);
        if (!numbers || h < 8f || b.Max == 0) return;
        long key = ((long)b.Max << 32) | b.Cur;
        string? text = b.Text;
        if (key != b.TextKey || text == null)
        {
            b.TextKey = key;
            text = b.Text = b.Cur.ToString(CultureInfo.InvariantCulture) + " / " + b.Max.ToString(CultureInfo.InvariantCulture);
        }
        float tw = MonsterHud.TextWidth(font, size, text);
        var pos = new Vector2(MathF.Round(left + (w - tw) * 0.5f), MathF.Round(top + (h - size) * 0.5f));
        // A dark band behind the text, then an outline, so it reads over bright and dark fills alike.
        // (Text sized bigger than the bar: the band grows to the text.)
        dl.AddRectFilled(new Vector2(pos.X - 3f, MathF.Min(top + 1f, pos.Y)), new Vector2(pos.X + tw + 3f, MathF.Max(top + h - 1f, pos.Y + size)),
            MonsterHud.Mul(0x70000000u, a), 2f);
        MonsterHud.OutlinedText(dl, font, size, pos, a, text);
        dl.AddText(font, size, pos, MonsterHud.Mul(NumCol, a), text);
    }

    private static string _name = "";
    private static uint _nameOwner;

    private static string PlayerName()
    {
        uint id = ClientHelperHooks.GetPlayerId();
        if (id != _nameOwner && ClientObjectHooks.TryGetSnapshotName(id, out string n) && n.Length > 0)
        {
            _nameOwner = id;
            _name = n;
        }
        return _name;
    }

    public static string Describe() =>
        $"Self plate {(MonsterHudSettings.SelfPlate ? "on" : "off")}" +
        (MonsterHudSettings.SelfPlacement == SelfPlacement.Fixed
            ? (MonsterHudSettings.SelfLocked ? ", fixed on screen (locked)." : ", fixed on screen (unlocked: drag it).")
            : FirstPerson ? ", follows you (first person" + (MonsterHudSettings.SelfHideFirstPerson ? ", hidden)." : ").") : ", follows you.");
}
