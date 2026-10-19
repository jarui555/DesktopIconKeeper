# 生成程序图标 build\app.ico（多尺寸 PNG 帧打包的 ICO，Vista+ 支持）
# 不依赖任何第三方库，只用 System.Drawing。

param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\build\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([double]$x, [double]$y, [double]$w, [double]$h, [double]$r)

    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2.0
    if ($d -le 0.1) { $d = 0.1 }
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # 底板：蓝色渐变圆角方块
    $radius = [double]$Size * 0.22
    $path = New-RoundedPath -x 0 -y 0 -w ([double]$Size) -h ([double]$Size) -r $radius
    $rect = New-Object System.Drawing.RectangleF(0, 0, [single]$Size, [single]$Size)
    $c1 = [System.Drawing.Color]::FromArgb(255, 46, 104, 232)
    $c2 = [System.Drawing.Color]::FromArgb(255, 22, 48, 140)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [single]60.0)
    $g.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()

    # 2x2 图标方块
    $padD = [double]$Size * 0.20
    $innerD = [double]$Size - 2.0 * $padD
    $gapD = $innerD * 0.14
    $cellD = ($innerD - $gapD) / 2.0
    $crD = $cellD * 0.24

    $x0 = $padD
    $x1 = $padD + $cellD + $gapD
    $y0 = $padD
    $y1 = $padD + $cellD + $gapD

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 196, 64))

    $cells = New-Object System.Collections.ArrayList
    [void]$cells.Add(@{ x = $x0; y = $y0; accent = $false })
    [void]$cells.Add(@{ x = $x1; y = $y0; accent = $true })
    [void]$cells.Add(@{ x = $x0; y = $y1; accent = $false })
    [void]$cells.Add(@{ x = $x1; y = $y1; accent = $false })

    foreach ($c in $cells) {
        $p = New-RoundedPath -x $c.x -y $c.y -w $cellD -h $cellD -r $crD
        if ($c.accent) { $g.FillPath($accent, $p) } else { $g.FillPath($white, $p) }
        $p.Dispose()
    }

    $white.Dispose()
    $accent.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()

    return [byte[]]$bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    [void]$frames.Add((New-IconPng -Size $s))
}

$outDir = Split-Path -Parent $OutFile
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = [byte[]]$frames[$i]
    $dim = 0
    if ($s -lt 256) { $dim = $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}

foreach ($fr in $frames) { $bw.Write([byte[]]$fr) }

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output ("icon written: {0} ({1} bytes, {2} frames)" -f (Resolve-Path $OutFile).Path, (Get-Item $OutFile).Length, $sizes.Count)
