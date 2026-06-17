# ModDebugKit — IO schemas

Every file ModDebugKit reads or writes, and the exact shape of each. This is
the contract an agent (or any external driver) relies on to drive the game and
read results **through the filesystem** — no console keystrokes, no
screenshots. Paths are defined in one place (`ModDebugKit.Io.DbgPaths`); this
document mirrors them.

## Output root

Everything lives under one root, default
`…/Modules/ModDebugKit/Debug/`. Override with the `MODDEBUGKIT_OUT`
environment variable (set before launch) to redirect output anywhere.

```
<root>/
  io/
    in.txt          # append commands here
    out.jsonl       # one result object per line
  battle_state.json # last dbg.snapshot
  moddebugkit.log   # session log
  telemetry.jsonl   # (M2) flight recorder
  errors.jsonl      # (M2) captured exceptions
  shots/            # (M3) screenshots
  rec/              # (M3) frame recordings
```

## The command channel

### `io/in.txt` — command input (you write)

Plain UTF-8 text, **append-only**. Each line is one command:

```
namespace.command arg1 arg2 "an arg with spaces"
```

- Tokens are whitespace-separated. Double quotes group a token containing
  spaces. A backslash escapes the next character (`\"`, `\\`, `\ `).
- Blank lines and lines beginning with `#` or `//` are ignored (comments).
- Only the **first** `.` splits namespace from name, so `dbg.camp.goto` parses
  as namespace `dbg`, name `camp.goto`.

**Append, don't overwrite.** The channel tracks a byte cursor and executes only
*newly appended* complete lines (a line without a trailing newline waits until
its newline arrives). Lines already present when the module loads are **skipped**
— a stale file never re-runs last session's commands. If the file is truncated
or replaced (length shrinks), the channel resets and re-reads from the top.

Drive it from a shell, e.g.:

```bash
echo 'dbg.ping' >> "<root>/io/in.txt"
```

```powershell
Add-Content -Path "<root>\io\in.txt" -Value 'dbg.snapshot'
```

The channel polls ~5×/second on the game's main thread, so a command runs within
~0.2 s of being appended.

### `io/out.jsonl` — command results (you read)

One JSON object per line (JSONL), appended in execution order. One line per
command the **file channel** executed (console-issued commands print to the
console and are not mirrored here). Fields:

| field         | type           | notes                                                        |
|---------------|----------------|--------------------------------------------------------------|
| `seq`         | int            | Monotonic per-session sequence number.                       |
| `ts`          | string         | Wall-clock capture time, ISO-8601 UTC.                       |
| `missionTime` | float          | Mission time in seconds; omitted when no mission is active.  |
| `ok`          | bool           | True on success.                                             |
| `cmd`         | string         | Dispatch key, e.g. `dbg.snapshot`.                           |
| `raw`         | string         | The original input line, verbatim.                          |
| `msg`         | string         | Human-readable one-liner (also the console return value).    |
| `error`       | string         | Present only when `ok` is false.                            |
| `data`        | object         | Per-command structured payload (below); omitted when none.   |

Example:

```json
{"seq":1,"ts":"2026-06-17T12:00:00.0000000Z","ok":true,"cmd":"dbg.ping","raw":"dbg.ping","msg":"pong — ModDebugKit live, at the menu","data":{"pong":true,"outputRoot":"…/Debug","inMission":false}}
```

## Commands

| command            | `data` payload                                              | effect                                                              |
|--------------------|-------------------------------------------------------------|---------------------------------------------------------------------|
| `dbg.ping`         | `{ pong, outputRoot, inMission, scene? }`                   | Liveness; confirms the channel is live and where the game is.       |
| `dbg.help`         | `{ commands: string[] }`                                    | Lists every registered command's usage.                            |
| `dbg.snapshot [path]` | `{ path, formations, battleStarted }`                    | Writes the full battle state to `path` (default `battle_state.json`) and acks with a summary. Requires an active mission. |
| `dbg.battle [preset]` | `{ preset, mode }` (`mode`: `load` from menu, `direct` from the custom-battle menu) | Launches a custom field battle from a preset, no menu navigation. No arg = the default Empire-vs-Aserai battle. Refuses when already in a mission. |
| `dbg.ready`        | —                                                           | Finishes deployment and starts the battle (no Ready click). After this, `battleStarted` is true and the snapshot reports casualties against the deployment baseline. |
| `dbg.leave`        | —                                                           | Ends the current mission and returns to the menu.                  |
| `dbg.restart`      | —                                                           | Ends the current battle and relaunches the same preset (the relaunch is deferred until the game is back at the custom-battle menu). |
| `dbg.assign <sel> <N>` | `{ moved, formation, selector }`                        | Moves the player's units matching `<sel>` into formation `N` (1–8). `<sel>` ∈ `all`/`inf`/`ranged`/`cav`/`ha` (matched by live class — mounted × shoots). |
| `dbg.layout <sel=N> …` | `{ assignments: [{selector, formation, moved}] }`       | Applies several `dbg.assign`s at once, e.g. `dbg.layout inf=1 ranged=3 cav=5 ha=7`. |

