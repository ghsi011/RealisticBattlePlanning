# Converts an rbp.screenshot capture (the engine writes BMP) to a PNG the
# dev/test loop can read. Pass a name, or omit to convert the newest capture.
#   tools\view-screenshot.ps1 panel      -> Screenshots\panel.png
#   tools\view-screenshot.ps1            -> newest *.bmp -> .png
param(
    [string]$Name = '',
    [string]$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $GameDir 'Modules\RealisticBattlePlanning\Logs\Screenshots'

if ($Name) {
    $bmp = Join-Path $dir ("{0}.bmp" -f $Name)
} else {
    $latest = Get-ChildItem (Join-Path $dir '*.bmp') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { Write-Error "no .bmp captures in $dir"; return }
    $bmp = $latest.FullName
}

if (-not (Test-Path $bmp)) { Write-Error "not found: $bmp"; return }

Add-Type -AssemblyName System.Drawing
$png = [System.IO.Path]::ChangeExtension($bmp, '.png')
$img = [System.Drawing.Image]::FromFile($bmp)
try { $img.Save($png, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $img.Dispose() }
Write-Output $png
