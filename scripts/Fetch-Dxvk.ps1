# Fetch-Dxvk.ps1
# Downloads the latest official DXVK release and stages x32/d3d9.dll into a cache folder
# (default: C:\Games\RynthCore\Runtime\dxvk). Used by the launcher's "Fetch / update DXVK"
# (C# twin: DxvkPackageService) and by Build-Installer.ps1 to seed the installer payload.
#
#   .\scripts\Fetch-Dxvk.ps1
#   .\scripts\Fetch-Dxvk.ps1 -Target "D:\asherons call-int code\RynthCore\installer\staging\core\Runtime\dxvk"
#   .\scripts\Fetch-Dxvk.ps1 -Version 3.1.1   # pin a tag (without leading v)

param(
    [string]$Target = "C:\Games\RynthCore\Runtime\dxvk",
    [string]$Version = ""   # empty = latest from GitHub
)

$ErrorActionPreference = "Stop"

function Get-LatestDxvkTag {
    $headers = @{ "User-Agent" = "RynthCore-Fetch-Dxvk/1.0"; "Accept" = "application/vnd.github+json" }
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/doitsujin/dxvk/releases/latest" -Headers $headers
    return ($rel.tag_name -replace '^v', '')
}

function Get-DxvkTarballUrl([string]$tag) {
    $headers = @{ "User-Agent" = "RynthCore-Fetch-Dxvk/1.0"; "Accept" = "application/vnd.github+json" }
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/doitsujin/dxvk/releases/tags/v$tag" -Headers $headers
    $asset = $rel.assets | Where-Object {
        $_.name -like "dxvk-*.tar.gz" -and $_.name -notlike "*native*"
    } | Select-Object -First 1
    if (-not $asset) { throw "No dxvk-*.tar.gz asset on release v$tag" }
    return @{ Url = $asset.browser_download_url; Name = $asset.name }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-LatestDxvkTag
    Write-Host "Latest DXVK tag: $Version"
}

$asset = Get-DxvkTarballUrl $Version
$stage = Join-Path $env:TEMP ("rynthcore-dxvk-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$tarball = Join-Path $stage $asset.Name
Write-Host "Downloading $($asset.Url) ..."
Invoke-WebRequest -Uri $asset.Url -OutFile $tarball -UseBasicParsing

$extract = Join-Path $stage "extract"
New-Item -ItemType Directory -Force -Path $extract | Out-Null
tar -xzf $tarball -C $extract

$src = Get-ChildItem $extract -Recurse -Filter "d3d9.dll" |
    Where-Object { $_.Directory.Name -eq "x32" } |
    Select-Object -First 1
if (-not $src) { throw "x32/d3d9.dll not found in archive" }

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item $src.FullName (Join-Path $Target "d3d9.dll") -Force
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $Target "version.txt"), $Version, $utf8)
$conf = @"
# Managed by RynthCore (launcher Enable DXVK). Safe to delete with the toggle off.
dxgi.deferSurfaceCreation = True
d3d9.maxFrameLatency = 1
d3d9.presentInterval = 1
"@
[IO.File]::WriteAllText((Join-Path $Target "dxvk.conf"), $conf.TrimStart() + "`n", $utf8)

Write-Host "DXVK $Version staged at $Target"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
