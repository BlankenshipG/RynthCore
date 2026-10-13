// ============================================================================
//  RynthCore.Engine — UI/Panels/RynthChatPresets.cs
//  Canned ("standard") RynthChat filters, modelled on UtilityBelt-IT's
//  ChatFilter / Mag ChatFilter categories. Each preset is a tick-box in the
//  ChatFilters panel that hides its lines or moves them to a tab.
//
//  Presets match the raw line text (no timestamp / sender prefix). Most are
//  limited to non-chat channels so a player typing the same words in Local or
//  a channel is never eaten. ChatRouter evaluates them on the plugin pump
//  (after the custom rules); editors change Enabled / Tab from any thread and
//  then call ChatModel.FiltersChanged so every kept line is routed again.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RynthCore.Engine.UI.Panels;

internal static class RynthChatPresets
{
    /// <summary>Which RynthChat channels a preset may match.</summary>
    internal enum Scope
    {
        /// <summary>Anything except Chat / Channels (player-typed text).</summary>
        NotPlayerChat,
        /// <summary>Only the Combat channel (spell incantations arrive as chat type 0x11 → Combat).</summary>
        CombatOnly,
        /// <summary>Only the Channels tab (General / Trade / LFG / Roleplay / Society).</summary>
        ChannelsOnly,
        /// <summary>Every channel.</summary>
        Any,
    }

    internal sealed class Preset
    {
        internal string Id          { get; }
        internal string Group       { get; }
        internal string Label       { get; }
        internal string Description { get; }
        internal string Example     { get; }
        internal Scope  Where       { get; }
        internal Regex  Pattern     { get; }

        // User state (persisted in rynthchat_settings.json "presets" by ChatModel).
        // Volatile: written by an editor, read by the pump's router.
        internal volatile bool   Enabled;
        internal volatile string Tab = "";   // "" = hide matching lines

        internal Preset(string id, string group, string label, string description, string example, Scope where, string pattern)
        {
            Id = id;
            Group = group;
            Label = label;
            Description = description;
            Example = example;
            Where = where;
            Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        }

