# RynthAi overlay windows draw again: bind to the engine's cimgui (RynthAi 0.5.6-legacy-ui)

Date: 2026-10-05
Branches (local): RynthSuite `fix/rynthai-launcher-remote-commands`
Previous plugin version: 0.5.5-legacy-ui

## Problem

Since the aelrynth merge, none of RynthAi's own ImGui windows appear. That covers the ILT Hub, the
Pets and Quests windows, the Mini Remote, the Item HUD and the Inventory HUDs setup. The launcher
buttons and `/ra` commands reach the plugin (`applied remote command: remote=toggle` /
`pets=toggle`), but nothing shows.

## Cause

The engine now loads its ImGui build as `RynthCore.cimgui.dll` (log: `ImGuiNative: Loading
...\Runtime\RynthCore.cimgui.dll`).

RynthAi is NativeAOT. Its ImGui.NET P/Invokes ask for `cimgui`, which no loaded module is named.
Windows therefore loaded a second, different `cimgui.dll` from the install's `Runtime` folder. RynthAi
set that module's context pointer to the engine's context and built its windows through a separate
ImGui instance, so they never reached the engine's draw data. The engine heartbeat shows this: the
plugin's overlay call runs every frame (`overlayCalls` = frames), but `drawLists=3` doesn't change
after `/ra hub show` or `/ra remote toggle`.

ub-Rythai already avoids this with a DllImport resolver bound to the engine's module (log:
`ub-Rythai: ImGui native resolver bound to engine cimgui.`). RynthAi had no such resolver.

## Fix

- New `ImGuiNativeBinding`, called at the top of `RynthAiPlugin.Initialize` before any ImGui call. It
  registers `NativeLibrary.SetDllImportResolver` for the ImGui.NET assembly.
  - `cimgui` resolves to the loaded `RynthCore.cimgui.dll`, else a loaded `cimgui.dll`, else the
    default search (the old behaviour).
  - It's registered once per process, so a reused plugin DLL copy doesn't throw.
  - Logged in the UI log as `[ImGui] native resolver registered ...` and, on the first ImGui call,
    `[ImGui] cimgui bound to loaded RynthCore.cimgui.dll (0x...)`.
- No changes to the windows themselves. The Mini Remote looks as it did before the merge: stats,
  ResetXP / Report, item buttons, toggles, profile pickers, Guardian, Translate, Chat XL and Rebuff.

## Two clients no longer overwrite each other's RynthAi logs (RynthAi 0.5.7-legacy-ui)

- Before: with two clients running, RynthAi's shared log files (`Logs\Diagnostics\rynthai_*.txt`,
  `exceptions_*.txt`, `Trace\*.txt`) lost and garbled lines. Each process opened the file in Append
  mode once with `FileShare.ReadWrite`, kept a buffered writer, and wrote at its own end-of-file
  offset, overwriting the other client's lines.
- Now each file has one writing process. `RynthLog.OpenWriter` opens it with `FileShare.Read`, so
  readers are fine but a second writer is refused. When another client already holds the file, this
  process writes `{name}.{pid}.txt` beside it, e.g. `rynthai_2026-10-05.30060.txt`. The engine's own
  logs (`RynthCore.{pid}.log`) are per process the same way.
- Retention and pruning are unchanged. The per-process files are `*.txt` in the same folders, so
  they age out with the rest.
