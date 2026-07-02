<#
.SYNOPSIS
  Kill + build + relaunch the game, then spawn a custom battle and wait for deployment —
  the full "C# changed, get me back to a plannable mission" cycle in one command.

.DESCRIPTION
  Wraps dev-relaunch.ps1 (kill -> build -> launch -> dismiss safe-mode -> pin window) then
  drives the ModDebugKit file channel via tools\mdk.ps1 (send-and-await, JSON-checked):
  confirm a fresh menu pong, dbg.battle <preset> with retry while the engine view layer is
  still warming up (the ~15-18s cold-launch window), and poll until in-mission. Leaves the
  game at deployment with the RBP planner reachable via tools\map-iterate.ps1.

.PARAMETER Preset   ModDebugKit battle preset (default cav-clash).
.PARAMETER NoBuild  Pass through to dev-relaunch (skip the build).
#>
param(
  [string]$Preset = 'cav-clash',
  [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$mdk = Join-Path $PSScriptRoot 'mdk.ps1'

$relaunchArgs = @{}
if ($NoBuild) { $relaunchArgs['NoBuild'] = $true }
& (Join-Path $PSScriptRoot 'dev-relaunch.ps1') @relaunchArgs | Select-Object -Last 2

# Fresh menu pong (the channel truncates in.txt at load; retry until it answers).
$menu = $null
for ($r = 0; $r -lt 10 -and -not $menu; $r++) {
  try {
    $pong = & $mdk -TimeoutSec 6 -AllowFail dbg.ping
    if ($pong.ok -and $pong.msg -match 'menu') { $menu = $pong }
    elseif ($pong.ok) { Write-Host "channel answered but not at menu yet ($($pong.msg))"; Start-Sleep -Seconds 2 }
  } catch { Start-Sleep -Seconds 2 }
}
if (-not $menu) { throw 'channel never reported "at the menu" — is the game up?' }
Write-Host 'channel live at menu.'

# dbg.battle, retrying while the cold-launch view layer is still warming up.
# The guard returns a clean ok:false with a retryable error for that window.
$battle = $null
for ($r = 0; $r -lt 10 -and -not $battle; $r++) {
  $reply = & $mdk -TimeoutSec 20 -AllowFail dbg.battle $Preset
  if ($reply.ok) { $battle = $reply }
  elseif ("$($reply.error) $($reply.msg)" -match 'view layer|still loading|retry') {
    Write-Host "cold launch: view layer not ready, retrying ($($r + 1)/10)..."
    Start-Sleep -Seconds 3
  }
  else {
    $why = if ($reply.error) { $reply.error } else { $reply.msg }
    throw "dbg.battle failed: $why"
  }
}
if (-not $battle) { throw "battle '$Preset' never accepted (view layer never became ready)" }
Write-Host "battle '$Preset' loading; waiting for the mission..."

# Poll until a ping answers from inside the mission (fresh reply each time — no stale tail).
$inMission = $null
$deadline = (Get-Date).AddSeconds(90)
while (-not $inMission -and (Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 3
  try {
    $pong = & $mdk -TimeoutSec 8 -AllowFail dbg.ping
    if ($pong.ok -and $pong.msg -match 'mission') { $inMission = $pong }
  } catch { }
}
if ($inMission) { Write-Host 'MISSION UP (deployment).' }
else { Write-Host 'WARN: no in-mission pong within 90s; it may still be loading.' }
