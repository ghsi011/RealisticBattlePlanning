using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// What the monitor tells the engine (and the log) each tick. The engine
    /// side translates these into orders; tests assert on them directly.
    /// </summary>
    public abstract class PlanEvent
    {
        protected PlanEvent(PlannedFormationClass formation)
        {
            Formation = formation;
        }

        public PlannedFormationClass Formation { get; }

        public abstract string Describe();
    }

    /// <summary>A directive with its anchors resolved to world positions.</summary>
    public sealed class ResolvedDirective
    {
        public ResolvedDirective(DirectiveSpec spec, MapVec? target, IReadOnlyList<MapVec> path)
        {
            Spec = spec;
            Target = target;
            Path = path ?? new List<MapVec>();
        }

        public DirectiveSpec Spec { get; }

        /// <summary>Resolved single-anchor destination, when the directive has one.</summary>
        public MapVec? Target { get; }

        /// <summary>Resolved waypoints, when the directive is a path MoveTo.</summary>
        public IReadOnlyList<MapVec> Path { get; }

        /// <summary>The position to move toward first (path start, else Target).</summary>
        public MapVec? FirstMoveTarget => Path.Count > 0 ? Path[0] : Target;
    }

    public sealed class StageActivated : PlanEvent
    {
        public StageActivated(PlannedFormationClass formation, int stageIndex, Stage stage, ResolvedDirective directive)
            : base(formation)
        {
            StageIndex = stageIndex;
            Stage = stage;
            Directive = directive;
        }

        public int StageIndex { get; }
        public Stage Stage { get; }
        public ResolvedDirective Directive { get; }

        public override string Describe()
        {
            var name = string.IsNullOrEmpty(Stage.Name) ? "" : $" \"{Stage.Name}\"";
            return $"[{Formation}] stage {StageIndex + 1}{name} activated: {Directive.Spec.Type}" +
                   (Directive.FirstMoveTarget != null ? $" -> {Directive.FirstMoveTarget}" : "");
        }
    }

    /// <summary>Path MoveTo progressed to its next waypoint.</summary>
    public sealed class MoveTargetChanged : PlanEvent
    {
        public MoveTargetChanged(PlannedFormationClass formation, int waypointIndex, MapVec target)
            : base(formation)
        {
            WaypointIndex = waypointIndex;
            Target = target;
        }

        public int WaypointIndex { get; }
        public MapVec Target { get; }

        public override string Describe()
            => $"[{Formation}] advancing to waypoint {WaypointIndex + 1} at {Target}";
    }

    /// <summary>
    /// A steering directive (Skirmish, FlankArc, Screen, Follow) shifted its
    /// computed move goal — the battlefield moved, not the plan. The engine
    /// treats it exactly like a move order.
    /// </summary>
    public sealed class SteeringTargetChanged : PlanEvent
    {
        public SteeringTargetChanged(PlannedFormationClass formation, MapVec target)
            : base(formation)
        {
            Target = target;
        }

        public MapVec Target { get; }

        public override string Describe()
            => $"[{Formation}] steering to {Target}";
    }

    /// <summary>A manual player order took the formation off its plan (B5).</summary>
    public sealed class PlanSuspended : PlanEvent
    {
        public PlanSuspended(PlannedFormationClass formation)
            : base(formation)
        {
        }

        public override string Describe() => $"[{Formation}] plan suspended: player override";
    }

    /// <summary>Resume picked a stage and the plan is governing again (B5).</summary>
    public sealed class PlanResumed : PlanEvent
    {
        public PlanResumed(PlannedFormationClass formation, int stageIndex)
            : base(formation)
        {
            StageIndex = stageIndex;
        }

        public int StageIndex { get; }

        public override string Describe() => $"[{Formation}] plan resumed at stage {StageIndex + 1}";
    }

    /// <summary>An abort condition fired; the formation leaves the plan for good (B4).</summary>
    public sealed class PlanAborted : PlanEvent
    {
        public PlanAborted(PlannedFormationClass formation, string reason)
            : base(formation)
        {
            Reason = reason;
        }

        public string Reason { get; }

        public override string Describe() => $"[{Formation}] plan ABORTED: {Reason}";
    }

    /// <summary>A stage's directive was no longer evaluable and was skipped (B6).</summary>
    public sealed class StageSkipped : PlanEvent
    {
        public StageSkipped(PlannedFormationClass formation, int stageIndex, string reason)
            : base(formation)
        {
            StageIndex = stageIndex;
            Reason = reason;
        }

        public int StageIndex { get; }
        public string Reason { get; }

        public override string Describe() => $"[{Formation}] stage {StageIndex + 1} skipped: {Reason}";
    }

    /// <summary>No remaining stage was evaluable; the formation holds (B6).</summary>
    public sealed class PlanHolding : PlanEvent
    {
        public PlanHolding(PlannedFormationClass formation, string reason)
            : base(formation)
        {
            Reason = reason;
        }

        public string Reason { get; }

        public override string Describe() => $"[{Formation}] holding: {Reason}";
    }

    /// <summary>
    /// A stage's trigger fired, but the commander's fidelity adds a reaction
    /// delay before it activates (spec D3). Tagged INTENDED_FIDELITY so the
    /// log distinguishes designed wobble from a genuine fault (R2).
    /// </summary>
    public sealed class ReactionDelayed : PlanEvent
    {
        public ReactionDelayed(PlannedFormationClass formation, int stageIndex, float delaySeconds, Fidelity.FidelityTier tier)
            : base(formation)
        {
            StageIndex = stageIndex;
            DelaySeconds = delaySeconds;
            Tier = tier;
        }

        /// <summary>0-based index of the stage waiting to activate.</summary>
        public int StageIndex { get; }
        public float DelaySeconds { get; }
        public Fidelity.FidelityTier Tier { get; }

        public override string Describe()
            => System.FormattableString.Invariant(
                $"[INTENDED_FIDELITY] [{Formation}] {Tier} commander reacts to stage {StageIndex + 1} after {DelaySeconds:0.#}s");
    }

    public sealed class SignalEmitted : PlanEvent
    {
        public SignalEmitted(PlannedFormationClass formation, string signal)
            : base(formation)
        {
            Signal = signal;
        }

        public string Signal { get; }

        public override string Describe() => $"[{Formation}] emits signal '{Signal}'";
    }
}
