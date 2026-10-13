<#
.SYNOPSIS
  Copies the built UltimateAscent.dll into an r2modman profile (BepInEx\plugins\UltimateAscent).
  Warns about More Ascents / 13DDA in that profile: they cannot run next to Ultimate Ascent.
.EXAMPLE
  .\tools\deploy.ps1 -Profile FinalAscent
#>
param(
    [Parameter(Mandatory = $true)][string]$Profile,
    [string]$ProfilesDir = "$env:APPDATA\r2modmanPlus-local\PEAK\profiles"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root 'src\UltimateAscent\bin\Release\netstandard2.1\UltimateAscent.dll'
if (-not (Test-Path $dll)) { throw "Build first: $dll not found." }
$plugins = Join-Path $ProfilesDir "$Profile\BepInEx\plugins"
if (-not (Test-Path $plugins)) { throw "Profile not found: $plugins" }

$conflicts = Get-ChildItem $plugins -Directory | Where-Object { $_.Name -match 'MoreAscents|Dastardly|PeakAscentsContinued' }
foreach ($conflict in $conflicts) {
    Write-Warning "$($conflict.Name) is installed in this profile; BepInEx will not load Ultimate Ascent next to it. Disable it in r2modman."
}
$target = Join-Path $plugins 'UltimateAscent'
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $dll $target -Force
Write-Host "Deployed to $target"
