# Floating panel windows: destroy on the owner thread, create on the game thread (2026-10-04)

Ships in the next installer after 2026.10.4.15.

## Destroy each panel window on the thread that owns it

`LayeredWindow.Dispose` used to call `Win32Backend.PostDestroyWindow`, which always
posted the destroy to the game window. That was wrong for any panel not created on
the game thread (for example the login-time pop-out, created on the Avalonia UI
thread). The game thread's `DestroyWindow` then failed because it didn't own the
window, and the panel stayed on screen, frozen. The post was also dropped if the
game WndProc had already been unhooked at shutdown.

- New `LayeredWindow.DestroyOnOwnerThread`: it looks up the window's owning thread with
  `GetWindowThreadProcessId`.
  - If the window no longer exists, it logs and returns.
  - If the caller owns the window, it calls `DestroyWindow` inline.
  - Otherwise it posts the new `WM_RYNTH_SELF_DESTROY` (0x8005) to the panel window itself.
    The panel's own `StaticWndProc` then destroys it on its owner thread. This never
    blocks the caller, and it works after the game WndProc is unhooked.
- `PostMessage` in `LayeredWindow` now sets the last error, so a failed post is logged
  with the real error code.
- Removed `Win32Backend.PostDestroyWindow` and its `WM_RYNTH_DESTROY_HWND` (0x8003)
  handler; 0x8003 is reserved and not reused. The shutdown sweep
  (`WM_RYNTH_SWEEP_PANELS`) remains as a backstop for panels that were never disposed.

## Hold the login-time pop-out until panels can be created on the game thread

Floating panels saved as popped out are restored when `LoginComplete` fires. That can
happen before the engine has found and subclassed the game window, so
`FloatingPanelHost` took its legacy path and created the window on the Avalonia UI
thread. That path brings back the Win11 `WS_EX_NOACTIVATE` click-drop race and the
cross-thread destroy problem above.

- New `Win32Backend.CanRunOnGameThread`: true once the game window is known and its
  WndProc is subclassed, which is what `RunOnGameThread` needs. The window handle
  alone isn't enough.
- `AvaloniaOverlay.RestoreFloatingPanels` now polls every 200 ms on the UI thread until
  `CanRunOnGameThread` is true, then restores. It logs once when it starts holding and
  again when it releases.
- After 60 s it restores anyway on the legacy path and logs a warning line, so the panels
  are not lost on a client that is never hooked.
