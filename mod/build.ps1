# Mechanical Water Pump (MWP) build + package script
#
#   pwsh -File build.ps1                 # build, test, validate, stage the mod package
#   pwsh -File build.ps1 -SkipTests      # build + validate + stage only
#   pwsh -File build.ps1 -Install        # additionally copy into Documents\Timberborn\Mods
#
# IMPORTANT: every msbuild invocation uses -m:1. On this machine parallel MSBuild fails
# project-reference restore silently (exit 1, zero messages). See docs/API_FACTS_MWP.md ch.7.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$TimberbornDir = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn',
    [switch]$SkipTests,
    [switch]$Install,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$buildDir = Join-Path $root 'build'
$stageDir = Join-Path $buildDir 'MWP'
$managedDir = Join-Path $TimberbornDir 'Timberborn_Data\Managed'

function Invoke-Dotnet {
    param([string[]]$Arguments)
    # `dotnet run` starts the freshly built apphost right after linking it. On this machine the
    # first CreateProcess of that .exe intermittently fails with Win32 error 5 ("Access is denied",
    # localized text on a zh/dpi machine) - typically an AV/Defender real-time-scan race - and the
    # exact same command succeeds on the next attempt. Retry only that transient launch failure
    # (message emitted by dotnet run itself, so it is not localized by the child CLI), max 5 tries.
    # NOTE: keep this file pure ASCII: Windows PowerShell 5.1 reads BOM-less scripts as the system
    # ANSI codepage (Big5 here), so a non-ASCII literal would be corrupted at parse time.
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor DarkCyan
        # The script sets $ErrorActionPreference = 'Stop'. Native stderr merged with 2>&1 turns
        # into ErrorRecords that would terminate the function before the retry check can run, so
        # run the child with 'Continue' and judge success by the exit code instead.
        $previousEap = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $output = & dotnet @Arguments 2>&1
            $exitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previousEap
        }
        $output | ForEach-Object { Write-Host $_ }
        if ($exitCode -eq 0) { return }

        $text = ($output | Out-String)
        $transientLaunch = $text -match 'An error occurred trying to start process' -and
            $text -match 'with working directory'
        if ($transientLaunch -and $attempt -lt 5) {
            Write-Warning "transient apphost launch denial (attempt $attempt/5); retrying in 2s"
            Start-Sleep -Seconds 2
            continue
        }
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode"
    }
}

if (-not (Test-Path $managedDir)) {
    throw "Timberborn assemblies not found at '$managedDir'. Pass -TimberbornDir <game folder>."
}

if ($Clean) {
    # Stop the MSBuild/VBCSCompiler servers first: a lingering process can hold
    # Mono.Cecil.dll or a project DLL and make Remove-Item fail with Access Denied.
    & dotnet build-server shutdown 2>&1 | Out-Null
    Remove-Item -Recurse -Force $buildDir -ErrorAction SilentlyContinue
    Get-ChildItem $root -Recurse -Directory -Force |
        Where-Object { $_.Name -in @('obj', 'bin') } |
        ForEach-Object {
            try { Remove-Item -Recurse -Force $_.FullName -ErrorAction Stop }
            catch { Write-Warning "clean skipped (file in use): $($_.FullName)" }
        }
}

$projects = @(
    'src\MWP.Core\MWP.Core.csproj',
    'src\MWP.Gameplay\MWP.Gameplay.csproj',
    'src\MWP.Integration\MWP.Integration.csproj',
    'src\MWP.UI\MWP.UI.csproj'
)

foreach ($project in $projects) {
    Invoke-Dotnet @('build', (Join-Path $root $project), '-c', $Configuration, '-m:1', '--nologo', '-v', 'q',
        "-p:TimberbornDir=$TimberbornDir")
}

if (-not $SkipTests) {
    Invoke-Dotnet @('run', '--project', (Join-Path $root 'tests\MWP.Tests\MWP.Tests.csproj'),
        '-c', $Configuration, '-m:1', '--nologo')
}

# Data validation runs against the real game metadata (spec names, serialized fields, enum
# values, blueprint block counts, localization coverage, config schema).
Invoke-Dotnet @('run', '--project', (Join-Path $root 'tools\validate\validate.csproj'),
    '-c', $Configuration, '-m:1', '--nologo', '--', (Join-Path $root 'data'), '--managed', $managedDir)

# ---- stage a game-ready mod folder -------------------------------------------------------
if (Test-Path $stageDir) {
    Remove-Item -Recurse -Force $stageDir
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$assemblies = @('MWP.Core', 'MWP.Gameplay', 'MWP.Integration', 'MWP.UI')
foreach ($assembly in $assemblies) {
    $dll = Join-Path $root "src\$assembly\bin\$Configuration\netstandard2.1\$assembly.dll"
    if (-not (Test-Path $dll)) {
        throw "expected build output missing: $dll"
    }

    Copy-Item $dll $stageDir
}

# Data files: everything under data\ ships verbatim at the mod root (blueprints,
# localizations, manifest, config, icons). The game discovers blueprint JSON by walking
# the mod directory.
Copy-Item (Join-Path $root 'data\*') $stageDir -Recurse -Force

# ---- sanity checks on the staged package -------------------------------------------------
$required = @('manifest.json', 'MWP_Config.json', 'MWP.Core.dll', 'MWP.Gameplay.dll')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $stageDir $file))) {
        throw "staged package is missing $file"
    }
}

$manifest = Get-Content (Join-Path $stageDir 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($field in @('Name', 'Version', 'Id', 'MinimumGameVersion')) {
    if (-not $manifest.$field) {
        throw "manifest.json is missing required field $field"
    }
}

if ($manifest.Id -ne 'MechanicalWaterPump') {
    throw "manifest.json Id must be MechanicalWaterPump, found '$($manifest.Id)'"
}

$blueprints = Get-ChildItem $stageDir -Recurse -Filter '*.blueprint.json'
if ($blueprints.Count -lt 8) {
    throw "expected at least 8 blueprints (4 tiers x 2 factions), found $($blueprints.Count)"
}

$timbermeshes = Get-ChildItem $stageDir -Recurse -Filter '*.timbermesh'
if ($timbermeshes.Count -lt 16) {
    throw "expected at least 16 self-made .timbermesh models (8 finished + 8 construction), found $($timbermeshes.Count)"
}

foreach ($languageFile in @('enUS.csv', 'zhCN.csv', 'zhTW.csv')) {
    if (-not (Get-ChildItem $stageDir -Recurse -Filter $languageFile)) {
        throw "staged package is missing Localizations/$languageFile"
    }
}

$sizeBytes = (Get-ChildItem $stageDir -Recurse -File | Measure-Object -Sum Length).Sum
Write-Host ''
Write-Host ("Staged mod package: {0}" -f $stageDir) -ForegroundColor Green
Write-Host ("  assemblies : {0}" -f ($assemblies -join ', '))
Write-Host ("  blueprints : {0}" -f $blueprints.Count)
Write-Host ("  size       : {0:N0} KB" -f ($sizeBytes / 1KB))

# ---- optional install -------------------------------------------------------------------
if ($Install) {
    $modsRoot = Join-Path $env:USERPROFILE 'Documents\Timberborn\Mods'
    $target = Join-Path $modsRoot 'MechanicalWaterPump'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $stageDir '*') $target -Recurse -Force
    Write-Host "Installed to $target" -ForegroundColor Green
    Write-Host 'Enable it in Timberborn -> Mods, then restart the game.' -ForegroundColor Yellow
}
