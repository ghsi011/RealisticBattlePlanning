# Implementation Plan

Derived from [the spec](../bannerlord-battle-planning-mod-spec.md). The spec is the
WHAT; this document is the order of work. The spec's advisory milestones M1–M5
are regrouped into three phases; Phase 1 is a playable MVP with **no training,
no progression, no maneuver templates** — every commander executes at a fixed
fidelity.

**Resolved (2026-06-11):** the v0.1 launch crash was the assembly targeting
`net6.0` while the Steam build of Bannerlord hosts the .NET Framework CLR
(crash log: unresolved `System.Runtime 6.0` → `0xE0434352`). Retargeted to
`net472`. Pending one in-game confirmation launch.

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

- **Fidelity is fixed.** A `IFidelityModel` seam exists from day one with a
  pass-through implementation (perfect execution, or a single configurable
  tier). Phase 2 swaps in the real model — no Phase 1 code rewritten.
  This is also the spec's "progression off" master toggle (F), built first.
- **UI is the fallback presentation** (A2.5): vanilla deployment top-down
  camera + Gauntlet overlay. The stylized battle map is Phase 3.
- **Friction reducers** (A3.9): only the "suggested opening stage" default
  ships in Phase 1. Presets and repeat-last-plan land in Phase 3 (they're
  required for v1 *release*, not for the MVP).
- **Debug plan file**: until the editor exists (Iteration 7), plans are
  authored in a JSON file and loaded at battle start. It stays forever as a
  dev/test tool, and its event log is the seed of Phase 2's AAR and the
  R2 "fidelity noise vs. genuine bug" debug log.

### Front-loaded risks

1. **Team-AI suppression** (B1) — keeping the general AI's tactic system from
   overriding planned formations is the make-or-break engine unknown. It is
   Iteration 2, immediately after the data model. Reference: RTSCamera's
   formation/order patches (`Patch_MissionOrderTroopControllerVM`,
   `Patch_OrderTroopPlacer`).
2. **Custom formation behaviors** (feign retreat, missile-only flank arc,
   rear guard) — probed in Iteration 4 right after vanilla-relative ones.
3. **Deployment-phase UI injection** — Iteration 7; RTSCamera's
   `Patch_DeploymentMissionController` and mission view registration are the
   pattern.

---

## Phase 1 iterations

Each iteration ends green: builds clean, deploys, and its verify step passes
in-game. Earlier iterations' verify steps keep passing (manual regression).

### I1 — Mission gating, infrastructure, plan model

Foundation; no visible behavior change.

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

### I2 — Plan Monitor + AI suppression (the risk spike)

The engine make-or-break. Smallest possible trigger/directive set, deepest
engine question.

- Plan Monitor ticking a few times per second (B2); stage advancement.
- Suppress team-level general AI for plan-governed formations only; issue
  movement/arrangement orders programmatically through the order system.
- Triggers: *On battle start*, *Timer elapsed*, *Own position reached*.
  Directives: *Hold position* (arrangement, facing, width), *Move to*
  (anchor/waypoints), *Charge*.
- **Verify:** debug plan walks a formation through a 3-waypoint path, holds in
  shieldwall 30 s, then charges — while the vanilla general AI never touches
  it and unplanned formations behave vanilla.
- Spec: B1, B2, A4/A5 (subset).

### I3 — Signal bus + full trigger vocabulary

- Signal bus: stages emit named signals on activation; *Signal received*
  trigger; AND-composition of up to 3 atomic conditions (A3.5).
- Remaining triggers: *Enemy commits to attack* (sustained-approach heuristic
  with configurable defaults — tune later against RBM, R3), *Enemy/Friendly
  formation within distance*, *Casualties above*, *Enemy broken/fleeing*.
- "Player's formation" usable as the reference formation in triggers (A3.10).
- All thresholds/defaults data-driven in config (D3 prep).
- **Verify:** two-formation debug plan coordinates via signal; distance and
  enemy-commit triggers fire at sane moments (log timestamps vs. observation).
- Spec: A3.4, A3.5, A4.

### I4 — Full directive vocabulary

- Vanilla-relative first (reuse/subclass `BehaviorComponent`s, per 1.1):
  *Skirmish/Harass*, *Pull back*, *Follow/Escort*, *Hold/Free fire*.
- Custom behaviors: *Feign retreat toward* (fire-while-withdrawing flag),
  *Flank arc* (side, standoff, **missile-only**), *Screen/Rear guard*
  (delaying posture).
