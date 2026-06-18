# ModDebugKit — design

A standalone Bannerlord module that makes **debugging other mods** (and the base
game) fast, deterministic, and — crucially — **drivable and observable from
outside the game window**. Born from the pain of developing RealisticBattlePlanning
(RBP) with an AI agent: every loop was bottlenecked on launching the game,
clicking through menus, pixel-hunting a console, and squinting at screenshots to
guess what the units actually did.

It lives in this repo as a sibling module but **does not depend on RBP** — it is a
general-purpose tool that any mod (including RBP) can be debugged with.

---

## North star

> An agent or human should be able to put the game into an exact state, make
> something happen, and read precisely what happened — **through the filesystem**,
> without touching the mouse or reading pixels — in seconds, repeatably.

Three properties fall out of that:

1. **File-first I/O.** Every command can be issued by writing a line to a watched
   file; every observation is available as JSON/JSONL at a known path. The screen
   becomes optional, not the interface. *This is the single highest-leverage idea
   in the whole design* — it removes computer-use (slow, flaky pixel clicking)
   from the inner loop almost entirely.
2. **Determinism.** Seeds, pause/step, AI freeze, fixed rosters and layouts, fixed
   scene — so a bug reproduces on demand and a fix is provably a fix.
3. **Legibility.** When you *do* look, an on-screen overlay and clean,
   game-only screenshots (no desktop wallpaper) make the picture unambiguous.

---

## Why each capability exists (the RBP pain it kills)

