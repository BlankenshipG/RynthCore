// ============================================================================
//  RynthCore.Engine - ImGui/MonsterHud/DebuffIconRow.cs
//  The debuff icon row, shared by the monster plates (your debuffs on each
//  monster, under its health bar) and your own plate (harmful spells on you,
//  under your mana bar): one icon per kind (a Phosphor glyph in the kind's
//  tint on a dark square), a stack digit when a kind merges several spell
//  categories, a countdown line under each icon that runs out with the spell,
//  a blink in the last BlinkSec seconds, "+N" when more kinds than fit, and
//  (monster plates only) the "debuffed by others" marker.
//
//  Moved out of MonsterHud.PaintIcons unchanged (2026-10-27) so the player
//  plate can draw the same row; the monster plates look exactly as before.
//  Render thread only. Engine-owned statics.
// ============================================================================

using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using ImGuiNET;

namespace RynthCore.Engine.ImGuiBackend.Hud;

internal static class DebuffIconRow
{
    /// <summary>Icons per row; more shows "+N" (the last slot makes room for it).</summary>
    internal const int MaxIcons = 5;
    /// <summary>Icons blink in their last seconds.</summary>
    internal const float BlinkSec = 5f;

    private static readonly uint IconBack = MonsterHud.C(0xD80B1219), OthersTint = MonsterHud.C(0xFFFF8A7A);

    // One glyph and tint per DebuffKind (index = kind).
    private static readonly string[] KindGlyph =
    {
        PhosphorIcons.Fire, PhosphorIcons.Snowflake, PhosphorIcons.Lightning, PhosphorIcons.Flask,
        PhosphorIcons.Sword, PhosphorIcons.Knife, PhosphorIcons.Hammer,
        PhosphorIcons.ShieldSlash, PhosphorIcons.MagicWand, PhosphorIcons.ShieldWarning,
        PhosphorIcons.ArrowFatLinesDown, PhosphorIcons.HeartBreak, PhosphorIcons.Footprints,
        PhosphorIcons.TrendDown, PhosphorIcons.Skull, PhosphorIcons.ArrowFatDown,
    };
    private static readonly uint[] KindTint =
    {
        MonsterHud.ElemFire, MonsterHud.ElemCold, MonsterHud.ElemLightning, MonsterHud.ElemAcid,
        MonsterHud.ElemSlash, MonsterHud.ElemPierce, MonsterHud.ElemBludgeon,
        MonsterHud.C(0xFF9FB4C8),   // Imperil: steel
        MonsterHud.C(0xFFB98CFF),   // Magic Yield: violet
        MonsterHud.C(0xFFFFB347),   // melee / missile defense: orange
        MonsterHud.C(0xFFF2D24B),   // attributes: yellow
        MonsterHud.C(0xFFFF6B8A),   // vitals / regen: pink-red
        MonsterHud.C(0xFFA8C66C),   // movement: moss
        MonsterHud.C(0xFF8FB8DE),   // skills: blue-grey
        MonsterHud.C(0xFF7CE08A),   // damage over time: green
        MonsterHud.C(0xFFC8C8C8),   // other
    };
    private static readonly string[] StackDigits = { "", "", "2", "3", "4", "5", "6", "7", "8", "9" };

