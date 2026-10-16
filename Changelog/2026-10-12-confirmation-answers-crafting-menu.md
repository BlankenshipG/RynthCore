# Engine API v81: answer the server's craft dialog; Char menu > Crafting

RynthCore engine, branch build-comp-10-11. It pairs with RynthAi 0.5.51 (the Crafting tab).

## What changed

- **Confirmation answers** (`Compatibility\ConfirmationResponder.cs`, new):
  - Binds `CM_Character::Event_ConfirmationResponse` (0x006A2030, by byte pattern) at probe time
    (`ClientHelperHooks`).
  - `SmartBoxHooks` hands every ConfirmationRequest (game event 0x0274) to it. When a plugin has
    armed that prompt type, the engine reads the "N percent" from the text, answers Yes when N
    is at least the plugin's minimum (or the minimum is 0) and No otherwise, and the client's
    dialog never opens. AC's handler returns 0 on every path, so skipping it is safe.
  - Unarmed prompts go to the client as before and are only recorded.
- **Plugin API v81** (`PluginContract`, `PluginManager`, SDK):
  - `ArmConfirmationFn(type, minPercent, timeoutMs)`: one-shot, up to 120 s; timeout 0 disarms.
  - `GetLastConfirmationFn(...)`: the last prompt's type, context, percent, outcome (shown,
    accepted, declined, send failed) and age, with a sequence number.
  - SDK: `RynthCoreHost.HasConfirmationAnswers`, `ArmConfirmation`, `TryGetLastConfirmation`,
    `ConfirmationKind`, `ConfirmationOutcome`; `RynthCoreSdkApiVersion` 81.
- **RynthAi Char menu** (`ImGui\Panels\RynthAiFace.cs`): "Crafting" after Pets opens the Mini
  Remote's Crafting tab (`/rc miniremote tab crafting`).
- `tools\PluginManifestTests`: the two new slots are checked at the end of the table.

## Not tested in game yet

The engine builds and the manifest tests pass (182 assertions). The dialog answer hasn't been
tried against a live server yet.