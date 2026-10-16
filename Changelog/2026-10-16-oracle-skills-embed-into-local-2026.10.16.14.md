# 2026.10.16.14 — Oracle Skills embed into local Engine (fix Skills bar / ILT bridge)

**Previous:** 2026.10.16.13 (DXVK smoke). Hot push.

## What was wrong

Games was mixed:

- **RynthOracle 0.3.1** (sk) expects `/rc oracle-skills` and embeds Skills / Attributes / Progress inside Oracle.
- **Engine 2026.10.16.13** (local) still had the **Skills top-bar button** and **no** `oracle-skills` bridge → log: `unknown subcommand '/rc oracle-skills 2'`.
- Oracle Character charms / Pet Breeding read `RynthILT.Data` only while those tabs are open; Progress planners also need the embed + RynthILT. Without the bridge, Oracle looked “empty” of ILT skill/progress UI.

## Fix (local Engine, DXVK kept)

Port from sk `ae6de53`:

- `OracleSkillsEmbed.cs` + `/rc oracle-skills`
- `SkillsFace.DrawEmbedded` / hosted body
- `ScriptWindowFace` draws embed for `RynthOracle/Oracle`
- Remove Skills (and Item Count / Mini Remote) from `ImGuiBar`
- Char menu → `/ro skills|attrs|progress`
- **Inventory title bar / modern header:** Count, GInv, Sort, Remote buttons
  (bar removal left them nowhere until this port)
- **Radar → RynthNav:** removed from ImGui/DComp top bar; RynthNav header
  Radar toggle + `/rc radar` + Char menu; OverlayHost/RadarFace/snapshot/
  settings/`/rc ui open Radar` unchanged
- **Chat combat standard filters:** port sk `2026.10.16.2` / `.10` presets
  (`ChatFilterPresets` + `RynthChatPresets`) so Combat Standard filters
  (melee/magic deal/take, recklessness, sneak, stamina, etc.) show in
  Chat Filters again — live Engine 13 still had the short Combat list

## Deploy

`Publish-EngineCoreClr.ps1 -EngineOnly -AllowBehind -MsBuildArgs @('-p:Version=2026.10.16.14')`  
→ `C:\Games\RynthCore\Runtime\`, then reinject / relog.
