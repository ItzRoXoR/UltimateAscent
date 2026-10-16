<#
.SYNOPSIS
  In-game smoke test: builds the mod and tools/SmokeTest, installs both into an r2modman profile,
  starts PEAK through Steam with "--ua-smoketest", waits for the result and removes the test plugin
  again. The test plays a solo run on its own (title screen -> airport -> boarding pass -> ascent N
  on today's map -> jumps to later segments), checks the rules from inside the game and quits PEAK
  when it is done.
  The default ascent is 24: every rule but ascent 25's weather, whose storms and tornadoes push the
  test scout around; -Ascent 25 adds the weather checks.
  -Clean runs it in a throwaway profile (artifacts\smoketest\clean-profile) with nothing but BepInEx
  (copied from -Profile), Ultimate Ascent and the test plugin: no True Final Ascent, no other mods.
  True Final Ascent's Apocalypse mode is switched off for that session (in memory only), because its
  random weather can knock the test scout out; -KeepApocalypse keeps it and checks that TFA's events
  stay TFA's (at ascent 25: that the weather is left to TFA, instead of its storms). The test changes no
  Steam stats or achievements and saves no run. TFA's saved selection file is backed up before and put
  back afterwards in any case.
.EXAMPLE
  .\tools\smoketest.ps1 -Clean
  .\tools\smoketest.ps1 -Profile FinalAscent
  .\tools\smoketest.ps1 -Profile FinalAscent -Ascent 20 -SkipBuild -KeepApocalypse
  .\tools\smoketest.ps1 -Profile FinalAscent -Apocalypse -Only TFA   (Apocalypse on for the test: TFA's events)
  .\tools\smoketest.ps1 -Clean -Only chalk     (only the run steps whose names contain "chalk")
  .\tools\smoketest.ps1 -Biome K -Only Kiln    (on the next map with the Caldera and the Kiln)
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

$arguments = @('-applaunch', '3527290', '--doorstop-enabled', 'true', '--doorstop-target-assembly', $preloader, '--ua-smoketest', "--ua-smoketest-ascent=$Ascent")
if ($Apocalypse) { $arguments += '--ua-smoketest-apocalypse' }
elseif (-not $KeepApocalypse) { $arguments += '--ua-smoketest-noapocalypse' }
if ($Only) { $arguments += "--ua-smoketest-only=$Only" }
if ($Biome) { $arguments += "--ua-smoketest-biome=$Biome" }
$launched = Get-Date
Write-Host "Starting PEAK ($(if ($Clean) { 'clean profile' } else { "profile $Profile" }), ascent $Ascent)..."
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

