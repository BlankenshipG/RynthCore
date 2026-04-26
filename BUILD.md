# Build & Deploy

RynthCore is the injection framework. RynthSuite (separate repo) contains the plugins that run on top of it.

## Prerequisites

- Windows 10/11
- .NET 9 SDK (x86)
- Visual Studio 2022 Build Tools with the .NET desktop and C++ desktop workloads (required by the NativeAOT ILC linker)
- Asheron's Call client installed

## Projects

| Project | Type | Output |
|---------|------|--------|
| `src/RynthCore.Engine` | NativeAOT x86 DLL | Injected into acclient.exe — hooks D3D9 EndScene, hosts ImGui overlay |
| `src/RynthCore.Injector` | Console app | Injects the engine DLL via LoadLibrary + CreateRemoteThread |
| `src/RynthCore.App.Avalonia` | Avalonia desktop app | Desktop launcher — server/account profiles, plugin management, launches + injects |
| `src/RynthCore.PluginSdk` | Class library | Public host API surface for plugins |
| `src/RynthCore.PluginCore` | Class library | Runtime helpers for plugin bootstrap/lifecycle |
| `src/RynthCore.App` | Shared source | Model and service files linked by both the Avalonia app and the Engine (not a standalone project) |

## Publish Commands

### Launcher (Avalonia)

```bash
cd C:\Projects\RynthCore\src\RynthCore.App.Avalonia
dotnet build -c Release
```

Output: `bin\Release\net9.0-windows7.0\RynthCore.App.Avalonia.dll`

### Engine (NativeAOT — slow first build, ~2 min)

Requires `vswhere.exe` on PATH. If the build fails with `'vswhere.exe' is not recognized`, add the VS Installer directory:

```bash
set PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer
```

```bash
cd C:\Projects\RynthCore\src\RynthCore.Engine
dotnet publish -c Release
```

Output: `bin\Release\net9.0-windows\win-x86\publish\RynthCore.Engine.dll` (~26 MB)

### RynthAi Plugin (NativeAOT — separate repo)

Always clean first — incremental builds can silently skip NativeAOT and produce a stale DLL.

```bash
cd C:\Projects\RynthSuite\Plugins\RynthCore.Plugin.RynthAi
rmdir /s /q obj\Release bin\Release 2>nul
dotnet publish -c Release
```

Output: `bin\Release\net9.0-windows\win-x86\publish\RynthCore.Plugin.RynthAi.dll` (~7 MB)

## Deploy

### Directory Layout

```
C:\Games\RynthCore\
├── RynthCore.exe                 ← Launcher (renamed from RynthCore.App.Avalonia.exe)
├── RynthCore.App.Avalonia.dll    ← Launcher assembly
├── RynthCore.App.Avalonia.deps.json
├── RynthCore.App.Avalonia.runtimeconfig.json
├── Avalonia.*.dll                ← Avalonia framework DLLs
└── Runtime\
    ├── RynthCore.Engine.dll      ← Engine (NativeAOT x86)
    ├── minhook.x86.dll           ← MinHook (x86), preloaded by EntryPoint
    ├── cimgui.dll                ← ImGui C bindings (x86, docking branch)
    └── Plugins\
        └── *.dll                 ← Plugin DLLs (built-in directory)

C:\Games\RynthSuite\RynthAi\
├── RynthCore.Plugin.RynthAi.dll  ← RynthAi plugin (recommended deploy location)
├── NavProfiles\                  ← Navigation profiles
├── LootProfiles\                 ← Loot profiles
└── Metas\                        ← Meta files
```

### Deploy Launcher

The launcher can stay open while you replace its DLL.

```bash
copy src\RynthCore.App.Avalonia\bin\Release\net9.0-windows7.0\RynthCore.App.Avalonia.dll C:\Games\RynthCore\
copy src\RynthCore.App.Avalonia\bin\Release\net9.0-windows7.0\RynthCore.App.Avalonia.deps.json C:\Games\RynthCore\
```

Restart the launcher after deploying.

### Deploy Engine

**AC must be closed** — the running client holds a file lock on the engine DLL.

```bash
copy src\RynthCore.Engine\bin\Release\net9.0-windows\win-x86\publish\RynthCore.Engine.dll C:\Games\RynthCore\Runtime\
```

### Deploy RynthAi Plugin

