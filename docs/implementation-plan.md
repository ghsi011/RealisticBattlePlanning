# Implementation Plan

Derived from [the spec](../bannerlord-battle-planning-mod-spec.md). The spec is the
WHAT; this document is the order of work. The spec's advisory milestones M1–M5
are regrouped into three phases; Phase 1 is a playable MVP with **no training,
no progression, no maneuver templates** — every commander executes at a fixed
fidelity.

**Resolved (2026-06-11):** the v0.1 launch crash was the assembly targeting
`net6.0` while the Steam build of Bannerlord hosts the .NET Framework CLR
(crash log: unresolved `System.Runtime 6.0` → `0xE0434352`). Retargeted to
`net472`; confirmed by every verified launch since (I3–I6 in-game passes).

---

## Phase overview

| Phase | Name | Delivers | Spec areas | Acceptance |
|---|---|---|---|---|
| 1 | **Plan & Execute (MVP)** | Author a plan in the deployment phase, watch it execute; override/resume, aborts, player signals, HUD. Fixed fidelity. | A (fallback UI), B | H1 (fixed fidelity), H2, H3, H7, H8 (core) |
| 2 | **Officers** | Vanilla-derived competence, fidelity error model, XP, tiers, barks, AAR, dossier, drills, save persistence. | C, D, G | H1 (tiered), H4, H6, H9 |
| 3 | **Playbook & Polish** | Maneuver templates (built-ins, stamping, mid-battle invocation, custom), plan presets & repeat-last, stylized map, onboarding, MCM options, comms realism, RBM/RTS Camera compatibility, perf. | E, F, rest of A | H5, H10, full H1 (<3 min via template), perf |

Ordering rationale: execution before UI (the engine risk lives in execution);
progression before templates (template *learning* rules depend on the
progression system, E4/C6); polish last because presets and the stylized map
refine flows that must exist first.

### Phase 1 scope decisions

- **Fidelity is fixed.** An `IFidelityModel` seam with a pass-through
  implementation (perfect execution, or a single configurable tier) so the
  real model swaps in without rewriting Phase 1 code; it doubles as the
  spec's "progression off" master toggle (F). *Built 2026-06-13 (Phase-2
  pull-forward P1-P3): the seam exists (`Core/Fidelity`), the monitor
  consults it, and the engine stays on pass-through so in-game behaviour is
  unchanged until the model is deliberately switched on. See the Phase 2
  summary.*
- **UI is the fallback presentation** (A2.5): vanilla deployment top-down
  camera + Gauntlet overlay. The stylized battle map is Phase 3.
- **Friction reducers** (A3.9): only the "suggested opening stage" default
  ships in Phase 1. Presets and repeat-last-plan land in Phase 3 (they're
  required for v1 *release*, not for the MVP).
- **Debug plan file**: until the editor exists (I9), plans are authored in a
  JSON file and loaded at battle start. It stays forever as a dev/test tool —
  it is also the plan-input path for the Layer-2 harness — and its event log
  is the seed of Phase 2's AAR and the R2 "fidelity noise vs. genuine bug"
  debug log.

### Front-loaded risks

1. **Team-AI suppression** (B1) — ✅ **RESOLVED 2026-06-12.** With the player
   as general, `SetControlledByAI(false)` suppresses the team AI for planned
   formations; orders issue directly on `Formation` (no Harmony needed).
   Verified in-game, and compatible with RBM by design (its tactics guard on
   `!IsPlayerGeneral`). The Layer-1 snapshot architecture and the Layer-3
   contract check did their mitigation jobs and stay.
