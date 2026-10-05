# RynthChat: standard (canned) filters and easier custom rules

**Date:** 2026-10-05
**Component:** RynthCore Engine — RynthChat filters panel (`UI/Panels/RynthChatFiltersPanel.cs`)

## Summary

The Chat Filters window used to be a list of raw regex rows. It now has a
**Standard filters** page of ready-made tick-boxes, modelled on UtilityBelt-IT's
chat filter categories. Custom rules now work with plain text, so you no longer
need to know regex.

## Standard filters page

38 canned filters in 6 groups. Each group has **All on** / **All off** buttons and
an "n/total on" count, and a search box at the top narrows the list.

| Group   | Filters |
|---------|---------|
| Combat  | Your attacks evaded, You evade attacks, Damage you deal, Damage you take, Your spells resisted, You resist spells, Not-a-PK failures, Dirty Fighting procs, Monster death messages |
| Casting | Your spell words, Others' spell words, Spell fizzles, Component usage, Your buff casts, Buffs others cast on you, Spell expiry (keeps Brilliance / Prodigal / Spectral) |
| Items   | Healing kit success / failure, Salvage results / failures, Aura of the Craftsman, Mana stone usage, Periodic healing ticks |
| Social  | Trade / buff bot spam (-t- / -b-), Failed assess on you, Kill task complete, General / Trade / LFG / Roleplay / Society channels |
| NPCs    | Master Arbitrator (Colosseum) |
| Pets    | Pet hits, Pet attacks evaded, Pet damage taken, Pet died, Pet recall blocked, Pet heal overflow |

- **Tick a box** to hide those lines. **Type a tab name** beside it to move them
  to that tab instead; the tab is created automatically, like custom rules do.
- **Hover a filter** to see what it catches and an example line.
- **Player chat is protected:** filters on system text skip the Chat and Channels
  tabs, so a player typing the same words in Local is never hidden.
- **Spell words:** ACE sends incantations as chat type 0x11, which RynthChat files
  under Combat. "Your spell words" therefore never hides real Local speech.

## Custom rules page

- **Match modes:** each rule has a click-to-cycle mode button: **Contains**,
  **Starts with**, **Ends with** (plain text, case-insensitive) or **Regex**.
  - New rules default to Contains.
  - Plain-text modes match the message itself, so the timestamp prefix can't
    break "Starts with".
  - Regex mode keeps the old behaviour (matches "hh:mm:ss Sender: message"), so
    existing rules load and behave exactly as before.
- **Order:** first match wins, and ▲▼ reorder rules. Custom rules are checked
  before the standard filters, so a custom rule can override a preset.

## Line tester

Paste any chat line at the bottom of the window to see which custom rule or
standard filter catches it, and whether it is hidden or moved.

- **"as System / Combat / …":** picks the channel the line is treated as arriving on.
- **Not filtered:** if nothing catches the line, the tester names the standard
  filter you'd need to tick.
- **Make rule:** turns the test text into a Contains rule, ready to trim down.

## Settings

Saved in `%APPDATA%\RynthCore\rynthchat_settings.json`:

- `filters[].mode`: match mode. When missing, the rule is treated as Regex.
- `presets[]`: `{ id, enabled, tab }`, written only for filters you changed.
- Deleting a custom tab also turns off any standard filter that moved lines to it.

## Files

- New: `src/RynthCore.Engine/UI/Panels/RynthChatPresets.cs`
- Rewritten: `src/RynthCore.Engine/UI/Panels/RynthChatFiltersPanel.cs`
- Modified: `src/RynthCore.Engine/UI/Panels/RynthChatPanel.cs` (match modes,
  preset routing, tester, persistence)
