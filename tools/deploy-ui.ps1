# Deploy ONLY the Gauntlet UI data files (prefabs + brushes) into the running
# game's module — no C# build, no game kill. The DLL is locked while the game
# runs, but the GUI XML files are plain data and are not. After this, reopen the
# panel in-game (rbp.plan to close, rbp.plan to open) to pick up the change.
#
#   tools\deploy-ui.ps1
#
# Use this for XML/brush-only iterations to keep the game open between edits.
# C# (VM/Core) changes still require tools\dev-relaunch.ps1 (assembly reload
# needs a process restart).
$ErrorActionPreference = 'Stop'
$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord'
$src = Join-Path $PSScriptRoot '..\Module\GUI'
$dst = Join-Path $GameDir 'Modules\RealisticBattlePlanning\GUI'
Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
Write-Output "Deployed Module\GUI -> $dst"
Write-Output "Reopen the panel to apply: run 'rbp.plan' twice (close, then open)."
