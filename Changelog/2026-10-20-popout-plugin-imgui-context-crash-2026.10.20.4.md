# Engine: a plugin section in a popped-out window no longer kills the client (2026-10-20)

RynthCore engine 2026.10.20.4. Previous release: 2026.10.19.3 (the live engine).

## The crash (SK on DreamWeave, 2026-10-19 20:15:45 and 20:19:17)

Opening the "DreamWeave server (RynthDreamWeave)" entry in a popped-out Mini Remote froze the
client and closed it about 20 seconds later. The same thing happened on a second DreamWeave client
(pid 43516). The engine logged a main-thread hang; its CLR dump shows what actually happened:

- AC's main thread (0x7894, the one that draws the pop-outs) holds a
  `System.ExecutionEngineException`, the runtime's fatal error, in
  `ServerPackPlugin.RenderEmbeddedCard` -> `ImGuiNative.igGetCursorPosY()`, called from the
  popped-out Mini Remote's frame. The "hang" was the runtime dying: writing the dump, then
  terminating the client.
- That RynthDreamWeave is rynth's managed build (API v88): it loads into its own
  AssemblyLoadContext with its own copy of ImGui.NET. The engine's DLL resolver only covers its own
  ImGui.NET, so the plugin's copy loaded a second `cimgui.dll`: a second ImGui with its own
  "current context". The dump has both modules, `RynthCore.cimgui.dll` (the engine's) and
  `Runtime\cimgui.dll`.
- A pop-out draws in a context of its own, made current only in the engine's cimgui. The plugin
  asked its own copy for the current context, got the main one (not in a frame then), and its first
  call ran against a window that wasn't there: an access violation in native code, which no
  try/catch stops.

The docked Mini Remote worked by luck: there the main context is the one in a frame. Every
managed plugin loaded the same way (RynthLua, RynthNav, RynthTracker, RynthVision, RynthInventory,
RynthSort, RynthNet, RynthThreads and the server plugins), so any of their sections in a pop-out
could do the same, on any client.

### Changed

- **Managed plugins share the engine's ImGui.** A managed plugin's load context resolves `cimgui`
  / `cimgui.dll` to the engine's own module (`ManagedPlugins.PluginLoadContext.LoadUnmanagedDll`):
  one ImGui, one current context, so a pop-out's context is the one the plugin draws in.
- **Pop-outs set the context in a second cimgui too** (`Plugins/StrayImGuiContext.cs`). When a
  `cimgui.dll` that isn't the engine's is loaded (a NativeAOT plugin that doesn't bind its ImGui.NET
  to the engine's module), a plugin call made inside a pop-out
  (`PluginManager.RenderEmbed`, ":popout" surfaces: Mini Remote sections, Guardian, RynthAi panels)
  first makes the pop-out's context current in that copy, and puts the copy's own context back
  afterwards. Logged once: `a second cimgui.dll is loaded ...`.

## Tests

- Engine builds clean. The fix is native interop with the game's ImGui, so it was checked against
  the crash dump rather than a unit test: the faulting frame is exactly the call path changed.
