using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>Per-formation plan execution mode (B4/B5).</summary>
    public enum FormationPlanMode
    {
        Active,
        Suspended,
        Aborted,
    }

    /// <summary>
    /// The stage machine (spec B2): evaluates triggers over battlefield
    /// snapshots and advances each formation through its stages. Pure logic —
    /// the engine feeds it snapshots a few times per second and turns the
    /// returned events into orders. A formation executes its active stage's
    /// directive and advances when the NEXT stage's trigger fires (A3.3);
    /// an exhausted stage list holds its last directive.
    ///
    /// Signals (A3.4, B9) are latched: once raised they stay raised for the
    /// battle, and signals raised during a tick become visible the NEXT tick,
    /// so evaluation order within a tick never matters. Player signals enter
    /// through <see cref="RaiseExternalSignal"/>.
    /// </summary>
    public sealed class PlanMonitor
    {
        private readonly BattlePlan _plan;
        private readonly List<FormationExecutionState> _states;
        private readonly HashSet<string> _signals = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _pendingSignals = new();
        private readonly Dictionary<(string RefKey, int EnemyId), ApproachState> _approach = new();
        private readonly Dictionary<(TriggerSpec Spec, int EnemyId), float> _sustainedSince = new();
        // Enemy ids seen BROKEN while alive (with their class), latched until they
        // return alive-and-unbroken. EnemyBroken (A4 "routs") reads this rather than
        // inferring rout from disappearance — a formation cut down in melee never
        // "broke", and a reinforcement reusing a slot id isn't broken.
        private readonly Dictionary<int, PlannedFormationClass?> _observedBroken = new();
        private readonly HashSet<string> _warnedConditions = new(StringComparer.Ordinal);
        private readonly HashSet<int> _aliveEnemyIds = new();
        private readonly List<(TriggerSpec Spec, int EnemyId)> _staleSustainKeys = new();
        private readonly List<(string RefKey, int EnemyId)> _staleApproachKeys = new();
        private readonly HashSet<PlannedFormationClass> _pendingOverrides = new();
        private readonly HashSet<PlannedFormationClass> _pendingResumes = new();
        private readonly IFidelityModel _fidelity;
        private readonly Random _rng;
        private readonly Dictionary<PlannedFormationClass, CommanderProfile> _commanders = new();
        private float _battleStartSeconds;
        /// <summary>Battle axis captured at start — anchors and steering share it; the per-tick snapshot direction drifts as armies maneuver.</summary>
        private MapVec _attackAxis = new(0f, 1f);

        /// <param name="fidelity">How competence becomes execution deviation (D3). Null = pass-through (perfect; the Phase-1 / progression-off default), so existing callers are unchanged.</param>
        /// <param name="seed">Seeds the fidelity rolls so a battle replays identically and tests assert exact-per-seed outcomes.</param>
        public PlanMonitor(BattlePlan plan, IFidelityModel fidelity = null, int seed = 20260613)
        {
            _plan = plan;
            _fidelity = fidelity ?? new PassThroughFidelityModel();
            _rng = new Random(seed);
            _states = plan.Formations
                .Where(f => f.Stages.Count > 0)
                .Select(f => new FormationExecutionState(f))
                .ToList();
        }

        /// <summary>
        /// Sets the commander competence the fidelity model rolls against for
        /// a formation (D1). Unset formations roll against
        /// <see cref="CommanderProfile.Default"/>. The engine calls this at
        /// battle start from each formation's captain.
        /// </summary>
        public void SetCommander(PlannedFormationClass formation, CommanderProfile commander)
        {
            if (commander != null)
                _commanders[formation] = commander;
        }

        private CommanderProfile Commander(PlannedFormationClass formation)
            => _commanders.TryGetValue(formation, out var commander) ? commander : CommanderProfile.Default;

        public bool Started { get; private set; }

        public ResolvedAnchors Anchors { get; private set; }

        /// <summary>
        /// Raises a signal from outside the plan (Signal Palette player
        /// signals, drill cues). Latches like any stage-emitted signal,
        /// visible from the next tick.
        /// </summary>
        public void RaiseExternalSignal(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return;
            _pendingSignals.Add(signal);
            RbpLog.Info($"External signal '{signal}' raised.");
        }

        /// <summary>
        /// A manual player order hit this formation (B5). Latched; the plan
        /// suspends on the next tick so the monitor never re-steers over the
        /// player's order.
        /// </summary>
        public void NotifyPlayerOverride(PlannedFormationClass formation)
        {
            _pendingOverrides.Add(formation);
        }

        /// <summary>"Resume plan" for one formation (B5). Latched; the stage
        /// is selected against the next tick's snapshot.</summary>
        public void RequestResume(PlannedFormationClass formation)
        {
            _pendingResumes.Add(formation);
        }

        public FormationPlanMode GetMode(PlannedFormationClass formation)
        {
            foreach (var state in _states)
            {
                if (state.Plan.Formation == formation)
                    return state.Mode;
            }
            return FormationPlanMode.Active;
        }

        /// <summary>True when this formation has stages in the plan.</summary>
        public bool Governs(PlannedFormationClass formation)
        {
            foreach (var state in _states)
            {
                if (state.Plan.Formation == formation)
                    return true;
            }
            return false;
        }

        public IReadOnlyList<PlanEvent> Tick(IBattlefieldSnapshot snapshot)
        {
            var events = new List<PlanEvent>();

            if (!Started)
            {
                if (!snapshot.BattleStarted)
                    return events;

                Started = true;
                _battleStartSeconds = snapshot.TimeSeconds;
                _attackAxis = snapshot.AttackDirection.Normalized();
                Anchors = ResolvedAnchors.FromSnapshot(_plan.Anchors, snapshot);
            }

            // Signals raised last tick become visible now.
            foreach (var signal in _pendingSignals)
                _signals.Add(signal);
            _pendingSignals.Clear();

            PurgeVanishedEnemyState(snapshot);
            RegisterLateStartPositions(snapshot);
            ProcessOverrides(events);
            ProcessResumes(snapshot, events);

            foreach (var state in _states)
            {
                if (state.Mode != FormationPlanMode.Active)
                    continue;

                if (EvaluateAborts(state, snapshot, events))
                    continue;

                bool activatedThisTick;
                if (state.PendingStageIndex >= 0)
                {
                    // A triggered stage is waiting out the commander's reaction
                    // delay (D3); the formation keeps doing its current thing
                    // until the lag elapses, then the stage activates.
                    activatedThisTick = false;
                    if (snapshot.TimeSeconds >= state.PendingActivateAt)
                    {
                        ActivateChecked(state, state.TakePendingStage(), snapshot, events);
                        activatedThisTick = true;
                    }
                }
                else
                {
                    activatedThisTick = TryAdvance(state, snapshot, events);
                }

                if (!activatedThisTick)
                    TickWaypointProgression(state, snapshot, events);
                TickSteering(state, snapshot, events);
            }

            return events;
        }

        private void ProcessOverrides(List<PlanEvent> events)
        {
            if (_pendingOverrides.Count == 0)
                return;

            foreach (var state in _states)
            {
                if (!_pendingOverrides.Contains(state.Plan.Formation))
                    continue;
                if (state.Mode != FormationPlanMode.Active)
                    continue;

                state.Suspend(); // also drops any queued reaction — that was the plan's, not the player's
                events.Add(new PlanSuspended(state.Plan.Formation));
            }

            _pendingOverrides.Clear();
        }

        private void ProcessResumes(IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            if (_pendingResumes.Count == 0)
                return;

            foreach (var state in _states)
            {
                if (!_pendingResumes.Contains(state.Plan.Formation))
                    continue;

                if (state.Mode == FormationPlanMode.Aborted)
                {
                    RbpLog.Warn($"[{state.Plan.Formation}] cannot resume: its plan aborted.");
                    continue;
                }
                if (state.Mode != FormationPlanMode.Suspended)
                    continue;

                // The battle moved on while the player had the formation: an
                // abort condition that arose under manual control still ends
                // the plan instead of resuming it.
                if (EvaluateAborts(state, snapshot, events))
                    continue;

                state.Resume(); // clean re-adoption: clears any carried reaction
                var stageIndex = SelectResumeStage(state, snapshot);
                events.Add(new PlanResumed(state.Plan.Formation, stageIndex));
                ActivateChecked(state, stageIndex, snapshot, events);
            }

            _pendingResumes.Clear();
        }

        /// <summary>
        /// B5: the most recent stage whose trigger currently holds, else the
        /// stage that was active at suspension (or the first stage if the
        /// plan never started). TimerElapsed is measured from the last
        /// activation before the override — an approximation, stated in the
        /// plan doc.
        /// </summary>
        private int SelectResumeStage(FormationExecutionState state, IBattlefieldSnapshot snapshot)
        {
            for (var i = state.Plan.Stages.Count - 1; i >= 0; i--)
            {
                var stage = state.Plan.Stages[i];
                if (stage.When.Count == 0)
                {
                    if (i == 0)
                        return 0;
                    continue;
                }

                if (AllConditionsHold(stage.When, state, snapshot, readOnly: true))
                    return i;
            }

            return state.ActiveStageIndex < 0 ? 0 : state.ActiveStageIndex;
        }

        /// <summary>True when the formation just aborted (B4/A3.7). Only
        /// started plans abort; commander death aborts unconditionally.</summary>
        private bool EvaluateAborts(FormationExecutionState state, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            // A formation can abort once it has started executing — including while
            // an opening stage's reaction delay is still parked (PendingStageIndex set
            // but no stage active yet): commander-death/casualties/broken don't depend
            // on which stage is live.
            if (state.ActiveStageIndex < 0 && state.PendingStageIndex < 0)
                return false;

            var abort = state.Plan.Abort;
            var own = snapshot.GetOwn(state.Plan.Formation);
            string reason = null;

            if (own is { Exists: true })
            {
                // Abort composure (D3): a green commander pulls out before the
                // configured casualty limit; pass-through leaves it at 1.0. Clamped to
                // ≤ 1 so a future config can never make a unit fight PAST the limit.
                var composure = System.Math.Min(1f, _fidelity.AbortComposure(Commander(state.Plan.Formation)));
                var casualtyLimit = abort.CasualtiesAbovePercent * composure;

                // Unconditional on purpose: commander death always aborts in
                // Phase 1; AbortConditions.OnCommanderIncapacitated is
                // reserved for Phase 2's incapacitated-but-alive distinction
                // (the validator warns when it is set to false).
                if (own.CommanderDown)
                    reason = "commander down";
                else if (own.CasualtiesPercent >= casualtyLimit)
                    reason = composure < 1f
                        ? Invariant($"[INTENDED_FIDELITY] casualties {own.CasualtiesPercent:0.#}% (early limit {casualtyLimit:0.#}% = {abort.CasualtiesAbovePercent:0.#}% × {composure:0.##} composure)")
                        : Invariant($"casualties {own.CasualtiesPercent:0.#}% (threshold {abort.CasualtiesAbovePercent:0.#}%)");
                else if (own.IsBroken && abort.OnFormationBroken)
                    reason = "formation broke";
            }
            else
            {
                reason = "formation wiped out";
            }

            if (reason == null)
                return false;

            state.Abort();
            events.Add(new PlanAborted(state.Plan.Formation, reason));
            return true;
        }

        /// <summary>
        /// Whether a directive can be carried out right now: its anchor
        /// resolves and its live reference (enemy / friendly) exists.
        /// </summary>
        private bool DirectiveEvaluable(DirectiveSpec spec, FormationExecutionState state, IBattlefieldSnapshot snapshot, out string reason)
        {
            reason = null;
            if (spec == null)
                return true;

            switch (spec.Type)
            {
                case DirectiveType.MoveTo:
                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack:
                {
                    if (spec.Path is { Count: > 0 })
                    {
                        if (Anchors.Resolve(state.Plan.Formation, spec.Path[0]) != null)
                            return true;
                        reason = $"waypoint '{spec.Path[0]}' unresolvable";
                        return false;
                    }
                    if (Anchors.Resolve(state.Plan.Formation, spec.Anchor) != null)
                        return true;
                    reason = $"anchor '{spec.Anchor}' unresolvable";
                    return false;
                }

                case DirectiveType.Skirmish:
                case DirectiveType.FlankArc:
                {
                    var own = snapshot.GetOwn(state.Plan.Formation);
                    var from = own is { Exists: true } ? own.Position : default;
                    if (NearestEnemy(spec.Target, from, snapshot) != null)
                        return true;
                    reason = spec.Target == null ? "no enemies left" : $"no '{spec.Target}' enemies left";
                    return false;
                }

                case DirectiveType.Screen:
                case DirectiveType.Follow:
                {
                    if (ResolveFriendlyPosition(spec.Target, snapshot) != null)
                        return true;
                    reason = $"'{spec.Target}' is gone";
                    return false;
                }

                default:
                    return true;
            }
        }

        /// <summary>
        /// Activates the first evaluable stage at or after the given index;
        /// inevaluable ones are skipped (B6). When none remains, the
        /// formation holds in place and says so — never a silent stall.
        /// Pinned semantics (do not "fix" to arming): the skipped-to stage
        /// activates IMMEDIATELY — its trigger is bypassed and its timers
        /// re-baseline at skip time. Phase 2 reaction delays layer on top of
        /// activation, never on trigger arming.
        /// </summary>
        private void ActivateChecked(FormationExecutionState state, int startIndex, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            for (var i = startIndex; i < state.Plan.Stages.Count; i++)
            {
                var stage = state.Plan.Stages[i];
                if (DirectiveEvaluable(stage.Do, state, snapshot, out var reason))
                {
                    // Skipping past the requested stage means the plan is
                    // adapting (B6), not carrying out the reaction that was
                    // rolled for the now-gone stage — so that stage's drift
                    // must not leak onto its replacement. (Closes the
                    // pending-activation path, whose caller can't pre-reset
                    // the way the resume/steering-skip paths do.)
                    if (i != startIndex)
                        state.CarryFidelity(FidelityProfile.Perfect);
                    Activate(state, i, stage, snapshot, events);
                    return;
                }
                events.Add(new StageSkipped(state.Plan.Formation, i, reason));
            }

            // Skipped every remaining stage into a hold (EnterHold also drops
            // the now-irrelevant rolled reaction).
            var holdDirective = new ResolvedDirective(new DirectiveSpec { Type = DirectiveType.Hold }, null, null);
            state.EnterHold(Math.Min(startIndex, state.Plan.Stages.Count - 1), snapshot.TimeSeconds, holdDirective);
            events.Add(new PlanHolding(state.Plan.Formation, "no evaluable stage remains"));
        }

        /// <summary>
        /// Drops approach/sustain state for enemy ids no longer on the field.
        /// Engine ids are team/slot based, so a reinforcement wave reuses the
        /// id of a wiped formation — without the purge it would inherit the
        /// old wave's approach history and fire EnemyCommits without
        /// re-sustaining (or with a fabricated closing speed).
        /// </summary>
        private void PurgeVanishedEnemyState(IBattlefieldSnapshot snapshot)
        {
            _aliveEnemyIds.Clear();
            foreach (var enemy in snapshot.Enemies)
            {
                _aliveEnemyIds.Add(enemy.Id);
                // Latch a rout the moment we see it; clear it if the slot returns
                // alive-and-unbroken (rallied, or a fresh wave reusing the id). An id
                // that vanishes keeps whatever latch it had (broke-then-fled stays
                // broken; killed-without-routing was never latched).
                if (enemy.IsBroken)
                    _observedBroken[enemy.Id] = enemy.Class;
                else
                    _observedBroken.Remove(enemy.Id);
            }

            if (_sustainedSince.Count == 0 && _approach.Count == 0)
                return;

            _staleSustainKeys.Clear();
            foreach (var key in _sustainedSince.Keys)
            {
                if (!_aliveEnemyIds.Contains(key.EnemyId))
                    _staleSustainKeys.Add(key);
            }
            foreach (var key in _staleSustainKeys)
                _sustainedSince.Remove(key);

            _staleApproachKeys.Clear();
            foreach (var key in _approach.Keys)
            {
                if (!_aliveEnemyIds.Contains(key.EnemyId))
                    _staleApproachKeys.Add(key);
            }
            foreach (var key in _staleApproachKeys)
                _approach.Remove(key);
        }

        /// <summary>
        /// OwnStart geometry for a planned formation that had no units at
        /// battle start (reinforcement waves): its basis is wherever it first
        /// appears. Until then its stages stay unactivated (see TryAdvance) —
        /// a plan can't govern troops that aren't on the field.
        /// </summary>
        private void RegisterLateStartPositions(IBattlefieldSnapshot snapshot)
        {
            foreach (var state in _states)
            {
                if (Anchors.HasStartPosition(state.Plan.Formation))
                    continue;
                if (snapshot.GetOwn(state.Plan.Formation) is { Exists: true } own)
                {
                    Anchors.RegisterLateStart(state.Plan.Formation, own.Position);
                    RbpLog.Info($"[{state.Plan.Formation}] arrived late; OwnStart anchors resolve from {own.Position}.");
                }
            }
        }

        private bool TryAdvance(FormationExecutionState state, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            var nextIndex = state.ActiveStageIndex + 1;
            if (nextIndex >= state.Plan.Stages.Count)
                return false;

            // A formation that never fielded units doesn't start its plan;
            // its first stage activates when (if) it appears.
            if (state.ActiveStageIndex < 0 && snapshot.GetOwn(state.Plan.Formation) is not { Exists: true })
                return false;

            var stage = state.Plan.Stages[nextIndex];
            if (!TriggerFires(stage, state, snapshot))
                return false;

            // Already holding (B6): only leave the hold when some remaining
            // stage became evaluable again — otherwise the same skip/hold
            // events would re-emit every tick the trigger stays true.
            if (state.Holding && !AnyStageEvaluableFrom(state, nextIndex, snapshot))
                return false;

            // Nothing from here is evaluable: there's nothing to react to, so
            // skip straight to the hold without rolling — no phantom
            // ReactionDelayed before a PlanHolding (no activation follows it).
            if (!AnyStageEvaluableFrom(state, nextIndex, snapshot))
            {
                state.CarryFidelity(FidelityProfile.Perfect);
                ActivateChecked(state, nextIndex, snapshot, events);
                return true;
            }

            // Reaction delay (D3): a Master commander acts at once; a green one
            // lags. A positive delay parks the stage as pending and keeps the
            // current directive running until it elapses (so this is NOT an
            // activation this tick — return false). The opening posture (first
            // stage on battle start) is deployment, not a reaction to the
            // field, so it activates immediately — the lag is for responding
            // to triggers.
            var isOpeningPosture = state.ActiveStageIndex < 0 && stage.When.Count == 0;
            var fidelity = isOpeningPosture ? FidelityProfile.Perfect : RollFidelity(state, nextIndex, events);
            if (fidelity.ReactionDelaySeconds > 0f)
            {
                state.ParkReaction(nextIndex, snapshot.TimeSeconds + fidelity.ReactionDelaySeconds, fidelity);
                return false;
            }

            state.CarryFidelity(fidelity);
            ActivateChecked(state, nextIndex, snapshot, events);
            return true;
        }

        /// <summary>
        /// Rolls the commander's fidelity for activating a stage — reaction
        /// delay (parked here) and positional drift (applied at activation),
        /// from one roll so a battle replays consistently. Emits the
        /// INTENDED_FIDELITY reaction event when there is a lag. Pass-through
        /// (the default) returns Perfect — no delay, no drift, no rng draw —
        /// so the pre-fidelity behaviour is byte-for-byte unchanged.
        /// </summary>
        private FidelityProfile RollFidelity(FormationExecutionState state, int stageIndex, List<PlanEvent> events)
        {
            var profile = _fidelity.Roll(Commander(state.Plan.Formation), _rng);
            if (profile.ReactionDelaySeconds > 0f)
                events.Add(new ReactionDelayed(state.Plan.Formation, stageIndex, profile.ReactionDelaySeconds, profile.Tier));
            return profile;
        }

        private bool AnyStageEvaluableFrom(FormationExecutionState state, int startIndex, IBattlefieldSnapshot snapshot)
        {
            for (var i = startIndex; i < state.Plan.Stages.Count; i++)
            {
                if (DirectiveEvaluable(state.Plan.Stages[i].Do, state, snapshot, out _))
                    return true;
            }
            return false;
        }

        private void Activate(FormationExecutionState state, int stageIndex, Stage stage, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            // Resolve the directive against the carried fidelity BEFORE adopting
            // the stage; BeginStage takes the finished directive and clears the
            // per-stage scratch (waypoint, steering target, pending reaction).
            var directive = ApplyPositionError(ResolveDirective(state.Plan.Formation, stage.Do), state.ActiveFidelity);
            state.BeginStage(stageIndex, snapshot.TimeSeconds, directive);

            events.Add(new StageActivated(state.Plan.Formation, stageIndex, stage, state.ActiveDirective));
            foreach (var signal in stage.Emit)
            {
                _pendingSignals.Add(signal);
                events.Add(new SignalEmitted(state.Plan.Formation, signal));
            }
        }

        private bool TriggerFires(Stage stage, FormationExecutionState state, IBattlefieldSnapshot snapshot)
        {
            // First stage with no trigger defaults to "on battle start" (A3.3).
            if (stage.When.Count == 0)
                return state.ActiveStageIndex < 0;

            return AllConditionsHold(stage.When, state, snapshot, readOnly: false);
        }

        /// <summary>
        /// AND of up to 3 atomic conditions (A3.5). No short-circuit:
        /// stateful conditions (EnemyCommits) must observe every tick.
        /// readOnly is for out-of-band evaluation (the resume scan): it must
        /// never touch tracker state — seeding the sustain tracker outside
        /// continuous observation lets EnemyCommits fire later without its
        /// sustain window.
        /// </summary>
        private bool AllConditionsHold(List<TriggerSpec> conditions, FormationExecutionState state, IBattlefieldSnapshot snapshot, bool readOnly)
        {
            var allHold = true;
            foreach (var condition in conditions)
            {
                if (!ConditionHolds(condition, state, snapshot, readOnly))
                    allHold = false;
            }

            return allHold;
        }

        private bool ConditionHolds(TriggerSpec condition, FormationExecutionState state, IBattlefieldSnapshot snapshot, bool readOnly = false)
        {
            switch (condition.Type)
            {
                case TriggerType.BattleStart:
                    return true; // Only evaluated once Started.

                case TriggerType.TimerElapsed:
                {
                    var baseline = state.ActiveStageIndex < 0 ? _battleStartSeconds : state.ActivatedAtSeconds;
                    return condition.Seconds is { } seconds && snapshot.TimeSeconds - baseline >= seconds;
                }

                case TriggerType.PositionReached:
                {
                    var own = snapshot.GetOwn(state.Plan.Formation);
                    if (own is not { Exists: true })
                        return false;
                    var anchor = Anchors.Resolve(state.Plan.Formation, condition.Anchor);
                    if (anchor == null)
                        return false;
                    var tolerance = condition.ToleranceMeters ?? TriggerDefaults.PositionToleranceMeters;
                    return own.Position.DistanceTo(anchor.Value) <= tolerance;
                }

                case TriggerType.SignalReceived:
                case TriggerType.PlayerSignal:
                    return condition.Signal != null && _signals.Contains(condition.Signal);

                case TriggerType.EnemyCommits:
                    return EnemyCommits(condition, state, snapshot, readOnly);

                case TriggerType.EnemyWithinDistance:
                {
                    if (condition.Meters is not { } meters)
                        return false;
                    // Distance is measured from the named anchor when given
                    // (A6: "enemy within 40 m of the HA retreat anchor"),
                    // else from the own formation.
                    var reference = condition.Anchor != null
                        ? Anchors.Resolve(state.Plan.Formation, condition.Anchor)
                        : (snapshot.GetOwn(state.Plan.Formation) is { Exists: true } own ? own.Position : (MapVec?)null);
                    if (reference is not { } point)
                        return false;
                    var withinClass = FormationSelector.ParseClass(condition.Formation);
                    foreach (var enemy in snapshot.Enemies)
                    {
                        if (withinClass != null && enemy.Class != withinClass)
                            continue;
                        if (point.DistanceTo(enemy.Position) <= meters)
                            return true;
                    }
                    return false;
                }

                case TriggerType.FriendlyWithinDistance:
                {
                    var own = snapshot.GetOwn(state.Plan.Formation);
                    if (own is not { Exists: true } || condition.Meters is not { } meters)
                        return false;
                    var friendly = ResolveFriendlyPosition(condition.Formation, snapshot);
                    return friendly != null && own.Position.DistanceTo(friendly.Value) <= meters;
                }

                case TriggerType.CasualtiesAbove:
                {
                    if (condition.Percent is not { } percent)
                        return false;
                    if (FormationSelector.IsPlayer(condition.Formation))
                    {
                        WarnOnce($"casualties-player", "CasualtiesAbove cannot reference 'Player'; the condition never fires.");
                        return false;
                    }
                    var targetClass = condition.Formation == null
                        ? state.Plan.Formation
                        : FormationSelector.ParseClass(condition.Formation);
                    if (targetClass == null)
                        return false;
                    var target = snapshot.GetOwn(targetClass.Value);
                    // A formation that existed and is now gone is 100% casualties.
                    var casualties = target is { Exists: true } ? target.CasualtiesPercent : 100f;
                    return casualties >= percent;
                }

                case TriggerType.EnemyBroken:
                {
                    // Fires when a matching enemy has been observed routing (and hasn't
                    // recovered) — currently-broken or broke-then-fled. Updated each
                    // tick by PurgeVanishedEnemyState; a melee kill never latches.
                    var selectorClass = FormationSelector.ParseClass(condition.Formation);
                    foreach (var observed in _observedBroken.Values)
                    {
                        if (selectorClass == null || observed == selectorClass)
                            return true;
                    }
                    return false;
                }

                default:
                    WarnOnce(condition.Type.ToString(), $"Trigger type {condition.Type} is not implemented; conditions using it never fire.");
                    return false;
            }
        }

        private bool EnemyCommits(TriggerSpec condition, FormationExecutionState state, IBattlefieldSnapshot snapshot, bool readOnly)
        {
            var (refKey, refPos) = ResolveReference(condition.Formation, state, snapshot);
            if (refPos == null)
                return false;

            var threshold = condition.SpeedThreshold ?? TriggerDefaults.EnemyCommitsSpeedThreshold;
            var sustain = condition.SustainSeconds ?? TriggerDefaults.EnemyCommitsSustainSeconds;
            var maxRange = condition.Meters ?? TriggerDefaults.EnemyCommitsMaxRangeMeters;
            var fires = false;

            foreach (var enemy in snapshot.Enemies)
            {
                if (readOnly)
                {
                    // Peek at sustain state only; the trackers are owned by
                    // the continuous (pending-stage) evaluation path.
                    if (!enemy.IsBroken
                        && refPos.Value.DistanceTo(enemy.Position) <= maxRange
                        && _sustainedSince.TryGetValue((condition, enemy.Id), out var since)
                        && snapshot.TimeSeconds - since >= sustain)
                    {
                        fires = true;
                    }
                    continue;
                }

                var closing = ClosingSpeed(refKey, refPos.Value, enemy, snapshot.TimeSeconds);
                var key = (condition, enemy.Id);
                var inRange = refPos.Value.DistanceTo(enemy.Position) <= maxRange;

                if (inRange && closing is { } speed && speed >= threshold && !enemy.IsBroken)
                {
                    if (!_sustainedSince.ContainsKey(key))
                        _sustainedSince[key] = snapshot.TimeSeconds;
                    if (snapshot.TimeSeconds - _sustainedSince[key] >= sustain)
                        fires = true;
                }
                else
                {
                    _sustainedSince.Remove(key);
                }
            }

            return fires;
        }

        /// <summary>
        /// Closing speed of an enemy toward a reference point, from the
        /// distance change since the previous tick. Null on first sight.
        /// </summary>
        private float? ClosingSpeed(string refKey, MapVec refPos, IEnemyFormationSnapshot enemy, float time)
        {
            var distance = refPos.DistanceTo(enemy.Position);
            var key = (refKey, enemy.Id);

            if (!_approach.TryGetValue(key, out var approach))
            {
                _approach[key] = new ApproachState { LastTime = time, LastDistance = distance, ComputedAt = time };
                return null;
            }

            if (approach.ComputedAt < time)
            {
                var dt = time - approach.LastTime;
                if (dt > 1e-3f)
                    approach.Closing = (approach.LastDistance - distance) / dt;
                approach.LastTime = time;
                approach.LastDistance = distance;
                approach.ComputedAt = time;
            }

            return approach.Closing;
        }

        private (string RefKey, MapVec? Position) ResolveReference(string formationRef, FormationExecutionState state, IBattlefieldSnapshot snapshot)
        {
            if (FormationSelector.IsPlayer(formationRef))
                return ("player", snapshot.PlayerPosition);

            var targetClass = formationRef == null
                ? state.Plan.Formation
                : FormationSelector.ParseClass(formationRef);
            if (targetClass == null)
                return (formationRef, null);

            var formation = snapshot.GetOwn(targetClass.Value);
            return (targetClass.Value.ToString(), formation is { Exists: true } ? formation.Position : null);
        }

        private MapVec? ResolveFriendlyPosition(string formationRef, IBattlefieldSnapshot snapshot)
        {
            if (FormationSelector.IsPlayer(formationRef))
                return snapshot.PlayerPosition;

            var targetClass = FormationSelector.ParseClass(formationRef);
            if (targetClass == null)
                return null;

            var formation = snapshot.GetOwn(targetClass.Value);
            return formation is { Exists: true } ? formation.Position : null;
        }

        /// <summary>
        /// Offsets a directive's resolved destination(s) by the commander's
        /// positional drift (D3): a green formation ends up well off its
        /// anchor, a Master one nearly on it. Steering directives carry no
        /// pre-resolved target (they track per tick), so only Move / Feign /
        /// PullBack destinations drift here.
        /// </summary>
        private static ResolvedDirective ApplyPositionError(ResolvedDirective directive, FidelityProfile fidelity)
        {
            if (fidelity.PositionErrorMeters <= 0f)
                return directive;

            var offset = new MapVec(fidelity.PositionErrorX, fidelity.PositionErrorY);
            var target = directive.Target is { } t ? t + offset : (MapVec?)null;

            List<MapVec> path = null;
            if (directive.Path.Count > 0)
            {
                path = new List<MapVec>(directive.Path.Count);
                foreach (var point in directive.Path)
                    path.Add(point + offset);
            }

            return new ResolvedDirective(directive.Spec, target, path);
        }

        private ResolvedDirective ResolveDirective(PlannedFormationClass formationClass, DirectiveSpec spec)
        {
            if (spec == null)
                return new ResolvedDirective(new DirectiveSpec { Type = DirectiveType.Hold }, null, null);

            MapVec? target = null;
            if (spec.Anchor != null)
                target = Anchors.Resolve(formationClass, spec.Anchor);

            List<MapVec> path = null;
            if (spec.Path is { Count: > 0 })
            {
                path = new List<MapVec>();
                foreach (var waypointId in spec.Path)
                {
                    var waypoint = Anchors.Resolve(formationClass, waypointId);
                    if (waypoint == null)
                    {
                        RbpLog.Warn($"[{formationClass}] waypoint '{waypointId}' could not be resolved; truncating path.");
                        break;
                    }
                    path.Add(waypoint.Value);
                }
            }

            return new ResolvedDirective(spec, target, path);
        }

        /// <summary>
        /// Steering directives (A5: Skirmish, FlankArc, Screen, Follow) track
        /// moving references, so their move goal is recomputed every tick and
        /// re-issued when it shifts noticeably. The geometry lives here, not
        /// in the engine: an order stream the engine merely relays is what
        /// keeps the whole vocabulary scripted-timeline-testable.
        ///
        /// The same single scan also carries B6 mid-stage invalidation: a
        /// null goal while the formation itself is alive means the live
        /// reference is gone (escorted formation wiped, no matching enemy
        /// left), so the stage skips forward.
        /// </summary>
        private void TickSteering(FormationExecutionState state, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            var directive = state.ActiveDirective;
            if (state.ActiveStageIndex < 0 || state.Holding || directive == null)
                return;
            if (!IsSteeringDirective(directive.Spec.Type))
                return;
            if (snapshot.GetOwn(state.Plan.Formation) is not { Exists: true })
                return; // A wiped formation is the abort path's business.

            if (ComputeSteeringTarget(directive.Spec, state, snapshot) is not { } target)
            {
                // A steering reference vanished: the plan adapts, it isn't a
                // rolled reaction — activate the next stage cleanly (Perfect),
                // never with the previous stage's stale drift.
                events.Add(new StageSkipped(state.Plan.Formation, state.ActiveStageIndex, SteeringReferenceGone(directive.Spec)));
                state.CarryFidelity(FidelityProfile.Perfect);
                ActivateChecked(state, state.ActiveStageIndex + 1, snapshot, events);
                return;
            }

            if (state.LastSteeringTarget is { } last &&
                last.DistanceTo(target) < DirectiveDefaults.SteeringUpdateThresholdMeters)
                return;

            state.LastSteeringTarget = target;
            events.Add(new SteeringTargetChanged(state.Plan.Formation, target));
        }

        private static bool IsSteeringDirective(DirectiveType type)
            => type is DirectiveType.Skirmish or DirectiveType.FlankArc or DirectiveType.Screen or DirectiveType.Follow;

        private static string SteeringReferenceGone(DirectiveSpec spec) => spec.Type switch
        {
            DirectiveType.Screen or DirectiveType.Follow => $"'{spec.Target}' is gone",
            _ => spec.Target == null ? "no enemies left" : $"no '{spec.Target}' enemies left",
        };

        private MapVec? ComputeSteeringTarget(DirectiveSpec spec, FormationExecutionState state, IBattlefieldSnapshot snapshot)
        {
            var own = snapshot.GetOwn(state.Plan.Formation);
            if (own is not { Exists: true })
                return null;

            switch (spec.Type)
            {
                case DirectiveType.Skirmish:
                {
                    // Stand at the standoff distance on the line back toward
                    // our current position: gives ground as the enemy closes,
                    // follows as it withdraws.
                    var enemy = NearestEnemy(spec.Target, own.Position, snapshot);
                    if (enemy == null)
                        return null;
                    var standoff = spec.StandoffMeters ?? DirectiveDefaults.SkirmishStandoffMeters;
                    var away = own.Position - enemy.Position;
                    var direction = away.Length > 1e-3f ? away.Normalized() : _attackAxis * -1f;
                    return enemy.Position + direction * standoff;
                }

                case DirectiveType.FlankArc:
                {
                    // Abeam of the target at standoff, on the chosen side of
                    // the battle axis. Missile-only is upheld by construction:
                    // steering never emits a charge transition.
                    var enemy = NearestEnemy(spec.Target, own.Position, snapshot);
                    if (enemy == null)
                        return null;
                    var standoff = spec.StandoffMeters ?? DirectiveDefaults.FlankArcStandoffMeters;
                    var side = spec.Side == FlankSide.Left ? -1f : 1f;
                    return enemy.Position + _attackAxis.Right() * (side * standoff);
                }

                case DirectiveType.Screen:
                {
                    // Stand between the protected formation and the nearest
                    // threat, gap meters out (toward the battle axis when no
                    // enemy is left).
                    if (ResolveFriendlyPosition(spec.Target, snapshot) is not { } guarded)
                        return null;
                    var gap = spec.GapMeters ?? DirectiveDefaults.ScreenGapMeters;
                    var threat = NearestEnemy(null, guarded, snapshot);
                    var toward = threat != null ? threat.Position - guarded : _attackAxis;
                    var direction = toward.Length > 1e-3f ? toward.Normalized() : _attackAxis;
                    return guarded + direction * gap;
                }

                case DirectiveType.Follow:
                {
                    if (ResolveFriendlyPosition(spec.Target, snapshot) is not { } followed)
                        return null;
                    var forward = _attackAxis;
                    var alongForward = spec.OffsetForwardMeters ?? -DirectiveDefaults.FollowDefaultBehindMeters;
                    var alongRight = spec.OffsetRightMeters ?? 0f;
                    return followed + forward * alongForward + forward.Right() * alongRight;
                }

                default:
                    return null;
            }
        }

        private static IEnemyFormationSnapshot NearestEnemy(string selector, MapVec from, IBattlefieldSnapshot snapshot)
        {
            var selectorClass = FormationSelector.ParseClass(selector);
            IEnemyFormationSnapshot nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var enemy in snapshot.Enemies)
            {
                if (selectorClass != null && enemy.Class != selectorClass)
                    continue;
                var distance = from.DistanceTo(enemy.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = enemy;
                }
            }

            return nearest;
        }

        private void TickWaypointProgression(FormationExecutionState state, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            var directive = state.ActiveDirective;
            if (state.ActiveStageIndex < 0 || directive == null)
                return;
            if (directive.Spec.Type != DirectiveType.MoveTo || directive.Path.Count <= 1)
                return;
            if (state.WaypointIndex >= directive.Path.Count - 1)
                return;

            var own = snapshot.GetOwn(state.Plan.Formation);
            if (own is not { Exists: true })
                return;

            if (own.Position.DistanceTo(directive.Path[state.WaypointIndex]) <= TriggerDefaults.PositionToleranceMeters)
            {
                state.WaypointIndex++;
                events.Add(new MoveTargetChanged(state.Plan.Formation, state.WaypointIndex, directive.Path[state.WaypointIndex]));
            }
        }

        private void WarnOnce(string key, string message)
        {
            if (_warnedConditions.Add(key))
                RbpLog.Warn(message);
        }

        private sealed class ApproachState
        {
            public float LastTime;
            public float LastDistance;
            public float ComputedAt;
            public float? Closing;
        }

    }
}
