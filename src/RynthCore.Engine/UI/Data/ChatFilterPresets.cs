// ============================================================================
//  RynthCore.Engine - UI/Data/ChatFilterPresets.cs
//  Standard filters: ready-made RynthChat rules for common game spam
//  (2026-10-05; the idea of canned filters is Silentkelpie's, rynth PR #1).
//  Picking one in the rule editor adds it as an ordinary Hide rule, which the
//  player can then see, edit, move or delete like any other: there is no
//  second filter engine and nothing new in rynthchat_settings.json.
//
//  The patterns are our own. They were written from the lines the game sent
//  our own characters (the RynthCore logs' [BuffChat] lines, with their chat
//  types) and from the game's message text; none are copied from another
//  filter list. Each is held to its chat type or channel where the game gives
//  one, so a player who says the same words in Local or a tell is never
//  hidden, and tools/ChatFilterTests checks every example below.
//
//  Not included: NPC vendor and quest tells. They arrive as ordinary tells
//  (type 3), the same as a player's, and nothing in the line tells them apart.
// ============================================================================

using System;
using System.Text.RegularExpressions;

namespace RynthCore.Engine.UI.Data;

/// <summary>One standard filter: a rule template plus lines it must and must not catch.</summary>
internal sealed class ChatFilterPreset
{
    public string Id { get; }
    public string Group { get; }
    public string Label { get; }
    public string Description { get; }
    public ChatMatchMode Mode { get; }
    public string Pattern { get; }
    /// <summary>The rule's When ("" any line, "chan:X", "type:N").</summary>
    public string When { get; }
    /// <summary>Lines it hides: text, the channel tab it arrives in, its chat type.</summary>
    public (string Text, string Channel, int Type)[] Catches { get; }
    /// <summary>Lines it leaves alone (mostly the same words from a player).</summary>
    public (string Text, string Channel, int Type)[] Spares { get; }

    private readonly Regex? _rx;
    private readonly (string? Channel, int Type) _when;

    public ChatFilterPreset(string id, string group, string label, string description,
        ChatMatchMode mode, string pattern, string when,
        (string, string, int)[] catches, (string, string, int)[] spares)
    {
        Id = id; Group = group; Label = label; Description = description;
        Mode = mode; Pattern = pattern; When = when;
        Catches = catches; Spares = spares;
        _rx = ChatRuleMatching.Compile(mode, pattern, 50, out _);
        _when = ChatRuleMatching.ParseWhen(when);
    }

