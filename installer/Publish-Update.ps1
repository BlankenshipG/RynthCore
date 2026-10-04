#Requires -Version 7
<#
.SYNOPSIS
    Builds, signs and publishes a RynthCore + RynthSuite release to the launchers' update feed.

.DESCRIPTION
    One command per release:
      1. Refuses if the signing key isn't set up or isn't trusted by the launcher being shipped,
         if either repo has uncommitted changes, or if -Version isn't newer than the live feed.
      2. Builds the installer (Build-Installer.ps1 -Version; it includes the Decal bridge, so the
         build machine needs Decal installed) and the RynthSuite plugins into
         installer\Releases\<release>\ — never into the live C:\Games folders.
      3. Writes the manifest (sizes + SHA-256 of every file), signs it with the DPAPI key
         (Sign-Update.ps1, no prompt) and checks the signature the way the launcher will.
      4. Uploads the release files, checks their hashes on the server, replaces the website's
         RynthCore-Setup.exe and RynthSuite-Plugins.zip (old ones kept in ~/backups/rynth),
         and swaps in update.json last, atomically.
      5. Fetches the live feed back and compares it byte for byte.

    Launchers pick it up at their next check (start-up, every 6 hours, or the button). Plugins
    swap in place without touching running bots; the core installer waits until every
    RynthCore client is closed.

.EXAMPLE
    .\Publish-Update.ps1 -Version 2026.9.28.1 -Changes "Arrow crafting covers every head type.", "The Tell button works on players too."