**AC can stay open** — plugins are shadow-copied at load time, so the file is not locked.

The recommended deploy location is `C:\Games\RynthSuite\RynthAi\` — alongside the plugin's data files. Add this path in the launcher's **Plugins** tab. Paths are persisted to `%AppData%\RynthCore\engine.json`.

```bash
copy C:\Projects\RynthSuite\Plugins\RynthCore.Plugin.RynthAi\bin\Release\net9.0-windows\win-x86\publish\RynthCore.Plugin.RynthAi.dll C:\Games\RynthSuite\RynthAi\
```

Alternatively, deploy to the engine's built-in plugin directory:

```bash
copy ... C:\Games\RynthCore\Runtime\Plugins\
```

After deploying a plugin with AC running, click **RL** on the RynthCore overlay bar to hot-reload.

## Verify

| File | Expected Size | If Wrong |
|------|--------------|----------|
| `Runtime\RynthCore.Engine.dll` | ~26 MB | NativeAOT didn't run — check for `Generating native code` in build output |
| `RynthSuite\RynthAi\RynthCore.Plugin.RynthAi.dll` | ~7 MB | If ~1 MB, you built the stale stub at `C:\Projects\RynthCore\Plugins\` instead of `C:\Projects\RynthSuite\Plugins\` |
| `Runtime\cimgui.dll` | ~1.5 MB | Must match ImGui.NET 1.91.6.1 |

To confirm NativeAOT actually ran, check that `.lib` and `.exp` files exist alongside the DLL in the `native\` directory.

## Installer

The `installer/` directory contains an Inno Setup script and a PowerShell build script that publishes the launcher, engine, **injector**, all discovered `RynthSuite` plugins, **ub-Rythai** (`RynthCore.Plugin.UbRythai`) when `..\ub-Rythai\` is present, and the RynthSuite tools, stages the output, and produces a single **`RynthBundle-Setup.exe`**.

### Prerequisites

- [Inno Setup 6](https://jrsoftware.org/isdl.php) installed to the default location (`C:\Program Files (x86)\Inno Setup 6\`)
- All build prerequisites listed above (matching .NET SDK for the solution, VS Build Tools for NativeAOT as needed)
- **RynthCore.exe** and **Runtime\RynthCore.Injector.exe** are published **self-contained** (they bundle .NET 10 x86) — no separate Desktop Runtime install is required to run the launcher or injector from the install folder.
- **LootEditor** and **MonsterEditor** remain framework-dependent (x64). If either fails to start, install [.NET Desktop Runtime 10 x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime).

### Build the full release + installer (recommended)

From the `RynthCore` repo root:

```powershell
cd C:\Projects\RynthCore
.\Build-Release-All.ps1 -Version 0.4.2
```

This builds `RynthCore.sln` in **Release**, then runs `installer\Build-Installer.ps1` (same as below).

### Build the installer only

`RynthSuite` must be a **sibling** folder of `RynthCore` (e.g. `C:\Projects\RynthCore` and `C:\Projects\RynthSuite`), or pass `-RynthSuiteRoot`. **ub-Rythai** is picked up from the sibling **`ub-Rythai`** folder when `ub-Rythai\RynthCore.Plugin.UbRythai\RynthCore.Plugin.UbRythai.csproj` exists (override with `-UbRythaiPluginProject`, or use `-SkipUbRythai` to omit).

```powershell
cd C:\Projects\RynthCore\installer
.\Build-Installer.ps1 -Version 0.4.2
```

This runs publish steps for launcher (self-contained x86) + engine (NativeAOT) + injector (self-contained x86) + all `RynthCore.Plugin.*` projects under RynthSuite, bundles the RynthSuite tools (`LootEditor`, `MonsterEditor`) as framework-dependent win-x64 apps (x64 runtime may be required for those two), stages everything under `installer\staging\app\`, then invokes `ISCC.exe` to produce the installer.

Output: `installer\Output\RynthBundle-Setup.exe` (or `installer\Output\RynthBundle-Setup-<version>.exe` when `-Version` is set — Inno **`OutputBaseFilename`** is versioned so the compile is not blocked by a locked `RynthBundle-Setup.exe`; the script then copies to the canonical name when possible).

### Previous release folder

When you pass **`-Version`** to `Build-Installer.ps1` or `Build-Release-All.ps1`, the script compares it to `installer\previous-release\last-built-version.txt`. If that file lists a **different** version and `installer\Output\RynthBundle-Setup.exe` already exists, the **existing** setup exe is copied to:

`installer\previous-release\RynthBundle-Setup-<previous-version>.exe`

After a **successful** Inno compile, `last-built-version.txt` is updated to the new `-Version`. Archived `*.exe` files are gitignored; commit **`last-built-version.txt`** with the current shipping bundle version so the next bump archives the right label.

### Options

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Configuration` | `Release` | Build configuration |
| `-IsccPath` | `C:\Program Files (x86)\Inno Setup 6\ISCC.exe` | Path to the Inno Setup compiler |
| `-SkipBuild` | off | Skip `dotnet publish` steps and re-package using the existing staging directory |
| `-UbRythaiPluginProject` | *(default sibling path)* | Full path to `RynthCore.Plugin.UbRythai.csproj` |
| `-SkipUbRythai` | off | Do not publish or stage ub-Rythai (e.g. CI without that repo) |

