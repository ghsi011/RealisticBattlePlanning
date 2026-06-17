# Session prompt — build ModDebugKit

Paste everything below the line into a fresh Claude Code session opened in
`C:\github\RealisticBattlePlanning`.

---

You are building a new Bannerlord module, **ModDebugKit**, inside this repo. It is
a standalone, general-purpose **mod-debugging tool** — not part of
RealisticBattlePlanning (RBP) and it must never reference RBP. RBP is just its
first dogfood target. The full design is in
[docs/mod-debug-kit-design.md](../mod-debug-kit-design.md) — **read it first**, then
read `AGENTS.md`, `Directory.Build.props`, and the prior-art files it points to.

## The point of this mod (read this twice)

It exists so that an AI agent (me, in a future session) can debug Bannerlord mods
**fast and without the mouse**. The whole game today is driven by launching, clicking
through menus, pixel-hunting an in-game console, and guessing what happened from
screenshots. That inner loop is the bottleneck. ModDebugKit's north star:

> Put the game into an exact state, make something happen, and read precisely what
> happened — **through the filesystem** — in seconds, repeatably.

The single most important feature is therefore the **file command channel**: a
behavior that watches a text file, executes each appended line as a `dbg.*`
command on the game's main thread, and writes the structured result back to a
JSONL file. With that, I drive the game by `Write`-ing a line and `Read`-ing the
output — no console keystrokes, no screenshots. Build that first and make every
other command flow through it. Everything else (battle factory, telemetry,
capture, campaign control, determinism) hangs off that spine — see the design
doc's capability areas and milestones.

## Hard constraints

- **Three-assembly split**, mirroring RBP (see `AGENTS.md` "Repo layout"):
  - `src/ModDebugKit/` — `net472` engine assembly (SubModule, MissionBehaviors,
    Harmony hooks, Gauntlet overlay, campaign behavior). May use TaleWorlds types.
  - `src/ModDebugKit.Core/` — `netstandard2.0`, **engine-free**: command parsing/
    dispatch model, preset/script/snapshot/telemetry DTOs + JSON, the IO protocol.
    **Never reference TaleWorlds types here.**
  - `src/ModDebugKit.Core.Tests/` — `net8.0` xUnit; must run with no game install.
- Add all three projects to `RealisticBattlePlanning.sln`. Wire the same
  post-build deploy pattern RBP uses (copy DLL + `Module/**` into
  `$(BannerlordGameDir)\Modules\ModDebugKit\...`); reuse `Directory.Build.props`
  conventions (don't hard-code machine paths — `local.props` / `BannerlordGameDir`).
- Its own `Module/SubModule.xml` with a distinct `Id` = `ModDebugKit`. Depend only
  on what it needs (Native, SandBoxCore, Sandbox, StoryMode, CustomBattle,
  Bannerlord.Harmony — add MCM/UIExtender only if you actually use them).
- **Vanilla-first** (AGENTS.md): build on existing engine mechanisms; read the
  surrounding vanilla/reference-mod code before inventing a parallel system.
- The build must stay green and RBP must keep building. Don't touch RBP source
  except to *read* it as prior art.

## Prior art to lift (don't reinvent)

- Console command attribute pattern:
  `src/RealisticBattlePlanning/Execution/PlanCommands.cs`.
- Custom-battle creation & auto-spawn:
  `src/RealisticBattlePlanning/Harness/RbpCustomBattleFactory.cs`,
  `RbpAutoBattleGameManager.cs`, `AutoBattleCommands.cs` — generalize these into a
  preset-driven `BattleFactory` (arbitrary rosters, captains, **programmatic
  formation assignment**).
- Finding engine APIs: `tools/decompile-game.ps1` (ilspycmd). Use it to confirm
  the real APIs for: deployment/formation assignment (`Agent.Formation`,
  MissionAgentSpawnLogic), screenshots (`Utilities`/`ScreenManager`/`MBDebug`),
  campaign encounters (`EncounterManager`/`PlayerEncounter`/`StartBattleAction`),
  and order capture (`Formation.SetMovementOrder` / `OrderController`). **Verify
  before relying on any API name in this prompt — treat them as leads, not gospel.**
- Reference mods on disk: `C:\github\RTSCamera` (mission views, overlays, Harmony
  against MissionScreen), `C:\github\bannerlord-banner-kings` (CampaignBehaviors,
  MCM, UIExtender).

## Known gotchas (already paid for — don't repeat)

- **Move orders need a resolved nav-mesh face.** Use
  `formation.CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache.None)`
  then `SetVec2(target)`. A raw `new WorldPosition(...)` is silently ignored.
  Make the order-telemetry/gizmo *surface* missing faces — that bug cost days.
- **Formation slot ≠ class ≠ contents.** The deployment auto-sort is by class but
  assignment is player- and API-controllable. Read live composition and/or set
  the layout explicitly; never assume slot index = troop type.
- Gauntlet overlays: watch the DoNotPassEventsToChildren / focus-layer pitfalls
  (ask me or grep RBP's `UI/` for the patterns).
- The launcher safe-mode dialog is a pre-game concern handled by
  `tools/dev-relaunch.ps1`, not the module.

## Build order (milestones — commit + push each)

1. **M0 Spine:** three-project skeleton + deploy + `dbg.ping` + output dir +
   **file command channel** (write line → execute → JSONL result) + `dbg.snapshot`
   MVP (formations: number, class, live composition, count, position, order +
   move-target with nav-mesh validity, captain, casualties, broken).
2. **M1 Battle factory:** preset-driven custom battle, rosters + captains,
   programmatic formation assignment, auto-ready/restart.
3. **M2 Flight recorder:** `telemetry.jsonl` (cross-mod order hooks via Harmony,
   deaths, phase changes) + exception capture + overlay HUD.
4. **M3 Capture:** clean game-only screenshots + state sidecar; burst; frame-rec +
   `tools/make-video.ps1` (ffmpeg).
5. **M4 Campaign control:** goto / encounter / force-battle / party / time / gold /
   load.
6. **M5 Determinism & viz:** seed, pause/step, freeze, nav-mesh + order gizmos.
7. **M6 Scripting & polish:** `dbg.run <script.json>`, preset/scenario library,
   document every IO schema in `docs/mod-debug-kit-io.md`, and **dogfood**:
   reproduce RBP's nav-mesh move-bug and the formation/composition mismatch as
   canned demo scenarios proving the kit catches them.

## Definition of done (every milestone)

Builds clean (RBP still builds too); Core logic unit-tested and green; deploys to
the game; the new commands work **through the file channel**, not just the
console; IO schemas documented; committed and pushed. Per repo convention, write
the commit body to `COMMIT_MSG.tmp` and `git commit -F` it.

**Whole-kit acceptance test:** an agent can set up a specific battle, run it, and
report exactly what each formation did — with zero screenshots and zero mouse
clicks.

## Working agreement

- Start by reading the design doc + AGENTS.md + the prior-art files, then post a
  short M0 plan and proceed. Use ilspycmd to verify any uncertain engine API
  before coding against it.
- Keep `docs/mod-debug-kit-design.md` updated if the design changes under you.
- Prefer small, verifiable, committed increments over a big-bang drop.