        /// <summary>True when this preset applies to a line on <paramref name="channel"/> with raw <paramref name="text"/>.</summary>
        internal bool Matches(string text, string channel)
        {
            bool inScope = Where switch
            {
                Scope.NotPlayerChat => channel is not ("Chat" or "Channels"),
                Scope.CombatOnly    => channel == "Combat",
                Scope.ChannelsOnly  => channel == "Channels",
                _                   => true,
            };
            if (!inScope) return false;
            try { return Pattern.IsMatch(text); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }

    // Kill flavour text (retail/ACE death messages). Damage lines ("... for N points of ...")
    // share some verbs, so they are excluded up front.
    private const string KillPattern =
        @"^(?!.* for \d+ points? of )(?:" +
        @"You (?:flatten|knock|split|cleave|slay|bring|beat|run|suffocate|smite|killed) .+!" +
        @"|.+ is (?:reduced to cinders|torn to ribbons by your assault|shattered by your assault|incinerated by your assault|liquified by your attack|dessicated by your attack)!" +
        @"|.+ catches your attack, with dire consequences!" +
        @"|.+'s (?:seared corpse smolders before you|last strength dissolves before you)!" +
        @"|Electricity tears .+ apart!|Blistered by lightning, .+ falls!" +
        @"|Your killing blow nearly turns .+ inside-out!|The thunder of crushing .+ is followed by the deafening silence of death!" +
        @"|Your attack stops .+ cold!)";

    // Optional combat prefixes such as "Critical hit! " / "Sneak Attack! ".
    private const string HitPrefix = @"^(?:[A-Z][\w ]{2,20}! )*";

    private static readonly Preset[] _all =
    {
        // ── Combat ────────────────────────────────────────────────────────
        new("combat.attack_evades", "Combat", "Your attacks evaded",
            "A monster evades your melee or missile attack.",
            "Drudge Slinker evaded your attack.", Scope.NotPlayerChat, @" evaded your attack\.\s*$"),
        new("combat.defense_evades", "Combat", "You evade attacks",
            "You evade a monster's attack.",
            "You evaded Drudge Slinker!", Scope.NotPlayerChat, @"^You evaded "),
        new("combat.damage_dealt", "Combat", "Damage you deal",
            "Your melee, missile and war-spell hits (including critical / sneak attack prefixes).",
            "You slash Drudge Slinker for 54 points of slashing damage!", Scope.NotPlayerChat, HitPrefix + @"You .+ for \d+ points? of "),
        new("combat.damage_taken", "Combat", "Damage you take",
            "Monster hits and spell damage landing on you.",
            "Drudge Slinker slashes you for 12 points of slashing damage!", Scope.NotPlayerChat, HitPrefix + @"(?!You ).+ you (?:with .+ )?for \d+ points? of "),
        new("combat.attack_resists", "Combat", "Your spells resisted",
            "A target resists one of your spells.",
            "Drudge Slinker resists your spell", Scope.NotPlayerChat, @" resists your spell"),
        new("combat.defense_resists", "Combat", "You resist spells",
            "You resist a spell, or a caster was out of range / had no valid target.",
            "You resist the spell cast by Drudge Sorcerer", Scope.NotPlayerChat,
            @"^You resist the spell cast by |^You have no appropriate target.*spell|^You are an invalid target for the spell|tried to cast a spell on you, but was too far away!"),
        new("combat.npk", "Combat", "Not-a-PK failures",
            "\"You fail to affect X, you are not a player killer!\" and the mirror message.",
            "You fail to affect Bob because you are not a player killer!", Scope.NotPlayerChat,
            @"^You fail to affect .+ you are not a player killer!|fails to affect you.+ is not a player killer!"),
        new("combat.dirty_fighting", "Combat", "Dirty Fighting procs",
            "Dirty Fighting assault announcements.",
            "Dirty Fighting! Bob delivers a Traumatic Assault to Drudge!", Scope.NotPlayerChat, @"^Dirty Fighting! .+ delivers a "),
        new("combat.monster_deaths", "Combat", "Monster death messages",
            "Kill flavour text (\"You flatten…\", \"…is reduced to cinders!\").",
            "Drudge Slinker is reduced to cinders!", Scope.NotPlayerChat, KillPattern),

        // ── Casting ───────────────────────────────────────────────────────
        new("cast.mine", "Casting", "Your spell words",
            "Your own incantations (\"You say, \\\"Zojak…\\\"\"). Real Local chat is untouched.",
            "You say, \"Zojak Equin\"", Scope.CombatOnly, @"^You say, """),
        new("cast.others", "Casting", "Others' spell words",
            "Incantations from other players and monsters.",
            "Bob says, \"Cruath Quareth\"", Scope.CombatOnly, @"^(?!You say).+ says, """),
        new("cast.fizzle", "Casting", "Spell fizzles",
            "\"Your spell fizzled.\"",
            "Your spell fizzled.", Scope.NotPlayerChat, @"^Your spell fizzled\."),
        new("cast.components", "Casting", "Component usage",
            "\"The spell consumed the following components: …\"",
            "The spell consumed the following components: Lead Scarab", Scope.NotPlayerChat, @"^The spell consumed the following components"),
        new("cast.self_buffs", "Casting", "Your buff casts",
            "\"You cast X on yourself…\" confirmations.",
            "You cast Strength Self VII on yourself", Scope.NotPlayerChat, @"^You cast .+ on yourself"),
        new("cast.buffs_on_you", "Casting", "Buffs others cast on you",
            "\"Bob cast X on you…\" lines from buff bots and fellows.",
            "Bob cast Strength Other VII on you", Scope.NotPlayerChat, @"^(?!You ).+ cast .+ on you\b"),
        new("cast.expires", "Casting", "Spell expiry",
            "\"… has expired.\" — rares (Brilliance / Prodigal / Spectral) are kept.",
            "Strength Self VII has expired.", Scope.NotPlayerChat, @"^(?!.*(?:Brilliance|Prodigal|Spectral)).*\b(?:has|have) expired\."),

        // ── Items & utility ───────────────────────────────────────────────
        new("item.heal_ok", "Items", "Healing kit success",
            "\"You heal yourself for N Health points…\"",
            "You heal yourself for 80 Health points.", Scope.NotPlayerChat, @"^You .*heal yourself for "),
        new("item.heal_fail", "Items", "Healing kit failure",
            "\"You fail to heal yourself.\"",
            "You fail to heal yourself. ", Scope.NotPlayerChat, @"^You fail to heal yourself\."),
        new("item.salvage", "Items", "Salvage results",
            "\"You obtain … using your knowledge of …\"",
            "You obtain 12 Steel (ws 7.00) using your knowledge of Weapon Tinkering.", Scope.NotPlayerChat, @"^You obtain .+ using your knowledge of "),
        new("item.salvage_fail", "Items", "Salvage failures",
            "\"Salvaging Failed!\" and unsuitable-item notices.",
            "Salvaging Failed!", Scope.NotPlayerChat, @"^Salvaging Failed!|The following were not suitable for salvaging: "),
        new("item.craftsman", "Items", "Aura of the Craftsman",
            "The +5 skill augmentation notice on every craft.",
            "Your Aura of the Craftman augmentation increased your skill by 5!", Scope.NotPlayerChat, @"^Your Aura of the Craftman augmentation increased your skill"),
        new("item.mana_stone", "Items", "Mana stone usage",
            "Mana stone give / drain / destroyed lines.",
            "The Mana Stone gives 4,500 points of mana to the following items:", Scope.NotPlayerChat,
            @"^The Mana Stone (?:gives|drains) |^You need .+ more mana to fully charge your items\.\s*$|^The .+ is destroyed\.\s*$"),
        new("item.regen", "Items", "Periodic healing ticks",
            "\"You receive N points of periodic healing.\"",
            "You receive 12 points of periodic healing.", Scope.NotPlayerChat, @"^You receive .+ points of periodic healing"),

        // ── Social & trade ────────────────────────────────────────────────
        new("social.bot_spam", "Social", "Trade / buff bot spam",
            "Lines ending in the bot tags -t- or -b-.",
            "BuffBot says, \"Buffs at the Holtburg lifestone -b-\"", Scope.Any, @"-[tb]-""?\s*$"),
        new("social.failed_assess", "Social", "Failed assess on you",
            "\"Someone tried and failed to assess you!\"",
            "Bob tried and failed to assess you!", Scope.NotPlayerChat, @"tried and failed to assess you!\s*$"),
        new("social.kill_task", "Social", "Kill task complete",
            "\"You have killed N Xs! Your task is complete!\"",
            "You have killed 50 Drudges! Your task is complete!", Scope.NotPlayerChat, @"^You have killed \d+ .+Your task is complete!\s*$"),
        new("social.general", "Social", "General channel",
            "Everything said on General.",
            "[General] Bob says, \"hi\"", Scope.ChannelsOnly, @"^\[General\]|says on the General channel"),
        new("social.trade", "Social", "Trade channel",
            "Everything said on Trade.",
            "[Trade] Bob says, \"WTS\"", Scope.ChannelsOnly, @"^\[Trade\]|says on the Trade channel"),
        new("social.lfg", "Social", "LFG channel",
            "Everything said on LFG.",
            "[LFG] Bob says, \"LF fellow\"", Scope.ChannelsOnly, @"^\[LFG\]|says on the LFG channel"),
        new("social.roleplay", "Social", "Roleplay channel",
            "Everything said on Roleplay.",
            "[Roleplay] Bob says, \"…\"", Scope.ChannelsOnly, @"^\[Roleplay\]|says on the Roleplay channel"),
        new("social.society", "Social", "Society channel",
            "Everything said on your society channel.",
            "[Society] Bob says, \"…\"", Scope.ChannelsOnly, @"^\[(?:Society|Celestial Hand|Eldrytch Web|Radiant Blood)\]|says on the (?:Society|Celestial Hand|Eldrytch Web|Radiant Blood) channel"),

        // ── NPCs ──────────────────────────────────────────────────────────
        new("npc.arbitrator", "NPCs", "Master Arbitrator (Colosseum)",
            "All Master Arbitrator speech and the fellowship-lock notice.",
            "Master Arbitrator tells you, \"You shall be known…\"", Scope.Any,
            @"^Master Arbitrator (?:says|tells you)|^Your fellowship is now locked\..*you have 15 minutes to be recruited"),

        // ── Pets (ACE custom combat pets) ─────────────────────────────────
        new("pet.hit", "Pets", "Pet hits",
            "\"[Pet] … hit …\" outgoing damage.",
            "[Pet] Fire Banshee hit Drudge for 40 damage.", Scope.NotPlayerChat, @"^\[Pet\] .+ hit "),
        new("pet.evade", "Pets", "Pet attacks evaded",
            "\"[Pet] … evaded by …\"",
            "[Pet] Fire Banshee's attack was evaded by Drudge.", Scope.NotPlayerChat, @"^\[Pet\] .+ evaded by "),
        new("pet.damage_taken", "Pets", "Pet damage taken",
            "\"[Pet] … took …\" incoming damage.",
            "[Pet] Fire Banshee took 25 damage from Drudge.", Scope.NotPlayerChat, @"^\[Pet\] .+ took "),
        new("pet.death", "Pets", "Pet died",
            "\"[Pet] Your combat pet … has died.\"",
            "[Pet] Your combat pet Fire Banshee has died.", Scope.NotPlayerChat, @"^\[Pet\] Your combat pet .+ has died\.\s*$"),
        new("pet.recall_block", "Pets", "Pet recall blocked",
            "\"Your pet was in combat recently; you cannot recall it…\"",
            "Your pet was in combat recently; you cannot recall it for about 10 seconds.", Scope.NotPlayerChat,
            @"^Your pet was in combat recently; you cannot recall it"),
        new("pet.overflow", "Pets", "Pet heal overflow",
            "\"… receives N excess … from …\" boost overflow.",
            "Fire Banshee receives 20 excess health from Heal Other.", Scope.NotPlayerChat, @" receives .+ excess .+ from "),
    };

    private static readonly Dictionary<string, Preset> _byId =
        _all.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every preset in display order.</summary>
    internal static IReadOnlyList<Preset> All => _all;

    /// <summary>Group names in display order.</summary>
    internal static IEnumerable<string> Groups => _all.Select(p => p.Group).Distinct();

    internal static Preset? Get(string id) => _byId.TryGetValue(id, out var p) ? p : null;

    /// <summary>First enabled preset that matches the line, or null.</summary>
    internal static Preset? FirstMatch(string text, string channel)
    {
        foreach (var p in _all)
            if (p.Enabled && p.Matches(text, channel))
                return p;
        return null;
    }

    /// <summary>Tabs named by enabled presets (tab names create tabs implicitly, like custom rules).</summary>
    internal static IEnumerable<string> TargetTabs()
        => _all.Where(p => p.Enabled && p.Tab.Length > 0).Select(p => p.Tab);
}
