# Generuje kursory „łapek” (otwarta dłoń i zaciśnięta) -> src/MapoMyslacz/Assets/Cursors.dll
# Windows nie ma takich kursorów wbudowanych, a WinUI 3 wczytuje własne kursory tylko z zasobów modułu
# (InputDesktopResourceCursor), więc pakujemy je do małej biblioteki z samymi zasobami (bez kodu).
# Wymaga Windows SDK (rc.exe) i narzędzi C++ (link.exe) — tych samych, co Native AOT.
# Uruchom w Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-cursors.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$assets = Join-Path $PSScriptRoot '..\src\MapoMyslacz\Assets'
$work = Join-Path ([IO.Path]::GetTempPath()) "mapomyslacz-cursors"
New-Item -ItemType Directory -Force $work | Out-Null

# --- rysowanie w układzie 32 × 32 (skalowane do 32/48/64 px) ---------------------------------------------
function New-Capsule([float]$x, [float]$y, [float]$w, [float]$h) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Min($w, $h)
    $p.AddArc($x, $y, $r, $r, 180, 180)
    $p.AddArc($x, $y + $h - $r, $r, $r, 0, 180)
    $p.CloseFigure()
    return $p
}

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Rotated([System.Drawing.Drawing2D.GraphicsPath]$p, [float]$deg, [float]$cx, [float]$cy) {
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.RotateAt($deg, (New-Object System.Drawing.PointF $cx, $cy))
    $p.Transform($m)
    return $p
}

# kształty dłoni: palce (kapsułki) + kciuk + śródręcze; najpierw czarny obrys wszystkich, potem białe wypełnienie,
# więc wewnętrzne linie znikają, a zostaje obrys całości i szczeliny między palcami
function Get-HandShapes([bool]$grab) {
    $s = @()
    if (-not $grab) {
        $s += New-Capsule 8.2 6.5 3.6 11      # wskazujący
        $s += New-Capsule 12.0 4.5 3.6 13     # środkowy
        $s += New-Capsule 15.8 5.5 3.6 12     # serdeczny
        $s += New-Capsule 19.6 8.0 3.4 10     # mały
        $s += Rotated (New-Capsule 4.0 13.5 3.6 9) -42 5.8 18   # kciuk
        $s += New-RoundRect 8.0 13.0 15.0 14.0 4.5               # śródręcze
    }
    else {
        $s += New-Capsule 8.6 10.0 3.6 6      # zgięte palce (kostki)
        $s += New-Capsule 12.2 9.0 3.6 7
        $s += New-Capsule 15.8 9.5 3.6 6.5
        $s += New-Capsule 19.4 11.0 3.4 5.5
        $s += Rotated (New-Capsule 5.5 14.0 3.6 7) -30 7.3 17.5  # kciuk przy dłoni
        $s += New-RoundRect 8.0 12.5 15.0 13.5 4.5
    }
    return $s
}

function Draw-Hand([int]$size, [bool]$grab) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 32.0
    $g.ScaleTransform($k, $k)
    $shapes = Get-HandShapes $grab
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), (2.2 / [Math]::Max(1, $k * 0.75))
    $pen.LineJoin = 'Round'
    foreach ($p in $shapes) { $g.DrawPath($pen, $p) }
    foreach ($p in $shapes) { $g.FillPath([System.Drawing.Brushes]::White, $p) }
    $g.Dispose()
    return $bmp
}

# --- zapis .cur (wiele rozmiarów, 32 bpp DIB z kanałem alfa) --------------------------------------------
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $h)))   # maska AND: zera (przezroczystość z kanału alfa)
    $bw.Flush()
    return ,$ms.ToArray()   # przecinek: PowerShell nie rozwija tablicy bajtów na pojedyncze elementy
}

function Save-Cursor([string]$path, [bool]$grab, [int[]]$sizes) {
    $images = foreach ($s in $sizes) { ,(Get-DibBytes (Draw-Hand $s $grab)) }
    $fs = [IO.File]::Create($path)
    $bw = New-Object IO.BinaryWriter $fs
    $bw.Write([int16]0); $bw.Write([int16]2); $bw.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]($s / 2)); $bw.Write([int16]($s / 2))   # punkt aktywny: środek dłoni
        $bw.Write([int]$images[$i].Length); $bw.Write([int]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $bw.Write([byte[]]$img) }
    $bw.Close()
}

$sizes = 32, 48, 64
Save-Cursor (Join-Path $work 'hand_open.cur') $false $sizes
Save-Cursor (Join-Path $work 'hand_grab.cur') $true $sizes
(Draw-Hand 64 $false).Save((Join-Path $work 'hand_open.png'))
(Draw-Hand 64 $true).Save((Join-Path $work 'hand_grab.png'))

# --- biblioteka z zasobami: 1 = otwarta dłoń, 2 = zaciśnięta ---------------------------------------------
Set-Content -Path (Join-Path $work 'cursors.rc') -Encoding ASCII -Value @(
    '1 CURSOR "hand_open.cur"',
    '2 CURSOR "hand_grab.cur"'
)
$rc = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter rc.exe |
      Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
if (-not $rc) { throw 'Nie znaleziono rc.exe (Windows SDK).' }
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$link = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Recurse -Filter link.exe |
        Where-Object { $_.FullName -match 'Hostx64\\x64' } | Sort-Object FullName | Select-Object -Last 1
if (-not $link) { throw 'Nie znaleziono link.exe (narzędzia C++).' }

Push-Location $work
try {
    & $rc.FullName /nologo /fo cursors.res cursors.rc
    if ($LASTEXITCODE -ne 0) { throw 'rc.exe zakończył się błędem.' }
    & $link.FullName /NOLOGO /DLL /NOENTRY /MACHINE:X64 /OUT:Cursors.dll cursors.res
    if ($LASTEXITCODE -ne 0) { throw 'link.exe zakończył się błędem.' }
}
finally { Pop-Location }

Copy-Item (Join-Path $work 'Cursors.dll') (Join-Path $assets 'Cursors.dll') -Force
Write-Host "Zapisano $(Join-Path $assets 'Cursors.dll') (podgląd: $work\hand_open.png, hand_grab.png)"

