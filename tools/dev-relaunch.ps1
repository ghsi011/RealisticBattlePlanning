# One-call dev relaunch for the in-game UI loop. Replaces the manual
# kill -> build -> launch -> dismiss-safe-mode -> pin-window dance.
#
#   tools\dev-relaunch.ps1            # kill, rebuild+deploy, launch, ready the menu
#   tools\dev-relaunch.ps1 -NoBuild  # skip the build (just relaunch current deploy)
#
# Why it exists: force-killing the game (needed to unlock the DLL for redeploy)
# makes BLSE show a "Game shut down unexpectedly - enable safe mode?" dialog on
# the next boot. Driving that dialog with computer-use is slow and flaky (a
# transient text-input host keeps stealing focus). Instead we dismiss it
# directly with Win32 (find the dialog, BM_CLICK its "No" button), which is
# instant and reliable. We also pin the window to the primary monitor's
# top-left at a fixed size so screenshot/click coordinates stay stable across
# the multi-monitor setup.
param(
    [switch]$NoBuild,
    [int]$WidthPx = 1600,
    [int]$HeightPx = 900,
    [int]$TimeoutSec = 120
)

$ErrorActionPreference = 'Stop'
$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord'
$bin = Join-Path $GameDir 'bin\Win64_Shipping_Client'
$exe = Join-Path $bin 'Bannerlord.BLSE.Standalone.exe'
$proj = Join-Path $PSScriptRoot '..\src\RealisticBattlePlanning\RealisticBattlePlanning.csproj'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class DevWin {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int ht,bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
  delegate bool EnumProc(IntPtr h, IntPtr l);

  static List<IntPtr> Find(string needle) {
    var res = new List<IntPtr>();
    EnumWindows((h,l) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) res.Add(h);
      return true;
    }, IntPtr.Zero);
    return res;
  }
  static IntPtr ChildByText(IntPtr parent, string text) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h,l) => {
      var sb = new StringBuilder(64); GetWindowText(h, sb, 64);
      if (sb.ToString().Replace("&","").Trim().Equals(text, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  // Returns true if a "Safe Mode" dialog was found and its No button clicked.
  public static bool DismissSafeMode() {
    foreach (var w in Find("Safe Mode")) {
      var no = ChildByText(w, "No");
      if (no != IntPtr.Zero) { SendMessage(no, 0x00F5, IntPtr.Zero, IntPtr.Zero); return true; } // BM_CLICK
      SendMessage(w, 0x0111, (IntPtr)7, IntPtr.Zero); // WM_COMMAND IDNO fallback
      return true;
    }
    return false;
  }
}
"@

# 1. Kill any running instance (unlocks the DLL for redeploy). Also kill the
#    text-input host, which otherwise steals foreground from computer-use.
Get-Process -Name 'Bannerlord*','TaleWorlds*','TextInputHost' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2

# 2. Build + deploy (unless skipped).
if (-not $NoBuild) {
    Write-Output "Building..."
    & dotnet build $proj -c Debug -p:Platform=x64 --nologo -v m | Select-Object -Last 4
}

# 3. Launch with the launcher's selected module list (mod force-included).
$launcherData = Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml'
$xml = [xml](Get-Content $launcherData -Raw)
$mods = $xml.SelectNodes("//UserModData[IsSelected='true']/Id") | ForEach-Object { $_.InnerText }
if ($mods -notcontains 'RealisticBattlePlanning') { $mods = @($mods) + 'RealisticBattlePlanning' }
$modArg = '_MODULES_*' + ($mods -join '*') + '*_MODULES_'
Start-Process -FilePath $exe -ArgumentList '/singleplayer', $modArg -WorkingDirectory $bin
Write-Output "Launched. Watching for safe-mode dialog + menu..."

# 4. Poll: dismiss the safe-mode dialog the instant it appears, and pin the
#    window once the menu is up.
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$dismissed = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 800
    if (-not $dismissed -and [DevWin]::DismissSafeMode()) { $dismissed = $true; Write-Output "  safe-mode dialog dismissed" }
    $p = Get-Process -Name 'Bannerlord.BLSE.Standalone' -ErrorAction SilentlyContinue
    if (-not $p) { continue }
    if ($p.MainWindowTitle -match 'Singleplayer') {
        Get-Process -Name 'TextInputHost' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        $h = $p.MainWindowHandle
        [DevWin]::ShowWindow($h, 9) | Out-Null            # SW_RESTORE
        [DevWin]::MoveWindow($h, 0, 0, $WidthPx, $HeightPx, $true) | Out-Null
        [DevWin]::SetForegroundWindow($h) | Out-Null
        Write-Output "READY: menu up, window pinned at (0,0) ${WidthPx}x${HeightPx} (run tools\focus-game.ps1 before driving if needed)."
        exit 0
    }
}
Write-Output "TIMEOUT after ${TimeoutSec}s waiting for the menu."
exit 1
