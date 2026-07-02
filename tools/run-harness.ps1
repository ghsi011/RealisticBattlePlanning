# Relaunches Bannerlord (BLSE) with the mod reliably loaded, for the in-game
# harness loop, and waits until the mod confirms boot ("Engine contract
# verified" appears in rbp.log's NEW content).
#
# Launching is delegated to dev-relaunch.ps1 -NoBuild, which handles the whole
# unattended path: kill any running instance, launch with the launcher's module
# list (mod force-included), auto-dismiss BLSE's "Safe Mode?" dialog (a force-
# killed instance always triggers it), and pin the window. This script then
# verifies the MOD actually loaded, not just that a window exists.
#
# Usage:
#   tools\run-harness.ps1                 # kill, relaunch, wait for mod boot
#   tools\run-harness.ps1 -BootTimeoutSec 240
param(
    [string]$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [int]$BootTimeoutSec = 200,
    [switch]$NoKill   # deprecated: dev-relaunch always restarts; kept so old invocations don't break
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $GameDir 'Modules\RealisticBattlePlanning\Logs\rbp.log'
if ($NoKill) { Write-Output 'NOTE: -NoKill is deprecated; the relaunch path always restarts the game.' }

# Only log content appended AFTER this point counts as this boot's.
$logStart = if (Test-Path $log) { (Get-Item $log).Length } else { 0 }

& (Join-Path $PSScriptRoot 'dev-relaunch.ps1') -NoBuild | Select-Object -Last 2

Write-Output "Waiting up to $BootTimeoutSec s for the mod to load..."
$deadline = (Get-Date).AddSeconds($BootTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep 5
    if (-not (Test-Path $log)) { continue }
    $len = (Get-Item $log).Length
    if ($len -lt $logStart) { $logStart = 0 }  # log rotated/truncated by the new boot
    if ($len -le $logStart) { continue }
    $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        $fs.Seek($logStart, 'Begin') | Out-Null
        $new = (New-Object System.IO.StreamReader($fs)).ReadToEnd()
    } finally { $fs.Dispose() }
    if ($new -match 'Engine contract verified') {
        Write-Output 'MOD LOADED. Latest log:'
        Write-Output ((Get-Content $log -Tail 6) -join "`n")
        exit 0
    }
}
Write-Output "TIMEOUT: no new 'Engine contract verified' in rbp.log within $BootTimeoutSec s."
exit 1