2. **Custom formation behaviors** (feign retreat, missile-only flank arc,
   rear guard) — ✅ **RESOLVED in I6**: the whole vocabulary runs through the
   vanilla order system with Core-side steering; `BehaviorComponent` reuse
   was deliberately not taken (they don't tick under the suppression model).
3. **Deployment-phase UI injection** — **the one remaining open engine risk**,
   now next up (I9). RTSCamera's `Patch_DeploymentMissionController` and
   mission view registration are the pattern. Fallback if Gauntlet injection
   fights back: the debug-plan file plus templates remain a complete
   (developer-grade) authoring path, so I10–I12 are not blocked on the
   editor — but the MVP is not shippable without it.

---

## Testing architecture (binding)

Three layers plus cross-cutting rules. These are requirements, not
suggestions; the definition-of-done rule below gates every feature in every
phase.

### Assembly layout

| Assembly | TFM | Contents |
|---|---|---|
| `RealisticBattlePlanning.Core` | `netstandard2.0` | All engine-free logic: plan model, `PlanValidator`, `PlanFormatter`, plan JSON (de)serialization, Plan Monitor (stage machine + trigger evaluation), signal bus, fidelity error model, XP/tier math, template slot-matching, preset remapping, `RbpLog`. **No TaleWorlds types in this assembly, ever.** |
| `RealisticBattlePlanning.Core.Tests` | `net8.0` | xUnit tests for Core. Runs with plain `dotnet test`, no Bannerlord install. No TaleWorlds types here either. |
| `RealisticBattlePlanning` | `net472` | The engine assembly (existing). Mission behaviors, Harmony patches, snapshot adapters, order issuance, UI. References Core (`netstandard2.0` is consumable from `net472`). |

### Layer 1 — pure logic (unit tests)

Engine state reaches Core only through thin snapshot interfaces (e.g.
`IFormationSnapshot`: position, median speed, casualties, class). Tests drive
plans with **scripted snapshot timelines**: assert *Enemy commits to attack*
fires after a sustained approach and doesn't fire on a brief probe; assert the
full A6 feigned-retreat plan advances through all stages with correct signal
emission. All mod-owned RNG (fidelity rolls) is seedable; tests assert exact
outcomes per seed and distributions across seeds. Coverage target: **every
trigger, directive transition, abort rule, and fidelity formula.**

### Layer 2 — engine integration (scenario harness, dev-mode only)

Console commands that load a plan from JSON, run the armed battle at high
time-scale, and record results (stage activation times, positions vs.
anchors, aborts, signals) to a results file via a recorder `MissionBehavior`.
As shipped (I5), scenarios run on the next player-started field battle —
unattended after Ready; auto-spawning a scripted battle on a flat test scene
is the target and becomes an I12 prerequisite now that the pack is too big
to launch by hand (see I12). Every numeric acceptance criterion in spec
H1–H10 is encoded as a scenario with **tolerance-based assertions** — ranges,
never exact ticks; battles are non-deterministic. Regression = run the
scenario pack, diff results. (Pure time-budget criteria — H1/H10 authoring
stopwatch — stay manual.)

### Layer 3 — engine contract (patch-survival checks)

At mod load in dev mode, verify via reflection that every TaleWorlds
type/method we patch or depend on still exists with the expected signature;
report failures clearly instead of crashing mid-battle. The check list grows
with every new patch; the first version lands with the first Harmony patches
(I3).

### Cross-cutting rules

- **R2 lives in the code:** every plan deviation logged through `RbpLog` is
  tagged either `INTENDED_FIDELITY` (the dice rolled it) or `FAULT` (a bug),
  so playtest reports are triageable. The tag channel exists from Phase 1 —
  with the pass-through fidelity model there are no intended deviations, so
  anything tagged in Phase 1 is a `FAULT`; Phase 2's fidelity model emits
  `INTENDED_FIDELITY`.
- **Definition of done:** no feature is done until its Layer-1 tests exist
  and, if it has a numeric acceptance criterion, its H-scenario runs in the
  harness.
- **Testability beats cleverness:** prefer making code testable over writing
  clever tests. If something is hard to test, move the logic out of the engine
  layer (behind a snapshot interface) first.

---

## Phase 1 iterations

Each iteration ends green: builds clean, `dotnet test` green, deploys, and its
verify step passes. From I5 on, the harness scenario pack must also diff
clean. Earlier iterations' verify steps keep passing (manual regression).
Split gate since cloud development began (I5+): the cloud commit gate is
`dotnet test` green (with the pack validated against the Layer-1
simulation); deploy, the in-game pack diff, and the iteration's in-game
verify run at the local merge of each commit.

### I1 — Mission gating, infrastructure, plan model

Foundation; no visible behavior change. **Status: implemented** (gating fix
2026-06-11: `Mission.IsFieldBattle` is not valid at attach time — see
`PlannableMission` for the engine-ordering constraint).

- `MissionLogic` (or `MissionBehavior`) attached **only** to field battles the
  player commands — not sieges, hideouts, village fights, MP (G5). Detection
  helper + file logger (`rbp.log` style channel) for all plan events.
- Plan domain model as plain C# (mission-scoped, never saved — G1): `Plan`,
  `FormationPlan`, `Stage`, trigger/directive descriptors with parameters,
  signal names, abort conditions, map anchors (absolute for now).
- Debug plan loader: hand-written JSON in the module's config dir, keyed by
  formation class; validated and dumped to the log at mission start.
- **Verify:** log lines appear in a field battle and *don't* in a siege/town
  fight; malformed debug plan logs a readable error; zero behavior change (H7).
- Spec: §2, A1.1, A3.3–A3.5 (model only), G3, G5.

### I2 — Core assembly split + test project

Small but blocking: the Plan Monitor (I3) is exactly the logic that must be
born in Core behind snapshot interfaces, so the split happens before it is
written. **Status: implemented** (34 tests; `dotnet test
src\RealisticBattlePlanning.Core.Tests` runs gameless).

- Create `RealisticBattlePlanning.Core` (`netstandard2.0`) and
  `RealisticBattlePlanning.Core.Tests` (`net8.0`, xUnit); engine assembly
  references Core.
- Move into Core: `Planning/Model/*`, `PlanValidator`, `PlanFormatter`,
  the plan JSON serialization (model + serializer settings), `RbpLog`. The
  engine assembly keeps only the `ModuleHelper` path lookup as a thin wrapper
  feeding the Core deserializer, plus all mission code.
- First tests: `PlanValidator` error/warning cases (stage without a trigger →
  error; >3 AND'd conditions → error; emitted-but-never-consumed signal →
  warning); `PlanFormatter` golden output for a known plan; JSON round-trip
  (serialize → deserialize → structurally equal) for a plan exercising every
  trigger/directive enum value.
- **Verify:** `dotnet test` green on a machine with no Bannerlord install;
  the game build still deploys and the I1 in-game verify still passes
  unchanged.
- Spec: none new — enables the Testing architecture above.

### I3 — Plan Monitor + AI suppression (the risk spike)

The engine make-or-break. Smallest possible trigger/directive set, deepest
engine question. **Status: implemented, verified in-game 2026-06-12** (51
tests green at the time; suppression approach: planned formations get
`SetControlledByAI(false)` so the player-side general AI leaves them alone,
orders issued directly via `Formation.SetMovementOrder` etc.).

- Plan Monitor (stage machine + trigger evaluation) lives in **Core**, ticking
  a few times per second (B2). Engine state reaches it only through snapshot
  interfaces (`IFormationSnapshot`: position, median speed, casualties, class;
  plus a battlefield clock). The engine side implements only snapshot adapters
  over `Formation`/`Team` and issues orders from the directive transitions the
  monitor emits.
- Suppress team-level general AI for plan-governed formations only; issue
  movement/arrangement orders programmatically through the order system
  (first Harmony patches).
- Triggers: *On battle start*, *Timer elapsed*, *Own position reached*.
  Directives: *Hold position* (arrangement, facing, width), *Move to*
  (anchor/waypoints), *Charge*.
- **Layer 3:** dev-mode engine-contract check at mod load — reflect every
  patched/depended-on TaleWorlds member, report missing/changed signatures
  readably instead of crashing mid-battle.
- **Verify:** scripted-timeline unit tests — *Timer elapsed* advances its
  stage after the configured interval and exactly once; *Own position reached*
  fires when the snapshot position enters the radius, not during approach; a
  3-stage timeline emits Hold → Move → Charge transitions in order. In-game:
  a debug plan walks a formation through a 3-waypoint path, holds in
  shieldwall 30 s, then charges — while the vanilla general AI never touches
  it and unplanned formations behave vanilla; the contract check passes at
  load.
- Spec: B1, B2, A4/A5 (subset).

### I4 — Signal bus + full trigger vocabulary

**Status: implemented, verified in-game 2026-06-12** — signal relay ~250 ms,
counter-charge at 40 m, suppression holding (60 tests green at the time;
signals are latched and become visible the tick after they are raised, so
in-tick evaluation order never matters; `RaiseExternalSignal` is the entry
point the I8 palette and C7 drill cues will use).

- Signal bus: stages emit named signals on activation; *Signal received*
  trigger; AND-composition of up to 3 atomic conditions (A3.5). All Core-side,
  evaluated over snapshots.
- Remaining triggers: *Enemy commits to attack* (sustained-approach heuristic
  with configurable defaults — tune later against RBM, R3), *Enemy/Friendly
  formation within distance*, *Casualties above*, *Enemy broken/fleeing*.
- "Player's formation" usable as the reference formation in triggers (A3.10).
- All thresholds/defaults data-driven in config (D3 prep). *(Shipped as
  named constants — `TriggerDefaults`/`DirectiveDefaults`; the actual config
  file is I12's config pass.)*
- **Verify:** scripted-timeline unit tests — *EnemyCommitsAttack* fires after
  a sustained approach across consecutive snapshots and does **not** fire on a
  brief probe that reverses; a signal emitted on one formation's stage
  activation is received by another formation's *Signal received* trigger;
  an AND of 3 atomics fires only on a tick where all three hold; *Casualties
  above* fires once on threshold crossing. In-game: two-formation debug plan
  coordinates via signal; distance and enemy-commit triggers fire at sane
  moments (log timestamps vs. observation).
- Spec: A3.4, A3.5, A4.

### I5 — Layer-2 scenario harness (dev-mode)

Built now — before the full directive vocabulary — so the A6/H1 geometry and
timing criteria in I6 land as executable scenarios, not prose.
**Status: implemented, verified in-game 2026-06-12** — armed battles
fast-forward and write results to `Logs\Harness`; empty planned formations
fail as named preconditions (88 tests green at the time; pack
lives in `ModuleData\Harness`, console flow `rbp.harness_arm all` → fight the
armed battles → `rbp.harness_diff` / `rbp.harness_accept`; results in
`Logs\Harness`. One deliberate deviation: v1 does **not** auto-spawn the
scripted battle — the armed scenario runs on the next field battle the player
starts, and everything after clicking Ready is unattended (plan injection,
fast-forward via the vanilla UI fast-forward path, recording, evaluation,
diff). Auto-spawning a custom battle on a flat scene needs the CustomBattle
start API probed in-game first; it stays on the list for when the pack grows
past two scenarios. Also new: the engine assembly now compiles without a game
install — BUTR reference assemblies kick in as a compile-only fallback and
deploy is skipped — so cloud/CI sessions can build and API-check engine code.)
**Friction reducer added 2026-06-13:** an armed run auto-redistributes the
player's troops into exactly the formation slots the scenario needs at
deployment (so A6's four slots need no manual Order-of-Battle setup);
`rbp.harness_split` is the manual trigger for debug-plan runs. This is the
cheap "A" step toward the bigger automation lever — full auto-spawn +
scripted scenario actions (signal/override injection) for unattended H2/H8
runs — still queued for the I12 acceptance pass.

- Console commands / dev menu: load a plan from JSON, spawn a scripted battle
  on a flat test scene, run at high time-scale.
- Recorder `MissionBehavior` writes stage activation times, positions vs.
  anchors, aborts, and signals to a results file.
- Scenario definitions with tolerance-based assertions (ranges, never exact
  ticks); a runner that executes the pack and diffs results against the last
  known-good run.
- Seed the pack with two smoke scenarios: the I3 3-waypoint walk and an I4
  two-formation signal coordination.
- **Verify:** the smoke pack runs unattended at high time-scale and passes;
  a deliberately broken plan produces a failing, readable diff.
- Implementation notes: recording, assertion evaluation, and result diffing
  are all Core (`Harness/` — `RunRecorder`, `ScenarioEvaluator`,
  `ResultsDiff`); the engine side is a thin recorder `MissionLogic` fed by
  `PlanMissionLogic.MonitorTicked` (the recorder sees exactly what the
  monitor saw — no parallel engine reads). The shipped pack is itself a test
  fixture: Layer-1 tests run every scenario against a kinematic simulation
  (`HarnessSimulation`), so an in-game pack failure isolates to the engine
  adapters by construction.
- Spec: supports H1–H10 acceptance; G5 (dev-mode only, no player-facing
  surface).

### I6 — Full directive vocabulary

Core owns directive selection, parameters, and transitions; the behaviors
themselves are engine-side.
**Status: implemented, core verified in-game 2026-06-12** — the A6 trap
sprang correctly on the formations that were fielded (121 tests green at the
time).
Implementation decision: the whole A5 vocabulary is expressed through the
vanilla **order system**, not FormationAI `BehaviorComponent`s — the
in-game-verified B1 suppression (`SetControlledByAI(false)`) turns formation
AI off, so behavior weights wouldn't tick under it, and the spec's A5 note
makes behavior reuse the implementer's choice. Steering directives
(Skirmish, FlankArc, Screen, Follow) compute their move goal in Core every
monitor tick and re-issue it past an 8 m threshold
(`SteeringTargetChanged`); the engine stays a stateless order relay, so
every behavior is scripted-timeline-testable. A directive can still be
swapped to a `BehaviorComponent` engine-side later without touching the
Core contract. Model changes A6 forced: `PlannedFormationClass` now covers
all 8 vanilla order-of-battle slots (A6 fields 2 HA + 2 INF formations),
and `EnemyWithinDistance` takes an optional anchor reference ("enemy within
40 m of the retreat anchor"). Known limitations, deliberate: MoveTo
walk/run speed is not applied (vanilla movement orders don't expose it);
Charge's target selector is recorded but vanilla charge picks its own
melee targets; FeignRetreat reads as flight via movement-direction facing
+ the fire flag (no per-agent parthian-shot scripting).

- Vanilla-relative first (reuse/subclass `BehaviorComponent`s, per 1.1):
  *Skirmish/Harass*, *Pull back*, *Follow/Escort*, *Hold/Free fire*.
  *(Original scope — superseded by the decision note above: everything runs
  through the order system instead.)*
- Custom behaviors: *Feign retreat toward* (fire-while-withdrawing flag),
  *Flank arc* (side, standoff, **missile-only**), *Screen/Rear guard*
  (delaying posture).
- **Verify:** scripted-timeline unit tests — the full A6 plan walkthrough:
  all four formations advance through all their stages with "spring-trap"
  emitted and received at the right step; the missile-only flank arc never
  produces a melee-charge transition; the feign-retreat transition carries the
  fire-while-withdrawing flag. Harness: the canonical A6 Feigned Retreat runs
  end-to-end as a scenario against an aggressive AI army, with tolerance
  assertions on stage timing and anchor proximity — the MVP's "it actually
  works" moment (watched live at least once); each directive also gets a
  one-formation smoke scenario.
- Pack as shipped: `a6_feigned_retreat` (Skirmish, FeignRetreat, FlankArc,
  Hold, Charge under coordination — needs troops in all four of
  HorseArcher/LightCavalry/Infantry/HeavyInfantry) plus `pull_back`,
  `screen_guard`, `follow_escort`, `fire_discipline` smokes; with I5's
  `walk_waypoints`/`signal_coordination`, every A5 directive has at least
  one scenario. The simulation gained scripted enemies (aggressive chaser /
  static threat) so the pack stays Layer-1-validated. A6's geometric H1
  bands (±5 m, ≤2 s at Veteran) deliberately wait for I12's acceptance
  pass; the shipped assertions pin coordination (signal exists, both HA
  react within 2 s) with loose timing bands to survive scene variance.
- Spec: A5, A6.

### I7 — Override, resume, aborts, invalidation

Resume-stage selection, abort evaluation, and invalidation skipping are Core
logic; the engine side only reports overrides and reverts formations to
vanilla AI.
**Status: implemented, pending in-game verification** (145 tests green).
Implementation notes: override detection needs NO Harmony — the player's
orders flow through `Team.PlayerOrderController.OnOrderIssued` (a public
event), and our executor issues orders directly on `Formation`, so anything
on that event is genuinely player-issued. "Resume plan" ships as console
commands (`rbp.resume <formation|all>`, `rbp.plan_status`); the order-menu
entry is deferred to the UI iterations (I9+), as allowed by the session
brief. Semantics pinned by tests: commander death aborts unconditionally
(the `OnCommanderIncapacitated` flag is reserved for Phase 2's
incapacitated-but-alive distinction); a suspended formation does not abort
under the player's control — abort conditions are re-checked when resume is
requested; resume's TimerElapsed evaluation measures from the last
activation before the override (approximation, B5's "most appropriate
stage"); B6 skips bypass the skipped-to stage's trigger; a hold-and-notify
formation leaves the hold automatically if a later stage becomes evaluable
again. New plan events (PlanSuspended/PlanResumed/PlanAborted/StageSkipped/
PlanHolding) flow through the recorder into harness records.

- Any manual *movement/targeting* order suspends that formation's plan;
  *Resume plan* entry in the order menu picks the most appropriate stage (B5).
  **Refined 2026-06-13 (in-game):** posture orders (arrangement, facing, fire
  control, mount, cohesion) pass through without suspending — the player tunes
  *how* a formation fights without dropping it from the plan (conductor, not
  micromanager). The suspend set is the redirecting OrderTypes; everything
  else is posture.
- Abort conditions with editable defaults: casualties %, commander
  incapacitated, formation broken (A3.7); on abort revert to vanilla AI +
  notification (B4). Commander death always aborts.
- Situational invalidation: dead target/unreachable anchor → skip to next
  evaluable stage, else hold + notify (B6).
- **Verify:** unit tests — resume picks the most recent stage whose trigger
  currently holds, else the current stage; commander death aborts regardless
  of configured thresholds; an invalidated target skips to the next evaluable
  stage and holds + notifies when none exists. In-game: H2 (override & resume)
  and H3 (abort on commander death) pass manually — their harness encoding
  lands in I12.
- Spec: A3.7, B4–B6.

### I8 — Player Signal Palette

**Status: implemented, pending in-game verification** (150 tests green).
Input surface: Numpad1–4 fire the plan's declared player signals in order
(one input each, R7; numpad avoids the vanilla battle keys — MCM rebinding
arrives with Area F), plus `rbp.signal <name>` as the always-works console
fallback and the C7 drill-cue mechanism (undeclared names allowed there,
called out in the response). Every fire posts a battle message and an
RbpLog line (B11). Order-menu palette entries are deferred to the UI
iterations alongside I7's resume entry. Validator now enforces the declared
palette: blank/duplicate declarations are errors, and a PlayerSignal gate
on an undeclared name is an error (the palette could never fire it).
B8 comms delay and D3 missed signals are Phase 2 — the palette is
instantaneous and reliable for now.

- Plans declare up to 4 player signals; *Player signal* trigger type.
- Palette: order-menu entries + optional direct keybinds, fireable in ≤2
  inputs (R7); routes through the signal bus like any stage-emitted signal.
- **Verify:** unit test — a player signal injected into the bus releases a
  gated stage exactly as a stage-emitted signal does. In-game H8 core:
  infantry charge gated solely on player signal "hammer"; fire it mid-battle
  in ≤2 inputs, charge begins promptly.
- Spec: A4 (player signal), B9.

### I9 — Planning Mode UI: core editor

The biggest UI lift; primary authoring path replaces the debug file.
**In progress:** the editor's *logic layer* is complete, engine-free and
unit-tested — `PlanDraft` (add/remove/reorder stages, set triggers/
directives, abort conditions, declare signals/anchors, multi-formation
stage authoring per A3.6, live validation + plain summaries; bounds-checked,
no-throw) and `EditorDefaults` (the A3.9 opening stage + one-click
patterns). **Proven (2026-06-14):** the canonical A6 plan authored through
`PlanDraft` alone produces a byte-for-byte identical execution timeline to
the model/file-authored plan (the I9 "Verify" goal, at the logic layer) —
so the Gauntlet view is a thin shell over already-correct authoring, and
the only remaining work is the **editing widgets themselves** (a blind UI
build that needs in-game iteration to verify). **Deployment-phase view injection
RESOLVED in-game (2026-06-13) — the iteration's open engine risk is closed.**
`PlanningModeView` (a `MissionView` added via `AddMissionBehavior`) toggles a
read-only Gauntlet panel that renders the loaded plan's plain-language
summary during deployment. Hard-won details, now settled: the toggle key is
polled on the `MissionBehavior` tick (`OnMissionScreenTick` doesn't fire in
deployment); the screen is resolved via `ScreenManager.TopScreen` (the view's
`MissionScreen` is null because we add the view after the screen's
`RegisterView` pass and its setter is internal); a `rbp.plan` console toggle
exists as an input-independent path. Every Gauntlet call is guarded — a UI
fault degrades to a log line, never a mission crash. **Next:** swap the
read-only summary for editing widgets bound to the tested `PlanDraft`
(stage list, trigger/directive pickers), then anchors by ground-pick and the
I7/I8 order-menu entries (Resume plan, the Signal Palette). The
plan-logic-discovery standardization the 2026-06-12 review asked for is
**done** (the 2026-06-14 audit dropped the `Current` statics for
`Mission.Current?.GetMissionBehavior<T>()`).

- Enter/exit Planning Mode during deployment (keybind + button), time frozen
  (A1.1); confirming starts the battle (A1.3). Skipping = pure vanilla (A1.2).
- Formation list panel; select a formation → its Stage List editor: add/
  remove/reorder stages, trigger & directive dropdowns with parameter fields,
  signal naming, abort-condition editing.
- Map anchors and waypoint paths placed by ground-pick with the existing
  deployment camera (A2.4 minimal).
- New formations get the default Stage 1 "hold position, current facing"
  (A3.9 — the one friction reducer in scope).
- **Verify:** author the full A6 plan in the UI alone (no file); export it and
  run the I6 A6 harness scenario with the UI-authored plan — results diff
  clean against the file-authored run. No time budget yet.
- Spec: A1, A3.1–A3.3, A2.5 fallback.

### I10 — Editor completion: multi-select, warnings, polish

- Multi-select formations → author one shared stage cloned per formation
  (A3.6, the "instruct the 2 HA commanders together" story).
- Feasibility warnings, non-blocking: contradictory parameters only (e.g.
  flank standoff > weapon range) — competence warnings arrive with Phase 2
  (A3.8). Warning rules are `PlanValidator` logic → Core, unit-tested.
- Plain-language stage summaries ("When the enemy charges us → fall back
  behind the line, firing") and parameter defaults everywhere (R4). Summaries
  come from `PlanFormatter` → extend its golden tests.
- Player's own formation plannable (movement/arrangement directives, A3.10).
- **Verify:** new validator/formatter unit tests green; A6 plan authored in
  under 6 minutes manually (H1's manual budget, stopwatch); multi-select path
  used for the two HA formations.
- Spec: A3.6, A3.8 (partial), A3.9 (partial), A3.10, R4 (partial).

### I11 — Battlefield HUD & notifications

Carried from the 2026-06-12 review: begin this iteration by extracting a
`FormationExecutionState` (mode, active stage, timing, hold state) out of
`PlanMonitor` — the monitor already owns eight concerns at ~900 lines, and
both the HUD's queryable state and Phase 2's fidelity model want exactly
that per-formation state object. Splitting it before they land is cheap;
after, the fidelity/timing interdependencies make it expensive.

- Per-planned-formation HUD element: current stage, pending trigger,
  override/abort badge (B7) — rendered from the Plan Monitor's Core-side
  queryable state, no parallel bookkeeping. HUD verbosity config
  (full/minimal/off — F).
- Every plan event the player can perceive gets a battle message naming the
  formation/commander: stage transitions, aborts, overrides, resumes, signal
  receipts (the no-silent-deviation rule, B11 — text channel only; the
  in-character bark *flavor* comes with Phase 2 fidelity events). Each event
  also goes through `RbpLog` with the R2 deviation tag.
- **Verify:** play H1 watching only the HUD; every transition is legible; H8
  message trail shows signal → response; the harness recorder's event list
  matches the on-screen message trail for one scenario run.
- Spec: B7, B11 (channel), F (HUD verbosity).

### I12 — MVP hardening & acceptance pass

- **Prerequisite — harness auto-spawn:** probe the CustomBattle start API
  in-game and teach `rbp.harness_arm` to spawn its own scripted battles; the
  pack is 7+ scenarios and this pass runs it 3× on ≥2 scene types (~40+
  launches by hand otherwise). If the API is hostile, document the manual
  procedure and budget the time.
- **Harness scripted actions:** H2 and H8 involve player input, which the
  unattended harness lacks. Add a scenario `actions` list ("at t=30 inject
  signal 'hammer'"; "at t=20 override Infantry, resume at t=40") driving the
  monitor's existing entry points (`RaiseExternalSignal`,
  `NotifyPlayerOverride`, `RequestResume`) in both the simulation and the
  engine recorder.
- **`IFidelityModel` pass-through seam** + the progression-off master toggle
  (F): build the seam the Phase 1 scope decision promises, so Phase 2 swaps
  the real model in without touching Phase 1 code.
- Encode H1 (fixed fidelity), H2, H3, and H8 core as harness scenarios with
  tolerance-based assertions; this pack is the Phase 1 regression gate from
  here on. Run the pack 3×, on at least 2 scene types; diff results.
- Layer-1 coverage audit: every trigger, directive transition, and abort rule
  shipped in Phase 1 has scripted-timeline tests (the definition-of-done rule,
  retro-applied to anything that slipped).
- Performance: trigger evaluation profiled in a 1000-agent battle (B2 —
  target no measurable frame cost).
- Zero-touch audit (G3): with no plan authored, no codepath touches formation
  AI; siege/hideout entry points verified absent (G5).
- Config-file pass: all thresholds, defaults, and toggles introduced so far
  live in one documented config (F groundwork).
- **Verify:** scenario pack green 3×; `dotnet test` green; acceptance
  checklist in this doc ticked, with notes per run.

---

## Phase 2 — Officers (summary)

Fidelity, progression, drills, persistence. Roughly: derived Command
Competence from vanilla Tactics/Leadership + Plan Familiarity XP layer (D1);
five tiers (D2); the data-driven fidelity error model replacing the Phase 1
pass-through — reaction delay, positional error, trigger misjudgment,
discipline breaks, signal misses, abort composure (D3); XP sources and death
loss (D4); commander barks for every fidelity event (B11 full); After-Action
Report (B10); Commander Dossier + tier pips in the planner (D5); Drill
Sessions with costs, caps, drill cues via the signal palette, and pacing
controls (C1–C8); save persistence + safe-removal hygiene (G1, G2).

**Foundation pulled forward 2026-06-13 (P1-P5, Core, unit-tested, engine
still pass-through):**
- **P1** — the `IFidelityModel` seam, five tiers, seedable rolls, reaction
  delay applied at activation.
- **P2** — `CompetenceModel`: tier derived from Tactics 0.7 / Leadership 0.3
  + Plan Familiarity; `CompetenceFidelityModel`.
- **P3** — positional drift (15-25 m off-anchor Untrained → 2-3 m Master),
  recorded by the harness, tagged INTENDED_FIDELITY.
- **P4** — abort composure: green commanders pull out at ~0.7× the
  configured casualty limit.
- **P5** — progression (D4): `CommanderRecord` + `ProgressionModel`
  (familiarity XP, lesson-learned trickle, no decay, pacing tuned to
  Drilled-in-a-handful-of-battles) + `CommanderRecordBook` (death loses
  everything; absence keeps it). The C5 drill cap is enforced in
  `CompetenceModel.EffectiveScore` (drills lift only to Proficient).

223 unit tests. Four per-iteration review passes, plus a **holistic repo
audit (2026-06-14)** that found and fixed a critical execution-seam bug and
several consistency/architecture issues — see the checklist below. **Remaining
before switch-on:** trigger misjudgment / discipline / signal-miss dimensions
(the subtler D3 ones); barks (B11); AAR (B10); Dossier (D5); drills (C).
**The switch-on itself** — the engine adapter (read each captain's skills →
`CommanderProfile`, award XP on stage completion), a **per-battle seed**,
the progression on/off config toggle (F), and save persistence (G) — is a
deliberate, in-game-verified step: flipping the engine off pass-through is
where a green vs. veteran officer first visibly differ.

### Holistic audit (2026-06-14) — outcomes

Four parallel reviewers swept the whole repo before the next iterations.

**Fixed now (correctness + consistency, engine still pass-through):**
- **Drift leak (critical).** A pending reaction that skipped forward (its
  steering reference vanished during the delay) applied the *skipped* stage's
  positional drift to its replacement. The fidelity reset now lives inside
  `ActivateChecked` / `FormationExecutionState`, with a regression test.
- **One capped familiarity path.** `CommanderProfile.FromStats` is now
  stats-only; the only familiarity-bearing profile path is
  `ProgressionModel.ProfileFor` → `EffectiveScore`, which honors the C5 drill
  cap — the switch-on adapter can no longer bypass it.
- **`FidelityProfile.Perfect`** documented as a no-deviation sentinel (its
  `Master` tier is not a commander identity); added a `Deviates` predicate.
- **Live-resolver** for plan logic / view: dropped the cross-mission `Current`
  statics in favour of `Mission.Current?.GetMissionBehavior<T>()`.
- **`FormationExecutionState`** extracted out of `PlanMonitor` (the refactor
  this doc scheduled before the HUD), so the state machine's field invariants
  are guarded in one type.

**Owed at the switch-on (same iteration that flips the engine off pass-through):**
- **Per-battle seed** — derive `PlanMonitor`'s seed from a mission-stable
  value; the constant default would replay identical fidelity every battle.
- **Engine→profile adapter** — read each captain's Tactics/Leadership, build
  the profile (stats-only `FromStats`, or `ProfileFor` once a record exists),
  `SetCommander` per formation, and add the skill-read members
  (`GetSkillValue`, `DefaultSkills.Tactics/Leadership`) to `EngineContract`.
- **`StageCompleted` event** — the monitor emits only `StageActivated` (start);
  `ProgressionModel.OnStageCompleted/Failed` need a "stage finished" signal to
  award XP on, plus a defined `PlanAborted` → `OnStageFailed` mapping.
- **`ReactionDelayed` assertion** — the harness records the event but no
  scenario can assert on it; add a delay-band assertion + a `FixedTier`
  recorder test so the D3 reaction band is harness-gated.

**Owed in later Phase-2 iterations:**
- **D3 dimension taxonomy** — classify each remaining dimension on
  {trait | rolled} × {per-activation | per-tick | per-event} and thread the
  rng into the per-tick path; discipline-break (per-tick rolled) and
  signal-miss (per-signal) don't fit the current reaction/composure buckets.
- **Structured R2 tag** — replace the `[INTENDED_FIDELITY]`/`[FAULT]` log
  substrings with a `DeviationTag` on `PlanEvent` → `RecordedEvent` so the AAR
  (B10) and H9 partition mechanically; tag the untagged abort/skip/hold cases.
- **Area F config seam** — ~40 tuning constants are `public const` across six
  `*Defaults`/model classes (which inlines them across the assembly boundary);
  introduce a config object (defaults = today's values) and move the tunables
  to `static readonly` before MCM binding.

**Smaller hardening (cheap, opportunistic):**
- Scenario action formation selectors are validated for presence but not
  validity (a typo'd name no-ops silently) — parse-check at arm time.
- `MissionSnapshot` enemy id packing assumes ≤16 formations/team — pin with an
  assert/comment or key by `(team, index)`.
- `Speed` (walk/run) round-trips and prints but the executor drops it — the
  validator should warn it is not yet applied.
- `EngineContract` doesn't cover the Gauntlet/`MissionView` UI surface — add it
  as the panel becomes player-facing.

Testing: the fidelity error model lives in Core on a **seedable RNG** stream —
unit tests assert exact outcomes per seed and distributions across many seeds
(e.g. Untrained reaction delay lands in the D3 6–10 s band; signal-miss rates
match the configured table). XP/tier math is Core logic with unit tests over
thresholds and the D4 pacing formulas (pacing targets sanity-checked
numerically before playtest). The R2 tag goes live: fidelity rolls log their
deviations as `INTENDED_FIDELITY`, everything else stays `FAULT`, and H9's
no-silent-deviation check runs against the tagged log. The harness gains
tiered H-scenarios: H1 run at Untrained and at Veteran+ with tier-appropriate
tolerance bands.

Acceptance: tiered H1, H4, H6, H9; pacing targets sanity-checked (D4).

Two pins inherited from Phase 1 (recorded at the code level, surfaced here
so they are found from the planning side): `AbortConditions.
OnCommanderIncapacitated` is dead config in Phase 1 — commander death always
aborts; Phase 2 implements the incapacitated-but-alive distinction the flag
reserves. And B6 skip semantics are pinned by test: a skipped-to stage
activates immediately (trigger bypassed, timers re-baselined) — fidelity
reaction delays layer on top of activation, never on trigger arming.

## Phase 3 — Playbook & Polish (summary)

Templates, friction, presentation, compatibility. Roughly: Maneuver Template
model with role slots and relative anchors (E1); built-ins — Feigned Retreat,
Hedgehog, Organized Withdrawal (E2); per-commander knowledge + learn-by-doing
and drill teaching hooks into Phase 2 systems (E4, C6); stamping at planning
time (E5.1) and three-input mid-battle invocation with auto-suggest (E5.2,
E5.3); player-authored templates (E3); Battle Plan Presets + repeat-last-plan
(A3.9 remainder); stylized battle map presentation (A2 target); onboarding
walkthrough + concept pages (R5); MCM settings surface (F); comms realism
option (B8); RBM + RTS Camera compatibility passes across all battle
scenarios (G4, R3); performance and release packaging.

Testing: template slot-matching (class requirements, the E5.2 auto-suggest
ranking — matching class, highest Proficiency, nearest to origin) and preset
remapping (absolute→relative anchor conversion for E3.1, repeat-last remap
onto a same-composition army) are Core logic with unit tests. The **full
H1–H10 scenario pack** is the release regression gate, run on vanilla AI and
with RBM's AI module active (R3, G4); time-budget criteria (H1 <3 min via
template, H10 <30 s preset) stay manual stopwatch checks.

Acceptance: H5, H10, H1 in <3 min via template, full suite re-run on vanilla
and RBM AI.

---

## Next-phase iterations (I13–I29)

Detailed breakdown of Phase 2 completion → Phase 3, continuing the I1–I12
sequence. **New capability that reshapes verification:** `rbp.autobattle` +
the armed `HarnessRecorderLogic` make most fidelity work *assistant-verifiable*
(launch → `rbp.harness_arm <scenarios>` → `rbp.autobattle` → read
`Logs/Harness/last-run.results.json`). Each iteration flags **harness** (the
assistant can verify) vs **blind** (needs the human / `rbp.screenshot`).

**Guiding invariant for Phase 2A:** with fidelity off (the default), the
monitor produces byte-for-byte identical records to today — the known-good
harness baseline must diff clean after every 2A iteration except the two
deliberate re-accepts (I15, I18).

### Phase 2A — Fidelity switch-on

- **I13 — Engine→CommanderProfile adapter + per-battle seed. ✅ DONE (2026-06-14).**
  Read each captain's Tactics/Leadership (`DefaultSkills`, `GetSkillValue`) →
  `CommanderProfile.FromStats`; `SetCommander` per formation at deployment;
  per-battle seed (varied, pin-able). Shipped with I14. *(harness)*
- **I14 — Fidelity engine toggle (progression on/off). ✅ DONE (2026-06-14).**
  `FidelityConfig` (off / competence / fixed<tier>) + `rbp.fidelity` console
  command; monitor built with the chosen model+seed. Verified in-game:
  Untrained reaction delays (9.4/8.6s, in the 6–10s band, tagged
  INTENDED_FIDELITY) + abort composure (42% = 60%×0.7). *(harness)*
- **I15 — StageCompleted / PlanAborted→OnStageFailed events + XP firing.**
  Core: new `StageCompleted` PlanEvent (prior stage ends when the next
  activates / at battle end); `PlanAborted`→`OnStageFailed`, skipped→neither
  (pin all three). Record it (new `RecordedEventKind`). Engine: fire
  `ProgressionModel.OnStageCompleted/Failed` against a `CommanderRecordBook`
  (in-memory; persistence is I21), only when progression is on (G3). Tests:
  3-stage timeline emits StageCompleted correctly; XP pacing (Drilled in 3–5
  battles). **One deliberate baseline re-accept** (the new event fires
  regardless of model). *(harness)*
- **I16 — ReactionDelayBetween assertion + tiered H-scenarios.** Core:
  `AssertionType.ReactionDelayBetween` (delay in [min,max]); a scenario
  `fidelity` field (off/tier/on) so a run fixes the model unattended. Clone
  a6 into `a6_untrained` (wide bands, 6–10s reaction) and `a6_veteran` (tight
  bands). This is H1-tiered, now executable: same plan plays differently by
  tier. *(harness)*

### Phase 2B — Remaining D3 dimensions

- **I17 — Trigger misjudgment, discipline break, signal miss.** The audit's
  2×3 taxonomy {trait|rolled}×{per-activation|per-tick|per-event}: trigger
  jitter (rolled, per-activation — extend `FidelityProfile`/`Roll`);
  **discipline break (rolled, per-tick — NEW hook, thread `_rng` into the tick
  loop)** emits `DisciplineBroke`; **signal miss (rolled, per-event — NEW hook
  at signal receipt)** emits `SignalMissed`. Pass-through must draw no rng
  (rng-isolation test: new dimensions must not perturb existing reaction/drift
  draws). *(harness for wiring; Core for exact rates)*

### Phase 2C — Structured deviation tag

- **I18 — DeviationTag on PlanEvent→RecordedEvent→RbpLog.** Replace the
  `[INTENDED_FIDELITY]`/`[FAULT]` log substrings with a `DeviationTag` enum
  property; derive the log prefix from the tag (single source of truth); tag
  the untagged abort/skip/hold cases. Prereq for the AAR (B10) and H9. **One
  deliberate baseline re-accept** (new `Tag` field). *(harness)*

### Phase 2D — Player-facing surface

- **I19 — B11 barks/HUD deviation feedback.** Core `BarkCatalog` (event+tag →
  template) is unit-testable; engine fills the name + `DisplayMessage`. Every
  IntendedFidelity event yields a bark (no-silent-deviation guard over the
  event enum). *(blind render; logic testable; log trail is the proxy)*
- **I20 — B10 After-Action Report.** Pure-Core `AfterActionReport` builder
  over `BattleRecord` (planned-vs-actual timeline, deviations partitioned by
  tag, XP/tier deltas); engine renders it at battle end. Golden tests over the
  `a6_untrained` record. *(blind render; content harness-verifiable)*
- **I21 — D5 Commander Dossier + tier pips, and G persistence.** Save
  `CommanderRecordBook` (campaign behavior, keyed by hero id; death→Forget;
  mod-removal must not corrupt — G2). Dossier screen + Planning-Mode tier
  pips. Round-trip is Core-tested; the screen + save test are **blind/manual
  (H6)**.
- **I22 — Area-F config seam (RbpConfig).** Migrate the ~40 `public const`
  tunables (the audit item) to a config object (defaults = today's values;
  `const` inlines across the assembly boundary, so this precedes MCM). A pin
  test per migrated constant; **the whole baseline must diff clean** (defaults
  byte-identical). *(harness)*

### Phase 2E — Drills

- **I23 — C drills: drill mission, cues, gains, anti-grind caps.** Engine: a
  `Drill the troops` campaign action launching an enemy-less battle-map mission
  with Planning Mode + time-scale + cues (`rbp.signal`); time/food/denar cost
  (C3), 1/day cap. Core: route XP through `OnStageCompleted(inDrill:true)`
  (2× rate, Proficient cap — already built in P5). XP/cap math is Core-tested;
  the drill mission is **blind/manual (H4)**.

### Phase 3 — Playbook & Polish (coarser)

- **I24 — Maneuver Template model + built-ins (E1, E2):** `ManeuverTemplate`
  (1–6 role slots, relative anchors), built-ins (Feigned Retreat = A6,
  Hedgehog, Organized Withdrawal), per-commander knowledge (E4). A stamped
  Feigned-Retreat must execute-equal the hand-authored `a6_feigned_retreat`
  (reuse the I9 authored-equals-executed trick). *(harness)*
- **I25 — Stamping + mid-battle invocation + learn-by-doing (E5, E4, C6):**
  planning-time stamping; three-input "Execute Maneuver" with auto-suggest;
  learn-by-doing → `ProgressionModel`. Auto-suggest ranking Core-tested; the
  radial UI is **blind (H5)**.
- **I26 — Friction reducers: presets + repeat-last-plan (A3.9 remainder):**
  Core preset round-trip + repeat-last remap (un-mappable flagged). Execution
  is **harness**; the time budgets stay **manual (H10)**.
- **I27 — Presentation & onboarding (A2, B7, R5):** stylized map, HUD polish,
  onboarding. **Blind UI throughout.**
- **I28 — Options surface (MCM) + comms realism (F, B8):** bind I22 `RbpConfig`
  to MCM; distance-scaled signal delay (Core-computable, inserts at I17's
  per-event hook). Comms delay **harness**; MCM screen **blind**.
- **I29 — Compatibility + performance + release (G4, R3, perf):** RBM + RTS
  Camera passes; the full H1–H10 pack re-run on vanilla and RBM AI (the
  release gate — the autobattle loop was built for exactly this); 1000-agent
  perf profile; packaging. **The most harness-verifiable iteration.**

**Cross-cutting:** EngineContract grows in I13 (done), I21 (save/campaign),
I23 (drill menu), I27/I28 (UI). Fold the opportunistic audit hardening
(`MissionSnapshot` enemy-id `team*16+index` → key by tuple or assert ≤16; the
`Speed` walk/run dropped-at-execution validator warning — DONE 6cb032f) into
whichever iteration touches those files.

---

## Interactive Map Planner (IMP) — the map-first editor (spec A2.6)

User-directed 2026-06-19 (with a reference image): make the map the primary
authoring surface — click a formation number to select, point-and-click to add
move stages (default trigger = previous stage completed), multi-select + drag to
form a line, and a **KSP-style vertical stage rail** on the right that follows the
selection, expands a box on click to edit its command/trigger/target/distance,
reorders by drag, and (multi-select) colors only the stages identical across the
selected formations. **Principle:** all interaction logic lives in engine-free
Core (`PlanDraft` + new map-interaction helpers, unit-tested); the Gauntlet map +
rail are a thin direct-manipulation front-end verified visually via
`rbp.screenshot`/`dbg.shot`. Builds on the existing `PlanMapProjection`,
`PlanningModeVM` map scaffolding (`BuildMap`, `MapMarkerVM`), and `PlanDraft`.

Front-load Core (dotnet-test-verified), then Gauntlet (screenshot-verified):

- **IMP-1 (Core) — map inverse + click-to-march.** Add `PlanMapProjection.Unproject`
  (map point → world) with a round-trip test; a `MapAuthoring` helper that turns
  "click at world point with formation F selected" into a `PlanDraft` op (append a
  MoveTo scene-anchor at the point, trigger defaulting to previous-stage-completed).
- **IMP-2 (Core) — shared-stage detection.** Given N formations' stage lists, compute
  which stage indices are *identical* across all (command + trigger + params), for
  the multi-select rail coloring. Stage value-equality via the serializer.
- **IMP-3 (Core) — drag-to-line geometry.** Drag A→B + N formations → per-formation
  target world positions evenly along the line + the shared facing; one shared move
  stage cloned per formation.
- **IMP-4 (Core) — rail view-model.** An ordered, engine-free descriptor of a
  selection's stages (label, expanded state, shared flag, editable params) that the
  Gauntlet rail binds to; reorder maps to `PlanDraft.MoveStage`.
- **IMP-5 (Gauntlet) — map widget.** Custom full-bounds `Widget` (overrides
  `OnMousePressed`/`GetLocalPoint`; zero-size item wrappers fail hit-test descent —
  the 2026-06-12 review's diagnosis) rendering the A2.5 overlay map with formation
  markers at projected positions. Screenshot-verify it renders + reports clicks.
- **IMP-6 (Gauntlet) — select + highlight.** Click a number/block → select +
  highlight; box-drag / modifier multi-select.
- **IMP-7 (Gauntlet) — click-to-place wired.** Map click → IMP-1 → adds a move stage +
  draws the waypoint marker; in-game: the formation actually marches there.
- **IMP-8 (Gauntlet) — drag-to-line wired** (IMP-3): multi-select drag → line.
- **IMP-9 (Gauntlet) — the stage rail** (IMP-4): vertical boxes, per selection,
  click-to-expand inline editing.
- **IMP-10 (Gauntlet) — rail drag-reorder + multi-select shared-stage coloring** (IMP-2).
- **IMP-11+ — polish & stylized art** (A2.1/A2.2 target): terrain-matched relief,
  faction colors, troop glyphs; onboarding.
