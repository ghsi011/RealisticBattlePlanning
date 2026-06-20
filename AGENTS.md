# AGENTS.md

Conventions and context for AI assistants (and humans) contributing to this
repo. Keep this file short and load-bearing — link out instead of inlining
long explanations.

## What this project is

A Bannerlord singleplayer mod implementing the design in
[bannerlord-battle-planning-mod-spec.md](bannerlord-battle-planning-mod-spec.md).
That spec is the source of truth for *what* to build. This file covers *how* we
work inside the repo.

The single most important design principle from the spec is **vanilla-first**
(spec §1.1): any feature that can be built on, derived from, or expressed
through an existing Bannerlord mechanism *must* be. New systems are a last
resort. When in doubt, read the surrounding vanilla code before adding a
parallel one.

## Repo layout

- `src/RealisticBattlePlanning/` — the engine assembly (`net472`): SubModule,
  mission behaviors, Harmony patches, snapshot adapters, UI.
- `src/RealisticBattlePlanning.Core/` — engine-free logic (`netstandard2.0`):
  plan model, validator, formatter, serializer, RbpLog, Plan Monitor +
  trigger evaluation, harness recording/assertions/diffing. **Never reference
  TaleWorlds types here.**
- `src/RealisticBattlePlanning.Core.Tests/` — xUnit tests (`net8.0`). Run with
  `dotnet test src\RealisticBattlePlanning.Core.Tests` — no game install
  needed.
- `Module/` — files copied verbatim into the deployed module root
  (`SubModule.xml`, `ModuleData/`, `GUI/` prefabs + brushes, etc.).
- `tools/` — dev scripts: `map-iterate.ps1` / `respawn.ps1` / `crop-zoom.ps1`
  (the file-driven UI loop, below) + `dev-relaunch.ps1` / `deploy-ui.ps1` /
  `focus-game.ps1` / `view-screenshot.ps1`, `run-harness.ps1`,
  `decompile-game.ps1`.
- `Directory.Build.props` — resolves `BannerlordGameDir` and `ModuleDeployDir`.
- `local.props` (gitignored) — per-machine overrides. Template in `local.props.example`.
- `bannerlord-battle-planning-mod-spec.md` — the design spec.
- `docs/implementation-plan.md` — phased implementation plan (3 phases;
  Phase 1 MVP broken into iterations). Check the current iteration before
  starting work.
- `graphify-out/` (gitignored, generated) — a **queryable knowledge graph of
  the whole repo** (code + docs) built by the `/graphify` skill: `graph.json`
  (GraphRAG-ready), `graph.html` (interactive), `GRAPH_REPORT.md` (god nodes,
  communities, surprising cross-file links). For fast cross-file orientation,
  query it instead of grepping blind: `/graphify query "<question>"`. **Keep it
  fresh** — re-run `/graphify . --update` (incremental; re-extracts only changed
  files) after a substantial change, e.g. at the end of an iteration block.

There is also a sibling debugging module, **ModDebugKit** (`src/ModDebugKit/`),
for driving Bannerlord mods through the filesystem (no mouse/screenshots) — see
the `moddebugkit` skill and `docs/mod-debug-kit-design.md`. Prefer its file
command channel for in-game verification of RBP behaviour.

