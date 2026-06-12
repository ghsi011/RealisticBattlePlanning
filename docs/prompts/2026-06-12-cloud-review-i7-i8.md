# Cloud session brief: repo review + I7 + I8

Work on branch `claude/review-i7-i8` off current `master`. Three work
packages, in order, **exactly one commit each**, pushed when done. Do NOT
merge to master — the local session merges and in-game-tests each commit
separately, so bisectability is the point of the one-commit rule.

Read AGENTS.md first (conventions, testing architecture, reference repos);
read docs/implementation-plan.md for the I7/I8 sections; clone the private
decompiled-sources mirror per AGENTS.md before assuming any engine API.
`dotnet build` is hermetic without a game install (reference assemblies,
deploy skipped); `dotnet test src\RealisticBattlePlanning.Core.Tests` must be
green at every one of the three commits.

Ground truth already verified in-game — do not re-litigate:
- Plan governance via `SetControlledByAI(false)` works; the vanilla general
  AI leaves planned formations alone (and RBM's tactics guard on
  `!IsPlayerGeneral`, so it holds under RBM too).
- Core-side steering directives, the signal bus, the trigger vocabulary, and
  the Layer-2 harness all work in-game; the A6 trap sprang correctly on the
  formations that were fielded.
- The harness fast-forwards armed battles and writes results to
  `Logs\Harness`; empty planned formations now fail as named preconditions.

## WP1 — comprehensive code review + fixes (commit 1)

High effort, whole repo: `src/**`, `Module/**` (SubModule.xml, plan/scenario
JSON), `tools/`, docs consistency. Hunt in priority order:

1. Correctness bugs in Core logic (monitor state machine, steering math,
   sustain/approach trackers, anchor resolution, serializer dialects,
   harness evaluator/diff).
2. R2 integrity: any path where a plan deviation could be silent or a fault
   could read as intended behavior; any swallowed exception that should log.
3. G3 zero-touch: any codepath that could touch formations/missions when no
   plan is loaded or the mission is inert.
4. Core/engine boundary: no TaleWorlds types or engine assumptions in Core;
   engine reads only inside `MissionSnapshot.Capture`.
5. Tick-path performance (B2): per-tick allocations/LINQ in
   `MissionSnapshot.Capture`, `PlanMonitor.Tick`, steering, the recorder —
   a 1000-agent battle runs this at 4 Hz. Fix what's cheap, note what isn't.
6. EngineContract completeness: every engine member actually used is checked.
7. Validator gaps: plan/scenario JSON mistakes that parse but misbehave.

Fix what you find; keep behavior changes conservative and covered by new or
adjusted tests. The commit message must list findings fixed AND findings
reviewed-but-declined (with reasons) — it doubles as the review report.

## WP2 — I7: override, resume, aborts, invalidation (commit 2)

Spec A3.7, B4–B6; plan doc section "I7". Core owns the logic:

- Abort evaluation against the existing `AbortConditions` (casualties %,
  commander incapacitated, formation broken — defaults already modeled);
  commander death always aborts regardless of thresholds. Snapshot needs a
  captain-alive/commander-down field — extend `IFormationSnapshot` and the
  fakes.
- Suspend/override state per formation; resume picks the most recent stage
  whose trigger currently holds, else the current stage (B5).
- Invalidation (B6): directive target/anchor no longer evaluable → skip to
  the next stage whose trigger can still be evaluated; none → hold + notify.
- Monitor emits explicit events for all of it (PlanSuspended, PlanResumed,
  PlanAborted, StageSkipped or similar) — the engine relays, logs through
  RbpLog naming the commander (B4/B11: no silent deviation), and reverts
  aborted/suspended formations to vanilla AI (`SetControlledByAI(true)`).
- Engine override detection: research the vanilla order pipeline in the
  decompiled sources (e.g. `OrderController.OnOrderIssued`) for a hook that
  fires on PLAYER-issued orders to a planned formation; a Harmony patch is
  acceptable (first one — wire it into EngineContract). Resume entry: spec
  wants an order-menu entry; if writing Gauntlet/order-UI blind is too
  risky, ship console commands (`rbp.resume <formation>` / `rbp.resume all`)
  as the working path and leave the menu entry for the UI iterations —
  state the choice in the commit message and plan doc.
- Unit tests per the plan's verify step: resume-stage selection, death
  aborts despite thresholds, invalidation skip + hold-and-notify, abort
  thresholds honored, override suspends trigger evaluation.
- Update the plan doc I7 status: implemented, pending in-game verification
  (H2/H3 are manual, harness encoding waits for I12).

## WP3 — I8: Player Signal Palette (commit 3)

Spec A4 (player signal), B9; plan doc section "I8".

- `PlanMonitor.RaiseExternalSignal` already exists and is tested — the work
  is the input surface: fire any of the plan's ≤4 declared player signals in
  ≤2 inputs (R7).
- Primary: keybinds (check RTSCamera's hotkey registration pattern +
  `TaleWorlds.InputSystem` in the decompiled sources) — e.g. one chord/key
  per declared signal slot. Plus console command `rbp.signal <name>` as the
  always-works fallback and drill-cue mechanism (C7).
- Order-menu palette entries: same guidance as the I7 resume entry — attempt
  only if confident; otherwise document the deferral.
- In-battle feedback when a signal fires: battle message + RbpLog (B11).
- Validator: already caps at 4; add what's missing (e.g. duplicate names).
- Unit tests: palette-fired signal releases a gated stage identically to a
  stage-emitted signal (extend existing external-signal coverage for the
  declared-palette path); validator cases.
- Update the plan doc I8 status; note that B8 comms delay and D3 missed
  signals are Phase 2 — palette is instantaneous and reliable for now.

## Cross-cutting rules

- Every new engine member used → EngineContract check. Every new Harmony
  patch → contract check + graceful degradation (try/catch, plan disabled
  with a FAULT log, never a crash).
- Commit messages in the style of this repo's history: design decisions,
  deviations, test counts. They are the handoff document the local session
  reads before testing.
- Don't touch `Logs/` artifacts, known-good baselines, or `local.props`.
- Keep the harness pack green: if I7/I8 change event shapes, update the
  simulation + pack tests in the same commit as the change.
