# Repo-wide review findings (Phase 1, 2026-06-17)

Six parallel review agents (Core/editing, execution/monitor, fidelity/harness,
engine integration, UI, spec-adherence). Triaged below. Working checklist for the
autonomous mission ([[autonomous-mission]]). Check items off as committed.

## Hardening — fixes/refactors/tests (Phase 1, before the 10 iterations)

Correctness (Core, unit-testable — no game needed):
- [ ] **SetTrigger null-array throws** (PlanDraft) — `SetTrigger(f,i,null)` NREs; violates the no-throw contract. Guard `conditions != null`; cap at MaxTriggerConditions. + test.
- [ ] **EnemyBroken permanent/spurious** (PlanMonitor) — inferred from disappearance, never pruned; conflates kill with rout; reinforcement reusing a slot id reads stale. Latch observed-broken-while-alive; prune on re-seen-alive. + tests (wiped-then-reinforced must read false). NOTE: a current test asserts the old behavior — revise it.
- [ ] **MoveTo fully-unresolvable activates into a [FAULT] log** for an author error (R2). Treat as inevaluable → skip with StageSkipped. + test.
- [ ] **Abort blocked while opening stage parked** (currently unreachable, but latent) — gate the `ActiveStageIndex < 0` abort early-out on `Started` instead. + test.
- [ ] **Abort composure unclamped** — `Math.Min(1f, composure)` so a future >1 config can't make units fight past the limit.
- [ ] **PlanFormatter drops secondary params** — EnemyCommits range/sustain/speed; Hold+Fire policy; Follow offsets; EnemyWithinDistance tolerance. Render them. + formatter golden tests.

Harness trust (Core + engine):
- [ ] **Fidelity seed never pinned** when arming → fidelity-gated scenarios non-reproducible. Pin a default seed on arm; store in ScenarioSpec.
- [ ] **BattleRecord lacks fidelity mode/seed** — stamp it; ResultsDiff refuse cross-mode diffs.

Refactors / cleanup:
- [ ] Unify deep-copy: one `DeepCopy<T>` (PlanSerializer dialect) used by EditingCopyOf AND DuplicateStage.
- [ ] Remove dead code: VM `HasEmit`, `CanRemove`. Collapse `IsRemoveDisabled`/`IsUncommanded` vs `!HasStages` if clean.
- [ ] VM body-visibility: one `UpdateBodyVisibility()` for Refresh + ExecuteToggleMap.
- [ ] Remove the dead `Harmony.PatchAll` ceremony in SubModule (no patches exist) or comment it as a deliberate empty hook; fix the AGENTS.md "Harmony… registered" line.
- [ ] Rename `PlanDraft.Build()` → a live-accessor name (it returns the live `_plan`, mutated in place by the VM) OR document loudly.
- [ ] **VM split** (PlanningModeVM ~1190 lines) → extract `PlanMapVM` (map projection/markers/geometry) and `PlanPickerVM` (the modal picker engine). High value; pairs with the click-to-place widget.

Test gaps (add):
- [ ] Default-spec table: enumerate every TriggerType/DirectiveType, assert `DefaultTrigger`/`DefaultDirective` produces a spec that VALIDATES (catches a future enum addition that yields an un-appliable plan).
- [ ] Formatter goldens for the dropped secondary params.
- [ ] SetTrigger null/over-cap; AddStageToEach null args; SetDirective(f,i,null) no-op.
- [ ] EditorDefaults.SkirmishThenWithdraw builds valid.
- [ ] Harness determinism at integration: two pinned-seed runs identical; ResultsDiff cross-mode mismatch.
- [ ] Competence calibration: representative vanilla skills → sensible tiers.

## The 10 iterations (Phase 1 features, from the completeness backlog)

MUST-have for v1 (spec hard reqs / H-scenarios), roughly in dependency order:
1. **Click-to-place on the map** via a custom `PlanMapCanvasWidget : Widget` (override `OnMousePressed`, `GetLocalPoint` → normalize → `PlanMapProjection.Unproject`). Root cause of the earlier failure: zero-size item wrappers fail hit-test descent (`EventManager.CollectEnableWidgetsAt` gates on `IsPointInsideMeasuredArea`; a 0×0 `AreaRect` contains no point). The canvas owns clicks at full bounds; markers stay pure visuals. See [[planning-ui-direction]].
2. **`StageCompleted` event** in PlanEvents/PlanMonitor (prereq for D4).
3. **Wire progression (D4)** — CommanderRecordBook in PlanMissionLogic; award XP from monitor events; `ProfileFor` (familiarity + drill cap) instead of `FromStats`; death → Forget.
4. **Save persistence (G1)** — CampaignBehavior + SyncData for the record book; mod-removal-safe.
5. **After-Action Report (B10)** — Core builder over BattleRecord (planned-vs-actual, deviations by tag, XP deltas) + screen.
6. **Commander barks with attribution (B11/R2/H9)** — event → commander-named messages; no silent deviation.
7. **Remaining D3 fidelity dims** — trigger misjudgment, discipline break, signal miss (needed for H1-Untrained/H8).
8. **Per-formation HUD (B7)** — compact per-formation current-stage/pending-trigger/abort badge.
9. **MCM settings (F)** — settings class + master toggles (planning on/off, progression on/off); needs const→config.
10. **Presets + repeat-last-plan (A3.9)** — Core remap logic (testable) + UI.

LATER (Phase 2/3): Maneuver templates (E, large), Drills (C), stylized map (A2), onboarding (R5), RBM/RTS-Camera compat + perf pass (G4/R3).
