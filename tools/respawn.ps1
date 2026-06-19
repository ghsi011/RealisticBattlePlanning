<#
.SYNOPSIS
  Kill + build + relaunch the game, then spawn a custom battle and wait for deployment —
  the full "C# changed, get me back to a plannable mission" cycle in one command.

.DESCRIPTION
  Wraps dev-relaunch.ps1 (kill -> build -> launch -> dismiss safe-mode -> pin window) then
  drives the ModDebugKit file channel: confirm a fresh menu pong (avoids the load-window
  truncation race), dbg.battle <preset>, and poll until in-mission. Leaves the game at
  deployment with the RBP planner reachable via tools\map-iterate.ps1.

.PARAMETER Preset   ModDebugKit battle preset (default cav-clash).
.PARAMETER NoBuild  Pass through to dev-relaunch (skip the build).
#>
param(
  [string]$Preset = 'cav-clash',
  [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$io  = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ModDebugKit\Debug\io'
$out = "$io\out.jsonl"

$relaunchArgs = @{}
if ($NoBuild) { $relaunchArgs['NoBuild'] = $true }
& (Join-Path $PSScriptRoot 'dev-relaunch.ps1') @relaunchArgs | Select-Object -Last 2

function Wait-Line([string]$pattern, [int]$tries, [int]$ms) {
  for ($i = 0; $i -lt $tries; $i++) {
    Start-Sleep -Milliseconds $ms
    $l = Get-Content $out -Tail 1 -ErrorAction SilentlyContinue
    if ($l -match $pattern) { return $l }
  }
  return $null
}

# Fresh menu pong (channel truncates in.txt at load; wait until it answers).
$menu = $null
for ($r = 0; $r -lt 6 -and -not $menu; $r++) {
  Add-Content "$io\in.txt" 'dbg.ping'
  $menu = Wait-Line 'pong.*menu' 12 500
}
if (-not $menu) { throw 'channel never reported "at the menu" — is the game up + focused?' }
Write-Host 'channel live at menu.'

Add-Content "$io\in.txt" "dbg.battle $Preset"
if (-not (Wait-Line 'dbg.battle' 40 1000)) { throw "battle '$Preset' did not load" }
Write-Host "battle '$Preset' loading; waiting for deployment..."
Start-Sleep -Seconds 16
Add-Content "$io\in.txt" 'dbg.ping'
if (Wait-Line 'in mission' 20 500) { Write-Host 'MISSION UP (deployment).' }
else { Write-Host 'WARN: no in-mission pong yet; it may still be loading.' }
