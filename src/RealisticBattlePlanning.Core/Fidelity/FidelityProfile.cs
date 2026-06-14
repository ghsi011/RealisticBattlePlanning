namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// One rolled set of execution-fidelity parameters for a commander
    /// carrying out a stage (spec D3). Phase 1 (P1) populates reaction delay;
    /// positional error, trigger-threshold jitter, discipline-break and
    /// signal-miss chances join in later P-iterations. All magnitudes are
    /// data-driven (FidelityDefaults), so balance is config, not code.
    /// </summary>
    public sealed class FidelityProfile
    {
        public FidelityProfile(
            FidelityTier tier,
            float reactionDelaySeconds,
            float positionErrorMeters = 0f,
            float positionErrorX = 0f,
            float positionErrorY = 0f)
        {
            Tier = tier;
            ReactionDelaySeconds = reactionDelaySeconds;
            PositionErrorMeters = positionErrorMeters;
            PositionErrorX = positionErrorX;
            PositionErrorY = positionErrorY;
        }

        public FidelityTier Tier { get; }

        /// <summary>Lag between a stage's trigger firing and the commander acting on it (D3).</summary>
        public float ReactionDelaySeconds { get; }

        /// <summary>How far the formation drifts off its anchors/paths (D3); the magnitude of the offset.</summary>
        public float PositionErrorMeters { get; }

        /// <summary>The rolled drift offset, in meters (kept as components so Fidelity needn't know MapVec).</summary>
        public float PositionErrorX { get; }
        public float PositionErrorY { get; }

        /// <summary>Master-end execution: no deviation. The pass-through model returns this.</summary>
        public static readonly FidelityProfile Perfect = new(FidelityTier.Master, 0f);
    }
}
