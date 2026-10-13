// ============================================================================
//  RynthCore.Engine - ImGui/Panels/CharactersFace.cs
//  The Characters panel: your other characters (the other RynthCore clients on
//  this PC, through the RynthNet plugin), like UtilityBelt's networked
//  character window. Engine only, ImGui only.
//
//    top       how many, or why none (RynthNet missing / offline); "all
//              follow me"; the help mark
//    rows      this client first (dimmed), then the others alphabetically (a
//              stable order: characters standing together would reshuffle
//              several times a second if sorted by distance). Each row:
//              line 1  direction arrow (relative to where you face), name
//                      (+ server when it isn't yours), state (botting / idle /
//                      dead / portal space / combat stance), a stale marker,
//                      distance and compass point
//              line 2  health / stamina / mana bars with current/max, and
//                      where they are (map coordinates or dungeon name)
//    click     selects the character in game when this client can see it;
//    right     follow me / stop following / start or stop the macro on that
//              client (sent as /rn tell; its RynthNet command policy decides)
//
//  Data: UiSources.Characters (UI/Data/CharactersData.cs, 2 Hz while open).
//  Follows the panel text size (PanelTextScale): layout scales with the font.
//  AC's render thread; no AC calls, no per-frame allocation except a hovered
//  row's tooltip and the popup's labels.
// ============================================================================