### What the installer does

- On **interactive** installs, shows an **advisory** wizard page after **Welcome** that checks for **.NET 10 (x64)** in the registry (see `RynthCore.iss` `[Code]`): **(1)** `Microsoft.WindowsDesktop.App` 10+ under `HKLM\SOFTWARE\dotnet\shared\...`, **(2)** `InstalledManifests\x64` keys containing `Manifest-10.`, **(3)** `sharedhost` **Version** 10+ under `SOFTWARE\dotnet\Setup\InstalledVersions\x64\` (and the parallel `SOFTWARE\Microsoft\dotnet\Setup\...` path). The same logic is implemented in C# as **`RynthCore.DesktopLog.NetDesktopX64Prerequisite`**; the launcher reports it in the activity line and `RynthCore-Launcher.log`. **Next is never disabled** for missing x64 runtime — install can complete; Loot/Monster may not start until you install the desktop/runtime from the linked page. On 32-bit Windows the page explains the tools are N/A. **Silent** installs skip that page.
- Installs the launcher and injector (self-contained x86 — .NET 10 is bundled with `RynthCore.exe` and `RynthCore.Injector.exe`), the engine, and native dependencies under `C:\Games\RynthCore` (user-selectable; default matches this layout)
- Installs all default `RynthSuite` plugin DLLs and **ub-Rythai** (when built) into `Runtime\Plugins` so they auto-load on first inject
- Installs `RynthCore.LootEditor` and `RynthCore.MonsterEditor` into `Tools\` as framework-dependent apps (requires .NET Desktop Runtime x64).
- Creates data directories under `C:\Games\RynthSuite\RynthAi\` (NavProfiles, LootProfiles, MetaFiles, etc.) — these are preserved on uninstall
- Adds Start Menu and optional Desktop shortcuts
- Warns if `acclient.exe` is running (the engine DLL would be locked)
- Shows getting-started instructions on the finish page

## Gotchas

- **`dotnet publish`, not `dotnet build` for Engine and Plugins.** NativeAOT only runs during `dotnet publish`. A `dotnet build` produces a valid managed DLL that compiles fine but has no unmanaged exports and will be silently ignored by the engine. The Avalonia launcher is the exception — `dotnet build` is fine since it's a normal .NET app.
- **Clean before plugin publish.** Incremental NativeAOT builds can silently reuse stale output. Delete `obj\Release` and `bin\Release` before every publish to guarantee a fresh compile.
- **Engine deploy path is `Runtime\`**, not `C:\Games\RynthCore\`. The injector resolves `Runtime\RynthCore.Engine.dll` by default.
- **Two RynthAi projects exist.** The real plugin is at `C:\Projects\RynthSuite\Plugins\RynthCore.Plugin.RynthAi\`. There is a stale stub at `C:\Projects\RynthCore\Plugins\RynthCore.Plugin.RynthAi\` — do not build or deploy from there.
- **Engine publish copies a stale plugin** into the engine's publish output. Always deploy the plugin from the RynthSuite path last.
- **cimgui.dll version must match ImGui.NET NuGet.** Post-1.90 struct layouts changed. A mismatched cimgui.dll causes `DisplaySize = <1, 1>` or font crashes.
- **vswhere.exe must be on PATH** for NativeAOT link step. Add `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to PATH if missing.


