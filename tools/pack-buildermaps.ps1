<#
.SYNOPSIS
  Packs the Builder maps (built with True Final Ascent's Builder, see the mod's README) into their own zip for the
  release, apart from the mod's: artifacts\UltimateAscent-BuilderMaps-<version>.zip. Its layout is a Thunderstore
  package (manifest.json, icon.png, README.md and the maps in BuilderMaps\), so r2modman can import it as a local mod;
  by hand, its BuilderMaps folder goes anywhere under BepInEx\plugins.
.EXAMPLE
  .\tools\pack-buildermaps.ps1 -Maps artifacts\mapgen\maps -Version 1.0.0
#>
param(
    [string]$Maps = '',
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Maps) { $Maps = Join-Path $root 'artifacts\mapgen\maps' }
$files = @(Get-ChildItem $Maps -Filter '*.map' | Where-Object { $_.Name -match '^\d+_' } | Sort-Object Name)
if ($files.Count -eq 0) { throw "No Builder maps in $Maps." }
$numbers = $files | ForEach-Object { [int]($_.Name -split '_')[0] }
$missing = @(1..($numbers | Measure-Object -Maximum).Maximum | Where-Object { $numbers -notcontains $_ })
if ($missing.Count -gt 0) { Write-Warning "Maps missing from the numbers: $($missing -join ', ')" }

$modVersion = (Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json).version_number
$staging = Join-Path $root 'artifacts\buildermaps-package'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $staging 'BuilderMaps') | Out-Null
$files | Copy-Item -Destination (Join-Path $staging 'BuilderMaps')
Copy-Item (Join-Path $root 'icon.png') $staging
$manifest = [ordered]@{
    name = 'UltimateAscent_BuilderMaps'
    version_number = $Version
    website_url = ''
    description = "$($files.Count) True Final Ascent Builder maps with the original maps' layout, for Ultimate Ascent's map picker."
    # None listed: r2modman would look them up on Thunderstore on import; the README says what the maps need.
    dependencies = @()
}
Write-Host "For Ultimate Ascent $modVersion and True Final Ascent 3.0.3."
# UTF-8 without a byte order mark (a manifest with one does not read as JSON everywhere).
$plain = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $staging 'manifest.json'), ($manifest | ConvertTo-Json), $plain)
$readme = @"
# Ultimate Ascent: Builder maps

$($files.Count) maps built with True Final Ascent's Builder, each with the original maps' layout: the Shore; the Tropics or
the Roots; the Alpine or the Mesa; the Caldera and the Kiln or the Gloom and the Citadel; then the Peak, and the Nadir
through the gem statue. The layouts and each biome's variants are spread evenly over the maps.

With this pack and True Final Ascent installed, Ultimate Ascent's map picker on the boarding pass lists them after the
original maps as "Builder: MAP 1" and on. A picked one plays as True Final Ascent's FINAL ASCENT on that map, on the
ascent the pass shows. Only the host needs the pack: True Final Ascent sends the map to the other players (they need
True Final Ascent).

Install: in r2modman, Settings > Import local mod, and pick this zip. By hand: put the BuilderMaps folder anywhere
under BepInEx\plugins.
"@
[System.IO.File]::WriteAllText((Join-Path $staging 'README.md'), $readme, $plain)
$zip = Join-Path $root "artifacts\UltimateAscent-BuilderMaps-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Entries with forward slashes (Windows PowerShell's Compress-Archive writes backslashes, which some tools refuse); the
# maps are compressed already, so they are stored.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $staging -Recurse -File) {
        $entry = $file.FullName.Substring($staging.Length + 1).Replace('\', '/')
        $level = if ($file.Extension -eq '.map') { [System.IO.Compression.CompressionLevel]::NoCompression } else { [System.IO.Compression.CompressionLevel]::Optimal }
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, $level)
    }
}
finally {
    $archive.Dispose()
}
Write-Host "Packed $($files.Count) maps into $zip ($([int]((Get-Item $zip).Length / 1MB)) MB)"
