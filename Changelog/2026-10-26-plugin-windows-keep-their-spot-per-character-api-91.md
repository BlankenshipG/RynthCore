# Plugin windows keep their spot per character (plugin API v91)

Floating windows that plugins draw themselves (the Item HUD, the Mini Remote, the
Nav HUD, RynthAi's legacy settings windows, ub-Rythai's windows, RynthILT's pages,
the server packs' server window) kept their spot and size only in imgui.ini. Every
running client shares that one file and rewrites all of it, so a window moved or
resized in one client came back where another client's next save had it.

The engine now keeps those spots per character, the way its own panels already
were: `%LocalAppData%\RynthCore\window_state.<server>.<character>.txt`, one line
per window (`<key>=x,y,width,height`). A window is saved when the player lets go
of the mouse after moving or resizing it; moves the code makes are not saved.

- Engine: `UI/WindowPlacementStore.cs`; API v91 `GetWindowPlacement` /
  `SetWindowPlacement` (`Plugins/WindowPlacementApi.cs`). SDK:
  `RynthCoreHost.HasWindowPlacements`, `TryGetWindowPlacement`, `SetWindowPlacement`.
- Plugins: one shared control, `RynthSuite/Plugins/Shared/SavedWindow.cs` (linked
  source). The plugin sets `SavedWindow.Host` at start; each window calls `Apply()`
  right before `ImGui.Begin` and `Track()` inside it.
- RynthAi: the Item HUD and Mini Remote placement fields added to huds.json on
  2026-10-26 are gone (the shared control replaces them); place those two windows
  once more after updating.
- On an engine older than v91 the windows behave as before (imgui.ini).