- **Verify:** the canonical A6 Feigned Retreat runs end-to-end from a debug
  plan file against an aggressive AI army — the MVP's "it actually works"
  moment. Each directive also gets a one-formation smoke plan.
- Spec: A5, A6.

### I5 — Override, resume, aborts, invalidation

- Any manual player order suspends that formation's plan; *Resume plan* entry
  in the order menu picks the most appropriate stage (B5).
- Abort conditions with editable defaults: casualties %, commander
  incapacitated, formation broken (A3.7); on abort revert to vanilla AI +
  notification (B4). Commander death always aborts.
- Situational invalidation: dead target/unreachable anchor → skip to next
  evaluable stage, else hold + notify (B6).
- **Verify:** H2 (override & resume) and H3 (abort on commander death) pass.
- Spec: A3.7, B4–B6.

### I6 — Player Signal Palette

- Plans declare up to 4 player signals; *Player signal* trigger type.
- Palette: order-menu entries + optional direct keybinds, fireable in ≤2
  inputs (R7); routes through the signal bus like any stage-emitted signal.
- **Verify:** H8 core — infantry charge gated solely on player signal
  "hammer"; fire it mid-battle, charge begins promptly.
- Spec: A4 (player signal), B9.

### I7 — Planning Mode UI: core editor

The biggest UI lift; primary authoring path replaces the debug file.

- Enter/exit Planning Mode during deployment (keybind + button), time frozen
  (A1.1); confirming starts the battle (A1.3). Skipping = pure vanilla (A1.2).
- Formation list panel; select a formation → its Stage List editor: add/
  remove/reorder stages, trigger & directive dropdowns with parameter fields,
  signal naming, abort-condition editing.
- Map anchors and waypoint paths placed by ground-pick with the existing
  deployment camera (A2.4 minimal).
- New formations get the default Stage 1 "hold position, current facing"
  (A3.9 — the one friction reducer in scope).
- **Verify:** author the full A6 plan in the UI alone (no file) and it executes
  identically to the I4 file version. No time budget yet.
- Spec: A1, A3.1–A3.3, A2.5 fallback.

### I8 — Editor completion: multi-select, warnings, polish

- Multi-select formations → author one shared stage cloned per formation
  (A3.6, the "instruct the 2 HA commanders together" story).
- Feasibility warnings, non-blocking: contradictory parameters only (e.g.
  flank standoff > weapon range) — competence warnings arrive with Phase 2
  (A3.8).
- Plain-language stage summaries ("When the enemy charges us → fall back
  behind the line, firing") and parameter defaults everywhere (R4).
- Player's own formation plannable (movement/arrangement directives, A3.10).
- **Verify:** A6 plan authored in under 6 minutes manually (H1's manual
  budget); multi-select path used for the two HA formations.
- Spec: A3.6, A3.8 (partial), A3.9 (partial), A3.10, R4 (partial).

### I9 — Battlefield HUD & notifications

- Per-planned-formation HUD element: current stage, pending trigger,
  override/abort badge (B7). HUD verbosity config (full/minimal/off — F).
- Every plan event the player can perceive gets a battle message naming the
  formation/commander: stage transitions, aborts, overrides, resumes, signal
  receipts (the no-silent-deviation rule, B11 — text channel only; the
  in-character bark *flavor* comes with Phase 2 fidelity events).
- **Verify:** play H1 watching only the HUD; every transition is legible; H8
  message trail shows signal → response.
- Spec: B7, B11 (channel), F (HUD verbosity).

### I10 — MVP hardening & acceptance pass

- Run the full Phase 1 acceptance set: H1 (fixed fidelity, manual authoring),
  H2, H3, H7, H8 core — each 3×, on at least 2 scene types.
- Performance: trigger evaluation profiled in a 1000-agent battle (B2 —
  target no measurable frame cost).
- Zero-touch audit (G3): with no plan authored, no codepath touches formation
  AI; siege/hideout entry points verified absent (G5).
- Config-file pass: all thresholds, defaults, and toggles introduced so far
  live in one documented config (F groundwork).
- **Verify:** acceptance checklist in this doc ticked, with notes per run.

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
Acceptance: tiered H1, H4, H6, H9; pacing targets sanity-checked (D4).

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
Acceptance: H5, H10, H1 in <3 min via template, full suite re-run on vanilla
and RBM AI.
