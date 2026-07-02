<#
.SYNOPSIS
  Send one ModDebugKit command over the file channel and await its JSON reply.

.DESCRIPTION
  Appends the line to io\in.txt, remembers out.jsonl's current line count, then
  scans only NEW lines for the reply whose "raw" matches what was sent. Prints
  the reply's msg and returns the parsed object ($reply.ok/.msg/.error/.data).
  Throws on timeout; throws on ok:false unless -AllowFail.

  This replaces every hand-rolled "Add-Content + sleep + Get-Content -Tail 1"
  loop: tail-1 races made scripts read stale or wrong replies (a FAILED
  dbg.battle refusal line still matched the 'dbg.battle' regex), which wedged
  respawn.ps1 on the exact cold-launch case it exists to handle. See
  docs/review-2026-07-02.md (T2/T6).

.EXAMPLE
  tools\mdk.ps1 dbg.ping
  tools\mdk.ps1 -TimeoutSec 60 dbg.battle cav-clash
  $r = tools\mdk.ps1 dbg.exec rbp.plan_status; $r.data
#>
param(
  [int]$TimeoutSec = 30,
  [switch]$AllowFail,
  [Parameter(Mandatory=$true, ValueFromRemainingArguments=$true)][string[]]$Command
)
$ErrorActionPreference = 'Stop'

$gameDir = $env:BANNERLORD_GAME_DIR
if (-not $gameDir -or -not (Test-Path $gameDir)) { $gameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord' }
$io  = Join-Path $gameDir 'Modules\ModDebugKit\Debug\io'
$in  = Join-Path $io 'in.txt'
$out = Join-Path $io 'out.jsonl'
if (-not (Test-Path $io)) { throw "ModDebugKit io dir not found: $io (is the module deployed?)" }

$line = ($Command -join ' ').Trim()

# Baseline: only reply lines appended AFTER we send are candidates.
$before = @(Get-Content $out -ErrorAction SilentlyContinue).Count
Add-Content -Path $in -Value $line

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 200
  $all = @(Get-Content $out -ErrorAction SilentlyContinue)
  if ($all.Count -eq 0) { continue }
  if ($all.Count -lt $before) { $before = 0 } # journal truncated (game relaunched)
  for ($i = $before; $i -lt $all.Count; $i++) {
    $obj = $null
    try { $obj = $all[$i] | ConvertFrom-Json } catch { continue }
    if ($obj.raw -ne $line) { continue }
    if ($obj.msg) { Write-Host "mdk: $($obj.msg)" }
    if (-not $obj.ok -and -not $AllowFail) {
      $why = if ($obj.error) { $obj.error } else { $obj.msg }
      throw "mdk: '$line' failed: $why"
    }
    return $obj
  }
}
throw "mdk: no reply to '$line' within ${TimeoutSec}s (game up? channel alive?)"
