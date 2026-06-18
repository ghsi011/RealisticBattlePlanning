---
name: moddebugkit
description: >-
  Drive ModDebugKit to debug a Bannerlord (Mount & Blade II) mod from OUTSIDE the
  game — set up an exact battle or campaign state, make something happen, and read
  precisely what each formation/unit did through the filesystem (no mouse, no
  screenshots). Use when debugging RealisticBattlePlanning or any Bannerlord mod's
  in-mission or campaign behavior: reproducing a battle bug, checking what a
  formation actually does vs its plan, capturing death/order telemetry, snapshotting
  battle state, deterministically pausing/stepping a battle, freezing a side,
  taking clean screenshots, or driving the campaign map (load save, gold, party,
  time) programmatically. Triggers: "debug the battle", "what did formation N do",
  "why didn't the cavalry move", "snapshot the battle", "reproduce the move bug",
  "step through the battle", "drive the campaign".
---

# ModDebugKit — driving the kit

ModDebugKit is a standalone Bannerlord module (lives in this repo next to
RealisticBattlePlanning; never depends on it). It exists so an agent can debug
mods **fast and without the mouse**: put the game into an exact state, make
something happen, and read exactly what happened — through files.

**One idea drives everything: the file command channel.** You append a `dbg.*`
command line to a watched input file; the kit runs it on the game's main thread
and appends a structured JSON result to an output file. You drive with the shell
(append) and read with the Read tool. The in-game console is only a secondary
front-end.

Full IO schemas: [docs/mod-debug-kit-io.md](../../../docs/mod-debug-kit-io.md).
Design + lessons: [docs/mod-debug-kit-design.md](../../../docs/mod-debug-kit-design.md).

## Paths