    /// <summary>Would the rule this preset adds catch the line?</summary>
    public bool Matches(string text, string channel, int chatType)
    {
        if (_rx == null || !ChatRuleMatching.WhenMatches(_when.Channel, _when.Type, channel, chatType)) return false;
        try { return _rx.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Is a rule with these fields this preset (already added)?</summary>
    public bool SameAs(ChatMatchMode mode, string pattern, string when) =>
        mode == Mode
        && string.Equals(pattern, Pattern, StringComparison.Ordinal)
        && string.Equals((when ?? "").Trim(), When, StringComparison.OrdinalIgnoreCase);

    public string PickerLabel => Group + ": " + Label;
}

internal static class ChatFilterPresets
{
    // ChatMessageType numbers as RynthChat reports them (seen in the RynthCore logs).
    private const int Magic = 7, Emote = 12, Spellcasting = 17, EnemyCombat = 21, Notice = 26, ServerChannel = 27;
    private const int Say = 2, Tell = 3;

    private static (string, string, int) L(string text, string channel, int type) => (text, channel, type);

    public static readonly ChatFilterPreset[] All =
    {
        // ── Casting ─────────────────────────────────────────────────────
        new("cast.mine", "Casting", "Your spell words",
            "Your own incantations (You say, \"Malar Zhavik\"). Only spell-word lines: what you say in Local stays.",
            ChatMatchMode.StartsWith, "You say, \"", "type:17",
            new[] { L("You say, \"Malar Zhavik\"", "Combat", Spellcasting), L("You say, \"Puish Zharil\"", "Combat", Spellcasting) },
            new[] { L("You say, \"anyone selling keys?\"", "Chat", Say) }),
        new("cast.others", "Casting", "Others' spell words",
            "Incantations from other players and monsters (Drudge Sorcerer says, \"Zojak Equin\").",
            ChatMatchMode.Regex, "^(?!You say, ).+ says, \"", "type:17",
            new[] { L("Drudge Sorcerer says, \"Zojak Equin\"", "Combat", Spellcasting) },
            new[] { L("You say, \"Malar Zhavik\"", "Combat", Spellcasting), L("Bob says, \"Zojak Equin\"", "Chat", Say) }),
        new("cast.components", "Casting", "Components used",
            "The spell consumed the following components: ...",
            ChatMatchMode.StartsWith, "The spell consumed the following components", "type:7",
            new[] { L("The spell consumed the following components: Prismatic Taper", "Combat", Magic),
                    L("The spell consumed the following components: Prismatic Taper, Prismatic Taper", "Combat", Magic) },
            new[] { L("The spell consumed the following components: lots", "Chat", Say) }),
        new("cast.fizzle", "Casting", "Spell fizzled",
            "Your spell fizzled.",
            ChatMatchMode.StartsWith, "Your spell fizzled", "type:7",
            new[] { L("Your spell fizzled.", "Combat", Magic) },
            new[] { L("Bob tells you, \"Your spell fizzled again\"", "Chat", Tell) }),
        new("cast.self", "Casting", "Spells you cast on yourself",
            "You cast ... on yourself (buffs, Stamina to Mana and the like).",
            ChatMatchMode.Regex, "^You cast .+ on yourself", "type:7",
            new[] { L("You cast Focus Self I on yourself, but it is surpassed by Incantation of Focus Other", "Combat", Magic),
                    L("You cast Stamina to Mana Self V on yourself and lose 17 points of stamina and also gain 23 points of mana", "Combat", Magic) },
            new[] { L("You cast Impenetrability I on Driftwarden Robe, but it is surpassed by Impenetrability V", "Combat", Magic) }),
        new("cast.surpassed", "Casting", "Spell already surpassed",
            "You cast X on Y, but it is surpassed by Z (a weaker buff over a stronger one).",
            ChatMatchMode.Contains, ", but it is surpassed by ", "type:7",
            new[] { L("You cast Bludgeon Bane I on Driftwarden Robe, but it is surpassed by Bludgeon Bane V", "Combat", Magic) },
            new[] { L("You cast Revitalize Self V and restore 40 points of your stamina.", "Combat", Magic) }),
        new("cast.no_components", "Casting", "Missing components",
            "You don't have all the components for this spell.",
            ChatMatchMode.StartsWith, "You don't have all the components", "type:26",
            new[] { L("You don't have all the components for this spell.", "Other", Notice) },
            new[] { L("Bob says, \"You don't have all the components for this spell.\"", "Chat", Say) }),
        new("cast.no_mana", "Casting", "Not enough mana",
            "You don't have enough Mana to cast this spell.",
            ChatMatchMode.StartsWith, "You don't have enough Mana", "type:26",
            new[] { L("You don't have enough Mana to cast this spell.", "Other", Notice) },
            Array.Empty<(string, string, int)>()),

        // ── Combat ──────────────────────────────────────────────────────
        new("combat.you_evaded", "Combat", "You evaded",
            "You evaded Tusker Guard!",
            ChatMatchMode.StartsWith, "You evaded ", "type:21",
            new[] { L("You evaded Tusker Guard!", "Combat", EnemyCombat), L("You evaded Olthoi Swarm Gardener!", "Combat", EnemyCombat) },
            new[] { L("Bob tells you, \"You evaded my question\"", "Chat", Tell) }),
        new("combat.evaded_you", "Combat", "Evaded your attack",
            "Drudge Slinker evaded your attack.",
            ChatMatchMode.EndsWith, " evaded your attack.", "chan:Combat",
            new[] { L("Drudge Slinker evaded your attack.", "Combat", 22) },
            new[] { L("Bob says, \"it evaded your attack.\"", "Chat", Say) }),
        new("combat.damage_taken", "Combat", "Damage you take",
            "Tusker Guard grazes your foot for 4 points of bludgeoning damage! (critical hits too)",
            ChatMatchMode.Regex, @"^(?:Critical hit! )?(?!You )\S.* (?:you|your [a-z ]+) for \d+ points? of ", "type:21",
            new[] { L("Tusker Guard grazes your foot for 4 points of bludgeoning damage!", "Combat", EnemyCombat),
                    L("Critical hit! Olthoi Swarm Nymph nicks your head for 21 points of piercing damage!", "Combat", EnemyCombat),
                    L("Olthoi Swarm Gardener blisters your lower leg for 1 point of acid damage!", "Combat", EnemyCombat) },
            new[] { L("You evaded Tusker Guard!", "Combat", EnemyCombat),
                    L("Bob says, \"it hit your head for 5 points of damage\"", "Chat", Say) }),

        // ── Notices ─────────────────────────────────────────────────────
        new("notice.busy", "Notices", "You're too busy",
            "You're too busy!",
            ChatMatchMode.Regex, "^You're too busy!$", "type:26",
            new[] { L("You're too busy!", "Other", Notice) },
            new[] { L("You're too busy! (and other words)", "Other", Notice) }),
        new("notice.pk_battle", "Notices", "Recent PK battle",
            "You have been involved in a player killer battle too recently to do that! (and the same about a target)",
            ChatMatchMode.Contains, "involved in a player killer battle too recently", "",
            new[] { L("You have been involved in a player killer battle too recently to do that!", "Other", Notice),
                    L("Bob has been involved in a player killer battle too recently to do that!", "Combat", Magic) },
            new[] { L("You have been involved in a fight", "Other", Notice) }),

        // ── Social ──────────────────────────────────────────────────────
        new("social.emotes", "Social", "Emotes",
            "Every emote line (Bob waves.), yours included.",
            ChatMatchMode.Regex, ".", "type:12",
            new[] { L("Bob waves.", "Chat", Emote) },
            new[] { L("Bob says, \"waves\"", "Chat", Say) }),

        // ── Channels ────────────────────────────────────────────────────
        new("chan.general", "Channels", "General channel",
            "[General] lines.",
            ChatMatchMode.StartsWith, "[General]", "",
            new[] { L("[General] Aja says, \"lol\"", "Other", ServerChannel), L("[General] Bob says, \"hi\"", "Channels", 8) },
            new[] { L("Bob says, \"[General] is quiet\"", "Chat", Say) }),
        new("chan.trade", "Channels", "Trade channel",
            "[Trade] lines (buying and selling spam).",
            ChatMatchMode.StartsWith, "[Trade]", "",
            new[] { L("[Trade] Bob says, \"WTS Black Opal\"", "Other", ServerChannel) },
            new[] { L("Bob tells you, \"[Trade] is spammy\"", "Chat", Tell) }),
        new("chan.lfg", "Channels", "LFG channel",
            "[LFG] lines.",
            ChatMatchMode.StartsWith, "[LFG]", "",
            new[] { L("[LFG] Bob says, \"LF tank for the Graveyard\"", "Other", ServerChannel) },
            Array.Empty<(string, string, int)>()),
    };

    public static ChatFilterPreset? Get(string id)
    {
        foreach (ChatFilterPreset p in All)
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}
