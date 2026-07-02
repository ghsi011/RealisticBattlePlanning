using System.Collections.Generic;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Builds a <see cref="BattleRecord"/> from the same snapshots and events
    /// the Plan Monitor sees — no parallel engine reads, so a recorded run is
    /// exactly what the plan logic acted on. Engine-free: the recorder
    /// MissionBehavior is a thin adapter over this, and Layer-1 tests drive
    /// it with scripted timelines.
    /// </summary>
    public sealed class RunRecorder
    {
        public const float DefaultSampleIntervalSeconds = 2f;

        private readonly BattlePlan _plan;
        private readonly float _sampleInterval;
        private readonly BattleRecord _record;
        private float _battleStartSeconds;
        private float _lastSampleAt;

        public RunRecorder(string scenarioName, BattlePlan plan, float sampleIntervalSeconds = DefaultSampleIntervalSeconds)
        {
            _plan = plan;
            _sampleInterval = sampleIntervalSeconds;
            _record = new BattleRecord { Scenario = scenarioName };
        }

        public bool Started { get; private set; }

        public void Tick(IBattlefieldSnapshot snapshot, IReadOnlyList<PlanEvent> events)
        {
            if (!Started)
            {
                if (!snapshot.BattleStarted)
                    return;

                Started = true;
                _battleStartSeconds = snapshot.TimeSeconds;
                _lastSampleAt = float.MinValue;
                CaptureAnchors(snapshot);
                CaptureMissingFormations(snapshot);
            }

            var time = snapshot.TimeSeconds - _battleStartSeconds;

            foreach (var planEvent in events)
                Append(time, planEvent);

            if (snapshot.TimeSeconds - _lastSampleAt >= _sampleInterval)
            {
                _lastSampleAt = snapshot.TimeSeconds;
                SamplePositions(time, snapshot);
            }

            _record.DurationSeconds = time;
        }

        public BattleRecord Complete(string result)
        {
            _record.Result = result;
            return _record;
        }

        /// <summary>Marks the run invalidated by a plan-logic fault. First fault wins.</summary>
        public void MarkFault(string reason)
        {
            if (_record.Fault == null)
                _record.Fault = reason;
        }

        private void Append(float time, PlanEvent planEvent)
        {
            switch (planEvent)
            {
                case StageActivated stageActivated:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = stageActivated.Formation,
                        Kind = RecordedEventKind.StageActivated,
                        Stage = stageActivated.StageIndex + 1,
                        Name = stageActivated.Stage.Name,
                    });
                    break;

                case SignalEmitted signal:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = signal.Formation,
                        Kind = RecordedEventKind.SignalEmitted,
                        Name = signal.Signal,
                    });
                    break;

                case ReactionDelayed reaction:
                    // The intended fidelity deviation must be visible to the
                    // harness, not just the live log (R2).
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = reaction.Formation,
                        Kind = RecordedEventKind.ReactionDelayed,
                        Stage = reaction.StageIndex + 1,
                        DelaySeconds = reaction.DelaySeconds,
                        Name = reaction.Tier.ToString(),
                    });
                    break;

                case MoveTargetChanged moved:
                    // WaypointIndex is the NEW (0-based) target; the waypoint just
                    // reached is the one before it — 1-based, that's the index
                    // itself. Recording the target's number instead reported
                    // "reached waypoint 2" the moment waypoint 1 was reached.
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = moved.Formation,
                        Kind = RecordedEventKind.WaypointReached,
                        Waypoint = moved.WaypointIndex,
                    });
                    break;

                case PlanSuspended suspended:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = suspended.Formation,
                        Kind = RecordedEventKind.PlanSuspended,
                    });
                    break;

                case PlanResumed resumed:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = resumed.Formation,
                        Kind = RecordedEventKind.PlanResumed,
                        Stage = resumed.StageIndex + 1,
                    });
                    break;

                case PlanAborted aborted:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = aborted.Formation,
                        Kind = RecordedEventKind.PlanAborted,
                        Name = aborted.Reason,
                    });
                    break;

                case StageSkipped skipped:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = skipped.Formation,
                        Kind = RecordedEventKind.StageSkipped,
                        Stage = skipped.StageIndex + 1,
                        Name = skipped.Reason,
                    });
                    break;

                case PlanHolding holding:
                    _record.Events.Add(new RecordedEvent
                    {
                        TimeSeconds = time,
                        Formation = holding.Formation,
                        Kind = RecordedEventKind.PlanHolding,
                        Name = holding.Reason,
                    });
                    break;
            }
        }

        /// <summary>
        /// Same resolution path as the Plan Monitor (ResolvedAnchors over the
        /// battle-start snapshot), so assertions measure against the exact
        /// geometry the plan executed with.
        /// </summary>
        private void CaptureAnchors(IBattlefieldSnapshot snapshot)
        {
            var anchors = ResolvedAnchors.FromSnapshot(_plan.Anchors, snapshot);
            foreach (var formationPlan in _plan.Formations)
            {
                foreach (var anchor in _plan.Anchors)
                {
                    var position = anchors.Resolve(formationPlan.Formation, anchor.Id);
                    if (position is { } resolved)
                    {
                        _record.Anchors.Add(new AnchorPosition
                        {
                            Anchor = anchor.Id,
                            Formation = formationPlan.Formation,
                            X = resolved.X,
                            Y = resolved.Y,
                        });
                    }
                }
            }
        }

        /// <summary>
        /// A planned formation with no units at battle start is a scenario
        /// precondition failure (order-of-battle setup), recorded so the
        /// evaluator can report the cause instead of downstream symptoms.
        /// </summary>
        private void CaptureMissingFormations(IBattlefieldSnapshot snapshot)
        {
            foreach (var formationPlan in _plan.Formations)
            {
                if (formationPlan.Stages.Count == 0)
                    continue;
                if (snapshot.GetOwn(formationPlan.Formation) is not { Exists: true })
                    _record.MissingFormations.Add(formationPlan.Formation);
            }
        }

        private void SamplePositions(float time, IBattlefieldSnapshot snapshot)
        {
            foreach (var formationPlan in _plan.Formations)
            {
                var formation = snapshot.GetOwn(formationPlan.Formation);
                if (formation is { Exists: true })
                {
                    _record.Samples.Add(new PositionSample
                    {
                        TimeSeconds = time,
                        Formation = formationPlan.Formation,
                        X = formation.Position.X,
                        Y = formation.Position.Y,
                    });
                }
            }
        }
    }
}