The build's post-build target copies the DLL into
`$(BannerlordGameDir)\Modules\RealisticBattlePlanning\bin\Win64_Shipping_Client\`
and mirrors `Module\**` into the module root.

## Reference mods on this machine

Three larger mods are checked out next to this repo as implementation references:

- `C:\github\RTSCamera` — battle-mission camera + command overlays. Best
  reference for **mission views, HUD, Harmony patches against
  `MissionScreen`/`OrderTroopControllerVM`**.
- `C:\github\bannerlord-banner-kings` — campaign-systems mega-mod. Best
  reference for **CampaignBehaviors, save data, MCM settings, UIExtender
  prefab extensions, ButterLib usage**.
- `C:\github\RealisticBattleProject` — RBM, the spec's named compatibility
  target (G4/R3). `RealisticBattleAiModule` is where to read **how modified
  team AI decides to advance/commit** (EnemyCommits tuning) and what it
  touches on the player team. Verified 2026-06-12: its tactics only steer the
  player team when the player is *not* general (`IsPlayerTeam &&
  !IsPlayerGeneral && IsPlayerSergeant` guards), so plan governance via
  `SetControlledByAI(false)` is compatible by design — confirm again in the
  Phase 3 G4 pass.

When you need a pattern (registering a behavior, patching a vanilla type,
extending a Gauntlet prefab), grep these two before inventing one.

Decompiled vanilla sources live at `C:\github\bannerlord-decompiled`
(23 assemblies, regenerate with `tools/decompile-game.ps1` after game
patches — version recorded in its VERSION.txt). **Grep these before assuming
any engine API exists or behaves as expected.** In cloud/CI sessions where
that path doesn't exist, clone the private mirror as a sibling of the repo:
`gh repo clone ghsi011/bannerlord-decompiled ../bannerlord-decompiled`.
That repo is TaleWorlds' proprietary code: it stays private, and nothing is
ever copied from it into this codebase — read it for API reference only.

## Build & run

```powershell
dotnet build RealisticBattlePlanning.sln -c Debug -p:Platform=x64
```

The build is fully hermetic: when no game dir is configured (no `local.props`,
no `BANNERLORD_GAME_DIR`) and the hard default doesn't exist, the engine
assembly compiles against the BUTR `Bannerlord.ReferenceAssemblies` NuGet
package (compile-only) and deploy is skipped — so cloud/CI sessions can build
and API-check engine code. An *explicitly configured* path that is invalid
still fails the build (a typo'd `local.props` must not silently
build-without-deploy); a real install always wins and enables the deploy step.

Launch via BLSE. In-game verification is semi-automated by the Layer-2
harness (I5): in the dev console, `rbp.harness_arm all`, start a field battle
you command, and everything after clicking Ready is unattended (scenario plan
injection, fast-forward, recording, assertion evaluation); then
`rbp.harness_diff` against the known-good baseline, `rbp.harness_accept` to
promote a run. Results live in `Modules\RealisticBattlePlanning\Logs\Harness`.
The harness does not yet spawn battles itself, and anything it doesn't cover
still needs a manual launch — say "verified by launching" or say you couldn't.

**Seeing the game yourself (UI work):** the in-game `rbp.screenshot [name]`
console command saves a frame to `Modules\RealisticBattlePlanning\Logs\
Screenshots\<name>.bmp` (the engine writes BMP regardless of extension). Run
`tools\view-screenshot.ps1 <name>` to convert it to a `.png`, then Read that
PNG — the Read tool renders it, so you can visually verify UI/state directly
instead of asking the user to describe it. Logs go to `Logs\rbp.log`.

**Fast UI dev loop (file-driven, no keyboard).** The in-game keyboard is
unreliable when the game is driven over automation/remote (keystrokes and the
`rbp.plan`/Numpad0 toggle drop), so the visual loop does NOT use computer-use to
open the planner. Instead `PlanningModeView` polls a sentinel file
`Modules\RealisticBattlePlanning\Debug\planner.cmd` (inert in normal play) for
one verb per write: `open｜close｜toggle｜reopen｜brushes｜shot <name>｜reshot <name>｜
click <nx> <ny>｜rightclick <nx> <ny>`. Two wrapper scripts drive it:

- **`tools\map-iterate.ps1 <name>`** — one command per *visual* iteration on the
  map/panel: `deploy-ui` (hot-copy `Module\GUI\**`) → `brushes` (hot-reload
  `RbpBrushes.xml` into the live factory — brushes are cached at startup and do
  NOT reload on reopen otherwise) → `reopen` (rebuild the movie, re-reads the
  prefab) → `shot` → convert the BMP to `temp\<name>.png` to Read. **No relaunch
  for XML/brush edits.** `-Cmd reopen|open|close|toggle|shot`, `-NoDeploy` to
  skip the GUI copy.
- **`tools\respawn.ps1`** — one command for a *C# change*: `dev-relaunch.ps1`
  (kill → build+deploy → launch → dismiss safe-mode → pin window) then spawn a
  `cav-clash` battle and wait for deployment. A loaded .NET assembly can't be
  hot-swapped, so C# still needs this (~90s); XML/brush use `map-iterate` only.
- **`tools\crop-zoom.ps1 <name>`** — upscale the map-panel region of a capture so
  marker/glyph/rail detail is legible when Read.

The `click`/`rightclick` verbs dispatch a normalized map click straight into the
VM, so select/place/remove are testable deterministically over the file channel
(no mouse). `dev-relaunch.ps1 -NoBuild` skips the build; `tools\deploy-ui.ps1`
and `tools\focus-game.ps1` still exist for manual use. In the dev console (when
the keyboard works), `rbp.autobattle` spawns a field battle and `rbp.plan`
toggles the editor. Keep the game open between iterations; close it when done.

**Committing:** PowerShell here-strings mangle `-`, `/`, `!`, and `()` in a
commit body — write the message to `COMMIT_MSG.tmp` (gitignored) and
`git commit -F COMMIT_MSG.tmp` instead of `-m`.

## Testing

Three binding layers — details in
[docs/implementation-plan.md](docs/implementation-plan.md), "Testing
architecture (binding)":

1. **Pure logic** — all plan/trigger/stage/fidelity/XP/template logic lives in
   `RealisticBattlePlanning.Core` (`netstandard2.0`, no TaleWorlds types) and
   is unit-tested with scripted snapshot timelines via `dotnet test`
   (`net8.0` test project, no game install needed).
2. **Engine integration** — dev-mode in-game scenario harness: scripted
   battles, a recorder MissionBehavior, tolerance-based assertions encoding
   the spec's H-scenarios.
3. **Engine contract** — reflection checks at mod load (dev mode) that every
   patched/depended-on TaleWorlds member still has the expected signature.

A feature is not done until its Layer-1 tests exist and its numeric H-scenario
(if any) runs in the harness. If something is hard to test, move the logic out
of the engine layer rather than write a clever engine-coupled test. Every plan
deviation in the log is tagged `INTENDED_FIDELITY` or `FAULT` (spec R2).

## Available frameworks

Assume installed and loaded (declared in `Module\SubModule.xml`):

- **Harmony** (`Lib.Harmony`) — patch hook registered in
  `SubModule.OnSubModuleLoad`. Vanilla-first, so there is exactly **one
  `[HarmonyPatch]`**: `Patches/DeploymentCameraReachPatch` (relaxes the
  deployment-camera leash so you can pan to field-planned waypoints — a private
  camera clamp with no event/extension point). Order-override detection still
  rides the vanilla `OrderController.OnOrderIssued` event, not a patch. Add a
  patch only when no event/extension point exists — and add its target to
  `EngineContract` so a game update surfaces as a readable failure, not a crash.
- **BLSE** — replaces the launcher. Mods don't take a code dependency, but it
  must be running for ButterLib/MCM to behave.
- **ButterLib** — DI container, extended logging, event helpers.
- **UIExtenderEx** — extend vanilla Gauntlet prefabs and view models without
  cloning them. Registered in `SubModule.OnSubModuleLoad`.
- **MCM** — settings UI. Use the `Bannerlord.MCM` package and the
  `[SettingsType]`/`[SettingProperty…]` attributes.

All four ship runtime DLLs through their own Bannerlord modules; our csproj
takes them as `IncludeAssets="compile"` PackageReferences so we get the API at
build time without shipping a copy.

## Coding conventions

- **C# 10, `net472`, nullable disabled.** The TFM is load-bearing: the Steam
  build of Bannerlord hosts the .NET Framework CLR, so a `net6.0` assembly
  crashes the game at startup (unresolvable `System.Runtime 6.0`). Don't
  "modernize" it.
- Namespace root: `RealisticBattlePlanning`.
- Folder structure (when these start to exist) follows the spec's feature
  areas: `Planning/` (Area A), `Execution/` (Area B), `Drills/` (Area C),
  `Competence/` (Area D), `Maneuvers/` (Area E), `Settings/` (Area F),
  `Persistence/` (Area G). Plus `Patches/` for Harmony patches and `UI/` for
  view models and prefab extensions.
- Prefer extending vanilla types and behaviors over wrapping them
  (vanilla-first, spec §1.1).
- No comments explaining *what* well-named code already says. One-line *why*
  comments are fine when the reason is non-obvious.
- Don't add backwards-compatibility shims for code that hasn't shipped.

## Things to leave alone

- `local.props` — never commit it; never read another machine's into the repo.
- The `BannerlordGameDir` fallback default in `Directory.Build.props` — change
  the template, not the default, unless the install path itself changes.
- `bannerlord-battle-planning-mod-spec.md` — treat as the spec. Significant
  design changes go through it, not around it.

## When in doubt

1. Re-read the relevant section of the spec.
2. Grep the two reference mods for a matching pattern.
3. Read the decompiled vanilla sources at `C:\github\bannerlord-decompiled`.
   Engine lifecycle ordering has already bitten once: submodule
   `OnMissionBehaviorInitialize` runs before behaviors' `EarlyStart`, so
   mission state like `Mission.IsFieldBattle` (set by
   `MissionCombatantsLogic.EarlyStart`) is not valid at attach time — check
   such state in `AfterStart` or later.
4. Only then write something new.
