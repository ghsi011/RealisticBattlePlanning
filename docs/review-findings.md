# Repo-wide review findings (Phase 1, 2026-06-17)

Six parallel review agents (Core/editing, execution/monitor, fidelity/harness,
engine integration, UI, spec-adherence). Triaged below. Working checklist for the
autonomous mission ([[autonomous-mission]]). Check items off as committed.

## Hardening — fixes/refactors/tests (Phase 1, before the 10 iterations)

Correctness (Core, unit-testable — no game needed):
- [x] **SetTrigger null-array throws** — fixed + capped + tests. (7d0bbfd)
- [x] **EnemyBroken permanent/spurious** — now latches observed-broken-while-alive; test revised + melee-kill test. (7d0bbfd)
- [~] **MoveTo fully-unresolvable** — DirectiveEvaluable already skips it (anchor/Path[0] guard); the zero-point-after-truncation edge is LOW and largely covered. Left as-is.
- [x] **Abort blocked while opening stage parked** — early-out now gated on Started/Pending. (7d0bbfd)
- [x] **Abort composure unclamped** — Math.Min(1f, …). (7d0bbfd)
- [x] **PlanFormatter drops secondary params** — EnemyCommits range, Follow offsets, width, non-FireControl fire policy + golden. (7d0bbfd)

Harness trust (Core + engine):
- [ ] **Fidelity seed never pinned** when arming → non-reproducible. (verification-heavy; do with a harness run)
- [ ] **BattleRecord lacks fidelity mode/seed** — stamp it; ResultsDiff refuse cross-mode diffs.

Refactors / cleanup:
- [x] Unify deep-copy: `PlanSerializer.DeepCopy<T>` used by EditingCopyOf + DuplicateStage. (7d0bbfd)
- [x] Remove dead VM `HasEmit`, `CanRemove`. (31f7f2d)
- [x] VM body-visibility: `UpdateBodyVisibility()`. (31f7f2d)
- [x] Harmony hook commented as deliberately-empty; AGENTS.md line fixed. (31f7f2d)
- [ ] Rename `PlanDraft.Build()` → a live-accessor name. (do with VM split)
- [ ] **VM split** (PlanningModeVM ~1190 lines) → `PlanMapVM` + `PlanPickerVM`. (pairs with click-to-place, iteration 1)

Test gaps (add):
- [x] Formatter goldens for the dropped secondary params. (7d0bbfd)
- [x] SetTrigger null/over-cap. (7d0bbfd)
- [ ] Default-spec table enumeration (needs DefaultTrigger/Directive moved to Core).
- [ ] AddStageToEach null args; SetDirective(f,i,null) no-op; EditorDefaults.SkirmishThenWithdraw valid.
- [ ] Harness determinism (pinned-seed identical; cross-mode mismatch); competence calibration.

## The 10 iterations (Phase 1 features, from the completeness backlog)

MUST-have for v1 (spec hard reqs / H-scenarios), roughly in dependency order:
1. **Click-to-place on the map** via a custom `PlanMapCanvasWidget : Widget` (override `OnMousePressed`, `GetLocalPoint` → normalize → `PlanMapProjection.Unproject`). Root cause of the earlier failure: zero-size item wrappers fail hit-test descent (`EventManager.CollectEnableWidgetsAt` gates on `IsPointInsideMeasuredArea`; a 0×0 `AreaRect` contains no point). The canvas owns clicks at full bounds; markers stay pure visuals. See [[planning-ui-direction]].
2. **`StageCompleted` event** in PlanEvents/PlanMonitor (prereq for D4). — DONE (iter 1, f10607a).
3. **Wire progression (D4)** — DONE (iter 4 Core `ProgressionService` seam bfd8e1c; iter 5 engine adapter 2fe9f34). `CommanderProgressionBehavior` owns the service per campaign; PlanMissionLogic maps formation→captain-hero, feeds monitor events (completed=XP, skipped/aborted=trickle), uses `ProfileFor` instead of `FromStats`, bumps battles-under-command; `HeroKilledEvent`→Forget. Core unit-tested (8); adapter compile-verified. **Awaiting the batched campaign-wiring review** (can't exercise save round-trip / death-loss in Custom Battle).
4. **Save persistence (G1)** — DONE (iter 5, part of `CommanderProgressionBehavior.SyncData`): record book ↔ single JSON string through `IDataStore`; corrupt-blob-safe; no SaveableTypeDefiner. Same review caveat.
5. **After-Action Report (B10)** — Core builder DONE (iter 2, e4124fa). UI screen still pending (Phase 2/3).
6. **Commander barks with attribution (B11/R2/H9)** — event → commander-named messages; no silent deviation.
7. **Remaining D3 fidelity dims** — trigger misjudgment, discipline break, signal miss (needed for H1-Untrained/H8).
8. **Per-formation HUD (B7)** — compact per-formation current-stage/pending-trigger/abort badge.
9. **MCM settings (F)** — settings class + master toggles (planning on/off, progression on/off); needs const→config.
10. **Presets + repeat-last-plan (A3.9)** — Core remap logic DONE (iter 3, b448c3e `PlanRemapper`). UI re-apply button pending.

LATER (Phase 2/3): Maneuver templates (E, large), Drills (C), stylized map (A2), onboarding (R5), RBM/RTS-Camera compat + perf pass (G4/R3).