Output root (this dev machine): `C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ModDebugKit\Debug\`
(In general: `<BannerlordGameDir>\Modules\ModDebugKit\Debug\`, or wherever the
`MODDEBUGKIT_OUT` env var points if set.)

| file | role |
|------|------|
| `io/in.txt`  | **you append** command lines here (one `dbg.* args` per line) |
| `io/out.jsonl` | **you read** — one JSON result object per executed command |
| `battle_state.json` | last `dbg.snapshot` (the full battlefield) |
| `telemetry.jsonl` | flight recorder: phases, deaths, order changes (+ nav-mesh verdict) |
| `errors.jsonl` | captured faults + stack; first fault auto-writes `error_snapshot.json` |
| `campaign_state.json` | last `dbg.camp.status` |
| `shots/<name>.bmp` + `.json` | `dbg.shot` screenshot + state sidecar |
| `presets/<name>.json`, `scripts/<name>.json` | preset / script libraries |

## Launch the game with the kit

From the repo root (PowerShell):

1. **Build the whole solution** (not just RBP) so ModDebugKit deploys; kill the
   game first if it's running (a loaded DLL is locked):
   `dotnet build RealisticBattlePlanning.sln -c Debug -p:Platform=x64`
2. **Relaunch**: `tools\dev-relaunch.ps1 -NoBuild` — kills → launches with the
   launcher's module list → dismisses the BLSE safe-mode dialog → pins the window
   → returns when the menu is up (~60s). ModDebugKit must be enabled in
   `LauncherData.xml` (it is on this machine; the relaunch reads the list).
3. Fresh worktrees need a `local.props` pointing `BannerlordGameDir` at
   `C:\games\Steam\...` (the `BANNERLORD_GAME_DIR` env var is stale) or the build
   fails. See the `worktree-build-setup` memory.

## The drive loop

```powershell
$io = "C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ModDebugKit\Debug\io"
# 1. confirm the channel is up BEFORE driving (see gotchas): append dbg.ping, read the pong
Add-Content "$io\in.txt" 'dbg.ping'
# 2. append a command, wait ~0.5s, read the new out.jsonl line(s)
Add-Content "$io\in.txt" 'dbg.snapshot'
```
Then `Read` the JSON it points at (`battle_state.json`), or read the new
`out.jsonl` tail. `out.jsonl` grows append-only across sessions; track a line
count to find new results.

The headline, mouse-free loop — **set up a battle, run it, report what each
formation did**:
```
dbg.battle            # launch the default Empire-vs-Aserai field battle (or: dbg.battle <preset>)
dbg.ready             # finish deployment, start fighting (no Ready click)
dbg.snapshot          # -> battle_state.json
dbg.leave             # back to the menu   (or dbg.restart to re-run the same preset)
```

## Command reference

**Liveness / channel:** `dbg.ping` · `dbg.help` (lists every command)

**Snapshot** — `dbg.snapshot [path] [all|player|enemy]` → `battle_state.json` (filter
the teams; default all). Per formation:
`number` (1–8), `slotClass`, `representativeClass`, **`composition` {counts+label}**,
`count`, `position`, `facing`, `order` {`type`, `moveTarget`, **`targetHasNavMeshFace`**},
`captain`, `casualtiesPercent`, `broken`. **Always trust `composition`, not the slot
name** — a formation's slot ≠ its contents.

**Battle factory:**
- `dbg.battle [preset]` — launch a custom field battle. No arg = default. Preset =
  `presets/<name>.json` or a path. Refuses if already in a mission.
- `dbg.ready` · `dbg.leave` · `dbg.restart`
- `dbg.assign <all|inf|ranged|cav|ha> <1-8>` — move the player's matching units into a formation
- `dbg.layout inf=1 ranged=3 cav=5 ha=7` — several assigns at once
- assign/layout work **any time**: during deployment they queue and auto-apply at
  `dbg.ready` (the engine auto-sort reverts an immediate move; the kit handles the
  timing), and casualties resolve correctly against the laid-out slots.

**Time-series** — `dbg.track <seconds> [interval=1] [all|player|enemy]` → `track.jsonl`
(append-only, truncated per run): samples each formation every interval for the window
(`t`, `n`, `side`, `comp`, `order`, `x`, `y`, `cas`). One command captures a whole
maneuver (move→flank→charge, an orbit) instead of a manual wait+snapshot loop.

**Flight recorder:** `dbg.telemetry [on|off|clear|status]` → `telemetry.jsonl`
(phases, `agent_removed` with killer, `order` events with the nav-mesh verdict).
`dbg.errors [on|off|clear|status]` → `errors.jsonl`.

**Determinism:** `dbg.pause` · `dbg.resume` · `dbg.step [seconds]` (advance then
re-pause) · `dbg.timescale <x>` (0=pause, <1 slow, >1 fast-forward) ·
`dbg.freeze <enemy|all|player|none>` (reliable for the player side; the enemy
general AI keeps re-commanding — use `dbg.pause` for a hard freeze of everything).

**Capture:** `dbg.shot [name]` → `shots/<name>.bmp` + state sidecar. Convert to a
readable PNG with `tools\mdk-shot.ps1 <name>`, then Read the PNG.

**HUD:** `dbg.hud <message>` — show a line on the in-game debug channel.

**Scripting:** `dbg.run <name|path>` — run a timed scenario from `scripts/<name>.json`
(steps fire by elapsed seconds, spanning menu→battle→result). `dbg.stop`.

**Campaign** (file-channel only; load is read-only, mutations are in-memory —
never writes a save):
- `dbg.camp.saves` · `dbg.camp.load [save]` (no arg = newest = Continue)
- `dbg.camp.status [path]` → `campaign_state.json` (day, gold, position, party)
- `dbg.camp.gold <n|+n|-n>` · `dbg.camp.party <troopId> <count>` · `dbg.camp.time <stop|play|fast>`

## Recipes

- **What did formation N actually do?** `dbg.battle` → `dbg.ready` → wait →
  `dbg.snapshot` → Read `battle_state.json`. Compare `composition` to the slot,
  read `order` + `targetHasNavMeshFace`.
- **Why didn't a formation move? (the nav-mesh bug class)** Watch `telemetry.jsonl`
  for that formation's `order` events — a Move with `targetHasNavMeshFace:false`
  is silently ignored by the engine. (`scripts/move-watch.json` packages this.)
- **Slot ≠ contents demo:** `dbg.run formation-mismatch` → Read `dogfood-mismatch.json`.
- **Deterministic inspection:** `dbg.pause` → `dbg.snapshot a.json` →
  `dbg.step 2` → `dbg.snapshot b.json` → diff. Positions are frozen while paused.
- **Reproduce an exact roster/layout:** write a `presets/<name>.json` (per-class
  `counts` or exact `troops:[{troop,count}]`), `dbg.battle <name>`, then
  `dbg.assign`/`dbg.layout` to place units, then `dbg.ready`.
- **Record a whole scenario:** author `scripts/<name>.json` (`[{at, do}]`) and
  `dbg.run <name>` — hands-free.
- **Campaign:** `dbg.camp.load` → `dbg.camp.status` → `dbg.camp.gold +50000` etc.
- **See it:** `dbg.shot` → `tools\mdk-shot.ps1` → Read the PNG.

## Gotchas (hard-won)

- **Wait for `dbg.ping` to respond before driving.** The channel truncates `in.txt`
  on load; commands appended during the load window are lost. Append `dbg.ping`,
  read the pong from `out.jsonl`, THEN issue real commands.
- **Append, don't overwrite** `in.txt`. PowerShell `Add-Content` is fine (the
  parser strips its UTF-8 BOM).
- **C# changes need a relaunch** (kill → build solution → `dev-relaunch -NoBuild`);
  a loaded DLL can't be hot-swapped and the deploy copy fails while the game holds
  it. Only `Module/`/scenario edits can be redeployed without a relaunch.
- **Launching right after the menu appears can transiently NRE** in the engine's
  screen push — let the menu settle a few seconds (and confirm `dbg.ping`) before
  `dbg.battle`/`dbg.camp.load`.
- **`dbg.freeze enemy` is best-effort** (the enemy general AI re-commands);
  `dbg.pause` is the hard freeze.
- **Close the game when debugging is finished** (keep it open between iterations
  for speed; kill it before the next C# build).
- Driving the in-game console instead needs computer-use, and the game window's
  process (`bannerlord.blse.standalone.exe`) must be granted explicitly — prefer
  the file channel, which needs none of that.
