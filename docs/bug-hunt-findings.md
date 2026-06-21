# Bug-hunt findings (2026-06-20)

A parallel bug-hunt (two Claude investigators on the reported bugs + two Codex
general passes). This records what was **fixed** and the **deferred** findings so
they aren't lost.

## Fixed this session

- **Plan vanishes on "Ready" (reported).** `PlanMissionLogic._executor` was null
  when a plan was FIRST authored in-mission on a blank-start battle (`AfterStart`
  only inits the executor after its blank-start early-return; `ApplyPlan` rebuilt
  the monitor but never the executor). Adoption + the next tick NRE'd, the
  tick-fault nulled `_monitor`, and `ActivePlan` (gated on `_monitor`) went null →
  the reopened planner showed empty. Fix: `_executor ??= new FormationOrderExecutor()`
  in `ApplyPlan`; and `ActivePlan => _plan` (decoupled from `_monitor`) so a
  mid-battle monitor fault can never hide the user's plan from the editor.
- **Signal palette / "advance" not numpad-fireable (reported).** The numpad palette
  bound keys only to *declared* `PlayerSignals`, while the "advance" default of a
  `SignalReceived` trigger is referenced-but-never-declared, so no key fired it
  (only `rbp.signal` worked). Fix: `SignalPalette.Resolve` (Core, tested) builds the
  palette from declared ∪ every signal a trigger references; `PollSignalPalette`
  binds from it and logs a `Numpad N = signal` legend (+ warns on >4 signals).
- **Casualty baseline corrupted on re-Apply (Codex HIGH).** `AdoptPlannedFormations`
  overwrote `_initialCounts`/`_initialCaptains` every call, so re-applying a plan
  mid-battle reset the casualty baseline to already-reduced counts. Fix: capture the
  baseline only the first time (skip if the key exists).
- **`_rightDown` never self-healed (Codex MEDIUM).** Same class as the `_isMouseDown`
  bug — a missed right-release latched it and measured the next click from a stale
  press. Fix: clear `_rightDown` when the button isn't held.
- **`AddComponent(null)` if the flag mesh is ever missing (Codex LOW).** A future
  asset rename would NRE every tick. Fix: `if (mesh != null) e.AddComponent(mesh)`.

## Fixed 2026-06-21 (the deferred Core debt + the HIGH plan leak)

- **HIGH — Cross-session plan leak.** `SessionPlanStore` is now **keyed** to a session
  identity the engine derives (`"campaign:" + Campaign.UniqueGameId`, or one `"custom"`
  bucket for non-campaign battles): `Set(key, plan)` / `CurrentFor(key)` / `HasPlanFor(key)`.
  A plan carries across consecutive battles of the *same* game but reads as blank in a
  *different* game, so a campaign plan can't surface in a later custom battle (and
  vice-versa). The intended battle→battle carry is preserved (same key). 3 unit tests.
- **MEDIUM — `CasualtiesAbove` on an absent formation fired immediately.** The monitor
  now tracks which watched own-formation classes have actually fielded units
  (`_ownEverPresent`, updated each tick for the classes any CasualtiesAbove condition
  references). Absent = 100% casualties only when the formation was once present; a
  never-deployed one can't fire. The "vanished = total loss" case still works.
- **MEDIUM — `TimerElapsed` baseline stale on resume.** `FormationExecutionState.Resume`
  now re-baselines `ActivatedAtSeconds` to the resume moment, so a short timer on a
  later stage no longer reads as already-elapsed and skips the stage being resumed into
  — the timer re-runs from the resume.
- **MEDIUM — Click-to-march chains stalled under fidelity drift.** `PositionReached`
  now widens its arrival tolerance by the active stage's positional drift
  (`state.ActiveFidelity.PositionErrorMeters`), so a drifted formation still registers
  as "reached" and the chain advances. Pass-through drift is 0, so the fidelity-off
  baseline is byte-identical.
- **MEDIUM — Validator gaps.** `CasualtiesAbove` now errors on a non-class selector
  (`"Player"`/`"Nearest"`/typo — null still means "this formation"); `PositionReached`
  errors on `toleranceMeters <= 0`.

## Deferred (real, but riskier or lower-value — fix deliberately)

- **MEDIUM — Enemy formation id scheme** (`MissionSnapshot.cs:91`,
  `TeamIndex*16 + FormationIndex`) is safe for field battles (≤16 enemy teams) but
  fragile; prefer a struct key or assert.
- **LOW — `SessionPlanStore.Copy` returns the original on a DeepCopy failure**
  (aliasing). Deliberate ("don't lose the plan on a hiccup") and DeepCopy never fails
  on plain plan data; left as-is, noted.
- **LOW — Redundant `CarryFidelity(Perfect)` before a hold-skip** in `PlanMonitor.TryAdvance`
  — harmless double-reset; left as-is.

## UX follow-ups noted
- No in-battle HUD legend for the numpad palette (the binding is now logged to
  `rbp.log`, but not shown on screen).
- `SignalReceived` vs `PlayerSignal` triggers are both labelled "Signal" in the
  editor and the signal picker only offers *declared* signals — a player-driven gate
  is easy to mis-author. The palette fix makes any referenced signal fireable, but
  the editor distinction could be clearer.
