# Chat auto-translation (inbound and outbound)

**Date:** 2026-10-05
**Component:** RynthAi plugin (RynthSuite `Plugins/RynthCore.Plugin.RynthAi`)
**Version:** 0.6.31 → 0.6.32

## What's new

RynthAi can translate chat automatically, modelled on UB-IT's Chat Translate tool
but with a choice of translation service.

- **Incoming:** lines on the channels you tick are translated into your receive
  language. Each translation is written back to chat as
  `[Translate] [General] Bob (es): Hello everyone`, in the same colour and tab as
  the original. It shows in AC's chat window and in RynthChat (the RynthAi
  dashboard chat and the RynthChat panel). Lines already in your language are
  left alone.
- **Outgoing:** what you type on the ticked channels is held back, translated into
  your send language and sent with the same command (`/f`, `/t Bob,`, `/cg`, ...).
  This works from AC's chat bar, RynthChat and meta chat commands. Start a line
  with `\` to send it untranslated. If the translation fails, the original is sent
  instead (this can be switched off).
- **Channels:** each of these can be ticked separately to listen to, send on, or
  both: Local, Tell, Fellowship, Allegiance, Patron, Vassals, Covassals, Monarch,
  General, Trade, LFG, Roleplay, Society and Olthoi.
- **Translation services:**
  - **Google Cloud Translation** (default): needs an API key; 500k characters a
    month are free.
  - **DeepL:** Free-plan keys ending in `:fx` are sent to the free endpoint
    automatically.
  - **LibreTranslate:** your own server URL, or the public server with a key.

### Chat Translate window

Opened with `/ra translate window` or the Mini Remote's "Window" button. It has:

- **Header:** Enabled, Incoming and Outgoing switches, counters (translations
  done, characters used, requests queued) and the last error.
- **Language row:** Receive, a swap button (<->), Send, and Default (puts the send
  language back to the default).
- **Log:** every translation, both directions. Hover a line to see the original.
- **Compose:** pick a channel (and a tell name), type in any language, then
  Translate & send, or Preview only.
- **Channels:** a Listen and a Send checkbox for every channel, plus all / none /
  defaults buttons.
- **Settings:** the same page as Advanced Settings > Translate.

### Settings (Advanced Settings > Translate)

- Service, API key (hidden unless "Show" is ticked) and endpoint.
- **Receive (my language)**, the language incoming chat is in (auto-detect by
  default), and the **Default send language**. Every login starts with the
  default send language.
- Skip my own lines, write incoming translations to chat, show what I typed after
  sending, send the original on failure, the send-untranslated prefix, the minimum
  time between requests (750 ms default) and the log size.
- A Test box that translates into the current send language.

### Mini Remote (hub)

A new **Translate** row has an on/off box, Receive and Send pickers with a swap
button, a "def" button to go back to the default send language, and a Window
button. It can be hidden from the Mini Remote's right-click menu.

### Commands

`/ra translate` (or `/ra tr`):

- `on` / `off`
- `in [on|off]` and `out [on|off]`
- `window`
- `send <lang>`, `recv <lang>`, `default <lang>`
- `swap`
- `reset` (send language back to the default)
- `test <text>`
- With no argument it prints the status.

## How it works

- `ChatTranslator` runs on the plugin pump thread.
  - Chat hooks and the UI only add work to a queue.
  - One HTTP request runs at a time, spaced by the minimum interval. Outgoing
    lines go ahead of incoming ones.
  - Incoming lines are de-duplicated for 5 s, and the incoming queue is capped at
    40.
- **Incoming parsing:**
  - Understands `Name says, "..."`, `Name says on the X channel, "..."`,
    `Name tells you, "..."` and the `[Channel]` prefix.
  - Removes tell-link markup.
  - Expands AC's `<hhhh>` Unicode escapes before translating.
  - The channel comes from the prefix, the "on the X channel" text, the chat
    colour, or words such as "patron" and "vassal".
  - Your own lines and the translator's `[Translate]` echoes are skipped.
- **Outgoing:**
  - The translated text is resent through the chat parser with the original
    command prefix.
  - A short-lived "resend" set lets that line pass back through `OnChatBarEnter`
    without being translated again.
  - Characters above U+00FF are sent as `<hhhh>` escapes. Latin-1 letters such
    as ä and ñ stay as they are.
- **Chat echoes:** `Host.WriteToChat` drops calls made within 100 ms of each other
  off AC's main thread, so echoes are queued 150 ms apart and retried when one is
  dropped.
- **HTTP:** a single `HttpClient` with a 15 s timeout. Requests are built with
  `Utf8JsonWriter` and responses read with `JsonDocument`, which is safe for
  NativeAOT (the publish finishes with no trim or AOT warnings).
- **Settings file:** `RynthAi\translate.json` is shared by every character. It is
  saved every 2 s, and only when something changed.

## Notes

- The API key is stored in plain text in `translate.json`.
- AC's chat font can't draw CJK, Thai or Arabic script. Translations into those
  languages show as boxes in AC chat.
- Listening to Local also translates NPC speech, which uses more of your quota.
  Local is off by default.
- The Advanced Settings page list gained "Translate" before "Diagnostics".

## Files

- New:
  - `Translate/ChatTranslator.cs`
  - `Translate/TranslateClient.cs`
  - `Translate/TranslateSettings.cs`
  - `Translate/TranslateChannels.cs`
  - `Translate/TranslateLanguages.cs`
  - `Translate/AcChatEscapes.cs`
  - `Translate/TranslateUi.cs`
- Modified:
  - `RynthAiPlugin.cs`
  - `RynthAiCommands.cs`
  - `Huds/MiniRemoteHud.cs`
  - `Huds/HudController.cs`
  - `Huds/HudState.cs`
  - `LegacyUi/LegacyAdvancedSettingsUi.cs`
  - `LegacyUi/LegacyDashboardRenderer.cs`
  - `LegacyUi/LegacyUiSettings.cs`
  - `LegacyUi/RynthAiJsonContext.cs`
  - `RynthCore.Plugin.RynthAi.csproj`
