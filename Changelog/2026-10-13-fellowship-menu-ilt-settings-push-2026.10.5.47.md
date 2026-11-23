# RynthCore 2026.10.5.47: Fellowship in the Char menu, no Hub launcher, ILT settings for RynthILT

Branch `feat/dynamic-sections`, after 8cf45a0 (2026.10.5.46). It pairs with RynthAi 0.5.56 and
RynthILT 0.1.1 on the RynthSuite branch of the same name; build them together.

| Component | Previous | Now |
|---|---|---|
| Engine | 2026.10.5.46 | 2026.10.5.47 |

## RynthAi dashboard

- **Fellowship in the Char menu.** The Char launcher's right-click menu has **Fellowship** with the
  other universal Mini Remote tabs. It runs `/rc miniremote tab fellowship`, which opens the
  Mini Remote on RynthAi's Fellowship view. The Char tooltip lists it.
- **The Hub launcher is gone.** It opened the same Mini Remote window as the bar button. The
  launcher row is now nine buttons.
  - The Mini Remote window: the bar's Mini Remote button, `/rc miniremote`, or Char > Mini Remote.
  - The Mini Remote drawer: its drawer tab on the dashboard's edge.
  - The drawer's tooltip and its "can't draw here" notes no longer mention Hub.

## ILT settings for RynthILT

RynthILT 0.1.1 shows a copy of the Infinite Leaftide settings. The engine now gives it what it needs:

- **The "servermenu" push also goes to RynthILT** when it is installed. It is sent through
  `RynthPluginApplyRemoteCommand`, when it changes and every 10 s, because RynthILT can load after
  the last change. A push that isn't delivered is tried again on the next check (1 s).
- **The push carries more:**
  - `V` lines add a seventh field, the verdict's reason (for example "forced on for this character").
  - New `N` lines list a server's world names: `N  ilt  <pattern>  default|engine.json`.
  - RynthAi 0.5.54 and older skip unknown line kinds and extra fields, so the change is compatible.
- New `PluginManager.SendPluginCommand(plugin, action, value)`: the engine's own command to a loaded
  plugin's optional `RynthPluginApplyRemoteCommand`. It returns false when the plugin isn't loaded or
  doesn't have the export.

## Other

- `/rc miniremote tab` help lists `fellowship`.

## Files

- `ImGui/Panels/RynthAiFace.cs`: the Hub launcher is removed; Fellowship is added to the Char menu.
- `ImGui/Panels/RynthAiFace.Drawers.cs`: texts.
- `UI/ServerMenus.cs`: the RynthILT push, reasons, world names.
- `Plugins/PluginManager.cs`: `SendPluginCommand`.
- `EntryPoint.cs`: `PushToRynthIlt`.
- `Compatibility/RynthCoreChatCommands.cs`: comment.