Together these give a full mouse-free battle lifecycle: `dbg.battle` → (`dbg.assign`/`dbg.layout` to set the layout) → `dbg.ready` → `dbg.snapshot` → `dbg.restart` or `dbg.leave`.

**Slot vs contents:** assignment moves units into a formation *number*, whose slot keeps its own class name. After `dbg.layout inf=5`, formation 5 (slot "Skirmisher") holds infantry — the snapshot's `slotClass` reads "Skirmisher" while `composition.label` reads "Infantry". Always trust `composition`, not the slot.

### `dbg.battle` preset resolution

The argument resolves to a preset file:
- A bare name (`skirmish`) → `presets/skirmish.json` under the output root.
- A path or `.json` name (`foo/bar.json`, `C:\tmp\x.json`) → resolved relative to the output root, or absolute.
- No argument → the built-in default (Empire 150 inf / 49 rng vs Aserai 120 inf / 40 rng / 40 cav, player Defender Commander, `battle_terrain_029`, summer dawn).

From the main menu it loads the custom game then launches on load-finish; from the custom-battle menu it launches directly. Then `dbg.snapshot` reads the result — the whole "set up a battle and read what each formation did" loop runs with no mouse and no console.

## Battle presets — `presets/*.json`

Every field is optional; an omitted field falls back to the default, so a partial preset only overrides what it sets (`{}` is the full default).

```json
{
  "scene": "battle_terrain_029",
  "season": "summer",
  "timeOfDay": 6.0,
  "gameType": "Battle",
  "playerSide": "Defender",
  "playerType": "Commander",
  "player": {
    "culture": "battania",
    "commander": "commander_1",
    "counts": [120, 40, 0, 0],
    "troopsByClass": [["battanian_veteran_falxman"], ["battanian_fian_champion"], [], []]
  },
  "enemy": {
    "culture": "empire",
    "commander": "commander_11",
    "counts": [100, 30, 30, 0]
  }
}
```

| field                | type        | notes                                                                                  |
|----------------------|-------------|----------------------------------------------------------------------------------------|
| `scene`              | string      | Scene id; default `battle_terrain_029`.                                               |
| `season`             | string      | `summer`/`winter`/`spring`/`fall`; default `summer`.                                  |
| `timeOfDay`          | float       | Hours 0–24; default 6.                                                                 |
| `gameType`           | string      | `Battle` (field). Default `Battle`.                                                   |
| `playerSide`         | string      | `Defender` or `Attacker`; default `Defender`.                                         |
| `playerType`         | string      | `Commander` or `Sergeant` (no spectator role in custom battle); default `Commander`.  |
| `player` / `enemy`   | object      | One side each (below).                                                                 |

**Side**

| field           | type       | notes                                                                                               |
|-----------------|------------|-----------------------------------------------------------------------------------------------------|
| `culture`       | string     | Culture string id (`empire`, `aserai`, `battania`, …). The +1 commander is added on top.            |
| `commander`     | string     | Commander character id (e.g. `commander_1`); becomes the side's general.                            |
| `counts`        | int[4]     | `[infantry, ranged, cavalry, horseArcher]`. Note: troops are sorted into formation slots by their *actual* class, so an "aserai cavalry" count can land in the HorseArcher slot — read the snapshot's `composition`, not the slot. |
| `troopsByClass` | string[][] | Optional troop ids per class bucket (index 0–3). The bucket's count is split across the listed ids; an empty/missing bucket uses the culture's default troop. Ignored when `troops` is set. |
| `troops`        | object[]   | **Exact** roster: each `{ "troop": id, "count": n }` adds exactly `n` of that troop. Overrides `counts`/`troopsByClass`. Use this to reproduce a precise roster (e.g. 50 + 30 + 12), which the per-class split can't express. |
| `heroes`        | string[]   | Named characters (companions, lords) added one each, on top of the roster. |

