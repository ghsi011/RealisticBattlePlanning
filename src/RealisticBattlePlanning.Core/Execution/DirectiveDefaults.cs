namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Default directive tuning (spec A4/R4: sensible defaults for every
    /// parameter). Like TriggerDefaults, these become config-driven in
    /// Area F.
    /// </summary>
    public static class DirectiveDefaults
    {
        /// <summary>Skirmish: distance kept from the target enemy.</summary>
        public const float SkirmishStandoffMeters = 60f;

        /// <summary>Flank arc: distance kept abeam of the target enemy (A6 uses ~50).</summary>
        public const float FlankArcStandoffMeters = 50f;

        /// <summary>
        /// Flank arc with charge allowed (MissileOnly off): once the formation is
        /// within this of its abeam station it commits to the charge instead of
        /// holding off — so cavalry presses the flank home rather than circling.
        /// </summary>
        public const float FlankChargeRangeMeters = 20f;

        /// <summary>Screen: gap kept between the rear guard and the protected formation.</summary>
        public const float ScreenGapMeters = 30f;

        /// <summary>Follow: escort station behind the followed formation when no offset is given.</summary>
        public const float FollowDefaultBehindMeters = 15f;

        /// <summary>
        /// Steering directives re-issue their move order only when the
        /// desired point shifts by more than this — keeps the order stream
        /// at human cadence instead of 4 Hz jitter.
        /// </summary>
        public const float SteeringUpdateThresholdMeters = 8f;
    }
}
