# RynthAi 0.5.2-legacy-ui: Char / Hub launchers open their windows again

Date: 2026-10-05
Branch: RynthSuite `fix/rynthai-launcher-remote-commands` (local, based on `merge/aelrynth-2026.10.5.1` @ 532fa67)
Previous plugin version: 0.5.1-legacy-ui

## Problem

On 2026.10.5.3 the dashboard's **Char** and **Hub** launcher buttons showed, but clicking them did
nothing: no ILT Hub, no Mini Remote, no Inventory HUDs window.

The engine log showed every click arriving at RynthAi
(`[RynthAi] applied remote command: hub=show`, `remote=toggle`, `huds=show`), and the overlay was
drawing (`OnRenderOverlay drawing (iltHub=ready)`). The ILT Hub diagnostics had no
`command: /ra hub ...` line for any click.

## Cause

The launchers send remote commands through the engine broker into `RynthAiPlugin.ApplyRemoteCommand`.
The aelrynth 2026.10.5.1 version of that switch has no `hub` / `quests` / `huds` / `itemhud` /
`remote` / `miniremote` cases. SK-local had them, and the merge dropped them. The commands fell
through the switch untouched, and the trailing log line still reported them as "applied".

Typed `/ra hub ...` commands use a separate dispatcher (`DispatchRaCommand`) that still had the cases.
That is why `/ra hub status`, `force on` and `refresh` worked, but no button did.

## Fix

- `ApplyRemoteCommand` routes `hub` / `quests` to `IltHubController.HandleCommand`, and routes
  `huds` / `itemhud` / `remote` / `miniremote` to `HandleHudCommand`. These are the same handlers as
  the typed commands, and both run on the pump thread.
- Unknown remote commands log `ignored unknown remote command: ...` instead of falsely logging
  "applied".
- `map` / `lua` were not restored. Upstream's engine owns those windows now.

## Typed equivalents

- `/ra hub show`: ILT Hub (the Char button)
- `/ra remote toggle`: Mini Remote (Hub button, left-click)
- `/ra huds show`: Inventory HUDs setup (Hub button, right-click)

## Not yet shipped

Built locally; not in an installer yet (next would be 2026.10.5.4). Nothing pushed.
