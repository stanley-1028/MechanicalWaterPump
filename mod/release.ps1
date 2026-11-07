# Mechanical Water Pump (MWP) - release packaging
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File mod\release.ps1
#   powershell ... -File mod\release.ps1 -SkipBuild      # reuse an existing build/MWP
#   powershell ... -File mod\release.ps1 -Install        # also install the upload-ready folder
#
# Produces, under <repo>\dist\:
#   MechanicalWaterPump-<version>\        multiplayer/upload-ready mod folder
#   MechanicalWaterPump-<version>.zip     GitHub release asset / manual install archive
#   MechanicalWaterPump-<version>.sha256  checksum
#
# The upload-ready folder additionally contains the files the in-game Steam Workshop uploader
# reads from the mod root: workshop_data.json, thumbnail.png, thumbnail.png.meta.json
# (verified from game IL: Timberborn.SteamWorkshop + ModUploaderBox).

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$TimberbornDir = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn',
    [switch]$SkipBuild,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = Split-Path -Parent $root
$workShopDir = Join-Path $root 'workshop'
$stageDir = Join-Path $root 'build\MWP'

if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -Configuration $Configuration -TimberbornDir $TimberbornDir
    if ($LASTEXITCODE -ne 0) {
        throw "build.ps1 failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path $stageDir)) {
    throw "staged mod folder not found: $stageDir (run build.ps1 first)"
}

$manifest = Get-Content (Join-Path $stageDir 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $manifest.Version
if (-not $version) {
    throw 'manifest.json has no Version'
}

$distRoot = Join-Path $repo 'dist'
$distDir = Join-Path $distRoot ("MechanicalWaterPump-" + $version)
$zipPath = Join-Path $distRoot ("MechanicalWaterPump-" + $version + ".zip")
$shaPath = $zipPath + '.sha256'

if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item (Join-Path $stageDir '*') $distDir -Recurse -Force

# Workshop metadata (in-game uploader reads these from the mod root).
foreach ($file in @('thumbnail.png', 'thumbnail.png.meta.json', 'workshop_data.json')) {
    $source = Join-Path $workShopDir $file
    if (-not (Test-Path $source)) { throw "workshop asset missing: $source" }
    Copy-Item $source $distDir -Force
}

# Human docs are nice to have inside the uploaded folder.
foreach ($doc in @('CHANGELOG.md', 'LICENSE')) {
    $source = if ($doc -eq 'LICENSE') { Join-Path $repo 'LICENSE' } else { Join-Path $root $doc }
    if (Test-Path $source) { Copy-Item $source $distDir -Force }
}

# ---- sanity checks ----------------------------------------------------------
$required = @('manifest.json', 'MWP.Core.dll', 'MWP.Gameplay.dll', 'workshop_data.json', 'thumbnail.png')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $distDir $file))) { throw "release folder is missing $file" }
}

$timbermeshes = (Get-ChildItem $distDir -Recurse -Filter '*.timbermesh').Count
$icons = (Get-ChildItem $distDir -Recurse -Filter '*.png' | Where-Object { $_.DirectoryName -like '*Icons*' }).Count
$blueprints = (Get-ChildItem $distDir -Recurse -Filter '*.blueprint.json').Count
if ($timbermeshes -lt 16) { throw "release folder has $timbermeshes .timbermesh files, expected 16" }
if ($icons -lt 8) { throw "release folder has $icons icons, expected 8" }
if ($blueprints -lt 10) { throw "release folder has $blueprints blueprints, expected 10" }

# ---- zip + checksum ---------------------------------------------------------
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Set-Content -Path $shaPath -Value ($hash + '  ' + (Split-Path -Leaf $zipPath)) -Encoding ASCII

$sizeKb = [Math]::Round(((Get-ChildItem $distDir -Recurse -File | Measure-Object -Sum Length).Sum) / 1KB, 0)
Write-Host ''
Write-Host ("Release folder : {0}" -f $distDir) -ForegroundColor Green
Write-Host ("Version        : {0}" -f $version)
Write-Host ("Contents       : {0} files, {1} KB, {2} .timbermesh, {3} icons, {4} blueprints" -f `
        (Get-ChildItem $distDir -Recurse -File).Count, $sizeKb, $timbermeshes, $icons, $blueprints)
Write-Host ("Zip            : {0}" -f $zipPath)
Write-Host ("SHA256         : {0}" -f $hash)

# ---- optional: install the upload-ready folder into the local Mods directory ----
if ($Install) {
    $target = Join-Path $env:USERPROFILE 'Documents\Timberborn\Mods\MechanicalWaterPump'
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $distDir '*') $target -Recurse -Force
    Write-Host ("Installed      : {0}" -f $target) -ForegroundColor Green
    Write-Host 'Open Timberborn -> Mods -> (this mod) -> Upload to publish/update on Steam Workshop.' -ForegroundColor Yellow
}
