# Builds Thermalyn.ico at every size Windows asks for, from installer\logo-source.png.
#
# A single large PNG inside the .ico is not enough:
#  - the original icon carried one 256x256 entry only, which Windows downsampled itself for the
#    taskbar, hence the blurry result;
#  - the artwork is not centred in its canvas, so it is cropped to its bounding box first.
#
# The source mark is black and grey. Both the icon and the in-app artwork are recoloured with the
# two accent tones, so the taskbar, the title bar and the loading screen show the same blue logo.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Default accent, and its darker companion for the inner stroke. Kept in sync with App.xaml.
$accent = [System.Drawing.Color]::FromArgb(0x3B, 0x9E, 0xFF)
$accentDeep = [System.Drawing.Color]::FromArgb(0x1A, 0x5C, 0x9E)

# Splits the mark by luminance and paints each half with its own tone. Alpha is preserved, so the
# anti-aliased edges survive every later resample. Mid-grey belongs with the outer chevron; only
# the near-black stroke is treated as the inner line.
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
            # Rec. 601 luma on the straight colour, matching Color.GetBrightness closely enough here.
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
$targets = @(
    (Join-Path $root 'Thermalyn\Assets\Thermalyn.ico'),
    (Join-Path $root 'installer\Thermalyn-setup.ico')
)

if (-not (Test-Path $source)) { throw "Logo not found: $source" }
$logo = New-Object System.Drawing.Bitmap $source

# Bounding box of the artwork: everything that is not fully transparent.
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

# Square the box so the drawing is not stretched, then add a margin.
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
    # The comma stops PowerShell from unrolling the array on the way out of the function.
    return ,$body
}

$entries = @()
foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128) {
    $bmp = New-Artwork $size
    $entries += [pscustomobject]@{ Size = $size; Data = (Get-Dib $bmp) }
    $bmp.Dispose()
}
# 256 is stored as PNG: the same image as a bitmap would weigh 256 KB on its own.
$big = New-Artwork 256
$ms = New-Object IO.MemoryStream
$big.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$entries += [pscustomobject]@{ Size = 256; Data = $ms.ToArray() }
$ms.Dispose(); $big.Dispose()
$logo.Dispose()

foreach ($target in $targets) {
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
}

# --- In-app artwork ---------------------------------------------------------
# The window shows the logo tinted with the current accent, in two tones. A single image
# cannot carry two tints, so the mark is split by luminance: the outer chevron on one layer,
# the inner stroke on the other. Each layer is then used as an OpacityMask in XAML.
# The layers are split at the source resolution, then cropped to the same bounding box as the
# icon and resampled to 512. Writing them uncropped at source size left the mark occupying a
# fraction of the canvas, so a 14 px title-bar mask ended up carrying only a handful of pixels.
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
        # The masks are tinted by the running accent in XAML, so only the split matters here; the
        # alpha is what the OpacityMask reads. Recolouring already separated the two tones.
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
