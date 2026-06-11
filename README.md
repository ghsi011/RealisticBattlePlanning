# Realistic Battle Planning

A Mount & Blade II: Bannerlord singleplayer mod. The player is a general, not a
superhuman micromanager — gather your sub-commanders at a battle map before the
fight, lay out a plan with triggers and directives per formation, and watch
your officers carry it out with fidelity earned through training and battle.

The full design is in [bannerlord-battle-planning-mod-spec.md](bannerlord-battle-planning-mod-spec.md).
This README covers how to build, deploy, and run the mod skeleton.

Status: **v0.1 scaffold** — the mod loads and toasts "Realistic Battle Planning
loaded" on the main menu. No gameplay features yet.

---

## Requirements

- Bannerlord (tested against v1.4.6)
- A modern .NET SDK (8.x+) for building — the project targets `net472`
- The following Bannerlord mods installed and enabled in the launcher:
  - Bannerlord.Harmony
  - Bannerlord Software Extender (BLSE)
  - ButterLib
  - UIExtenderEx
  - MCM (Mod Configuration Menu)

The mod targets `net472` because the Steam build of Bannerlord runs on the
.NET Framework CLR — a `net6.0` assembly crashes the game on load (it can't
resolve `System.Runtime 6.0`). TaleWorlds / SandBox assemblies are referenced
directly from your Bannerlord install.

## First-time setup

1. Clone the repo.
2. Copy `local.props.example` to `local.props` and edit `BannerlordGameDir` to
   match your install path. `local.props` is gitignored so it won't follow you
   into commits.

   ```xml
   <BannerlordGameDir>C:\games\Steam\steamapps\common\Mount &amp; Blade II Bannerlord</BannerlordGameDir>
   ```

   If `BannerlordGameDir` isn't set anywhere, the build also falls back to the
   `BANNERLORD_GAME_DIR` environment variable, then to a hard-coded default in
   [Directory.Build.props](Directory.Build.props). The build fails fast with a
   readable error if the resolved path doesn't contain
   `bin\Win64_Shipping_Client\TaleWorlds.Library.dll`.

## Build & deploy

```powershell
dotnet build RealisticBattlePlanning.sln -c Debug -p:Platform=x64
```

On success the build copies:

- `RealisticBattlePlanning.dll` (and `.pdb`) → `$(BannerlordGameDir)\Modules\RealisticBattlePlanning\bin\Win64_Shipping_Client\`
- everything under `Module\` (currently just `SubModule.xml`) → `$(BannerlordGameDir)\Modules\RealisticBattlePlanning\`

Launch Bannerlord through BLSE, enable **Realistic Battle Planning** in the
launcher mod list (drag it below the framework mods), start a new game or load
a save. You should see a green "Realistic Battle Planning loaded" toast on the
main menu.

## Tests

Engine-free logic lives in `RealisticBattlePlanning.Core` and is unit-tested
without a game install:

```powershell
dotnet test src\RealisticBattlePlanning.Core.Tests
```

## Project layout

```
RealisticBattlePlanning/
├── Directory.Build.props                  # BannerlordGameDir + module identity
├── local.props.example                    # template for per-machine config
├── local.props                            # gitignored, your real config
├── RealisticBattlePlanning.sln
├── src/
│   ├── RealisticBattlePlanning/           # engine assembly (net472): SubModule, mission logic
│   ├── RealisticBattlePlanning.Core/      # engine-free logic (netstandard2.0): plan model & co.
│   └── RealisticBattlePlanning.Core.Tests/  # xUnit tests (net8.0)
├── Module/                                # copied verbatim into the deployed module root
│   ├── SubModule.xml
│   └── ModuleData/rbp_debug_plan.json     # dev/test plan, hand-editable in the deployed copy
├── tools/                                 # decompile-game.ps1, offline smoke tests
├── docs/implementation-plan.md            # phased plan + binding testing architecture
├── bannerlord-battle-planning-mod-spec.md
├── README.md
└── AGENTS.md                              # contributor + AI-assistant conventions
```

## License

TBD.
