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

## Deferred (real, but riskier or lower-value — fix deliberately)

- **HIGH — Cross-session plan leak.** `SessionPlanStore` is static and never cleared,
  so a plan from one game leaks into the first plannable battle of a *different* game
  in the same process (e.g. campaign plan showing in a later custom battle). NOT a
  simple "clear on OnGameStart": that same never-clears behaviour is *why* the
  intended battle→battle carry works across consecutive custom battles (each a new
  game). The correct fix keys the carry to the game/campaign identity (or clears only
  on a real session boundary) — needs care to not break the verified carry.
- **MEDIUM — `CasualtiesAbove` on an absent formation fires immediately**
  (`PlanMonitor.cs:693`). A formation that was never present reads as 100% casualties
  (same as "existed and now gone"), so a cross-formation casualty trigger watching an
  un-deployed formation advances at battle start. Fix needs the monitor to track
  "ever present" to distinguish never-deployed from wiped-out.
- **MEDIUM — `TimerElapsed` baseline stale on resume** (`PlanMonitor` resume path). A
  resumed formation with a short timer can read elapsed on the first post-resume tick
  and skip the stage it was re-entered into. Fix: re-baseline the timer on resume.
- **MEDIUM — Click-to-march chains can stall under fidelity drift**
  (`MapAuthoring.cs:43`). `PositionReached` tolerance may be below the configured
  drift, so the next waypoint never triggers. Only with fidelity ON (default OFF).
  Fix: widen tolerance to cover drift, or chain off stage-completion not arrival.
- **MEDIUM — Validator gaps.** `CasualtiesAbove` doesn't validate its formation
  selector (a typo or `"Player"` passes, then silently never fires); `PositionReached`
  accepts `toleranceMeters <= 0`. Add validator errors/warnings.
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
