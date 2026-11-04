<#
.SYNOPSIS
  In-game smoke test: builds the mod and tools/SmokeTest, installs both into an r2modman profile,
  starts PEAK through Steam with "--ua-smoketest", waits for the result and removes the test plugin
  again. The test plays a solo run on its own (title screen -> airport -> boarding pass -> ascent N
  on today's map -> jumps to later segments), checks the rules from inside the game and quits PEAK
  when it is done.
  The default ascent is 24: every rule but ascent 25's weather, whose storms and tornadoes push the
  test scout around; -Ascent 25 adds the weather checks. In the checks not about the weather, the storms stay calm
  and no tornadoes come (on any ascent), so they cannot push the test scout off a ledge.
  -Clean runs it in a throwaway profile (artifacts\smoketest\clean-profile) with nothing but BepInEx
  (copied from -Profile), Ultimate Ascent and the test plugin: no True Final Ascent, no other mods.
  True Final Ascent's Apocalypse mode is switched off for that session (in memory only), because its
  random weather can knock the test scout out; -KeepApocalypse keeps it and checks that TFA's events
  stay TFA's (at ascent 25: that the weather is left to TFA, instead of its storms). The test changes no
  Steam stats or achievements and saves no run. TFA's saved selection file is backed up before and put
  back afterwards in any case, and so are its badges; maps TFA caches for the test's climb are moved out
  of its folder into artifacts\smoketest\tfa-maps, and the cached maps TFA deletes to make room for them
  (it keeps 10) are put back.
  -WithTfa (with -Clean) adds True Final Ascent and the libraries it needs to the throwaway profile, copied
  from -Profile (which is only read). -Chain climbs TFA's FINAL ASCENT on -Ascent as that chain of its
  Builder (TFA's letters: S Shore, T Tropics, R Roots, A Alpine, M Mesa, C Caldera, K Kiln, G Gloom,
  Z Citadel, N Nadir; "*200" after one makes it 200%), set in memory only.
  -BuilderMaps (with -WithTfa) puts the Builder maps (*.map) of that folder into the throwaway profile, as the pack
  installs them; "-Only buildermap" climbs the first of them.
.EXAMPLE
  .\tools\smoketest.ps1 -Clean
  .\tools\smoketest.ps1 -Profile FinalAscent
  .\tools\smoketest.ps1 -Profile FinalAscent -Ascent 20 -SkipBuild -KeepApocalypse
  .\tools\smoketest.ps1 -Profile FinalAscent -Apocalypse -Only TFA   (Apocalypse on for the test: TFA's events)
  .\tools\smoketest.ps1 -Clean -Only chalk     (only the run steps whose names contain "chalk")
  .\tools\smoketest.ps1 -Biome V -Only Kiln    (on the next map with the Caldera and the Kiln)
  .\tools\smoketest.ps1 -Clean -WithTfa -Ascent 25 -Chain "S,A,K*200,N" -Only tfabuilder
#>
param(
    [string]$Profile = 'FinalAscent',
    [int]$Ascent = 24,
    [switch]$Clean,
    [switch]$SkipBuild,
    [switch]$KeepHarness,
    [switch]$KeepApocalypse,
    [switch]$Apocalypse,
    [string]$Only = '',
    [string]$Biome = '',
    [switch]$WithTfa,
    [string]$Chain = '',
    [string]$BuilderMaps = '',
    [int]$TimeoutSeconds = 960,
    [string]$ProfilesDir = "$env:APPDATA\r2modmanPlus-local\PEAK\profiles",
    [string]$SteamExe = 'C:\Program Files (x86)\Steam\steam.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceProfile = Join-Path $ProfilesDir $Profile
$out = Join-Path $root 'artifacts\smoketest'
$profileDir = if ($Clean) { Join-Path $out 'clean-profile' } else { $sourceProfile }
$plugins = Join-Path $profileDir 'BepInEx\plugins'
$preloader = Join-Path $profileDir 'BepInEx\core\BepInEx.Preloader.dll'
$resultFile = Join-Path $profileDir 'BepInEx\UltimateAscent.SmokeTest.txt'
$logFile = Join-Path $profileDir 'BepInEx\LogOutput.log'
$harnessDir = Join-Path $plugins 'UltimateAscent.SmokeTest'
$modDll = Join-Path $root 'src\UltimateAscent\bin\Release\netstandard2.1\UltimateAscent.dll'
if (-not (Test-Path (Join-Path $sourceProfile 'BepInEx\core\BepInEx.Preloader.dll'))) { throw "BepInEx not found in profile '$Profile'." }
if (Get-Process -Name PEAK -ErrorAction SilentlyContinue) { throw 'PEAK is already running; close it first.' }
if ($WithTfa -and -not $Clean) { throw '-WithTfa goes with -Clean (the profile itself already has its mods).' }
if ($Chain -and $Clean -and -not $WithTfa) { throw '-Chain needs True Final Ascent: add -WithTfa.' }

if (-not $SkipBuild) {
    dotnet build (Join-Path $root 'src\UltimateAscent\UltimateAscent.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Building the mod failed.' }
}
dotnet build (Join-Path $root 'tools\SmokeTest\SmokeTest.csproj') -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Building the smoke test failed.' }

if ($Clean) {
    # A fresh profile every time: BepInEx itself and Ultimate Ascent, nothing else.
    if (Test-Path $profileDir) { Remove-Item $profileDir -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $plugins 'UltimateAscent'), (Join-Path $profileDir 'BepInEx\config') | Out-Null
    Copy-Item (Join-Path $sourceProfile 'BepInEx\core') (Join-Path $profileDir 'BepInEx\core') -Recurse
    $bepinexCfg = Join-Path $sourceProfile 'BepInEx\config\BepInEx.cfg'
    if (Test-Path $bepinexCfg) { Copy-Item $bepinexCfg (Join-Path $profileDir 'BepInEx\config') }
    Copy-Item $modDll (Join-Path $plugins 'UltimateAscent')
    if ($WithTfa) {
        # True Final Ascent and the libraries it needs, copied from the source profile (only read there).
        foreach ($folder in 'CptPICHU-TrueFinalAscent', 'PEAKModding-PEAKLib_Items', 'PEAKModding-PEAKLib_Core', 'PEAKModding-SoftDependencyFix', 'MonoDetour-MonoDetour', 'MonoDetour-MonoDetour_BepInEx_5') {
            $from = Join-Path $sourceProfile "BepInEx\plugins\$folder"
            if (-not (Test-Path $from)) { throw "True Final Ascent needs '$folder', which profile '$Profile' does not have." }
            Copy-Item $from (Join-Path $plugins $folder) -Recurse
        }
        $patcher = Join-Path $sourceProfile 'BepInEx\patchers\MonoDetour-MonoDetour_BepInEx_5'
        if (Test-Path $patcher) {
            New-Item -ItemType Directory -Force (Join-Path $profileDir 'BepInEx\patchers') | Out-Null
            Copy-Item $patcher (Join-Path $profileDir 'BepInEx\patchers\MonoDetour-MonoDetour_BepInEx_5') -Recurse
        }
        Get-ChildItem (Join-Path $sourceProfile 'BepInEx\config') -Filter '*.cfg' | Where-Object { $_.Name -match 'truefinalascent' } |
            Copy-Item -Destination (Join-Path $profileDir 'BepInEx\config')
        if ($BuilderMaps) {
            # The Builder maps pack, as it installs: a BuilderMaps folder of its own under plugins.
            $pack = Join-Path $plugins 'UltimateAscent-BuilderMaps\BuilderMaps'
            New-Item -ItemType Directory -Force $pack | Out-Null
            Copy-Item (Join-Path $BuilderMaps '*.map') $pack
        }
    }
}
else {
    & (Join-Path $PSScriptRoot 'deploy.ps1') -Profile $Profile -ProfilesDir $ProfilesDir
}
New-Item -ItemType Directory -Force $harnessDir | Out-Null
Copy-Item (Join-Path $root 'tools\SmokeTest\bin\Release\netstandard2.1\UltimateAscent.SmokeTest.dll') $harnessDir -Force
Remove-Item $resultFile -ErrorAction SilentlyContinue
Remove-Item (Join-Path $profileDir 'BepInEx\UltimateAscent.SmokeTest.*.png') -ErrorAction SilentlyContinue

# True Final Ascent keeps its last selection (Apocalypse mode included) in this file; keep a copy.
$tfaSelection = Join-Path $env:LOCALAPPDATA 'LandCrab\PEAK\TrueFinalAscent\TrueFinalAscent.Auswahl.txt'
$tfaBackup = $null
if (Test-Path $tfaSelection) { $tfaBackup = [System.IO.File]::ReadAllBytes($tfaSelection) }
# TFA's badges, and the maps it caches (a Builder climb adds one): kept as they were.
$tfaFolder = Split-Path -Parent $tfaSelection
$tfaBadges = Join-Path $tfaFolder 'TFABadges.json'
$tfaBadgesBackup = if (Test-Path $tfaBadges) { [System.IO.File]::ReadAllBytes($tfaBadges) } else { $null }
$tfaMaps = Join-Path $tfaFolder 'Maps'
$tfaMapsBefore = if (Test-Path $tfaMaps) { @(Get-ChildItem $tfaMaps -File | ForEach-Object { $_.Name }) } else { @() }
# TFA keeps its 10 latest maps and deletes older ones when it caches a new one: a copy of all of them, put back after.
$tfaMapsBackup = Join-Path $out 'tfa-maps-backup'
if (Test-Path $tfaMapsBackup) { Remove-Item $tfaMapsBackup -Recurse -Force }
if ($tfaMapsBefore.Count -gt 0) {
    New-Item -ItemType Directory -Force $tfaMapsBackup | Out-Null
    Copy-Item (Join-Path $tfaMaps '*') $tfaMapsBackup
}
# Ultimate Ascent's own save (unlocked cosmetics, the ones worn, Blind Mode and Hot Mode), next to the game's: the test
# changes it; keep a copy.
$modSave = Join-Path $env:USERPROFILE 'AppData\LocalLow\LandCrab\PEAK\UltimateAscent\Save.txt'
$modSaveBackup = if (Test-Path $modSave) { [System.IO.File]::ReadAllBytes($modSave) } else { $null }

$arguments = @('-applaunch', '3527290', '--doorstop-enabled', 'true', '--doorstop-target-assembly', $preloader, '--ua-smoketest', "--ua-smoketest-ascent=$Ascent")
if ($Apocalypse) { $arguments += '--ua-smoketest-apocalypse' }
elseif (-not $KeepApocalypse) { $arguments += '--ua-smoketest-noapocalypse' }
if ($Only) { $arguments += "--ua-smoketest-only=$Only" }
if ($Biome) { $arguments += "--ua-smoketest-biome=$Biome" }
if ($Chain) { $arguments += "--ua-smoketest-chain=$Chain" }
$launched = Get-Date
Write-Host "Starting PEAK ($(if ($Clean) { if ($WithTfa) { 'clean profile with True Final Ascent' } else { 'clean profile' } } else { "profile $Profile" }), ascent $Ascent$(if ($Chain) { ", Builder chain $Chain" }))..."
Start-Process $SteamExe -ArgumentList $arguments

$deadline = $launched.AddSeconds($TimeoutSeconds)
$seenGame = $false
$done = $false
try {
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        $game = Get-Process -Name PEAK -ErrorAction SilentlyContinue
        if ($game) { $seenGame = $true }
        if ((Test-Path $resultFile) -and (Select-String -Path $resultFile -Pattern '^DONE' -Quiet)) { $done = $true }
        if ($done -and -not $game) { break }
        if ($seenGame -and -not $game) { break }
        if (-not $seenGame -and ((Get-Date) - $launched).TotalSeconds -gt 120) { throw 'PEAK did not start within 2 minutes.' }
    }
}
finally {
    $game = Get-Process -Name PEAK -ErrorAction SilentlyContinue
    if ($game) {
        Write-Warning 'PEAK is still running; closing it.'
        [void]$game.CloseMainWindow()
        if (-not $game.WaitForExit(20000)) { Stop-Process -Id $game.Id -Force }
    }
    if (-not $KeepHarness -and -not $Clean) { Remove-Item $harnessDir -Recurse -Force -ErrorAction SilentlyContinue }
    if ($null -ne $tfaBackup) {
        $now = if (Test-Path $tfaSelection) { [System.IO.File]::ReadAllBytes($tfaSelection) } else { $null }
        if ($null -eq $now -or [Convert]::ToBase64String($now) -ne [Convert]::ToBase64String($tfaBackup)) {
            [System.IO.File]::WriteAllBytes($tfaSelection, $tfaBackup)
            Write-Warning 'True Final Ascent rewrote its selection file during the test; the previous one was put back.'
        }
    }
    if ($null -ne $modSaveBackup) { [System.IO.File]::WriteAllBytes($modSave, $modSaveBackup) }
    elseif (Test-Path $modSave) { Remove-Item -LiteralPath $modSave }
    if ($null -ne $tfaBadgesBackup) {
        $now = if (Test-Path $tfaBadges) { [System.IO.File]::ReadAllBytes($tfaBadges) } else { $null }
        if ($null -eq $now -or [Convert]::ToBase64String($now) -ne [Convert]::ToBase64String($tfaBadgesBackup)) {
            [System.IO.File]::WriteAllBytes($tfaBadges, $tfaBadgesBackup)
            Write-Warning 'True Final Ascent changed its badges during the test; the previous ones were put back.'
        }
    }
    if (Test-Path $tfaMaps) {
        $newMaps = @(Get-ChildItem $tfaMaps -File | Where-Object { $tfaMapsBefore -notcontains $_.Name })
        if ($newMaps.Count -gt 0) {
            $kept = Join-Path $out 'tfa-maps'
            New-Item -ItemType Directory -Force $kept | Out-Null
            $newMaps | Move-Item -Destination $kept -Force
            Write-Host "Moved $($newMaps.Count) map(s) TFA cached for the test's climb to $kept."
        }
    }
    if (Test-Path $tfaMapsBackup) {
        $lost = @(Get-ChildItem $tfaMapsBackup -File | Where-Object { -not (Test-Path (Join-Path $tfaMaps $_.Name)) })
        if ($lost.Count -gt 0) {
            $lost | Copy-Item -Destination $tfaMaps
            Write-Warning "True Final Ascent removed $($lost.Count) of its cached map file(s) during the test; they were put back."
        }
        Remove-Item $tfaMapsBackup -Recurse -Force
    }
}

New-Item -ItemType Directory -Force $out | Out-Null
if (Test-Path $resultFile) { Copy-Item $resultFile $out -Force }
if (Test-Path $logFile) { Copy-Item $logFile (Join-Path $out 'LogOutput.log') -Force }
Copy-Item (Join-Path $profileDir 'BepInEx\UltimateAscent.SmokeTest.*.png') $out -Force -ErrorAction SilentlyContinue

if (-not (Test-Path $resultFile)) { throw 'The smoke test wrote no result (see artifacts\smoketest\LogOutput.log).' }
Get-Content $resultFile
$errors = Select-String -Path $logFile -Pattern '^\[(Error|Warning|Fatal)\s*:\s*Ultimate Ascent\]' -ErrorAction SilentlyContinue
if ($errors) {
    Write-Host ''
    Write-Host 'Errors and warnings logged by Ultimate Ascent:'
    $errors | ForEach-Object { $_.Line }
}
if (-not $done) { throw 'The smoke test did not finish.' }
if (Select-String -Path $resultFile -Pattern '^FAIL' -Quiet) { exit 1 }

