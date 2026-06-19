<#
.SYNOPSIS
  One-command, keyboard-free visual iteration on the Planning Mode map.

.DESCRIPTION
  The in-game keyboard is unreliable when the game is driven over automation, so the
  planner can't be opened/closed with the toggle key. PlanningModeView instead polls a
  sentinel file (<module>\Debug\planner.cmd) for: open|close|toggle|reopen|shot <name>|
  reshot <name>. This script drives that sentinel and returns a PNG to Read.

  Typical loop for prefab/brush (XML) art edits (no relaunch, Gauntlet re-reads on reopen):
      edit Module/GUI/**  ->  tools\map-iterate.ps1 mapN          # deploy-ui + reshot + convert
  For C# changes you must relaunch first (dev-relaunch.ps1), then this still works.

.PARAMETER Name
  Screenshot base name (-> temp\<name>.png). Default: 'map'.

.PARAMETER Cmd
  Sentinel verb: reshot (default), reopen, open, close, toggle, shot.

.PARAMETER NoDeploy
  Skip the deploy-ui.ps1 hot-copy of Module/GUI (use when no XML changed).

.PARAMETER WaitMs
  Delay after the sentinel write before converting the BMP (screenshot lands next frame).
#>
param(
  [string]$Name = 'map',
  [ValidateSet('reshot','reopen','open','close','toggle','shot')]
  [string]$Cmd = 'reshot',
  [switch]$NoDeploy,
  [int]$WaitMs = 1600
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

# Resolve the deployed module dir (mirrors Directory.Build.props default / local.props).
$gameDir = $env:BANNERLORD_GAME_DIR
if (-not $gameDir -or -not (Test-Path $gameDir)) { $gameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord' }
$moduleDir = Join-Path $gameDir 'Modules\RealisticBattlePlanning'
if (-not (Test-Path $moduleDir)) { throw "module dir not found: $moduleDir" }

# 1) Hot-copy Module/GUI (XML/brush) into the running game unless skipped.
if (-not $NoDeploy -and $Cmd -ne 'shot' -and $Cmd -ne 'close') {
  & (Join-Path $PSScriptRoot 'deploy-ui.ps1') *> $null
}

# 2) Write the sentinel verb(s).
$dbgDir = Join-Path $moduleDir 'Debug'
New-Item -ItemType Directory -Force -Path $dbgDir | Out-Null
$cmdFile = Join-Path $dbgDir 'planner.cmd'
function Send-Verb([string]$v) {
  # Wait for any prior verb to be consumed (file emptied) so verbs don't clobber.
  $d = (Get-Date).AddSeconds(3)
  while ((Get-Date) -lt $d) {
    if (-not (Test-Path $cmdFile)) { break }
    $raw = Get-Content $cmdFile -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($raw)) { break }
    Start-Sleep -Milliseconds 100
  }
  Set-Content -Path $cmdFile -Value $v -Encoding utf8 -NoNewline
  Write-Host "sentinel <- '$v'"
}

if ($Cmd -in @('close','open','toggle','reopen')) {
  Send-Verb $Cmd
  Start-Sleep -Milliseconds 600
  Write-Host "done ($Cmd)."
  return
}

# reshot = reopen (so a hot-reloaded prefab/brush is re-read) THEN, after Gauntlet has
# built + rendered the panel (a few frames), shot. Doing both in one C# tick captured
# before the layer rendered, so the script orchestrates the gap explicitly.
if ($Cmd -eq 'reshot') {
  Send-Verb 'reopen'
  Start-Sleep -Milliseconds 1100   # let the movie build + render
  Send-Verb "shot $Name"
} else {
  Send-Verb "shot $Name"
}

# 3) Wait for the screenshot (engine writes it a frame or two after the sentinel is consumed).
Start-Sleep -Milliseconds $WaitMs
$bmp = Join-Path $moduleDir "Logs\Screenshots\$Name.bmp"
$png = Join-Path $repo "temp\$Name.png"
New-Item -ItemType Directory -Force -Path (Split-Path $png) | Out-Null

$deadline = (Get-Date).AddSeconds(6)
while (-not (Test-Path $bmp) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (-not (Test-Path $bmp)) { throw "screenshot not produced: $bmp (is the planner reachable? is the game in a plannable mission?)" }

Add-Type -AssemblyName System.Drawing
# Retry the load: the engine may still be flushing the BMP when we first touch it.
for ($i = 0; $i -lt 10; $i++) {
  try { $img = [System.Drawing.Image]::FromFile($bmp); break }
  catch { Start-Sleep -Milliseconds 250 }
}
$img.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$w = $img.Width; $h = $img.Height
$img.Dispose()
Write-Host "READ: $png ($w x $h)"
