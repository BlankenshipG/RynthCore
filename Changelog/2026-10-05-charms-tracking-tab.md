# Charms Tracking tab (Settings) - RynthAi 0.5.10-legacy-ui

Date: 2026-10-05
Previous RynthAi version: 0.5.9-legacy-ui
Next installer: 2026.10.5.9 (not built yet; includes the Nav breadcrumb / route overlay toggles)

## Charm list check against ACECustom

RynthAi's charm registry (`IltHub/IltInventory.cs`, `Charms`) was compared with ACECustom's
`CharmAbilityRegistry.cs`. All 13 registry charms are present with every tier WCID:

| Charm | WCIDs | Player ability flag |
|-------|-------|---------------------|
| Mana Barrier | 777700001, 777700054, 777710004, 777720004 | 50010 |
| Infinite Casting Stone | 777700019, 777700055 | 50028 |
| Asheron's Favor | 777700020, 777710002, 777720002 | 50030 |
| Artisan's | 777700021, 777710003, 777720003 | 50031 |
| Shrapnel | 777700022 | 50032 |
| Agony | 777700023 | 50033 |
| Split Cast | 777700024 | 50035 |
| Explosive Arrow | 777700025, 777710005, 777720005 | 50036 |
| Omni Strike | 777700026 | 50037 |
| Fork | 777700027, 777710007, 777720007 | 50039 |
| Auto-Rebuff | 777700300 | none (server keeps it in memory) |
| Summon Essence Refill | 78780030 | 9049 |
| Universal Summoning Mastery (universal pet charm) | 78780031 | 50038 |

Guardian Hand is not an ACECustom registry charm; RynthAi still recognises it by name on the Gear window.

## New: Settings > Charms Tracking

The engine Settings panel has a new last sidebar tab, **Charms Tracking**, with one row per charm:

- **Acquired**: `Carried` (in your pack or equipped, with a count when you have several),
  `Stored` (this character carried it before; RynthAi remembers it per character in
  `ilt-hub.json`, because the client only sees carried items) or `Not yet`.
- **Status**: `ON` / `OFF` from the charm's appraisal text ("Status: ON"), falling back to the
  character's ability flag. Appraisal is requested automatically (throttled to every 30 s per item).
- **Tier**: current / max tier (CharmLevel / CharmMaxLevel, or "Charm [Tier x/y]" from the appraisal).
- **Server**: `Disabled` when the server refused the charm (Auto-Rebuff, Universal Summoning
  Mastery) or a feature dump reported it off, `Enabled` when reported on, otherwise `-`
  (players can't query the server's /charms list).
- Hover a charm name for its effect. A summary line shows how many are acquired and active.
- Off ILT worlds the tab says so and points to `/ra hub force on`.

## Fix

- Pets: an Infinite Casting Stone that is switched OFF no longer counts as "casting supplies"
  (an OFF stone doesn't waive spell components).

## Files

- RynthSuite: `IltHub/IltCharmTracker.cs` (new), `IltHub/IltInventory.cs` (charm flags, effects,
  tier reader, casting-stone check), `IltHub/IltHubState.cs` / `IltHubStore.cs` (`CharmsSeen`),
  `IltHub/IltHubController.cs`, `PluginExports.cs` (`RynthPluginGetCharmsJson`),
  `RynthAiPlugin.LocalFeatures.cs`, `RynthAiVersionCommand.cs` (0.5.10-legacy-ui).
- RynthCore: `UI/Data/CharmsData.cs` (new `CharmsSource`), `UI/Data/UiSources.cs`,
  `ImGui/Panels/SettingsFace.cs` (Charms Tracking tab).