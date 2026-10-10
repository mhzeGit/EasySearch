# Draws the EasySearch app icon and writes it as a multi-size .ico (PNG-compressed frames).
#   pwsh tools/make-icon.ps1
param([string]$Out = "$PSScriptRoot\..\src\EasySearch\Assets\app.ico")

Add-Type -AssemblyName System.Drawing

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $s = $size / 256.0

    # Rounded tile with a vertical gradient.
    $r = 58 * $s; $d = 2 * $r; $w = $size - 1
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, $d, $d, 180, 90)
    $tile.AddArc($w - $d, 0, $d, $d, 270, 90)
    $tile.AddArc($w - $d, $w - $d, $d, $d, 0, 90)
    $tile.AddArc(0, $w - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 74, 163, 255)), ([System.Drawing.Color]::FromArgb(255, 10, 92, 255)), 90
    $g.FillPath($fill, $tile)

    # Magnifier.
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single](22 * $s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawEllipse($pen, [single](62 * $s), [single](62 * $s), [single](100 * $s), [single](100 * $s))
    $g.DrawLine($pen, [single](150 * $s), [single](150 * $s), [single](192 * $s), [single](192 * $s))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }

$stream = [System.IO.File]::Create((New-Item -ItemType File -Force $Out).FullName)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose()
"Wrote $Out"
