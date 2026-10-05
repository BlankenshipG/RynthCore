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
  3. With -Version, Build-Installer.ps1 also assembles the deployment package
     installer\Output\Release-<version>\ and installer\Output\RynthCore-<version>-deploy.zip
     (installer + SHA256SUMS.txt + release-manifest.json + RELEASE-NOTES.md, hash-verified).

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

# Plugin API delegates must be cdecl. On x86 a delegate without
# [UnmanagedFunctionPointer(CallingConvention.Cdecl)] marshals as stdcall, and every cdecl call
# from a plugin then pops its arguments twice and corrupts the plugin's stack.
$contract = Join-Path $RepoRoot "src\RynthCore.Engine\Plugins\PluginContract.cs"
$contractLines = [IO.File]::ReadAllLines($contract)
$badDelegates = @()
for ($i = 0; $i -lt $contractLines.Length; $i++) {
    if ($contractLines[$i] -notmatch '^\s*(internal|public)\s+(unsafe\s+)?delegate\s') { continue }
    # Nearest previous line that is not blank and not a // comment must carry the attribute.
    $j = $i - 1
    while ($j -ge 0 -and ($contractLines[$j].Trim() -eq '' -or $contractLines[$j].Trim().StartsWith('//'))) { $j-- }
    if ($j -lt 0 -or $contractLines[$j] -notmatch 'UnmanagedFunctionPointer\(CallingConvention\.Cdecl\)') {
        $badDelegates += "  line $($i + 1): $($contractLines[$i].Trim())"
    }
}
if ($badDelegates.Count -gt 0) {
    throw "PluginContract.cs delegate(s) missing [UnmanagedFunctionPointer(CallingConvention.Cdecl)]:`n$($badDelegates -join "`n")"
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
