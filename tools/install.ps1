# Installs a saved build of the mod into the "Rogue like mod" profile (close PEAK first).
# Usage:  powershell -File install.ps1            -> newest build in backups\
#         powershell -File install.ps1 v0.3.0     -> roll back to that build
param([string]$Version = "")

$root = Split-Path $PSScriptRoot -Parent
$plug = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\PEAK\profiles\Rogue like mod\BepInEx\plugins\PeakRoguelike"

if ($Version -eq "") {
    $Version = (Get-ChildItem "$root\backups" -Directory | Sort-Object { [version]($_.Name.TrimStart('v')) } | Select-Object -Last 1).Name
}
$src = "$root\backups\$Version\PeakRoguelike.dll"
if (-not (Test-Path $src)) { Write-Host "No saved build $Version. Available:"; Get-ChildItem "$root\backups" -Directory | ForEach-Object { "  " + $_.Name }; exit 1 }

try {
    Copy-Item $src "$plug\PeakRoguelike.dll" -Force -ErrorAction Stop
    Write-Host "Installed PEAK Roguelike $Version."
} catch {
    Write-Host "Couldn't install - is PEAK still running? Close it and try again."
    exit 1
}
