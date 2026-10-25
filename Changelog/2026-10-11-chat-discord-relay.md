# Chat: Discord relay in RynthChat's settings

**Date:** 2026-10-11
**Area:** Engine (`RynthCore.Engine`), RynthChat face. Ships with RynthAi 0.5.34-legacy-ui, which
removes RynthAi's own relay (RynthSuite `Changelog/2026-10-11-release-0.5.34-discord-relay-moved-to-chat.md`).

## What's new

The chat window can copy chat to Discord, configured from the chat window itself:

- **Chat > gear menu > Discord:** a "Copy chat to Discord" switch, the relay's status, and
  **Discord settings…**, which opens the new **Discord** panel.
- **Discord panel, Routes tab:** a route is one Discord channel and the chat it receives. You can
  have as many routes as you like, for example tells to one channel and allegiance to another.
  - **Connection:** a webhook URL, or a bot token plus channel ID, with Save, Clear and
    **Send test**. The webhook display name defaults to the character's name. Paused routes have a
    **Resume** button.
  - **Speech channels:** Local, Tell, Fellowship, Allegiance, Patron, Vassals, Covassals, Monarch,
    General, Trade, LFG, Roleplay, Society and Olthoi, with All, None and Defaults buttons. The
    defaults are tells, fellowship and the allegiance channels.
  - **Chat tabs:** every line that shows in a ticked tab is relayed. This covers the built-in tabs
    (Chat, Channels, System, Combat, Rynth, Other) and any tabs you have made with chat rules.
  - **Keywords:** optional. When set, only lines that contain one of the words are sent.
  - **How often:** each route uses the relay's default pacing or its own. The batch window
    (0–60 s) groups lines that arrive close together into one message. The message spacing
    (1 s–10 min) sets the minimum time between posts. The panel shows the resulting messages per
    minute.
- **Settings tab:**
  - Who relays: only these characters, skip your own lines, and skip lines a chat rule hides.
  - Message format: game time and a `[Character]` tag.
  - The default pacing.
  - Queue and log limits.
- **Log tab:** what was sent, errors, and pauses.
- **`/rc discord [on|off|status|window|test [route]|resume [route]]`**.

## Behaviour

- The relay reads RynthChat's own line feed. It keeps that feed running while the relay is on,
  even with the Chat window closed. Scrollback from before the relay started (engine start, hot
  reload, switching the relay on) is never re-posted.
- Discord limits are honoured:
  - Messages are packed to 2000 characters.
  - 429 responses wait for `retry_after`, and the `X-RateLimit-*` buckets are respected.
  - 5xx and network errors retry with backoff.
  - 400, 401, 403 and 404 pause the route until **Resume**, so a bad credential isn't retried.
- Mentions can't ping (`allowed_mentions.parse = []`), Markdown and `@everyone` are escaped, and
  embeds are suppressed.
- Webhook URLs and bot tokens are encrypted with Windows DPAPI for the current user. The plain
  text is never saved or logged, and errors are redacted. Encryption and file writes run off AC's
  thread.
- Settings are shared by every client and character in
  `%AppData%\RynthCore\rynthchat_discord.json`. With several clients in one fellowship or
  allegiance, set "only these characters" so each line is posted once.
- **Import:** the first start with no settings file imports RynthAi 0.5.33's
  `RynthAi\discord.json`. The import keeps the routes, saved secrets (same DPAPI entropy), channels
  and keywords; "other lines" become the Other tab.

## Files

- New: `src/RynthCore.Engine/UI/Data/Discord/`:
  - `ChatDiscord.cs` (runtime)
  - `ChatDiscordLogic.cs` (per-line decisions)
  - `ChatDiscordSettings.cs` (settings, store and import)
  - `ChatSpeech.cs` (speech channel parsing)
  - `DiscordClient.cs`, `DiscordFormat.cs`, `DiscordSecrets.cs`
- New: `src/RynthCore.Engine/ImGui/Panels/ChatDiscordFace.cs` (the Discord panel).
- Changed:
  - `UI/Data/ChatData.cs`: new lines and a tick go to the relay from the chat poll.
  - `ImGui/Panels/ChatFace.cs`: the Discord section in the gear menu.
  - `EntryPoint.cs`: loads the settings and registers the panel when RynthChat is present.
  - `EngineLifecycle.cs`: cancels in-flight posts on shutdown.
  - `Compatibility/RynthCoreChatCommands.cs`: `/rc discord`.
- New tests: `tools/ChatDiscordTests` (`dotnet run -c Release`): 218 checks, all passing.

## Verification

- `dotnet build -c Release` of the engine: no errors and no new warnings.
- NativeAOT `dotnet publish -c Release`: no new trim or AOT warnings. The Avalonia ones are
  unchanged.
- `tools/ChatDiscordTests`: PASS (218 checks). The tests cover speech parsing, route selection,
  formatting and escaping, scrollback detection, batching, validation of pasted values, the request
  body and 429 handling, settings round-trip and repair, the RynthAi import, and DPAPI.
- Not yet tried in game against a live Discord channel.
