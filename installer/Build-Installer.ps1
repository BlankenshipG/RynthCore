#Requires -Version 5.1
<#
.SYNOPSIS
    Builds RynthCore + RynthSuite release outputs and produces RynthBundle-Setup.exe via Inno Setup.

.DESCRIPTION
    1. Publishes the Avalonia launcher (self-contained, x86 — bundles .NET; no global runtime for RynthCore.exe)
    2. Publishes RynthCore.Engine (NativeAOT, x86 -- ~2 min)
    3. Publishes RynthCore.Injector (self-contained, x86)
    4. Publishes every RynthCore.Plugin.* project under RynthSuite (NativeAOT, x86), plus
       ub-Rythai (RynthCore.Plugin.UbRythai) when ../ub-Rythai/... exists or -UbRythaiPluginProject is set
    5. Optionally publishes LootEditor and MonsterEditor (framework-dependent, x64)
    6. Stages all output under installer\staging\app\ (recursive trees for tools and launcher)
    7. Invokes ISCC.exe to produce installer\Output\RynthBundle-Setup.exe

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER IsccPath
    Path to ISCC.exe (Inno Setup compiler). Default looks in the standard Inno Setup 6 location.
    Install Inno Setup from: https://jrsoftware.org/isdl.php

.PARAMETER SkipBuild
    Skip dotnet publish steps and just re-run ISCC against the existing staging directory.
