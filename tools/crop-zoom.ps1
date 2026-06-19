<#
.SYNOPSIS  Crop the map-panel region from a full-screen capture and upscale it, so marker
           detail (glyphs, borders, enemy blocks, overlap) is legible when Read.
.PARAMETER Name   temp\<Name>.png in, temp\<Name>_zoom.png out.
.PARAMETER X,Y,W,H  Crop rectangle in the 1920x1080 render (defaults frame the map panel).
.PARAMETER Scale  Integer upscale factor (nearest-neighbour). Default 2.
#>
param(
  [Parameter(Mandatory=$true)][string]$Name,
  [int]$X = 590, [int]$Y = 150, [int]$W = 770, [int]$H = 340,
  [int]$Scale = 2
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$src  = Join-Path $repo "temp\$Name.png"
$out  = Join-Path $repo "temp\${Name}_zoom.png"
if (-not (Test-Path $src)) { throw "no such capture: $src" }

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($src)
[int]$ow = $W * $Scale
[int]$oh = $H * $Scale
$bmp = New-Object System.Drawing.Bitmap $ow, $oh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$dst = New-Object System.Drawing.Rectangle 0, 0, $ow, $oh
$g.DrawImage($img, $dst, $X, $Y, $W, $H, [System.Drawing.GraphicsUnit]::Pixel)
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $img.Dispose()
Write-Host "READ: $out (${ow}x${oh})"