#>
param(
    [Parameter(Mandatory)][string]$Version,
    # Release notes for players, one line per change: they become this release's entry in
    # downloads/rynth/releases.json, which the "What's new" list on aelrynth.com/rynth.html shows.
    [string[]]$Changes = @(),
    # Short text the launcher shows next to an available update; defaults to the changes.
    [string]$Notes = "",
    [switch]$DryRun,        # build + sign + verify, upload nothing
    [switch]$AllowDirty,    # publish with uncommitted changes (don't)
    [string]$SshHost = "aelrynth-vps",
    [string]$RemoteDir = "/var/www/site/downloads/rynth",
    [string]$BaseUrl = "https://aelrynth.com/downloads/rynth",
    [string]$KeyDir = (Join-Path $env:USERPROFILE ".rynthcore-update-signing"),
    [string]$RynthSuiteRoot = "",
    [string]$TrustFile = "",  # defaults to the launcher's UpdateTrust.cs; override for tests
    # RynthNav's data shipped to launchers as the signed "navdata" archive: tiles (nav_XXXX.tile),
    # portals.tsv and locations.json from this folder. Only re-uploaded when the set changes
    # (its id is a hash of every file); otherwise the live feed's entry is carried over.
    [string]$NavDataDir = "C:\Games\RynthCore\NavData",
    [switch]$SkipNavData       # leave navdata out of this feed (launchers then keep what they have)
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path $ScriptDir -Parent
if (-not $RynthSuiteRoot) { $RynthSuiteRoot = Join-Path (Split-Path $RepoRoot -Parent) "RynthSuite" }
if (-not $TrustFile) { $TrustFile = Join-Path $RepoRoot "src\RynthCore.App.Avalonia\UpdateTrust.cs" }

function Step($m) { Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function Sha256([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Sha256Text([string]$text) { [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant() }

# Same check as RynthUpdater.VerifyEnvelope: any trusted key's signature over the payload bytes.
function Test-Envelope([string]$path, [string[]]$keys) {
    $envelope = Get-Content $path -Raw | ConvertFrom-Json
    $payload = [Convert]::FromBase64String($envelope.payload)
    foreach ($k in $keys) {
        $spki = [Convert]::FromBase64String($k)
        $id = ([Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($spki), 0, 8)).ToLowerInvariant()
        $ec = [System.Security.Cryptography.ECDsa]::Create()
        $read = 0; $ec.ImportSubjectPublicKeyInfo($spki, [ref]$read)
        foreach ($s in $envelope.signatures) {
            if ($s.key -ne $id) { continue }
            if ($ec.VerifyData($payload, [Convert]::FromBase64String($s.sig),
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                    [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) { return $true }
        }
    }
    return $false
}

# ── 1. Preconditions ─────────────────────────────────────────────────────────
Step "Checking preconditions"
$p = $Version.Split('.')
if ($p.Count -ne 4 -or ($p | Where-Object { $_ -notmatch '^\d+$' })) { throw "-Version must be yyyy.m.d.n, got '$Version'" }
$release = [int]$p[0] * 1000000 + [int]$p[1] * 10000 + [int]$p[2] * 100 + [int]$p[3]
if ($Changes.Count -eq 0 -and $Notes) { $Changes = @($Notes -split ';\s*' | Where-Object { $_ }) }
if ($Changes.Count -eq 0) { throw "Give -Changes: the release notes players see on aelrynth.com/rynth.html (one string per change)." }
if (-not $Notes) { $Notes = $Changes -join " " }

$trusted = [regex]::Matches((Get-Content $TrustFile -Raw), '"([A-Za-z0-9+/=]{80,})"') | ForEach-Object { $_.Groups[1].Value }
if ($trusted.Count -lt 2) {
    throw "UpdateTrust.cs has $($trusted.Count) key(s); it needs the primary and the backup. Run New-UpdateSigningKey.ps1 and commit UpdateTrust.cs."
}
$probe = New-TemporaryFile
try {
    Set-Content $probe "probe $release" -NoNewline
    & (Join-Path $ScriptDir "Sign-Update.ps1") -PayloadPath $probe -OutPath "$probe.sig" -KeyDir $KeyDir | Out-Null
    if (-not (Test-Envelope "$probe.sig" $trusted)) {
        throw "The signing key in $KeyDir is not one UpdateTrust.cs trusts: launchers would refuse this release."
    }
} finally { Remove-Item $probe, "$probe.sig" -ErrorAction SilentlyContinue }
Write-Host "  signing key is trusted by the launcher ($($trusted.Count) keys)"

if (-not $AllowDirty) {
    foreach ($repo in $RepoRoot, $RynthSuiteRoot) {
        $dirty = git -C $repo status --porcelain --untracked-files=no
        if ($dirty) { throw "$repo has uncommitted changes — release from committed code (or pass -AllowDirty):`n$dirty" }
    }
    Write-Host "  RynthCore $(git -C $RepoRoot rev-parse --short HEAD), RynthSuite $(git -C $RynthSuiteRoot rev-parse --short HEAD)"
}

$liveManifest = $null
try {
    $live = Invoke-RestMethod "$BaseUrl/update.json" -TimeoutSec 20
    $liveManifest = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($live.payload)) | ConvertFrom-Json
    $liveRelease = $liveManifest.release
    if ($release -le $liveRelease) { throw "Release $release is not newer than the live feed's $liveRelease — bump -Version." }
    Write-Host "  live feed is at $liveRelease; publishing $release"
} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
    Write-Host "  no live feed yet — this is the first release"
}

# ── 2. Build ─────────────────────────────────────────────────────────────────
$rel = Join-Path $ScriptDir "Releases\$release"
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Force "$rel\plugins", "$rel\build" | Out-Null

Step "Building the installer ($Version)"
& (Join-Path $ScriptDir "Build-Installer.ps1") -Version $Version -RynthSuiteRoot $RynthSuiteRoot
if (-not $?) { throw "Build-Installer.ps1 failed" }
# The Decal bridge ships in every release (experimental, opt-in per account): the installer
# carries it, so a launcher that updates through this feed gets it as a fresh install does.
# Build-Installer.ps1 fails without Decal; this guards against a -SkipDecalBridge staging.
$bridgeStaged = Join-Path $ScriptDir "staging\app\DecalBridge\RynthCore.DecalBridge.dll"
if (-not (Test-Path $bridgeStaged)) { throw "The installer was built without the Decal bridge ($bridgeStaged missing) - not publishing." }
Write-Host "  Decal bridge in the installer: $((Get-Item $bridgeStaged).VersionInfo.FileVersion)"
Copy-Item (Join-Path $ScriptDir "Output\RynthCore-Setup.exe") "$rel\RynthCore-Setup.exe"
Copy-Item (Join-Path $ScriptDir "staging\plugins\RynthAi\RynthCore.Plugin.RynthAi.dll") "$rel\plugins\"

$suitePlugins = "RynthChat", "RynthTracker", "RynthNav", "RynthVision", "RynthLua", "RynthOracle", "RynthInventory"
foreach ($name in $suitePlugins) {
    Step "Building $name"
    $proj = Join-Path $RynthSuiteRoot "Plugins\RynthCore.Plugin.$name\RynthCore.Plugin.$name.csproj"
    # PublishDir overrides the projects' own (RynthNav/RynthVision publish into the live C:\Games folders).
    dotnet publish $proj -c Release "-p:Version=$Version" "-p:PublishDir=$rel\build\$name\" -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $name" }
    Copy-Item "$rel\build\$name\RynthCore.Plugin.$name.dll" "$rel\plugins\"
}

Step "Packing RynthSuite-Plugins.zip"
$zipRoot = "$rel\build\zip"
foreach ($name in $suitePlugins) {
    New-Item -ItemType Directory -Force "$zipRoot\RynthSuite\$name" | Out-Null
    Copy-Item "$rel\plugins\RynthCore.Plugin.$name.dll" "$zipRoot\RynthSuite\$name\"
    # Third-party licence notices travel with the plugin (RynthOracle: Oracle of Dereth, MIT).
    $notice = "$rel\build\$name\THIRD_PARTY_NOTICES.md"
    if (Test-Path $notice) { Copy-Item $notice "$zipRoot\RynthSuite\$name\" }
}
$navSrc = $NavDataDir
New-Item -ItemType Directory -Force "$zipRoot\RynthCore\NavData" | Out-Null
# The route planner's portals.tsv and the Atlas's locations.json (both made by
# tools\navdata\gen_locations.py from the world database), and the Town Network's townnet.json
# (tools\navdata\Build-TownNet.ps1: the world database plus the walks inside from the dats).
# Never the player's own files
# there (atlas.txt, recalls.txt, _player.txt). The tiles are no longer in this zip (the whole
# world is ~40,000 of them): the launcher installs them from the feed's navdata archive.
Get-ChildItem $navSrc -File | Where-Object { $_.Name -eq "portals.tsv" -or $_.Name -eq "locations.json" -or $_.Name -eq "townnet.json" } |
    Copy-Item -Destination "$zipRoot\RynthCore\NavData\"
if (-not (Test-Path "$navSrc\locations.json")) {
    Write-Warning "NavData has no locations.json: RynthNav's Atlas ships empty. Run tools\navdata\gen_locations.py and copy its output there."
}
if (-not (Test-Path "$navSrc\townnet.json")) {
    Write-Warning "NavData has no townnet.json: routes won't use the Town Network. Run tools\navdata\Build-TownNet.ps1 and copy its townnet.json there."
}
@"
RynthSuite plugins for RynthCore (Asheron's Call, retail client) - $Version

Extract this zip into C:\Games - it creates:
  C:\Games\RynthSuite\RynthChat\     RynthChat plugin
  C:\Games\RynthSuite\RynthNav\      RynthNav plugin
  C:\Games\RynthSuite\RynthTracker\  RynthTracker plugin
  C:\Games\RynthSuite\RynthVision\   RynthVision plugin
  C:\Games\RynthSuite\RynthLua\      RynthLua plugin (Lua scripting; was part of RynthAi)
  C:\Games\RynthSuite\RynthOracle\   RynthOracle plugin (quests, character, titles; based on Oracle of Dereth by Advis Eveldan)
  C:\Games\RynthSuite\RynthInventory\ RynthInventory plugin (search every character's items)
  C:\Games\RynthCore\NavData\        RynthNav's portals and Atlas

Then, in the RynthCore launcher's Plugins tab, add the full path of each
plugin DLL you want. Install RynthCore first (RynthCore-Setup.exe), which
also installs RynthAi and the Loot Editor. After that the launcher keeps
the plugins up to date by itself, and once RynthNav is in the list it also
downloads RynthNav's navmesh tiles into C:\Games\RynthCore\NavData.

MIT licensed. https://aelrynth.com/projects.html
"@ -replace "`n", "`r`n" | Set-Content "$zipRoot\README.txt" -NoNewline
Compress-Archive -Path "$zipRoot\*" -DestinationPath "$rel\RynthSuite-Plugins.zip" -CompressionLevel Optimal

# RynthNav's data for the launchers (RynthUpdater.NavData.cs): one archive, flat, only the file
# names RynthNav reads. Its id is a hash over every file's name and SHA-256, so an unchanged
# set reuses the live entry (and the archive already on the server) instead of re-uploading.
$navData = $null
$navZip = $null
if (-not $SkipNavData) {
    Step "RynthNav data (tiles, route graph, portals, Atlas, Town Network) from $NavDataDir"
    $navFiles = @(Get-ChildItem $NavDataDir -File | Where-Object { $_.Name -match '^(nav_[0-9A-Fa-f]{4}\.tile|portals\.tsv|locations\.json|navgraph\.bin|townnet\.json)$' } | Sort-Object Name)
    $tileCount = @($navFiles | Where-Object { $_.Extension -eq ".tile" }).Count
    if ($tileCount -eq 0) { throw "$NavDataDir has no tiles. Pass -NavDataDir, or -SkipNavData to publish without them." }
    # The long-range route graph (RynthNav.Baker --graph) must come from these very tiles: the
    # plugin ignores a graph whose tiles don't match (long gotos then walk the straight line).
    $graph = $navFiles | Where-Object { $_.Name -eq "navgraph.bin" }
    $newestTile = ($navFiles | Where-Object { $_.Extension -eq ".tile" } | Measure-Object LastWriteTimeUtc -Maximum).Maximum
    if (-not $graph) { Write-Warning "No navgraph.bin in ${NavDataDir}: long gotos will walk the straight line. Run: RynthNav.Baker --graph $NavDataDir" }
    elseif ($graph.LastWriteTimeUtc -lt $newestTile) { Write-Warning "navgraph.bin is older than the newest tile: rebuild it (RynthNav.Baker --graph $NavDataDir), or RynthNav will ignore it." }
    $idText = ($navFiles | ForEach-Object { "$($_.Name) $(Sha256 $_.FullName)" }) -join "`n"
    $navId = (Sha256Text $idText).Substring(0, 32)
    # [long]: Measure-Object's Sum is a double, which ConvertTo-Json writes as 1383002945.0, and the
    # launcher reads "bytes" with GetInt64 - a ".0" failed every launcher's update check (2026.10.3.1/2).
    $navBytes = [long](($navFiles | Measure-Object Length -Sum).Sum)
    if ($liveManifest -and $liveManifest.navdata -and $liveManifest.navdata.id -eq $navId) {
        $navData = $liveManifest.navdata
        # Carried over from the live feed (ConvertFrom-Json): force the whole numbers back to integers.
        $navData = [ordered]@{
            id      = [string]$navData.id
            release = [long]$navData.release
            url     = [string]$navData.url
            size    = [long]$navData.size
            sha256  = [string]$navData.sha256
            files   = [long]$navData.files
            bytes   = [long]$navData.bytes
        }
        Write-Host "  unchanged ($($navFiles.Count) files, id $navId): the live archive from release $($navData.release) stays"
    } else {
        $navZip = "$rel\RynthNav-NavData.zip"
        Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
        $fs = [IO.File]::Open($navZip, 'CreateNew')
        try {
            $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
            foreach ($f in $navFiles) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $f.Name, [IO.Compression.CompressionLevel]::Optimal)
            }
            $za.Dispose()
        } finally { $fs.Dispose() }
        $zi = Get-Item $navZip
        $navData = [ordered]@{
            id      = $navId
            release = $release
            url     = "$BaseUrl/releases/$release/RynthNav-NavData.zip"
            size    = $zi.Length
            sha256  = Sha256 $zi.FullName
            files   = $navFiles.Count
            bytes   = $navBytes
        }
        Write-Host ("  new set: {0} files ({1} tiles), {2:N0} MB packed into {3:N0} MB, id {4}" -f $navFiles.Count, $tileCount, ($navBytes / 1MB), ($zi.Length / 1MB), $navId)
    }
}

# ── 3. Manifest + signature ──────────────────────────────────────────────────
Step "Writing and signing the manifest"
$setup = Get-Item "$rel\RynthCore-Setup.exe"
# One line per plugin for the launcher's Available plugins list (launchers from 2026.10.5 on;
# older ones ignore the field, and a plugin without one falls back to the launcher's own table
# in RynthUpdater.Available.cs). Plain English for players, no names.
$pluginDescriptions = @{
    RynthAi        = "The bot: combat, buffs, looting, navigation routes and metas."
    RynthChat      = "A chat window that sorts messages into tabs."
    RynthTracker   = "Kills, experience and luminance per hour for each session."
    RynthNav       = "Walks you to coordinates and towns, with route planning and the Atlas. Downloads its map (about 540 MB)."
    RynthVision    = "Shows unclimbable slopes, impassable water and your radar range in the world."
    RynthLua       = "Lua scripting for metas and your own scripts."
    RynthOracle    = "Quests, character, titles and leaderboards."
    RynthInventory = "Search every character's items."
}
$plugins = foreach ($dll in Get-ChildItem "$rel\plugins\*.dll" | Sort-Object Name) {
    $pluginName = $dll.BaseName -replace '^RynthCore\.Plugin\.', ''
    $entry = [ordered]@{
        name   = $pluginName
        file   = $dll.Name
        url    = "$BaseUrl/releases/$release/plugins/$($dll.Name)"
        size   = $dll.Length
        sha256 = Sha256 $dll.FullName
    }
    if ($pluginDescriptions[$pluginName]) { $entry.description = $pluginDescriptions[$pluginName] }
    else { Write-Warning "No description for $pluginName - the launcher shows its own (or none). Add one to `$pluginDescriptions." }
    $entry
}
$manifest = [ordered]@{
    schema    = 1
    release   = $release
    version   = $Version
    published = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    notes     = $Notes
    # The changes, one line each: the launcher's update card shows a few as "What's new"
    # (signed with the rest; launchers before 2026.10.5 ignore it and show the notes).
    changes   = @($Changes)
    core      = [ordered]@{
        release = $release
        version = $Version
        url     = "$BaseUrl/releases/$release/RynthCore-Setup.exe"
        size    = $setup.Length
        sha256  = Sha256 $setup.FullName
    }
    plugins   = @($plugins)
}
if ($navData) { $manifest.navdata = $navData }
Set-Content "$rel\update.payload.json" ($manifest | ConvertTo-Json -Depth 5) -Encoding utf8NoBOM
& (Join-Path $ScriptDir "Sign-Update.ps1") -PayloadPath "$rel\update.payload.json" -OutPath "$rel\update.json" -KeyDir $KeyDir
if (-not (Test-Envelope "$rel\update.json" $trusted)) { throw "Signed feed does not verify against UpdateTrust.cs" }
Write-Host "  verified against the launcher's trusted keys"

# Release notes: this release on top of the live list (a re-run replaces its own entry).
$history = @()
try { $history = @((Invoke-RestMethod "$BaseUrl/releases.json" -TimeoutSec 20).releases) }
catch [Microsoft.PowerShell.Commands.HttpResponseException] { Write-Host "  no releases.json yet — starting one" }
$entry = [ordered]@{ version = $Version; date = (Get-Date -Format "yyyy-MM-dd"); changes = @($Changes) }
$releaseNotes = [ordered]@{ releases = @($entry) + @($history | Where-Object { $_ -and $_.version -ne $Version }) }
Set-Content "$rel\releases.json" ($releaseNotes | ConvertTo-Json -Depth 5) -Encoding utf8NoBOM
Write-Host "  releases.json: $($releaseNotes.releases.Count) release(s), $($Changes.Count) change(s) in this one"

if ($DryRun) {
    Write-Host ""; Write-Host "Dry run: release built and signed in $rel; nothing uploaded." -ForegroundColor Yellow
    return
}

# ── 4. Upload ────────────────────────────────────────────────────────────────
Step "Uploading release $release"
$remoteRel = "$RemoteDir/releases/$release"
ssh $SshHost "mkdir -p '$remoteRel/plugins'"
if ($LASTEXITCODE -ne 0) { throw "ssh mkdir failed" }
scp -q "$rel\RynthCore-Setup.exe" "${SshHost}:$remoteRel/RynthCore-Setup.exe"
if ($LASTEXITCODE -ne 0) { throw "scp of the installer failed" }
scp -q (Get-ChildItem "$rel\plugins\*.dll").FullName "${SshHost}:$remoteRel/plugins/"
if ($LASTEXITCODE -ne 0) { throw "scp of the plugins failed" }
$navSum = ""
if ($navZip) {
    scp -q $navZip "${SshHost}:$remoteRel/RynthNav-NavData.zip"
    if ($LASTEXITCODE -ne 0) { throw "scp of the RynthNav data archive failed" }
    $navSum = " RynthNav-NavData.zip"
}

$remoteSums = ssh $SshHost "cd '$remoteRel' && sha256sum RynthCore-Setup.exe plugins/*.dll$navSum"
$expect = @{ "RynthCore-Setup.exe" = $manifest.core.sha256 }
if ($navZip) { $expect["RynthNav-NavData.zip"] = $navData.sha256 }
foreach ($pl in $plugins) { $expect["plugins/$($pl.file)"] = $pl.sha256 }
foreach ($line in $remoteSums) {
    $sum, $name = $line -split '\s+', 2
    if ($expect[$name.TrimStart('*')] -ne $sum) { throw "Hash mismatch on the server for $name — feed NOT published" }
}
Write-Host "  $($expect.Count) files uploaded and hash-checked"

Step "Replacing the website downloads"
scp -q "$rel\RynthCore-Setup.exe" "${SshHost}:$RemoteDir/.RynthCore-Setup.exe.new"
if ($LASTEXITCODE -ne 0) { throw "scp of the website installer failed" }
scp -q "$rel\RynthSuite-Plugins.zip" "${SshHost}:$RemoteDir/.RynthSuite-Plugins.zip.new"
if ($LASTEXITCODE -ne 0) { throw "scp of the plugins zip failed" }
ssh $SshHost "set -e; mkdir -p ~/backups/rynth; cd '$RemoteDir'; for f in RynthCore-Setup.exe RynthSuite-Plugins.zip; do [ -f `$f ] && cp -p `$f ~/backups/rynth/`$f.before-$release || true; mv -f .`$f.new `$f; done"
if ($LASTEXITCODE -ne 0) { throw "Replacing the website downloads failed" }

Step "Publishing the feed"
scp -q "$rel\update.json" "${SshHost}:$RemoteDir/.update.json.new"
if ($LASTEXITCODE -ne 0) { throw "scp of update.json failed" }
ssh $SshHost "mv -f '$RemoteDir/.update.json.new' '$RemoteDir/update.json'"
if ($LASTEXITCODE -ne 0) { throw "Publishing update.json failed" }
# The release notes follow the feed, so the page never lists a release launchers can't get yet.
scp -q "$rel\releases.json" "${SshHost}:$RemoteDir/.releases.json.new"
if ($LASTEXITCODE -ne 0) { throw "scp of releases.json failed" }
ssh $SshHost "mv -f '$RemoteDir/.releases.json.new' '$RemoteDir/releases.json'"
if ($LASTEXITCODE -ne 0) { throw "Publishing releases.json failed" }

# ── 5. Verify ────────────────────────────────────────────────────────────────
Step "Verifying the live feed"
$tmp = New-TemporaryFile
try {
    Invoke-WebRequest "$BaseUrl/update.json" -OutFile $tmp -UseBasicParsing -Headers @{ "Cache-Control" = "no-cache" }
    if ((Sha256 $tmp) -ne (Sha256 "$rel\update.json")) { throw "The live update.json differs from the one just signed!" }
    if (-not (Test-Envelope $tmp $trusted)) { throw "The live update.json does not verify!" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "Published RynthCore $Version (release $release)." -ForegroundColor Green
Write-Host "Launchers pick it up at their next check, and aelrynth.com/rynth.html lists it. Still by hand: the public source snapshots."