#>
param(
    [string]$Configuration = "Release",
    [string]$IsccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    # Explicit path to the RynthSuite repo root. When omitted, defaults to the
    # sibling directory of RynthCore (i.e. ..\RynthSuite relative to this repo).
    [string]$RynthSuiteRoot = "",
    # Version string injected into the installer (e.g. "0.3"). When omitted,
    # the version defined in RynthCore.iss is used as-is.
    [string]$Version = "",
    # Include RynthSuite desktop tools (LootEditor + MonsterEditor) in setup.
    [switch]$IncludeSuiteTools = $true,
    # Full path to RynthCore.Plugin.UbRythai.csproj. Empty = default sibling ..\ub-Rythai\...
    [string]$UbRythaiPluginProject = "",
    # Omit ub-Rythai from the bundle (e.g. CI without that checkout).
    [switch]$SkipUbRythai,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Off

function Copy-PublishTree {
    param(
        [string]$SourceRoot,
        [string]$DestRoot,
        [switch]$ExcludePdb
    )
    if (-not (Test-Path $SourceRoot)) {
        throw "Publish output not found: $SourceRoot"
    }
    $srcFull = (Resolve-Path $SourceRoot).Path
    # Normalize trailing separator so substring math is safe on Windows
    if (-not $srcFull.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        $srcFull += [IO.Path]::DirectorySeparatorChar
    }
    Get-ChildItem $SourceRoot -Recurse -File | ForEach-Object {
        if ($ExcludePdb -and $_.Extension -eq '.pdb') { return }
        $rel = $_.FullName.Substring($srcFull.Length)
        $target = Join-Path $DestRoot $rel
        $parent = Split-Path $target -Parent
        if ($parent -and -not (Test-Path $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

$ScriptDir    = $PSScriptRoot
$RepoRoot     = Split-Path $ScriptDir -Parent          # e.g. C:\Projects\RynthCore
$ProjectsRoot = Split-Path $RepoRoot -Parent           # e.g. C:\Projects

# RynthSuite root: explicit param takes priority, otherwise sibling convention.
if (-not $RynthSuiteRoot) {
    $RynthSuiteRoot = "$ProjectsRoot\RynthSuite"
}

$LauncherProject = "$RepoRoot\src\RynthCore.App.Avalonia\RynthCore.App.Avalonia.csproj"
$EngineProject   = "$RepoRoot\src\RynthCore.Engine\RynthCore.Engine.csproj"
$InjectorProject = "$RepoRoot\src\RynthCore.Injector\RynthCore.Injector.csproj"
$LootEditorProject    = "$RynthSuiteRoot\Tools\RynthCore.LootEditor\RynthCore.LootEditor.csproj"
$MonsterEditorProject = "$RynthSuiteRoot\Tools\RynthCore.MonsterEditor\RynthCore.MonsterEditor.csproj"

$LauncherPublish = "$RepoRoot\src\RynthCore.App.Avalonia\bin\$Configuration\net10.0-windows7.0\win-x86\publish"
$EnginePublish   = "$RepoRoot\src\RynthCore.Engine\bin\$Configuration\net10.0-windows\win-x86\publish"
$InjectorPublish = "$RepoRoot\src\RynthCore.Injector\bin\$Configuration\net10.0-windows\win-x86\publish"

$PluginProjects = @()
$pluginProjectFiles = Get-ChildItem -Path "$RynthSuiteRoot\Plugins" -Filter "RynthCore.Plugin.*.csproj" -File -Recurse -ErrorAction SilentlyContinue
foreach ($pluginProject in $pluginProjectFiles) {
    $pluginName = [IO.Path]::GetFileNameWithoutExtension($pluginProject.Name)
    $PluginProjects += [pscustomobject]@{
        Name        = $pluginName
        ProjectPath = $pluginProject.FullName
        PublishPath = Join-Path $pluginProject.DirectoryName "bin\$Configuration\net10.0-windows\win-x86\publish"
        DllPath     = Join-Path (Join-Path $pluginProject.DirectoryName "bin\$Configuration\net10.0-windows\win-x86\publish") "$pluginName.dll"
    }
}

# ub-Rythai ships from a sibling repo (same layout as RynthSuite/README.md); publish + stage into Runtime\Plugins.
if (-not $SkipUbRythai) {
    if ($UbRythaiPluginProject) {
        $ubRythaiProj = $UbRythaiPluginProject
    }
    else {
        $ubRythaiProj = "$ProjectsRoot\ub-Rythai\RynthCore.Plugin.UbRythai\RynthCore.Plugin.UbRythai.csproj"
    }

    if (Test-Path -LiteralPath $ubRythaiProj) {
        $ubDir = Split-Path $ubRythaiProj -Parent
        $ubName = "RynthCore.Plugin.UbRythai"
        $PluginProjects += [pscustomobject]@{
            Name        = $ubName
            ProjectPath = $ubRythaiProj
            PublishPath = Join-Path $ubDir "bin\$Configuration\net10.0-windows\win-x86\publish"
            DllPath     = Join-Path (Join-Path $ubDir "bin\$Configuration\net10.0-windows\win-x86\publish") "$ubName.dll"
        }
        Write-Host "ub-Rythai plugin included: $ubRythaiProj" -ForegroundColor DarkCyan
    }
    else {
        $warn = "ub-Rythai project not found (optional): $ubRythaiProj - RynthCore.Plugin.UbRythai.dll will be omitted. Clone next to RynthCore or pass -UbRythaiPluginProject."
        Write-Warning $warn
    }
}

$StagingDir  = "$ScriptDir\staging\app"

# ── Validate projects ───────────────────────────────────────────────────────
foreach ($p in @($LauncherProject, $EngineProject, $InjectorProject)) {
    if (-not (Test-Path $p)) {
        throw "Project not found: $p`nUpdate paths in Build-Installer.ps1 if your repo layout differs."
    }
}

if ($PluginProjects.Count -eq 0) {
    throw "No RynthSuite plugin projects found under: $RynthSuiteRoot\Plugins"
}

if ($IncludeSuiteTools) {
    foreach ($p in @($LootEditorProject, $MonsterEditorProject)) {
        if (-not (Test-Path $p)) {
            throw "Suite tool project not found: $p"
        }
    }
}

if (-not $SkipBuild) {
    $step = 0
    $totalSteps = 3 + $PluginProjects.Count + $(if ($IncludeSuiteTools) { 2 } else { 0 })

    $step++
    Write-Host ""
    # Self-contained: ship .NET 10 with the apphost so RynthCore.exe does not require a machine-wide
    # Desktop Runtime (avoids the official apphost "You must install .NET" dialog on clean PCs).
    Write-Host "[$step/$totalSteps] Publishing Launcher (self-contained, x86)..." -ForegroundColor Cyan
    # RollForward=Disable: host must use bundled runtimes only (avoids probing %ProgramFiles%\dotnet\shared\...).
    dotnet publish $LauncherProject -c $Configuration -r win-x86 --self-contained true `
        -p:SelfContained=true -p:PublishSelfContained=true -p:RollForward=Disable
    if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed (exit $LASTEXITCODE)" }

    $step++
    Write-Host ""
    Write-Host "[$step/$totalSteps] Publishing Engine (NativeAOT, ~2 min)..." -ForegroundColor Cyan
    dotnet publish $EngineProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Engine publish failed (exit $LASTEXITCODE)" }

    $step++
    Write-Host ""
    Write-Host "[$step/$totalSteps] Publishing Injector (self-contained, x86)..." -ForegroundColor Cyan
    dotnet publish $InjectorProject -c $Configuration -r win-x86 --self-contained true `
        -p:SelfContained=true -p:PublishSelfContained=true -p:RollForward=Disable
    if ($LASTEXITCODE -ne 0) { throw "Injector publish failed (exit $LASTEXITCODE)" }

    foreach ($plugin in $PluginProjects) {
        $step++
        Write-Host ""
        Write-Host "[$step/$totalSteps] Publishing plugin: $($plugin.Name) (NativeAOT)..." -ForegroundColor Cyan
        dotnet publish $plugin.ProjectPath -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed for $($plugin.Name) (exit $LASTEXITCODE)" }
    }

    if ($IncludeSuiteTools) {
        if (-not (Test-Path "$RepoRoot\installer\_toolpublish")) {
            New-Item -ItemType Directory -Path "$RepoRoot\installer\_toolpublish" -Force | Out-Null
        }
        $step++
        Write-Host ""
        Write-Host "[$step/$totalSteps] Publishing LootEditor (framework-dependent x64)..." -ForegroundColor Cyan
        dotnet publish $LootEditorProject -c $Configuration -r win-x64 --self-contained false --output "$RepoRoot\installer\_toolpublish\LootEditor"
        if ($LASTEXITCODE -ne 0) { throw "LootEditor publish failed (exit $LASTEXITCODE)" }

        $step++
        Write-Host ""
        Write-Host "[$step/$totalSteps] Publishing MonsterEditor (framework-dependent x64)..." -ForegroundColor Cyan
        dotnet publish $MonsterEditorProject -c $Configuration -r win-x64 --self-contained false --output "$RepoRoot\installer\_toolpublish\MonsterEditor"
        if ($LASTEXITCODE -ne 0) { throw "MonsterEditor publish failed (exit $LASTEXITCODE)" }
    }
}

# ── Stage ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Staging files..." -ForegroundColor Cyan

if (Test-Path "$ScriptDir\staging") {
    Remove-Item "$ScriptDir\staging" -Recurse -Force
}
New-Item -ItemType Directory -Path "$StagingDir\Runtime\Native"   -Force | Out-Null
New-Item -ItemType Directory -Path "$StagingDir\Runtime\Plugins"  -Force | Out-Null
if ($IncludeSuiteTools) {
    New-Item -ItemType Directory -Path "$StagingDir\Tools\LootEditor"    -Force | Out-Null
    New-Item -ItemType Directory -Path "$StagingDir\Tools\MonsterEditor" -Force | Out-Null
}

# Document runtime prerequisites: launcher + injector are self-contained; editor tools are FDD x64.
$prereqPath = Join-Path $StagingDir "RynthCore-Prerequisites.txt"
Set-Content -LiteralPath $prereqPath -Encoding UTF8 -Value @"
RynthCore Runtime Prerequisites
==============================

RynthCore.exe and Runtime\RynthCore.Injector.exe are published self-contained (they bundle .NET 10
x86). You do not need a separate .NET Desktop Runtime install to run the launcher or injector from
this folder.

The optional suite tools (Loot Editor / Monster Editor) are framework-dependent x64. If they show
a missing-runtime dialog, install:

- .NET Desktop Runtime 10 x64:
  https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime
"@

# Launcher (self-contained — copy full tree, then rename launcher host + host metadata files)
if (-not (Test-Path $LauncherPublish)) {
    throw "Launcher publish not found: $LauncherPublish`nRun without -SkipBuild first."
}
Copy-PublishTree -SourceRoot $LauncherPublish -DestRoot $StagingDir -ExcludePdb
$avExe = "$StagingDir\RynthCore.App.Avalonia.exe"
if (Test-Path $avExe) {
    Move-Item -LiteralPath $avExe -Destination "$StagingDir\RynthCore.exe" -Force
    # Keep host metadata filenames in sync with the renamed apphost.
    # If these remain as RynthCore.App.Avalonia.* the apphost can fail runtime resolution
    # and show a ".NET install required" prompt even when .NET 10 is already installed.
    $avRuntimeConfig = "$StagingDir\RynthCore.App.Avalonia.runtimeconfig.json"
    if (Test-Path $avRuntimeConfig) {
        Move-Item -LiteralPath $avRuntimeConfig -Destination "$StagingDir\RynthCore.runtimeconfig.json" -Force
    }
    $avDeps = "$StagingDir\RynthCore.App.Avalonia.deps.json"
    if (Test-Path $avDeps) {
        Move-Item -LiteralPath $avDeps -Destination "$StagingDir\RynthCore.deps.json" -Force
    }
} else {
    throw "Expected launcher executable not found after publish: RynthCore.App.Avalonia.exe under $LauncherPublish"
}

# Engine runtime files (into Runtime\, skip .pdb)
foreach ($file in (Get-ChildItem "$EnginePublish" -File)) {
    if ($file.Extension -eq '.pdb') { continue }
    Copy-Item $file.FullName "$StagingDir\Runtime\$($file.Name)" -Force
}

# Engine Native subfolder
if (Test-Path "$EnginePublish\Native") {
    foreach ($file in (Get-ChildItem "$EnginePublish\Native" -File)) {
        if ($file.Extension -eq '.pdb') { continue }
        Copy-Item $file.FullName "$StagingDir\Runtime\Native\$($file.Name)" -Force
    }
}

# Injector (self-contained x86) — full publish tree into Runtime\ next to the engine
if (-not (Test-Path $InjectorPublish)) {
    throw "Injector publish not found: $InjectorPublish`nRun without -SkipBuild, or publish RynthCore.Injector for $Configuration|win-x86."
}
Copy-PublishTree -SourceRoot $InjectorPublish -DestRoot "$StagingDir\Runtime" -ExcludePdb

# Plugin DLLs only (cimgui.dll excluded — already in Runtime from Engine)
foreach ($plugin in $PluginProjects) {
    if (-not (Test-Path $plugin.DllPath)) {
        throw "Plugin DLL not found at: $($plugin.DllPath)"
    }

    Copy-Item $plugin.DllPath "$StagingDir\Runtime\Plugins\" -Force
}

if ($IncludeSuiteTools) {
    foreach ($tool in @(
        @{ Source = "$RepoRoot\installer\_toolpublish\LootEditor"; Dest = "$StagingDir\Tools\LootEditor" },
        @{ Source = "$RepoRoot\installer\_toolpublish\MonsterEditor"; Dest = "$StagingDir\Tools\MonsterEditor" }
    )) {
        if (-not (Test-Path $tool.Source)) {
            throw "Tool publish output not found: $($tool.Source)"
        }
        # FDD x64 tools — copy the full tree (runtimes\ from NuGet, not a bundled .NET host)
        Copy-PublishTree -SourceRoot $tool.Source -DestRoot $tool.Dest -ExcludePdb
    }
}

# Report staged sizes
$engineDll = "$StagingDir\Runtime\RynthCore.Engine.dll"
if (Test-Path $engineDll) {
    $sizeMb = [math]::Round((Get-Item $engineDll).Length / 1MB, 1)
    Write-Host "  Engine.dll: ${sizeMb} MB"
}
$totalMb = [math]::Round((Get-ChildItem "$StagingDir" -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "  Total staged: ${totalMb} MB"

Write-Host "Staging complete: $StagingDir" -ForegroundColor Green

# ── Archive prior bundle when -Version bumps ────────────────────────────────
# Copies installer\Output\RynthBundle-Setup.exe to previous-release\ using the
# version recorded in previous-release\last-built-version.txt (updated after a
# successful ISCC when -Version is set). See BUILD.md "Previous release folder".
$previousReleaseDir = Join-Path $ScriptDir "previous-release"
$lastVersionFile    = Join-Path $previousReleaseDir "last-built-version.txt"
$bundleExe          = Join-Path $ScriptDir "Output\RynthBundle-Setup.exe"

function Get-LastBuiltBundleVersion {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    return (Get-Content -LiteralPath $Path -Raw).Trim()
}

function Get-SafeVersionFileSuffix {
    param([string]$Ver)
    # Allow digits, dots, letters, hyphen for Inno-style version labels.
    $s = ($Ver -replace '[^\d\.\w\-]', '_')
    if (-not $s) { return "unknown" }
    return $s
}

if ($Version) {
    $versionTrim = $Version.Trim()
    New-Item -ItemType Directory -Path $previousReleaseDir -Force | Out-Null
    $lastVer = Get-LastBuiltBundleVersion -Path $lastVersionFile
    if ((Test-Path -LiteralPath $bundleExe) -and $lastVer -and ($lastVer -ne $versionTrim)) {
        $suffix = Get-SafeVersionFileSuffix -Ver $lastVer
        $archivePath = Join-Path $previousReleaseDir "RynthBundle-Setup-$suffix.exe"
        Write-Host ""
        Write-Host "Archiving previous bundle ($lastVer) -> $archivePath" -ForegroundColor Cyan
        Copy-Item -LiteralPath $bundleExe -Destination $archivePath -Force
    }
}

# ── Inno Setup ───────────────────────────────────────────────────────────────
if (-not (Test-Path $IsccPath)) {
    Write-Host ""
    Write-Warning "Inno Setup compiler not found at:"
    Write-Warning "  $IsccPath"
    Write-Warning "Install Inno Setup 6 from: https://jrsoftware.org/isdl.php"
    Write-Warning "Then re-run this script, or invoke ISCC manually:"
    Write-Warning "  `"$IsccPath`" `"$ScriptDir\RynthCore.iss`""
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
$installerExeName = "RynthBundle-Setup"
if ($Version) {
    $versionTrim = $Version.Trim()
    $issContent = $issContent -replace 'AppVersion=0\.0\.0', "AppVersion=$versionTrim"
    # Versioned .exe avoids Inno error 32 when Output\RynthBundle-Setup.exe is open in Explorer or AV.
    $fnSuffix = Get-SafeVersionFileSuffix -Ver $versionTrim
    $installerExeName = "RynthBundle-Setup-$fnSuffix"
    $issContent = $issContent -replace 'OutputBaseFilename=RynthBundle-Setup', "OutputBaseFilename=$installerExeName"
    Write-Host "  Version override: $versionTrim (OutputBaseFilename=$installerExeName)"
}
Set-Content -Path $issTmp -Value $issContent -Encoding UTF8

try {
    & $IsccPath $issTmp
    if ($LASTEXITCODE -ne 0) { throw "ISCC build failed (exit $LASTEXITCODE)" }
} finally {
    Remove-Item $issTmp -Force -ErrorAction SilentlyContinue
}

$builtInstaller = Join-Path $ScriptDir "Output\$installerExeName.exe"
# Best-effort copy to the legacy canonical name for scripts that expect RynthBundle-Setup.exe.
if ($Version -and (Test-Path -LiteralPath $builtInstaller)) {
    $canonicalInstaller = Join-Path $ScriptDir "Output\RynthBundle-Setup.exe"
    try {
        Copy-Item -LiteralPath $builtInstaller -Destination $canonicalInstaller -Force
    }
    catch {
        Write-Warning "Could not copy to RynthBundle-Setup.exe (file may be in use). Primary artifact: $builtInstaller"
    }
}

# Record the version that produced the bundle (enables archive on next bump).
if ($Version) {
    New-Item -ItemType Directory -Path $previousReleaseDir -Force | Out-Null
    [System.IO.File]::WriteAllText($lastVersionFile, $Version.Trim())
}

Write-Host ""
Write-Host "SUCCESS" -ForegroundColor Green
Write-Host "Installer: $builtInstaller"

