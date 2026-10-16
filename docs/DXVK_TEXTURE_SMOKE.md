# DXVK drop-in smoke checklist (textures / overlay)

Preferred path: launcher **Launch Behavior → Enable DXVK** (opt-in) plus **Fetch / update DXVK**. That copies x86 `d3d9.dll` from `<RynthCore>\Runtime\dxvk\` next to `acclient.exe` (and removes it when unchecked). Installer seeds the same cache; `scripts\Fetch-Dxvk.ps1` can refresh it.

Manual drop-in still works the same for smoke without the launcher toggle.

Live AC installs used for this checklist:
- `C:\Turbine\Asheron's Call-ryn\` (Rynth-launched clients; cwd in recent logs)
- `C:\Turbine\Asheron's Call\` (retail copy; same drop-in applied)

Engine logs: `C:\Games\RynthCore\Logs\RynthCore.<pid>.log`  
Cache / drop-in: **DXVK 3.1.1** `x32/d3d9.dll` (+ `dxvk.conf`).

## 0. Prerequisites

- [ ] **32-bit (x86) DXVK** (AC is 32-bit). Launcher Fetch / installer seed handle this.
- [ ] Decal **off** for first pass (fewer moving parts).
- [ ] Optional: set `DXVK_HUD=1` or `DXVK_LOG_LEVEL=info` in the environment to confirm Vulkan is active.

## 1. Files to place (beside `acclient.exe`)

| File | Required | Notes |
|---|---|---|
| `d3d9.dll` | Yes | From DXVK `x32` / win32 package |
| `dxvk.conf` | Optional | See sample below |
| `d3d11.dll` / `dxgi.dll` | No | AC only needs d3d9 |

Sample `dxvk.conf` (conservative for old clients):

```ini
# dxvk.conf — Asheron's Call / Rynth smoke
dxgi.deferSurfaceCreation = True
d3d9.maxFrameLatency = 1
d3d9.presentInterval = 1
```

## 2. AC alone (no Rynth)

1. Launch AC from the folder that has the drop-in `d3d9.dll`.
2. Log in to world.
3. Pass if: world renders, no black screen, FPS roughly sane, optional DXVK HUD visible.
4. Fail if: crash on device create, pure software fallback, or no Vulkan device.

## 3. AC + Rynth (no Decal)

1. With DXVK still in place, launch via Rynth as usual.
2. Open `C:\Games\RynthCore\Logs\RynthCore.<pid>.log` and grep:

| Look for | Meaning |
|---|---|
| `DxvkDetection: DXVK detected` | Harness saw DXVK version resource / `dxvk.conf` |
| `forcing software overlay textures` | Shared-HANDLE path disabled; LockRect path forced |
| `D3D9Bootstrapper: d3d9.dll loaded` | Bootstrap saw the module |
| `EndSceneHook: INSTALLED successfully` | Hook installed |
| `EndSceneHook: Warmup complete - initializing ImGui` | First backbuffer frames OK |
| `DX9Backend: CreateTexture failed` | **Fail** — font/icon upload broken |
| `OverlayTextureRenderer: CreateTexture` / `Created … overlay texture` | Software overlay upload working |
| `D3D9Ex shared-texture consumption is unavailable` | Expected under DXVK if Angle path still probed |
| `EndSceneHook: FAILED` / `vtable discovery returned null` | **Fail** — PE/NULLREF discovery needs work |

3. In-game pass criteria:
   - [ ] ImGui / Rynth panels draw
   - [ ] Script / DAT icons visible (not solid magenta or blank)
   - [ ] Avalonia HUD / Mini Remote readable (may be slightly blurrier on software path)
   - [ ] Pop-outs: optional; `GetRenderTargetData` can flake — note fail, don’t block Phase 0

## 4. Env overrides (no rebuild)

| Variable | Effect |
|---|---|
| `RYNTHCORE_DISABLE_ANGLE_BRIDGE=1` | Force software overlay even without DXVK |
| `DXVK_HUD=1` | Confirm DXVK is the active D3D9 |
| `DXVK_LOG_PATH=<dir>` | DXVK’s own log next to game |

## 5. Rollback

1. Quit AC + Rynth.
2. Delete or rename game-dir `d3d9.dll` (and `dxvk.conf`).
3. Relaunch — logs should show `DxvkDetection: stock system d3d9.dll`.

## 6. Known non-goals for this smoke

- D3D9Ex **shared HANDLE** Avalonia interop (expected broken / forced off).
- Decal coexistence under DXVK (second pass only if alone+Rynth passes).
- Shipping DXVK inside the Rynth installer.
