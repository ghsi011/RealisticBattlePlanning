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
  (`SubModule.xml`, eventually `ModuleData/`, `GUI/`, etc.).
- `Directory.Build.props` — resolves `BannerlordGameDir` and `ModuleDeployDir`.
- `local.props` (gitignored) — per-machine overrides. Template in `local.props.example`.
- `bannerlord-battle-planning-mod-spec.md` — the design spec.
- `docs/implementation-plan.md` — phased implementation plan (3 phases;
  Phase 1 MVP broken into iterations). Check the current iteration before
  starting work.

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

- **Harmony** (`Lib.Harmony`) — patches via `[HarmonyPatch]`. Registered in
  `SubModule.OnSubModuleLoad`.
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
