<#
Generates WMP Tools Manager ribbon/window icons with System.Drawing only.
Purpose: reproducible, dependency-free icon generation matching Smart Export /
  File Naming Manager conventions.
Inputs: none. Outputs (written next to this script): tools-manager-logo.png
  (1280x1280 transparent source), tools-manager-32.png, tools-manager-16.png.
Dependencies: System.Drawing.Common via Add-Type (Windows, PowerShell 5.1, no installs).
Assumptions: run with `powershell -File generate-icons.ps1`; System.Drawing has no
  Lanczos resampler, so derivatives use HighQualityBicubic instead.
#>
Add-Type -AssemblyName System.Drawing
$outDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$size = 1280
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
$cobalt = [System.Drawing.Color]::FromArgb(255, 31, 78, 156)   # #1F4E9C
$amber  = [System.Drawing.Color]::FromArgb(255, 242, 169, 59)  # #F2A93B
$cx = 640; $cy = 640; $outerR = 460; $innerR = 280
$startDeg = 20; $sweepDeg = 290; $endDeg = $startDeg + $sweepDeg
# Circular-arrow ring: thick donut arc (update/refresh motif), cobalt blue.
$outerRect = New-Object System.Drawing.RectangleF ($cx - $outerR), ($cy - $outerR), ($outerR * 2), ($outerR * 2)
$innerRect = New-Object System.Drawing.RectangleF ($cx - $innerR), ($cy - $innerR), ($innerR * 2), ($innerR * 2)
$ring = New-Object System.Drawing.Drawing2D.GraphicsPath
$ring.AddArc($outerRect, $startDeg, $sweepDeg)
$ring.AddArc($innerRect, $endDeg, -$sweepDeg)
$ring.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush $cobalt), $ring)
# Amber arrowhead accent at the ring's leading tip, pointing against the sweep.
function Rad([double]$deg) { $deg * [Math]::PI / 180 }
$theta = Rad $startDeg
$outerTip = New-Object System.Drawing.PointF ($cx + 500 * [Math]::Cos($theta)), ($cy + 500 * [Math]::Sin($theta))
$innerTip = New-Object System.Drawing.PointF ($cx + 240 * [Math]::Cos($theta)), ($cy + 240 * [Math]::Sin($theta))
$midX = $cx + 370 * [Math]::Cos($theta); $midY = $cy + 370 * [Math]::Sin($theta)
$tanX = [Math]::Sin($theta); $tanY = -[Math]::Cos($theta)
$leadTip = New-Object System.Drawing.PointF ($midX + 260 * $tanX), ($midY + 260 * $tanY)
$arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
$arrow.AddPolygon(@($outerTip, $leadTip, $innerTip))
$g.FillPath((New-Object System.Drawing.SolidBrush $amber), $arrow)
$g.Dispose()
$bmp.Save("$outDir\tools-manager-logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
# Crop to visible alpha bounds, pad 8 percent, resize with high-quality bicubic.
function Export-Derivative([string]$path, [int]$target) {
    $src = New-Object System.Drawing.Bitmap "$outDir\tools-manager-logo.png"
    $rect = New-Object System.Drawing.Rectangle 0, 0, $src.Width, $src.Height
    $data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $src.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $src.UnlockBits($data)
    $minX = $src.Width; $minY = $src.Height; $maxX = 0; $maxY = 0
    for ($y = 0; $y -lt $src.Height; $y++) {
        $row = $y * $data.Stride
        for ($x = 0; $x -lt $src.Width; $x++) {
            if ($bytes[$row + $x * 4 + 3] -gt 0) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $w = $maxX - $minX + 1; $h = $maxY - $minY + 1
    $pad = [Math]::Round([Math]::Max($w, $h) * 0.08)
    $cropRect = New-Object System.Drawing.Rectangle ($minX - $pad), ($minY - $pad), ($w + $pad * 2), ($h + $pad * 2)
    $cropped = New-Object System.Drawing.Bitmap $src, $cropRect.Width, $cropRect.Height
    $cg = [System.Drawing.Graphics]::FromImage($cropped)
    $cg.Clear([System.Drawing.Color]::Transparent)
    $cg.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $cropRect.Width, $cropRect.Height), $cropRect, [System.Drawing.GraphicsUnit]::Pixel)
    $out = New-Object System.Drawing.Bitmap $target, $target
    $og = [System.Drawing.Graphics]::FromImage($out)
    $og.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $og.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $og.Clear([System.Drawing.Color]::Transparent)
    $og.DrawImage($cropped, 0, 0, $target, $target)
    $og.Dispose(); $cg.Dispose(); $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose(); $cropped.Dispose(); $src.Dispose()
}
Export-Derivative "$outDir\tools-manager-32.png" 32
Export-Derivative "$outDir\tools-manager-16.png" 16
