# Genera src\SackMix\Assets\sackmix.ico: un cuadrado redondeado con degradado neón
# (cian → violeta → rosa) y tres faders blancos de distinta altura. Varios tamaños en PNG.
#   pwsh tools\generar-icono.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$salida = Join-Path $PSScriptRoot '..\src\SackMix\Assets\sackmix.ico'

function Redondeado([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Png([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [float]($s * 0.04)
    $fondo = Redondeado $m $m ($s - 2 * $m) ($s - 2 * $m) ([float]($s * 0.26))
    $deg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 0, 229, 255)), ([System.Drawing.Color]::FromArgb(255, 255, 61, 127))
    $mezcla = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $mezcla.Colors = @([System.Drawing.Color]::FromArgb(255, 0, 229, 255), [System.Drawing.Color]::FromArgb(255, 139, 92, 255), [System.Drawing.Color]::FromArgb(255, 255, 61, 127))
    $mezcla.Positions = @([float]0, [float]0.55, [float]1)
    $deg.InterpolationColors = $mezcla
    $g.FillPath($deg, $fondo)

    # Tres faders: barras blancas con la base alineada, como en la cabecera de la app.
    $ancho = [float]([Math]::Max(1.5, $s * 0.11)); $hueco = [float]($s * 0.08)
    $alturas = @(0.30, 0.52, 0.38)
    $total = 3 * $ancho + 2 * $hueco
    $x = [float](($s - $total) / 2); $base = [float]($s * 0.74)
    $blanco = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    foreach ($a in $alturas) {
        $h = [float]($s * $a)
        $g.FillPath($blanco, (Redondeado $x ($base - $h) $ancho $h ([float]($ancho / 2))))
        $x += $ancho + $hueco
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$tamanos = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $tamanos) { , (Png $s) }

# Formato ICO: cabecera, una entrada por tamaño y luego las imágenes PNG.
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $s = $tamanos[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
New-Item -ItemType Directory -Force (Split-Path $salida) | Out-Null
[IO.File]::WriteAllBytes($salida, $ms.ToArray())
"Icono: $salida ($($ms.Length) bytes)"
