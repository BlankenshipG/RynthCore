// ============================================================================
//  RynthCore.Engine - ImGui/MonsterHud/PlayerPlate.cs
//  The player's own plate: three thin stacked bars over your head (health red,
//  stamina amber, mana blue) in the monster plates' style (edge, shine, the
//  chip that drains after a drop); optional "cur/max" in each bar and the
//  name above. No name and no numbers by default, to stay compact.
//
//  Data: PlayerVitalsHooks.TryGetSnapshot, the buffed values AC's own vital
//  bars show, kept on AC's main thread (a lock and a struct copy per frame).
//  Position: the player's live pose, projected like the monster plates
//  (MonsterHud.Project handles the landblock frame).
//
//  First person: when the camera is inside or right at the head, the plate
//  would sit in the middle of the screen, so it hides (setting; on by
//  default). The head point is still published for the gain text.
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
    }

    public static void Draw(ref MonsterHud.Frame f)
    {
        // Head point (AC landblock-local), used by the gain text even when the plate is off.
        float hz = f.Pz + HeadHeight + MonsterHudSettings.HeightLift;
        FirstPerson = IsFirstPerson(ref f);
        HeadOnScreen = MonsterHud.Project(ref f, f.PlayerCell, f.Px, f.Py, hz, out Vector2 head, keepOnScreen: true) &&
                       head.X > -40 && head.X < f.Display.X + 40 && head.Y > -40 && head.Y < f.Display.Y + 40;
        HeadScreen = head;
        PlateTop = head.Y;

        bool show = MonsterHudSettings.SelfPlate && HeadOnScreen && !(FirstPerson && MonsterHudSettings.SelfHideFirstPerson) &&
                    PlayerVitalsHooks.TryGetSnapshot(out _);
        float kAlpha = 1f - MathF.Exp(-f.Dt * 12f);
        _alpha += ((show ? MonsterHudSettings.Opacity : 0f) - _alpha) * kAlpha;
        if (!show) { if (_alpha < 0.02f) _alpha = 0f; return; }
        if (_alpha < 0.01f) return;

        PlayerVitalsHooks.TryGetSnapshot(out PlayerVitalsSnapshot v);
        float kBar = 1f - MathF.Exp(-f.Dt * 14f);
        Step(ref _hp, v.Health, v.MaxHealth, f.Now, kBar, f.Dt);
        Step(ref _st, v.Stamina, v.MaxStamina, f.Now, kBar, f.Dt);
        Step(ref _mn, v.Mana, v.MaxMana, f.Now, kBar, f.Dt);

        float a = Math.Clamp(_alpha, 0f, 1f);
        float k = MonsterHudSettings.Scale * f.UiScale;
        bool numbers = MonsterHudSettings.SelfNumbers;
        // With numbers on, the bars grow to fit bold, outlined text that reads over any fill.
        float barW = MathF.Round((numbers ? 124f : 96f) * k);
        float barH = MathF.Round((numbers ? 15f : 5f) * k);
        float gap = MathF.Max(2f, MathF.Round(2f * k));
        float round = MathF.Max(1f, 1.5f * k);
        // Sized from the 11 px bold bake, drawn from the smallest bake at least that big (no blur when scaled up).
        float smallSize = MathF.Round(ImGuiFonts.Get(UiFont.UiBold11).FontSize * MonsterHudSettings.Scale);
        ImFontPtr small = ImGuiFonts.Sharp(smallSize, bold: true);

        ImDrawListPtr dl = f.Dl;
        float blockH = 3 * barH + 2 * gap;
        float nameH = 0f;
        float nameSize = MathF.Round(ImGuiFonts.Get(UiFont.UiBold11).FontSize * MonsterHudSettings.Scale);
        ImFontPtr nameFont = ImGuiFonts.Sharp(nameSize, bold: true);
        if (MonsterHudSettings.SelfName) nameH = nameSize + 3f * k;

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
        left = MathF.Round(left);
        y = MathF.Round(y);
        PaintOne(dl, ref _hp, left, y, barW, barH, round, a, HealthCol, numbers, small, smallSize);
        PaintOne(dl, ref _st, left, y + barH + gap, barW, barH, round, a, StaminaCol, numbers, small, smallSize);
        PaintOne(dl, ref _mn, left, y + 2 * (barH + gap), barW, barH, round, a, ManaCol, numbers, small, smallSize);
        float top = y - 1f;

        if (MonsterHudSettings.SelfName)
        {
            string name = PlayerName();
            if (name.Length > 0)
            {
                ImFontPtr font = nameFont;
                float size = nameSize;
                float w = MonsterHud.TextWidth(font, size, name);
                var pos = new Vector2(MathF.Round(left + (barW - w) * 0.5f), MathF.Round(y - 3f * k - size));
                MonsterHud.OutlinedText(dl, font, size, pos, a, name);
                dl.AddText(font, size, pos, MonsterHud.Mul(NameCol, a), name);
                top = pos.Y;
            }
        }
        // Gain text floats above the plate when it's over the head, else above the head itself.
        PlateTop = where == SelfPlatePosition.Above ? top : head.Y;
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
        dl.AddRectFilled(new Vector2(pos.X - 3f, top + 1f), new Vector2(pos.X + tw + 3f, top + h - 1f), MonsterHud.Mul(0x70000000u, a), 2f);
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
        (FirstPerson ? " (first person" + (MonsterHudSettings.SelfHideFirstPerson ? ", hidden)." : ").") : ".");
}
