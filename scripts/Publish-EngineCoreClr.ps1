# Publish-EngineCoreClr.ps1
# Builds the CoreCLR-hosted engine (docs/UNLOADABLE_ENGINE_PLAN.md) and lays it out in a
# Runtime folder:
#   RynthCore.Loader.dll        native loader (native\Loader), hosts CoreCLR
#   RynthCore.Shim.dll          default-ALC shim (src\RynthCore.Shim)
#   RynthCore.Engine.dll (+pdb) the engine as a managed assembly (-p:EngineHost=coreclr)
#   Avalonia*, ImGui.NET, ...   the engine's managed dependencies (load into its collectible ALC)
#   cimgui, minhook, Skia, ...  native libraries (process-wide, loaded once)
#   dotnet\                     app-local CoreCLR x86 (from the NuGet runtime pack)
#
# Default target is the live install; a running client hot-reloads onto a new engine by itself:
#   .\scripts\Publish-EngineCoreClr.ps1                       -> C:\Games\RynthCore\Runtime
#   .\scripts\Publish-EngineCoreClr.ps1 -Target <dir> -EngineOnly   (engine + deps only: a hot reload)

param(
    [string]$Target = "C:\Games\RynthCore\Runtime",
    [string]$Configuration = "Release",
    [switch]$EngineOnly,
    # The engine is precompiled (ReadyToRun) unless -NoReadyToRun: each generation then
    # loads in ~130 ms instead of ~750 (less JIT); the DLL is ~5 MB instead of ~2.
    [switch]$NoReadyToRun,
    # Extra MSBuild arguments for the engine and shim builds (the installer passes -p:Version=...).
    [string[]]$MsBuildArgs = @(),
    # Deploy to the live Runtime even though status-export has commits this checkout lacks.
    [switch]$AllowBehind
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

# Deploying to the live install from a checkout that lacks status-export's commits rolls
# back other sessions' engine fixes on every running client (2026-09-29, API v70). Refuse.
$liveRuntime = "C:\Games\RynthCore\Runtime"
if ([IO.Path]::GetFullPath($Target).TrimEnd('\') -ieq $liveRuntime -and -not $AllowBehind) {
    git -C $repo merge-base --is-ancestor status-export HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        $missing = git -C $repo log --oneline HEAD..status-export
        throw "This checkout is missing status-export commits; deploying it to the live Runtime would roll them back:`n$($missing -join "`n")`nMerge status-export first (or pass -AllowBehind if you really mean it)."
    }
}
$env:PATH += ";C:\Program Files (x86)\Microsoft Visual Studio\Installer"
New-Item -ItemType Directory -Force -Path $Target | Out-Null

$stage = Join-Path $repo "out\engine-clr"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Write-Host "Building the engine (EngineHost=coreclr)..."
$r2r = if (-not $NoReadyToRun) { "-p:PublishReadyToRun=true" } else { "-p:PublishReadyToRun=false" }
dotnet publish "$repo\src\RynthCore.Engine\RynthCore.Engine.csproj" -c $Configuration -p:EngineHost=coreclr $r2r -o $stage -nologo -v q @MsBuildArgs
if ($LASTEXITCODE -ne 0) { throw "engine build failed" }

if (-not $EngineOnly) {
    Write-Host "Building the native loader..."
    & "$repo\native\Loader\Build-Loader.ps1"
    Write-Host "Building the shim..."
    dotnet build "$repo\src\RynthCore.Shim\RynthCore.Shim.csproj" -c $Configuration -nologo -v q @MsBuildArgs
    if ($LASTEXITCODE -ne 0) { throw "shim build failed" }

    # The loader and the shim load once per client: a running client keeps its copy (and
    # locks the file) until it restarts. Skip them with a warning rather than fail.
    $hostFiles = @(
        "$repo\native\Loader\bin\RynthCore.Loader.dll", "$repo\native\Loader\bin\RynthCore.Loader.pdb",
        "$repo\src\RynthCore.Shim\bin\$Configuration\net10.0-windows\RynthCore.Shim.dll",
        "$repo\src\RynthCore.Shim\bin\$Configuration\net10.0-windows\RynthCore.Shim.pdb")
    # A running client holds these files; Windows lets a loaded DLL be renamed, so the old
    # copy is moved aside (the client keeps using it) and the new one goes in for the next
    # launch. Earlier moved-aside copies are deleted once no client holds them.
    Get-ChildItem $Target -Filter "RynthCore.*.old-*" -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    foreach ($f in $hostFiles) {
        $dest = Join-Path $Target (Split-Path $f -Leaf)
        try { Copy-Item $f $dest -Force -ErrorAction Stop }
        catch {
            try {
                Rename-Item $dest ("{0}.old-{1:yyyyMMddHHmmss}" -f (Split-Path $dest -Leaf), (Get-Date)) -ErrorAction Stop
                Copy-Item $f $dest -Force -ErrorAction Stop
                Write-Host "$(Split-Path $f -Leaf) was in use: moved the old one aside; new launches get the new one."
            }
            catch { Write-Warning "$(Split-Path $f -Leaf) is in use by a running client; it keeps the old one until it restarts." }
        }
    }

    $seh = "$repo\native\SehTrampoline\bin\RynthCore.SehTrampoline.dll"
    if (-not (Test-Path $seh)) { $seh = "C:\Games\RynthCore\Runtime\RynthCore.SehTrampoline.dll" }
    $sehDest = Join-Path (Resolve-Path $Target).Path "RynthCore.SehTrampoline.dll"
    $sameSeh = (Test-Path $sehDest) -and ((Resolve-Path $seh).Path -eq $sehDest -or
        (Get-FileHash $seh).Hash -eq (Get-FileHash $sehDest).Hash)
    if (-not $sameSeh) {
        try { Copy-Item $seh $sehDest -Force -ErrorAction Stop }
        catch { Write-Warning "RynthCore.SehTrampoline.dll changed but is in use by a running client; it gets the new one when it restarts." }
    }

    # App-local runtime: the win-x86 runtime pack the SDK restores (same version the engine was built against).
    $dotnet = Join-Path $Target "dotnet"
    if (-not (Test-Path "$dotnet\coreclr.dll")) {
        $pack = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.netcore.app.runtime.win-x86"
        $ver = (Get-ChildItem $pack | Where-Object Name -like "10.*" | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1).Name
        if (-not $ver) { throw "microsoft.netcore.app.runtime.win-x86 10.x not in the NuGet cache (restore any win-x86 self-contained project once)" }
        $rt = "$pack\$ver\runtimes\win-x86"
        New-Item -ItemType Directory -Force -Path $dotnet | Out-Null
        Copy-Item "$rt\native\*" $dotnet -Force
        Copy-Item "$rt\lib\net10.0\*.dll" $dotnet -Force
        # Diagnostics-only natives the host never loads.
        Remove-Item "$dotnet\createdump.exe", "$dotnet\mscordaccore_*.dll", "$dotnet\msquic.dll" -Force -ErrorAction SilentlyContinue
        Write-Host "App-local runtime $ver -> $dotnet"
    }
}

# Engine + dependencies. The canonical RynthCore.Engine.dll goes last: the loader's file
# watcher reloads on its timestamp, so everything it needs must already be in place.
# Only what changed. Native libraries (cimgui, Skia...) are loaded once per client and
# locked while it runs; they rarely change, so a locked unchanged one is simply skipped.
foreach ($f in Get-ChildItem $stage -File | Where-Object { $_.Name -notlike "RynthCore.Engine.*" -and $_.Name -ne "minhook.x86.dll" }) {
    $dest = Join-Path $Target $f.Name
    if ((Test-Path $dest) -and (Get-Item $dest).Length -eq $f.Length -and
        (Get-FileHash $dest).Hash -eq (Get-FileHash $f.FullName).Hash) { continue }
    try { Copy-Item $f.FullName $dest -Force -ErrorAction Stop }
    catch { Write-Warning "$($f.Name) changed but is in use by a running client; it gets the new one when it restarts." }
}
if (-not (Test-Path "$Target\minhook.x86.dll")) { Copy-Item "$stage\minhook.x86.dll" $Target -Force }
Copy-Item "$stage\RynthCore.Engine.pdb" $Target -Force
Copy-Item "$stage\RynthCore.Engine.dll" $Target -Force
Write-Host "Published the CoreCLR engine to $Target"
