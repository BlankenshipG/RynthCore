# 2026.10.16.12 - Pop-out HUDs keep their floating screen position after redock

## Why

Float a panel (Mini Remote, RynthAi, …), move it, dock it, then float again: it
came back over the docked client spot instead of the last floating screen
position. `panel_state.txt` still held `FloatingLeft`/`FloatingTop` after redock
(`Floating=false`), but `StartPopOut` only restored those coords when
`fromSaved || Floating` — a manual Float after Dock skipped them, then
`PersistPopped` overwrote the saved floating spot with the docked-derived one.

## Fix

`ImGuiPanelHost.StartPopOut`: restore the last floating screen coords whenever
they exist (or the panel is still marked floating / reopen-from-saved), not only
when `Floating` is still true.

## Files

- `ImGui/ImGuiPanelHost.cs`
