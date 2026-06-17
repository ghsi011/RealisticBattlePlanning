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

## Commands (M0)

| command            | `data` payload                                              | effect                                                              |
|--------------------|-------------------------------------------------------------|---------------------------------------------------------------------|
| `dbg.ping`         | `{ pong, outputRoot, inMission, scene? }`                   | Liveness; confirms the channel is live and where the game is.       |
| `dbg.help`         | `{ commands: string[] }`                                    | Lists every registered command's usage.                            |
| `dbg.snapshot [path]` | `{ path, formations, battleStarted }`                    | Writes the full battle state to `path` (default `battle_state.json`) and acks with a summary. Requires an active mission. |

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
