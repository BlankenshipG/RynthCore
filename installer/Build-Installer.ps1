#Requires -Version 5.1
<#
.SYNOPSIS
    Builds RynthCore + RynthSuite from source and produces the full installer
    (RynthCore-Setup-<version>.exe) via Inno Setup.

.DESCRIPTION
    1. Publishes the Avalonia launcher (self-contained, x86)
    2. Publishes RynthCore.Engine (NativeAOT, x86 -- ~2 min)
    3. Publishes RynthCore.Loader (NativeAOT, x86) -- the DLL the launcher injects
    4. Publishes RynthCore.Plugin.RynthAi (NativeAOT, x86) from RynthSuite
    5. Publishes the Loot Editor (self-contained, x86)
    6. Publishes the Monster Editor (self-contained, x86) from RynthSuite
    7. Publishes the experimental plugins (RynthChat, RynthJuice, RynthNav, RynthTracker,
       RynthVision; NativeAOT, x86) -- optional components in the installer
    8. Stages RynthCore under installer\staging\core\ and RynthSuite under
       installer\staging\suite\, plus the hand-built RynthCore.SehTrampoline.dll,
       and checks every required runtime file is there
    9. Archives the previous installer (installer\previous-release\) when -Version changes
   10. Invokes ISCC.exe to produce installer\Output\RynthCore-Setup-<version>.exe
       (and a RynthCore-Setup.exe copy)

    The installer lets the user choose both the RynthCore and the RynthSuite folders; the
    code resolves them at runtime from HKA\Software\Rynth (see src\RynthCore.App\RynthInstallPaths.cs).

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER IsccPath
    Path to ISCC.exe (Inno Setup compiler). Default looks in the standard Inno Setup 6 locations.
    Install Inno Setup from: https://jrsoftware.org/isdl.php

.PARAMETER RynthSuiteRoot
    RynthSuite repo root. Default: the sibling ..\RynthSuite next to this RynthCore repo.

.PARAMETER Version
    Release version yyyy.m.d.n (e.g. 2026.10.4.2). Stamped into the launcher/engine/tools,
    release.txt and the installer. Omit for a dev build.

.PARAMETER SkipBuild
    Skip dotnet publish steps and just re-stage + re-run ISCC against existing publish output.
