# Composites the cut PNGs onto a flat colour (optionally zoomed) to check edges and erased text.
param([string]$Out = "$env:TEMP\card_preview.png", [float]$Zoom = 1)
Add-Type -AssemblyName System.Drawing
$d = "$PSScriptRoot\..\src\PeakRoguelike\Assets"
$canvas = [System.Drawing.Bitmap]::new([int](1300 * $Zoom), [int](720 * $Zoom))
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::FromArgb(40, 160, 90))
$g.InterpolationMode = 'NearestNeighbor'
$place = @(@('card_climber', 10, 10), @('card_mountaineer', 440, 10), @('card_explorer', 870, 10), @('button_back', 40, 640), @('button_confirm', 500, 640))
foreach ($p in $place) {
    $i = [System.Drawing.Image]::FromFile("$d\$($p[0]).png")
    $g.DrawImage($i, [int]($p[1] * $Zoom), [int]($p[2] * $Zoom), [int]($i.Width * $Zoom), [int]($i.Height * $Zoom))
    $i.Dispose()
}
$canvas.Save($Out); $g.Dispose(); $canvas.Dispose()
