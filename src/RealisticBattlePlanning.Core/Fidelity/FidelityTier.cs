namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// The five competence/proficiency tiers (spec D2). Both Command
    /// Competence and per-maneuver Proficiency map onto these; fidelity
    /// effects scale from the Untrained end to the Master end (D3).
    /// </summary>
    public enum FidelityTier
    {
        Untrained,
        Drilled,
        Proficient,
        Veteran,
        Master,
    }
}
