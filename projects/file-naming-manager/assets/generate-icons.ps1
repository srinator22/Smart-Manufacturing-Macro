<#
Generates File Naming Manager ribbon/window icons with System.Drawing only.
Purpose: reproducible, dependency-free icon generation matching Smart Export conventions.
Inputs: none. Outputs (written next to this script): file-naming-logo.png (1280x1280
  transparent source), file-naming-16.png, file-naming-32.png.
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
$offX = 190; $offY = 320
function Pt($x, $y) { New-Object System.Drawing.PointF (($x + $offX), ($y + $offY)) }
# Filing tag silhouette: pointed left edge, rectangular body, punch hole near the tip.
$tag = New-Object System.Drawing.Drawing2D.GraphicsPath
$tag.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
$tag.AddPolygon(@((Pt 0 320), (Pt 220 90), (Pt 850 90), (Pt 850 550), (Pt 220 550)))
$hole = Pt 95 265
$tag.AddEllipse($hole.X, $hole.Y, 110, 110)
$g.FillPath((New-Object System.Drawing.SolidBrush $cobalt), $tag)
# Amber index strip: three short bars suggesting numbering/ordering on the tag face.
function Bar($y) {
    $p = Pt 420 $y
    $r = New-Object System.Drawing.Rectangle ([int]$p.X), ([int]$p.Y), 340, 70
    $rad = 35
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc($r.X, $r.Y, $rad * 2, $rad * 2, 180, 90)
    $gp.AddArc($r.Right - $rad * 2, $r.Y, $rad * 2, $rad * 2, 270, 90)
    $gp.AddArc($r.Right - $rad * 2, $r.Bottom - $rad * 2, $rad * 2, $rad * 2, 0, 90)
    $gp.AddArc($r.X, $r.Bottom - $rad * 2, $rad * 2, $rad * 2, 90, 90)
    $gp.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $amber), $gp)
}
Bar 160; Bar 285; Bar 410
$g.Dispose()
$bmp.Save("$outDir\file-naming-logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
# Crop to visible alpha bounds, pad 8 percent, resize with high-quality bicubic.
function Export-Derivative([string]$path, [int]$target) {
    $src = New-Object System.Drawing.Bitmap "$outDir\file-naming-logo.png"
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
Export-Derivative "$outDir\file-naming-32.png" 32
Export-Derivative "$outDir\file-naming-16.png" 16