using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Data;
using static RynthCore.Engine.ImGuiBackend.Panels.FaceKit;

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal sealed class CharactersFace : IImGuiPanel
{
    public const string Title = "Characters";

    public static void Register()
    {
        ImGuiPanelHost.Register(Title,
            new PanelSpec(new Vector2(420, 260), new Vector2(280, 140), EdgeToEdge: true, GripInBody: true),
            () => new CharactersFace());
    }

    // ── Palette (the RynthAi dashboard's vitals colours) ─────────────────
    private static uint C(uint argb) => RynthTheme.Argb(argb);
    private static readonly uint HpCol = C(0xFFD93333), StCol = C(0xFF40D973), MpCol = C(0xFF268CF2),
        BarBg = C(0xFF0D161F), Purple = C(0xFFBF4DFF), White = C(0xFFFFFFFF);

    private const string HelpText =
        "Your other characters: every RynthCore client on this PC running the RynthNet plugin.\n\n" +
        "Each row shows health, stamina and mana, what the character is doing, and where it is. " +
        "The distance and direction show when you are both outdoors (or in surface buildings), or in the same dungeon; " +
        "otherwise the row says elsewhere and names the dungeon or gives map coordinates. Characters on another server " +
        "only show their server.\n\n" +
        "Rows stay in alphabetical order. A row that stops updating says so (its client may be frozen).\n\n" +
        "Click a row to select that character in game (when this client can see it). Right-click for commands: " +
        "follow me, stop following, start or stop the macro. They go through RynthNet, so the other client's " +
        "remote command setting (/rn remote) decides whether they run.\n\n" +
        "/rc characters opens and closes this panel.";

    private float _k = 1f;
    private float Px(float v) => v * _k;

    // The right-clicked row (copied: the snapshot can change while the popup is open).
    private string _menuName = "";
    private uint _menuId;
    private bool _menuInView, _menuOtherWorld, _menuSelf, _menuLoggedOut;
    private bool _openMenu;

    public int PopOutIdleHz => 10;   // the arrows turn with you

    public void OnShown()
    {
        UiSources.Characters.Subscribe();
        UiSources.Characters.RequestRefresh();
    }

    public void OnHidden() => UiSources.Characters.Unsubscribe();

    public void Draw()
    {
        _k = PanelTextScale.Factor(ImGuiFonts.CurrentStep);
        float w = Begin(out Vector2 origin, out Vector2 size);
        CharactersView view = UiSources.Characters.Current?.Value ?? CharactersView.Empty;
        DrawTop(view, w);

        float bodyH = Math.Max(Px(30), Remaining(origin, size) - GripOverlap());
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
        ImGuiNET.ImGui.BeginChild("##chars_rows", new Vector2(w, bodyH));
        DrawRows(view);
        ImGuiNET.ImGui.EndChild();
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor();
        End(origin, size);

        DrawMenu(view);
    }

    // ── Top line ────────────────────────────────────────────────────────

    private void DrawTop(CharactersView view, float w)
    {
        Vector2 start = ImGuiNET.ImGui.GetCursorScreenPos();
        float h = Px(20), iconW = Px(22);
        float fontH = ImGuiNET.ImGui.GetFontSize();
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        float textW = Math.Max(10, w - 2 * iconW - Px(8));
        dl.PushClipRect(start, start + new Vector2(textW, h), true);
        dl.AddText(new Vector2(start.X, start.Y + (h - fontH) * 0.5f), view.NetLoaded ? Mute : Amber,
            view.Status.Length > 0 ? view.Status : "Waiting for RynthNet...");
        dl.PopClipRect();

        float bx = start.X + w - 2 * iconW - Px(4);
        bool canFollow = view.NetLoaded && view.MyName.Length > 0;
        if (IconButton("##chars_followall", PhosphorIcons.Footprints, new Vector2(bx, start.Y), new Vector2(iconW, h), Text, BtnFill,
                enabled: canFollow, font: UiFont.Ui11))
            CharactersCommands.FollowMeAll();
        ImGuiNET.ImGui.SetItemTooltip("Everyone follow me (/rn followme): every other client runs /ra follow <you>.");
        IconButton("##chars_help", PhosphorIcons.Info, new Vector2(bx + iconW + Px(4), start.Y), new Vector2(iconW, h), Mute, BtnFill,
            font: UiFont.Ui11);
        if (ImGuiNET.ImGui.IsItemHovered())
        {
            ImGuiNET.ImGui.BeginTooltip();
            ImGuiNET.ImGui.PushTextWrapPos(ImGuiNET.ImGui.GetFontSize() * 30);
            ImGuiNET.ImGui.TextUnformatted(HelpText);
            ImGuiNET.ImGui.PopTextWrapPos();
            ImGuiNET.ImGui.EndTooltip();
        }
        NextLine(start, h + Px(4));
    }

    // ── Rows ────────────────────────────────────────────────────────────

    private float RowH => Px(38);

    private void DrawRows(CharactersView view)
    {
        CharacterRow[] rows = view.Rows;
        float bw = ImGuiNET.ImGui.GetContentRegionAvail().X;
        var dl = ImGuiNET.ImGui.GetWindowDrawList();
        if (rows.Length == 0)
        {
            Vector2 at = ImGuiNET.ImGui.GetCursorScreenPos() + new Vector2(Px(6), Px(6));
            dl.AddText(at, Faded(Mute), view.NetLoaded ? "Not in the world." : "");
            return;
        }

        uint selected = ClientHelperHooks.GetSelectedItemId();
        float heading = PlayerHeading(out bool haveHeading);
        float rowH = RowH;
        Vector2 start = ImGuiNET.ImGui.GetCursorScreenPos();
        int hovered = -1;

        for (int i = 0; i < rows.Length; i++)
        {
            CharacterRow r = rows[i];
            var p = new Vector2(start.X, start.Y + i * rowH);
            ImGuiNET.ImGui.SetCursorScreenPos(p);
            ImGuiNET.ImGui.PushID(i);
            bool clicked = ImGuiNET.ImGui.InvisibleButton("##row", new Vector2(bw, rowH),
                ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hot = ImGuiNET.ImGui.IsItemHovered();
            bool right = clicked && ImGuiNET.ImGui.IsMouseReleased(ImGuiMouseButton.Right);
            ImGuiNET.ImGui.PopID();
            if (hot) hovered = i;
            if (clicked && !right && r.InView && r.Id != 0 && !r.IsSelf)
                AcMainThreadQueue.EnqueueOverlaySelect(r.Id);
            if (right) OpenMenu(r);

            DrawRow(dl, r, p, bw, rowH, i, hot, r.Id != 0 && r.Id == selected, heading, haveHeading);
        }

        ImGuiNET.ImGui.SetCursorScreenPos(start);
        ImGuiNET.ImGui.Dummy(new Vector2(bw, rows.Length * rowH));

        if (hovered >= 0 && rows[hovered].Tooltip.Length > 0 && !ImGuiNET.ImGui.IsPopupOpen("##chars_menu"))
        {
            ImGuiNET.ImGui.BeginTooltip();
            ImGuiNET.ImGui.TextUnformatted(rows[hovered].Tooltip);
            ImGuiNET.ImGui.EndTooltip();
        }
    }

    private void DrawRow(ImDrawListPtr dl, CharacterRow r, Vector2 p, float bw, float rowH, int index, bool hot, bool isSel,
        float heading, bool haveHeading)
    {
        uint bg = isSel ? Selected : hot ? Lighten(PanelBg) : (index & 1) == 1 ? RowAlt : 0;
        if (bg != 0) dl.AddRectFilled(p, p + new Vector2(bw, rowH), bg);
        if (isSel) dl.AddRect(p, p + new Vector2(bw, rowH), Teal);
        // This client's row is dimmed: it's here for comparison.
        float dim = r.IsSelf || r.LoggedOut ? 0.55f : 1f;

        ImFontPtr bold = ImGuiFonts.Get(UiFont.UiBold11), f10 = ImGuiFonts.Get(UiFont.Ui10);
        float arrowW = Px(20), pad = Px(4);
        float line1 = p.Y + Px(3);
        float x = p.X + arrowW;
        float right = p.X + bw - pad;

        // Arrow / dot / ring.
        var mid = new Vector2(p.X + arrowW * 0.5f, line1 + bold.FontSize * 0.5f + Px(1));
        uint arrowCol = Fade(StateColor(r), dim);
        if (!r.IsSelf && r.Measured && haveHeading && !float.IsNaN(r.Bearing))
            DrawArrow(dl, mid, r.Bearing - heading, arrowCol, _k);
        else if (r.IsSelf || r.Measured)
            dl.AddCircleFilled(mid, Px(2.5f), arrowCol);
        else
            dl.AddCircle(mid, Px(3.5f), Faded(Mute), 12, Px(1.2f));

        // Right side of line 1: distance + compass (or "elsewhere").
        string dist = r.IsSelf ? "" : r.Measured ? r.DistanceText + (r.Compass.Length > 0 ? " " + r.Compass : "") : r.DistanceText;
        float distW = dist.Length > 0 ? ImGuiNET.ImGui.CalcTextSize(dist).X : 0;
        if (dist.Length > 0)
            dl.AddText(new Vector2(right - distW, line1 + (bold.FontSize - ImGuiNET.ImGui.GetFontSize()) * 0.5f),
                Fade(r.Measured ? Text : Faded(Mute), dim), dist);
        float limit = right - distW - (distW > 0 ? Px(8) : 0);

        // Name (+ server), then state and the stale marker.
        float nameW = Math.Min(CalcW(bold, r.Name), Math.Max(Px(40), (limit - x) * 0.55f));
        Clip(dl, bold, r.Name, x, line1, nameW, Fade(r.IsSelf ? Mute : Text, dim));
        float sx = x + nameW + Px(6);
        if (r.OtherWorld.Length > 0)
        {
            string wtext = "[" + r.OtherWorld + "]";
            float ww = Math.Min(CalcW(f10, wtext), Math.Max(0, limit - sx));
            Clip(dl, f10, wtext, sx, line1 + Px(1), ww, Fade(Amber, dim));
            sx += ww + Px(6);
        }
        if (r.StateText.Length > 0 && sx < limit)
        {
            float tw = Math.Min(CalcW(f10, r.StateText), limit - sx);
            Clip(dl, f10, r.StateText, sx, line1 + Px(1), tw, Fade(StateColor(r), dim));
            sx += tw + Px(6);
        }
        if (r.Stale && sx < limit)
        {
            string st = PhosphorIcons.Warning + " " + r.StaleText;
            Clip(dl, f10, st, sx, line1 + Px(1), limit - sx, Amber);
        }

        // Line 2: bars, then the location.
        float line2 = line1 + bold.FontSize + Px(4);
        float barH = Px(13);
        float locW = r.Location.Length > 0 ? Math.Min(CalcW(f10, r.Location), Math.Max(Px(70), bw * 0.32f)) : 0;
        float barsW = right - x - (locW > 0 ? locW + Px(8) : 0);
        if (barsW < Px(150)) { barsW = right - x; locW = 0; }   // narrow: bars first, the tooltip has the place
        if (r.HasVitals)
        {
            float gap = Px(3);
            float one = (barsW - 2 * gap) / 3f;
            Bar(dl, x, line2, one, barH, r.Hp, HpCol, f10, dim);
            Bar(dl, x + one + gap, line2, one, barH, r.St, StCol, f10, dim);
            Bar(dl, x + 2 * (one + gap), line2, one, barH, r.Mp, MpCol, f10, dim);
        }
        else if (!r.LoggedOut)
        {
            Clip(dl, f10, "no vitals yet", x, line2, barsW, Faded(Mute));
        }
        if (locW > 0)
            Clip(dl, f10, r.Location, right - locW, line2 + (barH - f10.FontSize) * 0.5f, locW, Fade(Mute, dim));
    }

    private void Bar(ImDrawListPtr dl, float x, float y, float w, float h, CharVital v, uint color, ImFontPtr font, float dim)
    {
        if (w <= 2) return;
        var a = new Vector2(x, y);
        var b = new Vector2(x + w, y + h);
        float round = Px(3);
        dl.AddRectFilled(a, b, BarBg, round);
        if (v.Fraction > 0) dl.AddRectFilled(a, new Vector2(x + w * v.Fraction, b.Y), Fade(color, dim), round);
        float tw = CalcW(font, v.Text);
        float tx = x + Math.Max(Px(3), (w - tw) * 0.5f);
        dl.PushClipRect(a, b, true);
        dl.AddText(font, font.FontSize, new Vector2(tx, y + (h - font.FontSize) * 0.5f), Fade(White, dim), v.Text);
        dl.PopClipRect();
    }

    private static uint StateColor(CharacterRow r) => r.StateKind switch
    {
        _ when r.IsSelf => Mute,
        CharStateKind.Dead => Red,
        CharStateKind.Fighting => Amber,
        CharStateKind.Botting => Green,
        CharStateKind.Portal => Purple,
        CharStateKind.CombatMode => Teal,
        CharStateKind.NoStatus or CharStateKind.LoggedOut => Faded(Mute),
        _ => Mute,
    };

    private static float CalcW(ImFontPtr font, string text) =>
        text.Length == 0 ? 0 : font.CalcTextSizeA(font.FontSize, float.MaxValue, 0f, text).X;

    private static void Clip(ImDrawListPtr dl, ImFontPtr font, string text, float x, float y, float width, uint color)
    {
        if (text.Length == 0 || width <= 0) return;
        dl.PushClipRect(new Vector2(x, y - 2), new Vector2(x + width, y + font.FontSize + 4), true);
        dl.AddText(font, font.FontSize, new Vector2(x, y), color, text);
        dl.PopClipRect();
    }

    private static uint Fade(uint abgr, float k)
    {
        if (k >= 1f) return abgr;
        uint a = (uint)Math.Clamp((int)(((abgr >> 24) & 0xFF) * k), 0, 255);
        return (abgr & 0x00FFFFFF) | (a << 24);
    }

    /// <summary>An arrow pointing <paramref name="degrees"/> clockwise from straight up (up = the way you face).</summary>
    private static void DrawArrow(ImDrawListPtr dl, Vector2 c, float degrees, uint color, float k)
    {
        float rad = degrees * (MathF.PI / 180f);
        float s = MathF.Sin(rad), co = MathF.Cos(rad);
        Vector2 tip = Rot(c, 0, -6.5f * k, s, co), left = Rot(c, -4.5f * k, 5f * k, s, co),
            notch = Rot(c, 0, 2.5f * k, s, co), rgt = Rot(c, 4.5f * k, 5f * k, s, co);
        dl.AddTriangleFilled(tip, left, notch, color);
        dl.AddTriangleFilled(tip, notch, rgt, color);
    }

    private static Vector2 Rot(Vector2 c, float x, float y, float s, float co) =>
        new(c.X + x * co - y * s, c.Y + x * s + y * co);

    /// <summary>Your heading this frame (compass degrees) from the pose snapshot; no AC call (SenseFace's way).</summary>
    private static float PlayerHeading(out bool ok)
    {
        ok = PlayerPhysicsHooks.TryGetPlayerPoseSnapshot(out _, out _, out _, out _, out float qw, out float qz,
            out bool headingValid, out float heading);
        if (!ok) return 0;
        if (headingValid) return heading;
        double yaw = 2.0 * Math.Atan2(qz, qw) * (180.0 / Math.PI);
        return (float)((-yaw % 360.0 + 720.0) % 360.0);
    }

    // ── Right-click menu ────────────────────────────────────────────────

    private void OpenMenu(CharacterRow r)
    {
        _menuName = r.Name;
        _menuId = r.Id;
        _menuInView = r.InView;
        _menuOtherWorld = r.OtherWorld.Length > 0;
        _menuSelf = r.IsSelf;
        _menuLoggedOut = r.LoggedOut;
        _openMenu = true;
    }

    private void DrawMenu(CharactersView view)
    {
        if (_openMenu)
        {
            _openMenu = false;
            ImGuiNET.ImGui.OpenPopup("##chars_menu");
        }
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.PopupBg, ShellBg);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, Teal);
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGuiNET.ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(8), Px(6)));
        bool open = ImGuiNET.ImGui.BeginPopup("##chars_menu");
        ImGuiNET.ImGui.PopStyleVar();
        ImGuiNET.ImGui.PopStyleColor(3);
        if (!open) return;
        try
        {
            string me = view.MyName;
            bool canFollow = me.Length > 0;
            if (!_menuSelf && !_menuLoggedOut)
            {
                ImGuiNET.ImGui.TextDisabled(_menuName);
                if (ImGuiNET.ImGui.MenuItem("Select", "", false, _menuInView && _menuId != 0))
                    AcMainThreadQueue.EnqueueOverlaySelect(_menuId);
                ImGuiNET.ImGui.Separator();
                if (ImGuiNET.ImGui.MenuItem("Follow me", "", false, canFollow && !_menuOtherWorld))
                    CharactersCommands.Tell(_menuName, "/ra follow " + me);
                ImGuiNET.ImGui.SetItemTooltip($"/rn tell {_menuName}, /ra follow {me}");
                if (ImGuiNET.ImGui.MenuItem("Stop following"))
                    CharactersCommands.Tell(_menuName, "/ra follow off");
                ImGuiNET.ImGui.SetItemTooltip($"/rn tell {_menuName}, /ra follow off");
                ImGuiNET.ImGui.Separator();
                if (ImGuiNET.ImGui.MenuItem("Start macro"))
                    CharactersCommands.Tell(_menuName, "/ra start");
                ImGuiNET.ImGui.SetItemTooltip($"/rn tell {_menuName}, /ra start");
                if (ImGuiNET.ImGui.MenuItem("Stop macro"))
                    CharactersCommands.Tell(_menuName, "/ra stop");
                ImGuiNET.ImGui.SetItemTooltip($"/rn tell {_menuName}, /ra stop");
                ImGuiNET.ImGui.Separator();
            }
            if (ImGuiNET.ImGui.MenuItem("Everyone: follow me", "", false, canFollow))
                CharactersCommands.FollowMeAll();
            ImGuiNET.ImGui.SetItemTooltip("/rn followme");
            ImGuiNET.ImGui.TextDisabled("The other client's /rn remote setting decides.");
        }
        finally
        {
            ImGuiNET.ImGui.EndPopup();
        }
    }
}
