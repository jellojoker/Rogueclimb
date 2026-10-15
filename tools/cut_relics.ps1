# Cuts the four amulet relic cards and the fusion card out of their mockups, erases the
# placeholder text, masks them to their rounded card shape and saves PNGs (+ round gem
# icons for the HUD) into the mod's Assets.
# Usage: powershell -File cut_relics.ps1
param(
    [string]$Art = "$env:USERPROFILE\OneDrive\Documents\Peak Rogue like assets and mock ups",
    [string]$OutDir = "$PSScriptRoot\..\src\PeakRoguelike\Assets"
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition (Get-Content "$PSScriptRoot\Cutter.cs" -Raw)
New-Item -ItemType Directory -Force $OutDir | Out-Null

# name, card outer border (x0,y0,x1,y1), title text, parchment panel interior, rarity text, gem centre + radius
$cards = @(
    @{ n = 'generosity'; b = 46, 76, 364, 593;    t = 81, 104, 329, 140;    p = 86, 433, 334, 544;    r = 156, 558, 256, 582;   g = 204, 261, 100 },
    @{ n = 'ambition';   b = 381, 151, 699, 715;  t = 421, 179, 661, 215;   p = 413, 545, 669, 661;   r = 486, 677, 596, 701;   g = 541, 376, 108 },  # yellow = Scout's Ambition (infinite stamina)
    @{ n = 'tenacity';   b = 717, 77, 1034, 590;  t = 757, 105, 997, 141;   p = 747, 430, 1005, 530;  r = 832, 553, 922, 577;   g = 879, 272, 104 },
    @{ n = 'initiative'; b = 1050, 76, 1368, 683; t = 1090, 104, 1330, 140; p = 1082, 517, 1338, 628; r = 1160, 646, 1260, 670; g = 1213, 316, 116 }   # red = Scout's Initiative (super jump)
)

foreach ($c in $cards) {
    [Cutter]::Load("$Art\Amulet relic cards.jpg")
    $g = $c.g
    [Cutter]::SaveDisk("$OutDir\gem_$($c.n).png", $g[0], $g[1], $g[2], 160)
    $t = $c.t; [Cutter]::EraseText($t[0], $t[1], $t[2], $t[3], 0, 0, 0, 120, 3, 3)  # cream title glyphs, row fill keeps the banner
    $p = $c.p; [Cutter]::FillPanel($p[0], $p[1], $p[2], $p[3])                       # whole parchment interior
    $r = $c.r; [Cutter]::EraseText($r[0], $r[1], $r[2], $r[3], 0, 0, 0, 0, 0, 3)    # rarity label
    $b = $c.b
    [Cutter]::RoundedMask($b[0], $b[1], $b[2], $b[3], 13, 0, 0)
    [Cutter]::Save("$OutDir\relic_$($c.n).png", $b[0], $b[1], $b[2], $b[3], 0)
}

[Cutter]::Load("$Art\Fusion Card.jpg")
[Cutter]::RoundedMask(468, 39, 939, 722, 18, 222, 7)
[Cutter]::Save("$OutDir\card_fusion.png", 468, 39, 939, 722, 0)
