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

        /// <summary>Fraction of a formation running away that counts as broken.</summary>
        public const float BrokenRunningAwayFraction = 0.5f;
    }
}
