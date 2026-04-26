#Requires -Version 5.1
<#
.SYNOPSIS
  Builds a full Release of RynthCore + RynthSuite and produces a single Windows installer.

.DESCRIPTION
  1. Compiles the RynthCore.sln in Release (validates all SDK projects).
  2. Invokes installer\Build-Installer.ps1, which:
     - Publishes launcher, engine, injector, RynthSuite plugin(s), ub-Rythai when present, LootEditor, MonsterEditor
     - Stages files under installer\staging\app\
     - Compiles RynthCore.iss → installer\Output\RynthBundle-Setup.exe (requires Inno Setup 6)
     - When -Version is set, archives the prior Output\RynthBundle-Setup.exe to installer\previous-release\

.PARAMETER Version
  Optional version string written into the installer (e.g. 0.4.1).

.EXAMPLE
  .\Build-Release-All.ps1 -Version 0.4.2

.EXAMPLE
  .\Build-Release-All.ps1 -Version 0.4.2 -SkipUbRythai   # CI without ub-Rythai checkout
#>
param(
    [string]$Version = "",
    [string]$RynthSuiteRoot = "",
    [string]$UbRythaiPluginProject = "",
    [switch]$SkipUbRythai,
    [string]$IsccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"
# This script lives at the RynthCore repository root.
$RepoRoot = $PSScriptRoot

$sln = Join-Path $RepoRoot "RynthCore.sln"
if (-not (Test-Path $sln)) {
    throw "RynthCore.sln not found at: $sln"
}

Write-Host "Building solution: $sln" -ForegroundColor Cyan
dotnet build $sln -c Release
if ($LASTEXITCODE -ne 0) { throw "Solution build failed (exit $LASTEXITCODE)" }

$invokeArgs = @{
    Configuration  = "Release"
    IsccPath       = $IsccPath
    IncludeSuiteTools = $true
    SkipBuild      = $false
}
if ($Version) { $invokeArgs.Version = $Version }
if ($RynthSuiteRoot) { $invokeArgs.RynthSuiteRoot = $RynthSuiteRoot }
if ($UbRythaiPluginProject) { $invokeArgs.UbRythaiPluginProject = $UbRythaiPluginProject }
if ($SkipUbRythai) { $invokeArgs.SkipUbRythai = $true }

& (Join-Path $RepoRoot "installer\Build-Installer.ps1") @invokeArgs
if ($LASTEXITCODE -ne 0) { throw "Build-Installer.ps1 failed (exit $LASTEXITCODE)" }
