# Builds the Thunderstore zip from a saved build: dist\Rogue_Climb-<version>.zip
# Usage:  powershell -File package.ps1 0.10.7
param([Parameter(Mandatory = $true)][string]$Version)

Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$dll = "$root\backups\v$Version\PeakRoguelike.dll"
$plug = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\PEAK\profiles\Rogue like mod\BepInEx\plugins\PeakRoguelike"
$song = "$plug\It's In The Fog.mp3"
$art = "$env:USERPROFILE\OneDrive\Documents\Peak Rogue like assets and mock ups\Thunderstore mod image.jpg"
foreach ($f in $dll, $song, $art) { if (-not (Test-Path -LiteralPath $f)) { Write-Host "Missing: $f"; exit 1 } }

$stage = "$root\dist\Rogue_Climb-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\plugins\PeakRoguelike" | Out-Null

$manifest = [ordered]@{
    name           = "Rogue_Climb"
    version_number = $Version
    website_url    = "https://github.com/jellojoker/rogueclimb"
    description    = "A roguelike for PEAK: crate picks, item trees that evolve and fuse, relics, coloured suns and blood moons. Early test build."
    dependencies   = @("BepInEx-BepInExPack_PEAK-5.4.75301")
}
[IO.File]::WriteAllText("$stage\manifest.json", ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

# 256x256 icon from the mod art
$src = [Drawing.Image]::FromFile($art)
$icon = New-Object Drawing.Bitmap 256, 256
$g = [Drawing.Graphics]::FromImage($icon)
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($src, 0, 0, 256, 256)
$icon.Save("$stage\icon.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $icon.Dispose(); $src.Dispose()

Copy-Item "$root\README.md", "$root\CHANGELOG.md", "$root\LICENSE" $stage
Copy-Item $dll "$stage\plugins\PeakRoguelike\"
Copy-Item -LiteralPath $song "$stage\plugins\PeakRoguelike\"

$zip = "$root\dist\Rogue_Climb-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Not Compress-Archive: PowerShell 5.1 writes "\" into entry paths, which Thunderstore rejects.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Host "Packaged $zip"
