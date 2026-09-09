$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$accent = [System.Drawing.Color]::FromArgb(0x3B, 0x9E, 0xFF)
$accentDeep = [System.Drawing.Color]::FromArgb(0x1A, 0x5C, 0x9E)

function Set-AccentColors([System.Drawing.Bitmap]$bmp, [System.Drawing.Color]$light, [System.Drawing.Color]$dark) {
    $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $bmp.Width, $bmp.Height
    $bits = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $line = New-Object byte[] ($bmp.Width * 4)
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $scan = [IntPtr]($bits.Scan0.ToInt64() + $y * $bits.Stride)
        [Runtime.InteropServices.Marshal]::Copy($scan, $line, 0, $line.Length)
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $i = $x * 4
            if ($line[$i + 3] -eq 0) { continue }
            $luma = (0.299 * $line[$i + 2] + 0.587 * $line[$i + 1] + 0.114 * $line[$i]) / 255
            $c = if ($luma -gt 0.35) { $light } else { $dark }
            $line[$i] = $c.B; $line[$i + 1] = $c.G; $line[$i + 2] = $c.R
        }
        [Runtime.InteropServices.Marshal]::Copy($line, 0, $scan, $line.Length)
    }
    $bmp.UnlockBits($bits)
}

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'installer\logo-source.png'
$target = Join-Path $root 'Thermalyn\Assets\Thermalyn.ico'

if (-not (Test-Path $source)) { throw "Logo not found: $source" }
$logo = New-Object System.Drawing.Bitmap $source

$data = $logo.LockBits(
    (New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $logo.Width, $logo.Height),
    [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$row = New-Object byte[] ($logo.Width * 4)
$minX = $logo.Width; $minY = $logo.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $logo.Height; $y++) {
    [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
    for ($x = 0; $x -lt $logo.Width; $x++) {
        if ($row[$x * 4 + 3] -gt 8) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$logo.UnlockBits($data)
if ($maxX -lt 0) { throw 'The logo contains no opaque pixel.' }

$w = $maxX - $minX + 1
$h = $maxY - $minY + 1
$side = [Math]::Max($w, $h)
$cx = $minX + $w / 2.0
$cy = $minY + $h / 2.0
$box = $side * 1.08
$srcRect = New-Object System.Drawing.RectangleF -ArgumentList ([single]($cx - $box / 2)), ([single]($cy - $box / 2)), ([single]$box), ([single]$box)
Write-Output ("artwork {0}x{1} at ({2},{3})" -f $w, $h, $minX, $minY)

Set-AccentColors $logo $accent $accentDeep

function New-Artwork([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'AntiAlias'
    $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $dest = New-Object System.Drawing.RectangleF -ArgumentList ([single]0), ([single]0), ([single]$size), ([single]$size)
    $g.DrawImage($logo, $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

function Get-Dib([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $andStride = [Math]::Ceiling($size / 32) * 4
    $xorBytes = $size * $size * 4
    $body = New-Object byte[] (40 + $xorBytes + $andStride * $size)
    [BitConverter]::GetBytes([int]40).CopyTo($body, 0)
    [BitConverter]::GetBytes([int]$size).CopyTo($body, 4)
    [BitConverter]::GetBytes([int]($size * 2)).CopyTo($body, 8)
    [BitConverter]::GetBytes([int16]1).CopyTo($body, 12)
    [BitConverter]::GetBytes([int16]32).CopyTo($body, 14)
    [BitConverter]::GetBytes([int]($xorBytes + $andStride * $size)).CopyTo($body, 20)

    $bits = $bmp.LockBits((New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $size, $size),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $line = New-Object byte[] ($size * 4)
    $offset = 40
    for ($y = $size - 1; $y -ge 0; $y--) {
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]($bits.Scan0.ToInt64() + $y * $bits.Stride), $line, 0, $line.Length)
        [Array]::Copy($line, 0, $body, $offset, $line.Length)
        $offset += $line.Length
    }
    $bmp.UnlockBits($bits)
    # The comma stops PowerShell unrolling the array on return.
    return ,$body
}

$entries = @()
foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128) {
    $bmp = New-Artwork $size
    $entries += [pscustomobject]@{ Size = $size; Data = (Get-Dib $bmp) }
    $bmp.Dispose()
}
# 256 goes in as PNG. The same image as a bitmap would weigh 256 KB.
$big = New-Artwork 256
$ms = New-Object IO.MemoryStream
$big.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$entries += [pscustomobject]@{ Size = 256; Data = $ms.ToArray() }
$ms.Dispose(); $big.Dispose()
$logo.Dispose()

$stream = [IO.File]::Create($target)
$writer = New-Object IO.BinaryWriter($stream)
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$e.Data.Length)
    $writer.Write([int]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $writer.Write($e.Data) }
$writer.Flush(); $writer.Close(); $stream.Dispose()
Write-Output "written: $target"

$assets = Join-Path $root 'Thermalyn\Assets'
$logo = New-Object System.Drawing.Bitmap $source
Set-AccentColors $logo $accent $accentDeep
$layers = @{
    LogoOuter = New-Object System.Drawing.Bitmap($logo.Width, $logo.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    LogoInner = New-Object System.Drawing.Bitmap($logo.Width, $logo.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}
for ($y = 0; $y -lt $logo.Height; $y++) {
    for ($x = 0; $x -lt $logo.Width; $x++) {
        $c = $logo.GetPixel($x, $y)
        if ($c.A -lt 8) { continue }
        if ($c.R -eq $accent.R -and $c.G -eq $accent.G) { $layers.LogoOuter.SetPixel($x, $y, $c) }
        else { $layers.LogoInner.SetPixel($x, $y, $c) }
    }
}
foreach ($name in $layers.Keys) {
    $scaled = New-Object System.Drawing.Bitmap(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'AntiAlias'
    $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $dest = New-Object System.Drawing.RectangleF -ArgumentList ([single]0), ([single]0), ([single]512), ([single]512)
    $g.DrawImage($layers[$name], $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $scaled.Save((Join-Path $assets "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $scaled.Dispose()
    $layers[$name].Dispose()
}
$logo.Dispose()
Write-Output 'written: Assets\LogoOuter.png + Assets\LogoInner.png'
