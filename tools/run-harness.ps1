# Launches Bannerlord (BLSE) with the mod reliably loaded, for the in-game
# harness loop. BLSE.Standalone with NO module args drops our mod entirely
# (rbp.* -> "Unknown command"), so we pass the explicit ordered module list
# from the launcher config. Waits for the mod's "Engine contract verified"
# line in rbp.log to confirm it actually loaded.
#
# Usage:
#   tools\run-harness.ps1                 # kill any running instance, relaunch, wait for boot
#   tools\run-harness.ps1 -NoKill         # just launch (no running instance)
#   tools\run-harness.ps1 -BootTimeoutSec 240
#
# Note: force-killing a running instance makes the next boot show BLSE's
# "Safe Mode" dialog (click No). The auto-spawn clean-exit avoids that; for a
# manual restart, dismiss the dialog once.
param(
    [string]$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [int]$BootTimeoutSec = 200,
    [switch]$NoKill
)

$ErrorActionPreference = 'Stop'
$bin = Join-Path $GameDir 'bin\Win64_Shipping_Client'
$log = Join-Path $GameDir 'Modules\RealisticBattlePlanning\Logs\rbp.log'
$exe = Join-Path $bin 'Bannerlord.BLSE.Standalone.exe'

if (-not (Test-Path $exe)) { throw "BLSE Standalone not found at $exe" }

# Ordered module list: prefer the launcher config (the validated load order),
# but guarantee our mod is present.
$launcherData = Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml'
if (Test-Path $launcherData) {
    $mods = ([xml](Get-Content $launcherData -Raw)).SelectNodes('//UserModData/Id') | ForEach-Object { $_.InnerText }
} else {
    $mods = @('Bannerlord.Harmony','Bannerlord.ButterLib','Bannerlord.UIExtenderEx','Bannerlord.MBOptionScreen','Native','SandBoxCore','CustomBattle','Sandbox','StoryMode')
}
if ($mods -notcontains 'RealisticBattlePlanning') { $mods = @($mods) + 'RealisticBattlePlanning' }
$modArg = '_MODULES_*' + ($mods -join '*') + '*_MODULES_'

if (-not $NoKill) {
    $running = Get-Process -Name 'Bannerlord*' -ErrorAction SilentlyContinue
    if ($running) {
        $running | ForEach-Object { try { $_.CloseMainWindow() | Out-Null } catch {} }
        Start-Sleep 4
        Get-Process -Name 'Bannerlord*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep 2
    }
}

$before = if (Test-Path $log) { (Get-Item $log).LastWriteTime } else { [datetime]::MinValue }
Start-Process -FilePath $exe -ArgumentList '/singleplayer', $modArg -WorkingDirectory $bin
Write-Output "Launched BLSE Standalone with $($mods.Count) modules (RealisticBattlePlanning included)."
Write-Output "Waiting up to $BootTimeoutSec s for the mod to load..."

$deadline = (Get-Date).AddSeconds($BootTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep 5
    if (Test-Path $log) {
        $lw = (Get-Item $log).LastWriteTime
        if ($lw -gt $before) {
            $tail = Get-Content $log -Tail 6
            if ($tail -match 'Engine contract verified') {
                Write-Output "MOD LOADED. Latest log:"
                Write-Output ($tail -join "`n")
                exit 0
            }
        }
    }
}
Write-Output "TIMEOUT: no new 'Engine contract verified' in rbp.log within $BootTimeoutSec s."
exit 1
