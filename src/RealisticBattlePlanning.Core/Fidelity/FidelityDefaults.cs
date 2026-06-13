namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// Default per-tier fidelity magnitudes (spec D3 ships these as a data
    /// table; this is the code-side default until the Area-F config surface
    /// exists). The reaction-delay bands interpolate the D3 endpoints
    /// (Untrained 6–10 s → Master ≤1 s) across the five tiers.
    /// </summary>
    public static class FidelityDefaults
    {
        /// <summary>Inclusive [min, max] reaction-delay band, seconds, per tier (D3).</summary>
        public static (float Min, float Max) ReactionDelaySeconds(FidelityTier tier) => tier switch
        {
            FidelityTier.Untrained => (6f, 10f),
            FidelityTier.Drilled => (4f, 7f),
            FidelityTier.Proficient => (2.5f, 4.5f),
            FidelityTier.Veteran => (1.5f, 2.5f),
            FidelityTier.Master => (0f, 1f),
            _ => (0f, 0f),
        };
    }
}
