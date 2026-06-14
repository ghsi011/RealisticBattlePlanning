namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// Default per-tier fidelity magnitudes (spec D3 ships these as a data
    /// table; this is the code-side default until the Area-F config surface
    /// exists). Hand-authored per-tier bands spanning the D3 endpoints
    /// (reaction delay Untrained 6–10 s → Master ≤1 s; drift 15–25 m → 2–3 m).
    /// </summary>
    public static class FidelityDefaults
    {
        /// <summary>Reaction-delay band [min, max), seconds, per tier (D3); the roll is min + [0,1)·range.</summary>
        public static (float Min, float Max) ReactionDelaySeconds(FidelityTier tier) => tier switch
        {
            FidelityTier.Untrained => (6f, 10f),
            FidelityTier.Drilled => (4f, 7f),
            FidelityTier.Proficient => (2.5f, 4.5f),
            FidelityTier.Veteran => (1.5f, 2.5f),
            FidelityTier.Master => (0f, 1f),
            _ => (0f, 0f),
        };

        /// <summary>Positional-drift band [min, max), meters, per tier (D3: 15–25 m Untrained → 2–3 m Master).</summary>
        public static (float Min, float Max) PositionErrorMeters(FidelityTier tier) => tier switch
        {
            FidelityTier.Untrained => (15f, 25f),
            FidelityTier.Drilled => (10f, 17f),
            FidelityTier.Proficient => (6f, 11f),
            FidelityTier.Veteran => (3f, 6f),
            FidelityTier.Master => (2f, 3f),
            _ => (0f, 0f),
        };
    }
}
