# RynthCore 2026.10.5.48: known-server list in the launcher's Add Server dialog

Local full install. Previous release: 2026.10.5.47.

## What changed

- **Launcher: Known server picker.** The Server Profile dialog (Add Server and Edit) has a "Known
  server" list at the top. Picking a server fills in Server Name, Host, Port and Emulator; Alias
  and Rodat keep what you typed. "Custom server" leaves the fields alone, so any server still
  works. When you edit a saved profile, the list selects the known server whose host matches.
- The list (`src/RynthCore.App/KnownServerCatalog.cs`) has the 17 servers RynthSuite ships a
  server plugin for. They come from the public server list (github.com/acresources/serverslist,
  checked 2026-10-13) plus Aelrynth (aelrynth.com:9020). Infinite Leaftide is
  `game.infiniteleaftide.online:9000` (ACE), with plugin RynthILT.
- Host matching: the same host, or one under the known host's own domain
  (`infiniteleaftide.online` finds ILT). Hosts on shared dynamic-DNS domains (ddns.net, duckdns.org
  and others) and IP addresses match only themselves.

The picker was written on `feat/dynamic-sections` (commit 8ee7ef4), which had not reached this
branch, so 2026.10.5.47 shipped without it. This brings over only the launcher part:
`KnownServerCatalog.cs` is byte-identical to that commit. The rest of that commit (the
server-plugin load gate, the SDK manifest "server" block) is not included.

## Files

- `src/RynthCore.App/KnownServerCatalog.cs` (new): the catalog, `FindById`, `FindByHost`, `ApplyTo`.
- `src/RynthCore.App.Avalonia/ServerProfileDialog.axaml(.cs)`: the picker and its note line.
- `src/RynthCore.App.Avalonia/RynthCore.App.Avalonia.csproj`: links the catalog.
- `tools/LauncherSettingsTests`: "known servers" scenario (129 checks pass).

## Also in this install (RynthSuite)

RynthILT 0.2.2 (Leaftide feature list, "RynthILT.Server" interface), RynthOracle 0.2.1 (Infinite
Leaftide fellowship tab), RynthAi 0.5.56-legacy-ui (fellowships up to 29). See RynthSuite
`Changelog/2026-10-13-ilt-features-oracle-fellowship.md`.

Not yet tested in game.