| Pain we actually hit building RBP | What ModDebugKit does about it |
|---|---|
| Launch is a slow kill→build→deploy→launch→dismiss-safe-mode→pin-window dance; menus need mouse clicks. | Boot-skip: auto-advance splash/menus and drop straight into a target (custom battle preset or campaign save) from a launch arg or command file. |
| Getting into a battle needs menu + gating conversation + deployment + a "Ready" click found by drifting pixel coords. | `dbg.battle <preset>` builds and auto-readies a configured battle; conversation/menu auto-skip; no mouse. |
| Reading battle state from screenshots is slow and ambiguous — we literally **moved the wrong formation** because we trusted log labels over what units did. | `dbg.snapshot` dumps every formation (number, class, **live composition**, count, position, facing, current order + move target, captain, casualties %, broken) to JSON the agent reads directly. |
| The move-order **nav-mesh bug** was invisible: formations silently ignored move orders for ages. | Order logging records each order's resolved `WorldPosition` **and whether it has a valid nav-mesh face**; a gizmo draws the target; `dbg.navtest x,y` probes pathability. The bug would have been a one-liner. |
| **Formation ≠ class** confusion: troops sort into slots by a default, so slot name ≠ contents. | Programmatic formation assignment (`dbg.assign`) to reproduce ANY layout deterministically, plus a live-composition readout. (Per the user's correction: deployment assignment is controllable — so control it.) |
| Battles vary run-to-run; can't isolate the unit under test. | Seed control, `dbg.freeze enemy|all`, pause/step, time-scale. |
| Driving the in-game console via Alt+grave + typed pixels is slow and fragile. | File command channel: write a line, it executes, the result is written back as JSON. |
| Campaign-map navigation and encounters are all mouse clicks. | API commands: goto / encounter / force-battle / set party / add troops / set time / gold. |
| Mod exceptions get swallowed; crashes leave no trail. | Exception capture to `errors.jsonl` with full stack traces; a state dump on crash. |
| "Show me what happened" means manual screenshots after the fact. | On-demand clean screenshots to known paths with a state sidecar; burst capture on interval/events; frame-sequence recording assembled to video by a tool. |

---

## Capability areas

Commands use the `dbg.` console namespace. **Every** command is also reachable via
the file channel (below), so "console" and "file" are two front-ends to one
dispatcher.

### 1. Command channel & scripting (the spine — build first)
- **File channel.** A behavior watches `…/ModDebugKit/io/in.txt`. Each appended
  line is parsed as `namespace.command arg1 arg2 …` and dispatched on the main
  thread; the result (string + structured payload) is appended to
  `…/io/out.jsonl` with the originating line and a tick/time stamp. This is how an
  agent drives the game with the `Write` tool and reads results with `Read`.
- **Console parity.** The same dispatcher backs `[CommandLineFunctionality]`
  console commands, so everything works from Alt+grave too.
- **Script runner.** `dbg.run <script.json>` executes a timed/event-triggered
  sequence: `[{ "at": "T+5s", "do": "dbg.charge 3" }, { "on": "battle_start",
  "do": "dbg.snapshot" }, …]`. This generalizes RBP's harness into a mod-agnostic
  scenario runner.

### 2. Boot & menu skip
- Launch arg / config to auto-enter: splash → (custom battle preset | load save)
  → into the mission, with no menu interaction.
- `dbg.skipconvo` / auto-advance the campaign encounter conversation that gates a
  field battle.
- Auto-ready deployment (or auto-deploy to a named layout).

### 3. Battle factory (any roster, captains, layout)
- `dbg.battle <preset.json>`: build a mission from a preset describing both
  sides' rosters (`{ troopId, count }[]`), captains/heroes per side and per
  formation, culture, scene, time/season, and player role
  (commander | soldier | spectator).
- **Generalize RBP's existing factory.** RBP already builds field battles via
  `CustomBattleHelper.GetCustomBattleParties` / `PrepareBattleData` in
  `src/RealisticBattlePlanning/Harness/RbpCustomBattleFactory.cs` and auto-spawns
  via `RbpAutoBattleGameManager`. Lift that pattern; replace the hard-coded
  Empire-vs-Aserai / `[150,49,0,0]` defaults with the preset.
- **Programmatic formation assignment.** `dbg.assign <troopQuery> <formationN>`
  and `dbg.layout <preset>` set exactly which troops occupy each of formations
  1–8, so a battle reproduces a real campaign layout (e.g. RBP's 1‑2 infantry /
  3‑4 ranged / 5‑6 cav / 7‑8 HA) deterministically. Verify the assignment API
  with ilspycmd (`Agent.Formation`, the deployment/MissionAgentSpawnLogic path).
- `dbg.ready`, `dbg.restart` (re-run the same battle, same seed).

### 4. Campaign control (API, not mouse)
- `dbg.camp.goto <settlement|party|x,y>`, `dbg.camp.encounter <party>`,
  `dbg.camp.battle <party>` (force a field battle), `dbg.camp.time <scale|skip h>`,
  `dbg.camp.party add <troopId> <n>` / `set`, `dbg.camp.gold <n>`,
  `dbg.camp.spawn <party>`, `dbg.load <save>`.
- A quick "bootstrap to a known campaign state" command for repeatable campaign
  tests (cf. RBP's `campaign-test-loop`).

### 5. Observability / telemetry
- `dbg.snapshot [path]` → `battle_state.json`: mission time/phase; for each own &
  enemy formation: number, class, live composition, count, centre position,
  facing, current `MovementOrder` + target (with nav-mesh validity), captain,
  casualties %, broken/routing. Optionally per-agent rows behind a flag.
- **Continuous telemetry** → `telemetry.jsonl`: append an event for order issued
  (by *any* mod — hook `Formation.SetMovementOrder`/OrderController via Harmony),
  formation move start/stop, agent death, mission phase change, each tagged with
  tick + time. This is the flight recorder.
- `dbg.hud "msg"` pushes a line to an on-screen debug channel.
- Exception capture → `errors.jsonl` (hook AppDomain + the mission tick) with
  stack traces, plus an automatic `dbg.snapshot` on first exception.

### 6. Visual debug
- Gauntlet overlay: formation numbers, current order per formation, move-target
  markers, mission time, FPS, and the `dbg.hud` text channel. (Reuse RBP's
  gauntlet patterns — see `gauntlet-ui-patterns` memory; mind the
  DoNotPassEventsToChildren / focus-layer gotchas.)
- Gizmos: order-target markers, facing arrows, ranges, waypoints, and a
  nav-mesh-face highlight under a queried point.
- Free debug camera toggle (RTS-style) for observation; or document leaning on
  the existing RTSCamera mod.

### 7. Capture
- `dbg.shot [name]` → a **clean, game-only** PNG to `…/shots/` plus a sidecar
  `name.json` with the state at capture (so a screenshot is self-describing).
  Investigate the engine screenshot API (`Utilities`/`MBDebug`/`ScreenManager`)
  with ilspycmd; fall back to a windowed GDI grab keyed off the game's HWND if
  no clean engine path exists.
- `dbg.shot.auto on <interval|events>` burst capture.
- `dbg.rec start|stop` → numbered frame sequence + `manifest.json`;
  `tools/make-video.ps1` (ffmpeg) stitches frames to mp4/gif out-of-process.
  (Frame-burst is the robust path; a live encoder is a stretch goal.)

### 8. Determinism & inspection
- `dbg.seed <n>`, `dbg.pause` / `dbg.step <n ticks>` / `dbg.resume`,
  `dbg.timescale <x>`, `dbg.freeze enemy|all|none`, `dbg.god`, and inspection
  helpers like `dbg.select <agent|formation>` → full state dump.

---

## Architecture

- **`src/ModDebugKit/`** — engine assembly (`net472`), Bannerlord module id
  `ModDebugKit`, its own `SubModule.xml`, `MBSubModuleBase` entry point. Depends
  only on TaleWorlds assemblies (+ Harmony for hooks); **never references RBP**.
  Deploys via the same post-build copy pattern as RBP (see `Directory.Build.props`
  / module `bin` layout).
- **`src/ModDebugKit.Core/`** — engine-free logic (`netstandard2.0`): command
  parsing/dispatch model, script DTOs, preset DTOs, snapshot/telemetry DTOs +
  JSON, the io-channel protocol. No TaleWorlds types.
- **`src/ModDebugKit.Core.Tests/`** — xUnit (`net8.0`): parser, script timeline,
  preset round-trip, snapshot serialization. Runs with no game installed (mirror
  RBP's split — see AGENTS.md).
- Engine side: a `CommandPump` MissionBehavior + global behavior for the file
  channel; a `TelemetryRecorder` MissionBehavior (Harmony hooks for cross-mod
  order capture); a `DebugOverlay` Gauntlet layer; a `CampaignDebugBehavior :
  CampaignBehaviorBase`; a `BattleFactory`; a `CaptureService`.
- **Output root** (configurable; default `Modules/ModDebugKit/Debug/`):
  `io/in.txt`, `io/out.jsonl`, `battle_state.json`, `telemetry.jsonl`,
  `errors.jsonl`, `shots/`, `rec/`. Document every schema in
  `docs/mod-debug-kit-io.md` so the agent (and future sessions) can rely on it.

## Prior art to reuse (in this repo)
- Console-command pattern: `src/RealisticBattlePlanning/Execution/PlanCommands.cs`
  (`[CommandLineFunctionality.CommandLineArgumentFunction("name","ns")]`).
- Custom-battle spawn: `src/RealisticBattlePlanning/Harness/RbpCustomBattleFactory.cs`
  + `RbpAutoBattleGameManager.cs` + `AutoBattleCommands.cs`.
- Auto-leave / scenario lifecycle: the RBP harness (`Harness/` + Core `Harness/`).
- Build/deploy/launch tooling: `tools/dev-relaunch.ps1`, `focus-game.ps1`,
  `decompile-game.ps1` (ilspycmd wrapper for finding engine APIs).
- Conventions: `AGENTS.md` (three-assembly split, vanilla-first, commit style).
- Reference mods on disk: `C:\github\RTSCamera` (mission views/overlays/Harmony),
  `C:\github\bannerlord-banner-kings` (CampaignBehaviors, MCM, UIExtender).

## Known gotchas (learned the hard way)
- **Move orders need a nav-mesh face.** Use
  `formation.CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache.None)`
  then `SetVec2(target)`. A raw `new WorldPosition(...)` has no face and the move
  is silently ignored. The telemetry/order viz should *surface* this, not repeat it.
- **Formation slot ≠ class ≠ contents.** The default deployment auto-sort is by
  class, but assignment is controllable — read live composition and/or set the
  layout explicitly.
- **Safe-mode dialog** appears after a force-kill relaunch; it's a pre-game
  launcher dialog (handled by `dev-relaunch.ps1`, not the module).
- Gauntlet input layers: the DoNotPassEventsToChildren / focus-layer pitfalls
  from `gauntlet-ui-patterns`.

## Milestones (original build order)
- **M0 — Spine.** Module skeleton (both projects + tests + deploy) · `dbg.ping` ·
  output dir · **file command channel** · `dbg.snapshot` MVP. *This alone
  transforms the agent loop.*
- **M1 — Battle factory.** Preset-driven custom battle · roster + captains ·
  programmatic formation assignment · auto-ready/restart.
- **M2 — Flight recorder.** Telemetry stream (cross-mod order hooks, deaths,
  phases) · exception capture · overlay HUD.
- **M3 — Capture.** Clean screenshots + sidecar · burst · frame-rec + video tool.
- **M4 — Campaign control.** goto/encounter/force-battle/party/time/gold/load.
- **M5 — Determinism & viz.** seed · pause/step · freeze · nav-mesh & order gizmos.
- **M6 — Scripting & polish.** Script runner · preset/scenario library · IO-schema
  docs · **dogfood**: reproduce RBP's move-bug and formation-mismatch as canned
  demo scenarios proving the kit catches them.

## Build progress & revised plan (learned during development)

**Shipped (committed + pushed, each build-clean / unit-tested / verified in-game
through the file channel):** M0 spine · M1 battle factory (`dbg.battle` presets,
exact rosters + heroes, `dbg.assign`/`dbg.layout`, `dbg.ready`/`leave`/`restart`)
· M2 flight recorder (`telemetry.jsonl`: phases/deaths/orders incl. the nav-mesh
verdict; `errors.jsonl` + auto-snapshot; `dbg.telemetry`/`dbg.errors`) · the
determinism time controls (`dbg.pause`/`resume`/`step`/`timescale`/`freeze`),
pulled forward from M5.

**Remaining (revised order):** Capture → HUD overlay → Campaign control →
Scripting & dogfood. (The remaining M2 *overlay HUD* moved to sit after Capture.)

**Lessons that reshaped the plan:**

1. **The file channel is not just the spine — it's the dominant interface.**
   Every feature so far is fully drivable and verifiable by appending a line and
   reading a JSON(L) file. Build each capability *file-first*; the console and
   any UI are secondary front-ends. Prioritise features by (agent value ×
   file-channel verifiability).
2. **Verify visual features only where you can see them — so order by
   verifiability.** Anything whose correctness lives on-screen (the HUD overlay,
   gizmos) can't be confirmed in a headless/autonomous run (the computer-use
   grant for the game window times out with no human present). **Capture
   (`dbg.shot`) is self-verifiable** — the agent writes a PNG and reads it back —
   so it now comes *before* the HUD, and is the tool that makes the HUD
   verifiable. This is why determinism (pure file-channel) was done before the
   visual M2.4/M3 work.
3. **Vanilla-first over Harmony, hard.** Patching `Formation.SetMovementOrder`
   to capture orders **crashed the game** (it runs on worker threads and
   destabilised `MovementOrder`'s static init). Polling each formation's
   effective order on the main thread catches orders from *any* source, is safe,
   and needs no patch. Prefer observing engine state on the main thread to
   intercepting engine calls.
4. **All engine reads on the main thread.** The file-channel pump and all
   mission reads run on the main thread (`OnApplicationTick` / mission ticks);
   never touch engine objects from a callback that may fire off-thread.
5. **Resolve "current X" by lookup, not by caching a lifecycle static.**
   `OnBehaviorInitialize` is not reliably called for an added behavior; a static
   assigned there silently stays null. Use `Mission.Current.GetMissionBehavior<T>()`
   (cost the casualties bug a session).
6. **Engine APIs are leads, not gospel — read the decompiled source.** Time
   control is a *request* system (engine sets `Scene.TimeSpeed` to the min
   request each tick; a raw set is reverted), and `RemoveTimeSpeedRequest` does
   an unguarded `RemoveAt(-1)` when the id is absent. Both only became clear from
   the source after the naïve approach failed in-game.
7. **Some state resists external control — document the limit, don't fake it.**
   The enemy general AI keeps re-commanding its formations, so per-side
   `dbg.freeze enemy` is best-effort; `dbg.pause` (time = 0) is the hard freeze.
   Surface such limits in the command help and docs rather than shipping a
   silent half-feature.
8. **Iterate in small committed slices with an in-game check each.** Splitting
   milestones into sub-iterations (M1.1–M1.4, M2.1–M2.3) and verifying each
   through the file channel caught real bugs early (the order-capture crash, the
   casualties static, the pause request system) — each would have been far
   costlier found later.

## Definition of done (per milestone)
Builds clean; Core logic unit-tested; deploys to the game; the new commands work
**through the file channel** (not just console); the IO schemas are documented;
each milestone is committed + pushed. The acceptance test for the whole kit:
*an agent can set up a specific battle, run it, and report exactly what each
formation did — without a single screenshot or mouse click.*
