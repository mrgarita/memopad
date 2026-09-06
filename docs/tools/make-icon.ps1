<#
.SYNOPSIS
  memopad のアプリ アイコン（src/memopad/memopad.ico）を System.Drawing で生成する。

.DESCRIPTION
  紺の角丸背景に黄色の罫線、左に赤・緑・水色の色見本を置いた図案。
  「色を変えられるメモ帳」を表す。16〜256px の 7 サイズを PNG 圧縮で 1 つの ICO にまとめる。
  画像編集ソフトを使わずに再生成できるよう、スクリプトで持つ。

.EXAMPLE
  .\docs\tools\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\..\src\memopad\memopad.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Frame {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    [int]$margin = [Math]::Max(1, [int][Math]::Round($Size * 0.06))
    [int]$radius = [Math]::Max(2, [int][Math]::Round($Size * 0.22))
    [int]$inner = $Size - $margin * 2

    # 角丸四角形の背景（紺）
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($margin, $margin, $radius, $radius, 180, 90)
    $path.AddArc($margin + $inner - $radius, $margin, $radius, $radius, 270, 90)
    $path.AddArc($margin + $inner - $radius, $margin + $inner - $radius, $radius, $radius, 0, 90)
    $path.AddArc($margin, $margin + $inner - $radius, $radius, $radius, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(0x1E, 0x3A, 0x8A))
    $g.FillPath($bg, $path)

    # 罫線（黄）
    [float]$penWidth = [Math]::Max(1.0, $Size * 0.055)
    $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(0xFF, 0xD5, 0x4F)), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    [float]$x1 = $Size * 0.34
    [float]$x2 = $Size * 0.80
    foreach ($f in 0.32, 0.50, 0.68) {
        [float]$y = $Size * $f
        $g.DrawLine($pen, $x1, $y, $x2, $y)
    }

    # 左端の色見本（赤・緑・水色）
    [float]$dot = [Math]::Max(2.0, $Size * 0.11)
    $colors = @(
        [System.Drawing.Color]::FromArgb(0xEF, 0x44, 0x44),
        [System.Drawing.Color]::FromArgb(0x22, 0xC5, 0x5E),
        [System.Drawing.Color]::FromArgb(0x38, 0xBD, 0xF8)
    )
    for ($i = 0; $i -lt 3; $i++) {
        $brush = New-Object System.Drawing.SolidBrush -ArgumentList $colors[$i]
        [float]$cy = $Size * (0.32 + 0.18 * $i)
        $g.FillEllipse($brush, [float]($Size * 0.20 - $dot / 2), [float]($cy - $dot / 2), $dot, $dot)
        $brush.Dispose()
    }

    $pen.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# 各サイズを PNG にして ICO コンテナに詰める
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $bmp = New-Frame -Size $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [void]$frames.Add(@{ Size = $s; Data = $ms.ToArray() })
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter -ArgumentList $out
$w.Write([uint16]0)                 # 予約
$w.Write([uint16]1)                 # 1 = ICO
$w.Write([uint16]$frames.Count)
[int]$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    [byte]$dim = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 256 は 0 と書く決まり
    $w.Write($dim); $w.Write($dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Data.Length); $w.Write([uint32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $w.Write($f.Data) }
$w.Flush()

$OutPath = [System.IO.Path]::GetFullPath($OutPath)
[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
Write-Host "書き出し: $OutPath ($($out.Length) bytes, $($frames.Count) サイズ)"