#>
param(
    [string]$Configuration = "Release",
    [string]$IsccPath = "",
    # Explicit path to the RynthSuite repo root. When omitted, defaults to the
    # sibling directory of RynthCore (i.e. ..\RynthSuite relative to this repo).
    [string]$RynthSuiteRoot = "",
    # Version string injected into the installer (yyyy.m.d.n). When omitted,
    # the version defined in RynthCore.iss is used as-is.
    [string]$Version = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Off

# Release number for release.txt, which the launcher's updater reads
# (RynthUpdater.InstalledCoreRelease). -Version yyyy.m.d.n → yyyymmddnn; a dev build
# without -Version records 0 ("unknown", so any published release reads as newer).
# Checked here, before the builds, so a typo fails in a second rather than at the end.
$release = 0
if ($Version) {
    $p = $Version.Split('.')
    if ($p.Count -ne 4 -or ($p | Where-Object { $_ -notmatch '^\d+$' }) -or [int]$p[1] -gt 12 -or [int]$p[2] -gt 31 -or [int]$p[3] -gt 99) {
        throw "-Version must be yyyy.m.d.n (e.g. 2026.9.26.3), got '$Version'"
    }
    $release = [int]$p[0] * 1000000 + [int]$p[1] * 10000 + [int]$p[2] * 100 + [int]$p[3]
}

# Stamp the core builds and tools with the release, so the launcher, the engine log and the
# in-game Status panel show it (RynthCore.App.BuildVersion); the SDK appends the git commit.
# The RynthAi plugin keeps its own semantic version (RynthCore.Plugin.RynthAi.csproj), so it
# is published WITHOUT these args.
$VersionArgs = @()
if ($Version) { $VersionArgs = @("-p:Version=$Version") }

$ScriptDir    = $PSScriptRoot
$RepoRoot     = Split-Path $ScriptDir -Parent          # e.g. C:\Projects\RynthCore
$ProjectsRoot = Split-Path $RepoRoot -Parent           # e.g. C:\Projects

# RynthSuite root: explicit param takes priority, otherwise sibling convention.
if (-not $RynthSuiteRoot) {
    $RynthSuiteRoot = "$ProjectsRoot\RynthSuite"
}

$LauncherProject      = "$RepoRoot\src\RynthCore.App.Avalonia\RynthCore.App.Avalonia.csproj"
$EngineProject        = "$RepoRoot\src\RynthCore.Engine\RynthCore.Engine.csproj"
$LoaderProject        = "$RepoRoot\src\RynthCore.Loader\RynthCore.Loader.csproj"
$PluginProject        = "$RynthSuiteRoot\Plugins\RynthCore.Plugin.RynthAi\RynthCore.Plugin.RynthAi.csproj"
$LootEditorProject    = "$RynthSuiteRoot\Tools\RynthCore.LootEditor\RynthCore.LootEditor.csproj"
$MonsterEditorProject = "$RynthSuiteRoot\Tools\RynthCore.MonsterEditor\RynthCore.MonsterEditor.csproj"

$LauncherPublish      = "$RepoRoot\src\RynthCore.App.Avalonia\bin\$Configuration\net10.0-windows7.0\win-x86\publish"
$EnginePublish        = "$RepoRoot\src\RynthCore.Engine\bin\$Configuration\net10.0-windows\win-x86\publish"
$LoaderPublish        = "$RepoRoot\src\RynthCore.Loader\bin\$Configuration\net10.0-windows\win-x86\publish"
$PluginPublish        = "$RynthSuiteRoot\Plugins\RynthCore.Plugin.RynthAi\bin\$Configuration\net10.0-windows\win-x86\publish"
$LootEditorPublish    = "$RynthSuiteRoot\Tools\RynthCore.LootEditor\bin\$Configuration\net10.0\win-x86\publish"
$MonsterEditorPublish = "$RynthSuiteRoot\Tools\RynthCore.MonsterEditor\bin\$Configuration\net10.0\win-x86\publish"

# Experimental RynthSuite plugins (optional installer components). Each installs to
# <RynthSuite>\<Name>\RynthCore.Plugin.<Name>.dll and keeps its own semantic version.
$ExperimentalPlugins = @('RynthChat', 'RynthJuice', 'RynthNav', 'RynthTracker', 'RynthVision')
function Get-ExperimentalProject([string]$Name) { "$RynthSuiteRoot\Plugins\RynthCore.Plugin.$Name\RynthCore.Plugin.$Name.csproj" }
function Get-ExperimentalPublish([string]$Name) { "$RynthSuiteRoot\Plugins\RynthCore.Plugin.$Name\bin\$Configuration\net10.0-windows\win-x86\publish" }
# RynthNav's starter portal list (shipped into <RynthCore>\NavData, never overwritten).
$NavPortalsTsv = "$RynthSuiteRoot\Tools\RynthNav.PortalGraph\Data\portals.tsv"

# staging\core  → {app}                          (user-chosen RynthCore folder)
# staging\suite → {code:GetSuiteDir}             (user-chosen RynthSuite folder)
$StagingRoot = "$ScriptDir\staging"
$CoreStaging  = "$StagingRoot\core"
$SuiteStaging = "$StagingRoot\suite"

# ── Validate projects ───────────────────────────────────────────────────────
$allProjects = @($LauncherProject, $EngineProject, $LoaderProject, $PluginProject, $LootEditorProject, $MonsterEditorProject) +
               @($ExperimentalPlugins | ForEach-Object { Get-ExperimentalProject $_ })
foreach ($p in $allProjects) {
    if (-not (Test-Path $p)) {
        throw "Project not found: $p`nUpdate paths in Build-Installer.ps1 if your repo layout differs."
    }
}

if (-not $SkipBuild) {
    # ── 1. Launcher (self-contained Avalonia WinExe) ─────────────────────────
    Write-Host ""
    Write-Host "[1/7] Publishing Launcher (self-contained, x86)..." -ForegroundColor Cyan
    dotnet publish $LauncherProject -c $Configuration -r win-x86 --self-contained true @VersionArgs
    if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed (exit $LASTEXITCODE)" }

    # ── 2. Engine (NativeAOT — the slow one) ──────────────────────────────────
    Write-Host ""
    Write-Host "[2/7] Publishing Engine (NativeAOT, ~2 min)..." -ForegroundColor Cyan
    dotnet publish $EngineProject -c $Configuration @VersionArgs
    if ($LASTEXITCODE -ne 0) { throw "Engine publish failed (exit $LASTEXITCODE)" }

    # ── 3. Loader (NativeAOT, small) ──────────────────────────────────────────
    Write-Host ""
    Write-Host "[3/7] Publishing Loader (NativeAOT)..." -ForegroundColor Cyan
    dotnet publish $LoaderProject -c $Configuration @VersionArgs
    if ($LASTEXITCODE -ne 0) { throw "Loader publish failed (exit $LASTEXITCODE)" }

    # ── 4. RynthAi plugin (NativeAOT; keeps its own version) ─────────────────
    Write-Host ""
    Write-Host "[4/7] Publishing RynthAi plugin (NativeAOT)..." -ForegroundColor Cyan
    dotnet publish $PluginProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed (exit $LASTEXITCODE)" }

    # ── 5. Loot Editor (self-contained Avalonia tool) ─────────────────────────
    Write-Host ""
    Write-Host "[5/7] Publishing Loot Editor (self-contained, x86)..." -ForegroundColor Cyan
    dotnet publish $LootEditorProject -c $Configuration -r win-x86 --self-contained true @VersionArgs
    if ($LASTEXITCODE -ne 0) { throw "Loot Editor publish failed (exit $LASTEXITCODE)" }

    # ── 6. Monster Editor (self-contained Avalonia tool) ──────────────────────
    Write-Host ""
    Write-Host "[6/7] Publishing Monster Editor (self-contained, x86)..." -ForegroundColor Cyan
    dotnet publish $MonsterEditorProject -c $Configuration -r win-x86 --self-contained true @VersionArgs
    if ($LASTEXITCODE -ne 0) { throw "Monster Editor publish failed (exit $LASTEXITCODE)" }

    # ── 7. Experimental plugins (NativeAOT; optional components) ──────────────
    # RynthJuice/RynthNav/RynthVision set <PublishDir> to the live C:\Games\RynthSuite\<Name>\
    # folder for dev hot-reload; override it so an installer build never touches a live install.
    # A trailing '/' (not '\') keeps the argument intact when the path contains spaces.
    Write-Host ""
    Write-Host "[7/7] Publishing experimental plugins (NativeAOT)..." -ForegroundColor Cyan
    foreach ($name in $ExperimentalPlugins) {
        Write-Host "  $name"
        $publishDir = (Get-ExperimentalPublish $name) + '/'
        dotnet publish (Get-ExperimentalProject $name) -c $Configuration "-p:PublishDir=$publishDir"
        if ($LASTEXITCODE -ne 0) { throw "$name publish failed (exit $LASTEXITCODE)" }
    }
}

# ── Stage ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Staging files..." -ForegroundColor Cyan

if (Test-Path $StagingRoot) {
    Remove-Item $StagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path "$CoreStaging\Runtime\Native"   -Force | Out-Null
New-Item -ItemType Directory -Path "$CoreStaging\Tools\LootEditor" -Force | Out-Null
New-Item -ItemType Directory -Path "$SuiteStaging\RynthAi\MonsterEditor" -Force | Out-Null

# Copies a publish folder (files + subfolders) without .pdb files.
function Copy-PublishTree([string]$Source, [string]$Dest) {
    if (-not (Test-Path $Source)) { throw "Publish output not found: $Source" }
    foreach ($file in (Get-ChildItem $Source -File)) {
        if ($file.Extension -eq '.pdb') { continue }
        Copy-Item $file.FullName (Join-Path $Dest $file.Name) -Force
    }
    foreach ($dir in (Get-ChildItem $Source -Directory)) {
        Copy-Item $dir.FullName $Dest -Recurse -Force
        Get-ChildItem (Join-Path $Dest $dir.Name) -Recurse -File -Filter '*.pdb' | Remove-Item -Force
    }
}

# Launcher root files (rename .exe → RynthCore.exe; drop .pdb)
foreach ($file in (Get-ChildItem "$LauncherPublish" -File)) {
    if ($file.Extension -eq '.pdb') { continue }
    $destName = if ($file.Name -eq 'RynthCore.App.Avalonia.exe') { 'RynthCore.exe' } else { $file.Name }
    Copy-Item $file.FullName "$CoreStaging\$destName" -Force
}

# Engine runtime files (into Runtime\, skip .pdb)
foreach ($file in (Get-ChildItem "$EnginePublish" -File)) {
    if ($file.Extension -eq '.pdb') { continue }
    Copy-Item $file.FullName "$CoreStaging\Runtime\$($file.Name)" -Force
}

# Loader — the DLL the launcher injects (EngineInjectionService.EngineDllName); it
# maps RynthCore.Engine.dll and provides hot-reload. Without it a fresh install's
# launcher can't auto-find an engine to inject.
$loaderDll = "$LoaderPublish\RynthCore.Loader.dll"
if (-not (Test-Path $loaderDll)) { throw "Loader DLL not found at: $loaderDll" }
Copy-Item $loaderDll "$CoreStaging\Runtime\" -Force

# SEH trampoline — hand-built native DLL (native\SehTrampoline\Build-SehTrampoline.ps1),
# NOT part of the engine publish output. Without it SehTrampoline.IsAvailable stays
# false and CombatActionHooks.CastSpell fails closed on every targeted cast, which
# includes self-buffs (the plugin passes the player id): the bot says "Casting: X"
# and nothing happens. Same missing/stale checks as scripts\Deploy-RynthCore.ps1.
$sehDll = "$RepoRoot\native\SehTrampoline\bin\RynthCore.SehTrampoline.dll"
$sehSrc = "$RepoRoot\native\SehTrampoline\SehTrampoline.c"
if (-not (Test-Path $sehDll)) { throw "SEH trampoline not found at: $sehDll`nBuild it with native\SehTrampoline\Build-SehTrampoline.ps1" }
if ((Test-Path $sehSrc) -and (Get-Item $sehSrc).LastWriteTime -gt (Get-Item $sehDll).LastWriteTime) {
    throw "RynthCore.SehTrampoline.dll is STALE (SehTrampoline.c is newer). Rebuild with native\SehTrampoline\Build-SehTrampoline.ps1"
}
Copy-Item $sehDll "$CoreStaging\Runtime\" -Force

# Engine Native subfolder
if (Test-Path "$EnginePublish\Native") {
    foreach ($file in (Get-ChildItem "$EnginePublish\Native" -File)) {
        if ($file.Extension -eq '.pdb') { continue }
        Copy-Item $file.FullName "$CoreStaging\Runtime\Native\$($file.Name)" -Force
    }
}

# Loot Editor → <RynthCore>\Tools\LootEditor (the launcher/engine look for it there)
Copy-PublishTree $LootEditorPublish "$CoreStaging\Tools\LootEditor"

# RynthAi plugin DLL → <RynthSuite>\RynthAi (next to its data dirs; the installer hands the
# path to the launcher via HKA\Software\Rynth\PendingPluginRegistration).
$pluginDll = "$PluginPublish\RynthCore.Plugin.RynthAi.dll"
if (-not (Test-Path $pluginDll)) { throw "Plugin DLL not found at: $pluginDll" }
Copy-Item $pluginDll "$SuiteStaging\RynthAi\" -Force

# Monster Editor → <RynthSuite>\RynthAi\MonsterEditor (RynthAi's dashboard launches it from there)
Copy-PublishTree $MonsterEditorPublish "$SuiteStaging\RynthAi\MonsterEditor"

# Experimental plugins → <RynthSuite>\<Name>\RynthCore.Plugin.<Name>.dll (the NativeAOT DLL is self-contained;
# cimgui.dll is provided by the engine's Runtime folder).
foreach ($name in $ExperimentalPlugins) {
    $dll = Join-Path (Get-ExperimentalPublish $name) "RynthCore.Plugin.$name.dll"
    if (-not (Test-Path $dll)) { throw "$name DLL not found at: $dll" }
    New-Item -ItemType Directory -Path "$SuiteStaging\$name" -Force | Out-Null
    Copy-Item $dll "$SuiteStaging\$name\" -Force
}

# RynthNav starter data → <RynthCore>\NavData (staged apart from core so it is only installed with RynthNav).
if (-not (Test-Path $NavPortalsTsv)) { throw "RynthNav portals.tsv not found at: $NavPortalsTsv" }
New-Item -ItemType Directory -Path "$StagingRoot\navdata" -Force | Out-Null
Copy-Item $NavPortalsTsv "$StagingRoot\navdata\" -Force

# release.txt — which core release this install is ($release, from -Version at the top).
Set-Content -Path "$CoreStaging\release.txt" -Value @($release, $Version) -Encoding ASCII

# Everything a working install needs. The 2026-09-25 public installer went out
# without the Loader and the SEH trampoline because nothing checked.
$required = @(
    "$CoreStaging\RynthCore.exe",
    "$CoreStaging\RynthCore.App.Avalonia.dll",
    "$CoreStaging\RynthCore.App.Avalonia.deps.json",
    "$CoreStaging\RynthCore.App.Avalonia.runtimeconfig.json",
    "$CoreStaging\Runtime\RynthCore.Loader.dll",
    "$CoreStaging\Runtime\RynthCore.Engine.dll",
    "$CoreStaging\Runtime\RynthCore.SehTrampoline.dll",
    "$CoreStaging\Runtime\minhook.x86.dll",
    "$CoreStaging\Runtime\cimgui.dll",
    "$CoreStaging\Tools\LootEditor\RynthCore.LootEditor.exe",
    "$SuiteStaging\RynthAi\RynthCore.Plugin.RynthAi.dll",
    "$SuiteStaging\RynthAi\MonsterEditor\RynthCore.MonsterEditor.exe",
    "$StagingRoot\navdata\portals.tsv"
) + @($ExperimentalPlugins | ForEach-Object { "$SuiteStaging\$_\RynthCore.Plugin.$_.dll" })
$missing = @($required | Where-Object { -not (Test-Path $_) })
if ($missing.Count -gt 0) { throw "Staging is missing required files:`n  $($missing -join "`n  ")" }

# Every assembly the launcher's deps.json lists must be staged next to it — a launcher DLL whose
# deps.json doesn't match is exactly the "Could not load RynthCore.DesktopLog" failure of 2026-10-04.
$depsJson = Get-Content "$CoreStaging\RynthCore.App.Avalonia.deps.json" -Raw | ConvertFrom-Json
$projectLibs = $depsJson.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'project' } | ForEach-Object { ($_.Name -split '/')[0] }
$missingLibs = @($projectLibs | Where-Object { -not (Test-Path "$CoreStaging\$_.dll") })
if ($missingLibs.Count -gt 0) { throw "Launcher deps.json references project assemblies that were not staged:`n  $($missingLibs -join "`n  ")" }

# Report staged sizes
$engineDll = "$CoreStaging\Runtime\RynthCore.Engine.dll"
if (Test-Path $engineDll) {
    $sizeMb = [math]::Round((Get-Item $engineDll).Length / 1MB, 1)
    Write-Host "  Engine.dll: ${sizeMb} MB"
}
$coreMb  = [math]::Round((Get-ChildItem $CoreStaging  -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
$suiteMb = [math]::Round((Get-ChildItem $SuiteStaging -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "  Staged: RynthCore ${coreMb} MB, RynthSuite ${suiteMb} MB"

Write-Host "Staging complete: $StagingRoot" -ForegroundColor Green

# ── Archive the previous installer when -Version bumps ─────────────────────
# installer\previous-release\last-built-version.txt records the last version built; when a new
# -Version is built, that version's installer is copied to previous-release\ first.
$previousReleaseDir = Join-Path $ScriptDir "previous-release"
$lastVersionFile    = Join-Path $previousReleaseDir "last-built-version.txt"
if ($Version) {
    New-Item -ItemType Directory -Path $previousReleaseDir -Force | Out-Null
    $lastVer = if (Test-Path $lastVersionFile) { (Get-Content $lastVersionFile -Raw).Trim() } else { "" }
    if ($lastVer -and $lastVer -ne $Version) {
        $prevExe = Join-Path $ScriptDir "Output\RynthCore-Setup-$lastVer.exe"
        if (-not (Test-Path $prevExe)) { $prevExe = Join-Path $ScriptDir "Output\RynthCore-Setup.exe" }
        if (Test-Path $prevExe) {
            $archive = Join-Path $previousReleaseDir "RynthCore-Setup-$lastVer.exe"
            Write-Host "Archiving previous installer ($lastVer) -> $archive" -ForegroundColor Cyan
            Copy-Item $prevExe $archive -Force
        }
    }
}

# ── Inno Setup ───────────────────────────────────────────────────────────────
# Auto-detect ISCC if not specified: try standard install location then user-local.
if (-not $IsccPath) {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $IsccPath = $c; break } }
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    Write-Host ""
    Write-Warning "Inno Setup compiler not found. Install Inno Setup 6 from: https://jrsoftware.org/isdl.php"
    Write-Warning "Or pass -IsccPath to this script explicitly."
    exit 0
}

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path "$ScriptDir\Output" -Force | Out-Null

# Substitute version into a temp copy of the .iss file so we never pass
# /D flags to ISPP (the preprocessor chokes on them in some Inno Setup 6
# builds when the .iss file has specific content patterns).
$issSource = "$ScriptDir\RynthCore.iss"
# Write temp file alongside the original so all relative paths (SetupIconFile,
# Source, staging\) still resolve from the installer\ directory.
$issTmp    = "$ScriptDir\RynthCore.tmp.iss"
$issContent = Get-Content $issSource -Raw
$outputName = "RynthCore-Setup"
if ($Version) {
    # Versioned .exe avoids Inno error 32 when Output\RynthCore-Setup.exe is open in Explorer or AV.
    $outputName = "RynthCore-Setup-$Version"
    $issContent = $issContent -replace 'AppVersion=0\.0\.0', "AppVersion=$Version"
    $issContent = $issContent -replace 'AppVerName=RynthCore \+ RynthSuite 0\.0\.0', "AppVerName=RynthCore + RynthSuite $Version"
    $issContent = $issContent -replace 'OutputBaseFilename=RynthCore-Setup', "OutputBaseFilename=$outputName"
    Write-Host "  Version override: $Version"
}
Set-Content -Path $issTmp -Value $issContent -Encoding UTF8

try {
    & $IsccPath $issTmp
    if ($LASTEXITCODE -ne 0) { throw "ISCC build failed (exit $LASTEXITCODE)" }
} finally {
    Remove-Item $issTmp -Force -ErrorAction SilentlyContinue
}

$builtInstaller = "$ScriptDir\Output\$outputName.exe"
if ($Version) {
    # Canonical name for the launcher updater / docs; best effort if the file is locked.
    try { Copy-Item $builtInstaller "$ScriptDir\Output\RynthCore-Setup.exe" -Force }
    catch { Write-Warning "Could not refresh Output\RynthCore-Setup.exe (in use?). Primary artifact: $builtInstaller" }
    [IO.File]::WriteAllText($lastVersionFile, $Version)
}

Write-Host ""
Write-Host "SUCCESS" -ForegroundColor Green
Write-Host "Installer: $builtInstaller"
