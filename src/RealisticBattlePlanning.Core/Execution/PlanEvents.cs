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
