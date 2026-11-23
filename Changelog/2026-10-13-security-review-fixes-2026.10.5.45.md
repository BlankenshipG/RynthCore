# RynthCore 2026.10.5.45: security review fixes (engine, StatusAgent 1.0.1, installer)

Branch `fix/security-review-2026-10-13`, from d76e062. It pairs with the RynthSuite branch of the same
name (RynthAi 0.5.54); build the two together.

| Component | Previous | Now |
|---|---|---|
| Engine | 2026.10.5.44 | 2026.10.5.45 |
| StatusAgent | 1.0.0 | 1.0.1 |

## F1: a '%' in shown text could crash the client

This is the same printf-format problem as in RynthSuite. ImGui.NET passes `Text`, `TextColored`,
`TextWrapped`, `TextDisabled`, `BulletText`, `SetTooltip`, `SetItemTooltip` and `LabelText` strings to
native ImGui as a format string with no arguments.

- New `src/RynthCore.Engine/ImGui/SafeImGui.cs` (root namespace) doubles every `%`.
- 246 calls in 39 engine files use it, among them the shell, the chat, item, inventory, loot-editor,
  meta and nav panels, and the plates.
- Script windows already drew through `igTextUnformatted`.
- `Panels/SkillsFace.Progression.cs` (5 calls) is left for after the local uncommitted work in it lands.

## L1: the engine's own server-reply readers

The engine eats its own `/mastery-data` and `/mule-data` replies before AC draws them. A player could
type a copy and have it read or hidden. For the mastery reader this worked through an emote, which has
no quote for `MasteryWire`'s check to catch.

- New `Net/ChatOrigin.cs` holds the player chat types (ACE `ChatMessageType`) and a line-start marker
  match. It is also compiled into NetMessageTests.
- `ChatCallbackHooks.EatOwnReply(line, chatType)` skips player chat types. That covers both feeds.
- `AelrynthVaultWire`:
  - `IsMuleLine` and `TryParse` need `~ael1 ` at the start of the line.
  - `IsUnknownCommand` needs "Unknown command:" at the start and exactly `mule-data`.
- `VaultWireTests` gains spoof cases.

## M2: the status server, before it is opened to other PCs

What it already did: it requires a token (compared in constant time), binds 127.0.0.1 by default, and
its file endpoints take only numbers (pid, landblock, icon id), so there is no path traversal.

New in `ServeGuard.cs`:

- **Token length.** A `ServePrefix` other than 127.0.0.1, localhost or [::1] needs a `ServeToken` of at
  least 16 characters. The agent says so and stays off. Before, it showed the urlacl hint.
- **Lockout.** 10 bad tokens from one address within a minute lock it out for 5 minutes, even with the
  right token. Loopback is never locked out. The lockout is logged once.
- **Bounded request bodies.** A command may be 64 KB, a WebRTC offer 256 KB, a client log 16 KB.
  Anything larger gets 413. Before, every body was read whole.
- **Safe log text.** Client text in the log (user agent, client log, SDP lines, command values) has
  control characters replaced and is capped, so nobody can forge log lines.
- **Startup warning.** A non-loopback bind is plain HTTP; the agent says to use it only inside a VPN
  such as Tailscale or WireGuard.

StatusAgentTests gains a ServeGuard section: 146 checks pass.

## Low: MinHook load and the .NET runtime download

- **MinHook.** `MinHook.ResolveApi` loaded `minhook.x86.dll` by bare name. When the early preload
  missed it, Windows' search order (the AC folder, the current directory, PATH) could load a planted
  copy into the client. It now loads the file that `EntryPoint.FindNativeDll` finds in the engine's own
  folders, and fails if there is none.
- **.NET runtime download.** The installer downloaded aka.ms's moving "latest" .NET 10 Desktop Runtime
  and ran it unchecked. It now downloads 10.0.12 (x86) from builds.dotnet.microsoft.com and Inno Setup
  checks its SHA-256 first:

  ```text
  57c1d40967b73785b9ef95de5f0878e9264423228971a71eb362c03ae01937f5
  ```

  On 2026-10-13 that file matched Microsoft's release-metadata SHA-512 and carried a valid Microsoft
  Authenticode signature. The `.iss` explains how to move to a newer build. Any 10.x already installed
  is still accepted.

## Tests

| Suite | Result |
|---|---|
| NetMessageTests | 311 checks pass |
| StatusAgentTests | 146 checks pass |
| All other RynthCore `tools` test projects | pass |

The engine builds. The installer script compiles; it was checked with the staging files skipped.
