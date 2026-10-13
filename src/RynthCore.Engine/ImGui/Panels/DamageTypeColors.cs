// ============================================================================
//  RynthCore.Engine - ImGui/Panels/DamageTypeColors.cs
//  Text colour per damage type, from UB-ILT's LeaftideDamageTypeVtColors (the
//  Virindi Tank / Leaftide label palette its Items, Spell and Pets tabs share).
//  RynthAi's Pets roster uses the same values (IltHub\IltDamageTypeTint.cs);
//  keep the two tables in step. Colours are the panels' ABGR (RynthTheme.Argb).
// ============================================================================

namespace RynthCore.Engine.ImGuiBackend.Panels;

internal static class DamageTypeColors
{
    private static uint C(uint argb) => RynthTheme.Argb(argb);

    public static readonly uint Slash = C(0xFFFFB478), Pierce = C(0xFFF0C88C), Bludgeon = C(0xFFDCBE8C),
        Cold = C(0xFFA0D2FF), Fire = C(0xFFFF8246), Acid = C(0xFFA0FF8C), Lightning = C(0xFFFFF078),
        Nether = C(0xFFC8A0FF);

    /// <summary>Colour for an element name as the panels write it; <paramref name="fallback"/> for anything else (Shield, Auto, None).</summary>
    public static uint For(string? element, uint fallback) => element switch
    {
        "Slash" or "Slashing"                => Slash,
        "Pierce" or "Piercing"               => Pierce,
        "Bludgeon" or "Bludgeoning"          => Bludgeon,
        "Cold" or "Frost"                    => Cold,
        "Fire"                               => Fire,
        "Acid"                               => Acid,
        "Lightning" or "Electric" or "Storm" => Lightning,
        "Nether"                             => Nether,
        _                                    => fallback,
    };
}
