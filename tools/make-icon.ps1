# Generates src/VeloxDM.App/Assets/velox.ico (original artwork: a down arrow over a speed bar).
# Usage: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\VeloxDM.App\Assets\velox.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = [single]($s - 1)
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 20, 184, 166)), ([System.Drawing.Color]::FromArgb(255, 37, 99, 235)), 45
    $g.FillPath($brush, $path)

    $white = [System.Drawing.Brushes]::White
    $cx = $s / 2.0
    $shaftW = $s * 0.16
    $g.FillRectangle($white, [single]($cx - $shaftW / 2), [single]($s * 0.18), [single]$shaftW, [single]($s * 0.34))
    $tri = @(
        (New-Object System.Drawing.PointF ([single]($s * 0.26)), ([single]($s * 0.46))),
        (New-Object System.Drawing.PointF ([single]($s * 0.74)), ([single]($s * 0.46))),
        (New-Object System.Drawing.PointF ([single]$cx), ([single]($s * 0.70)))
    )
    $g.FillPolygon($white, $tri)
    $g.FillRectangle($white, [single]($s * 0.22), [single]($s * 0.76), [single]($s * 0.56), [single]([Math]::Max(1, $s * 0.08)))

    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic DIB frame (32bpp BGRA, bottom-up) + AND mask, for maximum compatibility.
        $w2 = New-Object System.IO.BinaryWriter $ms
        $w2.Write([UInt32]40); $w2.Write([Int32]$s); $w2.Write([Int32]($s * 2))
        $w2.Write([UInt16]1); $w2.Write([UInt16]32); $w2.Write([UInt32]0)
        $w2.Write([UInt32]0); $w2.Write([Int32]0); $w2.Write([Int32]0); $w2.Write([UInt32]0); $w2.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $px = $bmp.GetPixel($x, $y)
                $w2.Write([byte]$px.B); $w2.Write([byte]$px.G); $w2.Write([byte]$px.R); $w2.Write([byte]$px.A)
            }
        }
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $w2.Write((New-Object byte[] ($maskStride * $s)))
        $w2.Flush()
    }
    $pngs += , $ms.ToArray()
    $g.Dispose(); $bmp.Dispose()
}

# ICO container with PNG-compressed images.
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Wrote $out"

