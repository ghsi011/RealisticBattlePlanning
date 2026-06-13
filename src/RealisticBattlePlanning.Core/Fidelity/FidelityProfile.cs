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
        public FidelityProfile(FidelityTier tier, float reactionDelaySeconds)
        {
            Tier = tier;
            ReactionDelaySeconds = reactionDelaySeconds;
        }

        public FidelityTier Tier { get; }

        /// <summary>Lag between a stage's trigger firing and the commander acting on it (D3).</summary>
        public float ReactionDelaySeconds { get; }

        /// <summary>Master-end execution: no deviation. The pass-through model returns this.</summary>
        public static readonly FidelityProfile Perfect = new(FidelityTier.Master, 0f);
    }
}
