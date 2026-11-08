# Generuje ikonę aplikacji (Assets\AppIcon.ico + AppIcon.png) – rysowana w kodzie, bez zewnętrznych plików.
# Użycie:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\MapoMyslacz\Assets")
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Tło: zaokrąglony kwadrat z gradientem fiolet -> indygo
    $bg = New-RoundedPath (8 * $s) (8 * $s) (240 * $s) (240 * $s) (56 * $s)
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 142, 45, 226),
        [System.Drawing.Color]::FromArgb(255, 74, 0, 224), 45.0)
    $g.FillPath($brush, $bg)

    # Gałęzie
    $penW = [Math]::Max(1.5, 14 * $s)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 255, 255, 255), $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cx = 112 * $s; $cy = 132 * $s
    $ends = @(@(196, 70), @(204, 136), @(186, 200), @(48, 70))
    foreach ($e in $ends) {
        $ex = $e[0] * $s; $ey = $e[1] * $s
        $g.DrawBezier($pen, $cx, $cy, ($cx + $ex) / 2, $cy, ($cx + $ex) / 2, $ey, $ex, $ey)
    }

    # Węzły-liście
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $accents = @(
        [System.Drawing.Color]::FromArgb(255, 255, 196, 87),
        [System.Drawing.Color]::FromArgb(255, 110, 231, 183),
        [System.Drawing.Color]::FromArgb(255, 125, 211, 252),
        [System.Drawing.Color]::FromArgb(255, 255, 149, 149))
    for ($i = 0; $i -lt $ends.Count; $i++) {
        $ex = $ends[$i][0] * $s; $ey = $ends[$i][1] * $s
        $r = 22 * $s
        $node = New-RoundedPath ($ex - $r) ($ey - $r * 0.8) (2 * $r) (1.6 * $r) (0.8 * $r)
        $g.FillPath((New-Object System.Drawing.SolidBrush($accents[$i])), $node)
    }

    # Węzeł centralny
    $cr = 40 * $s
    $center = New-RoundedPath ($cx - $cr * 1.15) ($cy - $cr * 0.8) (2.3 * $cr) (1.6 * $cr) (0.8 * $cr)
    $g.FillPath($white, $center)
    $inner = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 110, 30, 220))
    $ir = 10 * $s
    $g.FillEllipse($inner, $cx - $ir, $cy - $ir, 2 * $ir, 2 * $ir)

    $g.Dispose()
    return $bmp
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir "AppIcon.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# Plik ICO z wpisami PNG (obsługiwane od Windows Vista)
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "AppIcon.ico"), $out.ToArray())
Write-Host "Zapisano ikonę do $OutDir"
