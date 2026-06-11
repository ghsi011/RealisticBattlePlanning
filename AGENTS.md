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

- `src/` — the C# project (`net6.0`). One assembly: `RealisticBattlePlanning.dll`.
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

Two larger mods are checked out next to this repo as implementation references:

- `C:\github\RTSCamera` — battle-mission camera + command overlays. Best
  reference for **mission views, HUD, Harmony patches against
  `MissionScreen`/`OrderTroopControllerVM`**.
- `C:\github\bannerlord-banner-kings` — campaign-systems mega-mod. Best
  reference for **CampaignBehaviors, save data, MCM settings, UIExtender
  prefab extensions, ButterLib usage**.

When you need a pattern (registering a behavior, patching a vanilla type,
extending a Gauntlet prefab), grep these two before inventing one.

## Build & run

```powershell
dotnet build RealisticBattlePlanning.sln -c Debug -p:Platform=x64
```

The build is hermetic except for `BannerlordGameDir` — it must point at a
Bannerlord install containing `bin\Win64_Shipping_Client\TaleWorlds.Library.dll`,
or the build errors out with a readable message.

Launch via BLSE. There is no automated way to verify in-game behavior; if you
change something visible, say "verified by launching" or say you couldn't.

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
3. Grep vanilla Bannerlord assemblies (via dotPeek / ILSpy) for the type
   you're about to override.
4. Only then write something new.
