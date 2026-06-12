namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Default trigger/monitor tuning. Spec A4 promises sensible defaults for
    /// every parameter; R3 requires the enemy-commits defaults to be tunable
    /// against vanilla and RBM AI — these become config-driven in Area F.
    /// </summary>
    public static class TriggerDefaults
    {
        /// <summary>PositionReached / waypoint arrival tolerance.</summary>
        public const float PositionToleranceMeters = 10f;

        /// <summary>Minimum closing speed (m/s) that counts as "committing to attack".</summary>
        public const float EnemyCommitsSpeedThreshold = 1.5f;

        /// <summary>How long the approach must be sustained before EnemyCommits fires.</summary>
        public const float EnemyCommitsSustainSeconds = 4f;

        /// <summary>
        /// Approach only counts as "committing" inside this range. Without it
        /// the trigger fires at battle start against any advancing army
        /// (2026-06-12 playtest) — an army marching at 300 m is maneuvering,
        /// a line closing at 100 m is attacking. Override per-trigger via
        /// the Meters parameter.
        /// </summary>
        public const float EnemyCommitsMaxRangeMeters = 150f;

        /// <summary>Fraction of a formation running away that counts as broken.</summary>
        public const float BrokenRunningAwayFraction = 0.5f;
    }
}
