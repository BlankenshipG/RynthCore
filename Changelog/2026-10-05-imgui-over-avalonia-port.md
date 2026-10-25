# ImGui windows draw over the Avalonia panels again (merge branch)

Date: 2026-10-05
Branch: RynthCore `merge/aelrynth-2026.10.5.1` (local)
Restores: SK-local 6b94752 (draw order part)

## Problem

In 2026.10.5.4 the ILT Hub, Mini Remote and Inventory HUDs open, but an Avalonia panel covering
the same screen area hides them. The Hub, saved at about (540,105), 633x786, only shows where no
panel covers it.

SK-local 6b94752 submitted ImGui draw data after the Avalonia blit. The aelrynth merge took
upstream's `EngineFrameController`. Upstream builds the ImGui frame in `BuildImGuiFrame` and
submits it inside `OnEndScene`, before `EndSceneHook` blits the Avalonia quad. So the panels drew
on top again.

## Fix

- `EngineFrameController.OnEndScene` keeps the built frame (device + timing) instead of submitting
  it. The pending frame is cleared at the start of every `OnEndScene`, so a frame that throws early
  never resubmits stale draw data.
- New `EngineFrameController.RenderDeferredImGui()` submits it once. `UiFrameStats` gets build +
  pop-out + submit time, as before.
- `EndSceneHook` calls `RenderDeferredImGui()` after `OverlayTextureRenderer.Render`. It runs even
  when the Avalonia blit doesn't (character select), so ImGui draws exactly as before there.
- Popped-out panels still render inside `OnEndScene`. They use their own render targets and restore
  AC's target and viewport.

## Mouse priority (checked, unchanged)

6b94752's `Win32Backend.ImGuiOwnsMouseOverAvalonia` survived the merge. A mouse message skips the
Avalonia forward while ImGui wants the mouse (`io.WantCaptureMouse` or a monster nameplate), unless
an Avalonia drag/resize holds pointer capture. So clicks on a visible ImGui window go to ImGui, not
to the panel underneath. Only a misplaced doc comment was fixed.

## Notes

- ImGui windows and in-client ImGui panels now cover any Avalonia panel or popup they overlap.
  Avalonia gets the click only where no ImGui window is under the cursor.
- Engine build only; not in an installer yet.
