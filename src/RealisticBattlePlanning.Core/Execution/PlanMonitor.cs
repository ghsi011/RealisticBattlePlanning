using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
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
        private readonly List<FormationState> _states;
        private readonly HashSet<string> _signals = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _pendingSignals = new();
        private readonly Dictionary<(string RefKey, int EnemyId), ApproachState> _approach = new();
        private readonly Dictionary<(TriggerSpec Spec, int EnemyId), float> _sustainedSince = new();
        private readonly HashSet<(int Id, PlannedFormationClass? Class)> _initialEnemies = new();
        private readonly HashSet<string> _warnedConditions = new(StringComparer.Ordinal);
        private float _battleStartSeconds;

        public PlanMonitor(BattlePlan plan)
        {
            _plan = plan;
            _states = plan.Formations
                .Where(f => f.Stages.Count > 0)
                .Select(f => new FormationState(f))
                .ToList();
        }

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

        public IReadOnlyList<PlanEvent> Tick(IBattlefieldSnapshot snapshot)
        {
            var events = new List<PlanEvent>();

            if (!Started)
            {
                if (!snapshot.BattleStarted)
                    return events;

                Started = true;
                _battleStartSeconds = snapshot.TimeSeconds;
                Anchors = ResolvedAnchors.FromSnapshot(_plan.Anchors, snapshot);
                foreach (var enemy in snapshot.Enemies)
                    _initialEnemies.Add((enemy.Id, enemy.Class));
            }

            // Signals raised last tick become visible now.
            foreach (var signal in _pendingSignals)
                _signals.Add(signal);
            _pendingSignals.Clear();

            foreach (var state in _states)
            {
                if (!TryAdvance(state, snapshot, events))
                    TickWaypointProgression(state, snapshot, events);
            }

            return events;
        }

        private bool TryAdvance(FormationState state, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            var nextIndex = state.ActiveStageIndex + 1;
            if (nextIndex >= state.Plan.Stages.Count)
                return false;

            var stage = state.Plan.Stages[nextIndex];
            if (!TriggerFires(stage, state, snapshot))
                return false;

            Activate(state, nextIndex, stage, snapshot, events);
            return true;
        }

        private void Activate(FormationState state, int stageIndex, Stage stage, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
        {
            state.ActiveStageIndex = stageIndex;
            state.ActivatedAtSeconds = snapshot.TimeSeconds;
            state.WaypointIndex = 0;
            state.ActiveDirective = ResolveDirective(state.Plan.Formation, stage.Do);

            events.Add(new StageActivated(state.Plan.Formation, stageIndex, stage, state.ActiveDirective));
            foreach (var signal in stage.Emit)
            {
                _pendingSignals.Add(signal);
                events.Add(new SignalEmitted(state.Plan.Formation, signal));
            }
        }

        private bool TriggerFires(Stage stage, FormationState state, IBattlefieldSnapshot snapshot)
        {
            // First stage with no trigger defaults to "on battle start" (A3.3).
            if (stage.When.Count == 0)
                return state.ActiveStageIndex < 0;

            // AND of up to 3 atomic conditions (A3.5). No short-circuit:
            // stateful conditions (EnemyCommits) must observe every tick.
            var allHold = true;
            foreach (var condition in stage.When)
            {
                if (!ConditionHolds(condition, state, snapshot))
                    allHold = false;
            }

            return allHold;
        }

        private bool ConditionHolds(TriggerSpec condition, FormationState state, IBattlefieldSnapshot snapshot)
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
                    return EnemyCommits(condition, state, snapshot);

                case TriggerType.EnemyWithinDistance:
                {
                    var own = snapshot.GetOwn(state.Plan.Formation);
                    if (own is not { Exists: true } || condition.Meters is not { } meters)
                        return false;
                    return MatchingEnemies(condition.Formation, snapshot)
                        .Any(enemy => own.Position.DistanceTo(enemy.Position) <= meters);
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
                    if (MatchingEnemies(condition.Formation, snapshot).Any(enemy => enemy.IsBroken))
                        return true;
                    // A battle-start enemy that has since vanished fled or died.
                    var selectorClass = FormationSelector.ParseClass(condition.Formation);
                    var alive = new HashSet<int>(snapshot.Enemies.Select(e => e.Id));
                    return _initialEnemies.Any(initial =>
                        (selectorClass == null || initial.Class == selectorClass) && !alive.Contains(initial.Id));
                }

                default:
                    WarnOnce(condition.Type.ToString(), $"Trigger type {condition.Type} is not implemented; conditions using it never fire.");
                    return false;
            }
        }

        private bool EnemyCommits(TriggerSpec condition, FormationState state, IBattlefieldSnapshot snapshot)
        {
            var (refKey, refPos) = ResolveReference(condition.Formation, state, snapshot);
            if (refPos == null)
                return false;

            var threshold = condition.SpeedThreshold ?? TriggerDefaults.EnemyCommitsSpeedThreshold;
            var sustain = condition.SustainSeconds ?? TriggerDefaults.EnemyCommitsSustainSeconds;
            var fires = false;

            foreach (var enemy in snapshot.Enemies)
            {
                var closing = ClosingSpeed(refKey, refPos.Value, enemy, snapshot.TimeSeconds);
                var key = (condition, enemy.Id);

                if (closing is { } speed && speed >= threshold && !enemy.IsBroken)
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

        private (string RefKey, MapVec? Position) ResolveReference(string formationRef, FormationState state, IBattlefieldSnapshot snapshot)
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

        private static IEnumerable<IEnemyFormationSnapshot> MatchingEnemies(string selector, IBattlefieldSnapshot snapshot)
        {
            var selectorClass = FormationSelector.ParseClass(selector);
            return selectorClass == null
                ? snapshot.Enemies
                : snapshot.Enemies.Where(enemy => enemy.Class == selectorClass);
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

        private void TickWaypointProgression(FormationState state, IBattlefieldSnapshot snapshot, List<PlanEvent> events)
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

        private sealed class FormationState
        {
            public FormationState(FormationPlan plan)
            {
                Plan = plan;
            }

            public FormationPlan Plan { get; }
            public int ActiveStageIndex { get; set; } = -1;
            public float ActivatedAtSeconds { get; set; }
            public int WaypointIndex { get; set; }
            public ResolvedDirective ActiveDirective { get; set; }
        }
    }
}
