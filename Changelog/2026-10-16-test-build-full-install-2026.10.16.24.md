# 2026.10.16.24 — test build full install

**Kind:** Hot push (test build installer). Previous installer `2026.10.16.22`.

Built from `test/hotbuild-2026-10-16-on-aelrynth` (Core + Suite, current `aelrynth/main` merged in).

## New since 2026.10.16.22

- **Chat:** Hide rules and standard filters keep lines out of AC's own chat window too
  (option under Standard filters, on by default); clients re-read the shared
  `rynthchat_settings.json` when another client saves it.
- **RynthILT 0.2.16:** follows RynthCore's server verdict again, so RynthAi's Mini Remote ILT
  tabs use the engine's Active flag and the character override (`/rc server ilt on|off|auto`);
  Vaulted Dungeons settings load and save in the NativeAOT build.

## Install

Close every AC client first (the loader and engine are replaced).
