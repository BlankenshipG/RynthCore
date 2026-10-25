# Plugin render snapshot fix and ImGui pipeline diagnostics (2026-10-04)

Ships in installer 2026.10.4.15.

## Fix: plugin overlay windows never drew (ILT Hub not showing)

`PluginManager.RenderAll` and `RenderOverlayAll` iterate `_pluginsRenderSnapshot`, a
copy of the plugin list published by `PublishPluginsRenderSnapshot()`. Since the aelrynth
2026.9.27.2 snapshot port (commit 4ddbb52e), the publish call that belonged at the end of
`LoadPluginsFromDisk()` had landed after the `return` in `TryGetCanonicalPath()`, where it
could never run. The only publish that ran was in `UnloadAllPlugins()`, which published an
empty list. The result was that every plugin's ImGui render and overlay callbacks were
skipped, so windows like the RynthAi ILT Hub never appeared even though `hub show` worked.

- `LoadPluginsFromDisk()` now publishes the snapshot after loading and logs the plugin count.
- The unreachable call has been removed.
- `RynthCore.Engine.csproj` now treats CS0162 (unreachable code) as a build error, so a
  misplaced statement like this one fails the build.

## Diagnostics: per-frame ImGui pipeline

All of these lines appear in the normal log at Info, and each is written once (or when its value changes):

- `reached ImGui gate (EnableImGuiBackend, EnableImGuiShell, EnablePluginOverlayWindows)`.
- ImGui init: success, or an Error naming the failing step (EnsureCore, Win32Backend.Init,
  DX9Backend.InitImGui), and an Error if frames are skipped because init failed.
- `first ImGui frame started` with the display size, and `first deferred ImGui draw submitted`.
- Which branch the frame took (shell, overlay or none), logged when it changes.
- `RenderOverlayAll`: waiting for login, then `login seen, N of M plugin(s) will draw overlay windows`
  with each plugin's state (overlay / no-overlay, not-init, failed).
- A heartbeat every 30 s: frames completed, draws submitted, last-frame draw lists and vertices,
  overlay calls, login seen, overlay plugin count.
- Engine-frame and ImGui-frame exceptions: the first one is logged as an Error with the full exception,
  then a count every 30 s instead of one line per frame.
