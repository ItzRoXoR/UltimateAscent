<#
.SYNOPSIS
  Builds Ultimate Ascent, checks every Harmony patch against the installed game and packs the
  Thunderstore zip into artifacts/.
.EXAMPLE
  .\tools\build.ps1
  .\tools\build.ps1 -PeakDir 'D:\SteamLibrary\steamapps\common\PEAK'
#>
param(
    [string]$PeakDir = 'C:\Program Files (x86)\Steam\steamapps\common\PEAK',
    [string]$ProfileDir = "$env:APPDATA\r2modmanPlus-local\PEAK\profiles\FinalAscent"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\UltimateAscent\UltimateAscent.csproj'
$output = Join-Path $root 'src\UltimateAscent\bin\Release\netstandard2.1'

dotnet build $project -c Release "-p:PeakDir=$PeakDir" "-p:ProfileDir=$ProfileDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

dotnet run --project (Join-Path $root 'tools\PatchCheck\PatchCheck.csproj') -c Release -- `
    (Join-Path $output 'UltimateAscent.dll') (Join-Path $PeakDir 'PEAK_Data\Managed') (Join-Path $ProfileDir 'BepInEx\core')
if ($LASTEXITCODE -ne 0) { throw 'Patch check found problems.' }

$version = (Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json).version_number
$staging = Join-Path $root 'artifacts\package'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $staging 'plugins\UltimateAscent') | Out-Null
Copy-Item (Join-Path $output 'UltimateAscent.dll') (Join-Path $staging 'plugins\UltimateAscent')
foreach ($file in 'manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png') {
    Copy-Item (Join-Path $root $file) $staging
}
$zip = Join-Path $root "artifacts\UltimateAscent-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Write-Host "Packed $zip"
