# Cuts the perk cards + buttons out of the mockup JPG, removes the baked-in
# checkerboard, erases placeholder text, and writes PNGs into the mod's Assets.
# Usage: powershell -File cut_cards.ps1 [mockup.jpg]
param(
    [string]$Mockup = "$env:USERPROFILE\OneDrive\Documents\Peak Rogue like assets and mock ups\Perk Choices mock ups.jpg",
    [string]$OutDir = "$PSScriptRoot\..\src\PeakRoguelike\Assets"
)

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition (Get-Content "$PSScriptRoot\Cutter.cs" -Raw)

New-Item -ItemType Directory -Force $OutDir | Out-Null
[Cutter]::Load($Mockup)
# Card frame rectangles (outer border of the painted frame) - pockets outside these are background.
[Cutter]::RemoveBackground(@(95,95,445,610, 513,40,865,610, 943,40,1290,610))

# Placeholder text. Card centres are 418px apart; circle at (272,316) on the first card.
$offsets = @{ climber = 0; mountaineer = 418; explorer = 846 }
foreach ($k in $offsets.Keys) {
    $o = $offsets[$k]
    [Cutter]::EraseText(148 + $o, 98, 400 + $o, 136, 0, 0, 0, 140, 4, 2)                # banner title line 1 (on maroon)
    [Cutter]::EraseText(148 + $o, 134, 400 + $o, 172, 0, 0, 0, 140, 5, 3)               # line 2 (overlaps the teal edge)
    [Cutter]::FillDisk(272 + $o, 316, 69)                                              # circle placeholder
    [Cutter]::EraseText(203 + $o, 416, 339 + $o, 446, 0, 0, 0, 0, 0, 1)                # tier tab
    if ($k -eq 'explorer') {
        # the backpack overlaps the left of this panel - only erase glyphs there
        [Cutter]::EraseText(155 + $o, 476, 1012, 522, 0, 0, 0, 120, 3, 1)
        [Cutter]::EraseText(1010, 479, 390 + $o, 519, 0, 0, 0, 0, 0, 1)
    } else {
        [Cutter]::EraseText(155 + $o, 479, 390 + $o, 519, 0, 0, 0, 0, 0, 1)            # description
    }
}

[Cutter]::Save("$OutDir\card_climber.png",     0,   0, 479, 660, 2)
[Cutter]::Save("$OutDir\card_mountaineer.png", 480, 0, 889, 660, 2)
[Cutter]::Save("$OutDir\card_explorer.png",    890, 0, 1364, 660, 2)
[Cutter]::Save("$OutDir\button_back.png",      0, 670, 400, 767, 2)
[Cutter]::Save("$OutDir\button_confirm.png",   500, 670, 900, 767, 2)
