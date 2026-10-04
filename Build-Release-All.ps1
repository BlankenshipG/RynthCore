#Requires -Version 5.1
<#
.SYNOPSIS
  Builds a full Release of RynthCore + RynthSuite and produces the Windows installer.

.DESCRIPTION
  1. Compiles RynthCore.sln in Release (validates all SDK projects before the slow publishes).
  2. Invokes installer\Build-Installer.ps1, which publishes the launcher, engine, loader,
     RynthAi, the Loot/Monster editors and the experimental plugins (incl. ub-Rythai), stages
     them, archives the previous installer when -Version changes, and compiles RynthCore.iss
     into installer\Output\RynthCore-Setup-<version>.exe (requires Inno Setup 6).

.PARAMETER Version
  Installer version (e.g. 2026.10.4.7). Passed through to Build-Installer.ps1.

.PARAMETER RynthSuiteRoot
  RynthSuite checkout. Default (empty): the sibling ..\RynthSuite folder.

.PARAMETER IsccPath
  ISCC.exe location. Default (empty): Build-Installer.ps1 searches the usual Inno Setup paths.

.EXAMPLE
  .\Build-Release-All.ps1 -Version 2026.10.4.7
#>
param(
    [string]$Version = "",
    [string]$RynthSuiteRoot = "",
    [string]$IsccPath = ""
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

# Only forward parameters Build-Installer.ps1 actually declares.
$invokeArgs = @{ Configuration = "Release" }
if ($Version)        { $invokeArgs.Version = $Version }
if ($RynthSuiteRoot) { $invokeArgs.RynthSuiteRoot = $RynthSuiteRoot }
if ($IsccPath)       { $invokeArgs.IsccPath = $IsccPath }

& (Join-Path $RepoRoot "installer\Build-Installer.ps1") @invokeArgs
if ($LASTEXITCODE -ne 0) { throw "Build-Installer.ps1 failed (exit $LASTEXITCODE)" }
