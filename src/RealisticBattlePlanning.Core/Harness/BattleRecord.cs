using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// What one scenario run produced (testing architecture, Layer 2): plan
    /// events, periodic position samples, and the anchor geometry they should
    /// be measured against. All times are battle-relative seconds (0 = the
    /// recorder's first started tick), so tolerance assertions read the same
    /// in-game and in scripted-timeline tests. Written to
    /// Logs\Harness\&lt;scenario&gt;.record.json by the recorder behavior.
    /// </summary>
    public sealed class BattleRecord
    {
        public string Scenario { get; set; }

        /// <summary>Battle-relative time of the last recorded tick.</summary>
        public float DurationSeconds { get; set; }

        /// <summary>Outcome ("PlayerVictory", "PlayerDefeated", ...); null if the battle never resolved.</summary>
        public string Result { get; set; }

        /// <summary>
        /// Set when plan execution faulted mid-run (R2: a crashed run must
        /// never read as a genuine scenario outcome). A faulted run always
        /// fails evaluation, even if every assertion holds on the partial
        /// record.
        /// </summary>
        public string Fault { get; set; }

        /// <summary>
        /// Formations the plan fields that had no units at battle start —
        /// a scenario precondition failure (wrong order-of-battle setup),
        /// reported up front instead of as misleading per-assertion failures
        /// ("stage 2 never activated").
        /// </summary>
        public List<PlannedFormationClass> MissingFormations { get; set; } = new();

        /// <summary>Plan anchors resolved against battle-start geometry, per formation.</summary>
        public List<AnchorPosition> Anchors { get; set; } = new();

        public List<RecordedEvent> Events { get; set; } = new();

        public List<PositionSample> Samples { get; set; } = new();
    }

    public enum RecordedEventKind
    {
        StageActivated,
        SignalEmitted,
        WaypointReached,
        PlanSuspended,
        PlanResumed,
        PlanAborted,
        StageSkipped,
        PlanHolding,
        ReactionDelayed,
    }

    /// <summary>A plan event flattened for the results file.</summary>
    public sealed class RecordedEvent
    {
        public float TimeSeconds { get; set; }

        public PlannedFormationClass Formation { get; set; }

        public RecordedEventKind Kind { get; set; }

        /// <summary>1-based stage number, matching the log lines ("stage 2").</summary>
        public int? Stage { get; set; }

        /// <summary>Stage name or signal name, depending on Kind.</summary>
        public string Name { get; set; }

        /// <summary>1-based waypoint number for WaypointReached.</summary>
        public int? Waypoint { get; set; }

        /// <summary>Reaction-delay seconds for ReactionDelayed (D3).</summary>
        public float? DelaySeconds { get; set; }
    }

    public sealed class PositionSample
    {
        public float TimeSeconds { get; set; }
        public PlannedFormationClass Formation { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    /// <summary>
    /// An anchor resolved to world coordinates for one planned formation
    /// (OwnStart anchors resolve differently per formation).
    /// </summary>
    public sealed class AnchorPosition
    {
        public string Anchor { get; set; }
        public PlannedFormationClass Formation { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }
}
