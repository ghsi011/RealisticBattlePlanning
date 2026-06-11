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
    /// I3 trigger subset: BattleStart, TimerElapsed, PositionReached.
    /// Unimplemented trigger types never fire (warned once); the signal bus
    /// arrives in I4.
    /// </summary>
    public sealed class PlanMonitor
    {
        public const float DefaultPositionToleranceMeters = 10f;
        public const float WaypointToleranceMeters = 10f;

        private readonly List<FormationState> _states;
        private readonly BattlePlan _plan;
        private readonly HashSet<TriggerType> _warnedUnsupported = new();
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
            }

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
                events.Add(new SignalEmitted(state.Plan.Formation, signal));
        }

        private bool TriggerFires(Stage stage, FormationState state, IBattlefieldSnapshot snapshot)
        {
            // First stage with no trigger defaults to "on battle start" (A3.3).
            if (stage.When.Count == 0)
                return state.ActiveStageIndex < 0;

            // AND of up to 3 atomic conditions (A3.5).
            return stage.When.All(condition => ConditionHolds(condition, state, snapshot));
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
                    var tolerance = condition.ToleranceMeters ?? DefaultPositionToleranceMeters;
                    return own.Position.DistanceTo(anchor.Value) <= tolerance;
                }

                default:
                    if (_warnedUnsupported.Add(condition.Type))
                        RbpLog.Warn($"Trigger type {condition.Type} is not implemented yet; conditions using it never fire (planned for I4).");
                    return false;
            }
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

            if (own.Position.DistanceTo(directive.Path[state.WaypointIndex]) <= WaypointToleranceMeters)
            {
                state.WaypointIndex++;
                events.Add(new MoveTargetChanged(state.Plan.Formation, state.WaypointIndex, directive.Path[state.WaypointIndex]));
            }
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