Either `counts` (per-class, default-troop filled) **or** `troops` (exact per-troop) describes a side; the two sides are independent (one can use counts while the other uses an exact roster).

## `battle_state.json` — battle snapshot

Written by `dbg.snapshot`, pretty-printed. The agent reads this instead of a
screenshot. It deliberately records the three things that are **not** the same —
the lesson the kit exists to surface: the slot (`number`/`slotClass`), the
engine's dominant-class guess (`representativeClass`), and what the formation
actually holds (`composition`).

```json
{
  "capturedAtUtc": "2026-06-17T12:00:00.0000000Z",
  "sceneName": "battle_terrain_029",
  "missionTime": 42.5,
  "battleStarted": true,
  "playerTeamIndex": 0,
  "formations": [
    {
      "side": "player",
      "teamIndex": 0,
      "number": 1,
      "slotClass": "Infantry",
      "representativeClass": "Infantry",
      "count": 98,
      "composition": { "infantry": 88, "ranged": 10, "cavalry": 0, "horseArcher": 0, "total": 98, "label": "Mostly Infantry" },
      "position": { "x": 612.3, "y": 488.1 },
      "facing": { "x": 0.0, "y": 1.0 },
      "order": { "type": "Move", "moveTarget": { "x": 640.0, "y": 520.0 }, "targetIsValid": true, "targetHasNavMeshFace": true },
      "captain": { "name": "Caladog", "agentIndex": 12, "active": true },
      "casualtiesPercent": 2.0,
      "broken": false
    }
  ]
}
```

### Field reference

**Top level**

| field            | type   | notes                                                       |
|------------------|--------|-------------------------------------------------------------|
| `capturedAtUtc`  | string | Wall-clock capture time, ISO-8601 UTC.                     |
| `sceneName`      | string | Mission scene id.                                          |
| `missionTime`    | float  | Mission time in seconds.                                   |
| `battleStarted`  | bool   | True once deployment is over and the battle is running.   |
| `playerTeamIndex`| int    | Team index of the player's team; -1 if none.              |
| `formations`     | array  | One entry per non-empty formation on every team.          |

**Each formation**

| field                 | type     | notes                                                                                  |
|-----------------------|----------|----------------------------------------------------------------------------------------|
| `side`                | string   | `player`, `enemy`, or `other` (allies/neutral), relative to the player's team.        |
| `teamIndex`           | int      | The owning team's index.                                                               |
| `number`              | int      | Player-visible formation number 1–8 (`FormationClass` index + 1).                     |
| `slotClass`           | string   | The slot's class name (e.g. `Infantry`). **Not** necessarily what it holds.           |
| `representativeClass` | string   | The engine's dominant-class guess for the current contents.                           |
| `count`               | int      | Current unit count.                                                                    |
| `composition`         | object   | Live counts by `infantry`/`ranged`/`cavalry`/`horseArcher` + `total` + `label`. A unit is mounted×shoots-classified; `label` follows `CompositionClassifier` (≥70% ⇒ "Mostly X", else "Mixed"). |
| `position`            | `{x,y}`  | Formation centre (engine X/Y).                                                         |
| `facing`              | `{x,y}`  | Unit facing direction (normalized).                                                    |
| `order`               | object   | Current movement order (below).                                                        |
| `captain`             | object   | `{ name, agentIndex, active }`; omitted when no captain.                              |
| `casualtiesPercent`   | float    | Losses since deployment, 0–100; omitted when no baseline was captured (pre-deployment or no deployment phase). |
| `broken`              | bool     | True when ≥50% of units are routing.                                                  |

**`order`**

| field                  | type    | notes                                                                                       |
|------------------------|---------|---------------------------------------------------------------------------------------------|
| `type`                 | string  | Movement order enum: `Move`, `Charge`, `Stop`, `Advance`, `FallBack`, `Retreat`, `Follow`, … |
| `moveTarget`           | `{x,y}` | Resolved destination; present only for a `Move` order.                                      |
| `targetIsValid`        | bool    | `WorldPosition.IsValid` — scene set and position finite.                                    |
| `targetHasNavMeshFace` | bool    | **The move-bug detector.** False ⇒ the target has no nav-mesh face, so the engine silently ignores the move and the formation never advances. |
```
