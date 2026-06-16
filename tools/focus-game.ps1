# Force the Bannerlord window to the foreground so computer-use can drive it.
# Call this right before a computer-use batch. The Windows Text Input Host
# (textinputhost.exe — touch keyboard / IME candidate host) intermittently
# becomes the z-order-frontmost window even when not visible, which makes
# computer-use refuse clicks ("Textinputhost is frontmost"). Killing it (it
# respawns on demand, harmless) and re-foregrounding the game clears that.
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class FocusGame {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
}
"@
Get-Process -Name 'TextInputHost' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$p = Get-Process -Name 'Bannerlord.BLSE.Standalone' -ErrorAction SilentlyContinue
if (-not $p) { "game not running"; exit 1 }
[FocusGame]::ShowWindow($p.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
[FocusGame]::BringWindowToTop($p.MainWindowHandle) | Out-Null
[FocusGame]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
$sb = New-Object System.Text.StringBuilder 256
[FocusGame]::GetWindowText([FocusGame]::GetForegroundWindow(), $sb, 256) | Out-Null
if ($sb.ToString() -match 'Bannerlord') { "OK: game is foreground" } else { "WARN: foreground is '$($sb.ToString())'" }
