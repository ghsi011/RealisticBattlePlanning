namespace RealisticBattlePlanning.Planning.Model
{
    /// <summary>Trigger vocabulary, v1 minimum (spec A4).</summary>
    public enum TriggerType
    {
        BattleStart,
        EnemyCommits,
        EnemyWithinDistance,
        FriendlyWithinDistance,
        PositionReached,
        CasualtiesAbove,
        TimerElapsed,
        SignalReceived,
        PlayerSignal,
        EnemyBroken,
    }

    /// <summary>
    /// One atomic trigger condition. Which parameters apply depends on
    /// <see cref="Type"/>; PlanValidator enforces the required ones.
    /// </summary>
    public sealed class TriggerSpec
    {
        public TriggerType Type { get; set; }

        /// <summary>
        /// Formation selector. For friendly references: a PlannedFormationClass
        /// name or "Player" (spec A3.10). For enemy references: "Nearest" or a
        /// class name. Null defaults to own formation / nearest enemy.
        /// </summary>
        public string Formation { get; set; }

        /// <summary>Distance triggers: threshold in meters.</summary>
        public float? Meters { get; set; }

        /// <summary>PositionReached: anchor id and arrival tolerance.</summary>
        public string Anchor { get; set; }
        public float? ToleranceMeters { get; set; }

        /// <summary>CasualtiesAbove: percent of starting strength.</summary>
        public float? Percent { get; set; }

        /// <summary>TimerElapsed: seconds since the previous stage activated.</summary>
        public float? Seconds { get; set; }

        /// <summary>SignalReceived / PlayerSignal: the signal name.</summary>
        public string Signal { get; set; }

        /// <summary>EnemyCommits: sustained-approach parameters (defaults supplied at runtime).</summary>
        public float? SustainSeconds { get; set; }
        public float? SpeedThreshold { get; set; }
    }
}
