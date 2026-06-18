---
paths:
  - "src/RealisticBattlePlanning.Core/**"
  - "src/ModDebugKit.Core/**"
---

# Core assemblies stay engine-free

`*.Core` projects target `netstandard2.0` and hold **engine-free logic only**.

- **Never** reference TaleWorlds types (`TaleWorlds.*`), `Mission`, `Agent`,
  `Formation`, `WorldPosition`, MCM, Harmony, or any Bannerlord runtime type here.
- Engine reads live in the `net472` engine assembly (e.g. `MissionSnapshot`, the
  mission behaviors / `Dbg*` observers), which adapts live engine state into the
  plain data types Core consumes (`MapVec`, the snapshot interfaces, DTOs).
- This is what keeps Core unit-testable with no game install (`dotnet test`).

If you need engine data in Core, pass it through a snapshot/DTO captured on the
engine side — never reach into the engine from Core.