    /// <summary>
    /// The row centred on <paramref name="cx"/> with its top at <paramref name="top"/>:
    /// <paramref name="count"/> kinds (may exceed <paramref name="icons"/>' length: the rest
    /// show as "+N"), then the "debuffed by others" marker when <paramref name="others"/>.
    /// <paramref name="sz"/> is the icon square, <paramref name="smallFont"/>/<paramref name="smallSize"/>
    /// draw the stack digit and "+N"; <paramref name="overflowKey"/>/<paramref name="overflowText"/>
    /// cache the "+N" string per row. <paramref name="now"/> is Stopwatch ticks.
    /// </summary>
    internal static void Paint(ImDrawListPtr dl, DebuffIcon[] icons, int count, bool others, float sz,
        ImFontPtr smallFont, float smallSize, ref int overflowKey, ref string overflowText,
        float cx, float top, float k, float a, long now)
    {
        float gap = MathF.Max(2f, MathF.Round(2f * k));
        int shown = count > icons.Length ? icons.Length - 1 : count;
        int overflow = count - shown;
        string more = "";
        float moreW = 0f;
        if (overflow > 0)
        {
            if (overflow != overflowKey)
            {
                overflowKey = overflow;
                overflowText = "+" + overflow.ToString(CultureInfo.InvariantCulture);
            }
            more = overflowText;
            moreW = MonsterHud.TextWidth(smallFont, smallSize, more) + 4f * k;
        }
        int slots = shown + (others ? 1 : 0);
        float rowW = slots * sz + Math.Max(0, slots - 1) * gap + (overflow > 0 ? gap + moreW : 0f);
        float x = MathF.Round(cx - rowW * 0.5f);
        float glyphPx = MathF.Round(sz * 0.8f);
        ImFontPtr iconFont = ImGuiFonts.Sharp(glyphPx, bold: false);   // the icons are merged into every bake
        float lineH = MathF.Max(1f, MathF.Round(1.5f * k));

        for (int i = 0; i < shown; i++)
        {
            DebuffIcon ic = icons[i];
            int kind = (int)ic.Kind;
            uint tint = kind < KindTint.Length ? KindTint[kind] : KindTint[^1];
            float remaining = (ic.Expiry - now) / (float)Stopwatch.Frequency;
            float frac = ic.Duration > 0 ? Math.Clamp(remaining / ic.Duration, 0f, 1f) : 0f;
            float blink = remaining < BlinkSec ? 0.45f + 0.55f * MathF.Abs(MathF.Cos(remaining * MathF.PI * 2f)) : 1f;

            var i0 = new Vector2(x, top);
            var i1 = i0 + new Vector2(sz, sz);
            dl.AddRectFilled(i0, i1, MonsterHud.Mul(IconBack, a), 2f * k);
            dl.AddRect(i0, i1, MonsterHud.Mul(tint, a * 0.55f * blink), 2f * k, ImDrawFlags.None, 1f);
            PhosphorIcons.DrawCentered(dl, iconFont, glyphPx, KindGlyph[kind < KindGlyph.Length ? kind : KindGlyph.Length - 1],
                i0, new Vector2(sz, sz), MonsterHud.Mul(tint, a * blink));
            if (ic.Stack > 1 && ic.Stack < StackDigits.Length)
            {
                string digit = StackDigits[ic.Stack];
                float ds = MathF.Round(smallSize * 0.85f);
                var dp = new Vector2(i1.X - MonsterHud.TextWidth(smallFont, ds, digit) + 1f, i0.Y - 2f);
                MonsterHud.OutlinedText(dl, smallFont, ds, dp, a, digit);
                dl.AddText(smallFont, ds, dp, MonsterHud.Mul(MonsterHud.HpText, a), digit);
            }
            // Countdown: a line under the icon that shrinks to nothing at expiry.
            float ly = i1.Y + 1f;
            dl.AddRectFilled(new Vector2(i0.X, ly), new Vector2(i1.X, ly + lineH), MonsterHud.Mul(MonsterHud.Outline, a * 0.8f));
            if (frac > 0f)
                dl.AddRectFilled(new Vector2(i0.X, ly), new Vector2(i0.X + MathF.Max(1f, sz * frac), ly + lineH), MonsterHud.Mul(tint, a * blink));
            x += sz + gap;
        }

        if (overflow > 0)
        {
            var mp = new Vector2(x, MathF.Round(top + (sz - smallSize) * 0.5f));
            MonsterHud.OutlinedText(dl, smallFont, smallSize, mp, a, more);
            dl.AddText(smallFont, smallSize, mp, MonsterHud.Mul(MonsterHud.DimText, a), more);
            x += moreW + gap;
        }

        if (others)
        {
            var i0 = new Vector2(x, top);
            dl.AddRectFilled(i0, i0 + new Vector2(sz, sz), MonsterHud.Mul(IconBack, a * 0.8f), 2f * k);
            PhosphorIcons.DrawCentered(dl, iconFont, glyphPx, PhosphorIcons.UsersThree, i0, new Vector2(sz, sz), MonsterHud.Mul(OthersTint, a * 0.8f));
        }
    }
}
